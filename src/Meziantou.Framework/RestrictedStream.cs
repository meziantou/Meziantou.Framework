namespace Meziantou.Framework;

/// <summary>A wrapper stream that restricts access to specific operations based on configured options.</summary>
/// <param name="stream">The underlying stream to wrap.</param>
/// <param name="options">The options that control which operations are allowed.</param>
public sealed class RestrictedStream(Stream stream, RestrictedStreamOptions options) : Stream
{
    /// <inheritdoc />
    public override bool CanRead => stream.CanRead && options.AllowReading;

    /// <inheritdoc />
    public override bool CanSeek => stream.CanSeek && options.AllowSeeking;

    /// <inheritdoc />
    public override bool CanWrite => stream.CanWrite && options.AllowWriting;

    /// <inheritdoc />
    public override long Length => stream.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => stream.Position;
        set
        {
            ThrowIfSeekingNotAllowed();
            stream.Position = value;
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        stream.Flush();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.Read(buffer, offset, count);
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfSeekingNotAllowed();
        return stream.Seek(offset, origin);
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        ThrowIfWritingNotAllowed();
        stream.SetLength(value);
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        stream.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.BeginRead(buffer, offset, count, callback, state);
    }

    /// <inheritdoc />
    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        return stream.BeginWrite(buffer, offset, count, callback, state);
    }

    /// <inheritdoc />
    public override void CopyTo(Stream destination, int bufferSize)
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        stream.CopyTo(destination, bufferSize);
    }

    /// <inheritdoc />
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        stream.Dispose();
    }

    /// <inheritdoc />
    public async override ValueTask DisposeAsync()
    {
        await stream.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override int EndRead(IAsyncResult asyncResult)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.EndRead(asyncResult);
    }

    /// <inheritdoc />
    public override void EndWrite(IAsyncResult asyncResult)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        stream.EndWrite(asyncResult);
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        return stream.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.Read(buffer);
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override int ReadByte()
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfReadingNotAllowed();
        return stream.ReadByte();
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        stream.Write(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        return stream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfAsynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        return stream.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        ThrowIfSynchronousCallNotAllowed();
        ThrowIfWritingNotAllowed();
        stream.WriteByte(value);
    }

    private void ThrowIfSynchronousCallNotAllowed()
    {
        if (!options.AllowSynchronousCalls)
            throw new NotSupportedException("Synchronous operations are not allowed on this stream.");
    }

    private void ThrowIfAsynchronousCallNotAllowed()
    {
        if (!options.AllowAsynchronousCalls)
            throw new NotSupportedException("Asynchronous operations are not allowed on this stream.");
    }

    private void ThrowIfReadingNotAllowed()
    {
        if (!options.AllowReading)
            throw new NotSupportedException("Reading is not allowed on this stream.");
    }

    private void ThrowIfWritingNotAllowed()
    {
        if (!options.AllowWriting)
            throw new NotSupportedException("Writing is not allowed on this stream.");
    }

    private void ThrowIfSeekingNotAllowed()
    {
        if (!options.AllowSeeking)
            throw new NotSupportedException("Seeking is not allowed on this stream.");
    }
}
