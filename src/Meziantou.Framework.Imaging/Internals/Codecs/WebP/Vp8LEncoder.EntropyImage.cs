using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The meta prefix codes of the main image: the image is cut into square tiles, the symbols of each tile are counted, and
/// tiles with similar statistics share a group of prefix codes. The entropy image gives the group of each tile.
/// </summary>
/// <remarks>
/// <para>
/// The tiles are first gathered in bins by the cost per symbol of their green, red and blue symbols. The bins are then merged
/// pairwise, the pair that saves the most bits first, while a merge saves bits (a group costs its prefix codes). Last, every
/// tile moves to the group that codes its symbols in the fewest bits, the groups are recounted, and this is repeated while
/// the estimated size decreases. The plan is kept when its estimate (groups and entropy image) is below the estimate of a
/// single group.
/// </para>
/// <para>
/// Memory, in two buffers per tile size tried (few rentals: each one is a failure point of the fault injection tests): the
/// histograms of the tiles (16-bit counts: a tile of at most 128 x 128 pixels starts at most 16,384 symbols), at most 8
/// bytes per pixel (or 1 MiB), which sets the smallest tile size; the histograms and symbol costs of the groups; and a
/// matrix of the merge gains of the bins. A kept plan holds its entropy image and the histograms of its groups.
/// </para>
/// </remarks>
internal sealed partial class Vp8LEncoder
{
    private const int MaxEntropyImageBits = 7;

    /// <summary>Gets the most groups of the first clustering (the bins of the tiles): 3, 4 or 5 levels per feature.</summary>
    private int MaxInitialGroups => _effort switch
    {
        < 5 => 27,
        < 9 => 64,
        _ => 125,
    };

    private bool EntropyImageEnabled => _effort >= 3 && !Ranking;

    /// <summary>Chooses the meta prefix codes of the main image.</summary>
    /// <returns>The plan, or <see langword="null"/> when a single group is estimated smaller.</returns>
    private EntropyImagePlan? PlanEntropyImage(ReadOnlySpan<uint> pixels, int width, int height, ReadOnlySpan<uint> tokens, int cacheBits, ReadOnlySpan<uint> total)
    {
        var size = HistogramSize(cacheBits);
        var greenSize = GreenAlphabetSize(cacheBits);
        var bestBits = EstimateGroupBits(total, greenSize);
        int[] candidates = _effort switch
        {
            < 5 => [5],
            < 9 => [4, 5],
            _ => [3, 4, 5, 6],
        };

        var budget = Math.Max(1L << 20, 8L * pixels.Length);
        EntropyImagePlan? best = null;
        try
        {
            best = PlanBestEntropyImage(pixels, width, height, tokens, cacheBits, size, candidates, budget, bestBits, ref best);
            var result = best;
            best = null;
            return result;
        }
        finally
        {
            best?.Dispose();
        }
    }

    /// <summary>Builds the plan of each candidate tile size, keeping the one estimated smallest in <paramref name="best"/>.</summary>
    private EntropyImagePlan? PlanBestEntropyImage(ReadOnlySpan<uint> pixels, int width, int height, ReadOnlySpan<uint> tokens, int cacheBits, int size, int[] candidates, long budget, double bestBits, ref EntropyImagePlan? best)
    {
        var previousBits = 0;
        foreach (var candidate in candidates)
        {
            var bits = candidate;
            while (bits < MaxEntropyImageBits && (long)Vp8LDecoder.DivRoundUp(width, bits) * Vp8LDecoder.DivRoundUp(height, bits) * size * sizeof(ushort) > budget)
            {
                bits++;
            }

            if (bits == previousBits || (Vp8LDecoder.DivRoundUp(width, bits) == 1 && Vp8LDecoder.DivRoundUp(height, bits) == 1))
                continue;

            previousBits = bits;
            var plan = BuildEntropyImagePlan(pixels, width, height, tokens, cacheBits, bits);
            if (plan is not null && plan.EstimatedBits < bestBits)
            {
                best?.Dispose();
                best = plan;
                bestBits = plan.EstimatedBits;
            }
            else
            {
                plan?.Dispose();
            }
        }

        return best;
    }

