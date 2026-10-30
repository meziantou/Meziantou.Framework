namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The layout of the symbol histograms of a prefix code group, and the estimates of their coded size.</summary>
internal sealed partial class Vp8LEncoder
{
    /// <summary>The size of the green alphabet: 256 literals, 24 length prefixes and the color cache indexes.</summary>
    private static int GreenAlphabetSize(int cacheBits) => NumLiteralCodes + NumLengthCodes + (cacheBits == 0 ? 0 : 1 << cacheBits);

    /// <summary>
    /// The number of entries of the histograms of a group: green (<see cref="GreenAlphabetSize"/>), red, blue, alpha (256
    /// each) and distance (40), in this order.
    /// </summary>
    private static int HistogramSize(int cacheBits) => GreenAlphabetSize(cacheBits) + (3 * NumLiteralCodes) + NumDistanceCodes;

    /// <summary>The estimated size, in bits, of the five prefix codes of a group and of the symbols it codes (extra bits excluded).</summary>
    private static double EstimateGroupBits(ReadOnlySpan<uint> histogram, int greenSize)
    {
        return EstimateAlphabetBits(histogram[..greenSize], default)
            + EstimateAlphabetBits(histogram.Slice(greenSize, NumLiteralCodes), default)
            + EstimateAlphabetBits(histogram.Slice(greenSize + NumLiteralCodes, NumLiteralCodes), default)
            + EstimateAlphabetBits(histogram.Slice(greenSize + (2 * NumLiteralCodes), NumLiteralCodes), default)
            + EstimateAlphabetBits(histogram.Slice(greenSize + (3 * NumLiteralCodes), NumDistanceCodes), default);
    }

    /// <summary>The estimated size of the group that would code the symbols of two histograms.</summary>
    private static double EstimateMergedGroupBits(ReadOnlySpan<uint> first, ReadOnlySpan<uint> second, int greenSize)
    {
        var red = greenSize;
        var blue = red + NumLiteralCodes;
        var alpha = blue + NumLiteralCodes;
        var distance = alpha + NumLiteralCodes;
        return EstimateAlphabetBits(first[..greenSize], second[..greenSize])
            + EstimateAlphabetBits(first.Slice(red, NumLiteralCodes), second.Slice(red, NumLiteralCodes))
            + EstimateAlphabetBits(first.Slice(blue, NumLiteralCodes), second.Slice(blue, NumLiteralCodes))
            + EstimateAlphabetBits(first.Slice(alpha, NumLiteralCodes), second.Slice(alpha, NumLiteralCodes))
            + EstimateAlphabetBits(first.Slice(distance, NumDistanceCodes), second.Slice(distance, NumDistanceCodes));
    }

    /// <summary>
    /// The estimated size of one prefix code and its symbols (the counts of <paramref name="counts"/>, plus those of
    /// <paramref name="other"/> when it is not empty).
    /// </summary>
    /// <remarks>
    /// The symbols cost their Shannon information, but at least one bit each (a prefix code cannot do better), and nothing
    /// when a single symbol is used (a zero-bit code). The code itself costs about 40 bits, 3 per used symbol and 7 per run
    /// of unused symbols (the run-length coding of the code lengths); a simple code of one or two literals costs 11 or 19.
    /// </remarks>
    private static double EstimateAlphabetBits(ReadOnlySpan<uint> counts, ReadOnlySpan<uint> other)
    {
        var merged = !other.IsEmpty;
        long total = 0;
        var used = 0;
        var gaps = 0;
        var previousUsed = true;
        var last = -1;
        for (var i = 0; i < counts.Length; i++)
        {
            var count = merged ? counts[i] + other[i] : counts[i];
            if (count == 0)
            {
                if (previousUsed)
                {
                    gaps++;
                }

                previousUsed = false;
                continue;
            }

            total += count;
            used++;
            last = i;
            previousUsed = true;
        }

        if (used <= 1)
            return used == 0 || last < 2 ? 4 : 11;

        if (used == 2 && last < NumLiteralCodes)
            return 19 + total;

        var log2Total = Log2Count(total);
        var bits = 40.0 + (3.0 * used) + (7.0 * gaps);
        for (var i = 0; i < counts.Length; i++)
        {
            var count = merged ? counts[i] + other[i] : counts[i];
            if (count != 0)
            {
                bits += Math.Max(count * (log2Total - Log2Count(count)), count);
            }
        }

        return bits;
    }

    private static double Log2Count(long value) => value < Log2Table.Length ? Log2Table[value] : Math.Log2(value);

    /// <summary>
    /// The cost, in bits, of each symbol of an alphabet coded with a prefix code built from <paramref name="counts"/>: its
    /// Shannon information (at least one bit), nothing for the only used symbol, and more than the rarest used symbol for an
    /// unused one.
    /// </summary>
    private static void ComputeSymbolCosts(ReadOnlySpan<uint> counts, Span<float> costs)
    {
        long total = 0;
        var used = 0;
        foreach (var count in counts)
        {
            total += count;
            if (count != 0)
            {
                used++;
            }
        }

        if (total == 0)
        {
            costs.Fill(8);
            return;
        }

        var log2Total = Log2Count(total);
        var unused = (float)(log2Total + 2);
        for (var i = 0; i < counts.Length; i++)
        {
            var count = counts[i];
            costs[i] = count == 0 ? unused : used == 1 ? 0 : (float)Math.Max(1, log2Total - Log2Count(count));
        }
    }
}
