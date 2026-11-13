using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
/// spans 1/256).
/// </para>
/// <para>
/// A window of 9 keys (a radius of 1) is selected by a network of minimums and maximums instead
/// (<see cref="SelectRow"/>), for many samples at a time in vectors: the keys have the size of the samples, and the 9 keys
/// of a sample are at the same index of the 3 rows, shifted by 0, 1 and 2 pixels. The network only selects one of the
/// keys, so it gives the key the histograms give.
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
            return unsafe(Unsafe.BitCast<TSample, byte>(sample));

        if (typeof(TSample) == typeof(ushort))
            return unsafe(Unsafe.BitCast<TSample, ushort>(sample));

        if (typeof(TSample) == typeof(Half))
        {
            uint bits = BitConverter.HalfToUInt16Bits(unsafe(Unsafe.BitCast<TSample, Half>(sample)));
            if ((bits & 0x7FFF) > 0x7C00)
            {
                bits &= 0x7FFF;
            }

            return (bits & 0x8000) != 0 ? ~bits & 0xFFFF : bits | 0x8000;
        }

        if (typeof(TSample) != typeof(float))
            throw UnsupportedType.Create<TSample>();

        var value = BitConverter.SingleToUInt32Bits(unsafe(Unsafe.BitCast<TSample, float>(sample)));
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
            return unsafe(Unsafe.BitCast<byte, TSample>(b));
        }

        if (typeof(TSample) == typeof(ushort))
        {
            var s = (ushort)key;
            return unsafe(Unsafe.BitCast<ushort, TSample>(s));
        }

        if (typeof(TSample) == typeof(Half))
        {
            var h = BitConverter.UInt16BitsToHalf((ushort)((key & 0x8000) != 0 ? key & 0x7FFF : ~key & 0xFFFF));
            return unsafe(Unsafe.BitCast<Half, TSample>(h));
        }

        if (typeof(TSample) != typeof(float))
            throw UnsupportedType.Create<TSample>();

        var f = BitConverter.UInt32BitsToSingle((key & 0x8000_0000) != 0 ? key & 0x7FFF_FFFF : ~key);
        return unsafe(Unsafe.BitCast<float, TSample>(f));
    }

    private static void Apply<TSample>(PixelStorage storage, MedianPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.UsesSortingNetwork)
        {
            if (typeof(TSample) == typeof(byte))
            {
                ApplyNetwork<TSample, byte>(storage, plan, cancellationToken);
            }
            else if (typeof(TSample) == typeof(float))
            {
                ApplyNetwork<TSample, uint>(storage, plan, cancellationToken);
            }
            else
            {
                ApplyNetwork<TSample, ushort>(storage, plan, cancellationToken);
            }

            return;
        }

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
        // On the stack up to 1 KB: a band runs on a worker thread, whose stack can be small
        Span<int> rows = diameter <= 256 ? stackalloc int[diameter] : new int[diameter];

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

                if (y == start)
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
            counts.Clear();
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

    /// <summary>
    /// Filters with a radius of 1 (<see cref="MedianPlan.UsesSortingNetwork"/>): the keys of the rows have the size of the
    /// samples (<typeparamref name="TKey"/>: <see cref="byte"/>, <see cref="ushort"/> for 16-bit and half-precision samples,
    /// <see cref="uint"/> for single-precision ones), and the selected keys are written to the row, then turned back into
    /// samples.
    /// </summary>
    private static void ApplyNetwork<TSample, TKey>(PixelStorage storage, MedianPlan plan, CancellationToken cancellationToken)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>, IUnsignedNumber<TKey>
    {
        // Every band reads its halos from the original pixels: load them all before the first row is rewritten
        var workers = plan.Workers;
        var paddedLength = plan.PaddedLength;
        for (var band = 0; band < workers; band++)
        {
            var keys = GetNetworkKeys<TKey>(plan, band);
            LoadKeys<TSample, TKey>(storage, plan.GetBandStart(band) - 1, plan, keys.Slice(3 * paddedLength, paddedLength));
            LoadKeys<TSample, TKey>(storage, plan.GetBandStart(band + 1), plan, keys.Slice(4 * paddedLength, paddedLength));
        }

        if (workers == 1)
        {
            FilterBandNetwork<TSample, TKey>(storage, plan, 0, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => FilterBandNetwork<TSample, TKey>(storage, plan, band, cancellationToken));
    }

    private static void FilterBandNetwork<TSample, TKey>(PixelStorage storage, MedianPlan plan, int band, CancellationToken cancellationToken)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>, IUnsignedNumber<TKey>
    {
        var keys = GetNetworkKeys<TKey>(plan, band);
        var start = plan.GetBandStart(band);
        var end = plan.GetBandStart(band + 1);
        var paddedLength = plan.PaddedLength;
        var length = plan.Size.Width * plan.Channels;
        Span<int> rows = stackalloc int[3];

        // The rows of the band in the window of its first row
        for (var y = start; y <= start + 1 && y < end; y++)
        {
            LoadKeys<TSample, TKey>(storage, y, plan, keys.Slice(y % 3 * paddedLength, paddedLength));
        }

        for (var y = start; y < end; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (y > start && y + 1 < end)
            {
                LoadKeys<TSample, TKey>(storage, y + 1, plan, keys.Slice((y + 1) % 3 * paddedLength, paddedLength));
            }

            // The keys of the row are in the ring: the row receives the selected keys, then the samples of these keys
            GetWindowRows(plan, start, end, y, rows);
            var output = unsafe(MemoryMarshal.Cast<byte, TKey>(storage.GetRowSpanCore(y)))[..length];
            SelectRow<TKey>(keys.Slice(rows[0], paddedLength), keys.Slice(rows[1], paddedLength), keys.Slice(rows[2], paddedLength), output, plan.Channels, plan.Rank);
            ConvertKeys<TSample, TKey>(output, toKeys: false);
        }
    }

    private static Span<TKey> GetNetworkKeys<TKey>(MedianPlan plan, int worker)
        where TKey : unmanaged
        => unsafe(MemoryMarshal.Cast<uint, TKey>(plan.GetKeys(worker)));

    /// <summary>Loads the keys of row <paramref name="y"/> as <see cref="LoadRow"/> does, in keys of the size of the samples.</summary>
    private static void LoadKeys<TSample, TKey>(PixelStorage storage, int y, MedianPlan plan, Span<TKey> destination)
        where TSample : unmanaged
        where TKey : unmanaged, IBinaryInteger<TKey>, IUnsignedNumber<TKey>
    {
        var zero = TKey.CreateTruncating(ToKey<TSample>(default));
        var source = plan.MapRow(y);
        if (source < 0)
        {
            destination.Fill(zero);
            return;
        }

        var channels = plan.Channels;
        var width = plan.Size.Width;
        var row = destination.Slice(channels, width * channels);
        var samples = unsafe(MemoryMarshal.Cast<byte, TKey>(storage.GetRowSpanCore(source)));
        samples[..row.Length].CopyTo(row);
        ConvertKeys<TSample, TKey>(row, toKeys: true);
        FillPadding(destination, 0, plan.MapColumn(-1), 1, channels, zero);
        FillPadding(destination, 1 + width, plan.MapColumn(width), 1, channels, zero);
    }

    private static void FillPadding<TKey>(Span<TKey> padded, int pixel, int sourceColumn, int radius, int channels, TKey zero)
        where TKey : unmanaged
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

    /// <summary>
    /// Replaces the bits of half-precision or single-precision samples by their keys (<see cref="ToKey"/>), or keys by the
    /// bits of their samples (<see cref="FromKey"/>), in place; the keys of 8-bit and 16-bit samples are their bits.
    /// </summary>
    internal static void ConvertKeys<TSample, TKey>(Span<TKey> values, bool toKeys)
        where TSample : unmanaged
        where TKey : unmanaged
    {
        if (typeof(TSample) == typeof(float))
        {
            var bits = unsafe(MemoryMarshal.Cast<TKey, uint>(values));
            var i = 0;
            if (Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar)
            {
                ref var start = ref unsafe(MemoryMarshal.GetReference(bits));
                if (Vector256.IsHardwareAccelerated)
                {
                    for (; i + Vector256<uint>.Count <= bits.Length; i += Vector256<uint>.Count)
                    {
                        var value = unsafe(Vector256.LoadUnsafe(ref start, (nuint)i));
                        unsafe { (toKeys ? SingleToKeys(value) : KeysToSingle(value)).StoreUnsafe(ref start, (nuint)i); }
                    }
                }

                for (; i + Vector128<uint>.Count <= bits.Length; i += Vector128<uint>.Count)
                {
                    var value = unsafe(Vector128.LoadUnsafe(ref start, (nuint)i));
                    unsafe { (toKeys ? SingleToKeys(value) : KeysToSingle(value)).StoreUnsafe(ref start, (nuint)i); }
                }
            }

            for (; i < bits.Length; i++)
            {
                bits[i] = toKeys ? ToKey(BitConverter.UInt32BitsToSingle(bits[i])) : BitConverter.SingleToUInt32Bits(FromKey<float>(bits[i]));
            }
        }
        else if (typeof(TSample) == typeof(Half))
        {
            var bits = unsafe(MemoryMarshal.Cast<TKey, ushort>(values));
            var i = 0;
            if (Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar)
            {
                ref var start = ref unsafe(MemoryMarshal.GetReference(bits));
                if (Vector256.IsHardwareAccelerated)
                {
                    for (; i + Vector256<ushort>.Count <= bits.Length; i += Vector256<ushort>.Count)
                    {
                        var value = unsafe(Vector256.LoadUnsafe(ref start, (nuint)i));
                        unsafe { (toKeys ? HalfToKeys(value) : KeysToHalf(value)).StoreUnsafe(ref start, (nuint)i); }
                    }
                }

                for (; i + Vector128<ushort>.Count <= bits.Length; i += Vector128<ushort>.Count)
                {
                    var value = unsafe(Vector128.LoadUnsafe(ref start, (nuint)i));
                    unsafe { (toKeys ? HalfToKeys(value) : KeysToHalf(value)).StoreUnsafe(ref start, (nuint)i); }
                }
            }

            for (; i < bits.Length; i++)
            {
                bits[i] = toKeys ? (ushort)ToKey(BitConverter.UInt16BitsToHalf(bits[i])) : BitConverter.HalfToUInt16Bits(FromKey<Half>(bits[i]));
            }
        }
    }

    // The keys of ToKey: a number that is not a number is made positive, then the sign bit is set for positive numbers and
    // every bit is inverted for negative ones; FromKey inverts every bit of the keys whose high bit is clear
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> SingleToKeys(Vector128<uint> value)
    {
        var magnitude = value & Vector128.Create(0x7FFF_FFFFu);
        value = Vector128.ConditionalSelect(Vector128.GreaterThan(magnitude.AsInt32(), Vector128.Create(0x7F80_0000)).AsUInt32(), magnitude, value);
        return value ^ (Vector128.ShiftRightArithmetic(value.AsInt32(), 31).AsUInt32() | Vector128.Create(0x8000_0000u));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> SingleToKeys(Vector256<uint> value)
    {
        var magnitude = value & Vector256.Create(0x7FFF_FFFFu);
        value = Vector256.ConditionalSelect(Vector256.GreaterThan(magnitude.AsInt32(), Vector256.Create(0x7F80_0000)).AsUInt32(), magnitude, value);
        return value ^ (Vector256.ShiftRightArithmetic(value.AsInt32(), 31).AsUInt32() | Vector256.Create(0x8000_0000u));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> KeysToSingle(Vector128<uint> key) => key ^ (~Vector128.ShiftRightArithmetic(key.AsInt32(), 31).AsUInt32() | Vector128.Create(0x8000_0000u));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> KeysToSingle(Vector256<uint> key) => key ^ (~Vector256.ShiftRightArithmetic(key.AsInt32(), 31).AsUInt32() | Vector256.Create(0x8000_0000u));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> HalfToKeys(Vector128<ushort> value)
    {
        var magnitude = value & Vector128.Create((ushort)0x7FFF);
        value = Vector128.ConditionalSelect(Vector128.GreaterThan(magnitude.AsInt16(), Vector128.Create((short)0x7C00)).AsUInt16(), magnitude, value);
        return value ^ (Vector128.ShiftRightArithmetic(value.AsInt16(), 15).AsUInt16() | Vector128.Create((ushort)0x8000));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ushort> HalfToKeys(Vector256<ushort> value)
    {
        var magnitude = value & Vector256.Create((ushort)0x7FFF);
        value = Vector256.ConditionalSelect(Vector256.GreaterThan(magnitude.AsInt16(), Vector256.Create((short)0x7C00)).AsUInt16(), magnitude, value);
        return value ^ (Vector256.ShiftRightArithmetic(value.AsInt16(), 15).AsUInt16() | Vector256.Create((ushort)0x8000));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> KeysToHalf(Vector128<ushort> key) => key ^ (~Vector128.ShiftRightArithmetic(key.AsInt16(), 15).AsUInt16() | Vector128.Create((ushort)0x8000));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ushort> KeysToHalf(Vector256<ushort> key) => key ^ (~Vector256.ShiftRightArithmetic(key.AsInt16(), 15).AsUInt16() | Vector256.Create((ushort)0x8000));

    /// <summary>
    /// Sets every key of <paramref name="output"/> to the key of rank <paramref name="rank"/> of its window of 3x3 pixels: key
    /// <c>i</c> of the output is selected among the keys <c>i</c>, <c>i + channels</c> and <c>i + 2 channels</c> of the 3
    /// padded rows. The median sorts the 3 keys of each column and takes the median of the largest of the smallest keys, the
    /// median of the middle keys and the smallest of the largest keys; other ranks sort the 9 keys (25 exchanges).
    /// </summary>
    internal static void SelectRow<TKey>(ReadOnlySpan<TKey> top, ReadOnlySpan<TKey> middle, ReadOnlySpan<TKey> bottom, Span<TKey> output, int channels, int rank)
        where TKey : unmanaged, IBinaryInteger<TKey>, IUnsignedNumber<TKey>
    {
        var done = 0;
        if (Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar)
        {
            done = Vector512.IsHardwareAccelerated && output.Length >= Vector512<TKey>.Count ? SelectRowVectors<TKey, Vector512<TKey>, Lanes512<TKey>>(top, middle, bottom, output, channels, rank)
                : Vector256.IsHardwareAccelerated && output.Length >= Vector256<TKey>.Count ? SelectRowVectors<TKey, Vector256<TKey>, Lanes256<TKey>>(top, middle, bottom, output, channels, rank)
                : SelectRowVectors<TKey, Vector128<TKey>, Lanes128<TKey>>(top, middle, bottom, output, channels, rank);
        }

        for (var i = done; i < output.Length; i++)
        {
            var c1 = i + channels;
            var c2 = c1 + channels;
            output[i] = rank == 4
                ? Median9<TKey, TKey, ScalarLanes<TKey>>(top[i], top[c1], top[c2], middle[i], middle[c1], middle[c2], bottom[i], bottom[c1], bottom[c2])
                : Select9<TKey, TKey, ScalarLanes<TKey>>(top[i], top[c1], top[c2], middle[i], middle[c1], middle[c2], bottom[i], bottom[c1], bottom[c2], rank);
        }
    }

    /// <summary>The reference of <see cref="SelectRow"/>: the 9 keys of every window are sorted.</summary>
    internal static void SelectRowScalar<TKey>(ReadOnlySpan<TKey> top, ReadOnlySpan<TKey> middle, ReadOnlySpan<TKey> bottom, Span<TKey> output, int channels, int rank)
        where TKey : unmanaged, IBinaryInteger<TKey>
    {
        Span<TKey> window = stackalloc TKey[9];
        for (var i = 0; i < output.Length; i++)
        {
            for (var k = 0; k < 3; k++)
            {
                window[k] = top[i + (k * channels)];
                window[3 + k] = middle[i + (k * channels)];
                window[6 + k] = bottom[i + (k * channels)];
            }

            window.Sort();
            output[i] = window[rank];
        }
    }

    /// <summary>Selects whole vectors of keys, the last one overlapping the one before it. Returns the number of keys set: 0 when the row is shorter than a vector.</summary>
    private static int SelectRowVectors<TKey, TVector, TLanes>(ReadOnlySpan<TKey> top, ReadOnlySpan<TKey> middle, ReadOnlySpan<TKey> bottom, Span<TKey> output, int channels, int rank)
        where TKey : unmanaged
        where TVector : struct
        where TLanes : struct, ILanes<TKey, TVector>
    {
        var count = TLanes.Count;
        if (output.Length < count)
            return 0;

        ref var t = ref unsafe(MemoryMarshal.GetReference(top));
        ref var m = ref unsafe(MemoryMarshal.GetReference(middle));
        ref var b = ref unsafe(MemoryMarshal.GetReference(bottom));
        ref var o = ref unsafe(MemoryMarshal.GetReference(output));
        var c1 = (nuint)channels;
        var c2 = 2 * c1;
        var last = (nuint)(output.Length - count);
        nuint i = 0;
        if (rank == 4)
        {
            while (true)
            {
                var value = Median9<TKey, TVector, TLanes>(TLanes.Load(ref t, i), TLanes.Load(ref t, i + c1), TLanes.Load(ref t, i + c2), TLanes.Load(ref m, i), TLanes.Load(ref m, i + c1), TLanes.Load(ref m, i + c2), TLanes.Load(ref b, i), TLanes.Load(ref b, i + c1), TLanes.Load(ref b, i + c2));
                TLanes.Store(value, ref o, i);
                if (i == last)
                    break;

                i = Math.Min(i + (nuint)count, last);
            }
        }
        else
        {
            while (true)
            {
                var value = Select9<TKey, TVector, TLanes>(TLanes.Load(ref t, i), TLanes.Load(ref t, i + c1), TLanes.Load(ref t, i + c2), TLanes.Load(ref m, i), TLanes.Load(ref m, i + c1), TLanes.Load(ref m, i + c2), TLanes.Load(ref b, i), TLanes.Load(ref b, i + c1), TLanes.Load(ref b, i + c2), rank);
                TLanes.Store(value, ref o, i);
                if (i == last)
                    break;

                i = Math.Min(i + (nuint)count, last);
            }
        }

        return output.Length;
    }

    /// <summary>The median of 9 values: the 3 columns <c>(a, d, g)</c>, <c>(b, e, h)</c> and <c>(c, f, i)</c> are sorted first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TValue Median9<TKey, TValue, TLanes>(TValue a, TValue b, TValue c, TValue d, TValue e, TValue f, TValue g, TValue h, TValue i)
        where TKey : unmanaged
        where TValue : struct
        where TLanes : struct, ILanes<TKey, TValue>
    {
        Sort3<TKey, TValue, TLanes>(ref a, ref d, ref g);
        Sort3<TKey, TValue, TLanes>(ref b, ref e, ref h);
        Sort3<TKey, TValue, TLanes>(ref c, ref f, ref i);
        var low = TLanes.Max(TLanes.Max(a, b), c);
        var high = TLanes.Min(TLanes.Min(g, h), i);
        return Median3<TKey, TValue, TLanes>(low, Median3<TKey, TValue, TLanes>(d, e, f), high);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Sort3<TKey, TValue, TLanes>(ref TValue a, ref TValue b, ref TValue c)
        where TKey : unmanaged
        where TValue : struct
        where TLanes : struct, ILanes<TKey, TValue>
    {
        var low = TLanes.Min(a, b);
        var high = TLanes.Max(a, b);
        a = TLanes.Min(low, c);
        b = TLanes.Max(low, TLanes.Min(high, c));
        c = TLanes.Max(high, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TValue Median3<TKey, TValue, TLanes>(TValue a, TValue b, TValue c)
        where TKey : unmanaged
        where TValue : struct
        where TLanes : struct, ILanes<TKey, TValue>
        => TLanes.Max(TLanes.Min(a, b), TLanes.Min(TLanes.Max(a, b), c));

    /// <summary>The value of rank <paramref name="rank"/> of 9 values, sorted by a network of 25 exchanges.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TValue Select9<TKey, TValue, TLanes>(TValue v0, TValue v1, TValue v2, TValue v3, TValue v4, TValue v5, TValue v6, TValue v7, TValue v8, int rank)
        where TKey : unmanaged
        where TValue : struct
        where TLanes : struct, ILanes<TKey, TValue>
    {
        Exchange<TKey, TValue, TLanes>(ref v0, ref v3);
        Exchange<TKey, TValue, TLanes>(ref v1, ref v7);
        Exchange<TKey, TValue, TLanes>(ref v2, ref v5);
        Exchange<TKey, TValue, TLanes>(ref v4, ref v8);
        Exchange<TKey, TValue, TLanes>(ref v0, ref v7);
        Exchange<TKey, TValue, TLanes>(ref v2, ref v4);
        Exchange<TKey, TValue, TLanes>(ref v3, ref v8);
        Exchange<TKey, TValue, TLanes>(ref v5, ref v6);
        Exchange<TKey, TValue, TLanes>(ref v0, ref v2);
        Exchange<TKey, TValue, TLanes>(ref v1, ref v3);
        Exchange<TKey, TValue, TLanes>(ref v4, ref v5);
        Exchange<TKey, TValue, TLanes>(ref v7, ref v8);
        Exchange<TKey, TValue, TLanes>(ref v1, ref v4);
        Exchange<TKey, TValue, TLanes>(ref v3, ref v6);
        Exchange<TKey, TValue, TLanes>(ref v5, ref v7);
        Exchange<TKey, TValue, TLanes>(ref v0, ref v1);
        Exchange<TKey, TValue, TLanes>(ref v2, ref v4);
        Exchange<TKey, TValue, TLanes>(ref v3, ref v5);
        Exchange<TKey, TValue, TLanes>(ref v6, ref v8);
        Exchange<TKey, TValue, TLanes>(ref v2, ref v3);
        Exchange<TKey, TValue, TLanes>(ref v4, ref v5);
        Exchange<TKey, TValue, TLanes>(ref v6, ref v7);
        Exchange<TKey, TValue, TLanes>(ref v1, ref v2);
        Exchange<TKey, TValue, TLanes>(ref v3, ref v4);
        Exchange<TKey, TValue, TLanes>(ref v5, ref v6);
        return rank switch
        {
            0 => v0,
            1 => v1,
            2 => v2,
            3 => v3,
            4 => v4,
            5 => v5,
            6 => v6,
            7 => v7,
            _ => v8,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Exchange<TKey, TValue, TLanes>(ref TValue a, ref TValue b)
        where TKey : unmanaged
        where TValue : struct
        where TLanes : struct, ILanes<TKey, TValue>
    {
        var low = TLanes.Min(a, b);
        b = TLanes.Max(a, b);
        a = low;
    }

    /// <summary>The operations of the network, on single keys or on vectors of keys.</summary>
    private interface ILanes<TKey, TValue>
        where TKey : unmanaged
        where TValue : struct
    {
        static abstract int Count { get; }

        static abstract TValue Load(ref TKey source, nuint index);

        static abstract void Store(TValue value, ref TKey destination, nuint index);

        static abstract TValue Min(TValue left, TValue right);

        static abstract TValue Max(TValue left, TValue right);
    }

    private readonly struct ScalarLanes<TKey> : ILanes<TKey, TKey>
        where TKey : unmanaged, IBinaryInteger<TKey>
    {
        public static int Count => 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TKey Load(ref TKey source, nuint index) => unsafe(Unsafe.Add(ref source, index));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(TKey value, ref TKey destination, nuint index)
        {
            unsafe { Unsafe.Add(ref destination, index) = value; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TKey Min(TKey left, TKey right) => left < right ? left : right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TKey Max(TKey left, TKey right) => left > right ? left : right;
    }

    private readonly struct Lanes128<TKey> : ILanes<TKey, Vector128<TKey>>
        where TKey : unmanaged
    {
        public static int Count => Vector128<TKey>.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<TKey> Load(ref TKey source, nuint index) => unsafe(Vector128.LoadUnsafe(ref source, index));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Vector128<TKey> value, ref TKey destination, nuint index)
        {
            unsafe { value.StoreUnsafe(ref destination, index); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<TKey> Min(Vector128<TKey> left, Vector128<TKey> right) => Vector128.Min(left, right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<TKey> Max(Vector128<TKey> left, Vector128<TKey> right) => Vector128.Max(left, right);
    }

    private readonly struct Lanes256<TKey> : ILanes<TKey, Vector256<TKey>>
        where TKey : unmanaged
    {
        public static int Count => Vector256<TKey>.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<TKey> Load(ref TKey source, nuint index) => unsafe(Vector256.LoadUnsafe(ref source, index));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Vector256<TKey> value, ref TKey destination, nuint index)
        {
            unsafe { value.StoreUnsafe(ref destination, index); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<TKey> Min(Vector256<TKey> left, Vector256<TKey> right) => Vector256.Min(left, right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<TKey> Max(Vector256<TKey> left, Vector256<TKey> right) => Vector256.Max(left, right);
    }

    private readonly struct Lanes512<TKey> : ILanes<TKey, Vector512<TKey>>
        where TKey : unmanaged
    {
        public static int Count => Vector512<TKey>.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<TKey> Load(ref TKey source, nuint index) => unsafe(Vector512.LoadUnsafe(ref source, index));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Store(Vector512<TKey> value, ref TKey destination, nuint index)
        {
            unsafe { value.StoreUnsafe(ref destination, index); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<TKey> Min(Vector512<TKey> left, Vector512<TKey> right) => Vector512.Min(left, right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<TKey> Max(Vector512<TKey> left, Vector512<TKey> right) => Vector512.Max(left, right);
    }
}
