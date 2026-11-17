namespace Meziantou.Framework.Compression.Internals.BZip2;

/// <summary>
/// A canonical Huffman code of a bzip2 block, read most significant bit first: a table of the codes of at most
/// <see cref="LookupBits"/> bits, and the first code of each length for the longer codes.
/// </summary>
internal sealed class BZip2HuffmanDecoder
{
    public const int LookupBits = 10;

    // (symbol << 5) | length, or 0 for a code longer than LookupBits bits (or a prefix that is not a code)
    private readonly ushort[] _lookup = new ushort[1 << LookupBits];
    private readonly int[] _firstCode = new int[BZip2Constants.MaxCodeLength + 2];
    private readonly int[] _count = new int[BZip2Constants.MaxCodeLength + 2];
    private readonly int[] _offset = new int[BZip2Constants.MaxCodeLength + 2];
    private readonly ushort[] _sorted = new ushort[BZip2Constants.MaxAlphabetSize];
    private int _maxLength;

    public ReadOnlySpan<ushort> Lookup => _lookup;

    /// <summary>Builds the code from the lengths (1 to 20) of the symbols; the code may be incomplete, but not over-subscribed.</summary>
    public void Build(ReadOnlySpan<byte> lengths)
    {
        Array.Clear(_count);
        _maxLength = 0;
        foreach (var length in lengths)
        {
            _count[length]++;
            _maxLength = Math.Max(_maxLength, length);
        }

        var code = 0;
        var offset = 0;
        for (var length = 1; length <= BZip2Constants.MaxCodeLength; length++)
        {
            _firstCode[length] = code;
            _offset[length] = offset;
            code += _count[length];
            offset += _count[length];
            if (code > 1 << length)
                throw new InvalidDataException("The bzip2 data has an over-subscribed Huffman code.");

            code <<= 1;
        }

        Span<int> next = stackalloc int[BZip2Constants.MaxCodeLength + 1];
        for (var length = 1; length <= BZip2Constants.MaxCodeLength; length++)
        {
            next[length] = _offset[length];
        }

        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            _sorted[next[lengths[symbol]]++] = (ushort)symbol;
        }

        Array.Clear(_lookup);
        for (var length = 1; length <= Math.Min(LookupBits, _maxLength); length++)
        {
            var first = _firstCode[length];
            var start = _offset[length];
            for (var i = 0; i < _count[length]; i++)
            {
                var entry = (ushort)((_sorted[start + i] << 5) | length);
                var shift = LookupBits - length;
                var index = (first + i) << shift;
                _lookup.AsSpan(index, 1 << shift).Fill(entry);
            }
        }
    }

    /// <summary>Decodes a symbol whose code is longer than <see cref="LookupBits"/> bits from the next 20 bits of the input.</summary>
    /// <returns>The symbol and the length of its code, as <c>(symbol &lt;&lt; 5) | length</c>.</returns>
    public int DecodeLong(uint next20Bits)
    {
        for (var length = LookupBits + 1; length <= _maxLength; length++)
        {
            var value = (int)(next20Bits >> (BZip2Constants.MaxCodeLength - length)) - _firstCode[length];
            if ((uint)value < (uint)_count[length])
                return (_sorted[_offset[length] + value] << 5) | length;
        }

        throw new InvalidDataException("The bzip2 data has an invalid Huffman code.");
    }
}
