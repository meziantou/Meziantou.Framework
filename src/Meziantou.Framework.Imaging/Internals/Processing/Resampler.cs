using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The scalar reference resampler of <see cref="ImageProcessingExtensions.Resize"/>. It is the
/// numerical reference for later vectorized kernels, which must stay within the tolerances documented there.
/// </summary>
/// <remarks>
/// <para>
/// Nearest neighbor copies the selected source pixel bit for bit (zero-alpha pixels become transparent black). The other
/// filters are separable: each used source row is loaded into double-precision working samples (premultiplied by alpha,
/// in encoded or linear-light units), filtered horizontally, then combined vertically; the final sums are unpremultiplied,
/// rounded to nearest with ties upward (biased by 2^-20 so that exact ties are robust to floating-point error) and clamped. Samples are read and written at their storage precision (8 or 16 bits),
/// never through an 8-bit intermediate.
/// </para>
/// <para>Cancellation is observed between row bands; the destination is a replacement storage, so a partial result is never visible.</para>
/// <para>
/// With hardware vector support, the horizontal pass of 3- and 4-sample pixels accumulates the samples of one pixel in the
/// lanes of one 256-bit vector (two 128-bit vectors), and the vertical pass adds whole rows, lane by lane (gathering sums
/// the rows of a window by groups of at most 4, a block of samples at a time, <see cref="SetWeightedSum"/>): every sample is
/// still <c>0 + w0 x0 + w1 x1 + ...</c> in the same order without fused multiply-adds, so the result is bit-identical to the
/// scalar loops (<c>VectorizedKernelTests</c>).
/// </para>
/// </remarks>
internal static class Resampler
{
    // The largest number of ring rows that the taps summed in one pass over the accumulator read (gather)
    private const int MaxRowsPerPass = 4;

    private static readonly AsyncLocal<bool> TestForceScalarValue = new();
    private static readonly AsyncLocal<Action<int, bool>?> TestBandObserverValue = new();

    /// <summary>
    /// Gets or sets a value indicating whether the current asynchronous flow (including the workers of its parallel resizes)
    /// uses the scalar loops only. Tests compare both paths bit for bit.
    /// </summary>
    internal static bool TestForceScalar
    {
        get => TestForceScalarValue.Value;
        set => TestForceScalarValue.Value = value;
    }

    /// <summary>Gets or sets a test hook called when a row band starts (<see langword="true"/>) and ends, on the worker thread.</summary>
    internal static Action<int, bool>? TestBandObserver
    {
        get => TestBandObserverValue.Value;
        set => TestBandObserverValue.Value = value;
    }