    private EntropyImagePlan? BuildEntropyImagePlan(ReadOnlySpan<uint> pixels, int width, int height, ReadOnlySpan<uint> tokens, int cacheBits, int bits)
    {
        var size = HistogramSize(cacheBits);
        var greenSize = GreenAlphabetSize(cacheBits);
        var tilesX = Vp8LDecoder.DivRoundUp(width, bits);
        var tilesY = Vp8LDecoder.DivRoundUp(height, bits);
        var tileCount = tilesX * tilesY;

        // One buffer for the histograms of the tiles and their features, then one for the scratch of the clustering, sized
        // by the number of groups of the first clustering
        using var tileBuffer = RentSpan<byte>(SpanCarver.Size<ushort>((long)tileCount * size) + SpanCarver.Size<float>(3L * tileCount), out var tileBytes);
        var tileCarver = new SpanCarver(tileBytes);
        var tiles = tileCarver.Take<ushort>(tileCount * size);
        var features = tileCarver.Take<float>(3 * tileCount);
        CountTileSymbols(pixels, width, tokens, cacheBits, bits, tiles, size);

        EntropyImagePlan? plan = new(this, bits, tilesX, tilesY);
        try
        {
            var groups = plan.Groups;
            var groupCount = AssignInitialGroups(tiles, tileCount, size, greenSize, groups, features);
            if (groupCount <= 1)
                return null;

            var workBytes = SpanCarver.Size<uint>((long)groupCount * size) + SpanCarver.Size<double>(groupCount) + SpanCarver.Size<double>((long)groupCount * groupCount)
                + SpanCarver.Size<int>(groupCount) + SpanCarver.Size<float>((long)groupCount * size) + SpanCarver.Size<int>(tileCount) + SpanCarver.Size<int>(size);
            using var workBuffer = RentSpan<byte>(workBytes, out var work);
            var carver = new SpanCarver(work);
            var histograms = carver.Take<uint>(groupCount * size);
            var mergeCosts = carver.Take<double>(groupCount);
            var gains = carver.Take<double>(groupCount * groupCount);
            var targets = carver.Take<int>(groupCount);
            var symbolCosts = carver.Take<float>(groupCount * size);
            var previousGroups = carver.Take<int>(tileCount);
            var symbols = carver.Take<int>(size);
            SumGroups(tiles, size, groups, histograms);
            groupCount = MergeGroups(histograms, groupCount, size, greenSize, groups, mergeCosts, gains, targets);
            groupCount = RefineGroups(tiles, tileCount, size, greenSize, groups, histograms, groupCount, symbolCosts, previousGroups, symbols);
            if (groupCount <= 1)
                return null;

            plan.SetHistograms(histograms[..(groupCount * size)], groupCount);

            // Empty tiles (covered by a backward reference started in an earlier tile) take the group of the previous tile
            var groupBits = 0.0;
            for (var group = 0; group < groupCount; group++)
            {
                groupBits += EstimateGroupBits(plan.Histograms.Slice(group * size, size), greenSize);
            }

            var changes = 0;
            for (var tile = 0; tile < tileCount; tile++)
            {
                if (groups[tile] < 0)
                {
                    groups[tile] = tile == 0 ? 0 : groups[tile - 1];
                }

                if (tile > 0 && groups[tile] != groups[tile - 1] && (tile < tilesX || groups[tile] != groups[tile - tilesX]))
                {
                    changes++;
                }

                plan.Image[tile] = 0xFF000000u | ((uint)groups[tile] << 8);
            }

            plan.EstimatedBits = groupBits + 32 + (changes * (Log2Count(groupCount) + 6));
            var result = plan;
            plan = null;
            return result;
        }
        finally
        {
            plan?.Dispose();
        }
    }

