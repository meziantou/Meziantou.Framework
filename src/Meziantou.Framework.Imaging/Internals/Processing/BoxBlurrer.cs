using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The in-place box blur of <see cref="ImageProcessingExtensions.BoxBlur(Image, int, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>:
/// every working sample becomes the mean of the <c>n^2</c> samples of the box of side <c>n = 2 r + 1</c> around it, in a
/// constant number of additions per sample whatever the radius.
/// </summary>
/// <remarks>
/// <para>
/// The sums of the windows of <c>n</c> values of a sequence are computed by blocks of <c>n</c> values, at fixed positions:
/// a window starting at offset <c>q</c> of block <c>k</c> is the sum of the end of block <c>k</c> from <c>q</c> (a suffix,
/// <c>x_q + (x_{q+1} + (... + x_{n-1}))</c>) plus the sum of the start of block <c>k + 1</c> (a prefix,
/// <c>((y_0 + y_1) + ...) + y_{q-1}</c>, empty at <c>q = 0</c>). Each window costs three additions and adds only its own
/// values, without the subtractions of a running sum: a sample that is not a number or an infinity only reaches the
/// windows that contain it, and the order of the additions depends on the position of the window, not on where a
/// computation starts.
/// </para>
/// <para>
/// Every row is loaded as working samples (<see cref="WorkingSamples"/>) padded with <c>r</c> pixels on each side by the
/// edge mode, and summed horizontally, block positions counted from the first padded pixel. The vertical sums combine
/// these rows the same way, block positions counted from the row <c>r</c> rows above the image: the window holds the
/// <c>n</c> rows of the output row, each slot either a row of the next block or the suffix sum of the current block that
/// replaced it in place, and the prefix sum is a separate row. The mean is the sum divided by <c>n^2</c>, stored once.
/// </para>
/// <para>
/// The frame is rewritten in place, band by band. A band loads the rows it reads above its first row into its window, and
/// those below its last one into its bottom halo, for every band before any row is written. A band that starts inside a
/// block computes the suffix and prefix sums of that block from the rows of its window, which are those of the block
/// computation: the result does not depend on the number of bands.
/// </para>
/// <para>
/// A frame not taller than the radius (<see cref="BoxBlurPlan.UsesRowCache"/>) would load each of its rows many times
/// into the window, each time summing its padded row again. Its rows are instead summed horizontally once, into a row
/// cache, before any row is rewritten; the vertical sums then read the rows of each block from the cache, with the same
/// additions in the same order: the suffix sums of the rows of a block from its last row, kept for the output rows of the
/// band, and the prefix sums of the next block, one row at a time. The results are those of the window, bit for bit.
/// </para>
/// <para>
/// The weights add up to 1: a frame with alpha whose pixels all have the maximum alpha, and whose edge mode reads no
/// transparent pixel, is filtered as its three colors and keeps the maximum alpha. Cancellation is observed between rows.
/// </para>
/// </remarks>
internal static class BoxBlurrer
{
    public static void Blur<TPixel>(scoped in PixelLease lease, BoxBlurPlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        if (storage.Width != plan.Size.Width || storage.Height != plan.Size.Height)
            throw new InvalidOperationException("The storage does not have the size the blur was prepared for.");

        var alpha = Convolver.GetBlurWorkingAlpha<TPixel>(storage, normalized: true, plan.Alpha, plan.EdgeMode);
        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            Blur<ushort>(storage, plan, alpha, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            Blur<byte>(storage, plan, alpha, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaFloat))
        {
            Blur<float>(storage, plan, alpha, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            Blur<Half>(storage, plan, alpha, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>
    /// Sets <paramref name="sums"/> to the sums of the windows of <paramref name="diameter"/> pixels of a padded row:
    /// <c>sums[x]</c> adds the pixels <c>x</c> to <c>x + diameter - 1</c> of <paramref name="padded"/>, sample by sample,
    /// by the block sums of the type (blocks at multiples of <paramref name="diameter"/>).
    /// </summary>
    /// <param name="padded">The padded row: <c>width + diameter - 1</c> pixels.</param>
    /// <param name="suffix">Scratch of the length of <paramref name="padded"/>.</param>
    /// <param name="sums">The sums: <paramref name="width"/> pixels.</param>
    /// <param name="width">The number of windows.</param>
    /// <param name="diameter">The number of pixels of a window.</param>
    /// <param name="channels">The number of samples per pixel.</param>
    internal static void SumWindows(ReadOnlySpan<double> padded, Span<double> suffix, Span<double> sums, int width, int diameter, int channels)
    {
        // The suffix sums of the blocks that windows start in: x_i + (x_{i+1} + ...), from the last sample of the block
        for (var block = 0; block < width; block += diameter)
        {
            var first = block * channels;
            var last = ((block + diameter) * channels) - 1;
            for (var k = last; k > last - channels; k--)
            {
                suffix[k] = padded[k];
            }

            for (var k = last - channels; k >= first; k--)
            {
                suffix[k] = padded[k] + suffix[k + channels];
            }
        }

        // A window starting at offset q of its block: its suffix plus the prefix of the next block, whose samples are
        // diameter pixels to the right of the samples of the block
        Span<double> prefix = stackalloc double[channels];
        var offset = 0;
        for (var x = 0; x < width; x++)
        {
            var i = x * channels;
            if (offset == 0)
            {
                suffix.Slice(i, channels).CopyTo(sums.Slice(i, channels));
            }
            else
            {
                var next = (x + diameter - 1) * channels;
                for (var c = 0; c < channels; c++)
                {
                    prefix[c] = offset == 1 ? padded[next + c] : prefix[c] + padded[next + c];
                    sums[i + c] = suffix[i + c] + prefix[c];
                }
            }

            offset = offset == diameter - 1 ? 0 : offset + 1;
        }
    }

    /// <summary>Gets the number of working samples per pixel: the colors only for <see cref="WorkingAlpha.Opaque"/>.</summary>
    private static int GetChannels(BoxBlurPlan plan, WorkingAlpha alpha) => alpha == WorkingAlpha.Opaque ? 3 : plan.Channels;

    private static void Blur<TSample>(PixelStorage storage, BoxBlurPlan plan, WorkingAlpha alpha, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.UsesRowCache)
        {
            BlurFromRowCache<TSample>(storage, plan, alpha, cancellationToken);
            return;
        }

        // Every band reads its halos from the original pixels: load them all before the first row is rewritten
        var workers = plan.Workers;
        for (var band = 0; band < workers; band++)
        {
            LoadHalos<TSample>(storage, plan, alpha, band);
        }

        if (workers == 1)
        {
            BlurBand<TSample>(storage, plan, alpha, 0, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => BlurBand<TSample>(storage, plan, alpha, band, cancellationToken));
    }

    /// <summary>Sums every row of the frame horizontally into the row cache, then blurs every band from it.</summary>
    private static void BlurFromRowCache<TSample>(PixelStorage storage, BoxBlurPlan plan, WorkingAlpha alpha, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var workers = plan.Workers;
        if (workers == 1)
        {
            CacheRows<TSample>(storage, plan, alpha, 0, cancellationToken);
            BlurCachedBand<TSample>(storage, plan, alpha, 0, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => CacheRows<TSample>(storage, plan, alpha, band, cancellationToken));
        RowBands.RunParallel(workers, band => BlurCachedBand<TSample>(storage, plan, alpha, band, cancellationToken));
    }

    /// <summary>Sets the rows of the row cache of a band to the horizontal window sums of the image rows.</summary>
    private static void CacheRows<TSample>(PixelStorage storage, BoxBlurPlan plan, WorkingAlpha alpha, int band, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var scratch = plan.GetScratch(band);
        var length = plan.Size.Width * GetChannels(plan, alpha);
        for (var y = plan.GetBandStart(band); y < plan.GetBandStart(band + 1); y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            SumRow<TSample>(storage, y, plan, alpha, scratch, plan.GetCachedRow(y, length));
        }
    }

    /// <summary>
    /// Blurs the rows of a band from the row cache, block by block (blocks of <c>n</c> rows counted from the row <c>r</c>
    /// rows above the image, as in the window): the suffix sums of the rows of the block, from its last row down to the first
    /// output row of the band in the block (those below the last output row of the band in a running sum), then each output
    /// row adds the prefix sum of the next block.
    /// </summary>
    private static void BlurCachedBand<TSample>(PixelStorage storage, BoxBlurPlan plan, WorkingAlpha alpha, int band, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var scratch = plan.GetScratch(band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        var radius = plan.Radius;
        var diameter = plan.Diameter;
        var length = plan.Size.Width * GetChannels(plan, alpha);
        var prefix = scratch.Slice(plan.SuffixRows * plan.RowLength, length);
        var output = scratch.Slice((plan.SuffixRows + 1) * plan.RowLength, length);
        var count = (double)diameter * diameter;
        for (var block = start / diameter; block * diameter < end; block++)
        {
            // The rows of block k are k n - r to k n + r; its output rows are k n to k n + n - 1
            var first = block * diameter;
            var firstOffset = Math.Max(start - first, 0);
            var endOffset = Math.Min(end - first, diameter);
            var running = GetSlot(plan, alpha, scratch, endOffset - firstOffset);
            var next = running;
            for (var offset = diameter - 1; offset >= firstOffset; offset--)
            {
                if (offset % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var row = GetCachedRow(plan, first - radius + offset, length);
                var sums = offset >= endOffset ? running : GetSlot(plan, alpha, scratch, offset - firstOffset);
                if (offset == diameter - 1)
                {
                    row.CopyTo(sums);
                }
                else
                {
                    Add(row, next, sums);
                }

                next = sums;
            }

            // The rows of the next block start at k n + r + 1
            var nextBlock = first + diameter - radius;
            for (var offset = firstOffset; offset < endOffset; offset++)
            {
                var y = first + offset;
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var sums = GetSlot(plan, alpha, scratch, offset - firstOffset);
                if (offset == 0)
                {
                    Divide(sums, count, output);
                }
                else
                {
                    if (offset == 1 || offset == firstOffset)
                    {
                        GetCachedRow(plan, nextBlock, length).CopyTo(prefix);
                        for (var i = 1; i < offset; i++)
                        {
                            Add(prefix, GetCachedRow(plan, nextBlock + i, length), prefix);
                        }
                    }
                    else
                    {
                        Add(prefix, GetCachedRow(plan, nextBlock + offset - 1, length), prefix);
                    }

                    AddAndDivide(sums, prefix, count, output);
                }

                WorkingSamples.Store(output, GetSamples<TSample>(storage, y), alpha, plan.Transfer, plan.MaxValue);
            }
        }
    }

    /// <summary>Gets the horizontal sums of row <paramref name="v"/> (mapped by the edge mode, which is not <see cref="ConvolutionEdgeMode.Zero"/>) from the row cache.</summary>
    private static Span<double> GetCachedRow(BoxBlurPlan plan, int v, int length) => plan.GetCachedRow(plan.MapRow(v), length);

    /// <summary>Loads the summed rows a band reads above its first row (into its window) and below its last row (into its bottom halo).</summary>
    private static void LoadHalos<TSample>(PixelStorage storage, BoxBlurPlan plan, WorkingAlpha alpha, int band)
        where TSample : unmanaged
    {
        var scratch = plan.GetScratch(band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        for (var i = 0; i < plan.Radius; i++)
        {
            LoadRow<TSample>(storage, start - plan.Radius + i, plan, alpha, scratch, GetWindowRow(plan, alpha, scratch, start - plan.Radius + i));
            LoadRow<TSample>(storage, end + i, plan, alpha, scratch, GetHaloRow(plan, alpha, scratch, i));
        }
    }

    private static void BlurBand<TSample>(PixelStorage storage, BoxBlurPlan plan, WorkingAlpha alpha, int band, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        var scratch = plan.GetScratch(band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        var radius = plan.Radius;
        var diameter = plan.Diameter;
        var length = plan.Size.Width * GetChannels(plan, alpha);
        var prefix = scratch.Slice((diameter + radius) * plan.RowLength, length);
        var output = scratch.Slice((diameter + radius + 1) * plan.RowLength, length);
        var count = (double)diameter * diameter;

        // The window of the first row: the rows above the band are there already
        for (var v = start; v <= start + radius; v++)
        {
            EnsureRow<TSample>(storage, v, plan, alpha, scratch, end);
        }

        BeginBlock(plan, alpha, scratch, start % diameter, prefix);
        for (var y = start; y < end; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The window slot of the row that leaves the window is the one of the row that enters it
            var offset = y % diameter;
            if (y > start)
            {
                EnsureRow<TSample>(storage, y + radius, plan, alpha, scratch, end);
                if (offset == 0)
                {
                    BeginBlock(plan, alpha, scratch, 0, prefix);
                }
                else if (offset == 1)
                {
                    GetSlot(plan, alpha, scratch, 0).CopyTo(prefix);
                }
                else
                {
                    Add(prefix, GetSlot(plan, alpha, scratch, offset - 1));
                }
            }

            var sums = GetSlot(plan, alpha, scratch, offset);
            if (offset == 0)
            {
                Divide(sums, count, output);
            }
            else
            {
                AddAndDivide(sums, prefix, count, output);
            }

            WorkingSamples.Store(output, GetSamples<TSample>(storage, y), alpha, plan.Transfer, plan.MaxValue);
        }
    }

    /// <summary>
    /// Starts the vertical block sums at window slot <paramref name="offset"/>: the slots from the last one down to
    /// <paramref name="offset"/> become the suffix sums of their block, in place, and <paramref name="prefix"/> the sum of
    /// the slots before <paramref name="offset"/> (rows of the next block, kept as they are).
    /// </summary>
    private static void BeginBlock(BoxBlurPlan plan, WorkingAlpha alpha, Span<double> scratch, int offset, Span<double> prefix)
    {
        for (var slot = plan.Diameter - 2; slot >= offset; slot--)
        {
            Add(GetSlot(plan, alpha, scratch, slot), GetSlot(plan, alpha, scratch, slot + 1));
        }

        if (offset > 0)
        {
            GetSlot(plan, alpha, scratch, 0).CopyTo(prefix);
            for (var slot = 1; slot < offset; slot++)
            {
                Add(prefix, GetSlot(plan, alpha, scratch, slot));
            }
        }
    }

    /// <summary>Puts the summed row <paramref name="v"/> of the band into its window slot: from the image inside the band, from the bottom halo below it.</summary>
    private static void EnsureRow<TSample>(PixelStorage storage, int v, BoxBlurPlan plan, WorkingAlpha alpha, Span<double> scratch, int end)
        where TSample : unmanaged
    {
        var destination = GetWindowRow(plan, alpha, scratch, v);
        if (v < end)
        {
            LoadRow<TSample>(storage, v, plan, alpha, scratch, destination);
        }
        else
        {
            GetHaloRow(plan, alpha, scratch, v - end).CopyTo(destination);
        }
    }

    /// <summary>Loads row <paramref name="v"/> (mapped by the edge mode) padded by the radius on each side, and sets <paramref name="destination"/> to its horizontal window sums.</summary>
    private static void LoadRow<TSample>(PixelStorage storage, int v, BoxBlurPlan plan, WorkingAlpha alpha, Span<double> scratch, Span<double> destination)
        where TSample : unmanaged
    {
        var source = plan.MapRow(v);
        if (source < 0)
        {
            destination.Clear();
            return;
        }

        SumRow<TSample>(storage, source, plan, alpha, scratch, destination);
    }

    /// <summary>Loads image row <paramref name="source"/> padded by the radius on each side, and sets <paramref name="destination"/> to its horizontal window sums.</summary>
    private static void SumRow<TSample>(PixelStorage storage, int source, BoxBlurPlan plan, WorkingAlpha alpha, Span<double> scratch, Span<double> destination)
        where TSample : unmanaged
    {
        var channels = GetChannels(plan, alpha);
        var width = plan.Size.Width;
        var radius = plan.Radius;
        var paddedOffset = plan.WorkingRows * plan.RowLength;
        var padded = scratch.Slice(paddedOffset, (width + (2 * radius)) * channels);
        var suffix = scratch.Slice(paddedOffset + plan.PaddedLength, padded.Length);
        WorkingSamples.Load<TSample>(GetSamples<TSample>(storage, source), padded.Slice(radius * channels, width * channels), alpha, plan.Transfer);
        for (var i = 0; i < radius; i++)
        {
            FillPadding(padded, i, plan.MapColumn(i - radius), radius, channels);
            FillPadding(padded, radius + width + i, plan.MapColumn(width + i), radius, channels);
        }

        SumWindows(padded, suffix, destination, width, plan.Diameter, channels);
    }

    private static void FillPadding(Span<double> padded, int pixel, int sourceColumn, int radius, int channels)
    {
        var destination = padded.Slice(pixel * channels, channels);
        if (sourceColumn < 0)
        {
            destination.Clear();
        }
        else
        {
            padded.Slice((radius + sourceColumn) * channels, channels).CopyTo(destination);
        }
    }

    /// <summary>Gets the window slot of row <paramref name="v"/>, which is at least <c>-radius</c>: its offset in its block.</summary>
    private static Span<double> GetWindowRow(BoxBlurPlan plan, WorkingAlpha alpha, Span<double> scratch, int v)
        => GetSlot(plan, alpha, scratch, (v + plan.Radius) % plan.Diameter);

    private static Span<double> GetSlot(BoxBlurPlan plan, WorkingAlpha alpha, Span<double> scratch, int slot)
        => scratch.Slice(slot * plan.RowLength, plan.Size.Width * GetChannels(plan, alpha));

    /// <summary>Gets a row of the bottom halo: 0 is the row right after the last row of the band.</summary>
    private static Span<double> GetHaloRow(BoxBlurPlan plan, WorkingAlpha alpha, Span<double> scratch, int index)
        => scratch.Slice((plan.Diameter + index) * plan.RowLength, plan.Size.Width * GetChannels(plan, alpha));

    /// <summary>Adds <paramref name="source"/> to <paramref name="destination"/>, element by element (the same IEEE additions with or without vectors).</summary>
    private static void Add(Span<double> destination, ReadOnlySpan<double> source)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= destination.Length - Vector<double>.Count; i += Vector<double>.Count)
            {
                (new Vector<double>(destination[i..]) + new Vector<double>(source[i..])).CopyTo(destination[i..]);
            }
        }

        for (; i < destination.Length; i++)
        {
            destination[i] += source[i];
        }
    }

    /// <summary>Sets <paramref name="destination"/>, which may be <paramref name="right"/>, to <paramref name="left"/> plus <paramref name="right"/>, element by element (the additions of <see cref="Add(Span{double}, ReadOnlySpan{double})"/>).</summary>
    private static void Add(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> destination)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= destination.Length - Vector<double>.Count; i += Vector<double>.Count)
            {
                (new Vector<double>(left[i..]) + new Vector<double>(right[i..])).CopyTo(destination[i..]);
            }
        }

        for (; i < destination.Length; i++)
        {
            destination[i] = left[i] + right[i];
        }
    }

    private static void Divide(ReadOnlySpan<double> sums, double count, Span<double> destination)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var divisor = new Vector<double>(count);
            for (; i <= sums.Length - Vector<double>.Count; i += Vector<double>.Count)
            {
                (new Vector<double>(sums[i..]) / divisor).CopyTo(destination[i..]);
            }
        }

        for (; i < sums.Length; i++)
        {
            destination[i] = sums[i] / count;
        }
    }

    private static void AddAndDivide(ReadOnlySpan<double> sums, ReadOnlySpan<double> prefix, double count, Span<double> destination)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var divisor = new Vector<double>(count);
            for (; i <= sums.Length - Vector<double>.Count; i += Vector<double>.Count)
            {
                ((new Vector<double>(sums[i..]) + new Vector<double>(prefix[i..])) / divisor).CopyTo(destination[i..]);
            }
        }

        for (; i < sums.Length; i++)
        {
            destination[i] = (sums[i] + prefix[i]) / count;
        }
    }

    private static Span<TSample> GetSamples<TSample>(PixelStorage storage, int y)
        where TSample : unmanaged
        => unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(y)));
}
