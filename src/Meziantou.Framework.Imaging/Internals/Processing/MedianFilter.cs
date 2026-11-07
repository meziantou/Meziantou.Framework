using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The in-place median filter of <see cref="ImageProcessingExtensions.MedianBlur(Image, int, ConvolutionEdgeMode, CancellationToken)"/>
/// and the percentile filter of <see cref="ImageProcessingExtensions.PercentileFilter(Image, int, double, ConvolutionEdgeMode, CancellationToken)"/>:
/// every sample becomes the value of rank <see cref="MedianPlan.Rank"/> of the same sample of the <c>n^2</c> pixels of the
/// square of side <c>n = 2 r + 1</c> around it (<c>(n^2 - 1) / 2</c> for the median: one of them, so nothing is computed or
/// rounded).
/// </summary>
/// <remarks>
/// <para>
/// Samples are compared through keys whose unsigned order is the order of the samples (<see cref="ToKey"/>): integer
/// samples are their own keys, and floating-point samples their bits reordered, with every number that is not a number
/// above the positive infinity.
/// </para>
/// <para>
/// Keys are counted in one histogram per sample of a pixel, as a tree of 16 counters per level of 4 bits (2 levels for
/// 8-bit samples, 4 for 16-bit ones): adding or removing a key updates one counter per level, and the median is found by
/// walking down the tree, at most 16 counters per level. The window moves along the rows in alternate directions and
/// steps down at their ends, so that every step removes and adds one row or one column of the window: the time per sample
/// grows with the radius, not with its square.
/// </para>
/// <para>
/// 32-bit keys (<see cref="RgbaFloat"/>) are counted in two such trees of 16 bits. The first one counts the high 16 bits
/// of every key of the window, which gives the group of keys the median is in and its rank in the group. The second one
/// counts the low 16 bits of the keys of one group only (the <c>active</c> group of the sample), which gives the median.
/// When the median moves to another group, the second tree is rebuilt from the keys of the window: that costs the square
/// of the radius, but only when the median changes groups (65,536 keys per group: for numbers between 0.5 and 1, a group
/// spans 1/256). A window of 9 keys (a radius of 1) is sorted instead: that is faster.
/// </para>
/// <para>
/// The frame is rewritten in place, band by band, with the ring and the halos of the convolution
/// (<see cref="Convolver"/>): every window reads original pixels, whatever the number of bands, and the median does not
/// depend on the order the keys are counted in. Cancellation is observed between rows.
/// </para>
/// </remarks>
internal static class MedianFilter
{
    public static void Apply<TPixel>(scoped in PixelLease lease, MedianPlan plan, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var storage = lease.Storage;
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size.");

        if (storage.Width != plan.Size.Width || storage.Height != plan.Size.Height)
            throw new InvalidOperationException("The storage does not have the size the median filter was prepared for.");

        if (typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16) || typeof(TPixel) == typeof(Rgb48))
        {
            Apply<ushort>(storage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32) || typeof(TPixel) == typeof(Rgb24) || typeof(TPixel) == typeof(Gray8))
        {
            Apply<byte>(storage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaFloat))
        {
            Apply<float>(storage, plan, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(RgbaHalf))
        {
            Apply<Half>(storage, plan, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
    }

    /// <summary>
    /// Gets the key of a sample: the sample itself for <see cref="byte"/> and <see cref="ushort"/>; for <see cref="Half"/>
    /// and <see cref="float"/>, the bits with the sign bit set for positive numbers and all the bits inverted for negative
    /// ones, a number that is not a number being made positive first (so it sorts above the positive infinity, and comes
    /// back positive).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint ToKey<TSample>(TSample sample)
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(byte))
            return unsafe(Unsafe.As<TSample, byte>(ref sample));

        if (typeof(TSample) == typeof(ushort))
            return unsafe(Unsafe.As<TSample, ushort>(ref sample));

        if (typeof(TSample) == typeof(Half))
        {
            uint bits = BitConverter.HalfToUInt16Bits(unsafe(Unsafe.As<TSample, Half>(ref sample)));
            if ((bits & 0x7FFF) > 0x7C00)
            {
                bits &= 0x7FFF;
            }

            return (bits & 0x8000) != 0 ? ~bits & 0xFFFF : bits | 0x8000;
        }

        var value = BitConverter.SingleToUInt32Bits(unsafe(Unsafe.As<TSample, float>(ref sample)));
        if ((value & 0x7FFF_FFFF) > 0x7F80_0000)
        {
            value &= 0x7FFF_FFFF;
        }

        return (value & 0x8000_0000) != 0 ? ~value : value | 0x8000_0000;
    }

