using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

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
/// A frame whose pixels read by the filter (<see cref="ResampleWeights.First"/> to <see cref="ResampleWeights.Last"/> on
/// both axes) all have the maximum alpha is filtered as three colors per pixel, straight (<see cref="WorkingAlpha.Opaque"/>),
/// and every output alpha is the maximum: premultiplying by a constant alpha, then dividing by the filtered alpha (that
/// constant times weights that add up to 1), would only add roundings. Any other frame is filtered premultiplied.
/// </para>
/// <para>
/// With hardware vector support, the horizontal pass of 3- and 4-sample pixels accumulates the samples of one pixel in the
/// lanes of one 256-bit vector (two 128-bit vectors), and that of 1-sample pixels one sample of 4 (2) pixels, over windows
/// padded with zero weights (<see cref="ResampleWeights.CreatePadded"/>, which leave the sums unchanged); the vertical pass
/// adds whole rows, lane by lane (gathering sums the rows of a window by groups of at most 4, a block of samples at a time,
/// <see cref="SetWeightedSum"/>): every sample is still <c>0 + w0 x0 + w1 x1 + ...</c> in the same order, so the result is
/// bit-identical to the scalar loops (<c>VectorizedKernelTests</c>).
/// </para>
/// <para>
/// Every term after the first one is added with a fused multiply-add (<c>s = fma(w, x, s)</c>, one rounding per term
/// instead of two), in the scalar loops (<see cref="Math.FusedMultiplyAdd"/>) as in the vector ones, so the results do not
/// depend on the hardware. The first term, <c>0 + w0 x0</c>, is the rounded product either way. The vector loops run only
/// when fused multiply-adds are instructions of the processor (<see cref="IsFmaAccelerated"/>): without them, the
/// operation of <see cref="Vector128.FusedMultiplyAdd(Vector128{double}, Vector128{double}, Vector128{double})"/> is still
/// exact but computed one element at a time, and the scalar loops are used instead.
/// </para>
/// </remarks>
internal static class Resampler
{
    // The largest number of ring rows that the taps summed in one pass over the accumulator read (gather)
    private const int MaxRowsPerPass = 4;

    private static readonly AsyncLocal<bool> TestForceScalarValue = new();
    private static readonly AsyncLocal<bool> TestDisableFixedPointValue = new();
    private static readonly AsyncLocal<Action<int, bool>?> TestBandObserverValue = new();

    /// <summary>
    /// Gets a value indicating whether fused multiply-adds are instructions of the processor (FMA3 on x64, always on Arm64).
    /// The vector loops need them; otherwise the scalar loops compute the same fused operations.
    /// </summary>
    internal static bool IsFmaAccelerated => Fma.IsSupported || AdvSimd.IsSupported;

