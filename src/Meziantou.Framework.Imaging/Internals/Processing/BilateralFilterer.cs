using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bilateral filter of <see cref="ImageProcessingExtensions.BilateralFilter(Image, int, double, double, ConvolutionEdgeMode, CancellationToken)"/>
/// (Tomasi and Manduchi, 1998): every pixel becomes the mean of the pixels of a disk around it, each weighted by a
/// Gaussian of its distance (the spatial weight) times a Gaussian of the difference between its color and the color of the
/// pixel (the range weight), so that the edges, across which colors differ, are not blurred.
/// </summary>
/// <remarks>
/// <para>
/// The disk holds the offsets <c>(dx, dy)</c> with <c>dx² + dy² ≤ radius²</c>, as OpenCV's <c>cv2.bilateralFilter</c>
/// does. The spatial weight of an offset is <c>exp(-(dx² + dy²) / (2 σs²))</c>. The difference of two pixels is the root
/// mean square of the differences of their colors, as fractions of the sample range (premultiplied by alpha), and of their
/// alphas: <c>δ² = Σ Δcolor² / colorCount + Δalpha²</c>, which is <c>|Δ|</c> for a gray image whatever the number of its
/// color samples. The range weight is <c>exp(-δ² / (2 σr²))</c>, read from a table of <c>exp(-t)</c> with 256 entries per
/// unit of <c>t</c> and linear interpolation (a relative error below 2e-6), and 0 beyond <c>t = 32</c>. The pixel itself has
/// the weight 1. A neighbor whose difference is not a number has the weight 0, and a pixel whose samples are not numbers
/// stays so.
/// </para>
/// <para>
/// The frame is read from its working copy (<see cref="WorkingFrameCopy"/>): large frames are split into row bands, one
/// per worker, and every pixel is computed alone, so the result does not depend on the number of workers. A row is
/// accumulated one row of the disk at a time, in the buffer of its worker: its output samples, then the sums of the
/// weights of its pixels.
/// </para>
/// <para>
/// The vector path computes several pixels of a row at once, one per lane, with the samples of the center row and of the
/// row of the disk copied channel by channel (planes): each lane adds the terms of its pixel in the order of the scalar
/// loop, and the terms of a weight of 0 are added as 0, which changes no sum (the sums are never -0).
/// </para>
/// </remarks>
internal static class BilateralFilterer
{
    /// <summary>The largest value of <c>t = δ² / (2 σr²)</c> with a range weight: <c>exp(-32)</c> is about 1.3e-14.</summary>
    private const int MaxExponent = 32;

    private const int EntriesPerUnit = 256;

    private static readonly double[] RangeWeights = CreateRangeWeights();

    /// <summary>Gets the number of doubles of the row buffer of a worker for a frame of <paramref name="width"/> pixels of <paramref name="channels"/> samples.</summary>
    /// <remarks>
    /// The output samples and the sums of the weights, then for the vector path the sums of the samples, the samples of the
    /// center row and those of the row of the disk, as planes.
    /// </remarks>
    public static int GetRowDoubles(int width, int channels) => checked(width * ((4 * channels) + 1));

    /// <summary>Filters a leased frame of the format of the copy in place.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static void Apply(PixelStorage storage, WorkingFrameCopy copy, int radius, double spatialSigma, double rangeSigma, CancellationToken cancellationToken)
    {
        copy.Load(storage, cancellationToken);
        var spatialWeights = CreateSpatialWeights(radius, spatialSigma);
        var height = copy.Size.Height;
        var workers = copy.Workers;
        var rangeFactor = 1 / (2 * rangeSigma * rangeSigma);
        if (workers == 1)
        {
            FilterRows(storage, copy, radius, spatialWeights, rangeFactor, worker: 0, 0, height, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => FilterRows(storage, copy, radius, spatialWeights, rangeFactor, band, RowBands.GetBandStart(height, band, workers), RowBands.GetBandStart(height, band + 1, workers), cancellationToken));
    }

    /// <summary>Gets the range weight of <c>t = δ² / (2 σr²)</c>: <c>exp(-t)</c>, interpolated in the table, or 0 beyond the table or when <paramref name="t"/> is not a number.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double GetRangeWeight(double t)
    {
        if (!(t < MaxExponent))
            return 0;

        var position = t * EntriesPerUnit;
        var index = (int)position;
        var fraction = position - index;
        var low = RangeWeights[index];
        return low + (fraction * (RangeWeights[index + 1] - low));
    }

    /// <summary>Gets the spatial weights of the disk, row by row: entry <c>(dy + radius) × (2 radius + 1) + dx + radius</c>, 0 outside the disk.</summary>
    private static double[] CreateSpatialWeights(int radius, double spatialSigma)
    {
        var diameter = (2 * radius) + 1;
        var weights = new double[diameter * diameter];
        var factor = 1 / (2 * spatialSigma * spatialSigma);
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var distance = (dx * dx) + (dy * dy);
                if (distance <= radius * radius)
                {
                    weights[((dy + radius) * diameter) + dx + radius] = Math.Exp(-distance * factor);
                }
            }
        }

