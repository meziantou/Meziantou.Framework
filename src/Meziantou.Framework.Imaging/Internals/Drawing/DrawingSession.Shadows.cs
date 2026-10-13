using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Shadows: the alpha of a layer, blurred, moved and painted with a color under the layer.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <c>A(x, y)</c> the alpha of the pixels of the layer as fractions of one, the blur of the
/// shadow is <c>B(x, y) = sum w(i) w(j) A(x + i, y + j)</c> over the weights of <see cref="GaussianKernel"/>. The
/// drawing is taken to continue beyond the pixels the layer has as its pixels on the edges do: a shape that goes
/// beyond an edge of the canvas keeps its shadow along that edge. The shadow of the pixel <c>(x, y)</c> is that blur
/// at <c>(x - ox, y - oy)</c>, interpolated between the four pixels around that position when the offset is not
/// whole, and rounded to a coverage, <c>floor(65536 S + 1/2)</c>. The color of the shadow is then composited over the
/// pixels with that coverage, as a shape is: its own alpha makes the shadow of an opaque drawing translucent.
/// </para>
/// <para>
/// <b>Method.</b> The blur and the interpolation are one row of weights and one column of weights
/// (<c>k(m) = (1 - f) w(m) + f w(m + 1)</c>, with <c>f</c> the fractional part of the offset), applied to the rows of
/// the layer, then to the columns of the result, in double precision. The intermediate values are kept in single
/// precision; a row of the layer without any opacity is not read again.
/// </para>
/// <para>
/// <b>Inner shadows.</b> The shadow cast on a drawing by what is around it is computed the same way from
/// <c>1 - A</c> instead of <c>A</c>, what is beyond the layer continuing as on its edges too. Its color is combined
/// with the pixels of the layer itself, with <see cref="Drawing.CompositeMode.SourceAtop"/>: the alpha of the layer
/// does not change, and the shadow only shows where the drawing is. The layer is then drawn as any layer.
/// </para>
/// <para>The working storage is rented before a pixel changes, from the allocation scope of the image, and kept until the session ends.</para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private PooledBuffer? _shadowWeights;
    private long _shadowWeightCapacity;
    private PooledBuffer? _shadowRows;
    private long _shadowRowCapacity;
    private PooledBuffer? _shadowScratch;
    private PooledBuffer? _shadowCoverage;
    private PooledBuffer? _shadowFlags;

    /// <summary>
    /// Paints the shadow of a layer on what is under it, or the shadow of what is around its drawings on the layer
    /// itself. The pixels of the layer are still held by the session.
    /// </summary>
    private void DrawShadow(LayerLevel level, bool isInner)
    {
        var target = isInner ? level.Frames![0] : _frames[0];
        var color = target.ConvertDrawingColor(level.ShadowColor);
        var left = level.VisibleLeft;
        var top = level.VisibleTop;
        var width = level.VisibleRight - left;
        var height = level.VisibleBottom - top;
        if (color.Alpha == 0 || width <= 0 || height <= 0)
            return;

        var layerWidth = level.Right - level.Left;
        var layerHeight = level.Bottom - level.Top;
        var radius = GaussianKernel.GetRadius(level.BlurSigma);
        var taps = (2 * radius) + 2;

        // Every rental comes before the first pixel that changes
        var scope = _image.Owner.Scope;
        var weightCount = (3L * taps) - 1;
        if (weightCount > _shadowWeightCapacity)
        {
            _shadowWeights?.Dispose();
            _shadowWeights = null;
            _shadowWeightCapacity = 0;
            _shadowWeights = ResampleWeights.Rent<double>(scope, weightCount);
            _shadowWeightCapacity = weightCount;
        }

        var rowCount = (long)layerHeight * width;
        if (rowCount > _shadowRowCapacity)
        {
            _shadowRows?.Dispose();
            _shadowRows = null;
            _shadowRowCapacity = 0;
            _shadowRows = ResampleWeights.Rent<float>(scope, rowCount);
            _shadowRowCapacity = rowCount;
        }

        _shadowScratch ??= ResampleWeights.Rent<double>(scope, 2L * Width);
        _shadowCoverage ??= ResampleWeights.Rent<uint>(scope, Width);
        _shadowFlags ??= ResampleWeights.Rent<byte>(scope, Height);

        // The weights of the blur, then those of the blur moved by the fractional part of the offset, for each axis
        var weights = unsafe(MemoryMarshal.Cast<byte, double>(_shadowWeights!.Span));
        var blur = weights[..(taps - 1)];
        var horizontal = weights.Slice(taps - 1, taps);
        var vertical = weights.Slice((2 * taps) - 1, taps);
        GaussianKernel.Fill(level.BlurSigma, blur);
        var wholeX = Math.Floor(level.ShadowOffsetX);
        var wholeY = Math.Floor(level.ShadowOffsetY);
        Shift(blur, level.ShadowOffsetX - wholeX, horizontal);
        Shift(blur, level.ShadowOffsetY - wholeY, vertical);

        // The pixel of the layer the first weight of a pixel of the canvas applies to, from the first one of the layer
        var originX = left - (int)wholeX - radius - 1 - level.Left;
        var originY = top - (int)wholeY - radius - 1 - level.Top;
        var rows = unsafe(MemoryMarshal.Cast<byte, float>(_shadowRows!.Span));
        var scratch = unsafe(MemoryMarshal.Cast<byte, double>(_shadowScratch!.Span));
        var alpha = scratch[..layerWidth];
        var sums = scratch.Slice(Width, width);
        var hasOpacity = _shadowFlags!.Span[..layerHeight];
        var storage = level.Storages![0];
        var isWide = storage.BytesPerPixel == 8;
        for (var row = 0; row < layerHeight; row++)
        {
            if ((row & 15) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            hasOpacity[row] = ReadAlpha(storage.GetRowSpanCore(level.Top + row), isWide, level.Left, alpha, isInner) ? (byte)1 : (byte)0;
            if (hasOpacity[row] == 0)
                continue;

            var output = rows.Slice(row * width, width);
            for (var x = 0; x < width; x++)
            {
                var sum = 0d;
                var first = originX + x;
                for (var tap = 0; tap < taps; tap++)
                {
                    var index = first + tap;
                    sum += horizontal[tap] * alpha[index < 0 ? 0 : index >= layerWidth ? layerWidth - 1 : index];
                }

                output[x] = (float)sum;
            }
        }

        if (!isInner && !_thumbnailRemoved)
        {
            // The pixels are about to change: the EXIF thumbnail no longer shows them
            _image.Metadata.RemoveStaleThumbnail();
            _thumbnailRemoved = true;
        }

        var coverage = unsafe(MemoryMarshal.Cast<byte, uint>(_shadowCoverage!.Span))[..width];
        for (var y = 0; y < height; y++)
        {
            if ((y & 15) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            sums.Clear();
            var any = false;
            for (var tap = 0; tap < taps; tap++)
            {
                var row = originY + y + tap;
                row = row < 0 ? 0 : row >= layerHeight ? layerHeight - 1 : row;
                if (hasOpacity[row] == 0)
                    continue;

                any = true;
                var weight = vertical[tap];
                var source = rows.Slice(row * width, width);
                for (var x = 0; x < width; x++)
                {
                    sums[x] += weight * source[x];
                }
            }

            if (!any)
                continue;

            for (var x = 0; x < width; x++)
            {
                var value = Math.Floor((sums[x] * CoverageRasterizer.FullCoverage) + 0.5);
                coverage[x] = value >= CoverageRasterizer.FullCoverage ? CoverageRasterizer.FullCoverage : value > 0 ? (uint)value : 0;
            }

            if (isInner)
            {
                // On the drawing itself, where it is: the clip applies when the layer is drawn
                level.Frames![0].CompositePixels(storage, top + y, left, coverage, new DrawingSource(color, CompositeMode.SourceAtop, isLinear: _isLinear));
                continue;
            }

            var column = left;
            ReadOnlySpan<uint> run = coverage;
            if (_clipDepth != 0 && !ApplyClip(top + y, ref column, ref run, out _))
                continue;

            var paint = new DrawingSource(color, isLinear: _isLinear);
            for (var i = 0; i < _frames.Length; i++)
            {
                _frames[i].CompositePixels(_storages[i], top + y, column, run, paint);
            }
        }
    }

    /// <summary>Gets the weights of a blur read a fraction of a pixel before each pixel: one more weight than the blur has.</summary>
    private static void Shift(ReadOnlySpan<double> blur, double fraction, Span<double> shifted)
    {
        for (var i = 0; i < shifted.Length; i++)
        {
            shifted[i] = ((1 - fraction) * (i > 0 ? blur[i - 1] : 0)) + (fraction * (i < blur.Length ? blur[i] : 0));
        }
    }

    /// <summary>Reads the alpha of pixels of a row of a layer, as fractions of one, or what is missing for them to be opaque.</summary>
    /// <returns><see langword="false"/> when every value is zero.</returns>
    private static bool ReadAlpha(ReadOnlySpan<byte> row, bool isWide, int left, Span<double> alpha, bool isInverted)
    {
        if (isInverted)
        {
            ReadAlpha(row, isWide, left, alpha, isInverted: false);
            var isOpen = false;
            foreach (ref var value in alpha)
            {
                value = 1 - value;
                isOpen |= value != 0;
            }

            return isOpen;
        }

        var any = false;
        if (isWide)
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba64>(row)).Slice(left, alpha.Length);
            for (var i = 0; i < alpha.Length; i++)
            {
                var value = pixels[i].A;
                any |= value != 0;
                alpha[i] = value / 65535.0;
            }
        }
        else
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba32>(row)).Slice(left, alpha.Length);
            for (var i = 0; i < alpha.Length; i++)
            {
                var value = pixels[i].A;
                any |= value != 0;
                alpha[i] = value / 255.0;
            }
        }

        return any;
    }

    private void DisposeShadows()
    {
        _shadowWeights?.Dispose();
        _shadowRows?.Dispose();
        _shadowScratch?.Dispose();
        _shadowCoverage?.Dispose();
        _shadowFlags?.Dispose();
        _shadowWeights = _shadowRows = _shadowScratch = _shadowCoverage = _shadowFlags = null;
        _shadowWeightCapacity = 0;
        _shadowRowCapacity = 0;
    }
}
