using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixels of a transformed frame (<see cref="WarpPlan"/>): every output pixel center is mapped back to the source,
/// and the source is filtered there with the kernels of the resize, widened where the transformation shrinks it.
/// </summary>
/// <remarks>
/// <para>
/// A filtered pixel is <c>sum(w(i) w(j) s(i, j)) / sum(w(i) w(j))</c> over every integer <c>(i, j)</c> within the reach
/// of the kernels around the point it comes from, with <c>w(i) = K((i - c) / f)</c>. The pixels beyond the edges of the
/// source are the background: their part of the sum is the background times their weight, so that the edges of the
/// image blend into it. A pixel that reads nothing of the source, or whose center comes from no point of the source, is
/// the background. Samples are the working samples of the resize (<see cref="WorkingSamples"/>): premultiplied by alpha,
/// in double precision, rounded once to the storage precision. Weights that are zero are not read, so a pixel whose
/// center comes from a source pixel center is that pixel, and a sample that is not a number spreads only to the pixels
/// that read it.
/// </para>
/// <para>
/// Nearest neighbor copies the source pixel that contains the point, exactly, or the background beyond the edges.
/// </para>
/// </remarks>
internal static class Warper
{
    public static void Warp<TPixel>(scoped in PixelLease source, scoped in PixelLease destination, WarpPlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var sourceStorage = source.Storage;
        var destinationStorage = destination.Storage;
        if (Unsafe.SizeOf<TPixel>() != sourceStorage.BytesPerPixel || Unsafe.SizeOf<TPixel>() != destinationStorage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            Warp<TPixel, ushort>(sourceStorage, destinationStorage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            Warp<TPixel, byte>(sourceStorage, destinationStorage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaFloat))
        {
            Warp<TPixel, float>(sourceStorage, destinationStorage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            Warp<TPixel, Half>(sourceStorage, destinationStorage, plan, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    private static void Warp<TPixel, TSample>(PixelStorage source, PixelStorage destination, WarpPlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        // The background as a pixel of the format, as working samples, and as the pixel those samples store (the pixel of
        // the canvas where nothing of the source is read)
        TPixel background = default;
        var color = plan.Background;

        // A format without alpha only reads a translucent background when the result does not show it (the operation
        // rejects it otherwise): the conversion must not reject it
        if (plan.Alpha == WorkingAlpha.None)
        {
            color = new Rgba64(color.R, color.G, color.B, ushort.MaxValue);
        }

        PixelConverter.ConvertRow(new ReadOnlySpan<Rgba64>(in color), new Span<TPixel>(ref background));
        var backgroundWorking = new double[plan.Channels];
        WorkingSamples.Load(unsafe(MemoryMarshal.Cast<TPixel, TSample>(new ReadOnlySpan<TPixel>(in background))), backgroundWorking, plan.Alpha, plan.Transfer);
        TPixel stored = default;
        WorkingSamples.Store(backgroundWorking, unsafe(MemoryMarshal.Cast<TPixel, TSample>(new Span<TPixel>(ref stored))), plan.Alpha, plan.Transfer, plan.MaxValue);
        var context = new WarpContext<TPixel>(backgroundWorking, stored);

        if (plan.Workers == 1)
        {
            WarpBand<TPixel, TSample>(source, destination, plan, context, 0, cancellationToken);
            return;
        }

        RowBands.RunParallel(plan.Workers, band => WarpBand<TPixel, TSample>(source, destination, plan, context, band, cancellationToken));
    }

    private static void WarpBand<TPixel, TSample>(PixelStorage source, PixelStorage destination, WarpPlan plan, WarpContext<TPixel> context, int band, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        if (plan.Filter == ResamplingFilter.NearestNeighbor)
        {
            Nearest(source, destination, plan, context.StoredBackground, start, end, cancellationToken);
        }
        else
        {
            Filter<TPixel, TSample>(source, destination, plan, context.Background, plan.GetScratch(band), start, end, cancellationToken);
        }
    }

    private static Span<TPixel> GetRow<TPixel>(PixelStorage storage, int y)
        where TPixel : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y)));

    private static void Nearest<TPixel>(PixelStorage source, PixelStorage destination, WarpPlan plan, TPixel background, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var geometry = plan.Geometry;
        var width = geometry.OutputSize.Width;
        var sourceWidth = geometry.SourceSize.Width;
        var sourceHeight = geometry.SourceSize.Height;
        for (var y = start; y < end; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = GetRow<TPixel>(destination, y);
            for (var x = 0; x < width; x++)
            {
                // The source pixel that contains the point (written so that a point too far to be computed is outside)
                if (geometry.TryMapToSource(x, y, out var u, out var v))
                {
                    var sx = Math.Floor(u);
                    var sy = Math.Floor(v);
                    if (sx >= 0 && sx < sourceWidth && sy >= 0 && sy < sourceHeight)
                    {
                        row[x] = GetRow<TPixel>(source, (int)sy)[(int)sx];
                        continue;
                    }
                }

                row[x] = background;
            }
        }
    }

    private static void Filter<TPixel, TSample>(PixelStorage source, PixelStorage destination, WarpPlan plan, double[] background, Span<double> scratch, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var geometry = plan.Geometry;
        var width = geometry.OutputSize.Width;
        var channels = plan.Channels;
        var radius = ResamplingKernels.GetRadius(plan.Filter);

        var outputRow = scratch[..(width * channels)];
        var taps = scratch.Slice(width * channels, plan.TapsX * channels);
        var xWeights = scratch.Slice((width + plan.TapsX) * channels, plan.TapsX);
        var yWeights = scratch.Slice(((width + plan.TapsX) * channels) + plan.TapsX, plan.TapsY);
        var sums = scratch.Slice(((width + plan.TapsX) * channels) + plan.TapsX + plan.TapsY, channels);
        var rowSums = scratch.Slice(((width + plan.TapsX) * channels) + plan.TapsX + plan.TapsY + channels, channels);
        var integerEncoded = plan.Transfer is 0 && !WorkingSamples.IsFloatingPoint<TSample>();
        for (var y = start; y < end; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var output = outputRow.Slice(x * channels, channels);
                if (!geometry.TryMapToSource(x, y, out var u, out var v))
                {
                    background.CopyTo(output);
                    continue;
                }

                geometry.GetFilterScales(x, y, u, v, out var scaleX, out var scaleY);
                if (!GetWeights(plan.Filter, u - 0.5, radius, scaleX, geometry.SourceSize.Width, xWeights, out var firstX, out var countX, out var totalX, out var insideX)
                    || !GetWeights(plan.Filter, v - 0.5, radius, scaleY, geometry.SourceSize.Height, yWeights, out var firstY, out var countY, out var totalY, out var insideY))
                {
                    background.CopyTo(output);
                    continue;
                }

                sums.Clear();
                for (var j = 0; j < countY; j++)
                {
                    var weightY = yWeights[j];
                    if (weightY == 0)
                        continue;

                    var samples = unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(source, firstY + j))).Slice(firstX * channels, countX * channels);
                    rowSums.Clear();
                    if (integerEncoded)
                    {
                        // The working samples of WorkingSamples.Load, computed in place: a call per row of a few pixels
                        // costs more than the sums
                        AddEncodedRow(samples, xWeights[..countX], rowSums, channels, plan.Alpha == WorkingAlpha.Premultiplied);
                        for (var c = 0; c < channels; c++)
                        {
                            sums[c] += weightY * rowSums[c];
                        }

                        continue;
                    }

                    var working = taps[..(countX * channels)];
                    WorkingSamples.Load(samples, working, plan.Alpha, plan.Transfer);
                    for (var i = 0; i < countX; i++)
                    {
                        var weightX = xWeights[i];
                        if (weightX == 0)
                            continue;

                        for (var c = 0; c < channels; c++)
                        {
                            rowSums[c] += weightX * working[(i * channels) + c];
                        }
                    }

                    for (var c = 0; c < channels; c++)
                    {
                        sums[c] += weightY * rowSums[c];
                    }
                }

                // The weights of the pixels beyond the edges read the background; normalized by the sum of all the weights
                var total = totalX * totalY;
                var outside = total - (insideX * insideY);
                for (var c = 0; c < channels; c++)
                {
                    output[c] = (sums[c] + (background[c] * outside)) / total;
                }
            }

            var row = unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(destination, y)));
            WorkingSamples.Store(outputRow, row, plan.Alpha, plan.Transfer, plan.MaxValue);
        }
    }

    /// <summary>
    /// Adds the weighted working samples of the integer samples of one row, as stored (<c>v</c>, or <c>v a</c> for the colors
    /// of premultiplied pixels): the products of <see cref="WorkingSamples.Load"/>, in the same order.
    /// </summary>
    private static void AddEncodedRow<TSample>(ReadOnlySpan<TSample> samples, ReadOnlySpan<double> weights, Span<double> sums, int channels, bool premultiplied)
        where TSample : unmanaged
    {
        for (var i = 0; i < weights.Length; i++)
        {
            var weight = weights[i];
            if (weight == 0)
                continue;

            var pixel = samples.Slice(i * channels, channels);
            if (premultiplied)
            {
                double alpha = WorkingSamples.ToInt(pixel[3]);
                sums[0] += weight * (WorkingSamples.ToInt(pixel[0]) * alpha);
                sums[1] += weight * (WorkingSamples.ToInt(pixel[1]) * alpha);
                sums[2] += weight * (WorkingSamples.ToInt(pixel[2]) * alpha);
                sums[3] += weight * alpha;
                continue;
            }

            for (var c = 0; c < channels; c++)
            {
                sums[c] += weight * WorkingSamples.ToInt(pixel[c]);
            }
        }
    }

    /// <summary>
    /// Computes the weights of the source pixels around <paramref name="center"/> (in pixel indices) on one axis: the sum of
    /// all of them, and those of the pixels inside the source with their sum.
    /// </summary>
    /// <returns><see langword="false"/> when no pixel of the source is read.</returns>
    private static bool GetWeights(ResamplingFilter filter, double center, double radius, double scale, int length, Span<double> weights, out int first, out int count, out double total, out double inside)
    {
        first = 0;
        count = 0;
        total = 0;
        inside = 0;
        if (!WarpGeometry.GetWindow(filter, center, radius * scale, out var low, out var high) || high < 0 || low >= length)
            return false;

        var start = Math.Max(low, 0);
        var stop = Math.Min(high, length - 1);
        first = (int)start;
        count = (int)(stop - start + 1);

        // Every weight is added in increasing order, those inside the source to their own sum too: when every pixel read is
        // inside, both sums are the same number and nothing is read from the background
        for (var i = low; i <= high; i++)
        {
            // Every pixel of the footprint of a box counts the same: rounding must not move its boundaries
            var weight = filter is ResamplingFilter.Box ? 1 : ResamplingKernels.Evaluate(filter, (i - center) / scale);
            total += weight;
            if (i >= start && i <= stop)
            {
                weights[(int)(i - start)] = weight;
                inside += weight;
            }
        }

        return true;
    }

    /// <summary>The background of one frame, shared by the workers (read-only).</summary>
    private sealed class WarpContext<TPixel>(double[] background, TPixel storedBackground)
        where TPixel : unmanaged
    {
        /// <summary>Gets the working samples of the background.</summary>
        public double[] Background { get; } = background;

        /// <summary>Gets the pixel the working samples of the background store.</summary>
        public TPixel StoredBackground { get; } = storedBackground;
    }
}