        return weights;
    }

    private static double[] CreateRangeWeights()
    {
        var weights = new double[(MaxExponent * EntriesPerUnit) + 2];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = Math.Exp(-(double)i / EntriesPerUnit);
        }

        return weights;
    }

    /// <summary>Gets the half-width of the row <paramref name="dy"/> of the disk.</summary>
    private static int GetHalfWidth(int radius, int dy)
    {
        var halfWidth = (int)Math.Sqrt((radius * radius) - (dy * dy));
        while ((halfWidth + 1) * (halfWidth + 1) + (dy * dy) <= radius * radius)
        {
            halfWidth++;
        }

        while (halfWidth > 0 && (halfWidth * halfWidth) + (dy * dy) > radius * radius)
        {
            halfWidth--;
        }

        return halfWidth;
    }

    private static void FilterRows(PixelStorage storage, WorkingFrameCopy copy, int radius, double[] spatialWeights, double rangeFactor, int worker, int start, int end, CancellationToken cancellationToken)
    {
        if (!Resampler.TestForceScalar && ((Avx2.IsSupported && Vector256.IsHardwareAccelerated) || Vector128.IsHardwareAccelerated))
        {
            FilterRowsVector(storage, copy, radius, spatialWeights, rangeFactor, worker, start, end, cancellationToken);
        }
        else
        {
            FilterRowsScalar(storage, copy, radius, spatialWeights, rangeFactor, worker, start, end, cancellationToken);
        }
    }

    /// <summary>Gets the distance factor of every working sample: its scale squared, the colors divided by their number.</summary>
    private static void GetFactors(WorkingFrameCopy copy, double rangeFactor, Span<double> factors)
    {
        var channels = factors.Length;
        var colorCount = channels == 1 ? 1 : 3;
        var hasAlpha = channels == 4;
        var colorScale = copy.ColorScale;
        var alphaScale = copy.AlphaScale;
        for (var c = 0; c < channels; c++)
        {
            factors[c] = hasAlpha && c == 3 ? alphaScale * alphaScale * rangeFactor : colorScale * colorScale / colorCount * rangeFactor;
        }
    }

    internal static void FilterRowsScalar(PixelStorage storage, WorkingFrameCopy copy, int radius, double[] spatialWeights, double rangeFactor, int worker, int start, int end, CancellationToken cancellationToken)
    {
        var width = copy.Size.Width;
        var channels = copy.WorkingChannels;
        var colorCount = channels == 1 ? 1 : 3;
        var hasAlpha = channels == 4;
        var colorScale = copy.ColorScale;
        var alphaScale = copy.AlphaScale;

        // The distance factor of every working sample: its scale squared, the colors divided by their number
        Span<double> factors = stackalloc double[channels];
        for (var c = 0; c < channels; c++)
        {
            factors[c] = hasAlpha && c == 3 ? alphaScale * alphaScale * rangeFactor : colorScale * colorScale / colorCount * rangeFactor;
        }

        var buffer = copy.GetRowBuffer(worker);
        var output = buffer[..(width * channels)];
        var weightSums = buffer.Slice(width * channels, width);
        var diameter = (2 * radius) + 1;
        for (var y = start; y < end; y++)
        {
            if ((y - start) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            output.Clear();
            weightSums.Clear();
            var center = copy.GetRow(y);
            for (var dy = -radius; dy <= radius; dy++)
            {
                var sourceRow = copy.MapRow(y + dy);
                var neighbors = sourceRow < 0 ? default : copy.GetRow(sourceRow);
                var halfWidth = GetHalfWidth(radius, dy);
                var weights = spatialWeights.AsSpan(((dy + radius) * diameter) + radius - halfWidth, (2 * halfWidth) + 1);
                for (var x = 0; x < width; x++)
                {
                    var pixel = center.Slice(x * channels, channels);
                    var sums = output.Slice(x * channels, channels);
                    var weightSum = 0d;
                    for (var dx = -halfWidth; dx <= halfWidth; dx++)
                    {
                        // A pixel read beyond the edges in the zero mode is transparent black
                        var column = x + dx;
                        if ((uint)column >= (uint)width)
                        {
                            column = copy.MapColumn(column);
                        }

                        var t = 0d;
                        var transparent = sourceRow < 0 || column < 0;
                        var offset = column * channels;
                        for (var c = 0; c < channels; c++)
                        {
                            var difference = (transparent ? 0 : neighbors[offset + c]) - (double)pixel[c];
                            t += difference * difference * factors[c];
                        }

                        var weight = weights[dx + halfWidth] * GetRangeWeight(t);
                        if (weight == 0)
                            continue;

                        weightSum += weight;
                        if (!transparent)
                        {
                            for (var c = 0; c < channels; c++)
                            {
                                sums[c] += weight * neighbors[offset + c];
                            }
                        }
                    }

                    weightSums[x] += weightSum;
                }
            }

            for (var x = 0; x < width; x++)
            {
                var weightSum = weightSums[x];
                var sums = output.Slice(x * channels, channels);
                for (var c = 0; c < channels; c++)
                {
                    sums[c] /= weightSum;
                }
            }

            copy.Store(storage, y, output);
        }
    }

    private static void FilterRowsVector(PixelStorage storage, WorkingFrameCopy copy, int radius, double[] spatialWeights, double rangeFactor, int worker, int start, int end, CancellationToken cancellationToken)
    {
        var width = copy.Size.Width;
        var channels = copy.WorkingChannels;
        Span<double> factors = stackalloc double[channels];
        GetFactors(copy, rangeFactor, factors);

        var buffer = copy.GetRowBuffer(worker);
        var length = width * channels;
        var output = buffer[..length];
        var weightSums = buffer.Slice(length, width);
        var sums = buffer.Slice(length + width, length);
        var centerPlanes = buffer.Slice((2 * length) + width, length);
        var neighborPlanes = buffer.Slice((3 * length) + width, length);
        var diameter = (2 * radius) + 1;
        for (var y = start; y < end; y++)
        {
            if ((y - start) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            sums.Clear();
            weightSums.Clear();
            ToPlanes(copy.GetRow(y), centerPlanes, width, channels);
            for (var dy = -radius; dy <= radius; dy++)
            {
                var sourceRow = copy.MapRow(y + dy);
                var halfWidth = GetHalfWidth(radius, dy);
                var weights = spatialWeights.AsSpan(((dy + radius) * diameter) + radius - halfWidth, (2 * halfWidth) + 1);
                ReadOnlySpan<double> neighbors = default;
                var vectorStart = width;
                var vectorEnd = width;
                if (sourceRow >= 0)
                {
                    if (sourceRow == y)
                    {
                        neighbors = centerPlanes;
                    }
                    else
                    {
                        ToPlanes(copy.GetRow(sourceRow), neighborPlanes, width, channels);
                        neighbors = neighborPlanes;
                    }

                    // The pixels whose disk row is inside the frame
                    if (width > 2 * halfWidth)
                    {
                        vectorStart = halfWidth;
                        vectorEnd = width - halfWidth;
                        vectorEnd -= Avx2.IsSupported && Vector256.IsHardwareAccelerated
                            ? AccumulateVector256(centerPlanes, neighbors, sums, weightSums, weights, factors, width, channels, vectorStart, vectorEnd)
                            : AccumulateVector128(centerPlanes, neighbors, sums, weightSums, weights, factors, width, channels, vectorStart, vectorEnd);
                    }
                }

                for (var x = 0; x < width; x++)
                {
                    if (x == vectorStart)
                    {
                        x = vectorEnd;
                        if (x >= width)
                            break;
                    }

                    AccumulatePixel(copy, centerPlanes, neighbors, sourceRow < 0, sums, weightSums, weights, factors, width, channels, x);
                }
            }

            for (var x = 0; x < width; x++)
            {
                var weightSum = weightSums[x];
                for (var c = 0; c < channels; c++)
                {
                    output[(x * channels) + c] = sums[(c * width) + x] / weightSum;
                }
            }

            copy.Store(storage, y, output);
        }
    }

    /// <summary>Copies a row of working samples to planes of doubles, one per channel.</summary>
    private static void ToPlanes(ReadOnlySpan<float> row, Span<double> planes, int width, int channels)
    {
        for (var c = 0; c < channels; c++)
        {
            var plane = planes.Slice(c * width, width);
            for (var x = 0; x < plane.Length; x++)
            {
                plane[x] = row[(x * channels) + c];
            }
        }
    }

    /// <summary>Adds the terms of a row of the disk to the planar sums of one pixel, as the scalar loop does.</summary>
    private static void AccumulatePixel(WorkingFrameCopy copy, ReadOnlySpan<double> center, ReadOnlySpan<double> neighbors, bool transparentRow, Span<double> sums, Span<double> weightSums, ReadOnlySpan<double> weights, ReadOnlySpan<double> factors, int width, int channels, int x)
    {
        var halfWidth = weights.Length / 2;
        var weightSum = 0d;
        for (var dx = -halfWidth; dx <= halfWidth; dx++)
        {
            var column = x + dx;
            if ((uint)column >= (uint)width)
            {
                column = copy.MapColumn(column);
            }

            var t = 0d;
            var transparent = transparentRow || column < 0;
            for (var c = 0; c < channels; c++)
            {
                var difference = (transparent ? 0 : neighbors[(c * width) + column]) - center[(c * width) + x];
                t += difference * difference * factors[c];
            }

            var weight = weights[dx + halfWidth] * GetRangeWeight(t);
            if (weight == 0)
                continue;

            weightSum += weight;
            if (!transparent)
            {
                for (var c = 0; c < channels; c++)
                {
                    sums[(c * width) + x] += weight * neighbors[(c * width) + column];
                }
            }
        }

        weightSums[x] += weightSum;
    }

    /// <summary>Adds the terms of a row of the disk to the planar sums of the pixels from <paramref name="start"/>, 4 at a time, with the range weights gathered from the table.</summary>
    /// <returns>The number of pixels before <paramref name="end"/> left to the scalar loop.</returns>
    private static int AccumulateVector256(ReadOnlySpan<double> center, ReadOnlySpan<double> neighbors, Span<double> sums, Span<double> weightSums, ReadOnlySpan<double> weights, ReadOnlySpan<double> factors, int width, int channels, int start, int end)
    {
        var halfWidth = weights.Length / 2;
        ref var centerStart = ref unsafe(MemoryMarshal.GetReference(center));
        ref var neighborStart = ref unsafe(MemoryMarshal.GetReference(neighbors));
        ref var sumStart = ref unsafe(MemoryMarshal.GetReference(sums));
        ref var weightSumStart = ref unsafe(MemoryMarshal.GetReference(weightSums));
        var limit = Vector256.Create((double)MaxExponent);
        var scale = Vector256.Create((double)EntriesPerUnit);
        var x = start;
        unsafe
        {
            fixed (double* table = RangeWeights)
            {
                for (; x + Vector256<double>.Count <= end; x += Vector256<double>.Count)
                {
                    var weightSum = Vector256<double>.Zero;
                    for (var dx = -halfWidth; dx <= halfWidth; dx++)
                    {
                        var t = Vector256<double>.Zero;
                        for (var c = 0; c < channels; c++)
                        {
                            var offset = (nuint)((c * width) + x);
                            var difference = Vector256.LoadUnsafe(ref neighborStart, (nuint)((nint)offset + dx)) - Vector256.LoadUnsafe(ref centerStart, offset);
                            t += difference * difference * Vector256.Create(factors[c]);
                        }

                        // exp(-t) interpolated in the table, 0 from t = 32 and for a t that is not a number
                        var inRange = Vector256.LessThan(t, limit);
                        var position = (t & inRange) * scale;
                        var index = Avx.ConvertToVector128Int32WithTruncation(position);
                        var fraction = position - Avx.ConvertToVector256Double(index);
                        var low = Avx2.GatherVector256(table, index, sizeof(double));
                        var high = Avx2.GatherVector256(table + 1, index, sizeof(double));
                        var range = (low + (fraction * (high - low))) & inRange;
                        var weight = Vector256.Create(weights[dx + halfWidth]) * range;
                        var kept = ~Vector256.Equals(weight, Vector256<double>.Zero);
                        weightSum += weight;
                        for (var c = 0; c < channels; c++)
                        {
                            var offset = (nuint)((c * width) + x);
                            var neighbor = Vector256.LoadUnsafe(ref neighborStart, (nuint)((nint)offset + dx));
                            ref var sum = ref Unsafe.Add(ref sumStart, offset);
                            Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref sum) + ((weight * neighbor) & kept), ref sum);
                        }
                    }

                    ref var weightSumRef = ref Unsafe.Add(ref weightSumStart, x);
                    Vector256.StoreUnsafe(Vector256.LoadUnsafe(ref weightSumRef) + weightSum, ref weightSumRef);
                }
            }
        }

        return end - x;
    }

    /// <summary>Adds the terms of a row of the disk to the planar sums of the pixels from <paramref name="start"/>, 2 at a time, with the range weights read lane by lane.</summary>
    /// <returns>The number of pixels before <paramref name="end"/> left to the scalar loop.</returns>
    private static int AccumulateVector128(ReadOnlySpan<double> center, ReadOnlySpan<double> neighbors, Span<double> sums, Span<double> weightSums, ReadOnlySpan<double> weights, ReadOnlySpan<double> factors, int width, int channels, int start, int end)
    {
        var halfWidth = weights.Length / 2;
        ref var centerStart = ref unsafe(MemoryMarshal.GetReference(center));
        ref var neighborStart = ref unsafe(MemoryMarshal.GetReference(neighbors));
        ref var sumStart = ref unsafe(MemoryMarshal.GetReference(sums));
        ref var weightSumStart = ref unsafe(MemoryMarshal.GetReference(weightSums));
        var x = start;
        for (; x + Vector128<double>.Count <= end; x += Vector128<double>.Count)
        {
            var weightSum = Vector128<double>.Zero;
            for (var dx = -halfWidth; dx <= halfWidth; dx++)
            {
                var t = Vector128<double>.Zero;
                for (var c = 0; c < channels; c++)
                {
                    var offset = (nuint)((c * width) + x);
                    var difference = unsafe(Vector128.LoadUnsafe(ref neighborStart, (nuint)((nint)offset + dx))) - unsafe(Vector128.LoadUnsafe(ref centerStart, offset));
                    t += difference * difference * Vector128.Create(factors[c]);
                }

                var weight = Vector128.Create(weights[dx + halfWidth]) * Vector128.Create(GetRangeWeight(t.GetElement(0)), GetRangeWeight(t.GetElement(1)));
                var kept = ~Vector128.Equals(weight, Vector128<double>.Zero);
                weightSum += weight;
                for (var c = 0; c < channels; c++)
                {
                    var offset = (nuint)((c * width) + x);
                    var neighbor = unsafe(Vector128.LoadUnsafe(ref neighborStart, (nuint)((nint)offset + dx)));
                    ref var sum = ref unsafe(Unsafe.Add(ref sumStart, offset));
                    unsafe { Vector128.StoreUnsafe(Vector128.LoadUnsafe(ref sum) + ((weight * neighbor) & kept), ref sum); }
                }
            }

            ref var weightSumRef = ref unsafe(Unsafe.Add(ref weightSumStart, x));
            unsafe { Vector128.StoreUnsafe(Vector128.LoadUnsafe(ref weightSumRef) + weightSum, ref weightSumRef); }
        }

        return end - x;
    }
}
