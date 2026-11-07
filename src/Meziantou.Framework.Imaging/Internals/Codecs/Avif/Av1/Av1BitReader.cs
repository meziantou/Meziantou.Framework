using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the syntax elements that appear directly in an AV1 bitstream, most significant bit first (AV1 specification,
/// sections 4.10 "Descriptors" and 8.1 "Parsing process for f(n)"): <c>f(n)</c>, <c>su(n)</c>, <c>ns(n)</c>, <c>uvlc()</c>,
/// <c>le(n)</c> and <c>leb128()</c>. Reading past the end of the data is <see cref="InvalidImageContentException"/>: missing
/// bits are never read as zeros.
/// </summary>
internal ref struct Av1BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _position;

    public Av1BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    /// <summary>Gets the position of the next bit, from the start of the data (<c>get_position()</c>).</summary>
    public readonly long BitPosition => _position;

    /// <summary>Gets the position of the first byte that holds no consumed bit.</summary>
    public readonly int BytePosition => (int)((_position + 7) >> 3);

    /// <summary>Gets the number of bits that are not consumed yet.</summary>
    public readonly long RemainingBits => ((long)_data.Length << 3) - _position;

    /// <summary>Reads an unsigned <paramref name="count"/>-bit number (<c>f(n)</c>), 0 to 32 bits.</summary>
    public uint ReadBits(int count)
    {
        Debug.Assert(count is >= 0 and <= 32);
        if (count is 0)
            return 0;

        if (count > RemainingBits)
            throw Av1Errors.Truncated();

        var index = (int)(_position >> 3);
        var bitOffset = (int)(_position & 7);
        var byteCount = (bitOffset + count + 7) >> 3;
        ulong accumulator = 0;
        for (var i = 0; i < byteCount; i++)
        {
            accumulator = (accumulator << 8) | _data[index + i];
        }

        _position += count;
        return (uint)((accumulator >> ((byteCount << 3) - bitOffset - count)) & ((1UL << count) - 1));
    }

    /// <summary>Reads a one-bit flag (<c>f(1)</c>).</summary>
    public bool ReadFlag() => ReadBits(1) is not 0;

    /// <summary>Reads a signed number stored as the bottom <paramref name="count"/> bits of its two's complement form (<c>su(n)</c>).</summary>
    public int ReadSigned(int count)
    {
        Debug.Assert(count is >= 1 and <= 32);
        var value = (long)ReadBits(count);
        var signMask = 1L << (count - 1);
        if ((value & signMask) is not 0)
            value -= 2 * signMask;

        return (int)value;
    }

    /// <summary>Reads a number in the range 0 to <paramref name="valueCount"/> - 1 with the non-symmetric encoding (<c>ns(n)</c>).</summary>
    public int ReadNonSymmetric(int valueCount)
    {
        Debug.Assert(valueCount > 0);
        var w = (31 - int.LeadingZeroCount(valueCount)) + 1;
        var m = (1 << w) - valueCount;
        var v = (int)ReadBits(w - 1);
        if (v < m)
            return v;

        var extraBit = (int)ReadBits(1);
        return (v << 1) - m + extraBit;
    }

    /// <summary>Reads a variable-length unsigned number (<c>uvlc()</c>).</summary>
    public uint ReadUvlc()
    {
        var leadingZeros = 0;
        while (!ReadFlag())
        {
            leadingZeros++;
        }

        if (leadingZeros >= 32)
            return uint.MaxValue;

        return ReadBits(leadingZeros) + ((1u << leadingZeros) - 1);
    }

    /// <summary>Reads a little-endian number of <paramref name="byteCount"/> bytes (<c>le(n)</c>), 1 to 4 bytes.</summary>
    public uint ReadLittleEndian(int byteCount)
    {
        Debug.Assert(byteCount is >= 1 and <= 4);
        uint value = 0;
        for (var i = 0; i < byteCount; i++)
        {
            value |= ReadBits(8) << (i * 8);
        }

        return value;
    }

    /// <summary>Reads an unsigned number of at most 8 bytes of 7 bits, least significant group first (<c>leb128()</c>).</summary>
    /// <exception cref="InvalidImageContentException">The value does not fit in 32 bits, or the eighth byte announces a ninth one.</exception>
    public uint ReadLeb128()
    {
        ulong value = 0;
        for (var i = 0; i < 8; i++)
        {
            var leb128Byte = ReadBits(8);
            value |= (ulong)(leb128Byte & 0x7F) << (i * 7);
            if ((leb128Byte & 0x80) is 0)
            {
                if (value > uint.MaxValue)
                    throw Av1Errors.Invalid("a leb128 value does not fit in 32 bits.");

                return (uint)value;
            }
        }

        throw Av1Errors.Invalid("a leb128 value uses more than 8 bytes.");
    }

    /// <summary>Skips <paramref name="count"/> bits.</summary>
    public void Skip(long count)
    {
        Debug.Assert(count >= 0);
        if (count > RemainingBits)
            throw Av1Errors.Truncated();

        _position += count;
    }

    /// <summary>Skips the bits up to the next byte boundary (<c>byte_alignment()</c>).</summary>
    public void ByteAlign() => _position = (_position + 7) & ~7L;

    /// <summary>
    /// Reads the trailing bits that end an OBU payload (<c>trailing_bits()</c>): a one bit followed by zero bits up to the end of
    /// the data. The one bit is required; the value of the padding after it is not checked.
    /// </summary>
    public void ReadTrailingBits()
    {
        if (RemainingBits <= 0 || !ReadFlag())
            throw Av1Errors.Invalid("an OBU does not end with its trailing bit.");

        _position = (long)_data.Length << 3;
    }
}
