using System.Buffers;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// A buffer over the compressed input that the range decoder reads without a bounds check per byte. The array always has
/// <see cref="Padding"/> readable bytes after <see cref="End"/>: a symbol reads at most one byte per bit it decodes, so a
/// symbol that starts at or before the end of the data never reads outside the array, and the decoder detects afterward
/// that it read past the end of the data. The containers (XZ, lzip) read their headers from the same buffer, so nothing the
/// codec does not consume is lost.
/// </summary>
internal sealed class LzmaInputBuffer : IDisposable
{
    /// <summary>The number of bytes after <see cref="End"/> that are always in the array (a symbol decodes at most 51 bits).</summary>
    public const int Padding = 64;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private byte[] _buffer;

    public LzmaInputBuffer(Stream stream, bool leaveOpen, int capacity = 1 << 16)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _buffer = ArrayPool<byte>.Shared.Rent(capacity + Padding);
        Array.Clear(_buffer);
    }

    /// <summary>Gets the array that holds the data.</summary>
    public byte[] Buffer => _buffer;

    /// <summary>Gets or sets the position of the next byte to read.</summary>
    public int Position { get; set; }

    /// <summary>Gets the end of the data of the array.</summary>
    public int End { get; private set; }

    /// <summary>Gets a value indicating whether the stream has ended (the data of the array is the end of the input).</summary>
    public bool IsEndOfStream { get; private set; }

    /// <summary>Gets the number of bytes that the codecs and the containers consumed since the creation of the buffer.</summary>
    public long TotalConsumed => _discarded + Position;

    /// <summary>Gets the number of buffered bytes.</summary>
    public int Available => End - Position;

    private long _discarded;

    /// <summary>
    /// Reads from the stream until at least <paramref name="count"/> bytes are buffered, or the stream ends.
    /// </summary>
    /// <returns><see langword="true"/> when <paramref name="count"/> bytes are buffered.</returns>
    public bool Fill(int count)
    {
        if (End - Position >= count)
            return true;

        if (IsEndOfStream)
            return false;

        if (Position + count > _buffer.Length - Padding)
        {
            // Move the remaining data to the start of the array, and grow it when the request does not fit
            var remaining = End - Position;
            if (count > _buffer.Length - Padding)
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(count + Padding);
                _buffer.AsSpan(Position, remaining).CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
            }
            else
            {
                _buffer.AsSpan(Position, remaining).CopyTo(_buffer);
            }

            _discarded += Position;
            Position = 0;
            End = remaining;
        }

        while (End - Position < count)
        {
            var read = _stream.Read(_buffer, End, _buffer.Length - Padding - End);
            if (read <= 0)
            {
                IsEndOfStream = true;
                break;
            }

            End += read;
        }

        // The bytes after the data are zeros: a decoder that reads them reads past the end of the data
        _buffer.AsSpan(End, Padding).Clear();
        return End - Position >= count;
    }

    /// <summary>Reads a byte, or returns -1 at the end of the stream.</summary>
    public int ReadByte()
    {
        if (Position >= End && !Fill(1))
            return -1;

        return _buffer[Position++];
    }

    /// <summary>Reads a byte, or throws an <see cref="InvalidDataException"/> at the end of the stream.</summary>
    public byte ReadByteOrThrow()
    {
        if (Position >= End && !Fill(1))
            throw new InvalidDataException("Unexpected end of data.");

        return _buffer[Position++];
    }

    /// <summary>Fills <paramref name="destination"/>, or throws an <see cref="InvalidDataException"/> when the stream ends first.</summary>
    public void ReadExactly(Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            if (Position >= End && !Fill(1))
                throw new InvalidDataException("Unexpected end of data.");

            var count = Math.Min(destination.Length, End - Position);
            _buffer.AsSpan(Position, count).CopyTo(destination);
            Position += count;
            destination = destination[count..];
        }
    }

    /// <summary>Copies at most <paramref name="destination"/>.Length bytes, and returns 0 at the end of the stream.</summary>
    public int Read(Span<byte> destination)
    {
        if (destination.IsEmpty)
            return 0;

        if (Position >= End && !Fill(1))
            return 0;

        var count = Math.Min(destination.Length, End - Position);
        _buffer.AsSpan(Position, count).CopyTo(destination);
        Position += count;
        return count;
    }

    /// <summary>Gets a value indicating whether the stream has no more data.</summary>
    public bool IsAtEnd() => Position >= End && !Fill(1);

    public void Dispose()
    {
        if (_buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
