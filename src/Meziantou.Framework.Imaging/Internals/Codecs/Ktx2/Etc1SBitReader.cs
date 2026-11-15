using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A reader of the bits of BasisLZ data, from the least significant bit of each byte (KTX Appendix D.1).</summary>
[StructLayout(LayoutKind.Auto)]
internal ref struct Etc1SBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _position;

    public Etc1SBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>Reads <paramref name="count"/> bits (up to 32), the first one in the least significant bit of the value.</summary>
    public uint ReadBits(int count)
    {
        uint value = 0;
        for (var i = 0; i < count; i++)
        {
            value |= ReadBit() << i;
        }

        return value;
    }

    public uint ReadBit()
    {
        var index = _position >> 3;
        if (index >= _data.Length)
            throw Ktx2Format.Invalid("the BasisLZ data end inside a bitstream.");

        var bit = (uint)(_data[(int)index] >> (int)(_position & 7)) & 1;
        _position++;
        return bit;
    }

    /// <summary>decode_vlc4 and decode_vlc7 (Appendix D.5.2): chunks of 4 or 7 bits, each followed by a continuation bit.</summary>
    public uint ReadVariableLength(int chunkBits)
    {
        uint value = 0;
        var shift = 0;
        while (true)
        {
            var chunk = ReadBits(chunkBits + 1);
            value |= (chunk & ((1u << chunkBits) - 1)) << shift;
            shift += chunkBits;
            if ((chunk & (1u << chunkBits)) == 0)
                return value;

            if (shift >= 32)
                throw Ktx2Format.Invalid("a variable-length value of the BasisLZ data is longer than 32 bits.");
        }
    }
}
