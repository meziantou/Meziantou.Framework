using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the syntax elements that appear directly in an AV1 bitstream, most significant bit first (AV1 specification,
/// section 4.10 "Descriptors"): <c>f(n)</c>, <c>su(n)</c>, <c>ns(n)</c>, <c>uvlc()</c>, <c>le(n)</c> and <c>leb128()</c>.
/// The mirror of <see cref="Av1BitReader"/>.
/// </summary>
internal ref struct Av1BitWriter
{
    private readonly PooledPayloadWriter _output;
    private readonly int _start;
    private uint _pending;
    private int _pendingBits;

    /// <summary>Creates a writer that appends to <paramref name="output"/>.</summary>
    public Av1BitWriter(PooledPayloadWriter output)
    {
        _output = output;
        _start = output.Length;
    }

    /// <summary>Gets the number of bits written.</summary>
    public readonly long BitPosition => ((long)(_output.Length - _start) << 3) + _pendingBits;

    /// <summary>Writes an unsigned <paramref name="count"/>-bit number (<c>f(n)</c>), 0 to 32 bits.</summary>
    public void WriteBits(uint value, int count)
    {
        Debug.Assert(count is >= 0 and <= 32);
        Debug.Assert(count is 32 || (value >> count) is 0);
        for (var i = count - 1; i >= 0; i--)
        {
            _pending = (_pending << 1) | ((value >> i) & 1);
            if (++_pendingBits is 8)
            {
                _output.Write((byte)_pending);
                _pending = 0;
                _pendingBits = 0;
            }
        }
    }

    /// <summary>Writes a one-bit flag (<c>f(1)</c>).</summary>
    public void WriteFlag(bool value) => WriteBits(value ? 1u : 0u, 1);

    /// <summary>Writes a signed number as the bottom <paramref name="count"/> bits of its two's complement form (<c>su(n)</c>).</summary>
    public void WriteSigned(int value, int count)
    {
        Debug.Assert(count is >= 1 and <= 32);
        WriteBits(count is 32 ? (uint)value : (uint)value & ((1u << count) - 1), count);
    }

    /// <summary>Writes a number in the range 0 to <paramref name="valueCount"/> - 1 with the non-symmetric encoding (<c>ns(n)</c>).</summary>
    public void WriteNonSymmetric(int value, int valueCount)
    {
        Debug.Assert(valueCount > 0 && (uint)value < (uint)valueCount);
        var w = (31 - int.LeadingZeroCount(valueCount)) + 1;
        var m = (1 << w) - valueCount;
        if (value < m)
        {
            WriteBits((uint)value, w - 1);
            return;
        }

        // The reader computes (v << 1) - m + extra_bit from the w - 1 bits of v and the extra bit
        var coded = value + m;
        WriteBits((uint)(coded >> 1), w - 1);
        WriteBits((uint)(coded & 1), 1);
    }

    /// <summary>Writes a variable-length unsigned number (<c>uvlc()</c>), which is less than 2^32 - 1.</summary>
    public void WriteUvlc(uint value)
    {
        Debug.Assert(value < uint.MaxValue);
        var coded = (ulong)value + 1;
        var leadingZeros = 63 - (int)ulong.LeadingZeroCount(coded);
        WriteBits(0, leadingZeros);
        WriteBits(1, 1);
        WriteBits((uint)(coded - (1UL << leadingZeros)), leadingZeros);
    }

    /// <summary>Writes a little-endian number of <paramref name="byteCount"/> bytes (<c>le(n)</c>), 1 to 4 bytes.</summary>
    public void WriteLittleEndian(uint value, int byteCount)
    {
        Debug.Assert(byteCount is >= 1 and <= 4);
        for (var i = 0; i < byteCount; i++)
        {
            WriteBits((value >> (i * 8)) & 0xFF, 8);
        }
    }

    /// <summary>Writes an unsigned number as bytes of 7 bits, least significant group first (<c>leb128()</c>).</summary>
    public void WriteLeb128(uint value)
    {
        do
        {
            var group = value & 0x7F;
            value >>= 7;
            WriteBits(value is not 0 ? group | 0x80 : group, 8);
        }
        while (value is not 0);
    }

    /// <summary>Writes zero bits up to the next byte boundary (<c>byte_alignment()</c>).</summary>
    public void ByteAlign()
    {
        if (_pendingBits is not 0)
            WriteBits(0, 8 - _pendingBits);
    }

    /// <summary>Writes the trailing bits that end an OBU payload (<c>trailing_bits()</c>): a one bit, then zero bits up to the next byte boundary.</summary>
    public void WriteTrailingBits()
    {
        WriteBits(1, 1);
        ByteAlign();
    }

    /// <summary>Returns the number of bytes of the <c>leb128()</c> form of <paramref name="value"/>.</summary>
    public static int GetLeb128Length(uint value)
    {
        var length = 1;
        while ((value >>= 7) is not 0)
        {
            length++;
        }

        return length;
    }
}
