using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the least-significant-bit-first bit stream of the WebP lossless format (WebP Lossless Bitstream specification,
/// section 3: "ReadBits(n)") from a fully buffered payload. Reading past the end of the payload is
/// <see cref="InvalidImageContentException"/> (truncated data is never decoded as zero bits); peeking past the end returns
/// zero bits, so that a prefix-code lookup near the end only fails when the code actually needs the missing bits.
/// </summary>
internal sealed class Vp8LBitReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _position;
    private ulong _value;
    private int _bits;

    /// <summary>Creates a reader over <paramref name="data"/>[<paramref name="offset"/>..<paramref name="offset"/> + <paramref name="length"/>].</summary>
    public Vp8LBitReader(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length - length);
        _data = data;
        _position = offset;
        _end = offset + length;
    }

    /// <summary>Reads <paramref name="count"/> bits (0 to 32), least significant bit first.</summary>
    /// <exception cref="InvalidImageContentException">The payload ends before the bits.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int count)
    {
        if (_bits < count)
        {
            Fill();
            if (_bits < count)
                throw Truncated();
        }

        var result = (uint)(_value & ((1UL << count) - 1));
        _value >>= count;
        _bits -= count;
        return result;
    }

    /// <summary>Returns the next <paramref name="count"/> bits (at most 32) without consuming them; bits past the end of the payload are zeros.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint PeekBits(int count)
    {
        if (_bits < count)
        {
            Fill();
        }

        return (uint)(_value & ((1UL << count) - 1));
    }

    /// <summary>Consumes bits that were peeked.</summary>
    /// <exception cref="InvalidImageContentException">The bits are past the end of the payload.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count)
    {
        if (_bits < count)
            throw Truncated();

        _value >>= count;
        _bits -= count;
    }

    internal static InvalidImageContentException Truncated() => new("The WebP lossless bitstream is truncated (more bits are needed than the chunk contains).", ImageFormat.WebP);

    private void Fill()
    {
        // Top up to at least 32 buffered bits with one 4-byte read when possible
        if (_bits <= 32 && _end - _position >= 4)
        {
            _value |= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position, 4)) << _bits;
            _position += 4;
            _bits += 32;
            return;
        }

        while (_bits <= 56 && _position < _end)
        {
            _value |= (ulong)_data[_position++] << _bits;
            _bits += 8;
        }
    }
}
