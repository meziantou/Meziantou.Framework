using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The parse of the main image that minimizes its estimated size: a shortest path over the pixels, where coding a pixel as a
/// literal (or a color cache index) or starting a backward reference of a given length and distance costs the bits of its
/// symbols under the statistics of the previous parse.
/// </summary>
/// <remarks>
/// <para>
/// The decoder inserts every pixel in the color cache, literal or copied, so whether a pixel is in the cache does not depend
/// on the parse: the cost of a literal is exact for the statistics. At each pixel the candidate references are the left and
/// top neighbors and the longest match of the hash chains of the first parse; a candidate covers the lengths that no cheaper
/// distance covers. Lengths up to 32 are tried one by one, longer ones only at the end of each length prefix and at the full
/// length (the length cost is constant within a prefix). A match is extended from the previous pixel instead of searched
/// again, and the chains (64 entries at efforts 5 and 6, 128 at 7 and 8, 512 at 9) are not searched while a match of 16
/// pixels or more is known. Below effort 9, a match of 128 pixels or more is taken as it is: the pixels it covers start no
/// reference (visiting them made the parse of flat images several times slower for 0.2 % of size).
/// </para>
/// <para>
/// The parse is kept when its statistics are estimated smaller than those of the previous one; a second round uses the
/// statistics of the first at effort 9. Memory: the arrival of each pixel (4 bytes per pixel), into which the tokens are
/// traced; the costs are a ring of 8,192 entries (a reference is at most 4,096 pixels long).
/// </para>
/// </remarks>
internal sealed partial class Vp8LEncoder
{
    private const int CostRingSize = 8192;
    private const int ShortLengths = 32;
    private const int CommitLength = 128;
    private const int SkipLength = 16;

    private int CostModelRounds => _effort switch
    {
        < 5 => 0,
        < 9 => 1,
        _ => 2,
    };

    /// <summary>
    /// Parses the pixels again with the costs of <paramref name="statistics"/>, and keeps the parse when it is estimated smaller.
    /// <paramref name="previous"/> holds the hash chains of the first parse (every position inserted).
    /// </summary>
    /// <returns>The number of tokens of the kept parse.</returns>
    private int ParseWithCostModel(ReadOnlySpan<uint> pixels, int width, ReadOnlySpan<uint> previous, Span<uint> tokens, int tokenCount, ref Statistics statistics)
    {
        var count = pixels.Length;
        if (count < MinLength)
            return tokenCount;

        using var arrivalBuffer = RentScratch(count + 1L, out var arrivals);
        var bestBits = statistics.EstimateBits();
        for (var round = 0; round < CostModelRounds; round++)
        {
            var model = new CostModel(statistics);
            FindShortestPath(pixels, width, previous, model, statistics.CacheBits, arrivals);
            var candidates = TraceArrivals(arrivals);
            var candidateCount = candidates.Length;
            var candidateStatistics = new Statistics(statistics.CacheBits);
            var sink = new StatisticsSink([candidateStatistics]);
            Replay(pixels, candidates, candidateCount, ref sink);
            var bits = candidateStatistics.EstimateBits();
            if (bits >= bestBits)
                break;

            candidates[..candidateCount].CopyTo(tokens);
            tokenCount = candidateCount;
            statistics = candidateStatistics;
            bestBits = bits;
        }

        return tokenCount;
    }