    public static void Resize<TPixel>(scoped in PixelLease source, scoped in PixelLease destination, ResizePlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        Debug.Assert(destination.Width == plan.Geometry.OutputSize.Width && destination.Height == plan.Geometry.OutputSize.Height);

        // The leases are held by the caller for the whole operation; the workers use the leased storages directly (they
        // read the source and write disjoint destination rows)
        var sourceStorage = source.Storage;
        var destinationStorage = destination.Storage;
        if (Unsafe.SizeOf<TPixel>() != sourceStorage.BytesPerPixel || Unsafe.SizeOf<TPixel>() != destinationStorage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16))
        {
            Resize<TPixel, ushort>(sourceStorage, destinationStorage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            Resize<TPixel, byte>(sourceStorage, destinationStorage, plan, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    private static void Resize<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var observer = TestBandObserver;
        var workers = plan.Workers;
        if (workers == 1)
        {
            ResizeBand<TPixel, TSample>(source, destination, plan, 0, observer, cancellationToken);
            return;
        }

        // Bounded parallelism: one band per worker, at most `workers` bands running at once (the calling thread included)
        RowBands.RunParallel(workers, band => ResizeBand<TPixel, TSample>(source, destination, plan, band, observer, cancellationToken));
    }

    private static void ResizeBand<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, int band, Action<int, bool>? observer, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        observer?.Invoke(band, true);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        if (plan.Filter == ResamplingFilter.NearestNeighbor)
        {
            Nearest<TPixel, TSample>(source, destination, plan, start, end, cancellationToken);
        }
        else if (plan.Strategy == ResizeVerticalStrategy.Gather)
        {
            Gather<TPixel, TSample>(source, destination, plan, plan.GetScratch(band), start, end, cancellationToken);
        }
        else
        {
            Scatter<TPixel, TSample>(source, destination, plan, plan.GetScratch(band), start, end, cancellationToken);
        }

        observer?.Invoke(band, false);
    }

    private static Span<TPixel> GetRow<TPixel>(PixelStorage storage, int y)
        where TPixel : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y)));

    private static void Nearest<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var xs = plan.XIndices;
        var ys = plan.YIndices;
        for (var dy = start; dy < end; dy++)
        {
            if (dy % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var input = GetRow<TPixel>(source, ys[dy]);
            var output = GetRow<TPixel>(destination, dy);
            for (var dx = 0; dx < xs.Length; dx++)
            {
                output[dx] = input[xs[dx]];
            }

            if (plan.HasAlpha)
            {
                // A pixel whose alpha is zero is written as transparent black, as for every filter
                var samples = unsafe(MemoryMarshal.Cast<TPixel, TSample>(output));
                for (var i = 0; i < samples.Length; i += 4)
                {
                    if (WorkingSamples.ToInt(samples[i + 3]) == 0)
                    {
                        samples.Slice(i, 4).Clear();
                    }
                }
            }
        }
    }

    /// <summary>Output rows <paramref name="start"/> to <paramref name="end"/> - 1, gathering each from a ring of horizontally resampled source rows.</summary>
    private static void Gather<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, ResizeScratch scratch, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var yWeights = plan.YWeights!;
        var ringSize = scratch.RowCount;
        var accumulator = scratch.ExtraRow;
        var next = yWeights.GetStart(start);

        // The taps are summed a block of samples at a time (SetWeightedSum) by groups that read at most MaxRowsPerPass ring
        // rows (the prefetching of the processor follows a limited number of streams), each group adding to the sums of the
        // previous ones; the scalar reference of the tests adds a row per tap
        var blocked = !TestForceScalar && (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated);
        Span<int> offsets = stackalloc int[MaxRowsPerPass];
        for (var dy = start; dy < end; dy++)
        {
            if (dy % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The ring holds the horizontally resampled source rows [next - ringSize, next - 1]; windows are non-decreasing
            // and at most ringSize rows long, so the whole window of dy is in the ring once its last row is loaded
            var first = yWeights.GetStart(dy);
            var weights = yWeights.GetWeights(dy);
            var last = first + weights.Length - 1;
            while (next <= last)
            {
                LoadAndResampleRow<TPixel, TSample>(source, next, plan, scratch, scratch.GetSlot(next % ringSize));
                next++;
            }

            // 0 + w0 H0 written directly (no separate clearing pass), then + w1 H1 + ...
            if (blocked)
            {
                var rows = scratch.Rows;
                var rowLength = scratch.RowLength;
                for (var k = 0; k < weights.Length; k += MaxRowsPerPass)
                {
                    var count = Math.Min(MaxRowsPerPass, weights.Length - k);
                    for (var t = 0; t < count; t++)
                    {
                        offsets[t] = (((first + k + t) % ringSize) + 1) * rowLength;
                    }

                    SetWeightedSum(rows, 0, rowLength, offsets[..count], weights.Slice(k, count), accumulate: k is not 0);
                }
            }
            else
            {
                SetScaled(accumulator, scratch.GetSlot(first % ringSize), weights[0]);
                for (var k = 1; k < weights.Length; k++)
                {
                    AddScaled(accumulator, scratch.GetSlot((first + k) % ringSize), weights[k]);
                }
            }

            WorkingSamples.Store(accumulator, unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(destination, dy))), plan.Alpha, plan.Transfer, plan.MaxValue);
        }
    }

    /// <summary>Output rows <paramref name="start"/> to <paramref name="end"/> - 1, scattering each source row of their windows into per-output accumulators.</summary>
    private static void Scatter<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, ResizeScratch scratch, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var yWeights = plan.YWeights!;
        var slots = scratch.RowCount;
        var row = scratch.ExtraRow;
        var first = start; // the oldest output row still accumulating
        var next = start;  // the next output row to start
        var lastSource = yWeights.GetStart(end - 1) + yWeights.GetCount(end - 1) - 1;
        for (var sy = yWeights.GetStart(start); sy <= lastSource; sy++)
        {
            if (sy % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Every output row starts at its first source row; at most `slots` rows accumulate at the same time
            while (next < end && yWeights.GetStart(next) <= sy)
            {
                Debug.Assert(next - first < slots);
                next++;
            }

            // The first source row of an output row sets its accumulator to 0 + w0 H0 (no separate clearing pass)
            LoadAndResampleRow<TPixel, TSample>(source, sy, plan, scratch, row);
            for (var dy = first; dy < next; dy++)
            {
                var windowStart = yWeights.GetStart(dy);
                if (sy == windowStart)
                {
                    SetScaled(scratch.GetSlot(dy % slots), row, yWeights.GetWeights(dy)[0]);
                }
                else
                {
                    AddScaled(scratch.GetSlot(dy % slots), row, yWeights.GetWeights(dy)[sy - windowStart]);
                }
            }

            while (first < next && yWeights.GetStart(first) + yWeights.GetCount(first) - 1 == sy)
            {
                WorkingSamples.Store(scratch.GetSlot(first % slots), unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(destination, first))), plan.Alpha, plan.Transfer, plan.MaxValue);
                first++;
            }
        }

        Debug.Assert(first == end);
    }

    /// <summary>Loads the used columns of source row <paramref name="y"/> as working samples and resamples them horizontally into <paramref name="output"/>.</summary>
    private static void LoadAndResampleRow<TPixel, TSample>(PixelStorage source, int y, ResizePlan plan, ResizeScratch scratch, Span<double> output)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var xWeights = plan.XWeights!;
        var channels = plan.Channels;
        var first = xWeights.First;
        var working = scratch.SourceRow;
        var used = (xWeights.Last - first + 1) * channels;
        var samples = unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(source, y))).Slice(first * channels, used);
        WorkingSamples.Load(samples, working[..used], plan.Alpha, plan.Transfer);

        ResampleHorizontal(working, output, xWeights, channels);
    }

    /// <summary>Resamples one row of working samples horizontally (<c>output[dx] = 0 + w0 x0 + w1 x1 + ...</c> per sample).</summary>
    internal static void ResampleHorizontal(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights, int channels)
    {
        if (TestForceScalar)
        {
            ResampleHorizontalScalar(working, output, xWeights, channels);
        }
        else if (channels == 4 && Vector256.IsHardwareAccelerated)
        {
            ResampleHorizontal4Vector256(working, output, xWeights);
        }
        else if (channels == 4 && Vector128.IsHardwareAccelerated)
        {
            ResampleHorizontal4Vector128(working, output, xWeights);
        }
        else if (channels == 3 && Vector256.IsHardwareAccelerated)
        {
            ResampleHorizontal3Vector256(working, output, xWeights);
        }
        else if (channels == 3 && Vector128.IsHardwareAccelerated)
        {
            ResampleHorizontal3Vector128(working, output, xWeights);
        }
        else if (channels == 1)
        {
            ResampleHorizontal1Interleaved(working, output, xWeights);
        }
        else
        {
            ResampleHorizontalScalar(working, output, xWeights, channels);
        }
    }

    /// <summary>The scalar reference of <see cref="ResampleHorizontal"/>.</summary>
    internal static void ResampleHorizontalScalar(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights, int channels)
    {
        var first = xWeights.First;
        for (var dx = 0; dx < xWeights.OutputLength; dx++)
        {
            var weights = xWeights.GetWeights(dx);
            var input = working[((xWeights.GetStart(dx) - first) * channels)..];
            var o = output.Slice(dx * channels, channels);
            switch (channels)
            {
                case 4:
                {
                    double c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                    for (var k = 0; k < weights.Length; k++)
                    {
                        var w = weights[k];
                        var p = input.Slice(4 * k, 4);
                        c0 += w * p[0];
                        c1 += w * p[1];
                        c2 += w * p[2];
                        c3 += w * p[3];
                    }

                    o[0] = c0;
                    o[1] = c1;
                    o[2] = c2;
                    o[3] = c3;
                    break;
                }

                case 3:
                {
                    double c0 = 0, c1 = 0, c2 = 0;
                    for (var k = 0; k < weights.Length; k++)
                    {
                        var w = weights[k];
                        var p = input.Slice(3 * k, 3);
                        c0 += w * p[0];
                        c1 += w * p[1];
                        c2 += w * p[2];
                    }

                    o[0] = c0;
                    o[1] = c1;
                    o[2] = c2;
                    break;
                }

                default:
                {
                    Debug.Assert(channels == 1);
                    double c0 = 0;
                    for (var k = 0; k < weights.Length; k++)
                    {
                        c0 += weights[k] * input[k];
                    }

                    o[0] = c0;
                    break;
                }
            }
        }
    }

    // The vector loops below compute two output pixels per iteration (four for gray): each pixel is one chain of dependent
    // additions, so a single pixel at a time waits for the latency of every add. Each accumulator still sums its own taps
    // in increasing order (the taps both windows have, then the remaining taps of each), so the sums are those of the
    // scalar loop.
    private static void ResampleHorizontal4Vector256(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var outputLength = xWeights.OutputLength;
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output[..(outputLength * 4)]));
        var dx = 0;
        for (; dx + 1 < outputLength; dx += 2)
        {
            var weights0 = table.GetWeights(dx);
            var weights1 = table.GetWeights(dx + 1);
            ref var input0 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 4, weights0.Length * 4)));
            ref var input1 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx + 1) - first) * 4, weights1.Length * 4)));
            ref var w0 = ref unsafe(MemoryMarshal.GetReference(weights0));
            ref var w1 = ref unsafe(MemoryMarshal.GetReference(weights1));
            var common = Math.Min(weights0.Length, weights1.Length);
            var sum0 = Vector256<double>.Zero;
            var sum1 = Vector256<double>.Zero;
            for (var k = 0; k < common; k++)
            {
                sum0 += Vector256.Create(unsafe(Unsafe.Add(ref w0, k))) * unsafe(Vector256.LoadUnsafe(ref input0, (nuint)(4 * k)));
                sum1 += Vector256.Create(unsafe(Unsafe.Add(ref w1, k))) * unsafe(Vector256.LoadUnsafe(ref input1, (nuint)(4 * k)));
            }

            sum0 = Add4Vector256(sum0, ref input0, ref w0, common, weights0.Length);
            sum1 = Add4Vector256(sum1, ref input1, ref w1, common, weights1.Length);
            unsafe { sum0.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
            unsafe { sum1.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 4)); }
        }

        if (dx < outputLength)
        {
            var weights = table.GetWeights(dx);
            ref var input = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 4, weights.Length * 4)));
            var sum = Add4Vector256(Vector256<double>.Zero, ref input, ref unsafe(MemoryMarshal.GetReference(weights)), 0, weights.Length);
            unsafe { sum.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
        }
    }

    /// <summary>Adds the taps <paramref name="start"/> to <paramref name="end"/> (excluded) of a window of 4-sample pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Add4Vector256(Vector256<double> sum, ref double input, ref double weights, int start, int end)
    {
        for (var k = start; k < end; k++)
        {
            sum += Vector256.Create(unsafe(Unsafe.Add(ref weights, k))) * unsafe(Vector256.LoadUnsafe(ref input, (nuint)(4 * k)));
        }

        return sum;
    }

    private static void ResampleHorizontal4Vector128(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var outputLength = xWeights.OutputLength;
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output[..(outputLength * 4)]));
        var dx = 0;
        for (; dx + 1 < outputLength; dx += 2)
        {
            var weights0 = table.GetWeights(dx);
            var weights1 = table.GetWeights(dx + 1);
            ref var input0 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 4, weights0.Length * 4)));
            ref var input1 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx + 1) - first) * 4, weights1.Length * 4)));
            ref var w0 = ref unsafe(MemoryMarshal.GetReference(weights0));
            ref var w1 = ref unsafe(MemoryMarshal.GetReference(weights1));
            var common = Math.Min(weights0.Length, weights1.Length);
            var low0 = Vector128<double>.Zero;
            var high0 = Vector128<double>.Zero;
            var low1 = Vector128<double>.Zero;
            var high1 = Vector128<double>.Zero;
            for (var k = 0; k < common; k++)
            {
                var weight0 = Vector128.Create(unsafe(Unsafe.Add(ref w0, k)));
                var weight1 = Vector128.Create(unsafe(Unsafe.Add(ref w1, k)));
                low0 += weight0 * unsafe(Vector128.LoadUnsafe(ref input0, (nuint)(4 * k)));
                high0 += weight0 * unsafe(Vector128.LoadUnsafe(ref input0, (nuint)((4 * k) + 2)));
                low1 += weight1 * unsafe(Vector128.LoadUnsafe(ref input1, (nuint)(4 * k)));
                high1 += weight1 * unsafe(Vector128.LoadUnsafe(ref input1, (nuint)((4 * k) + 2)));
            }

            Add4Vector128(ref low0, ref high0, ref input0, ref w0, common, weights0.Length);
            Add4Vector128(ref low1, ref high1, ref input1, ref w1, common, weights1.Length);
            unsafe { low0.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
            unsafe { high0.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 2)); }
            unsafe { low1.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 4)); }
            unsafe { high1.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 6)); }
        }

        if (dx < outputLength)
        {
            var weights = table.GetWeights(dx);
            ref var input = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 4, weights.Length * 4)));
            var low = Vector128<double>.Zero;
            var high = Vector128<double>.Zero;
            Add4Vector128(ref low, ref high, ref input, ref unsafe(MemoryMarshal.GetReference(weights)), 0, weights.Length);
            unsafe { low.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
            unsafe { high.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 2)); }
        }
    }

    /// <summary>Adds the taps <paramref name="start"/> to <paramref name="end"/> (excluded) of a window of 4-sample pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Add4Vector128(ref Vector128<double> low, ref Vector128<double> high, ref double input, ref double weights, int start, int end)
    {
        for (var k = start; k < end; k++)
        {
            var weight = Vector128.Create(unsafe(Unsafe.Add(ref weights, k)));
            low += weight * unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * k)));
            high += weight * unsafe(Vector128.LoadUnsafe(ref input, (nuint)((4 * k) + 2)));
        }
    }

    /// <summary>
    /// 3-sample pixels in two 128-bit vectors: the first two samples, then the third and the first sample of the next pixel
    /// (the working row has one extra element, see <see cref="ResizePlan"/>), whose lane is ignored, so the three samples
    /// are those of the scalar loop.
    /// </summary>
    private static void ResampleHorizontal3Vector128(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var outputLength = xWeights.OutputLength;
        var dx = 0;
        for (; dx + 1 < outputLength; dx += 2)
        {
            var weights0 = table.GetWeights(dx);
            var weights1 = table.GetWeights(dx + 1);
            ref var input0 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 3, (weights0.Length * 3) + 1)));
            ref var input1 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx + 1) - first) * 3, (weights1.Length * 3) + 1)));
            ref var w0 = ref unsafe(MemoryMarshal.GetReference(weights0));
            ref var w1 = ref unsafe(MemoryMarshal.GetReference(weights1));
            var common = Math.Min(weights0.Length, weights1.Length);
            var low0 = Vector128<double>.Zero;
            var high0 = Vector128<double>.Zero;
            var low1 = Vector128<double>.Zero;
            var high1 = Vector128<double>.Zero;
            for (var k = 0; k < common; k++)
            {
                var weight0 = Vector128.Create(unsafe(Unsafe.Add(ref w0, k)));
                var weight1 = Vector128.Create(unsafe(Unsafe.Add(ref w1, k)));
                low0 += weight0 * unsafe(Vector128.LoadUnsafe(ref input0, (nuint)(3 * k)));
                high0 += weight0 * unsafe(Vector128.LoadUnsafe(ref input0, (nuint)((3 * k) + 2)));
                low1 += weight1 * unsafe(Vector128.LoadUnsafe(ref input1, (nuint)(3 * k)));
                high1 += weight1 * unsafe(Vector128.LoadUnsafe(ref input1, (nuint)((3 * k) + 2)));
            }

            Add3Vector128(ref low0, ref high0, ref input0, ref w0, common, weights0.Length);
            Add3Vector128(ref low1, ref high1, ref input1, ref w1, common, weights1.Length);
            var o = output.Slice(dx * 3, 6);
            o[0] = low0.GetElement(0);
            o[1] = low0.GetElement(1);
            o[2] = high0.GetElement(0);
            o[3] = low1.GetElement(0);
            o[4] = low1.GetElement(1);
            o[5] = high1.GetElement(0);
        }

        if (dx < outputLength)
        {
            var weights = table.GetWeights(dx);
            ref var input = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 3, (weights.Length * 3) + 1)));
            var low = Vector128<double>.Zero;
            var high = Vector128<double>.Zero;
            Add3Vector128(ref low, ref high, ref input, ref unsafe(MemoryMarshal.GetReference(weights)), 0, weights.Length);
            var o = output.Slice(dx * 3, 3);
            o[0] = low.GetElement(0);
            o[1] = low.GetElement(1);
            o[2] = high.GetElement(0);
        }
    }

    /// <summary>Adds the taps <paramref name="start"/> to <paramref name="end"/> (excluded) of a window of 3-sample pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Add3Vector128(ref Vector128<double> low, ref Vector128<double> high, ref double input, ref double weights, int start, int end)
    {
        for (var k = start; k < end; k++)
        {
            var weight = Vector128.Create(unsafe(Unsafe.Add(ref weights, k)));
            low += weight * unsafe(Vector128.LoadUnsafe(ref input, (nuint)(3 * k)));
            high += weight * unsafe(Vector128.LoadUnsafe(ref input, (nuint)((3 * k) + 2)));
        }
    }

    /// <summary>
    /// 3-sample pixels: each vector loads one pixel and the first sample of the next one (the working row has one extra
    /// element, see <see cref="ResizePlan"/>); the fourth lane is ignored, so the three samples are those of the scalar loop.
    /// </summary>
    private static void ResampleHorizontal3Vector256(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var outputLength = xWeights.OutputLength;
        var dx = 0;
        for (; dx + 1 < outputLength; dx += 2)
        {
            var weights0 = table.GetWeights(dx);
            var weights1 = table.GetWeights(dx + 1);
            ref var input0 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 3, (weights0.Length * 3) + 1)));
            ref var input1 = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx + 1) - first) * 3, (weights1.Length * 3) + 1)));
            ref var w0 = ref unsafe(MemoryMarshal.GetReference(weights0));
            ref var w1 = ref unsafe(MemoryMarshal.GetReference(weights1));
            var common = Math.Min(weights0.Length, weights1.Length);
            var sum0 = Vector256<double>.Zero;
            var sum1 = Vector256<double>.Zero;
            for (var k = 0; k < common; k++)
            {
                sum0 += Vector256.Create(unsafe(Unsafe.Add(ref w0, k))) * unsafe(Vector256.LoadUnsafe(ref input0, (nuint)(3 * k)));
                sum1 += Vector256.Create(unsafe(Unsafe.Add(ref w1, k))) * unsafe(Vector256.LoadUnsafe(ref input1, (nuint)(3 * k)));
            }

            sum0 = Add3Vector256(sum0, ref input0, ref w0, common, weights0.Length);
            sum1 = Add3Vector256(sum1, ref input1, ref w1, common, weights1.Length);
            var o = output.Slice(dx * 3, 6);
            o[0] = sum0.GetElement(0);
            o[1] = sum0.GetElement(1);
            o[2] = sum0.GetElement(2);
            o[3] = sum1.GetElement(0);
            o[4] = sum1.GetElement(1);
            o[5] = sum1.GetElement(2);
        }

        if (dx < outputLength)
        {
            var weights = table.GetWeights(dx);
            ref var input = ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetStart(dx) - first) * 3, (weights.Length * 3) + 1)));
            var sum = Add3Vector256(Vector256<double>.Zero, ref input, ref unsafe(MemoryMarshal.GetReference(weights)), 0, weights.Length);
            var o = output.Slice(dx * 3, 3);
            o[0] = sum.GetElement(0);
            o[1] = sum.GetElement(1);
            o[2] = sum.GetElement(2);
        }
    }

    /// <summary>Adds the taps <paramref name="start"/> to <paramref name="end"/> (excluded) of a window of 3-sample pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Add3Vector256(Vector256<double> sum, ref double input, ref double weights, int start, int end)
    {
        for (var k = start; k < end; k++)
        {
            sum += Vector256.Create(unsafe(Unsafe.Add(ref weights, k))) * unsafe(Vector256.LoadUnsafe(ref input, (nuint)(3 * k)));
        }

        return sum;
    }

    /// <summary>1-sample pixels, four output pixels per iteration (four independent chains of additions).</summary>
    private static void ResampleHorizontal1Interleaved(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var outputLength = xWeights.OutputLength;
        output = output[..outputLength];
        var dx = 0;
        for (; dx + 3 < outputLength; dx += 4)
        {
            var weights0 = table.GetWeights(dx);
            var weights1 = table.GetWeights(dx + 1);
            var weights2 = table.GetWeights(dx + 2);
            var weights3 = table.GetWeights(dx + 3);
            ref var input0 = ref unsafe(MemoryMarshal.GetReference(working.Slice(table.GetStart(dx) - first, weights0.Length)));
            ref var input1 = ref unsafe(MemoryMarshal.GetReference(working.Slice(table.GetStart(dx + 1) - first, weights1.Length)));
            ref var input2 = ref unsafe(MemoryMarshal.GetReference(working.Slice(table.GetStart(dx + 2) - first, weights2.Length)));
            ref var input3 = ref unsafe(MemoryMarshal.GetReference(working.Slice(table.GetStart(dx + 3) - first, weights3.Length)));
            ref var w0 = ref unsafe(MemoryMarshal.GetReference(weights0));
            ref var w1 = ref unsafe(MemoryMarshal.GetReference(weights1));
            ref var w2 = ref unsafe(MemoryMarshal.GetReference(weights2));
            ref var w3 = ref unsafe(MemoryMarshal.GetReference(weights3));
            var common = Math.Min(Math.Min(weights0.Length, weights1.Length), Math.Min(weights2.Length, weights3.Length));
            double sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
            for (var k = 0; k < common; k++)
            {
                sum0 += unsafe(Unsafe.Add(ref w0, k)) * unsafe(Unsafe.Add(ref input0, k));
                sum1 += unsafe(Unsafe.Add(ref w1, k)) * unsafe(Unsafe.Add(ref input1, k));
                sum2 += unsafe(Unsafe.Add(ref w2, k)) * unsafe(Unsafe.Add(ref input2, k));
                sum3 += unsafe(Unsafe.Add(ref w3, k)) * unsafe(Unsafe.Add(ref input3, k));
            }

            output[dx] = Add1(sum0, weights0, working[(table.GetStart(dx) - first)..], common);
            output[dx + 1] = Add1(sum1, weights1, working[(table.GetStart(dx + 1) - first)..], common);
            output[dx + 2] = Add1(sum2, weights2, working[(table.GetStart(dx + 2) - first)..], common);
            output[dx + 3] = Add1(sum3, weights3, working[(table.GetStart(dx + 3) - first)..], common);
        }

        for (; dx < outputLength; dx++)
        {
            output[dx] = Add1(0, table.GetWeights(dx), working[(table.GetStart(dx) - first)..], 0);
        }

        static double Add1(double sum, ReadOnlySpan<double> weights, ReadOnlySpan<double> input, int start)
        {
            for (var k = start; k < weights.Length; k++)
            {
                sum += weights[k] * input[k];
            }

            return sum;
        }
    }

    /// <summary>Sets the accumulator to <c>0 + weight * row</c>, sample by sample (the first term of a sum started from zero).</summary>
    /// <remarks>
    /// Never inlined, like <see cref="AddScaled"/>: when the tiered JIT inlined these hot calls into the row loops of their
    /// callers, resizing and convolution were measured 1.5 to 4 times slower.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetScaled(Span<double> accumulator, ReadOnlySpan<double> row, double weight)
    {
        Debug.Assert(accumulator.Length == row.Length);
        var i = 0;
        if (!TestForceScalar && Vector256.IsHardwareAccelerated)
        {
            ref var accumulatorStart = ref unsafe(MemoryMarshal.GetReference(accumulator));
            ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row[..accumulator.Length]));
            var w = Vector256.Create(weight);
            for (; i + 4 <= accumulator.Length; i += 4)
            {
                unsafe { (Vector256<double>.Zero + (w * Vector256.LoadUnsafe(ref rowStart, (nuint)i))).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }
        else if (!TestForceScalar && Vector128.IsHardwareAccelerated)
        {
            ref var accumulatorStart = ref unsafe(MemoryMarshal.GetReference(accumulator));
            ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row[..accumulator.Length]));
            var w = Vector128.Create(weight);
            for (; i + 2 <= accumulator.Length; i += 2)
            {
                unsafe { (Vector128<double>.Zero + (w * Vector128.LoadUnsafe(ref rowStart, (nuint)i))).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }

        for (; i < accumulator.Length; i++)
        {
            accumulator[i] = 0d + (weight * row[i]);
        }
    }

    /// <summary>
    /// Sets <paramref name="length"/> values of <paramref name="buffer"/> at <paramref name="accumulatorOffset"/> to the sum of
    /// the rows of <paramref name="buffer"/> at <paramref name="offsets"/> times their <paramref name="weights"/>, or adds that
    /// sum to them when <paramref name="accumulate"/> is set: the operations of <see cref="SetScaled"/> with the first row
    /// (<see cref="AddScaled"/> when accumulating) then of <see cref="AddScaled"/> with each next one, in that order for every
    /// sample, but summed in registers for a block of samples instead of one pass over the accumulator per row.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SetWeightedSum(Span<double> buffer, int accumulatorOffset, int length, ReadOnlySpan<int> offsets, ReadOnlySpan<double> weights, bool accumulate)
    {
        Debug.Assert(offsets.Length == weights.Length && offsets.Length > 0);
        Debug.Assert(accumulatorOffset + length <= buffer.Length);
        foreach (var offset in offsets)
        {
            Debug.Assert(offset >= 0 && offset + length <= buffer.Length && (offset + length <= accumulatorOffset || offset >= accumulatorOffset + length));
        }

        var i = 0;
        ref var start = ref unsafe(MemoryMarshal.GetReference(buffer));
        if (Vector256.IsHardwareAccelerated)
        {
            for (; i + 16 <= length; i += 16)
            {
                ref var target = ref unsafe(Unsafe.Add(ref start, accumulatorOffset + i));
                var sum0 = accumulate ? unsafe(Vector256.LoadUnsafe(ref target)) : Vector256<double>.Zero;
                var sum1 = accumulate ? unsafe(Vector256.LoadUnsafe(ref target, 4)) : Vector256<double>.Zero;
                var sum2 = accumulate ? unsafe(Vector256.LoadUnsafe(ref target, 8)) : Vector256<double>.Zero;
                var sum3 = accumulate ? unsafe(Vector256.LoadUnsafe(ref target, 12)) : Vector256<double>.Zero;
                var w = Vector256.Create(weights[0]);
                ref var row = ref unsafe(Unsafe.Add(ref start, offsets[0] + i));
                sum0 += w * unsafe(Vector256.LoadUnsafe(ref row));
                sum1 += w * unsafe(Vector256.LoadUnsafe(ref row, 4));
                sum2 += w * unsafe(Vector256.LoadUnsafe(ref row, 8));
                sum3 += w * unsafe(Vector256.LoadUnsafe(ref row, 12));
                for (var t = 1; t < offsets.Length; t++)
                {
                    w = Vector256.Create(weights[t]);
                    row = ref unsafe(Unsafe.Add(ref start, offsets[t] + i));
                    sum0 += w * unsafe(Vector256.LoadUnsafe(ref row));
                    sum1 += w * unsafe(Vector256.LoadUnsafe(ref row, 4));
                    sum2 += w * unsafe(Vector256.LoadUnsafe(ref row, 8));
                    sum3 += w * unsafe(Vector256.LoadUnsafe(ref row, 12));
                }

                unsafe { sum0.StoreUnsafe(ref target); }
                unsafe { sum1.StoreUnsafe(ref target, 4); }
                unsafe { sum2.StoreUnsafe(ref target, 8); }
                unsafe { sum3.StoreUnsafe(ref target, 12); }
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            for (; i + 8 <= length; i += 8)
            {
                ref var target = ref unsafe(Unsafe.Add(ref start, accumulatorOffset + i));
                var sum0 = accumulate ? unsafe(Vector128.LoadUnsafe(ref target)) : Vector128<double>.Zero;
                var sum1 = accumulate ? unsafe(Vector128.LoadUnsafe(ref target, 2)) : Vector128<double>.Zero;
                var sum2 = accumulate ? unsafe(Vector128.LoadUnsafe(ref target, 4)) : Vector128<double>.Zero;
                var sum3 = accumulate ? unsafe(Vector128.LoadUnsafe(ref target, 6)) : Vector128<double>.Zero;
                var w = Vector128.Create(weights[0]);
                ref var row = ref unsafe(Unsafe.Add(ref start, offsets[0] + i));
                sum0 += w * unsafe(Vector128.LoadUnsafe(ref row));
                sum1 += w * unsafe(Vector128.LoadUnsafe(ref row, 2));
                sum2 += w * unsafe(Vector128.LoadUnsafe(ref row, 4));
                sum3 += w * unsafe(Vector128.LoadUnsafe(ref row, 6));
                for (var t = 1; t < offsets.Length; t++)
                {
                    w = Vector128.Create(weights[t]);
                    row = ref unsafe(Unsafe.Add(ref start, offsets[t] + i));
                    sum0 += w * unsafe(Vector128.LoadUnsafe(ref row));
                    sum1 += w * unsafe(Vector128.LoadUnsafe(ref row, 2));
                    sum2 += w * unsafe(Vector128.LoadUnsafe(ref row, 4));
                    sum3 += w * unsafe(Vector128.LoadUnsafe(ref row, 6));
                }

                unsafe { sum0.StoreUnsafe(ref target); }
                unsafe { sum1.StoreUnsafe(ref target, 2); }
                unsafe { sum2.StoreUnsafe(ref target, 4); }
                unsafe { sum3.StoreUnsafe(ref target, 6); }
            }
        }

        for (; i < length; i++)
        {
            var sum = (accumulate ? buffer[accumulatorOffset + i] : 0d) + (weights[0] * buffer[offsets[0] + i]);
            for (var t = 1; t < offsets.Length; t++)
            {
                sum += weights[t] * buffer[offsets[t] + i];
            }

            buffer[accumulatorOffset + i] = sum;
        }
    }

    /// <summary>Adds <c>weight * row</c> to the accumulator, sample by sample (vectorized when supported; the same operations).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void AddScaled(Span<double> accumulator, ReadOnlySpan<double> row, double weight)
    {
        Debug.Assert(accumulator.Length == row.Length);
        var i = 0;
        if (TestForceScalar)
        {
            // Scalar loop below
        }
        else if (Vector256.IsHardwareAccelerated)
        {
            ref var accumulatorStart = ref unsafe(MemoryMarshal.GetReference(accumulator));
            ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row[..accumulator.Length]));
            var w = Vector256.Create(weight);
            for (; i + 4 <= accumulator.Length; i += 4)
            {
                unsafe { (Vector256.LoadUnsafe(ref accumulatorStart, (nuint)i) + (w * Vector256.LoadUnsafe(ref rowStart, (nuint)i))).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            ref var accumulatorStart = ref unsafe(MemoryMarshal.GetReference(accumulator));
            ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row[..accumulator.Length]));
            var w = Vector128.Create(weight);
            for (; i + 2 <= accumulator.Length; i += 2)
            {
                unsafe { (Vector128.LoadUnsafe(ref accumulatorStart, (nuint)i) + (w * Vector128.LoadUnsafe(ref rowStart, (nuint)i))).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }

        for (; i < accumulator.Length; i++)
        {
            accumulator[i] += weight * row[i];
        }
    }
}
