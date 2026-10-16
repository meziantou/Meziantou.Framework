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

    /// <summary>Gets the buffer, for hot loops that hold the reader state in locals (see <see cref="Load"/>).</summary>
    public byte[] Data => _data;

    /// <summary>Gets the end of the payload in <see cref="Data"/>.</summary>
    public int End => _end;

    /// <summary>Copies the reader state into locals, for a hot loop that calls the static members.</summary>
    public void Load(out ulong value, out int bits, out int position)
    {
        value = _value;
        bits = _bits;
        position = _position;
    }

    /// <summary>Stores the reader state copied by <see cref="Load"/> back.</summary>
    public void Store(ulong value, int bits, int position)
    {
        _value = value;
        _bits = bits;
        _position = position;
    }

    /// <summary>
    /// Tops the state of <see cref="Load"/> up to at least 32 buffered bits, or every remaining bit near the end of the payload.
    /// </summary>
    /// <param name="data">The first byte of <see cref="Data"/>.</param>
    /// <param name="end">The end of the payload (<see cref="End"/>), at most the length of <see cref="Data"/>.</param>
    /// <param name="value">The buffered bits.</param>
    /// <param name="bits">The number of buffered bits.</param>
    /// <param name="position">The next byte to buffer.</param>
    /// <remarks>
    /// Bits above the buffered ones are either zeros past the end of the payload or the next bits of the payload (a refill
    /// reads 8 bytes and counts only the whole bytes that fit), so peeking always sees the payload followed by zeros, and the
    /// other members, which OR whole bytes at the same positions, keep working on the same state.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Refill(ref byte data, int end, ref ulong value, ref int bits, ref int position)
    {
        if (bits >= 32)
            return;

        if (position <= end - 8)
        {
            // In bounds: position + 8 <= end <= data length
            var next = unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref data, position)));
            if (!BitConverter.IsLittleEndian)
            {
                next = BinaryPrimitives.ReverseEndianness(next);
            }

            value |= next << bits;
            var bytes = (63 - bits) >> 3;
            position += bytes;
            bits += bytes * 8;
            return;
        }

        while (bits <= 56 && position < end)
        {
            value |= (ulong)unsafe(Unsafe.Add(ref data, position++)) << bits;
            bits += 8;
        }
    }

    /// <summary>Reads <paramref name="count"/> bits (0 to 32) with the state of <see cref="Load"/>.</summary>
    /// <exception cref="InvalidImageContentException">The payload ends before the bits.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadBits(ref byte data, int end, ref ulong value, ref int bits, ref int position, int count)
    {
        Refill(ref data, end, ref value, ref bits, ref position);
        if (bits < count)
            throw Truncated();

        var result = (uint)(value & ((1UL << count) - 1));
        value >>= count;
        bits -= count;
        return result;
    }

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
