namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the bits of a packet header (T.800, B.10.1), the reverse of <see cref="Jpeg2000PacketBitReader"/>: from the most
/// significant bit of each byte, with a bit stuffed after an FF byte.
/// </summary>
internal struct Jpeg2000PacketBitWriter
{
    private readonly PooledPayloadWriter _output;
    private int _current;
    private int _free;
    private bool _afterMarkerPrefix;

    public Jpeg2000PacketBitWriter(PooledPayloadWriter output)
    {
        _output = output;
        _current = 0;
        _free = 8;
        _afterMarkerPrefix = false;
    }

    public void WriteBit(int bit)
    {
        _free--;
        _current |= bit << _free;
        if (_free == 0)
        {
            Emit();
        }
    }

    /// <summary>Writes the <paramref name="count"/> low bits of <paramref name="value"/>, most significant bit first.</summary>
    public void WriteBits(int value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            WriteBit((value >> i) & 1);
        }
    }

    /// <summary>Ends the packet header: the last byte is padded with 0 bits, and an FF byte is followed by a stuffed byte.</summary>
    public void Flush()
    {
        if (_free != (_afterMarkerPrefix ? 7 : 8))
        {
            Emit();
        }

        if (_afterMarkerPrefix)
        {
            _output.Write(0);
            _afterMarkerPrefix = false;
            _free = 8;
        }
    }

    private void Emit()
    {
        _output.Write((byte)_current);

        // The byte that follows an FF byte holds seven bits: its most significant bit is 0
        _afterMarkerPrefix = _current == 0xFF;
        _free = _afterMarkerPrefix ? 7 : 8;
        _current = 0;
    }
}
