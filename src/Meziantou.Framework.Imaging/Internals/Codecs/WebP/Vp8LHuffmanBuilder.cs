namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Builds the length-limited canonical prefix codes of the WebP lossless encoder from symbol histograms: Huffman code
/// lengths (two-queue construction over the symbols sorted by count, deterministic ties), limited to a maximum length by
/// raising the smallest counts until the tree fits, then the canonical codes in the decoder's bit order.
/// </summary>
internal static class Vp8LHuffmanBuilder
{
    /// <summary>Computes the code lengths of a histogram.</summary>
    /// <param name="histogram">The count of each symbol.</param>
    /// <param name="maxLength">The maximum code length (15 for symbols, 7 for the code-length code).</param>
    /// <param name="lengths">Receives the length of each symbol: 0 for unused symbols; a single used symbol gets length 1 (a zero-bit code for the decoder).</param>
    public static void BuildLengths(ReadOnlySpan<uint> histogram, int maxLength, Span<byte> lengths)
    {
        lengths.Clear();
        var used = 0;
        var last = -1;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] != 0)
            {
                used++;
                last = i;
            }
        }

        if (used == 0)
            return;

        if (used == 1)
        {
            lengths[last] = 1;
            return;
        }

        var symbols = new int[used];
        var counts = new long[used];
        var index = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] != 0)
            {
                symbols[index++] = i;
            }
        }

        // Raise the smallest counts until the tree depth fits (the counts keep their relative order)
        for (long minimum = 1; ; minimum *= 2)
        {
            for (var i = 0; i < used; i++)
            {
                counts[i] = Math.Max(histogram[symbols[i]], minimum);
            }

            if (TryBuild(symbols, counts, maxLength, lengths))
                return;
        }
    }

    /// <summary>Assigns the canonical codes (shorter first, then increasing symbol), bit-reversed for least-significant-bit-first writing.</summary>
    public static void BuildCodes(ReadOnlySpan<byte> lengths, Span<ushort> codes)
    {
        Span<int> counts = stackalloc int[16];
        counts.Clear();
        foreach (var length in lengths)
        {
            counts[length]++;
        }

        counts[0] = 0;
        Span<int> next = stackalloc int[16];
        var code = 0;
        for (var length = 1; length < 16; length++)
        {
            code = (code + counts[length - 1]) << 1;
            next[length] = code;
        }

        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            int length = lengths[symbol];
            codes[symbol] = length == 0 ? (ushort)0 : (ushort)Reverse(next[length]++, length);
        }
    }

    private static bool TryBuild(int[] symbols, long[] counts, int maxLength, Span<byte> lengths)
    {
        var leafCount = symbols.Length;

        // Leaves sorted by count, then symbol; internal nodes are created in nondecreasing weight order
        var order = new int[leafCount];
        for (var i = 0; i < leafCount; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            var compare = counts[a].CompareTo(counts[b]);
            return compare != 0 ? compare : symbols[a].CompareTo(symbols[b]);
        });

        var nodeCount = (2 * leafCount) - 1;
        var weights = new long[nodeCount];
        var parents = new int[nodeCount];
        for (var i = 0; i < leafCount; i++)
        {
            weights[i] = counts[order[i]];
        }

        var leafIndex = 0;
        var internalIndex = leafCount;
        var next = leafCount;
        while (next < nodeCount)
        {
            var first = Take(ref leafIndex, ref internalIndex, next, leafCount, weights);
            var second = Take(ref leafIndex, ref internalIndex, next, leafCount, weights);
            weights[next] = weights[first] + weights[second];
            parents[first] = next;
            parents[second] = next;
            next++;
        }

        // Depth of each leaf: walk to the root (the last node)
        var depths = new int[nodeCount];
        for (var node = nodeCount - 2; node >= 0; node--)
        {
            depths[node] = depths[parents[node]] + 1;
        }

        for (var i = 0; i < leafCount; i++)
        {
            if (depths[i] > maxLength)
                return false;
        }

        for (var i = 0; i < leafCount; i++)
        {
            lengths[symbols[order[i]]] = (byte)depths[i];
        }

        return true;
    }

    private static int Take(ref int leafIndex, ref int internalIndex, int next, int leafCount, long[] weights)
    {
        // Prefer a leaf on ties (shallower trees)
        if (leafIndex < leafCount && (internalIndex >= next || weights[leafIndex] <= weights[internalIndex]))
            return leafIndex++;

        return internalIndex++;
    }

    private static int Reverse(int code, int length)
    {
        var result = 0;
        for (var i = 0; i < length; i++)
        {
            result = (result << 1) | (code & 1);
            code >>= 1;
        }

        return result;
    }
}