    /// <summary>Gets the sample of a key (the inverse of <see cref="ToKey"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TSample FromKey<TSample>(uint key)
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(byte))
        {
            var b = (byte)key;
            return unsafe(Unsafe.As<byte, TSample>(ref b));
        }

        if (typeof(TSample) == typeof(ushort))
        {
            var s = (ushort)key;
            return unsafe(Unsafe.As<ushort, TSample>(ref s));
        }

        if (typeof(TSample) == typeof(Half))
        {
            var h = BitConverter.UInt16BitsToHalf((ushort)((key & 0x8000) != 0 ? key & 0x7FFF : ~key & 0xFFFF));
            return unsafe(Unsafe.As<Half, TSample>(ref h));
        }

        var f = BitConverter.UInt32BitsToSingle((key & 0x8000_0000) != 0 ? key & 0x7FFF_FFFF : ~key);
        return unsafe(Unsafe.As<float, TSample>(ref f));
    }

    private static void Apply<TSample>(PixelStorage storage, MedianPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Every band reads its halos from the original pixels: load them all before the first row is rewritten
        var workers = plan.Workers;
        for (var band = 0; band < workers; band++)
        {
            var keys = plan.GetKeys(band);
            var start = plan.GetBandStart(band);
            var end = plan.GetBandStart(band + 1);
            for (var i = 0; i < plan.Radius; i++)
            {
                LoadRow<TSample>(storage, start - plan.Radius + i, plan, keys.Slice((plan.Diameter + i) * plan.PaddedLength, plan.PaddedLength));
                LoadRow<TSample>(storage, end + i, plan, keys.Slice((plan.Diameter + plan.Radius + i) * plan.PaddedLength, plan.PaddedLength));
            }
        }

        // The histograms of the keys: the loops are compiled for each size of key
        if (typeof(TSample) == typeof(byte))
        {
            FilterBands<TSample, NarrowHistograms>(storage, plan, cancellationToken);
        }
        else if (typeof(TSample) == typeof(float))
        {
            FilterBands<TSample, SplitHistograms>(storage, plan, cancellationToken);
        }
        else
        {
            FilterBands<TSample, WideHistograms>(storage, plan, cancellationToken);
        }
    }

    private static void FilterBands<TSample, THistograms>(PixelStorage storage, MedianPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
        where THistograms : struct, IHistograms
    {
        if (plan.Workers == 1)
        {
            FilterBand<TSample, THistograms>(storage, plan, 0, cancellationToken);
            return;
        }

        RowBands.RunParallel(plan.Workers, band => FilterBand<TSample, THistograms>(storage, plan, band, cancellationToken));
    }

    private static void FilterBand<TSample, THistograms>(PixelStorage storage, MedianPlan plan, int band, CancellationToken cancellationToken)
        where TSample : unmanaged
        where THistograms : struct, IHistograms
    {
        var keys = plan.GetKeys(band);
        var counts = plan.GetCounts(band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        var radius = plan.Radius;
        var diameter = plan.Diameter;
        var width = plan.Size.Width;
        var channels = plan.Channels;
        var output = keys.Slice((diameter + (2 * radius)) * plan.PaddedLength, width * channels);
        var rank = plan.Rank;
        Span<int> rows = diameter <= 1024 ? stackalloc int[diameter] : new int[diameter];

        // The group of keys each low-bits tree counts (32-bit keys), none yet
        Span<int> active = stackalloc int[channels];
        active.Fill(-1);

        // The rows of the band in the window of its first row
        for (var y = start; y <= start + radius && y < end; y++)
        {
            LoadRow<TSample>(storage, y, plan, GetRingRow(plan, keys, y));
        }

        try
        {
            for (var y = start; y < end; y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (plan.SortsWindows)
                {
                    if (y > start && y + radius < end)
                    {
                        LoadRow<TSample>(storage, y + radius, plan, GetRingRow(plan, keys, y + radius));
                    }

                    GetWindowRows(plan, start, end, y, rows);
                    SortWindows(plan, keys, rows, unsafe(MemoryMarshal.Cast<int, uint>(counts)), output, rank);
                }
                else if (y == start)
                {
                    GetWindowRows(plan, start, end, y, rows);
                    for (var j = 0; j < diameter; j++)
                    {
                        Count<THistograms>(plan, counts, active, keys.Slice(rows[j], diameter * channels), 1);
                    }

                    FilterRow<THistograms>(plan, keys, counts, active, rows, output, rank, rightward: true);
                }
                else
                {
                    // Step down at the column where the previous row ended: the top row of the window leaves it, then its
                    // ring row receives the row that enters it
                    var rightward = ((y - start) & 1) == 0;
                    var column = rightward ? 0 : (width - 1) * channels;
                    GetWindowRows(plan, start, end, y - 1, rows);
                    Count<THistograms>(plan, counts, active, keys.Slice(rows[0] + column, diameter * channels), -1);
                    if (y + radius < end)
                    {
                        LoadRow<TSample>(storage, y + radius, plan, GetRingRow(plan, keys, y + radius));
                    }

                    GetWindowRows(plan, start, end, y, rows);
                    Count<THistograms>(plan, counts, active, keys.Slice(rows[diameter - 1] + column, diameter * channels), 1);
                    FilterRow<THistograms>(plan, keys, counts, active, rows, output, rank, rightward);
                }

                var samples = unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(y)));
                for (var i = 0; i < output.Length; i++)
                {
                    samples[i] = FromKey<TSample>(output[i]);
                }
            }
        }
        finally
        {
            // The scratch serves the next frame
            if (!plan.SortsWindows)
            {
                counts.Clear();
            }
        }
    }

    /// <summary>
    /// Sets the medians of a row, the window moving one column at a time in the given direction from the column where the
    /// histograms are (the first or the last one): each step removes the column that leaves the window and adds the one
    /// that enters it.
    /// </summary>
    private static void FilterRow<THistograms>(MedianPlan plan, Span<uint> keys, Span<int> counts, Span<int> active, ReadOnlySpan<int> rows, Span<uint> output, int rank, bool rightward)
        where THistograms : struct, IHistograms
    {
        var width = plan.Size.Width;
        var channels = plan.Channels;
        var diameter = plan.Diameter;
        var histogramLength = plan.HistogramLength;
        for (var step = 0; step < width; step++)
        {
            var x = rightward ? step : width - 1 - step;
            if (step > 0)
            {
                // Rightward, column x - 1 leaves and padded column x + diameter - 1 enters; leftward, padded column
                // x + diameter leaves and x enters
                // A key that leaves and enters the same histogram changes nothing (flat areas)
                var leaving = (rightward ? x - 1 : x + diameter) * channels;
                var entering = (rightward ? x + diameter - 1 : x) * channels;
                for (var j = 0; j < rows.Length; j++)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        var removed = keys[rows[j] + leaving + c];
                        var added = keys[rows[j] + entering + c];
                        if (removed != added)
                        {
                            THistograms.Update(counts, c * histogramLength, active, c, removed, -1);
                            THistograms.Update(counts, c * histogramLength, active, c, added, 1);
                        }
                    }
                }
            }

            for (var c = 0; c < channels; c++)
            {
                output[(x * channels) + c] = THistograms.Select(plan, keys, counts, active, rows, x, c, rank);
            }
        }
    }

    /// <summary>Sets the medians of a row by sorting the keys of every window (small windows of 32-bit keys).</summary>
    private static void SortWindows(MedianPlan plan, ReadOnlySpan<uint> keys, ReadOnlySpan<int> rows, Span<uint> window, Span<uint> output, int rank)
    {
        var channels = plan.Channels;
        var diameter = plan.Diameter;
        window = window[..(diameter * diameter)];
        for (var x = 0; x < plan.Size.Width; x++)
        {
            for (var c = 0; c < channels; c++)
            {
                var count = 0;
                foreach (var row in rows)
                {
                    for (var i = 0; i < diameter; i++)
                    {
                        window[count++] = keys[row + ((x + i) * channels) + c];
                    }
                }

                window.Sort();
                output[(x * channels) + c] = window[rank];
            }
        }
    }

    /// <summary>Adds (<paramref name="delta"/> 1) or removes (-1) the keys of consecutive pixels to the histograms of their samples.</summary>
    private static void Count<THistograms>(MedianPlan plan, Span<int> counts, ReadOnlySpan<int> active, ReadOnlySpan<uint> keys, int delta)
        where THistograms : struct, IHistograms
    {
        var channels = plan.Channels;
        for (var i = 0; i < keys.Length; i += channels)
        {
            for (var c = 0; c < channels; c++)
            {
                THistograms.Update(counts, c * plan.HistogramLength, active, c, keys[i + c], delta);
            }
        }
    }

    /// <summary>Adds <paramref name="delta"/> to the counter of a 16-bit key at every level of the tree at <paramref name="histogram"/>: levels of 16, 256, 4096 and 65,536 counters.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateWide(Span<int> counts, int histogram, uint key, int delta)
    {
        counts[histogram + (int)(key >> 12)] += delta;
        counts[histogram + 16 + (int)(key >> 8)] += delta;
        counts[histogram + 272 + (int)(key >> 4)] += delta;
        counts[histogram + 4368 + (int)key] += delta;
    }

    /// <summary>
    /// Gets the key of rank <paramref name="rank"/> (from 0) of the tree at <paramref name="histogram"/>, walking down its
    /// levels (16 counters at most per level), and the rank of the key among the keys equal to it.
    /// </summary>
    private static uint SelectInTree(Span<int> counts, int histogram, int levels, int rank, out int residual)
    {
        var remaining = rank;
        var node = 0;
        var offset = histogram;
        var size = 16;
        for (var level = 0; level < levels; level++)
        {
            var first = offset + (node * 16);
            var digit = 0;
            while (remaining >= counts[first + digit])
            {
                remaining -= counts[first + digit];
                digit++;
            }

            node = (node * 16) + digit;
            offset += size;
            size *= 16;
        }

        residual = remaining;
        return (uint)node;
    }

    /// <summary>The histograms of the keys of one size.</summary>
    private interface IHistograms
    {
        /// <summary>Adds <paramref name="delta"/> to the counters of a key of sample <paramref name="channel"/> in the histogram at <paramref name="histogram"/>.</summary>
        static abstract void Update(Span<int> counts, int histogram, ReadOnlySpan<int> active, int channel, uint key, int delta);

        /// <summary>Gets the key of rank <paramref name="rank"/> of sample <paramref name="channel"/> of the window whose first column is <paramref name="x"/>.</summary>
        static abstract uint Select(MedianPlan plan, ReadOnlySpan<uint> keys, Span<int> counts, Span<int> active, ReadOnlySpan<int> rows, int x, int channel, int rank);
    }

    /// <summary>8-bit keys: a tree of 2 levels.</summary>
    private readonly struct NarrowHistograms : IHistograms
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Update(Span<int> counts, int histogram, ReadOnlySpan<int> active, int channel, uint key, int delta)
        {
            counts[histogram + (int)(key >> 4)] += delta;
            counts[histogram + 16 + (int)key] += delta;
        }

        public static uint Select(MedianPlan plan, ReadOnlySpan<uint> keys, Span<int> counts, Span<int> active, ReadOnlySpan<int> rows, int x, int channel, int rank)
            => SelectInTree(counts, channel * plan.HistogramLength, 2, rank, out _);
    }

    /// <summary>16-bit keys: a tree of 4 levels.</summary>
    private readonly struct WideHistograms : IHistograms
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Update(Span<int> counts, int histogram, ReadOnlySpan<int> active, int channel, uint key, int delta)
            => UpdateWide(counts, histogram, key, delta);

        public static uint Select(MedianPlan plan, ReadOnlySpan<uint> keys, Span<int> counts, Span<int> active, ReadOnlySpan<int> rows, int x, int channel, int rank)
            => SelectInTree(counts, channel * plan.HistogramLength, 4, rank, out _);
    }

    /// <summary>
    /// 32-bit keys: a tree of their high 16 bits (the group of the key), followed by a tree of the low 16 bits of the keys
    /// of the active group of the sample only.
    /// </summary>
    private readonly struct SplitHistograms : IHistograms
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Update(Span<int> counts, int histogram, ReadOnlySpan<int> active, int channel, uint key, int delta)
        {
            var high = key >> 16;
            UpdateWide(counts, histogram, high, delta);
            if ((int)high == active[channel])
            {
                UpdateWide(counts, histogram + MedianPlan.WideTreeLength, key & 0xFFFF, delta);
            }
        }

        /// <summary>
        /// Gets the group of the key from the first tree, then its low 16 bits from the second one, rebuilt from the window
        /// when it counts another group.
        /// </summary>
        public static uint Select(MedianPlan plan, ReadOnlySpan<uint> keys, Span<int> counts, Span<int> active, ReadOnlySpan<int> rows, int x, int channel, int rank)
        {
            var histogram = channel * plan.HistogramLength;
            var group = SelectInTree(counts, histogram, 4, rank, out var residual);
            var low = histogram + MedianPlan.WideTreeLength;
            if ((int)group != active[channel])
            {
                // Remove the keys of the previous group and add those of the new one, in a single pass over the window
                var previous = (uint)active[channel];
                var channels = plan.Channels;
                var diameter = plan.Diameter;
                for (var j = 0; j < rows.Length; j++)
                {
                    var window = keys.Slice(rows[j] + (x * channels) + channel, ((diameter - 1) * channels) + 1);
                    for (var i = 0; i < window.Length; i += channels)
                    {
                        var key = window[i];
                        var high = key >> 16;
                        if (high == group)
                        {
                            UpdateWide(counts, low, key & 0xFFFF, 1);
                        }
                        else if (high == previous)
                        {
                            UpdateWide(counts, low, key & 0xFFFF, -1);
                        }
                    }
                }

                active[channel] = (int)group;
            }

            return (group << 16) | SelectInTree(counts, low, 4, residual, out _);
        }
    }

    /// <summary>Sets the positions in the key scratch of the padded rows of the window of row <paramref name="y"/>, from the top one.</summary>
    private static void GetWindowRows(MedianPlan plan, int start, int end, int y, Span<int> rows)
    {
        var radius = plan.Radius;
        for (var j = 0; j < rows.Length; j++)
        {
            var source = y - radius + j;
            var row = source < start ? plan.Diameter + (source - (start - radius))
                : source >= end ? plan.Diameter + radius + (source - end)
                : source % plan.Diameter;
            rows[j] = row * plan.PaddedLength;
        }
    }

    private static Span<uint> GetRingRow(MedianPlan plan, Span<uint> keys, int y)
        => keys.Slice(y % plan.Diameter * plan.PaddedLength, plan.PaddedLength);

    /// <summary>Loads the keys of row <paramref name="y"/> (mapped by the edge mode, the keys of zero samples when it reads nothing), padded by the radius on each side.</summary>
    private static void LoadRow<TSample>(PixelStorage storage, int y, MedianPlan plan, Span<uint> destination)
        where TSample : unmanaged
    {
        var zero = ToKey<TSample>(default);
        var source = plan.MapRow(y);
        if (source < 0)
        {
            destination.Fill(zero);
            return;
        }

        var channels = plan.Channels;
        var width = plan.Size.Width;
        var radius = plan.Radius;
        var samples = unsafe(MemoryMarshal.Cast<byte, TSample>(storage.GetRowSpanCore(source)))[..(width * channels)];
        var row = destination.Slice(radius * channels, samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            row[i] = ToKey(samples[i]);
        }

        for (var i = 0; i < radius; i++)
        {
            FillPadding(destination, i, plan.MapColumn(i - radius), radius, channels, zero);
            FillPadding(destination, radius + width + i, plan.MapColumn(width + i), radius, channels, zero);
        }
    }

    private static void FillPadding(Span<uint> padded, int pixel, int sourceColumn, int radius, int channels, uint zero)
    {
        var destination = padded.Slice(pixel * channels, channels);
        if (sourceColumn < 0)
        {
            destination.Fill(zero);
        }
        else
        {
            padded.Slice((radius + sourceColumn) * channels, channels).CopyTo(destination);
        }
    }
}
