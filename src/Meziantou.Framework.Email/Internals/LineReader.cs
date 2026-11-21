namespace Meziantou.Framework.Email.Internals;

/// <summary>Reads the lines of a stream, with their line breaks (LF or CRLF), through a buffer.</summary>
internal sealed class LineReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private int _start;
    private int _end;
    private byte[] _line = new byte[256];

    public LineReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>Gets the position, in the stream, of the next byte to read.</summary>
    public long Position { get; private set; }

    /// <summary>Reads the next line, with its line break. Returns <see langword="false"/> at the end of the stream.</summary>
    /// <param name="line">The line, valid until the next read.</param>
    /// <param name="maxLength">The length of the longest line to read.</param>
    /// <exception cref="EmailResourceLimitException">The line is longer than <paramref name="maxLength"/>.</exception>
    public bool ReadLine(out ReadOnlySpan<byte> line, long maxLength)
    {
        var length = 0;
        while (true)
        {
            if (_start == _end && !Fill())
            {
                line = _line.AsSpan(0, length);
                return length > 0;
            }

            var available = _buffer.AsSpan(_start, _end - _start);
            var newLine = available.IndexOf((byte)'\n');
            var count = newLine >= 0 ? newLine + 1 : available.Length;
            if (length + count > maxLength)
            {
                throw new EmailResourceLimitException($"A line is longer than the limit of {maxLength} bytes.");
            }

            if (length + count > _line.Length)
            {
                Array.Resize(ref _line, Math.Max(_line.Length * 2, length + count));
            }

            available[..count].CopyTo(_line.AsSpan(length));
            length += count;
            _start += count;
            Position += count;
            if (newLine >= 0)
            {
                line = _line.AsSpan(0, length);
                return true;
            }
        }
    }

    /// <summary>Reads up to <paramref name="count"/> bytes into a stream. Returns the number of bytes read.</summary>
    public long CopyTo(Stream destination, long count)
    {
        long copied = 0;
        while (copied < count)
        {
            if (_start == _end && !Fill())
            {
                break;
            }

            var length = (int)Math.Min(_end - _start, count - copied);
            destination.Write(_buffer, _start, length);
            _start += length;
            Position += length;
            copied += length;
        }

        return copied;
    }

    private bool Fill()
    {
        _start = 0;
        _end = _stream.Read(_buffer, 0, _buffer.Length);
        return _end > 0;
    }
}
