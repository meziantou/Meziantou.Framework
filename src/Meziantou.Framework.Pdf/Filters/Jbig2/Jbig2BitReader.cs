namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>Reads the bits of the Huffman-coded data of JBIG2, most significant bit first; the bits past the end are 0.</summary>
internal sealed class Jbig2BitReader(byte[] data, int offset, int length)
{
    private readonly int _end = offset + length;
    private long _position = (long)offset * 8;

    /// <summary>Gets the position of the next whole byte.</summary>
    public int BytePosition => (int)((_position + 7) / 8);

    public bool IsPastEnd => _position > (long)_end * 8;

    public int ReadBit()
    {
        var index = (int)(_position >> 3);
        var bit = index < _end ? (data[index] >> (7 - (int)(_position & 7))) & 1 : 0;
        _position++;
        return bit;
    }

    public uint ReadBits(int count)
    {
        uint value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | (uint)ReadBit();
        }

        return value;
    }

    public void AlignToByte() => _position = (_position + 7) & ~7L;

    public void SkipBytes(long count) => _position = Math.Min((long)_end * 8 + 8, ((_position + 7) & ~7L) + (count * 8));
}
