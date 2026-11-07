using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The in-place convolution of <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>
///.
/// </summary>
/// <remarks>
/// <para>
/// Rows are loaded into double-precision working samples (<see cref="WorkingSamples"/>), padded on both sides with the
/// pixels the kernel reads outside the image, so that every weight of a kernel row adds one whole shifted row to the
/// accumulator: <c>0 + w0 x0 + w1 x1 + ...</c>, kernel rows from top to bottom and weights from left to right, zero
/// weights skipped. The row additions are those of the resampler (<see cref="Resampler.AddScaled"/>): every term after the
/// first one added with a fused multiply-add, vectorized when supported, so the result is bit-identical to the scalar
/// loops.
/// </para>
/// <para>
/// The frame is rewritten in place, band by band. A band keeps the working rows it still needs in a ring before it
/// overwrites them, and reads the rows outside its range (those of the neighboring bands, and whatever the edge mode
/// designates beyond the image) from halos loaded for every band before any row is written. Every output sample is
/// therefore computed from original pixels by the same operations in the same order whatever the number of bands.
/// </para>
/// <para>
/// A separable plan (the Gaussian blur, the unsharp mask, a separable matrix) filters every row it loads by its row
/// weights, with the same row additions (zero weights skipped), into the ring or the halo, and its kernel of one column
/// combines those rows: the result is the two-dimensional sum <c>0 + v0 (0 + w0 x00 + w1 x01 + ...) + v1 (...) + ...</c>,
/// without any rounding to the storage precision in between. The weights of a blur add up to 1: a frame with alpha whose
/// pixels all have the maximum alpha, and whose edge mode reads no transparent pixel beyond the image (any but
/// <see cref="ConvolutionEdgeMode.Zero"/>), is filtered as three colors per pixel, straight
/// (<see cref="WorkingAlpha.Opaque"/>), and keeps the maximum alpha.
/// </para>
/// <para>
/// An unsharp mask reloads every row from the image right before it is rewritten (it is still original) and combines it
/// with its blur.
/// </para>
/// <para>
/// An edge detector sums several kernels of the same dimensions over the same working rows, into one accumulator each,
/// then combines the sums sample by sample (<see cref="KernelCombination"/>). A bias is added to the color working samples
/// last, right before they are stored.
/// </para>
/// <para>Cancellation is observed between row bands; the rows already written stay written.</para>
/// </remarks>
internal static class Convolver
{
    // The largest number of taps whose positions, weights and rows are kept on the stack (16 KB) to be summed a block at a time
    private const int MaxBlockedTaps = 1024;

    // The largest number of rows that the taps summed in one pass over the accumulator read
    private const int MaxRowsPerPass = 4;

    private static readonly AsyncLocal<Action<int, bool>?> TestBandObserverValue = new();

    /// <summary>Gets or sets a test hook called when a row band starts (<see langword="true"/>) and ends, on the worker thread.</summary>
    internal static Action<int, bool>? TestBandObserver
    {
        get => TestBandObserverValue.Value;
        set => TestBandObserverValue.Value = value;
    }

