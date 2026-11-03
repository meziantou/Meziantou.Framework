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

    /// <summary>The number of bits resolved by <see cref="FastLookup"/>.</summary>
    public const int FastLookupBits = 10;

    /// <summary>The flag of the <see cref="FastLookup"/> entries that also resolve the magnitude bits of their symbol.</summary>
    public const int FastLookupComplete = 1 << 10;

    /// <summary>The position of the symbol (8 bits) in a <see cref="FastLookup"/> entry.</summary>
    public const int FastLookupSymbolShift = 11;

    /// <summary>The position of the coefficient (signed, 11 bits) in a complete <see cref="FastLookup"/> entry.</summary>
    public const int FastLookupValueShift = 21;

    // Entries of FastAc, which no bit pattern decodes: the regular path decodes every coefficient
    private static readonly int[] NoFastAc = new int[1 << LookupBits];

    // (code length << 8) | symbol, or 0 when the code is longer than LookupBits
    private readonly ushort[] _lookup = new ushort[1 << LookupBits];

    // AC tables: (coefficient << 8) | (run << 4) | (code length + size) for the coefficients whose code and magnitude bits
    // fit in LookupBits, 0 for the other bit patterns (end of block, ZRL, longer codes or magnitudes); built by PrepareFastAc
    private int[]? _fastAc;

    private bool _isAc;

    // The code counts of the definition (1 to 16 bits) and the table built from them by BuildFastLookup
    private readonly byte[] _counts;
    private int[]? _fastLookup;

    // Largest code of each length (index 1 to 16), -1 when there is none
    private readonly int[] _maxCode = new int[17];

    // Index of the first symbol of each length minus the first code of that length
    private readonly int[] _valueOffset = new int[17];

    private readonly byte[] _values;

    private JpegHuffmanTable(byte[] counts, byte[] values)
    {
        _counts = counts;
        _values = values;
    }

    /// <summary>Builds a table from the 16 code counts and the symbols of a DHT definition.</summary>
    /// <param name="counts">The number of codes of each length (1 to 16).</param>
    /// <param name="values">The symbols, in order of increasing code length.</param>
    /// <param name="error">The reason the definition is invalid.</param>
    /// <param name="ac">Whether the table codes AC coefficients: <see cref="PrepareFastAc"/> then builds <see cref="FastAc"/>.</param>
    /// <returns>The table, or <see langword="null"/> if the definition is invalid.</returns>
    public static JpegHuffmanTable? TryCreate(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values, out string? error, bool ac = false)
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

        var table = new JpegHuffmanTable(counts.ToArray(), values.ToArray());
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

        table._isAc = ac;
        error = null;
        return table;
    }

    /// <summary>
    /// Gets, for AC tables, the coefficients resolved by the next <see cref="LookupBits"/> bits:
    /// <c>(coefficient &lt;&lt; 8) | (run &lt;&lt; 4) | length</c>, where <c>length</c> counts the bits of the code and of the
    /// magnitude (T.81 F.2.2.1, EXTEND applied), or 0 when the symbol is an end of block or a ZRL, or when its code or its
    /// magnitude bits do not fit. Until <see cref="PrepareFastAc"/> is called, and for DC tables, every entry is 0.
    /// </summary>
    public int[] FastAc => _fastAc ?? NoFastAc;

    /// <summary>Builds the entries of <see cref="FastAc"/> of an AC table, which pay for themselves on large images only.</summary>
    public void PrepareFastAc()
    {
        if (_isAc)
        {
            _fastAc ??= BuildFastAc(_lookup);
        }
    }

    private static int[] BuildFastAc(ushort[] lookup)
    {
        var fastAc = new int[1 << LookupBits];
        for (var bits = 0; bits < fastAc.Length; bits++)
        {
            var entry = lookup[bits];
            var length = entry >> 8;
            var size = entry & 0x0F;
            if (entry == 0 || size == 0 || length + size > LookupBits)
                continue;

            var run = (entry >> 4) & 0x0F;
            var magnitude = (bits >> (LookupBits - length - size)) & ((1 << size) - 1);
            var coefficient = magnitude < (1 << (size - 1)) ? magnitude - (1 << size) + 1 : magnitude;
            fastAc[bits] = (coefficient << 8) | (run << 4) | (length + size);
        }

        return fastAc;
    }

    /// <summary>
    /// Gets the entries of the next <see cref="FastLookupBits"/> bits (built on first use), which resolve a symbol and, when
    /// they fit too, the magnitude bits of its coefficient (T.81 F.2.2.1, EXTEND applied) in one lookup. Bits 0 to 4 of an
    /// entry are the number of bits it consumes (bit 5 is 0, so that a shift by the entry consumes them): the code, or the
    /// code and the magnitude bits when <see cref="FastLookupComplete"/> is set; bits 11 to 18 are the symbol, and bits 21 to
    /// 31 the signed coefficient of a complete entry (0 for a symbol without magnitude bits: DC category 0, end of block,
    /// ZRL). An entry is 0 when the code is longer than <see cref="FastLookupBits"/>. The magnitude size of a symbol is its
    /// value for a DC table, its low nibble for an AC table; a DC symbol above 15 is never complete.
    /// </summary>
    public int[] FastLookup => _fastLookup ??= BuildFastLookup();

    private int[] BuildFastLookup()
    {
        var lookup = new int[1 << FastLookupBits];
        var code = 0;
        var index = 0;
        for (var length = 1; length <= FastLookupBits; length++)
        {
            int count = _counts[length - 1];
            for (var i = 0; i < count; i++)
            {
                int symbol = _values[index];
                var size = _isAc ? symbol & 0x0F : symbol;
                var shift = FastLookupBits - length;
                var first = code << shift;
                var partial = length | (symbol << FastLookupSymbolShift);
                for (var j = 0; j < (1 << shift); j++)
                {
                    if (size == 0)
                    {
                        lookup[first + j] = partial | FastLookupComplete;
                    }
                    else if (size <= shift)
                    {
                        // The magnitude bits follow the code: EXTEND gives the coefficient
                        var magnitude = (j >> (shift - size)) & ((1 << size) - 1);
                        var coefficient = magnitude < (1 << (size - 1)) ? magnitude - (1 << size) + 1 : magnitude;
                        lookup[first + j] = (length + size) | FastLookupComplete | (symbol << FastLookupSymbolShift) | (coefficient << FastLookupValueShift);
                    }
                    else
                    {
                        lookup[first + j] = partial;
                    }
                }

                code++;
                index++;
            }

            code <<= 1;
        }

        return lookup;
    }

    /// <summary>Gets the lookup entry of the next <see cref="LookupBits"/> bits: <c>(length &lt;&lt; 8) | symbol</c>, or 0 for a longer code.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Lookup(int bits) => _lookup[bits];

    /// <summary>
    /// Resolves a code longer than <see cref="FastLookupBits"/> from the next 16 bits, inline: the loop of
    /// <see cref="DecodeLong"/> without a call, for the decoders that keep their bit state in registers.
    /// </summary>
    /// <param name="bits16">The next 16 bits, most significant first.</param>
    /// <returns><c>(length &lt;&lt; 8) | symbol</c>, or -1 if no code matches.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeLongInline(int bits16)
    {
        var maxCode = _maxCode;
        for (var length = FastLookupBits + 1; length < maxCode.Length; length++)
        {
            var code = bits16 >> (16 - length);
            if (code <= maxCode[length])
                return (length << 8) | _values[_valueOffset[length] + code];
        }

        return -1;
    }

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
