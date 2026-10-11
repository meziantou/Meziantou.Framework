using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A JPEG Huffman decoding table (ITU-T T.81 section C and F.2.2.3) built from a DHT definition: canonical codes of 1 to 16
/// bits assigned in order of increasing length. Codes of at most <see cref="LookupBits"/> bits are resolved by one table
/// lookup; longer codes by the per-length maximum codes of F.2.2.3.
/// </summary>
internal sealed class JpegHuffmanTable
{
    /// <summary>The number of bits resolved by the lookup table.</summary>
    public const int LookupBits = 9;

    // (code length << 8) | symbol, or 0 when the code is longer than LookupBits
    private readonly ushort[] _lookup = new ushort[1 << LookupBits];

    // Largest code of each length (index 1 to 16), -1 when there is none
    private readonly int[] _maxCode = new int[17];

    // Index of the first symbol of each length minus the first code of that length
    private readonly int[] _valueOffset = new int[17];

    private readonly byte[] _values;

    private JpegHuffmanTable(byte[] values)
    {
        _values = values;
    }

    /// <summary>Builds a table from the 16 code counts and the symbols of a DHT definition.</summary>
    /// <param name="counts">The number of codes of each length (1 to 16).</param>
    /// <param name="values">The symbols, in order of increasing code length.</param>
    /// <param name="error">The reason the definition is invalid.</param>
    /// <returns>The table, or <see langword="null"/> if the definition is invalid.</returns>
    public static JpegHuffmanTable? TryCreate(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values, out string? error)
    {
        if (counts.Length != 16)
            throw new ArgumentException("A Huffman table has 16 code counts.", nameof(counts));

        var total = 0;
        foreach (var count in counts)
        {
            total += count;
        }

        if (total != values.Length)
            throw new ArgumentException("The symbol count does not match the code counts.", nameof(values));

        if (total == 0)
        {
            error = "The JPEG Huffman table defines no code.";
            return null;
        }

        if (total > 256)
        {
            error = "The JPEG Huffman table defines more than 256 codes.";
            return null;
        }

        var table = new JpegHuffmanTable(values.ToArray());
        var code = 0;
        var index = 0;
        for (var length = 1; length <= 16; length++)
        {
            var count = counts[length - 1];
            if (count == 0)
            {
                table._maxCode[length] = -1;
            }
            else
            {
                // The codes of this length must fit in 'length' bits (otherwise the table is oversubscribed)
                if (code + count > (1 << length))
                {
                    error = "The JPEG Huffman table is oversubscribed (its code lengths do not form a prefix code).";
                    return null;
                }

                table._valueOffset[length] = index - code;
                for (var i = 0; i < count; i++)
                {
                    if (length <= LookupBits)
                    {
                        // Every lookup index whose first bits are this code
                        var shift = LookupBits - length;
                        var first = code << shift;
                        var entry = (ushort)((length << 8) | values[index]);
                        table._lookup.AsSpan(first, 1 << shift).Fill(entry);
                    }

                    code++;
                    index++;
                }

                table._maxCode[length] = code - 1;
            }

            code <<= 1;
        }

        error = null;
        return table;
    }

    /// <summary>Gets the lookup entry of the next <see cref="LookupBits"/> bits: <c>(length &lt;&lt; 8) | symbol</c>, or 0 for a longer code.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Lookup(int bits) => _lookup[bits];

    /// <summary>Resolves a code longer than <see cref="LookupBits"/> from the next 16 bits.</summary>
    /// <param name="bits16">The next 16 bits, most significant first.</param>
    /// <param name="length">The code length.</param>
    /// <returns>The symbol, or -1 if no code matches.</returns>
    public int DecodeLong(int bits16, out int length)
    {
        for (length = LookupBits + 1; length <= 16; length++)
        {
            var code = bits16 >> (16 - length);
            if (code <= _maxCode[length])
                return _values[_valueOffset[length] + code];
        }

        length = 0;
        return -1;
    }
}