    private void FindShortestPath(ReadOnlySpan<uint> pixels, int width, ReadOnlySpan<uint> previous, CostModel model, int cacheBits, Span<uint> arrivals)
    {
        var count = pixels.Length;
        var ring = new double[CostRingSize];
        ring.AsSpan().Fill(double.PositiveInfinity);
        ring[0] = 0;
        var cache = new uint[cacheBits == 0 ? 0 : 1 << cacheBits];
        var neighborCodes = NeighborCodes;
        var chainDepth = _effort switch
        {
            <= 6 => 64,
            <= 8 => 128,
            _ => 512,
        };

        var commitLength = _effort >= 9 ? int.MaxValue : CommitLength;
        Span<(int Distance, int Length, double Cost, int Code)> matches = stackalloc (int, int, double, int)[3];
        var leftLength = 0;
        var topLength = 0;
        var chainDistance = 0;
        var chainLength = 0;
        for (var position = 0; position < count; position++)
        {
            if ((position & 0xFFFF) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            ref var slot = ref ring[position & (CostRingSize - 1)];
            var cost = slot;
            slot = double.PositiveInfinity;

            // A literal, or the color cache index when the cache holds the pixel
            var argb = pixels[position];
            double literal;
            if (cache.Length != 0)
            {
                var key = (int)Vp8LDecoder.ColorCacheHash(argb, cacheBits);
                literal = cache[key] == argb ? model.Green[NumLiteralCodes + NumLengthCodes + key] : model.Literal(argb);
                cache[key] = argb;
            }
            else
            {
                literal = model.Literal(argb);
            }

            Relax(ring, arrivals, position + 1, cost + literal, 0);

            var maxLength = Math.Min(MaxLength, count - position);
            if (maxLength < MinLength)
            {
                leftLength = topLength = chainLength = 0;
                continue;
            }

            leftLength = Extend(pixels, position, 1, leftLength, maxLength);
            topLength = width > 1 ? Extend(pixels, position, width, topLength, maxLength) : 0;
            if (chainLength > 1 && chainLength - 1 >= SkipLength)
            {
                chainLength = Extend(pixels, position, chainDistance, chainLength, maxLength);
            }
            else if (Math.Max(leftLength, topLength) >= SkipLength)
            {
                chainLength = 0;
            }
            else
            {
                (chainDistance, chainLength) = SearchChain(pixels, previous, position, width, maxLength, chainDepth, Math.Max(leftLength, topLength));
            }

            // The candidates by increasing distance cost; each covers the lengths beyond those of the cheaper ones
            var matchCount = 0;
            AddMatch(matches, ref matchCount, 1, leftLength, width, model, neighborCodes);
            AddMatch(matches, ref matchCount, width, topLength, width, model, neighborCodes);
            AddMatch(matches, ref matchCount, chainDistance, chainLength, width, model, neighborCodes);
            for (var i = 1; i < matchCount; i++)
            {
                for (var j = i; j > 0 && matches[j].Cost < matches[j - 1].Cost; j--)
                {
                    (matches[j], matches[j - 1]) = (matches[j - 1], matches[j]);
                }
            }

            var covered = MinLength - 1;
            foreach (var (_, length, distanceCost, code) in matches[..matchCount])
            {
                if (length <= covered)
                    continue;

                var start = cost + distanceCost;
                for (var current = covered + 1; current <= length; current = NextLength(current, length))
                {
                    Relax(ring, arrivals, position + current, start + model.Lengths[current], CreateCopyToken(current, code));
                }

                covered = length;
            }

            // A long match is taken as it is: the pixels it covers do not start other references (they still enter the
            // color cache, and their costs are reset for the ring)
            if (covered >= commitLength)
            {
                for (var skipped = position + 1; skipped < position + covered; skipped++)
                {
                    ring[skipped & (CostRingSize - 1)] = double.PositiveInfinity;
                    if (cache.Length != 0)
                    {
                        var value = pixels[skipped];
                        cache[(int)Vp8LDecoder.ColorCacheHash(value, cacheBits)] = value;
                    }
                }

                leftLength = Math.Max(0, leftLength - (covered - 1));
                topLength = Math.Max(0, topLength - (covered - 1));
                chainLength = Math.Max(0, chainLength - (covered - 1));
                position += covered - 1;
            }
        }

        static void Relax(double[] ring, Span<uint> arrivals, int target, double cost, uint arrival)
        {
            ref var slot = ref ring[target & (CostRingSize - 1)];
            if (cost < slot)
            {
                slot = cost;
                arrivals[target] = arrival;
            }
        }

        static void AddMatch(Span<(int, int, double, int)> matches, ref int matchCount, int distance, int length, int width, CostModel model, ReadOnlySpan<byte> neighborCodes)
        {
            if (length < MinLength)
                return;

            for (var i = 0; i < matchCount; i++)
            {
                if (matches[i].Item1 == distance)
                    return;
            }

            var code = GetDistanceCode(distance, width, neighborCodes);
            matches[matchCount++] = (distance, length, model.DistanceCost(code), code);
        }
    }

    /// <summary>
    /// The length of the match at <paramref name="distance"/>, from the length of the match of the previous pixel at the same
    /// distance (one shorter, then extended).
    /// </summary>
    private static int Extend(ReadOnlySpan<uint> pixels, int position, int distance, int previousLength, int maxLength)
    {
        if (distance > position || distance > MaxDistance)
            return 0;

        var length = Math.Min(Math.Max(previousLength - 1, 0), maxLength);
        if (length < maxLength)
        {
            length += pixels.Slice(position - distance + length, maxLength - length).CommonPrefixLength(pixels.Slice(position + length, maxLength - length));
        }

        return length;
    }

    /// <summary>The longest match of the hash chain of a position that is longer than <paramref name="minimum"/>, or (0, 0).</summary>
    private static (int Distance, int Length) SearchChain(ReadOnlySpan<uint> pixels, ReadOnlySpan<uint> previous, int position, int width, int maxLength, int depth, int minimum)
    {
        var bestLength = minimum;
        var bestDistance = 0;
        var candidate = previous[position];
        while (candidate != uint.MaxValue && depth-- > 0 && bestLength < maxLength)
        {
            var distance = position - (int)candidate;
            if (distance > MaxDistance)
                break;

            if (distance != 1 && distance != width && pixels[(int)candidate + bestLength] == pixels[position + bestLength])
            {
                var length = pixels.Slice((int)candidate, maxLength).CommonPrefixLength(pixels.Slice(position, maxLength));
                if (length > bestLength)
                {
                    bestLength = length;
                    bestDistance = distance;
                }
            }

            candidate = previous[(int)candidate];
        }

        return bestDistance == 0 ? (0, 0) : (bestDistance, bestLength);
    }

    /// <summary>
    /// The next length to try after <paramref name="current"/>: the next one up to <see cref="ShortLengths"/>, then the last
    /// length of the next prefix (5-6, 7-8, 9-12, ... share a prefix: the last ones are the powers of two and their multiples
    /// by 1.5), and <paramref name="length"/> itself last.
    /// </summary>
    private static int NextLength(int current, int length)
    {
        if (current < ShortLengths || current >= length)
            return current + 1;

        var power = 1 << BitOperations.Log2((uint)current);
        var next = current < power + (power >> 1) ? power + (power >> 1) : power << 1;
        return Math.Min(next, length);
    }

    /// <summary>Converts the arrivals of the shortest path to tokens, merging the literals into runs, in place.</summary>
    /// <returns>The tokens, at the end of <paramref name="arrivals"/>.</returns>
    /// <remarks>
    /// Traced from the end, the tokens are written from the end of the buffer backward. Every token covers at least one pixel
    /// whose arrival was read before the token is written, so there are never more tokens written than arrivals read, and the
    /// write position stays above the next arrival to read.
    /// </remarks>
    private static ReadOnlySpan<uint> TraceArrivals(Span<uint> arrivals)
    {
        var tokens = arrivals;
        var write = tokens.Length;
        var literals = 0;
        for (var position = arrivals.Length - 1; position > 0;)
        {
            var arrival = arrivals[position];
            if (arrival == 0)
            {
                position--;
                if (++literals == MaxLiteralRun)
                {
                    tokens[--write] = CreateLiteralRunToken(literals);
                    literals = 0;
                }

                continue;
            }

            if (literals > 0)
            {
                tokens[--write] = CreateLiteralRunToken(literals);
                literals = 0;
            }

            tokens[--write] = arrival;
            position -= (int)(arrival >> TokenLengthShift) + 1;
        }

        if (literals > 0)
        {
            tokens[--write] = CreateLiteralRunToken(literals);
        }

        return tokens[write..];
    }

    /// <summary>The cost, in bits, of the symbols under the statistics of a parse.</summary>
    private sealed class CostModel
    {
        private readonly float[] _red = new float[NumLiteralCodes];
        private readonly float[] _blue = new float[NumLiteralCodes];
        private readonly float[] _alpha = new float[NumLiteralCodes];
        private readonly float[] _distance = new float[NumDistanceCodes];

        public CostModel(Statistics statistics)
        {
            Green = new float[statistics.Green.Length];
            ComputeSymbolCosts(statistics.Green, Green);
            ComputeSymbolCosts(statistics.Red, _red);
            ComputeSymbolCosts(statistics.Blue, _blue);
            ComputeSymbolCosts(statistics.Alpha, _alpha);
            ComputeSymbolCosts(statistics.Distance, _distance);
            Lengths = new float[MaxLength + 1];
            for (var length = 1; length <= MaxLength; length++)
            {
                var (prefix, extraBits, _) = GetPrefix(length);
                Lengths[length] = Green[NumLiteralCodes + prefix] + extraBits;
            }
        }

        public float[] Green { get; }

        /// <summary>Gets the cost of each length (its prefix and extra bits).</summary>
        public float[] Lengths { get; }

        public double Literal(uint argb) => Green[(argb >> 8) & 0xFF] + _red[(argb >> 16) & 0xFF] + _blue[argb & 0xFF] + _alpha[argb >> 24];

        public double DistanceCost(int code)
        {
            var (prefix, extraBits, _) = GetPrefix(code);
            return _distance[prefix] + extraBits;
        }
    }
}
