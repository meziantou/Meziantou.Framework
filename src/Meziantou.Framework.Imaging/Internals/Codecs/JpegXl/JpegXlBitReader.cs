using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the bit stream of a JPEG XL codestream (ISO/IEC 18181-1, section 9: bits are packed from the least significant
/// bit of each byte) and its field encodings: <c>u(n)</c>, <c>U32</c>, <c>U64</c>, <c>F16</c>, <c>Enum</c> and <c>Bool</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reading past the end of the data returns zero bits and is remembered (<see cref="IsOverrun"/>) instead of throwing: a
/// section is checked once it is decoded (<see cref="EnsureNotOverrun"/>), and the headers, whose length is only known
/// once they are read, are parsed speculatively on what was received so far.
/// </para>
/// <para>
/// A value read after an overrun means nothing. Loops whose bounds come from the stream must therefore stay bounded by the
/// length of the data, or check <see cref="IsOverrun"/>.
/// </para>
/// </remarks>
internal sealed class JpegXlBitReader
{
    private byte[] _data = [];
    private int _start;
    private int _end;
    private long _position;
    private ulong _bits;
    private int _bitCount;

    public JpegXlBitReader()
    {
    }

    public JpegXlBitReader(byte[] data, int offset, int length) => Reset(data, offset, length);

    /// <summary>Gets the number of bits consumed since the last <see cref="Reset"/>.</summary>
    public long BitsConsumed => ((_position - _start) * 8) - _bitCount;

    /// <summary>Gets the number of bits of the data.</summary>
    public long TotalBits => (long)(_end - _start) * 8;

    /// <summary>Gets a value indicating whether more bits were consumed than the data holds.</summary>
    public bool IsOverrun => BitsConsumed > TotalBits;

    /// <summary>Gets the number of whole bytes consumed: the offset, from the start of the data, of the byte that holds the next bit.</summary>
    public long BytesConsumed => (BitsConsumed + 7) >> 3;

    /// <summary>Starts reading <paramref name="length"/> bytes of <paramref name="data"/> from <paramref name="offset"/>.</summary>
    public void Reset(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, data.Length - offset);
        _data = data;
        _start = offset;
        _end = offset + length;
        _position = offset;
        _bits = 0;
        _bitCount = 0;
    }

    /// <summary>Throws when a read went past the end of the data: the section is truncated or its content is not what its length announced.</summary>
    /// <exception cref="InvalidImageContentException">More bits were consumed than the data holds.</exception>
    public void EnsureNotOverrun()
    {
        if (IsOverrun)
            throw JpegXlFormat.Invalid("a section of the codestream ends before its content.");
    }

    /// <summary>Reads <c>u(n)</c>: an unsigned integer of <paramref name="count"/> bits (0 to 32).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int count)
    {
        Debug.Assert(count is >= 0 and <= 32);
        if (_bitCount < count)
        {
            Refill();
        }

        var value = (uint)(_bits & ((1UL << count) - 1));
        _bits >>= count;
        _bitCount -= count;
        return value;
    }

    /// <summary>Returns the next <paramref name="count"/> bits (0 to 32) without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint PeekBits(int count)
    {
        Debug.Assert(count is >= 0 and <= 32);
        if (_bitCount < count)
        {
            Refill();
        }

        return (uint)(_bits & ((1UL << count) - 1));
    }

    /// <summary>Consumes bits returned by <see cref="PeekBits"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Consume(int count)
    {
        Debug.Assert(count >= 0 && count <= _bitCount);
        _bits >>= count;
        _bitCount -= count;
    }

    /// <summary>Reads <c>Bool()</c>: one bit.</summary>
    public bool ReadBool() => ReadBits(1) != 0;

    /// <summary>
    /// Reads <c>U32(d0, d1, d2, d3)</c>: two bits select one of four distributions, each of which is an offset added to an
    /// unsigned integer of a fixed number of bits (a constant has no bit).
    /// </summary>
    public uint ReadU32(uint offset0, int bits0, uint offset1, int bits1, uint offset2, int bits2, uint offset3, int bits3)
    {
        return ReadBits(2) switch
        {
            0 => offset0 + ReadBits(bits0),
            1 => offset1 + ReadBits(bits1),
            2 => offset2 + ReadBits(bits2),
            _ => offset3 + ReadBits(bits3),
        };
    }

    /// <summary>Reads <c>U64()</c>: a variable-length unsigned integer of up to 64 bits.</summary>
    public ulong ReadU64()
    {
        switch (ReadBits(2))
        {
            case 0:
                return 0;
            case 1:
                return 1 + ReadBits(4);
            case 2:
                return 17 + ReadBits(8);
        }

        ulong value = ReadBits(12);
        var shift = 12;
        while (ReadBits(1) != 0)
        {
            if (shift == 60)
            {
                value |= (ulong)ReadBits(4) << shift;
                break;
            }

            value |= (ulong)ReadBits(8) << shift;
            shift += 8;
        }

        return value;
    }

    /// <summary>Reads <c>F16()</c>: a binary16 floating-point number, which must be finite.</summary>
    /// <exception cref="InvalidImageContentException">The value is an infinity or a NaN.</exception>
    public float ReadF16()
    {
        var bits = (ushort)ReadBits(16);
        if ((bits & 0x7C00) == 0x7C00)
            throw JpegXlFormat.Invalid("a floating-point field is not finite.");

        return (float)BitConverter.UInt16BitsToHalf(bits);
    }

    /// <summary>Reads <c>Enum()</c>: <c>U32(0, 1, 2 + u(4), 18 + u(6))</c>, at most 63.</summary>
    /// <exception cref="InvalidImageContentException">The value is larger than 63.</exception>
    public int ReadEnum()
    {
        var value = ReadU32(0, 0, 1, 0, 2, 4, 18, 6);
        if (value > 63)
            throw JpegXlFormat.Invalid("an enumerated field is out of range.");

        return (int)value;
    }

    /// <summary>Converts a packed signed integer (<c>UnpackSigned</c>): even values are positive, odd values negative.</summary>
    public static int UnpackSigned(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

    /// <summary>Skips bits.</summary>
    public void SkipBits(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count <= _bitCount)
        {
            Consume((int)count);
            return;
        }

        count -= _bitCount;
        _bits = 0;
        _bitCount = 0;
        _position += count >> 3;
        var rest = (int)(count & 7);
        if (rest != 0)
        {
            _ = ReadBits(rest);
        }
    }

    /// <summary>Skips to the next byte boundary (<c>ZeroPadToByte</c>): the skipped bits must be zero.</summary>
    /// <exception cref="InvalidImageContentException">A padding bit is set.</exception>
    public void ZeroPadToByte()
    {
        var count = (int)(-BitsConsumed & 7);
        if (ReadBits(count) != 0 && !IsOverrun)
            throw JpegXlFormat.Invalid("the padding bits of the codestream are not zero.");
    }

    private void Refill()
    {
        // Bytes past the end are zeros: BitsConsumed tells whether they were used
        while (_bitCount <= 56)
        {
            var value = _position < _end ? _data[_position] : (byte)0;
            _bits |= (ulong)value << _bitCount;
            _bitCount += 8;
            _position++;
        }
    }
}
