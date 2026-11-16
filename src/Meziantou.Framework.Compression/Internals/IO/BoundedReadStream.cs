using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Compression.Internals.IO;

/// <summary>
/// Reads at most <c>length</c> bytes of another stream. On a seekable stream, the window starts at an absolute offset and
/// each read seeks to its position, so several windows of the same stream can be read alternately; otherwise the window
/// starts at the current position of the stream. Disposing the window does not dispose the stream.
/// </summary>
internal sealed class BoundedReadStream : Stream
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The window does not own the stream")]
    private readonly Stream _stream;
    private readonly long _start;
    private readonly long _length;
    private readonly bool _seekable;
    private long _position;

    /// <summary>A window that starts at the current position of a forward-only (or seekable, read sequentially) stream.</summary>
    public BoundedReadStream(Stream stream, long length)
    {
        _stream = stream;
        _length = length;
        _start = -1;
        _seekable = false;
    }

    /// <summary>A window at an absolute offset of a seekable stream.</summary>
    public BoundedReadStream(Stream stream, long start, long length)
    {
        _stream = stream;
        _start = start;
        _length = length;
        _seekable = true;
    }

    /// <summary>Gets a value indicating whether the stream ended before the end of the window.</summary>
    public bool IsTruncated { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => _seekable;

    public override bool CanWrite => false;

    public override long Length => _length;

    public long Remaining => _length - _position;

    public override long Position
    {
        get => _position;
        set
        {
            if (!_seekable)
                throw new NotSupportedException();

            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _length - _position;
        if (remaining <= 0 || buffer.IsEmpty)
            return 0;

        if (buffer.Length > remaining)
        {
            buffer = buffer[..(int)remaining];
        }

        if (_seekable && _stream.Position != _start + _position)
        {
            _stream.Position = _start + _position;
        }

        var read = _stream.Read(buffer);
        if (read is 0)
        {
            IsTruncated = true;
        }

        _position += read;
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remaining = _length - _position;
        if (remaining <= 0 || buffer.IsEmpty)
            return 0;

        if (buffer.Length > remaining)
        {
            buffer = buffer[..(int)remaining];
        }

        if (_seekable && _stream.Position != _start + _position)
        {
            _stream.Position = _start + _position;
        }

        var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read is 0)
        {
            IsTruncated = true;
        }

        _position += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <summary>Reads and discards the rest of the window.</summary>
    public void SkipToEnd()
    {
        if (_seekable)
        {
            _position = _length;
            return;
        }

        var buffer = new byte[Math.Min(81920, Math.Max(1, Remaining))];
        while (Read(buffer) > 0)
        {
        }
    }

    /// <summary>Reads and discards the rest of the window.</summary>
    public async Task SkipToEndAsync(CancellationToken cancellationToken)
    {
        if (_seekable)
        {
            _position = _length;
            return;
        }

        var buffer = new byte[Math.Min(81920, Math.Max(1, Remaining))];
        while (await ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!_seekable)
            throw new NotSupportedException();

        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
