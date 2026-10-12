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
}