    /// <summary>Counts the symbols that start in each tile (with the color cache of the parse).</summary>
    private void CountTileSymbols(ReadOnlySpan<uint> pixels, int width, ReadOnlySpan<uint> tokens, int cacheBits, int bits, Span<ushort> tiles, int size)
    {
        var greenSize = GreenAlphabetSize(cacheBits);
        var cache = new uint[cacheBits == 0 ? 0 : 1 << cacheBits];
        var tilesX = Vp8LDecoder.DivRoundUp(width, bits);
        var position = 0;
        var x = 0;
        var y = 0;
        foreach (var token in tokens)
        {
            var lengthField = (int)(token >> TokenLengthShift);
            var value = (int)(token & TokenValueMask) + 1;
            if (lengthField == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                foreach (var argb in pixels.Slice(position, value))
                {
                    var histogram = tiles.Slice((((y >> bits) * tilesX) + (x >> bits)) * size, size);
                    if (++x == width)
                    {
                        x = 0;
                        y++;
                    }

                    if (cache.Length != 0)
                    {
                        var key = (int)Vp8LDecoder.ColorCacheHash(argb, cacheBits);
                        if (cache[key] == argb)
                        {
                            histogram[NumLiteralCodes + NumLengthCodes + key]++;
                            continue;
                        }

                        cache[key] = argb;
                    }

                    histogram[(int)((argb >> 8) & 0xFF)]++;
                    histogram[greenSize + (int)((argb >> 16) & 0xFF)]++;
                    histogram[greenSize + NumLiteralCodes + (int)(argb & 0xFF)]++;
                    histogram[greenSize + (2 * NumLiteralCodes) + (int)(argb >> 24)]++;
                }

                position += value;
                continue;
            }

            var length = lengthField + 1;
            var tile = tiles.Slice((((y >> bits) * tilesX) + (x >> bits)) * size, size);
            tile[NumLiteralCodes + GetPrefix(length).Prefix]++;
            tile[greenSize + (3 * NumLiteralCodes) + GetPrefix(value).Prefix]++;
            if (cache.Length != 0)
            {
                foreach (var argb in pixels.Slice(position, length))
                {
                    cache[(int)Vp8LDecoder.ColorCacheHash(argb, cacheBits)] = argb;
                }
            }

            position += length;
            x += length;
            if (x >= width)
            {
                y += x / width;
                x %= width;
            }
        }
    }

    /// <summary>
    /// Gathers the tiles in bins of similar costs per green, red and blue symbol (or one group per tile when they are few).
    /// Empty tiles get the group -1.
    /// </summary>
    /// <returns>The number of groups.</returns>
    private int AssignInitialGroups(ReadOnlySpan<ushort> tiles, int tileCount, int size, int greenSize, Span<int> groups, Span<float> features)
    {
        var maxGroups = MaxInitialGroups;

        var nonEmpty = 0;
        for (var tile = 0; tile < tileCount; tile++)
        {
            groups[tile] = HasSymbols(tiles.Slice(tile * size, greenSize)) ? 0 : -1;
            nonEmpty += groups[tile] + 1;
        }

        if (nonEmpty <= maxGroups)
        {
            var count = 0;
            for (var tile = 0; tile < tileCount; tile++)
            {
                if (groups[tile] == 0)
                {
                    groups[tile] = count++;
                }
            }

            return count;
        }

        // Three features per tile, quantized between their extremes
        var levels = (int)Math.Round(Math.Cbrt(maxGroups), MidpointRounding.AwayFromZero);
        Span<float> minimum = [float.MaxValue, float.MaxValue, float.MaxValue];
        Span<float> maximum = [float.MinValue, float.MinValue, float.MinValue];
        for (var tile = 0; tile < tileCount; tile++)
        {
            if (groups[tile] < 0)
                continue;

            var histogram = tiles.Slice(tile * size, size);
            for (var feature = 0; feature < 3; feature++)
            {
                var counts = feature == 0 ? histogram[..greenSize] : histogram.Slice(greenSize + ((feature - 1) * NumLiteralCodes), NumLiteralCodes);
                var value = BitsPerSymbol(counts);
                features[(tile * 3) + feature] = value;
                minimum[feature] = Math.Min(minimum[feature], value);
                maximum[feature] = Math.Max(maximum[feature], value);
            }
        }

        Span<int> bins = stackalloc int[levels * levels * levels];
        bins.Fill(-1);
        var groupCount = 0;
        for (var tile = 0; tile < tileCount; tile++)
        {
            if (groups[tile] < 0)
                continue;

            var bin = 0;
            for (var feature = 0; feature < 3; feature++)
            {
                var range = maximum[feature] - minimum[feature];
                var level = range <= 0 ? 0 : Math.Min(levels - 1, (int)((features[(tile * 3) + feature] - minimum[feature]) / range * levels));
                bin = (bin * levels) + level;
            }

            if (bins[bin] < 0)
            {
                bins[bin] = groupCount++;
            }

            groups[tile] = bins[bin];
        }

        return groupCount;

        static bool HasSymbols(ReadOnlySpan<ushort> green)
        {
            foreach (var count in green)
            {
                if (count != 0)
                    return true;
            }

            return false;
        }

        static float BitsPerSymbol(ReadOnlySpan<ushort> counts)
        {
            long total = 0;
            var used = 0;
            foreach (var count in counts)
            {
                total += count;
                used += count == 0 ? 0 : 1;
            }

            if (used <= 1)
                return 0;

            var log2Total = Log2Count(total);
            double bits = 0;
            foreach (var count in counts)
            {
                if (count != 0)
                {
                    bits += Math.Max(count * (log2Total - Log2Count(count)), count);
                }
            }

            return (float)(bits / total);
        }
    }

