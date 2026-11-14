namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>Reads the bits of codewords, most significant bit first: the counterpart of <see cref="BitBuffer"/>.</summary>
internal sealed class BitReader
{
    private readonly byte[] _data;
    private readonly int _bitCount;

    public BitReader(byte[] data, int bitCount)
    {
        _data = data;
        _bitCount = bitCount;
    }

    public int Position { get; private set; }

    public int Remaining => _bitCount - Position;

    /// <summary>Reads <paramref name="count"/> bits (at most 31), or returns -1 when fewer bits remain.</summary>
    public int Read(int count)
    {
        if (count > Remaining)
            return -1;

        var value = Peek(count);
        Position += count;
        return value;
    }

    /// <summary>Reads up to <paramref name="count"/> bits without moving, padded with zeros past the end.</summary>
    public int Peek(int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            var position = Position + i;
            var bit = position < _bitCount ? (_data[position >> 3] >> (7 - (position & 7))) & 1 : 0;
            value = (value << 1) | bit;
        }

        return value;
    }
}
