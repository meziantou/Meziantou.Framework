using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Builds the Huffman table of a JPEG scan from the statistics of its symbols (ITU-T T.81 section K.2): code lengths of at
/// most 16 bits, and no code made of 1-bits only (section C.2).
/// </summary>
/// <remarks>
/// The lengths are those of a length-limited Huffman code (<see cref="CanonicalHuffmanBuilder"/>) over the symbols and one
/// more, reserved symbol counted once. The reserved symbol is given one of the longest codes and comes last in the
/// canonical order, so it takes the code of 1-bits and no symbol of the scan does.
/// </remarks>
internal static class JpegHuffmanCodeBuilder
{
    private const int ReservedSymbol = 256;

    /// <summary>Builds the table.</summary>
    /// <param name="histogram">The number of occurrences of each of the 256 symbols; at least one is not zero.</param>
    /// <returns>The table, which defines a code for every symbol that occurs.</returns>
    public static JpegHuffmanEncoder Build(ReadOnlySpan<uint> histogram)
    {
        Debug.Assert(histogram.Length == ReservedSymbol);
        Span<uint> counts = stackalloc uint[ReservedSymbol + 1];
        histogram.CopyTo(counts);
        counts[ReservedSymbol] = 1;
        Span<byte> lengths = stackalloc byte[ReservedSymbol + 1];
        CanonicalHuffmanBuilder.BuildLengths(counts, maxLength: 16, lengths);

        // The reserved symbol has the smallest count, so a symbol with a longer code has the same (possibly raised) count:
        // exchanging their lengths keeps the code as short
        var longest = lengths[ReservedSymbol];
        var longestSymbol = ReservedSymbol;
        for (var symbol = 0; symbol < ReservedSymbol; symbol++)
        {
            if (lengths[symbol] > longest)
            {
                longest = lengths[symbol];
                longestSymbol = symbol;
            }
        }

        (lengths[ReservedSymbol], lengths[longestSymbol]) = (lengths[longestSymbol], lengths[ReservedSymbol]);

        Span<byte> bits = stackalloc byte[16];
        bits.Clear();
        Span<byte> values = stackalloc byte[ReservedSymbol];
        var count = 0;
        for (var length = 1; length <= 16; length++)
        {
            for (var symbol = 0; symbol < ReservedSymbol; symbol++)
            {
                if (lengths[symbol] == length)
                {
                    bits[length - 1]++;
                    values[count++] = (byte)symbol;
                }
            }
        }

        Debug.Assert(count > 0);
        return new JpegHuffmanEncoder(bits, values[..count]);
    }

    /// <summary>
    /// Builds the table by the procedure of ITU-T T.81 section K.2, which libjpeg follows: the Huffman code sizes over the
    /// symbols and the reserved one (figure K.1: the two smallest nonzero counts merged first, the larger symbol on ties),
    /// the number of codes of each size (figure K.2), the codes longer than 16 bits moved two at a time to shorter lengths
    /// (figure K.3), the code of the reserved symbol removed from the longest length, and the symbols sorted by code size
    /// then value (figure K.4).
    /// </summary>
    /// <param name="histogram">The number of occurrences of each of the 256 symbols; at least one is not zero.</param>
    /// <returns>The table, which defines a code for every symbol that occurs.</returns>
    /// <remarks>
    /// The progressive scans use <see cref="Build"/>, whose limitation to 16 bits is coarser; this procedure is kept for the
    /// baseline scan with optimized tables, where it gives the tables other encoders write for the same statistics.
    /// </remarks>
    public static JpegHuffmanEncoder BuildAnnexK(ReadOnlySpan<uint> histogram)
    {
        // The depth of a tree of 257 leaves: libjpeg stops at 32 bits, which counts that grow like the Fibonacci numbers exceed
        const int MaxCodeSize = ReservedSymbol;
        Debug.Assert(histogram.Length == ReservedSymbol);
        Span<long> frequencies = stackalloc long[ReservedSymbol + 1];
        for (var symbol = 0; symbol < ReservedSymbol; symbol++)
        {
            frequencies[symbol] = histogram[symbol];
        }

        frequencies[ReservedSymbol] = 1;
        Span<int> codeSizes = stackalloc int[ReservedSymbol + 1];
        codeSizes.Clear();
        Span<int> others = stackalloc int[ReservedSymbol + 1];
        others.Fill(-1);
        while (true)
        {
            var first = FindSmallest(frequencies, excluded: -1);
            var second = FindSmallest(frequencies, excluded: first);
            if (second < 0)
                break;

            // The second tree is merged into the first: every symbol of both gets one more bit
            frequencies[first] += frequencies[second];
            frequencies[second] = 0;
            codeSizes[first]++;
            while (others[first] >= 0)
            {
                first = others[first];
                codeSizes[first]++;
            }

            others[first] = second;
            codeSizes[second]++;
            while (others[second] >= 0)
            {
                second = others[second];
                codeSizes[second]++;
            }
        }

        Span<int> bits = stackalloc int[MaxCodeSize + 1];
        bits.Clear();
        foreach (var size in codeSizes)
        {
            if (size > 0)
            {
                bits[size]++;
            }
        }

        // Figure K.3: the two symbols of the longest size share a prefix one bit shorter; one of them takes the prefix, and a
        // code of the next shorter size in use becomes the prefix of the other and of the code it replaces
        var length = MaxCodeSize;
        for (; length > 16; length--)
        {
            while (bits[length] > 0)
            {
                var prefix = length - 2;
                while (bits[prefix] == 0)
                {
                    prefix--;
                }

                bits[length] -= 2;
                bits[length - 1]++;
                bits[prefix + 1] += 2;
                bits[prefix]--;
            }
        }

        while (bits[length] == 0)
        {
            length--;
        }

        bits[length]--;

        Span<byte> counts = stackalloc byte[16];
        for (var size = 1; size <= 16; size++)
        {
            counts[size - 1] = (byte)bits[size];
        }

        // Figure K.4: the symbols in the order of their code sizes before the adjustment, then of their values
        Span<byte> values = stackalloc byte[ReservedSymbol];
        var count = 0;
        for (var size = 1; size <= MaxCodeSize; size++)
        {
            for (var symbol = 0; symbol < ReservedSymbol; symbol++)
            {
                if (codeSizes[symbol] == size)
                {
                    values[count++] = (byte)symbol;
                }
            }
        }

        Debug.Assert(count > 0);
        return new JpegHuffmanEncoder(counts, values[..count]);

        static int FindSmallest(ReadOnlySpan<long> frequencies, int excluded)
        {
            var found = -1;
            var smallest = long.MaxValue;
            for (var symbol = 0; symbol < frequencies.Length; symbol++)
            {
                if (frequencies[symbol] != 0 && frequencies[symbol] <= smallest && symbol != excluded)
                {
                    smallest = frequencies[symbol];
                    found = symbol;
                }
            }

            return found;
        }
    }
}
