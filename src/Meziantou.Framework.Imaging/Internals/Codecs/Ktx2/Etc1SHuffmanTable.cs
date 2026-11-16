namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A canonical Huffman code of BasisLZ (Appendix D.1): the code lengths of its symbols, as Deflate codes them.</summary>
internal sealed class Etc1SHuffmanTable
{
    private const int MaxCodeLength = 16;

    // Section D.1: the order of the code lengths of the code length code
    private static ReadOnlySpan<byte> CodeLengthOrder => [17, 18, 19, 20, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15, 16];

    private readonly int[] _counts = new int[MaxCodeLength + 1];
    private readonly ushort[] _symbols;

    private Etc1SHuffmanTable(ReadOnlySpan<byte> lengths, int maxLength)
    {
        SymbolCount = lengths.Length;
        var used = 0;
        foreach (var length in lengths)
        {
            if (length > maxLength)
                throw Ktx2Format.Invalid("a Huffman code length of the BasisLZ data is too long.");

            _counts[length]++;
            if (length != 0)
            {
                used++;
            }
        }

        // The lengths must not oversubscribe the code space (RFC 1951, section 3.2.2)
        _counts[0] = 0;
        var left = 1;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            left = (left << 1) - _counts[length];
            if (left < 0)
                throw Ktx2Format.Invalid("the Huffman code lengths of the BasisLZ data oversubscribe the code space.");
        }

        Span<int> offsets = stackalloc int[MaxCodeLength + 2];
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            offsets[length + 1] = offsets[length] + _counts[length];
        }

        _symbols = new ushort[used];
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            if (lengths[symbol] != 0)
            {
                _symbols[offsets[lengths[symbol]]++] = (ushort)symbol;
            }
        }
    }

    /// <summary>Gets the number of symbols of the table (used or not).</summary>
    public int SymbolCount { get; }

    /// <summary>read_huffman_table (Appendix D.1).</summary>
    public static Etc1SHuffmanTable Read(ref Etc1SBitReader reader)
    {
        var symbolCount = (int)reader.ReadBits(14);
        if (symbolCount == 0)
            throw Ktx2Format.Invalid("a Huffman table of the BasisLZ data has no symbol.");

        var codeLengthCodes = (int)reader.ReadBits(5);
        if (codeLengthCodes is 0 or > 21)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"a Huffman table of the BasisLZ data has {codeLengthCodes} code length codes; 1 to 21 are expected."));

        Span<byte> codeLengthLengths = stackalloc byte[21];
        codeLengthLengths.Clear();
        for (var i = 0; i < codeLengthCodes; i++)
        {
            codeLengthLengths[CodeLengthOrder[i]] = (byte)reader.ReadBits(3);
        }

        var codeLengthTable = new Etc1SHuffmanTable(codeLengthLengths, maxLength: 7);
        var lengths = new byte[symbolCount];
        var count = 0;
        while (count < symbolCount)
        {
            var symbol = codeLengthTable.Decode(ref reader);
            int repeat;
            byte value;
            switch (symbol)
            {
                case <= 16:
                    lengths[count++] = (byte)symbol;
                    continue;
                case 17:
                    repeat = 3 + (int)reader.ReadBits(3);
                    value = 0;
                    break;
                case 18:
                    repeat = 11 + (int)reader.ReadBits(7);
                    value = 0;
                    break;
                default:
                    // 19 and 20 repeat the previous length, which must exist and not be 0
                    if (count == 0 || lengths[count - 1] == 0)
                        throw Ktx2Format.Invalid("a Huffman table of the BasisLZ data repeats a code length that does not exist or is 0.");

                    repeat = symbol == 19 ? 3 + (int)reader.ReadBits(2) : 7 + (int)reader.ReadBits(7);
                    value = lengths[count - 1];
                    break;
            }

            if (repeat > symbolCount - count)
                throw Ktx2Format.Invalid("a Huffman table of the BasisLZ data has more code lengths than symbols.");

            lengths.AsSpan(count, repeat).Fill(value);
            count += repeat;
        }

        return new Etc1SHuffmanTable(lengths, MaxCodeLength);
    }

    /// <summary>decode_huffman: reads one code, the first bit of the code first.</summary>
    public int Decode(ref Etc1SBitReader reader)
    {
        var code = 0;
        var first = 0;
        var index = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            code |= (int)reader.ReadBit();
            var count = _counts[length];
            if (code - first < count)
                return _symbols[index + code - first];

            index += count;
            first += count;
            first <<= 1;
            code <<= 1;
        }

        throw Ktx2Format.Invalid("the BasisLZ data have a Huffman code that the table does not define.");
    }
}