    /// <summary>Adds the histogram of every tile to the histogram of its group (cleared first); empty tiles are skipped.</summary>
    private static void SumGroups(ReadOnlySpan<ushort> tiles, int size, ReadOnlySpan<int> groups, Span<uint> histograms)
    {
        histograms.Clear();
        for (var tile = 0; tile < groups.Length; tile++)
        {
            var group = groups[tile];
            if (group < 0)
                continue;

            var source = tiles.Slice(tile * size, size);
            var destination = histograms.Slice(group * size, size);
            for (var i = 0; i < size; i++)
            {
                destination[i] += source[i];
            }
        }
    }

    /// <summary>Merges groups pairwise while a merge saves bits, the largest saving first, and renumbers the groups of the tiles.</summary>
    /// <returns>The number of groups.</returns>
    private int MergeGroups(Span<uint> histograms, int groupCount, int size, int greenSize, Span<int> groups, Span<double> costs, Span<double> gains, Span<int> targets)
    {        for (var i = 0; i < groupCount; i++)
        {
            costs[i] = EstimateGroupBits(histograms.Slice(i * size, size), greenSize);
            targets[i] = i;
        }

        for (var i = 0; i < groupCount; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var j = i + 1; j < groupCount; j++)
            {
                gains[(i * groupCount) + j] = MergeGain(histograms, costs, size, greenSize, i, j);
            }
        }

        while (true)
        {
            var bestGain = 0.0;
            int first = -1, second = -1;
            for (var i = 0; i < groupCount; i++)
            {
                if (targets[i] != i)
                    continue;

                for (var j = i + 1; j < groupCount; j++)
                {
                    if (targets[j] == j && gains[(i * groupCount) + j] > bestGain)
                    {
                        bestGain = gains[(i * groupCount) + j];
                        first = i;
                        second = j;
                    }
                }
            }

            if (first < 0)
                break;

            _cancellationToken.ThrowIfCancellationRequested();
            var target = histograms.Slice(first * size, size);
            var source = histograms.Slice(second * size, size);
            for (var i = 0; i < size; i++)
            {
                target[i] += source[i];
            }

            targets[second] = first;
            costs[first] = EstimateGroupBits(target, greenSize);
            for (var k = 0; k < groupCount; k++)
            {
                if (k != first && targets[k] == k)
                {
                    var (i, j) = k < first ? (k, first) : (first, k);
                    gains[(i * groupCount) + j] = MergeGain(histograms, costs, size, greenSize, i, j);
                }
            }
        }

        // Renumber the remaining groups in order, moving their histograms down
        var count = 0;
        for (var i = 0; i < groupCount; i++)
        {
            if (targets[i] == i)
            {
                histograms.Slice(i * size, size).CopyTo(histograms.Slice(count * size, size));
                targets[i] = count++;
            }
            else
            {
                targets[i] = ~targets[i];
            }
        }

