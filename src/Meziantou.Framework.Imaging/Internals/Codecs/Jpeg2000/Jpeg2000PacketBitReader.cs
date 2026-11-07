namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the bits of a packet header (T.800, B.10.1): from the most significant bit of each byte, with a bit stuffed after
/// an FF byte so that no marker above FF8F appears in the header.
/// </summary>
internal ref struct Jpeg2000PacketBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private int _bits;
    private int _current;

    public Jpeg2000PacketBitReader(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
        _bits = 0;
        _current = 0;
    }

    /// <summary>Gets the offset of the byte that follows the bytes read so far.</summary>
    public readonly int Position => _position;

    /// <summary>Reads one bit.</summary>
    /// <exception cref="InvalidImageContentException">The data ends inside the packet header.</exception>
    public int ReadBit()
    {
        if (_bits == 0)
        {
            if (_position >= _data.Length)
                throw Jpeg2000Format.Invalid("the data of a tile ends inside a packet header.");

            // The byte that follows an FF byte holds seven bits
            _bits = _current == 0xFF ? 7 : 8;
            _current = _data[_position++];
        }

        _bits--;
        return (_current >> _bits) & 1;
    }

    /// <summary>Reads an unsigned integer of <paramref name="count"/> bits, most significant bit first.</summary>
    public int ReadBits(int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | ReadBit();
        }

        return value;
    }

    /// <summary>Ends the packet header: the bits left in the last byte are padding, and an FF byte is followed by a stuffed byte.</summary>
    /// <returns>The offset of the byte that follows the packet header.</returns>
    public int Align()
    {
        _bits = 0;
        if (_current == 0xFF)
        {
            if (_position >= _data.Length)
                throw Jpeg2000Format.Invalid("the data of a tile ends inside a packet header.");

            _position++;
        }

        _current = 0;
        return _position;
    }
}
