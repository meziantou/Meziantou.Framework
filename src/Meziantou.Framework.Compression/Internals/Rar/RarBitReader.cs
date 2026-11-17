using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// Reads the bits of the compressed data of a RAR entry, most significant bit first. The bits are kept in a 64-bit
/// container that is refilled 8 bytes at a time. After the end of the stream, the reader returns zeros for at most 8
/// bytes, so the decoders can peek past the end, and then throws an <see cref="InvalidDataException"/>.
/// </summary>
internal sealed class RarBitReader
{
    private const int BufferSize = 64 * 1024;
    private const int Padding = 16;

    private readonly byte[] _buffer = new byte[BufferSize + Padding];
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The reader does not own the stream")]
    private Stream _stream = Stream.Null;
    private int _length;
    private int _position;
    private long _bufferOffset;
    private bool _endOfStream;
    private ulong _bits;
    private int _count;

    public void Reset(Stream stream)
    {
        _stream = stream;
        _length = 0;
        _position = 0;
        _bufferOffset = 0;
        _endOfStream = false;
        _bits = 0;
        _count = 0;
    }

    /// <summary>Gets the number of bits read since <see cref="Reset"/>.</summary>
    public long BitPosition => ((_bufferOffset + _position) << 3) - _count;

    /// <summary>Gets a value indicating whether every bit of the stream is read.</summary>
    public bool IsEndOfStream
    {
        get
        {
            if (!_endOfStream && _position > _length - 8)
            {
                RefillSlow();
            }

            return _endOfStream && BitPosition >= (_bufferOffset + _length) << 3;
        }
    }

    /// <summary>Gets a value indicating whether at least <paramref name="bytes"/> bytes remain, or the end of the stream is not reached yet.</summary>
    public bool HasBytes(int bytes) => !_endOfStream || ((_bufferOffset + _length) << 3) - BitPosition >= (long)bytes << 3;

    /// <summary>Returns the next 16 bits, left-aligned in the low 16 bits of the result, without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Peek16()
    {
        if (_count < 16)
        {
            Refill();
        }

        return (uint)(_bits >> 48);
    }

    /// <summary>Returns the next <paramref name="count"/> bits (1 to 32) without consuming them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Peek(int count)
    {
        if (_count < count)
        {
            Refill();
        }

        return (uint)(_bits >> (64 - count));
    }

    /// <summary>Consumes <paramref name="count"/> bits that a previous peek made available.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count)
    {
        _bits <<= count;
        _count -= count;
    }

    /// <summary>Reads <paramref name="count"/> bits (0 to 32).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int count)
    {
        if (count is 0)
            return 0;

        var value = Peek(count);
        Skip(count);
        return value;
    }

    /// <summary>Reads <paramref name="count"/> bits (0 to 64).</summary>
    public ulong ReadBits64(int count)
    {
        if (count <= 32)
            return ReadBits(count);

        var high = (ulong)ReadBits(count - 32);
        return (high << 32) | ReadBits(32);
    }

    /// <summary>Skips the bits up to the next byte boundary.</summary>
    public void AlignToByte()
    {
        if (_count < 8)
        {
            Refill();
        }

        Skip(_count & 7);
    }

    /// <summary>Reads a byte at the current bit position, or returns -1 when every byte of the stream is read.</summary>
    public int ReadByteOrEnd()
    {
        if (_endOfStream && BitPosition + 8 > (_bufferOffset + _length) << 3)
            return -1;

        return (int)ReadBits(8);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Refill()
    {
        if (_position > _length - 8)
        {
            RefillSlow();
        }

        var value = BinaryPrimitives.ReadUInt64BigEndian(_buffer.AsSpan(_position, 8));
        _bits |= value >> _count;
        var bytes = (63 - _count) >> 3;
        _position += bytes;
        _count += bytes << 3;
    }

    private void RefillSlow()
    {
        if (!_endOfStream)
        {
            var remaining = _length - _position;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_buffer, _position, _buffer, 0, remaining);
            }

            _bufferOffset += _position;
            _position = 0;
            _length = remaining;
            while (_length < BufferSize)
            {
                var read = _stream.Read(_buffer, _length, BufferSize - _length);
                if (read is 0)
                {
                    _endOfStream = true;
                    break;
                }

                _length += read;
                if (_length >= 1024)
                    break;
            }

            if (_endOfStream)
            {
                Array.Clear(_buffer, _length, Padding);
            }
        }

        if (_endOfStream && _position > _length + 8)
            throw new InvalidDataException("Unexpected end of the compressed data.");
    }
}
