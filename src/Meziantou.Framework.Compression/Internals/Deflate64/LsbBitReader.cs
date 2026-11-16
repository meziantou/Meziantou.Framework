using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Deflate64;

/// <summary>
/// Reads the bits of a stream, least significant bit of each byte first, as Deflate and the legacy zip methods store them.
/// The bits after the end of the stream read as zeros, so that a code can be looked up, but consuming them throws an
/// <see cref="InvalidDataException"/>.
/// </summary>
internal sealed class LsbBitReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _position;
    private int _length;
    private bool _ended;

    // The next bits are the lowest _count bits; the bits above them are zeros
    private ulong _bits;
    private int _count;

    public LsbBitReader(Stream stream, int bufferSize = 16 * 1024)
    {
        _stream = stream;
        _buffer = new byte[bufferSize];
    }

    /// <summary>Gets the number of bits available before the next read from the stream.</summary>
    public int BitCount => _count;

    /// <summary>Gets a value indicating whether every bit of the stream is consumed.</summary>
    public bool IsAtEnd
    {
        get
        {
            if (_count > 0)
                return false;

            Refill();
            return _count is 0;
        }
    }

    /// <summary>Returns the next <paramref name="count"/> bits (at most 32) without consuming them; the bits after the end of the stream are zeros.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Peek(int count)
    {
        if (_count < count)
        {
            Refill();
        }

        return (uint)(_bits & ((1UL << count) - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Consume(int count)
    {
        if (count > _count)
            throw new InvalidDataException("The compressed data are truncated.");

        _bits >>= count;
        _count -= count;
    }

    /// <summary>Reads <paramref name="count"/> bits (at most 32).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Read(int count)
    {
        var value = Peek(count);
        Consume(count);
        return value;
    }

    /// <summary>Reads <paramref name="count"/> bits (at most 32), or returns <see langword="false"/> when the stream ends first.</summary>
    public bool TryRead(int count, out uint value)
    {
        value = Peek(count);
        if (count > _count)
            return false;

        Consume(count);
        return true;
    }

    /// <summary>Discards the bits up to the next byte boundary.</summary>
    public void AlignToByte() => Consume(_count & 7);

    /// <summary>Reads whole bytes, after <see cref="AlignToByte"/>.</summary>
    /// <returns>The number of bytes read: fewer than requested at the end of the stream only.</returns>
    public int ReadBytes(Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length && _count >= 8)
        {
            destination[read++] = (byte)_bits;
            _bits >>= 8;
            _count -= 8;
        }

        if (read < destination.Length && _position < _length)
        {
            var count = Math.Min(destination.Length - read, _length - _position);
            _buffer.AsSpan(_position, count).CopyTo(destination[read..]);
            _position += count;
            read += count;
        }

        while (read < destination.Length && !_ended)
        {
            var count = _stream.Read(destination[read..]);
            if (count is 0)
            {
                _ended = true;
                break;
            }

            read += count;
        }

        return read;
    }

    /// <summary>Returns the bytes read from the stream and not consumed yet, from the next byte boundary.</summary>
    public byte[] GetUnconsumedBytes()
    {
        var bitBytes = _count >> 3;
        var result = new byte[bitBytes + (_length - _position)];
        var bits = _bits >> (_count & 7);
        for (var i = 0; i < bitBytes; i++)
        {
            result[i] = (byte)bits;
            bits >>= 8;
        }

        _buffer.AsSpan(_position, _length - _position).CopyTo(result.AsSpan(bitBytes));
        return result;
    }

    private void Refill()
    {
        if (_position + 8 <= _length)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.AsSpan(_position));
            var bytes = (63 - _count) >> 3;
            _bits |= value << _count;
            _position += bytes;
            _count += bytes * 8;
            _bits &= ulong.MaxValue >> (64 - _count);
            return;
        }

        while (_count <= 56)
        {
            if (_position == _length)
            {
                if (_ended)
                    return;

                _position = 0;
                _length = _stream.Read(_buffer);
                if (_length is 0)
                {
                    _ended = true;
                    return;
                }
            }

            _bits |= (ulong)_buffer[_position++] << _count;
            _count += 8;
        }
    }
}
