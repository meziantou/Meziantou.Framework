using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Blurs: the pixels of a layer before it is drawn, or the pixels that are already drawn.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> With the pixels read as 16-bit colors (<see cref="ImageFrame.ReadRgba64"/>), <c>a</c> their alpha and
/// <c>p = c a / 65535</c> each of their colors multiplied by it, the blur of the pixel <c>(x, y)</c> is, for the
/// alpha and for each <c>p</c>, <c>sum w(i) w(j) v(x + i, y + j)</c> over the weights of <see cref="GaussianKernel"/>:
/// the rows first, then the columns of the result, in double precision, the intermediate values being kept in single
/// precision. The pixels beyond those that can be read are taken to be the nearest ones that can: beyond the edges of
/// the canvas, the drawing continues as on its edges. The result is the alpha <c>floor(A + 1/2 + 2^-20)</c> and the
/// colors <c>floor(65535 P / A + 1/2 + 2^-20)</c>, converted to the pixel format like any brush color, and it replaces
/// the pixel: entirely, or in proportion to the coverage of the clip (<see cref="CompositeMode.Source"/>). A pixel
/// whose blur reads transparent pixels only is not written.
/// </para>
/// <para>
/// <b>Linear light.</b> When the paints are combined in linear light (<see cref="DrawingCanvas.Compositing(CompositingSpace, DrawingAction)"/>),
/// what is blurred is light: with <c>l</c> the light the 16-bit color <c>c</c> decodes to (the function of the
/// transfer characteristics of the image, or the tone curve of its channel in the ICC profile), <c>p = l a</c>, and
/// the colors of the result are <c>floor(65535 encode(P / A) + 1/2 + 2^-20)</c>. The alpha is blurred as it is.
/// </para>
/// <para>
/// <b>Method.</b> The pixels are blurred in place, from the first row to the last one. The blurred rows a row needs
/// (as many as the blur has weights) are kept in a ring: each row of the pixels is read once, before the rows that are
/// written reach it. The working storage is rented before a pixel changes, from the allocation scope of the image,
/// and kept until the session ends.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private const double BlurRoundingOffset = 0.5 + (1.0 / 1048576);

    private PooledBuffer? _blurWeights;
    private long _blurWeightCapacity;
    private PooledBuffer? _blurRing;
    private long _blurRingCapacity;
    private PooledBuffer? _blurScratch;
    private PooledBuffer? _blurPixels;
    private PooledBuffer? _blurColors;
    private PooledBuffer? _blurCoverage;
    private PooledBuffer? _blurRows;
    private long _blurRowCapacity;

    /// <summary>Blurs the pixels that are already drawn, where the clip lets a drawing through.</summary>
    /// <param name="sigma">The standard deviation of the blur.</param>
    /// <exception cref="ArgumentException">The standard deviation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The standard deviation is negative or too large.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage exceeds the allocation limit. No pixel is changed.</exception>
    public void BlurBackdrop(double sigma)
    {
        GaussianKernel.Validate(sigma, nameof(sigma));
        _cancellationToken.ThrowIfCancellationRequested();
        sigma = ToCanvasBlur(sigma);
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        if (!(sigma > 0) || right <= left || bottom <= top)
            return;

        // The pixels that hold a drawing: those of the canvas, or those a layer can draw on
        var bounds = new Rectangle(0, 0, Width, Height);
        if (_layerDepth > 0)
        {
            var level = _layers[_layerDepth - 1];
            bounds = Rectangle.FromLTRB(level.Left, level.Top, level.Right, level.Bottom);
        }

        var area = Rectangle.FromLTRB(Math.Max(left, bounds.Left), Math.Max(top, bounds.Top), Math.Min(right, bounds.Right), Math.Min(bottom, bounds.Bottom));
        if (area.Width <= 0 || area.Height <= 0)
            return;

        ReserveBlur(sigma, bounds, area);
        if (!_thumbnailRemoved)
        {
            // The pixels are about to change: the EXIF thumbnail no longer shows them
            _image.Metadata.RemoveStaleThumbnail();
            _thumbnailRemoved = true;
        }

        MarkDrawn(area.Left, area.Top, area.Right, area.Bottom);
        for (var i = 0; i < _frames.Length; i++)
        {
            BlurPixels(_frames[i], _storages[i], sigma, bounds, area, applyClip: true);
        }
    }

    /// <summary>Blurs the pixels of a layer the clip it started under can show. The pixels of the layer are still held by the session.</summary>
    private void BlurLayer(LayerLevel level)
    {
        var bounds = Rectangle.FromLTRB(level.Left, level.Top, level.Right, level.Bottom);
        if (level.DirtyRight <= level.DirtyLeft)
            return;

        // The blur of a pixel too far from every pixel that may not be transparent reads transparent pixels only: it
        // stays as it is. The blur reads the pixels beyond the bounds as those on their edges.
        var radius = GaussianKernel.GetRadius(level.BlurSigma);
        var (reachLeft, reachRight) = GetReach(level.DirtyLeft, level.DirtyRight, bounds.Left, bounds.Right, radius, radius);
        var (reachTop, reachBottom) = GetReach(level.DirtyTop, level.DirtyBottom, bounds.Top, bounds.Bottom, radius, radius);
        var area = Rectangle.FromLTRB(
            (int)Math.Max(level.VisibleLeft, reachLeft),
            (int)Math.Max(level.VisibleTop, reachTop),
            (int)Math.Min(level.VisibleRight, reachRight),
            (int)Math.Min(level.VisibleBottom, reachBottom));
        if (area.Width <= 0 || area.Height <= 0)
            return;

        ReserveBlur(level.BlurSigma, bounds, area);
        level.MarkDirty(area.Left, area.Top, area.Right, area.Bottom);
        BlurPixels(level.Frames![0], level.Storages![0], level.BlurSigma, bounds, area, applyClip: false);
    }

    /// <summary>Rents the working storage of a blur: every rental comes before the first pixel that changes.</summary>
    private void ReserveBlur(double sigma, Rectangle bounds, Rectangle area)
    {
        var scope = _image.Owner.Scope;
        var taps = (2L * GaussianKernel.GetRadius(sigma)) + 1;
        if (taps > _blurWeightCapacity)
        {
            _blurWeights?.Dispose();
            _blurWeights = null;
            _blurWeightCapacity = 0;
            _blurWeights = ResampleWeights.Rent<double>(scope, taps);
            _blurWeightCapacity = taps;
        }

        // The blurred rows a row of the result needs: no more than the pixels have
        var rows = Math.Min(taps, bounds.Height);
        var ring = rows * area.Width * 4;
        if (ring > _blurRingCapacity)
        {
            _blurRing?.Dispose();
            _blurRing = null;
            _blurRingCapacity = 0;
            _blurRing = ResampleWeights.Rent<float>(scope, ring);
            _blurRingCapacity = ring;
        }

        if (rows > _blurRowCapacity)
        {
            _blurRows?.Dispose();
            _blurRows = null;
            _blurRowCapacity = 0;
            _blurRows = ResampleWeights.Rent<int>(scope, rows);
            _blurRowCapacity = rows;
        }

        _blurScratch ??= ResampleWeights.Rent<double>(scope, 8L * Width);
        _blurPixels ??= ResampleWeights.Rent<Rgba64>(scope, 2L * Width);
        _blurColors ??= ResampleWeights.Rent<ushort>(scope, 4L * Width);
        EnsureFullCoverageRow();
        if (_clipDepth != 0)
        {
            EnsureCoverageRow();
        }
    }

    /// <summary>Rents the row of full coverages that pixels are replaced with, when the session has none yet.</summary>
    private void EnsureFullCoverageRow()
    {
        if (_blurCoverage is null)
        {
            _blurCoverage = ResampleWeights.Rent<uint>(_image.Owner.Scope, Width);
            var coverage = unsafe(MemoryMarshal.Cast<byte, uint>(_blurCoverage.Span))[..Width];
            coverage.Fill(CoverageRasterizer.FullCoverage);
        }
    }

    /// <summary>Blurs pixels of a frame in place.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="storage">Its storage, held by the session.</param>
    /// <param name="sigma">The standard deviation of the blur.</param>
    /// <param name="bounds">The pixels that can be read: those beyond them are the nearest of them.</param>
    /// <param name="area">The pixels that are blurred, inside the bounds.</param>
    /// <param name="applyClip">Whether the pixels are replaced in proportion to the coverage of the clip.</param>
    private void BlurPixels(ImageFrame frame, PixelStorage storage, double sigma, Rectangle bounds, Rectangle area, bool applyClip)
    {
        var radius = GaussianKernel.GetRadius(sigma);
        var taps = (2 * radius) + 1;
        var width = area.Width;
        var weights = unsafe(MemoryMarshal.Cast<byte, double>(_blurWeights!.Span))[..taps];
        GaussianKernel.Fill(sigma, weights);
        var ringRows = Math.Min(taps, bounds.Height);
        var ring = unsafe(MemoryMarshal.Cast<byte, float>(_blurRing!.Span));
        var ringIndexes = unsafe(MemoryMarshal.Cast<byte, int>(_blurRows!.Span))[..ringRows];
        ringIndexes.Fill(int.MinValue);
        var scratch = unsafe(MemoryMarshal.Cast<byte, double>(_blurScratch!.Span));
        var premultiplied = scratch[..(4 * bounds.Width)];
        var sums = scratch.Slice(4 * Width, 4 * width);
        var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_blurPixels!.Span));
        var source = pixels[..bounds.Width];
        var samples = pixels.Slice(Width, width);
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_blurColors!.Span))[..(4 * width)];
        ReadOnlySpan<uint> full = unsafe(MemoryMarshal.Cast<byte, uint>(_blurCoverage!.Span))[..width];
        var firstColumn = area.Left - radius - bounds.Left;

        // In linear light, the light of every 16-bit sample of each channel; none on the stored values
        var transfer = _transfer;
        var curves = _curves;
        var isLinear = transfer != 0 || curves is not null;
        ReadOnlySpan<double> decodeRed = default;
        ReadOnlySpan<double> decodeGreen = default;
        ReadOnlySpan<double> decodeBlue = default;
        if (curves is not null)
        {
            decodeRed = curves.GetDecode16(0);
            decodeGreen = curves.GetDecode16(1);
            decodeBlue = curves.GetDecode16(2);
        }
        else if (isLinear)
        {
            decodeRed = decodeGreen = decodeBlue = CicpTransfer.GetDecode16(transfer);
        }

        for (var y = area.Top; y < area.Bottom; y++)
        {
            if ((y & 15) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            sums.Clear();
            var any = false;
            for (var tap = 0; tap < taps; tap++)
            {
                var row = y - radius + tap;
                row = row < bounds.Top ? bounds.Top : row >= bounds.Bottom ? bounds.Bottom - 1 : row;

                // The row of the pixels blurred along itself: read when a row of the result first needs it, which is
                // before any row that was written reaches it
                var slot = (row - bounds.Top) % ringRows;
                var blurred = ring.Slice(slot * width * 4, width * 4);
                if (ringIndexes[slot] != row && ringIndexes[slot] != ~row)
                {
                    frame.ReadRgba64(storage, row, bounds.Left, source);
                    ringIndexes[slot] = BlurRow(source, premultiplied, weights, firstColumn, blurred, decodeRed, decodeGreen, decodeBlue) ? row : ~row;
                }

                // A row without any opacity adds nothing
                if (ringIndexes[slot] != row)
                    continue;

                any = true;
                BlurKernels.Accumulate(sums, weights[tap], blurred);
            }

            if (!any)
                continue;

            for (var x = 0; x < width; x++)
            {
                var sum = sums.Slice(x * 4, 4);
                var alpha = RoundBlur(sum[3]);
                if (alpha == 0)
                {
                    samples[x] = default;
                }
                else if (isLinear)
                {
                    samples[x] = new Rgba64(
                        RoundBlur(EncodeLight(transfer, curves, 0, sum[0] / sum[3]) * ushort.MaxValue),
                        RoundBlur(EncodeLight(transfer, curves, 1, sum[1] / sum[3]) * ushort.MaxValue),
                        RoundBlur(EncodeLight(transfer, curves, 2, sum[2] / sum[3]) * ushort.MaxValue),
                        alpha);
                }
                else
                {
                    samples[x] = new Rgba64(RoundBlur(sum[0] * ushort.MaxValue / sum[3]), RoundBlur(sum[1] * ushort.MaxValue / sum[3]), RoundBlur(sum[2] * ushort.MaxValue / sum[3]), alpha);
                }
            }

            frame.ConvertDrawingColors(samples, colors);
            var column = area.Left;
            var run = full;
            ReadOnlySpan<ushort> paint = colors;
            if (applyClip && _clipDepth != 0)
            {
                if (!ApplyClip(y, ref column, ref run, out var skipped))
                    continue;

                paint = paint.Slice(skipped * 4, run.Length * 4);
            }

            frame.CompositePixels(storage, y, column, run, new DrawingSource(paint, CompositeMode.Source));
        }
    }

    /// <summary>Blurs a row of pixels along itself: the alpha and the colors multiplied by it, for each pixel of the area.</summary>
    /// <param name="source">The pixels of the row that can be read.</param>
    /// <param name="premultiplied">Working storage: four values per pixel of the row.</param>
    /// <param name="weights">The weights of the blur.</param>
    /// <param name="firstColumn">The pixel of the row the first weight of the first pixel of the area applies to.</param>
    /// <param name="blurred">The result: four values per pixel of the area.</param>
    /// <param name="decodeRed">The light of every 16-bit red sample when the blur adds light, or an empty span when it adds the stored values.</param>
    /// <param name="decodeGreen">The light of every 16-bit green sample.</param>
    /// <param name="decodeBlue">The light of every 16-bit blue sample.</param>
    /// <returns><see langword="false"/> when every pixel of the row is transparent: the result is not written.</returns>
    private static bool BlurRow(ReadOnlySpan<Rgba64> source, Span<double> premultiplied, ReadOnlySpan<double> weights, int firstColumn, Span<float> blurred, ReadOnlySpan<double> decodeRed, ReadOnlySpan<double> decodeGreen, ReadOnlySpan<double> decodeBlue)
    {
        var isLinear = !decodeRed.IsEmpty;
        var any = false;
        for (var i = 0; i < source.Length; i++)
        {
            var pixel = source[i];
            var entry = premultiplied.Slice(i * 4, 4);
            double alpha = pixel.A;
            any |= pixel.A != 0;
            if (isLinear)
            {
                entry[0] = decodeRed[pixel.R] * alpha;
                entry[1] = decodeGreen[pixel.G] * alpha;
                entry[2] = decodeBlue[pixel.B] * alpha;
            }
            else
            {
                entry[0] = pixel.R * alpha / ushort.MaxValue;
                entry[1] = pixel.G * alpha / ushort.MaxValue;
                entry[2] = pixel.B * alpha / ushort.MaxValue;
            }

            entry[3] = alpha;
        }

        if (!any)
            return false;

        BlurKernels.BlurRow(premultiplied[..(source.Length * 4)], weights, firstColumn, blurred);
        return true;
    }

    /// <summary>Encodes light to a fraction of the largest sample, with the function of the image or the tone curve of a channel of its profile.</summary>
    private static double EncodeLight(byte transfer, LinearLightCurves? curves, int channel, double light)
        => curves is null ? CicpTransfer.Encode(transfer, light) : curves.Encode(channel, light);

    // Written so that a value that is not a number is zero
    private static ushort RoundBlur(double value)
    {
        var rounded = Math.Floor(value + BlurRoundingOffset);
        return rounded >= ushort.MaxValue ? ushort.MaxValue : rounded > 0 ? (ushort)rounded : (ushort)0;
    }

    private void DisposeBlur()
    {
        _blurWeights?.Dispose();
        _blurRing?.Dispose();
        _blurScratch?.Dispose();
        _blurPixels?.Dispose();
        _blurColors?.Dispose();
        _blurCoverage?.Dispose();
        _blurRows?.Dispose();
        _blurWeights = _blurRing = _blurScratch = _blurPixels = _blurColors = _blurCoverage = _blurRows = null;
        _blurWeightCapacity = 0;
        _blurRingCapacity = 0;
        _blurRowCapacity = 0;
    }
}