    /// <summary>
    /// Gets or sets a value indicating whether the current asynchronous flow (including the workers of its parallel resizes)
    /// uses the scalar loops only. Tests compare both paths bit for bit.
    /// </summary>
    internal static bool TestForceScalar
    {
        get => TestForceScalarValue.Value;
        set => TestForceScalarValue.Value = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the resizes prepared on the current asynchronous flow keep the
    /// double-precision path for 8-bit samples (tests compare the integer path with it).
    /// </summary>
    internal static bool TestDisableFixedPoint
    {
        get => TestDisableFixedPointValue.Value;
        set => TestDisableFixedPointValue.Value = value;
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
        var alpha = GetWorkingAlpha<TPixel>(source, plan);
        if (workers == 1)
        {
            ResizeBand<TPixel, TSample>(source, destination, plan, alpha, 0, observer, cancellationToken);
            return;
        }

        // Bounded parallelism: one band per worker, at most `workers` bands running at once (the calling thread included)
        RowBands.RunParallel(workers, band => ResizeBand<TPixel, TSample>(source, destination, plan, alpha, band, observer, cancellationToken));
    }

    /// <summary>
    /// Gets how alpha takes part in the working samples of a frame: <see cref="WorkingAlpha.Opaque"/> when the resize is
    /// filtered and every source pixel its weights read has the maximum alpha, otherwise <see cref="ResizePlan.Alpha"/>.
    /// </summary>
    internal static WorkingAlpha GetWorkingAlpha<TPixel>(PixelStorage source, ResizePlan plan)
        where TPixel : unmanaged
        => plan.Filter != ResamplingFilter.NearestNeighbor && plan.HasAlpha && IsOpaque<TPixel>(source, plan) ? WorkingAlpha.Opaque : plan.Alpha;

    /// <summary>Gets a value indicating whether every source pixel that the weights of a filtered resize read has the maximum alpha.</summary>
    private static bool IsOpaque<TPixel>(PixelStorage source, ResizePlan plan)
        where TPixel : unmanaged
    {
        var xWeights = plan.XWeights!;
        var yWeights = plan.YWeights!;
        for (var y = yWeights.First; y <= yWeights.Last; y++)
        {
            if (PixelConverter.IndexOfNonOpaque<TPixel>(GetRow<TPixel>(source, y).Slice(xWeights.First, xWeights.Last - xWeights.First + 1)) >= 0)
                return false;
        }

        return true;
    }

    /// <summary>Gets the number of working samples per pixel: the colors only for <see cref="WorkingAlpha.Opaque"/>.</summary>
    private static int GetChannels(ResizePlan plan, WorkingAlpha alpha) => alpha == WorkingAlpha.Opaque ? 3 : plan.Channels;

    private static void ResizeBand<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, WorkingAlpha alpha, int band, Action<int, bool>? observer, CancellationToken cancellationToken)
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
        else if (plan.XFixed is not null && typeof(TSample) == typeof(byte) && alpha is WorkingAlpha.None or WorkingAlpha.Opaque)
        {
            FixedPointResampler.Gather(source, destination, plan, plan.GetScratch(band), start, end, cancellationToken);
        }
        else if (plan.Strategy == ResizeVerticalStrategy.Gather)
        {
            Gather<TPixel, TSample>(source, destination, plan, alpha, plan.GetScratch(band), start, end, cancellationToken);
        }
        else
        {
            Scatter<TPixel, TSample>(source, destination, plan, alpha, plan.GetScratch(band), start, end, cancellationToken);
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

            var output = GetRow<TPixel>(destination, dy);
            if (dy > start && ys[dy] == ys[dy - 1])
            {
                // Upscaling repeats source rows: the previous output row is this one already (alpha zeroing included)
                GetRow<TPixel>(destination, dy - 1).CopyTo(output);
                continue;
            }

            var input = GetRow<TPixel>(source, ys[dy]);
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
    private static void Gather<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, WorkingAlpha alpha, ResizeScratch scratch, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var yWeights = plan.YWeights!;
        var ringSize = scratch.RowCount;

        // The rows of the scratch are sized for every sample of a pixel; colors only use the start of each one
        var length = plan.Geometry.OutputSize.Width * GetChannels(plan, alpha);
        var accumulator = scratch.ExtraRow[..length];
        var next = yWeights.GetStart(start);

        // The taps are summed a block of samples at a time (SetWeightedSum) by groups that read at most MaxRowsPerPass ring
        // rows (the prefetching of the processor follows a limited number of streams), each group adding to the sums of the
        // previous ones; the scalar reference of the tests adds a row per tap
        var blocked = !TestForceScalar && IsFmaAccelerated && (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated);
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
                LoadAndResampleRow<TPixel, TSample>(source, next, plan, alpha, scratch, scratch.GetSlot(next % ringSize)[..length]);
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

                    SetWeightedSum(rows, 0, length, offsets[..count], weights.Slice(k, count), accumulate: k is not 0);
                }
            }
            else
            {
                SetScaled(accumulator, scratch.GetSlot(first % ringSize)[..length], weights[0]);
                for (var k = 1; k < weights.Length; k++)
                {
                    AddScaled(accumulator, scratch.GetSlot((first + k) % ringSize)[..length], weights[k]);
                }
            }

            WorkingSamples.Store(accumulator, unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(destination, dy))), alpha, plan.Transfer, plan.MaxValue);
        }
    }

    /// <summary>Output rows <paramref name="start"/> to <paramref name="end"/> - 1, scattering each source row of their windows into per-output accumulators.</summary>
    private static void Scatter<TPixel, TSample>(PixelStorage source, PixelStorage destination, ResizePlan plan, WorkingAlpha alpha, ResizeScratch scratch, int start, int end, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var yWeights = plan.YWeights!;
        var slots = scratch.RowCount;
        var length = plan.Geometry.OutputSize.Width * GetChannels(plan, alpha);
        var row = scratch.ExtraRow[..length];
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
            LoadAndResampleRow<TPixel, TSample>(source, sy, plan, alpha, scratch, row);
            for (var dy = first; dy < next; dy++)
            {
                var windowStart = yWeights.GetStart(dy);
                if (sy == windowStart)
                {
                    SetScaled(scratch.GetSlot(dy % slots)[..length], row, yWeights.GetWeights(dy)[0]);
                }
                else
                {
                    AddScaled(scratch.GetSlot(dy % slots)[..length], row, yWeights.GetWeights(dy)[sy - windowStart]);
                }
            }

            while (first < next && yWeights.GetStart(first) + yWeights.GetCount(first) - 1 == sy)
            {
                WorkingSamples.Store(scratch.GetSlot(first % slots)[..length], unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(destination, first))), alpha, plan.Transfer, plan.MaxValue);
                first++;
            }
        }

        Debug.Assert(first == end);
    }

    /// <summary>Loads the used columns of source row <paramref name="y"/> as working samples and resamples them horizontally into <paramref name="output"/>.</summary>
    private static void LoadAndResampleRow<TPixel, TSample>(PixelStorage source, int y, ResizePlan plan, WorkingAlpha alpha, ResizeScratch scratch, Span<double> output)
        where TPixel : unmanaged
        where TSample : unmanaged
    {
        var xWeights = plan.XWeights!;
        var first = xWeights.First;
        var working = scratch.SourceRow;
        var used = (xWeights.Last - first + 1) * plan.Channels;
        var samples = unsafe(MemoryMarshal.Cast<TPixel, TSample>(GetRow<TPixel>(source, y))).Slice(first * plan.Channels, used);
        WorkingSamples.Load(samples, working[..WorkingSamples.GetWorkingLength(used, alpha)], alpha, plan.Transfer);

        ResampleHorizontal(working, output, xWeights, GetChannels(plan, alpha));
    }

    /// <summary>
    /// Gets the layout of the horizontal weights that <see cref="ResampleHorizontal"/> reads for pixels of
    /// <paramref name="channels"/> samples: 0 for windows of their own length (the scalar loops), otherwise the
    /// <see cref="ResampleWeights.Group"/> of padded windows (<see cref="ResampleWeights.CreatePadded"/>).
    /// </summary>
    internal static int GetHorizontalWeightsGroup(int channels)
    {
        if (TestForceScalar || !Vector128.IsHardwareAccelerated || !IsFmaAccelerated)
            return 0;

        if (channels is not 1)
            return 1;

        return Vector256.IsHardwareAccelerated && Avx.IsSupported ? 4 : 2;
    }

    /// <summary>
    /// Resamples one row of working samples horizontally (<c>output[dx] = 0 + w0 x0 + w1 x1 + ...</c> per sample, every term
    /// added with a fused multiply-add), with the
    /// vector loops when the weights are padded (<see cref="GetHorizontalWeightsGroup"/>). The working row holds the samples
    /// of <see cref="ResampleWeights.PaddedSourceLength"/> pixels from <see cref="ResampleWeights.First"/>, plus one sample
    /// for 3-sample pixels; the samples after <see cref="ResampleWeights.Last"/> are finite (zero).
    /// </summary>
    internal static void ResampleHorizontal(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights, int channels)
    {
        if (xWeights.PaddedCount is 0)
        {
            ResampleHorizontalScalar(working, output, xWeights, channels);
            return;
        }

        Debug.Assert(xWeights.Group == GetHorizontalWeightsGroup(channels) || TestForceScalar);
        switch (channels)
        {
            case 4 when Vector256.IsHardwareAccelerated:
                ResampleHorizontal4Vector256(working, output, xWeights);
                break;
            case 4:
                ResampleHorizontal4Vector128(working, output, xWeights);
                break;
            case 3 when Vector256.IsHardwareAccelerated:
                ResampleHorizontal3Vector256(working, output, xWeights);
                break;
            case 3:
                ResampleHorizontal3Vector128(working, output, xWeights);
                break;
            default:
                Debug.Assert(channels == 1);
                if (xWeights.Group == 4)
                {
                    ResampleHorizontal1Vector256(working, output, xWeights);
                }
                else
                {
                    ResampleHorizontal1Vector128(working, output, xWeights);
                }

                break;
        }
    }

    /// <summary>The scalar reference of <see cref="ResampleHorizontal"/> (windows of their own length, or padded with a <see cref="ResampleWeights.Group"/> of 1).</summary>
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
                        c0 = Math.FusedMultiplyAdd(w, p[0], c0);
                        c1 = Math.FusedMultiplyAdd(w, p[1], c1);
                        c2 = Math.FusedMultiplyAdd(w, p[2], c2);
                        c3 = Math.FusedMultiplyAdd(w, p[3], c3);
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
                        c0 = Math.FusedMultiplyAdd(w, p[0], c0);
                        c1 = Math.FusedMultiplyAdd(w, p[1], c1);
                        c2 = Math.FusedMultiplyAdd(w, p[2], c2);
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
                        c0 = Math.FusedMultiplyAdd(weights[k], input[k], c0);
                    }

                    o[0] = c0;
                    break;
                }
            }
        }
    }

    // The vector loops below compute four output pixels per iteration (eight for gray): each pixel is one chain of dependent
    // additions, so a single pixel at a time waits for the latency of every add. Every window is padded to the same number of
    // taps (ResampleWeights.CreatePadded), so the pixels of an iteration share one loop without tails. Each accumulator sums
    // the taps of its padded window in increasing order: the zero weights of the padding leave the sums of the scalar loop
    // unchanged, bit for bit (see ResampleWeights).

    /// <summary>Gets the samples of the padded window of output pixel <paramref name="index"/> (<paramref name="length"/> samples from its first pixel).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref double GetPaddedWindow(ReadOnlySpan<double> working, scoped ResampleWeights.WindowTable table, int index, int first, int channels, int length)
        => ref unsafe(MemoryMarshal.GetReference(working.Slice((table.GetPaddedStart(index) - first) * channels, length)));

    private static void ResampleHorizontal4Vector256(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var taps = xWeights.PaddedCount;
        var outputLength = xWeights.OutputLength;
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output[..(outputLength * 4)]));
        ref var weights = ref unsafe(MemoryMarshal.GetReference(table.Weights[..(outputLength * taps)]));
        var dx = 0;
        for (; dx + 3 < outputLength; dx += 4)
        {
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 4, 4 * taps);
            ref var input1 = ref GetPaddedWindow(working, table, dx + 1, first, 4, 4 * taps);
            ref var input2 = ref GetPaddedWindow(working, table, dx + 2, first, 4, 4 * taps);
            ref var input3 = ref GetPaddedWindow(working, table, dx + 3, first, 4, 4 * taps);
            ref var w0 = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            ref var w1 = ref unsafe(Unsafe.Add(ref w0, taps));
            ref var w2 = ref unsafe(Unsafe.Add(ref w1, taps));
            ref var w3 = ref unsafe(Unsafe.Add(ref w2, taps));
            var sum0 = Vector256<double>.Zero;
            var sum1 = Vector256<double>.Zero;
            var sum2 = Vector256<double>.Zero;
            var sum3 = Vector256<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                sum0 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w0, k))), unsafe(Vector256.LoadUnsafe(ref input0, (nuint)(4 * k))), sum0);
                sum1 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w1, k))), unsafe(Vector256.LoadUnsafe(ref input1, (nuint)(4 * k))), sum1);
                sum2 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w2, k))), unsafe(Vector256.LoadUnsafe(ref input2, (nuint)(4 * k))), sum2);
                sum3 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w3, k))), unsafe(Vector256.LoadUnsafe(ref input3, (nuint)(4 * k))), sum3);
            }

            unsafe { sum0.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
            unsafe { sum1.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 4)); }
            unsafe { sum2.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 8)); }
            unsafe { sum3.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 12)); }
        }

        for (; dx < outputLength; dx++)
        {
            ref var input = ref GetPaddedWindow(working, table, dx, first, 4, 4 * taps);
            ref var w = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            var sum = Vector256<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                sum = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w, k))), unsafe(Vector256.LoadUnsafe(ref input, (nuint)(4 * k))), sum);
            }

            unsafe { sum.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
        }
    }

    private static void ResampleHorizontal4Vector128(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var taps = xWeights.PaddedCount;
        var outputLength = xWeights.OutputLength;
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output[..(outputLength * 4)]));
        ref var weights = ref unsafe(MemoryMarshal.GetReference(table.Weights[..(outputLength * taps)]));
        var dx = 0;
        for (; dx + 3 < outputLength; dx += 4)
        {
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 4, 4 * taps);
            ref var input1 = ref GetPaddedWindow(working, table, dx + 1, first, 4, 4 * taps);
            ref var input2 = ref GetPaddedWindow(working, table, dx + 2, first, 4, 4 * taps);
            ref var input3 = ref GetPaddedWindow(working, table, dx + 3, first, 4, 4 * taps);
            ref var w0 = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            ref var w1 = ref unsafe(Unsafe.Add(ref w0, taps));
            ref var w2 = ref unsafe(Unsafe.Add(ref w1, taps));
            ref var w3 = ref unsafe(Unsafe.Add(ref w2, taps));
            var low0 = Vector128<double>.Zero;
            var high0 = Vector128<double>.Zero;
            var low1 = Vector128<double>.Zero;
            var high1 = Vector128<double>.Zero;
            var low2 = Vector128<double>.Zero;
            var high2 = Vector128<double>.Zero;
            var low3 = Vector128<double>.Zero;
            var high3 = Vector128<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                var weight0 = Vector128.Create(unsafe(Unsafe.Add(ref w0, k)));
                var weight1 = Vector128.Create(unsafe(Unsafe.Add(ref w1, k)));
                var weight2 = Vector128.Create(unsafe(Unsafe.Add(ref w2, k)));
                var weight3 = Vector128.Create(unsafe(Unsafe.Add(ref w3, k)));
                low0 = Vector128.FusedMultiplyAdd(weight0, unsafe(Vector128.LoadUnsafe(ref input0, (nuint)(4 * k))), low0);
                high0 = Vector128.FusedMultiplyAdd(weight0, unsafe(Vector128.LoadUnsafe(ref input0, (nuint)((4 * k) + 2))), high0);
                low1 = Vector128.FusedMultiplyAdd(weight1, unsafe(Vector128.LoadUnsafe(ref input1, (nuint)(4 * k))), low1);
                high1 = Vector128.FusedMultiplyAdd(weight1, unsafe(Vector128.LoadUnsafe(ref input1, (nuint)((4 * k) + 2))), high1);
                low2 = Vector128.FusedMultiplyAdd(weight2, unsafe(Vector128.LoadUnsafe(ref input2, (nuint)(4 * k))), low2);
                high2 = Vector128.FusedMultiplyAdd(weight2, unsafe(Vector128.LoadUnsafe(ref input2, (nuint)((4 * k) + 2))), high2);
                low3 = Vector128.FusedMultiplyAdd(weight3, unsafe(Vector128.LoadUnsafe(ref input3, (nuint)(4 * k))), low3);
                high3 = Vector128.FusedMultiplyAdd(weight3, unsafe(Vector128.LoadUnsafe(ref input3, (nuint)((4 * k) + 2))), high3);
            }

            unsafe { low0.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
            unsafe { high0.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 2)); }
            unsafe { low1.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 4)); }
            unsafe { high1.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 6)); }
            unsafe { low2.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 8)); }
            unsafe { high2.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 10)); }
            unsafe { low3.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 12)); }
            unsafe { high3.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 14)); }
        }

        for (; dx < outputLength; dx++)
        {
            ref var input = ref GetPaddedWindow(working, table, dx, first, 4, 4 * taps);
            ref var w = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            var low = Vector128<double>.Zero;
            var high = Vector128<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                var weight = Vector128.Create(unsafe(Unsafe.Add(ref w, k)));
                low = Vector128.FusedMultiplyAdd(weight, unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * k))), low);
                high = Vector128.FusedMultiplyAdd(weight, unsafe(Vector128.LoadUnsafe(ref input, (nuint)((4 * k) + 2))), high);
            }

            unsafe { low.StoreUnsafe(ref outputStart, (nuint)(4 * dx)); }
            unsafe { high.StoreUnsafe(ref outputStart, (nuint)((4 * dx) + 2)); }
        }
    }

    /// <summary>
    /// 3-sample pixels: each vector loads one pixel and the first sample of the next one (the working row has one extra
    /// element, see <see cref="ResizePlan"/>); the fourth lane is ignored, so the three samples are those of the scalar loop.
    /// A pixel is stored with its fourth lane, overwritten by the next pixel, except the last one of an iteration.
    /// </summary>
    private static void ResampleHorizontal3Vector256(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var taps = xWeights.PaddedCount;
        var outputLength = xWeights.OutputLength;
        output = output[..(outputLength * 3)];
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
        ref var weights = ref unsafe(MemoryMarshal.GetReference(table.Weights[..(outputLength * taps)]));
        var dx = 0;
        for (; dx + 3 < outputLength; dx += 4)
        {
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 3, (3 * taps) + 1);
            ref var input1 = ref GetPaddedWindow(working, table, dx + 1, first, 3, (3 * taps) + 1);
            ref var input2 = ref GetPaddedWindow(working, table, dx + 2, first, 3, (3 * taps) + 1);
            ref var input3 = ref GetPaddedWindow(working, table, dx + 3, first, 3, (3 * taps) + 1);
            ref var w0 = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            ref var w1 = ref unsafe(Unsafe.Add(ref w0, taps));
            ref var w2 = ref unsafe(Unsafe.Add(ref w1, taps));
            ref var w3 = ref unsafe(Unsafe.Add(ref w2, taps));
            var sum0 = Vector256<double>.Zero;
            var sum1 = Vector256<double>.Zero;
            var sum2 = Vector256<double>.Zero;
            var sum3 = Vector256<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                sum0 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w0, k))), unsafe(Vector256.LoadUnsafe(ref input0, (nuint)(3 * k))), sum0);
                sum1 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w1, k))), unsafe(Vector256.LoadUnsafe(ref input1, (nuint)(3 * k))), sum1);
                sum2 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w2, k))), unsafe(Vector256.LoadUnsafe(ref input2, (nuint)(3 * k))), sum2);
                sum3 = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w3, k))), unsafe(Vector256.LoadUnsafe(ref input3, (nuint)(3 * k))), sum3);
            }

            unsafe { sum0.StoreUnsafe(ref outputStart, (nuint)(3 * dx)); }
            unsafe { sum1.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 3)); }
            unsafe { sum2.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 6)); }
            var o = output.Slice((3 * dx) + 9, 3);
            o[0] = sum3.GetElement(0);
            o[1] = sum3.GetElement(1);
            o[2] = sum3.GetElement(2);
        }

        for (; dx < outputLength; dx++)
        {
            ref var input = ref GetPaddedWindow(working, table, dx, first, 3, (3 * taps) + 1);
            ref var w = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            var sum = Vector256<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                sum = Vector256.FusedMultiplyAdd(Vector256.Create(unsafe(Unsafe.Add(ref w, k))), unsafe(Vector256.LoadUnsafe(ref input, (nuint)(3 * k))), sum);
            }

            var o = output.Slice(3 * dx, 3);
            o[0] = sum.GetElement(0);
            o[1] = sum.GetElement(1);
            o[2] = sum.GetElement(2);
        }
    }

    /// <summary>
    /// 3-sample pixels in two 128-bit vectors: the first two samples, then the third and the first sample of the next pixel
    /// (the working row has one extra element, see <see cref="ResizePlan"/>), whose lane is ignored, so the three samples
    /// are those of the scalar loop. A pixel is stored with its fourth lane, overwritten by the next pixel, except the last
    /// one of an iteration.
    /// </summary>
    private static void ResampleHorizontal3Vector128(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var taps = xWeights.PaddedCount;
        var outputLength = xWeights.OutputLength;
        output = output[..(outputLength * 3)];
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
        ref var weights = ref unsafe(MemoryMarshal.GetReference(table.Weights[..(outputLength * taps)]));
        var dx = 0;
        for (; dx + 3 < outputLength; dx += 4)
        {
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 3, (3 * taps) + 1);
            ref var input1 = ref GetPaddedWindow(working, table, dx + 1, first, 3, (3 * taps) + 1);
            ref var input2 = ref GetPaddedWindow(working, table, dx + 2, first, 3, (3 * taps) + 1);
            ref var input3 = ref GetPaddedWindow(working, table, dx + 3, first, 3, (3 * taps) + 1);
            ref var w0 = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            ref var w1 = ref unsafe(Unsafe.Add(ref w0, taps));
            ref var w2 = ref unsafe(Unsafe.Add(ref w1, taps));
            ref var w3 = ref unsafe(Unsafe.Add(ref w2, taps));
            var low0 = Vector128<double>.Zero;
            var high0 = Vector128<double>.Zero;
            var low1 = Vector128<double>.Zero;
            var high1 = Vector128<double>.Zero;
            var low2 = Vector128<double>.Zero;
            var high2 = Vector128<double>.Zero;
            var low3 = Vector128<double>.Zero;
            var high3 = Vector128<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                var weight0 = Vector128.Create(unsafe(Unsafe.Add(ref w0, k)));
                var weight1 = Vector128.Create(unsafe(Unsafe.Add(ref w1, k)));
                var weight2 = Vector128.Create(unsafe(Unsafe.Add(ref w2, k)));
                var weight3 = Vector128.Create(unsafe(Unsafe.Add(ref w3, k)));
                low0 = Vector128.FusedMultiplyAdd(weight0, unsafe(Vector128.LoadUnsafe(ref input0, (nuint)(3 * k))), low0);
                high0 = Vector128.FusedMultiplyAdd(weight0, unsafe(Vector128.LoadUnsafe(ref input0, (nuint)((3 * k) + 2))), high0);
                low1 = Vector128.FusedMultiplyAdd(weight1, unsafe(Vector128.LoadUnsafe(ref input1, (nuint)(3 * k))), low1);
                high1 = Vector128.FusedMultiplyAdd(weight1, unsafe(Vector128.LoadUnsafe(ref input1, (nuint)((3 * k) + 2))), high1);
                low2 = Vector128.FusedMultiplyAdd(weight2, unsafe(Vector128.LoadUnsafe(ref input2, (nuint)(3 * k))), low2);
                high2 = Vector128.FusedMultiplyAdd(weight2, unsafe(Vector128.LoadUnsafe(ref input2, (nuint)((3 * k) + 2))), high2);
                low3 = Vector128.FusedMultiplyAdd(weight3, unsafe(Vector128.LoadUnsafe(ref input3, (nuint)(3 * k))), low3);
                high3 = Vector128.FusedMultiplyAdd(weight3, unsafe(Vector128.LoadUnsafe(ref input3, (nuint)((3 * k) + 2))), high3);
            }

            unsafe { low0.StoreUnsafe(ref outputStart, (nuint)(3 * dx)); }
            unsafe { high0.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 2)); }
            unsafe { low1.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 3)); }
            unsafe { high1.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 5)); }
            unsafe { low2.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 6)); }
            unsafe { high2.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 8)); }
            unsafe { low3.StoreUnsafe(ref outputStart, (nuint)((3 * dx) + 9)); }
            output[(3 * dx) + 11] = high3.GetElement(0);
        }

        for (; dx < outputLength; dx++)
        {
            ref var input = ref GetPaddedWindow(working, table, dx, first, 3, (3 * taps) + 1);
            ref var w = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            var low = Vector128<double>.Zero;
            var high = Vector128<double>.Zero;
            for (var k = 0; k < taps; k++)
            {
                var weight = Vector128.Create(unsafe(Unsafe.Add(ref w, k)));
                low = Vector128.FusedMultiplyAdd(weight, unsafe(Vector128.LoadUnsafe(ref input, (nuint)(3 * k))), low);
                high = Vector128.FusedMultiplyAdd(weight, unsafe(Vector128.LoadUnsafe(ref input, (nuint)((3 * k) + 2))), high);
            }

            var o = output.Slice(3 * dx, 3);
            o[0] = low.GetElement(0);
            o[1] = low.GetElement(1);
            o[2] = high.GetElement(0);
        }
    }

    /// <summary>
    /// 1-sample pixels, two per 128-bit vector (one lane per output pixel), four vectors per iteration. The weights of two
    /// consecutive output pixels are interleaved (<see cref="ResampleWeights.Group"/> 2); the samples of two taps of both
    /// windows are loaded as two vectors, one per pixel, and transposed into one vector per tap.
    /// </summary>
    private static void ResampleHorizontal1Vector128(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        Debug.Assert(xWeights.Group == 2);
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var taps = xWeights.PaddedCount;
        var outputLength = xWeights.OutputLength;
        output = output[..outputLength];
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
        ref var weights = ref unsafe(MemoryMarshal.GetReference(table.Weights[..(((outputLength + 1) & ~1) * taps)]));
        var dx = 0;
        for (; dx + 7 < outputLength; dx += 8)
        {
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 1, taps);
            ref var input1 = ref GetPaddedWindow(working, table, dx + 1, first, 1, taps);
            ref var input2 = ref GetPaddedWindow(working, table, dx + 2, first, 1, taps);
            ref var input3 = ref GetPaddedWindow(working, table, dx + 3, first, 1, taps);
            ref var input4 = ref GetPaddedWindow(working, table, dx + 4, first, 1, taps);
            ref var input5 = ref GetPaddedWindow(working, table, dx + 5, first, 1, taps);
            ref var input6 = ref GetPaddedWindow(working, table, dx + 6, first, 1, taps);
            ref var input7 = ref GetPaddedWindow(working, table, dx + 7, first, 1, taps);
            ref var w0 = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            ref var w1 = ref unsafe(Unsafe.Add(ref w0, 2 * taps));
            ref var w2 = ref unsafe(Unsafe.Add(ref w1, 2 * taps));
            ref var w3 = ref unsafe(Unsafe.Add(ref w2, 2 * taps));
            var sum0 = Vector128<double>.Zero;
            var sum1 = Vector128<double>.Zero;
            var sum2 = Vector128<double>.Zero;
            var sum3 = Vector128<double>.Zero;
            for (var k = 0; k < taps; k += 2)
            {
                sum0 = Add1Vector128(sum0, ref input0, ref input1, ref w0, k);
                sum1 = Add1Vector128(sum1, ref input2, ref input3, ref w1, k);
                sum2 = Add1Vector128(sum2, ref input4, ref input5, ref w2, k);
                sum3 = Add1Vector128(sum3, ref input6, ref input7, ref w3, k);
            }

            unsafe { sum0.StoreUnsafe(ref outputStart, (nuint)dx); }
            unsafe { sum1.StoreUnsafe(ref outputStart, (nuint)(dx + 2)); }
            unsafe { sum2.StoreUnsafe(ref outputStart, (nuint)(dx + 4)); }
            unsafe { sum3.StoreUnsafe(ref outputStart, (nuint)(dx + 6)); }
        }

        for (; dx < outputLength; dx += 2)
        {
            // The last pixel may be alone in its group: the other lane has zero weights and reads the same window
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 1, taps);
            ref var input1 = ref GetPaddedWindow(working, table, Math.Min(dx + 1, outputLength - 1), first, 1, taps);
            ref var w = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            var sum = Vector128<double>.Zero;
            for (var k = 0; k < taps; k += 2)
            {
                sum = Add1Vector128(sum, ref input0, ref input1, ref w, k);
            }

            output[dx] = sum.GetElement(0);
            if (dx + 1 < outputLength)
            {
                output[dx + 1] = sum.GetElement(1);
            }
        }
    }

    /// <summary>Adds taps <paramref name="k"/> and <paramref name="k"/> + 1 of two 1-sample pixels (one per lane) to <paramref name="sum"/>, in that order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> Add1Vector128(Vector128<double> sum, ref double input0, ref double input1, ref double weights, int k)
    {
        var row0 = unsafe(Vector128.LoadUnsafe(ref input0, (nuint)k));
        var row1 = unsafe(Vector128.LoadUnsafe(ref input1, (nuint)k));
        sum = Vector128.FusedMultiplyAdd(unsafe(Vector128.LoadUnsafe(ref weights, (nuint)(2 * k))), InterleaveLower(row0, row1), sum);
        sum = Vector128.FusedMultiplyAdd(unsafe(Vector128.LoadUnsafe(ref weights, (nuint)((2 * k) + 2))), InterleaveUpper(row0, row1), sum);
        return sum;
    }

    /// <summary>Gets <c>(left[0], right[0])</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> InterleaveLower(Vector128<double> left, Vector128<double> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.ZipLow(left, right);

        if (Sse2.IsSupported)
            return Sse2.UnpackLow(left, right);

        return Vector128.Create(left.ToScalar(), right.ToScalar());
    }

    /// <summary>Gets <c>(left[1], right[1])</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> InterleaveUpper(Vector128<double> left, Vector128<double> right)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.ZipHigh(left, right);

        if (Sse2.IsSupported)
            return Sse2.UnpackHigh(left, right);

        return Vector128.Create(left.GetElement(1), right.GetElement(1));
    }

    /// <summary>
    /// 1-sample pixels, four per 256-bit vector (one lane per output pixel), two vectors per iteration. The weights of four
    /// consecutive output pixels are interleaved (<see cref="ResampleWeights.Group"/> 4); the samples of four taps of the
    /// four windows are loaded by halves and transposed into one vector per tap.
    /// </summary>
    private static void ResampleHorizontal1Vector256(ReadOnlySpan<double> working, Span<double> output, ResampleWeights xWeights)
    {
        Debug.Assert(xWeights.Group == 4);
        var first = xWeights.First;
        var table = xWeights.GetWindowTable();
        var taps = xWeights.PaddedCount;
        var outputLength = xWeights.OutputLength;
        output = output[..outputLength];
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
        ref var weights = ref unsafe(MemoryMarshal.GetReference(table.Weights[..(((outputLength + 3) & ~3) * taps)]));
        var dx = 0;
        for (; dx + 7 < outputLength; dx += 8)
        {
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 1, taps);
            ref var input1 = ref GetPaddedWindow(working, table, dx + 1, first, 1, taps);
            ref var input2 = ref GetPaddedWindow(working, table, dx + 2, first, 1, taps);
            ref var input3 = ref GetPaddedWindow(working, table, dx + 3, first, 1, taps);
            ref var input4 = ref GetPaddedWindow(working, table, dx + 4, first, 1, taps);
            ref var input5 = ref GetPaddedWindow(working, table, dx + 5, first, 1, taps);
            ref var input6 = ref GetPaddedWindow(working, table, dx + 6, first, 1, taps);
            ref var input7 = ref GetPaddedWindow(working, table, dx + 7, first, 1, taps);
            ref var w0 = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            ref var w1 = ref unsafe(Unsafe.Add(ref w0, 4 * taps));
            var sum0 = Vector256<double>.Zero;
            var sum1 = Vector256<double>.Zero;
            for (var k = 0; k < taps; k += 4)
            {
                sum0 = Add1Vector256(sum0, ref input0, ref input1, ref input2, ref input3, ref w0, k);
                sum1 = Add1Vector256(sum1, ref input4, ref input5, ref input6, ref input7, ref w1, k);
            }

            unsafe { sum0.StoreUnsafe(ref outputStart, (nuint)dx); }
            unsafe { sum1.StoreUnsafe(ref outputStart, (nuint)(dx + 4)); }
        }

        for (; dx < outputLength; dx += 4)
        {
            // The last pixels may not fill their group: the other lanes have zero weights and read the window of the last pixel
            var last = outputLength - 1;
            ref var input0 = ref GetPaddedWindow(working, table, dx, first, 1, taps);
            ref var input1 = ref GetPaddedWindow(working, table, Math.Min(dx + 1, last), first, 1, taps);
            ref var input2 = ref GetPaddedWindow(working, table, Math.Min(dx + 2, last), first, 1, taps);
            ref var input3 = ref GetPaddedWindow(working, table, Math.Min(dx + 3, last), first, 1, taps);
            ref var w = ref unsafe(Unsafe.Add(ref weights, dx * taps));
            var sum = Vector256<double>.Zero;
            for (var k = 0; k < taps; k += 4)
            {
                sum = Add1Vector256(sum, ref input0, ref input1, ref input2, ref input3, ref w, k);
            }

            for (var i = 0; i < 4 && dx + i < outputLength; i++)
            {
                output[dx + i] = sum.GetElement(i);
            }
        }
    }

    /// <summary>Adds taps <paramref name="k"/> to <paramref name="k"/> + 3 of four 1-sample pixels (one per lane) to <paramref name="sum"/>, in that order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Add1Vector256(Vector256<double> sum, ref double input0, ref double input1, ref double input2, ref double input3, ref double weights, int k)
    {
        // (x0[k], x0[k + 1], x2[k], x2[k + 1]) and (x1[k], x1[k + 1], x3[k], x3[k + 1]) interleave into the taps k and k + 1
        // of the four pixels, and the same two taps later into k + 2 and k + 3
        var even = Vector256.Create(unsafe(Vector128.LoadUnsafe(ref input0, (nuint)k)), unsafe(Vector128.LoadUnsafe(ref input2, (nuint)k)));
        var odd = Vector256.Create(unsafe(Vector128.LoadUnsafe(ref input1, (nuint)k)), unsafe(Vector128.LoadUnsafe(ref input3, (nuint)k)));
        sum = Vector256.FusedMultiplyAdd(unsafe(Vector256.LoadUnsafe(ref weights, (nuint)(4 * k))), Avx.UnpackLow(even, odd), sum);
        sum = Vector256.FusedMultiplyAdd(unsafe(Vector256.LoadUnsafe(ref weights, (nuint)((4 * k) + 4))), Avx.UnpackHigh(even, odd), sum);
        even = Vector256.Create(unsafe(Vector128.LoadUnsafe(ref input0, (nuint)(k + 2))), unsafe(Vector128.LoadUnsafe(ref input2, (nuint)(k + 2))));
        odd = Vector256.Create(unsafe(Vector128.LoadUnsafe(ref input1, (nuint)(k + 2))), unsafe(Vector128.LoadUnsafe(ref input3, (nuint)(k + 2))));
        sum = Vector256.FusedMultiplyAdd(unsafe(Vector256.LoadUnsafe(ref weights, (nuint)((4 * k) + 8))), Avx.UnpackLow(even, odd), sum);
        sum = Vector256.FusedMultiplyAdd(unsafe(Vector256.LoadUnsafe(ref weights, (nuint)((4 * k) + 12))), Avx.UnpackHigh(even, odd), sum);
        return sum;
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
        if (!TestForceScalar && Vector512.IsHardwareAccelerated)
        {
            // The remaining samples go through the 256-bit loop below
            ref var accumulatorStart = ref unsafe(MemoryMarshal.GetReference(accumulator));
            ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row[..accumulator.Length]));
            var w = Vector512.Create(weight);
            for (; i + 8 <= accumulator.Length; i += 8)
            {
                unsafe { (Vector512<double>.Zero + (w * Vector512.LoadUnsafe(ref rowStart, (nuint)i))).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }

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
    /// sample, but summed in registers for a block of samples instead of one pass over the accumulator per row. The first
    /// term is a fused multiply-add too (<c>fma(w0, x0, 0)</c> is the rounded product of <see cref="SetScaled"/>).
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
        var vectorized = IsFmaAccelerated;
        if (vectorized && Vector512.IsHardwareAccelerated)
        {
            // Blocks of 32 samples; the remaining ones go through the 256-bit loop below
            for (; i + 32 <= length; i += 32)
            {
                ref var target = ref unsafe(Unsafe.Add(ref start, accumulatorOffset + i));
                var sum0 = accumulate ? unsafe(Vector512.LoadUnsafe(ref target)) : Vector512<double>.Zero;
                var sum1 = accumulate ? unsafe(Vector512.LoadUnsafe(ref target, 8)) : Vector512<double>.Zero;
                var sum2 = accumulate ? unsafe(Vector512.LoadUnsafe(ref target, 16)) : Vector512<double>.Zero;
                var sum3 = accumulate ? unsafe(Vector512.LoadUnsafe(ref target, 24)) : Vector512<double>.Zero;
                var w = Vector512.Create(weights[0]);
                ref var row = ref unsafe(Unsafe.Add(ref start, offsets[0] + i));
                sum0 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row)), sum0);
                sum1 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row, 8)), sum1);
                sum2 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row, 16)), sum2);
                sum3 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row, 24)), sum3);
                for (var t = 1; t < offsets.Length; t++)
                {
                    w = Vector512.Create(weights[t]);
                    row = ref unsafe(Unsafe.Add(ref start, offsets[t] + i));
                    sum0 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row)), sum0);
                    sum1 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row, 8)), sum1);
                    sum2 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row, 16)), sum2);
                    sum3 = Vector512.FusedMultiplyAdd(w, unsafe(Vector512.LoadUnsafe(ref row, 24)), sum3);
                }

                unsafe { sum0.StoreUnsafe(ref target); }
                unsafe { sum1.StoreUnsafe(ref target, 8); }
                unsafe { sum2.StoreUnsafe(ref target, 16); }
                unsafe { sum3.StoreUnsafe(ref target, 24); }
            }
        }

        if (vectorized && Vector256.IsHardwareAccelerated)
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
                sum0 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row)), sum0);
                sum1 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row, 4)), sum1);
                sum2 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row, 8)), sum2);
                sum3 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row, 12)), sum3);
                for (var t = 1; t < offsets.Length; t++)
                {
                    w = Vector256.Create(weights[t]);
                    row = ref unsafe(Unsafe.Add(ref start, offsets[t] + i));
                    sum0 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row)), sum0);
                    sum1 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row, 4)), sum1);
                    sum2 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row, 8)), sum2);
                    sum3 = Vector256.FusedMultiplyAdd(w, unsafe(Vector256.LoadUnsafe(ref row, 12)), sum3);
                }

                unsafe { sum0.StoreUnsafe(ref target); }
                unsafe { sum1.StoreUnsafe(ref target, 4); }
                unsafe { sum2.StoreUnsafe(ref target, 8); }
                unsafe { sum3.StoreUnsafe(ref target, 12); }
            }
        }
        else if (vectorized && Vector128.IsHardwareAccelerated)
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
                sum0 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row)), sum0);
                sum1 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row, 2)), sum1);
                sum2 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row, 4)), sum2);
                sum3 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row, 6)), sum3);
                for (var t = 1; t < offsets.Length; t++)
                {
                    w = Vector128.Create(weights[t]);
                    row = ref unsafe(Unsafe.Add(ref start, offsets[t] + i));
                    sum0 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row)), sum0);
                    sum1 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row, 2)), sum1);
                    sum2 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row, 4)), sum2);
                    sum3 = Vector128.FusedMultiplyAdd(w, unsafe(Vector128.LoadUnsafe(ref row, 6)), sum3);
                }

                unsafe { sum0.StoreUnsafe(ref target); }
                unsafe { sum1.StoreUnsafe(ref target, 2); }
                unsafe { sum2.StoreUnsafe(ref target, 4); }
                unsafe { sum3.StoreUnsafe(ref target, 6); }
            }
        }

        for (; i < length; i++)
        {
            var sum = Math.FusedMultiplyAdd(weights[0], buffer[offsets[0] + i], accumulate ? buffer[accumulatorOffset + i] : 0d);
            for (var t = 1; t < offsets.Length; t++)
            {
                sum = Math.FusedMultiplyAdd(weights[t], buffer[offsets[t] + i], sum);
            }

            buffer[accumulatorOffset + i] = sum;
        }
    }

    /// <summary>
    /// Adds <c>weight * row</c> to the accumulator, sample by sample, with a fused multiply-add (vectorized when supported;
    /// the same operations).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void AddScaled(Span<double> accumulator, ReadOnlySpan<double> row, double weight)
    {
        Debug.Assert(accumulator.Length == row.Length);
        var i = 0;
        var scalar = TestForceScalar || !IsFmaAccelerated;
        if (!scalar && Vector512.IsHardwareAccelerated)
        {
            // The remaining samples go through the 256-bit loop below
            ref var accumulatorStart = ref unsafe(MemoryMarshal.GetReference(accumulator));
            ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row[..accumulator.Length]));
            var w = Vector512.Create(weight);
            for (; i + 8 <= accumulator.Length; i += 8)
            {
                unsafe { Vector512.FusedMultiplyAdd(w, Vector512.LoadUnsafe(ref rowStart, (nuint)i), Vector512.LoadUnsafe(ref accumulatorStart, (nuint)i)).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }

        if (scalar)
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
                unsafe { Vector256.FusedMultiplyAdd(w, Vector256.LoadUnsafe(ref rowStart, (nuint)i), Vector256.LoadUnsafe(ref accumulatorStart, (nuint)i)).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            ref var accumulatorStart = ref unsafe(MemoryMarshal.GetReference(accumulator));
            ref var rowStart = ref unsafe(MemoryMarshal.GetReference(row[..accumulator.Length]));
            var w = Vector128.Create(weight);
            for (; i + 2 <= accumulator.Length; i += 2)
            {
                unsafe { Vector128.FusedMultiplyAdd(w, Vector128.LoadUnsafe(ref rowStart, (nuint)i), Vector128.LoadUnsafe(ref accumulatorStart, (nuint)i)).StoreUnsafe(ref accumulatorStart, (nuint)i); }
            }
        }

        for (; i < accumulator.Length; i++)
        {
            accumulator[i] = Math.FusedMultiplyAdd(weight, row[i], accumulator[i]);
        }
    }
}
