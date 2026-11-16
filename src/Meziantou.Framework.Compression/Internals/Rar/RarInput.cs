using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// A buffered, forward reader of a volume of a RAR archive that tracks its position. On a seekable stream, skipping
/// seeks; otherwise it reads and discards.
/// </summary>
internal sealed class RarInput
{
    private const int BufferSize = 64 * 1024;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The input does not own the stream")]
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[BufferSize];
    private readonly bool _seekable;
    private int _offset;
    private int _length;
    private long _bufferPosition;

    public RarInput(Stream stream)
    {
        _stream = stream;
        _seekable = stream.CanSeek;
        _bufferPosition = _seekable ? stream.Position : 0;
    }

    public Stream Stream => _stream;

    /// <summary>Gets the position of the next byte in the stream (relative to the position of the stream when the input was created, on a non-seekable stream).</summary>
    public long Position => _bufferPosition + _offset;

    public int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        if (_offset == _length)
        {
            if (buffer.Length >= BufferSize)
            {
                SyncSeekableStream();
                var direct = _stream.Read(buffer);
                _bufferPosition += _offset + direct;
                _offset = 0;
                _length = 0;
                return direct;
            }

            if (!Fill())
                return 0;
        }

        var count = Math.Min(buffer.Length, _length - _offset);
        _buffer.AsSpan(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public void ReadExactly(Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = Read(buffer);
            if (read is 0)
                throw new InvalidDataException("Unexpected end of the archive.");

            buffer = buffer[read..];
        }
    }

    /// <summary>Reads up to <paramref name="buffer"/>.Length bytes, fewer only at the end of the stream.</summary>
    public int ReadAtMost(Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = Read(buffer[total..]);
            if (read is 0)
                break;

            total += read;
        }

        return total;
    }

    public int ReadByte()
    {
        if (_offset == _length && !Fill())
            return -1;

        return _buffer[_offset++];
    }

    /// <summary>Skips <paramref name="count"/> bytes; returns <see langword="false"/> when the stream ends first.</summary>
    public bool Skip(long count)
    {
        var buffered = _length - _offset;
        if (count <= buffered)
        {
            _offset += (int)count;
            return true;
        }

        if (_seekable)
        {
            var target = Position + count;
            _bufferPosition = target;
            _offset = 0;
            _length = 0;
            if (target > _stream.Length)
                return false;

            _stream.Position = target;
            return true;
        }

        count -= buffered;
        _offset = _length;
        while (count > 0)
        {
            if (!Fill())
                return false;

            var step = (int)Math.Min(count, _length);
            _offset = step;
            count -= step;
        }

        return true;
    }

    /// <summary>
    /// Finds the signature of a RAR archive in the first <paramref name="maxOffset"/> bytes (the size of the largest SFX
    /// module), and positions the input after it.
    /// </summary>
    /// <returns>The format of the archive, or <see langword="null"/> when there is no signature.</returns>
    public RarFormatVersion? FindSignature(long maxOffset)
    {
        ReadOnlySpan<byte> prefix = "Rar!\u001A\u0007"u8;
        long scanned = 0;
        while (true)
        {
            if (!FillAtLeast(8))
            {
                // Not enough bytes for the 8-byte signature of RAR5: the 7-byte signature of RAR 1.5-4.x may still fit
                if (_length - _offset >= 7 && _buffer.AsSpan(_offset, 7).SequenceEqual("Rar!\u001A\u0007\u0000"u8))
                {
                    _offset += 7;
                    return RarFormatVersion.Rar4;
                }

                return null;
            }

            var span = _buffer.AsSpan(_offset, _length - _offset);
            var index = span.IndexOf(prefix);
            if (index >= 0 && index + 8 <= span.Length)
            {
                if (span[index + 6] is 0)
                {
                    _offset += index + 7;
                    return RarFormatVersion.Rar4;
                }

                if (span[index + 6] is 1 && span[index + 7] is 0)
                {
                    _offset += index + 8;
                    return RarFormatVersion.Rar5;
                }

                // Not a signature: continue after it
                _offset += index + 1;
                scanned += index + 1;
            }
            else
            {
                // Keep the last 7 bytes: a signature may cross the end of the buffer
                var consumed = index >= 0 ? index : Math.Max(0, span.Length - 7);
                _offset += consumed;
                scanned += consumed;
                if (consumed is 0 && !FillAtLeast(span.Length + 1))
                    return null;
            }

            if (scanned > maxOffset)
                return null;
        }
    }

    /// <summary>Returns whether the stream starts with the signature of the archives of RAR 1.4 and older.</summary>
    public bool StartsWithOldSignature()
    {
        return FillAtLeast(4) && _buffer.AsSpan(_offset, 4).SequenceEqual("RE~^"u8);
    }

    private bool FillAtLeast(int count)
    {
        if (_length - _offset >= count)
            return true;

        if (_offset > 0)
        {
            Buffer.BlockCopy(_buffer, _offset, _buffer, 0, _length - _offset);
            _bufferPosition += _offset;
            _length -= _offset;
            _offset = 0;
        }

        SyncSeekableStream();
        while (_length < count)
        {
            var read = _stream.Read(_buffer, _length, BufferSize - _length);
            if (read is 0)
                return false;

            _length += read;
        }

        return true;
    }

    private bool Fill()
    {
        _bufferPosition += _length;
        _offset = 0;
        _length = 0;
        SyncSeekableStream();
        _length = _stream.Read(_buffer, 0, BufferSize);
        return _length > 0;
    }

    private void SyncSeekableStream()
    {
        // The entries of a random-access archive read the stream at other positions
        if (_seekable)
        {
            var position = _bufferPosition + _length;
            if (_stream.Position != position)
            {
                _stream.Position = position;
            }
        }
    }
}
