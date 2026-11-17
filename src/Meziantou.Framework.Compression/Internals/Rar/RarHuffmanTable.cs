namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// A canonical Huffman code of RAR (codes of 1 to 15 bits, assigned by length then by symbol), decoded with a lookup
/// table for the codes of at most <c>quickBits</c> bits, and a search of the limits of each length for the longer ones.
/// </summary>
internal sealed class RarHuffmanTable
{
    private readonly int _quickBits;
    private readonly ushort[] _quick;
    private readonly uint[] _limits = new uint[17];
    private readonly int[] _positions = new int[17];
    private readonly ushort[] _symbols;
    private int _symbolCount;
    private uint _quickLimit;

    public RarHuffmanTable(int maxSymbols, int quickBits)
    {
        _quickBits = quickBits;
        _quick = new ushort[1 << quickBits];
        _symbols = new ushort[maxSymbols];
    }

    /// <summary>Builds the code from the bit lengths of the symbols (0 for an unused symbol).</summary>
    /// <exception cref="InvalidDataException">The lengths do not define a prefix code.</exception>
    public void Build(ReadOnlySpan<byte> lengths)
    {
        Span<int> counts = stackalloc int[16];
        counts.Clear();
        foreach (var length in lengths)
        {
            counts[length & 15]++;
        }

        counts[0] = 0;
        uint upper = 0;
        _limits[0] = 0;
        _positions[0] = 0;
        for (var length = 1; length < 16; length++)
        {
            upper += (uint)counts[length];
            var limit = upper << (16 - length);
            if (limit > 0x10000)
                throw new InvalidDataException("The Huffman code lengths of the compressed data are invalid.");

            _limits[length] = limit;
            _positions[length] = _positions[length - 1] + counts[length - 1];
            upper <<= 1;
        }

        _limits[16] = 0x10000;
        Span<int> next = stackalloc int[16];
        for (var length = 1; length < 16; length++)
        {
            next[length] = _positions[length];
        }

        _symbolCount = 0;
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            var length = lengths[symbol] & 15;
            if (length is not 0)
            {
                _symbols[next[length]++] = (ushort)symbol;
                _symbolCount++;
            }
        }

        _quickLimit = _limits[_quickBits];
        var shift = 16 - _quickBits;
        var index = 0;
        for (var length = 1; length <= _quickBits; length++)
        {
            var end = (int)(_limits[length] >> shift);
            for (; index < end; index++)
            {
                var bits = (uint)index << shift;
                var position = _positions[length] + (int)((bits - _limits[length - 1]) >> (16 - length));
                _quick[index] = (ushort)((_symbols[position] << 4) | length);
            }
        }

        _quick.AsSpan(index).Clear();
    }

    /// <summary>Decodes a symbol.</summary>
    /// <exception cref="InvalidDataException">The bits are not a code of the table.</exception>
    public int Decode(RarBitReader input)
    {
        var bits = input.Peek16();
        if (bits < _quickLimit)
        {
            int entry = _quick[bits >> (16 - _quickBits)];
            input.Skip(entry & 15);
            return entry >> 4;
        }

        return DecodeSlow(input, bits);
    }

    private int DecodeSlow(RarBitReader input, uint bits)
    {
        var length = _quickBits + 1;
        while (length < 15 && bits >= _limits[length])
        {
            length++;
        }

        if (bits >= _limits[length])
            throw new InvalidDataException("The compressed data contain an invalid Huffman code.");

        input.Skip(length);
        var position = _positions[length] + (int)((bits - _limits[length - 1]) >> (16 - length));
        if ((uint)position >= (uint)_symbolCount)
            throw new InvalidDataException("The compressed data contain an invalid Huffman code.");

        return _symbols[position];
    }
}