        for (var i = 0; i < groupCount; i++)
        {
            // A merged group points to the group it was merged into, which may itself have been merged (into a lower index)
            var resolved = i;
            while (targets[resolved] < 0)
            {
                resolved = ~targets[resolved];
            }

            targets[i] = targets[resolved];
        }

        for (var tile = 0; tile < groups.Length; tile++)
        {
            if (groups[tile] >= 0)
            {
                groups[tile] = targets[groups[tile]];
            }
        }

        return count;

        static double MergeGain(ReadOnlySpan<uint> all, ReadOnlySpan<double> costs, int size, int greenSize, int i, int j)
            => costs[i] + costs[j] - EstimateMergedGroupBits(all.Slice(i * size, size), all.Slice(j * size, size), greenSize);
    }

    /// <summary>
    /// Moves every tile to the group that codes its symbols in the fewest bits and recounts the groups, while the estimated size
    /// decreases.
    /// </summary>
    /// <returns>The number of groups (empty groups are removed).</returns>
    private int RefineGroups(ReadOnlySpan<ushort> tiles, int tileCount, int size, int greenSize, Span<int> groups, Span<uint> histograms, int groupCount, Span<float> costs, Span<int> previous, Span<int> symbols)
    {
        var passes = _effort switch
        {
            < 5 => 1,
            < 9 => 2,
            _ => 4,
        };

        var currentBits = EstimateAllGroups(histograms, groupCount);
        for (var pass = 0; pass < passes && groupCount > 1; pass++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var group = 0; group < groupCount; group++)
            {
                var histogram = histograms.Slice(group * size, size);
                var cost = costs.Slice(group * size, size);
                ComputeSymbolCosts(histogram[..greenSize], cost[..greenSize]);
                for (var alphabet = 0; alphabet < 4; alphabet++)
                {
                    var start = greenSize + (alphabet * NumLiteralCodes);
                    var length = alphabet == 3 ? NumDistanceCodes : NumLiteralCodes;
                    ComputeSymbolCosts(histogram.Slice(start, length), cost.Slice(start, length));
                }
            }

            groups[..tileCount].CopyTo(previous);
            for (var tile = 0; tile < tileCount; tile++)
            {
                if (groups[tile] < 0)
                    continue;

                var histogram = tiles.Slice(tile * size, size);
                var used = 0;
                for (var i = 0; i < size; i++)
                {
                    if (histogram[i] != 0)
                    {
                        symbols[used++] = i;
                    }
                }

                var best = groups[tile];
                var bestCost = float.MaxValue;
                for (var group = 0; group < groupCount; group++)
                {
                    var cost = costs.Slice(group * size, size);
                    var sum = 0f;
                    foreach (var symbol in symbols[..used])
                    {
                        sum += histogram[symbol] * cost[symbol];
                    }

                    if (sum < bestCost)
                    {
                        bestCost = sum;
                        best = group;
                    }
                }

                groups[tile] = best;
            }

            // Recount, then drop the groups no tile uses
            SumGroups(tiles, size, groups[..tileCount], histograms[..(groupCount * size)]);
            var newCount = CompactGroups(histograms, groupCount, size, groups[..tileCount]);
            var newBits = EstimateAllGroups(histograms, newCount);
            if (newBits >= currentBits)
            {
                previous.CopyTo(groups);
                SumGroups(tiles, size, groups[..tileCount], histograms[..(groupCount * size)]);
                break;
            }

            currentBits = newBits;
            groupCount = newCount;
        }

        return groupCount;

        double EstimateAllGroups(ReadOnlySpan<uint> all, int count)
        {
            var bits = 0.0;
            for (var group = 0; group < count; group++)
            {
                bits += EstimateGroupBits(all.Slice(group * size, size), greenSize);
            }

            return bits;
        }
    }

    /// <summary>Removes the groups without symbols and renumbers the others in order.</summary>
    private static int CompactGroups(Span<uint> histograms, int groupCount, int size, Span<int> groups)
    {
        Span<int> mapping = stackalloc int[groupCount];
        var count = 0;
        for (var group = 0; group < groupCount; group++)
        {
            var histogram = histograms.Slice(group * size, size);
            if (histogram.ContainsAnyExcept(0u))
            {
                if (count != group)
                {
                    histogram.CopyTo(histograms.Slice(count * size, size));
                }

                mapping[group] = count++;
            }
            else
            {
                mapping[group] = -1;
            }
        }

        if (count != groupCount)
        {
            for (var tile = 0; tile < groups.Length; tile++)
            {
                if (groups[tile] >= 0)
                {
                    groups[tile] = mapping[groups[tile]];
                }
            }
        }

        return count;
    }

    /// <summary>Rents a zeroed buffer of <paramref name="count"/> items that the caller releases.</summary>
    private PooledBuffer RentSpan<T>(long count, out Span<T> items)
        where T : unmanaged
    {
        var bytes = count * unsafe(sizeof(T));
        if (bytes > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        var buffer = _scope.Rent((int)Math.Max(bytes, 1), AllocationKind.Temporary, clear: true);
        items = unsafe(MemoryMarshal.Cast<byte, T>(buffer.RawBuffer.AsSpan()))[..(int)count];
        return buffer;
    }

    /// <summary>Cuts spans of several types from one buffer, each aligned on 8 bytes.</summary>
    private ref struct SpanCarver(Span<byte> bytes)
    {
        private readonly Span<byte> _bytes = bytes;
        private int _offset;

        /// <summary>The bytes to reserve for a span of <paramref name="count"/> items, alignment included.</summary>
        public static long Size<T>(long count)
            where T : unmanaged => (count * unsafe(sizeof(T))) + 7;

        public Span<T> Take<T>(int count)
            where T : unmanaged
        {
            _offset = (_offset + 7) & ~7;
            var length = count * unsafe(sizeof(T));
            var span = unsafe(MemoryMarshal.Cast<byte, T>(_bytes.Slice(_offset, length)));
            _offset += length;
            return span;
        }
    }

    /// <summary>The meta prefix codes chosen for the main image.</summary>
    private sealed class EntropyImagePlan : IDisposable
    {
        private readonly PooledBuffer _buffer;
        private PooledBuffer? _histograms;
        private int _histogramLength;

        public EntropyImagePlan(Vp8LEncoder encoder, int bits, int tilesX, int tilesY)
        {
            Bits = bits;
            TilesX = tilesX;
            TilesY = tilesY;
            _buffer = encoder.RentSpan<uint>(2L * tilesX * tilesY, out _);
            Encoder = encoder;
        }

        private Vp8LEncoder Encoder { get; }

        /// <summary>Gets the tile size bits (2 to 9).</summary>
        public int Bits { get; }

        public int TilesX { get; }

        public int TilesY { get; }

        public int GroupCount { get; private set; }

        public double EstimatedBits { get; set; }

        /// <summary>Gets the entropy image: the group of each tile in the red and green components.</summary>
        public Span<uint> Image => unsafe(MemoryMarshal.Cast<byte, uint>(_buffer.RawBuffer.AsSpan()))[..(TilesX * TilesY)];

        /// <summary>Gets the group of each tile (-1 for an empty tile until the plan is complete).</summary>
        public Span<int> Groups => unsafe(MemoryMarshal.Cast<byte, int>(_buffer.RawBuffer.AsSpan())).Slice(TilesX * TilesY, TilesX * TilesY);

        /// <summary>Gets the histograms of the groups.</summary>
        public ReadOnlySpan<uint> Histograms => _histograms is null ? default : unsafe(MemoryMarshal.Cast<byte, uint>(_histograms.RawBuffer.AsSpan()))[.._histogramLength];

        public void SetHistograms(ReadOnlySpan<uint> histograms, int groupCount)
        {
            _histograms?.Dispose();
            _histograms = Encoder.RentSpan<uint>(histograms.Length, out var copy);
            histograms.CopyTo(copy);
            _histogramLength = histograms.Length;
            GroupCount = groupCount;
        }

        public void Dispose()
        {
            _buffer.Dispose();
            _histograms?.Dispose();
        }
    }
}
