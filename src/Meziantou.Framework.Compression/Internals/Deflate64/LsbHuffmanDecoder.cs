using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Deflate64;

/// <summary>
/// A canonical Huffman code (the codes of each length follow those of the shorter lengths, in the order of the symbols)
/// whose codes are read from their most significant bit, from a stream read least significant bit first, as in Deflate.
/// The codes of at most <see cref="LookupBits"/> bits are decoded with a table, the longer ones bit by bit. The codes of
/// the implode method of zip are the complements of these codes.
/// </summary>
internal sealed class LsbHuffmanDecoder
{
    private const int LookupBits = 10;
    private const int MaxLength = 16;

    // (symbol << 4) | length, or 0 for a longer code or a prefix that is not a code
    private readonly ushort[] _lookup = new ushort[1 << LookupBits];
    private readonly short[] _counts = new short[MaxLength + 1];
    private readonly short[] _symbols;
    private int _maxLength;
    private bool _inverted;

    public LsbHuffmanDecoder(int maxSymbols)
    {
        _symbols = new short[maxSymbols];
    }

    /// <summary>Builds the code from the lengths of the symbols (0 for an unused symbol, at most 16).</summary>
    /// <param name="lengths">The lengths of the codes of the symbols.</param>
    /// <param name="inverted">Whether the bits of the codes are complemented, as in the implode method.</param>
    /// <returns><see langword="false"/> when no symbol has a code.</returns>
    /// <exception cref="InvalidDataException">The code is over-subscribed.</exception>
    public bool Build(ReadOnlySpan<byte> lengths, bool inverted = false)
    {
        _inverted = inverted;
        Array.Clear(_counts);
        foreach (var length in lengths)
        {
            _counts[length]++;
        }

        _counts[0] = 0;
        _maxLength = 0;
        var left = 1;
        Span<int> offsets = stackalloc int[MaxLength + 2];
        offsets[1] = 0;
        for (var length = 1; length <= MaxLength; length++)
        {
            left = (left << 1) - _counts[length];
            if (left < 0)
                throw new InvalidDataException("The compressed data have an over-subscribed Huffman code.");

            if (_counts[length] is not 0)
            {
                _maxLength = length;
            }

            offsets[length + 1] = offsets[length] + _counts[length];
        }

        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            var length = lengths[symbol];
            if (length is not 0)
            {
                _symbols[offsets[length]++] = (short)symbol;
            }
        }

        // The lookup table: the index is the next bits of the input, the first bit of the code in the lowest bit
        Array.Clear(_lookup);
        var code = 0;
        var index = 0;
        for (var length = 1; length <= Math.Min(_maxLength, LookupBits); length++)
        {
            for (var i = 0; i < _counts[length]; i++)
            {
                var transmitted = inverted ? ~code & ((1 << length) - 1) : code;
                var reversed = Reverse(transmitted, length);
                var entry = (ushort)((_symbols[index + i] << 4) | length);
                for (var j = reversed; j < 1 << LookupBits; j += 1 << length)
                {
                    _lookup[j] = entry;
                }

                code++;
            }

            index += _counts[length];
            code <<= 1;
        }

        return _maxLength > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(LsbBitReader reader)
    {
        var bits = reader.Peek(MaxLength);
        int entry = _lookup[bits & ((1 << LookupBits) - 1)];
        if (entry is not 0)
        {
            reader.Consume(entry & 15);
            return entry >> 4;
        }

        return DecodeSlow(reader, bits);
    }

    private int DecodeSlow(LsbBitReader reader, uint bits)
    {
        var code = 0;
        var first = 0;
        var index = 0;
        var invert = _inverted ? 1 : 0;
        for (var length = 1; length <= _maxLength; length++)
        {
            code |= (int)((bits >> (length - 1)) & 1) ^ invert;
            int count = _counts[length];
            if (code - first < count)
            {
                reader.Consume(length);
                return _symbols[index + code - first];
            }

            index += count;
            first = (first + count) << 1;
            code <<= 1;
        }

        throw new InvalidDataException("The compressed data contain an invalid Huffman code.");
    }

    private static int Reverse(int value, int length)
    {
        var result = 0;
        for (var i = 0; i < length; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }

        return result;
    }
}