    public static void Convolve<TPixel>(scoped in PixelLease lease, ConvolutionPlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        // The lease is held by the caller for the whole operation; the workers use the leased storage directly (each
        // reads and writes the rows of its own band)
        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        if (storage.Width != plan.Size.Width || storage.Height != plan.Size.Height)
            throw new InvalidOperationException("The storage does not have the size the convolution was prepared for.");

        // The blur of an opaque frame is the blur of its colors: alpha stays at the maximum
        var alpha = GetWorkingAlpha<TPixel>(storage, plan);
        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            Convolve<ushort>(storage, plan, alpha, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            Convolve<byte>(storage, plan, alpha, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaFloat))
        {
            Convolve<float>(storage, plan, alpha, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            Convolve<Half>(storage, plan, alpha, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>
    /// Gets how alpha takes part in the working samples of a frame: <see cref="WorkingAlpha.Opaque"/> for a blur plan
    /// (whose weights add up to 1) filtered premultiplied, whose edge mode reads no pixel beyond the image, of a frame whose
    /// pixels all have the maximum alpha; otherwise <see cref="ConvolutionPlan.Alpha"/>.
    /// </summary>
    internal static WorkingAlpha GetWorkingAlpha<TPixel>(PixelStorage storage, ConvolutionPlan plan)
        where TPixel : unmanaged
        => GetBlurWorkingAlpha<TPixel>(storage, plan.IsBlur, plan.Alpha, plan.EdgeMode);

    /// <summary>
    /// Gets how alpha takes part in the working samples of a frame filtered by an operation whose weights add up to 1
    /// (<paramref name="normalized"/>): <see cref="WorkingAlpha.Opaque"/> when its samples are filtered premultiplied, its
    /// edge mode reads no pixel beyond the image and every pixel has the maximum alpha; otherwise <paramref name="alpha"/>.
    /// </summary>
    internal static WorkingAlpha GetBlurWorkingAlpha<TPixel>(PixelStorage storage, bool normalized, WorkingAlpha alpha, ConvolutionEdgeMode edgeMode)
        where TPixel : unmanaged
        => normalized && alpha == WorkingAlpha.Premultiplied && edgeMode != ConvolutionEdgeMode.Zero && IsOpaque<TPixel>(storage) ? WorkingAlpha.Opaque : alpha;

    /// <summary>Gets a value indicating whether every pixel of a frame has the maximum alpha.</summary>
    private static bool IsOpaque<TPixel>(PixelStorage storage)
        where TPixel : unmanaged
    {
        for (var y = 0; y < storage.Height; y++)
        {
            if (PixelConverter.IndexOfNonOpaque<TPixel>(unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y)))) >= 0)
                return false;
        }

        return true;
    }

    /// <summary>Gets the number of working samples per pixel: the colors only for <see cref="WorkingAlpha.Opaque"/>.</summary>
    private static int GetChannels(ConvolutionPlan plan, WorkingAlpha alpha) => alpha == WorkingAlpha.Opaque ? 3 : plan.Channels;

    private static void Convolve<TSample>(PixelStorage storage, ConvolutionPlan plan, WorkingAlpha alpha, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Every band reads its halos from the original pixels: load them all before the first row is rewritten
        var workers = plan.Workers;
        for (var band = 0; band < workers; band++)
        {
            LoadHalos<TSample>(storage, plan, alpha, band);
        }

        var observer = TestBandObserver;
        if (workers == 1)
        {
            ConvolveBand<TSample>(storage, plan, alpha, 0, observer, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => ConvolveBand<TSample>(storage, plan, alpha, band, observer, cancellationToken));
    }

    /// <summary>
    /// Loads the image rows that a band reads above its first row and below its last one, once each however many positions
    /// read them (absent rows contribute nothing and are not loaded).
    /// </summary>
    private static void LoadHalos<TSample>(PixelStorage storage, ConvolutionPlan plan, WorkingAlpha alpha, int band)
        where TSample : unmanaged
    {
        var scratch = plan.GetScratch(band);
        var halo = scratch.Halo;
        for (var i = 0; i < halo.RowCount; i++)
        {
            LoadRow<TSample>(storage, halo.GetImageRow(i), plan, alpha, scratch, scratch.GetHaloRowOffset(i));
        }
    }

    private static void ConvolveBand<TSample>(PixelStorage storage, ConvolutionPlan plan, WorkingAlpha alpha, int band, Action<int, bool>? observer, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        observer?.Invoke(band, true);
        var scratch = plan.GetScratch(band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        var kernelWidth = plan.KernelWidth;
        var kernelHeight = plan.KernelHeight;
        var centerY = plan.CenterY;
        var channels = GetChannels(plan, alpha);

        // The rows of the scratch are sized for every sample of a pixel; colors only use the start of each one
        var length = plan.Size.Width * channels;
        var accumulator = scratch.Accumulator[..length];

        // The taps of a row are summed a block of samples at a time (Resampler.SetWeightedSum) when the kernel is small enough
        // for their list to be on the stack; otherwise, and for the scalar reference of the tests, a row is added per tap
        var blocked = !Resampler.TestForceScalar && Resampler.IsFmaAccelerated && (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated) && kernelWidth * kernelHeight <= MaxBlockedTaps;
        Span<int> tapOffsets = stackalloc int[blocked ? kernelWidth * kernelHeight : 0];
        Span<double> tapWeights = stackalloc double[blocked ? kernelWidth * kernelHeight : 0];
        Span<int> tapRows = stackalloc int[blocked ? kernelWidth * kernelHeight : 0];
        var next = start; // the next row of the band to load into the ring
        for (var y = start; y < end; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The ring holds the rows of the band among [y - centerY, y + centerY]. A row is loaded before row y is
            // written, and the band has only written rows above y: the ring rows are original pixels.
            var last = Math.Min(y + centerY, end - 1);
            for (; next <= last; next++)
            {
                LoadRow<TSample>(storage, next, plan, alpha, scratch, scratch.GetRingRowOffset(next));
            }

            for (var k = 0; k < plan.KernelCount; k++)
            {
                SumKernel(plan, scratch, plan.GetKernelWeights(k), y, start, end, channels, scratch.GetAccumulatorOffset(k), length, blocked, tapOffsets, tapWeights, tapRows);
            }

            if (plan.Combination != KernelCombination.None)
            {
                Combine(plan, scratch, length);
            }

            if (plan.IsSharpen)
            {
                Sharpen<TSample>(storage, y, plan, alpha, scratch, accumulator);
            }

            if (plan.Bias != 0)
            {
                AddBias(accumulator, alpha, plan.Bias);
            }

            WorkingSamples.Store(accumulator, GetSamples<TSample>(storage, y), alpha, plan.Transfer, plan.MaxValue);
        }

        observer?.Invoke(band, false);
    }

    /// <summary>
    /// Sets the accumulator at <paramref name="accumulatorOffset"/> in the scratch to the sums of one kernel for output row
    /// <paramref name="y"/>: <c>0 + w0 x0 + w1 x1 + ...</c>, kernel rows from top to bottom and weights from left to right,
    /// zero weights skipped.
    /// </summary>
    private static void SumKernel(ConvolutionPlan plan, ConvolutionScratch scratch, ReadOnlySpan<double> weights, int y, int start, int end, int channels, int accumulatorOffset, int length, bool blocked, Span<int> tapOffsets, Span<double> tapWeights, Span<int> tapRows)
    {
        var kernelWidth = plan.KernelWidth;
        var kernelHeight = plan.KernelHeight;
        var centerY = plan.CenterY;
        var buffer = scratch.Buffer;
        var accumulator = buffer.Slice(accumulatorOffset, length);

        // The taps of the row, in the order of the kernel: the position in the scratch of the row they read, shifted by
        // the kernel column (the padded row starts centerX pixels left of the image), and their weight
        var count = 0;
        var empty = true;
        for (var j = 0; j < kernelHeight; j++)
        {
            var rowOffset = GetRowOffset(plan, scratch, y + j - centerY, start, end);
            if (rowOffset < 0)
                continue;

            var kernelRow = weights.Slice(j * kernelWidth, kernelWidth);
            for (var i = 0; i < kernelWidth; i++)
            {
                var weight = kernelRow[i];
                if (weight == 0)
                    continue;

                var offset = rowOffset + (i * channels);
                if (blocked)
                {
                    tapOffsets[count] = offset;
                    tapWeights[count] = weight;
                    tapRows[count] = j;
                    count++;
                }
                else if (empty)
                {
                    // 0 + w0 x0 written directly (no separate clearing pass)
                    Resampler.SetScaled(accumulator, buffer.Slice(offset, length), weight);
                    empty = false;
                }
                else
                {
                    Resampler.AddScaled(accumulator, buffer.Slice(offset, length), weight);
                }
            }
        }

        if (count is not 0)
        {
            // The taps are summed by groups that read at most MaxRowsPerPass rows (the prefetching of the processor follows
            // a limited number of streams), each group adding to the sums of the previous ones
            var first = 0;
            var rows = 1;
            for (var t = 1; t <= count; t++)
            {
                if (t < count && tapRows[t] == tapRows[t - 1])
                    continue;

                if (t < count && rows < MaxRowsPerPass)
                {
                    rows++;
                    continue;
                }

                Resampler.SetWeightedSum(buffer, accumulatorOffset, length, tapOffsets[first..t], tapWeights[first..t], accumulate: first is not 0);
                first = t;
                rows = 1;
            }
        }
        else if (empty)
        {
            accumulator.Clear();
        }
    }

    /// <summary>
    /// Combines the sums of the kernels of an edge detector into the first accumulator, sample by sample: the magnitude
    /// <c>sqrt(a * a + b * b)</c> of two gradients, the largest sum of a compass, or the largest absolute sum. A sum that is
    /// not a number gives a sample that is not a number.
    /// </summary>
    private static void Combine(ConvolutionPlan plan, ConvolutionScratch scratch, int length)
    {
        var buffer = scratch.Buffer;
        var result = buffer.Slice(scratch.GetAccumulatorOffset(0), length);
        switch (plan.Combination)
        {
            case KernelCombination.Magnitude:
            {
                var other = buffer.Slice(scratch.GetAccumulatorOffset(1), length);
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = Math.Sqrt((result[i] * result[i]) + (other[i] * other[i]));
                }

                break;
            }

            case KernelCombination.Maximum:
                for (var k = 1; k < plan.KernelCount; k++)
                {
                    var other = buffer.Slice(scratch.GetAccumulatorOffset(k), length);
                    for (var i = 0; i < result.Length; i++)
                    {
                        result[i] = Math.Max(result[i], other[i]);
                    }
                }

                break;

            case KernelCombination.MaximumAbsolute:
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = Math.Abs(result[i]);
                }

                for (var k = 1; k < plan.KernelCount; k++)
                {
                    var other = buffer.Slice(scratch.GetAccumulatorOffset(k), length);
                    for (var i = 0; i < result.Length; i++)
                    {
                        result[i] = Math.Max(result[i], Math.Abs(other[i]));
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Adds the bias of a plan to the color working samples of a row: multiplied by the filtered alpha when the colors are
    /// premultiplied (so that the stored color, divided by it, gets the bias), as is otherwise. Alpha never gets it (a
    /// preserved alpha is not stored).
    /// </summary>
    private static void AddBias(Span<double> values, WorkingAlpha alpha, double bias)
    {
        if (alpha == WorkingAlpha.Premultiplied)
        {
            for (var i = 0; i < values.Length; i += 4)
            {
                var a = values[i + 3];
                values[i] = Math.FusedMultiplyAdd(bias, a, values[i]);
                values[i + 1] = Math.FusedMultiplyAdd(bias, a, values[i + 1]);
                values[i + 2] = Math.FusedMultiplyAdd(bias, a, values[i + 2]);
            }

            return;
        }

        foreach (ref var value in values)
        {
            value += bias;
        }
    }

    // The position in the scratch of the working row of image row source, or -1 when the edge mode reads no row there
    private static int GetRowOffset(ConvolutionPlan plan, ConvolutionScratch scratch, int source, int start, int end)
    {
        if (source < start)
            return scratch.GetTopHaloOffset(source - (start - plan.CenterY));

        if (source >= end)
            return scratch.GetBottomHaloOffset(source - end);

        return scratch.GetRingRowOffset(source);
    }

    /// <summary>
    /// Loads image row <paramref name="y"/> into the ring or halo row at <paramref name="offset"/> in the scratch: padded
    /// for the kernel or, for a separable plan, padded for the row weights in the input row, then filtered by them.
    /// </summary>
    private static void LoadRow<TSample>(PixelStorage storage, int y, ConvolutionPlan plan, WorkingAlpha alpha, ConvolutionScratch scratch, int offset)
        where TSample : unmanaged
    {
        var channels = GetChannels(plan, alpha);
        if (!plan.IsSeparable)
        {
            LoadPaddedRow<TSample>(storage, y, plan, alpha, scratch.Buffer.Slice(offset, (plan.Size.Width + plan.KernelWidth - 1) * channels), plan.CenterX);
            return;
        }

        LoadPaddedRow<TSample>(storage, y, plan, alpha, scratch.InputRow[..((plan.Size.Width + plan.RowWeights.Length - 1) * channels)], plan.RowCenterX);
        FilterRow(plan, channels, scratch, offset);
    }

    /// <summary>
    /// Sets the row at <paramref name="offset"/> in the scratch to the input row filtered by the row weights of a separable
    /// plan: <c>0 + w0 x0 + w1 x1 + ...</c> per sample, zero weights skipped as in the kernel (a sample that is not a
    /// number spreads to the same pixels whatever the path), with the row additions of the kernel (all the taps read one
    /// row, so they are summed a block of samples at a time in one pass when their list fits on the stack).
    /// </summary>
    private static void FilterRow(ConvolutionPlan plan, int channels, ConvolutionScratch scratch, int offset)
    {
        var weights = plan.RowWeights;
        var length = plan.Size.Width * channels;
        var buffer = scratch.Buffer;
        var input = scratch.InputOffset;
        var row = buffer.Slice(offset, length);
        if (!Resampler.TestForceScalar && Resampler.IsFmaAccelerated && (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated) && weights.Length <= MaxBlockedTaps)
        {
            // fma(w, x, +0) is the 0 + w x of a first term
            Span<int> offsets = stackalloc int[weights.Length];
            Span<double> nonZeroWeights = stackalloc double[weights.Length];
            var count = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] != 0)
                {
                    offsets[count] = input + (i * channels);
                    nonZeroWeights[count] = weights[i];
                    count++;
                }
            }

            if (count is 0)
            {
                row.Clear();
                return;
            }

            Resampler.SetWeightedSum(buffer, offset, length, offsets[..count], nonZeroWeights[..count], accumulate: false);
            return;
        }

        var empty = true;
        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] == 0)
                continue;

            if (empty)
            {
                Resampler.SetScaled(row, buffer.Slice(input + (i * channels), length), weights[i]);
                empty = false;
            }
            else
            {
                Resampler.AddScaled(row, buffer.Slice(input + (i * channels), length), weights[i]);
            }
        }

