namespace Meziantou.Framework.Compression.Internals.BZip2;

/// <summary>Writes bits, most significant bit first, to a buffer that grows as needed.</summary>
internal sealed class BZip2BitWriter
{
    private byte[] _buffer = new byte[64 * 1024];
    private int _length;
    private ulong _bits;
    private int _bitCount;

    public void Write(int count, uint value)
    {
        _bits = (_bits << count) | (value & ((1UL << count) - 1));
        _bitCount += count;
        if (_bitCount >= 32)
        {
            FlushBits();
        }
    }

    private void FlushBits()
    {
        if (_length + 8 > _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        while (_bitCount >= 8)
        {
            _bitCount -= 8;
            _buffer[_length++] = (byte)(_bits >> _bitCount);
        }
    }

    /// <summary>Writes the whole bytes to <paramref name="output"/>; the bits of an incomplete byte stay in the writer.</summary>
    public void WriteTo(Stream output)
    {
        FlushBits();
        output.Write(_buffer, 0, _length);
        _length = 0;
    }

    /// <summary>Pads the last byte with zeros and writes everything to <paramref name="output"/>.</summary>
    public void WriteFinalTo(Stream output)
    {
        if ((_bitCount & 7) is not 0)
        {
            Write(8 - (_bitCount & 7), 0);
        }

        WriteTo(output);
    }
}
