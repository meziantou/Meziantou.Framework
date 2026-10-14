using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Color filters: a matrix applied to the pixels of a layer before it is drawn, or to the pixels that are already drawn.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> Each pixel is replaced by the color <see cref="ColorMatrixKernel"/> gives it, from its stored
/// samples at their precision: entirely where the clip covers it entirely (its samples are then those of the result,
/// the color of a pixel without alpha included), or else in proportion to the coverage of the clip, as a paint
/// drawn with <see cref="CompositeMode.Source"/>. The matrix of a layer is applied to the pixels of the layer (8-bit
/// or 16-bit colors with alpha), on the pixels the clip the layer started under can show, before the layer is drawn.
/// </para>
/// <para>The working storage is one row of colors, rented before a pixel changes and kept until the session ends.</para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private PooledBuffer? _filterColors;

    /// <summary>Starts a layer whose pixels go through a color matrix when it ends, before it is drawn.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the layer or the working storage exceeds the allocation limit.</exception>
    public void PushFilter(ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        _cancellationToken.ThrowIfCancellationRequested();
        ReserveFilter();
        var level = PushLayerCore(margin: 0);
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.SourceOver;
        level.Blend = BlendMode.Normal;
        level.Effect = matrix.IsIdentity ? LayerEffect.None : LayerEffect.ColorMatrix;
        level.IsLuminanceMask = false;
        level.Matrix = matrix;
    }

    /// <summary>Applies a color matrix to the pixels that are already drawn, where the clip lets a drawing through.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage exceeds the allocation limit. No pixel is changed.</exception>
    public void FilterBackdrop(ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        _cancellationToken.ThrowIfCancellationRequested();
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        if (matrix.IsIdentity || right <= left || bottom <= top)
            return;

        // The pixels that hold a drawing: those of the canvas, or those a layer can draw on
        if (_layerDepth > 0)
        {
            var level = _layers[_layerDepth - 1];
            left = Math.Max(left, level.Left);
            top = Math.Max(top, level.Top);
            right = Math.Min(right, level.Right);
            bottom = Math.Min(bottom, level.Bottom);
            if (right <= left || bottom <= top)
                return;
        }

        ReserveFilter();
        if (!_thumbnailRemoved)
        {
            // The pixels are about to change: the EXIF thumbnail no longer shows them
            _image.Metadata.RemoveStaleThumbnail();
            _thumbnailRemoved = true;
        }

        var area = Rectangle.FromLTRB(left, top, right, bottom);
        for (var i = 0; i < _frames.Length; i++)
        {
            FilterPixels(_frames[i], _storages[i], matrix, area, applyClip: true);
        }
    }

    /// <summary>Applies the matrix of a layer to the pixels the clip it started under can show. The pixels of the layer are still held by the session.</summary>
    private void FilterLayer(LayerLevel level)
    {
        var area = Rectangle.FromLTRB(level.VisibleLeft, level.VisibleTop, level.VisibleRight, level.VisibleBottom);
        if (area.Width <= 0 || area.Height <= 0)
            return;

        FilterPixels(level.Frames![0], level.Storages![0], level.Matrix!, area, applyClip: false);
    }

    /// <summary>Rents the working storage of a color matrix: every rental comes before the first pixel that changes.</summary>
    private void ReserveFilter()
    {
        _filterColors ??= ResampleWeights.Rent<ushort>(_image.Owner.Scope, 4L * Width);
        EnsureFullCoverageRow();
        if (_clipDepth != 0)
        {
            EnsureCoverageRow();
        }
    }

    /// <summary>Applies a color matrix to pixels of a frame, in place.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="storage">Its storage, held by the session.</param>
    /// <param name="matrix">The matrix.</param>
    /// <param name="area">The pixels that change.</param>
    /// <param name="applyClip">Whether the pixels are replaced in proportion to the coverage of the clip.</param>
    private void FilterPixels(ImageFrame frame, PixelStorage storage, ColorMatrix matrix, Rectangle area, bool applyClip)
    {
        var width = area.Width;
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(_filterColors!.Span))[..(4 * width)];
        ReadOnlySpan<uint> full = unsafe(MemoryMarshal.Cast<byte, uint>(_blurCoverage!.Span))[..width];
        for (var y = area.Top; y < area.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var column = area.Left;
            var run = full;
            if (applyClip && _clipDepth != 0 && !ApplyClip(y, ref column, ref run, out _))
                continue;

            // The colors of the pixels the clip keeps: the others are not read
            var paint = colors[..(run.Length * 4)];
            frame.ApplyColorMatrix(storage, y, column, matrix, paint);

            // The pixels the clip covers entirely get their color as it is, and the others a part of it
            var start = 0;
            while (start < run.Length)
            {
                var isFull = run[start] == CoverageRasterizer.FullCoverage;
                var end = start + 1;
                while (end < run.Length && (run[end] == CoverageRasterizer.FullCoverage) == isFull)
                {
                    end++;
                }

                var part = paint.Slice(start * 4, (end - start) * 4);
                if (isFull)
                {
                    frame.WriteColorMatrixResult(storage, y, column + start, part);
                }
                else
                {
                    frame.CompositePixels(storage, y, column + start, run[start..end], new DrawingSource(part, CompositeMode.Source));
                }

                start = end;
            }
        }
    }

    private void DisposeFilters()
    {
        _filterColors?.Dispose();
        _filterColors = null;
    }
}