        if (empty)
        {
            row.Clear();
        }
    }

    /// <summary>
    /// Combines the blurred working samples of row <paramref name="y"/> with its original ones, reloaded from the image
    /// (the row is rewritten right after): <c>o + amount (o - b)</c> with one rounding (a fused multiply-add), or <c>o</c>
    /// when <c>|o - b|</c> is below the threshold, a fraction of the range of the working sample.
    /// </summary>
    private static void Sharpen<TSample>(PixelStorage storage, int y, ConvolutionPlan plan, WorkingAlpha alpha, ConvolutionScratch scratch, Span<double> blurred)
        where TSample : unmanaged
    {
        var channels = GetChannels(plan, alpha);
        var original = scratch.InputRow[..blurred.Length];
        WorkingSamples.Load<TSample>(GetSamples<TSample>(storage, y), original, alpha, plan.Transfer);

        // The range of a working sample: a color is a code value (0 to max) or a light (0 to 1), multiplied by alpha (0 to
        // max) when premultiplied; alpha is 0 to max
        var color = plan.IsLinear ? 1d : plan.MaxValue;
        Span<double> thresholds = stackalloc double[channels];
        for (var c = 0; c < channels; c++)
        {
            var range = alpha != WorkingAlpha.Premultiplied ? color : c == channels - 1 ? plan.MaxValue : color * plan.MaxValue;
            thresholds[c] = plan.SharpenThreshold * range;
        }

        var amount = plan.SharpenAmount;
        for (var i = 0; i < blurred.Length; i += channels)
        {
            for (var c = 0; c < channels; c++)
            {
                var o = original[i + c];
                var difference = o - blurred[i + c];
                blurred[i + c] = Math.Abs(difference) < thresholds[c] ? o : Math.FusedMultiplyAdd(amount, difference, o);
            }
        }
    }

    /// <summary>
    /// Loads image row <paramref name="y"/> as working samples in the middle of <paramref name="padded"/>, then fills the
    /// <paramref name="centerX"/> pixels read on each side of the image according to the edge mode.
    /// </summary>
    private static void LoadPaddedRow<TSample>(PixelStorage storage, int y, ConvolutionPlan plan, WorkingAlpha alpha, Span<double> padded, int centerX)
        where TSample : unmanaged
    {
        var channels = GetChannels(plan, alpha);
        var width = plan.Size.Width;
        WorkingSamples.Load<TSample>(GetSamples<TSample>(storage, y), padded.Slice(centerX * channels, width * channels), alpha, plan.Transfer);
        for (var i = 0; i < centerX; i++)
        {
            FillPadding(padded, i, plan.MapColumn(i - centerX), centerX, channels);
            FillPadding(padded, centerX + width + i, plan.MapColumn(width + i), centerX, channels);
        }
    }

    private static void FillPadding(Span<double> padded, int pixel, int sourceColumn, int centerX, int channels)
    {
        var destination = padded.Slice(pixel * channels, channels);
        if (sourceColumn < 0)
        {
            destination.Clear();
        }
        else
        {
            padded.Slice((centerX + sourceColumn) * channels, channels).CopyTo(destination);
        }
    }

    private static Span<TSample> GetSamples<TSample>(PixelStorage storage, int y)
        where TSample : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(y)));
}
