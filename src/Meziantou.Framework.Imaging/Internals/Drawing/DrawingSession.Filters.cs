using System.Diagnostics;
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
/// <para>
/// The working storage is one row of colors, rented before a pixel changes and kept until the session ends. Without a clip,
/// the rows of an area of at least <see cref="RowBands.MinPixelsForParallelism"/> pixels are split between up to
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers (<see cref="RowBands"/>), each with a row of colors of
/// its own: the rows do not depend on one another, so the pixels are those of one worker.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private PooledBuffer? _filterColors;

    // The rows of colors of the other workers of a color matrix (the first one has _filterColors); kept until the session ends
    private PooledBuffer[]? _filterWorkerColors;

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
        MarkDrawn(area.Left, area.Top, area.Right, area.Bottom);
        for (var i = 0; i < _frames.Length; i++)
        {
            FilterPixels(_frames[i], _storages[i], matrix, area, applyClip: true);
        }
    }

    /// <summary>Applies the matrix of a layer to the pixels the clip it started under can show. The pixels of the layer are still held by the session.</summary>
    private void FilterLayer(LayerLevel level)
    {
        var left = level.VisibleLeft;
        var top = level.VisibleTop;
        var right = level.VisibleRight;
        var bottom = level.VisibleBottom;
        if (ColorMatrixKernel.KeepsTransparentBlack(level.Matrix!, level.Storages![0].BytesPerPixel == 8 ? ushort.MaxValue : byte.MaxValue))
        {
            // The pixels that are transparent black stay so: only the others can change. The layer may have drawn
            // nothing, or only outside the pixels the clip shows.
            left = Math.Max(left, level.DirtyLeft);
            top = Math.Max(top, level.DirtyTop);
            right = Math.Min(right, level.DirtyRight);
            bottom = Math.Min(bottom, level.DirtyBottom);
        }

        if (right <= left || bottom <= top)
            return;

        var area = Rectangle.FromLTRB(left, top, right, bottom);
        level.MarkDirty(area.Left, area.Top, area.Right, area.Bottom);
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
        // Each row is computed alone: without a clip (whose coverage row is shared), the rows of a large area are split
        // between workers, each with a row of colors of its own, rented before any pixel changes
        var clipped = applyClip && _clipDepth != 0;
        var workers = clipped ? 1 : RowBands.GetWorkerCount(area.Size, _image.Configuration.MaxDegreeOfParallelism);
        if (workers is 1)
        {
            FilterRows(frame, storage, matrix, area, area.Top, area.Bottom, _filterColors!, clipped);
            return;
        }

        if (_filterWorkerColors is null || _filterWorkerColors.Length < workers - 1)
        {
            var buffers = new PooledBuffer[workers - 1];
            _filterWorkerColors?.CopyTo(buffers, 0);
            var firstRented = _filterWorkerColors?.Length ?? 0;
            var rented = firstRented;
            try
            {
                for (; rented < buffers.Length; rented++)
                {
                    buffers[rented] = ResampleWeights.Rent<ushort>(_image.Owner.Scope, 4L * Width);
                }
            }
            catch
            {
                // A rental that fails releases the buffers rented before it: the buffers of the session stay as they were
                for (var i = firstRented; i < rented; i++)
                {
                    buffers[i].Dispose();
                }

                throw;
            }

            _filterWorkerColors = buffers;
        }

        var workerColors = _filterWorkerColors;
        RowBands.RunParallelInBandOrder(workers, band => FilterRows(
            frame,
            storage,
            matrix,
            area,
            area.Top + RowBands.GetBandStart(area.Height, band, workers),
            area.Top + RowBands.GetBandStart(area.Height, band + 1, workers),
            band is 0 ? _filterColors! : workerColors[band - 1],
            clipped: false));
    }

    /// <summary>Applies a color matrix to rows <paramref name="top"/> to <paramref name="bottom"/> - 1 of an area of a frame, in place.</summary>
    private void FilterRows(ImageFrame frame, PixelStorage storage, ColorMatrix matrix, Rectangle area, int top, int bottom, PooledBuffer colorRow, bool clipped)
    {
        var width = area.Width;
        var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(colorRow.Span))[..(4 * width)];
        ReadOnlySpan<uint> full = unsafe(MemoryMarshal.Cast<byte, uint>(_blurCoverage!.Span))[..width];
        for (var y = top; y < bottom; y++)
        {
            if ((y & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var column = area.Left;
            var run = full;
            if (clipped && !ApplyClip(y, ref column, ref run, out _))
                continue;

            // A floating-point frame gets the colors of the matrix in place, unclipped
            if (PixelFormats.IsFloatingPoint(frame.PixelFormat))
            {
                switch (frame.PixelFormat)
                {
                    case PixelFormat.RgbaFloat:
                        FloatDrawingKernels.ApplyColorMatrix<RgbaFloat>(storage, y, column, matrix, run);
                        break;
                    case PixelFormat.RgbaHalf:
                        FloatDrawingKernels.ApplyColorMatrix<RgbaHalf>(storage, y, column, matrix, run);
                        break;
                    case PixelFormat.GrayFloat:
                        FloatDrawingKernels.ApplyColorMatrix<GrayFloat>(storage, y, column, matrix, run);
                        break;
                    case PixelFormat.GrayHalf:
                        FloatDrawingKernels.ApplyColorMatrix<GrayHalf>(storage, y, column, matrix, run);
                        break;
                    case PixelFormat.LaFloat:
                        FloatDrawingKernels.ApplyColorMatrix<LaFloat>(storage, y, column, matrix, run);
                        break;
                    case PixelFormat.LaHalf:
                        FloatDrawingKernels.ApplyColorMatrix<LaHalf>(storage, y, column, matrix, run);
                        break;
                    default:
                        throw new UnreachableException();
                }

                continue;
            }

            // The colors of the pixels the clip keeps: the others are not read
            var paint = colors[..(run.Length * 4)];
            frame.ApplyColorMatrix(storage, y, column, matrix, paint);

            // The pixels the clip covers entirely get their color as it is, and the others a part of it
            var start = 0;
            while (start < run.Length)
            {
                var isFull = run[start] == CoverageRasterizer.FullCoverage;
                var length = isFull ? run[start..].IndexOfAnyExcept(CoverageRasterizer.FullCoverage) : run[start..].IndexOf(CoverageRasterizer.FullCoverage);
                var end = length < 0 ? run.Length : start + length;

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
        if (_filterWorkerColors is not null)
        {
            foreach (var buffer in _filterWorkerColors)
            {
                buffer.Dispose();
            }

            _filterWorkerColors = null;
        }
    }
}
