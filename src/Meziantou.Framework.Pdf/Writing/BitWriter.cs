namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Writes values of 1 to 32 bits, the most significant bit first, as the data of meshes and sampled functions are packed.</summary>
internal sealed class BitWriter
{
    private readonly List<byte> _bytes = [];
    private ulong _buffer;
    private int _count;

    public void Write(uint value, int bits)
    {
        _buffer = (_buffer << bits) | (value & (bits == 32 ? uint.MaxValue : (1u << bits) - 1));
        _count += bits;
        while (_count >= 8)
        {
            _count -= 8;
            _bytes.Add((byte)(_buffer >> _count));
        }

        _buffer &= (1UL << _count) - 1;
    }

    /// <summary>Pads the last byte with zero bits.</summary>
    public void Flush()
    {
        if (_count > 0)
        {
            Write(0, 8 - _count);
        }
    }

    public byte[] ToArray()
    {
        Flush();
        return [.. _bytes];
    }
}
