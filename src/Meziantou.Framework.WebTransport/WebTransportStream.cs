using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebTransport.Http3;

namespace Meziantou.Framework.WebTransport;

/// <summary>A stream of a <see cref="WebTransportSession"/>.</summary>
/// <remarks>
/// The error codes of <see cref="Abort"/> and <see cref="WebTransportException.StreamErrorCode"/> are the 32-bit
/// application error codes of WebTransport, mapped to HTTP/3 error codes on the wire. When the session ends, its streams
/// are aborted.
/// </remarks>
public sealed class WebTransportStream : Stream
{
    private readonly MsQuicStream _stream;
    private readonly int _headerLength;
    private readonly Lazy<Task> _readsClosed;
    private readonly Lazy<Task> _writesClosed;
    private int _disposed;

    internal WebTransportStream(WebTransportSession session, MsQuicStream stream, int headerLength, bool isOutbound)
    {
        Session = session;
        _stream = stream;
        _headerLength = headerLength;
        IsOutbound = isOutbound;
        _readsClosed = new Lazy<Task>(() => MapAsync(_stream.ReadsClosed));
        _writesClosed = new Lazy<Task>(() => MapAsync(_stream.WritesClosed));
    }

    /// <summary>Gets the session of the stream.</summary>
    public WebTransportSession Session { get; }

    /// <summary>Gets the QUIC stream identifier.</summary>
    public long Id => _stream.Id;

    /// <summary>Gets the type of the stream.</summary>
    public MsQuicStreamType Type => _stream.Type;

    /// <summary>Gets a value indicating whether this endpoint opened the stream.</summary>
    public bool IsOutbound { get; }

    /// <summary>Gets a task that completes when the reading side is closed. It faults with a <see cref="WebTransportException"/> when the reading side was aborted.</summary>
    public Task ReadsClosed => _readsClosed.Value;

    /// <summary>Gets a task that completes when the writing side is closed and the peer received all the data. It faults with a <see cref="WebTransportException"/> when the writing side was aborted.</summary>
    public Task WritesClosed => _writesClosed.Value;

    /// <summary>Gets the number of bytes read from the stream.</summary>
    public long BytesRead => IsOutbound ? _stream.BytesRead : Math.Max(0, _stream.BytesRead - _headerLength);

    /// <summary>Gets the number of bytes written to the stream.</summary>
    public long BytesWritten => IsOutbound ? Math.Max(0, _stream.BytesWritten - _headerLength) : _stream.BytesWritten;

    /// <summary>Gets or sets the priority of the stream, from 0 (lowest) to 65535 (highest). The data of the streams with a higher priority is sent first.</summary>
    public int Priority
    {
        get => _stream.Priority;
        set => _stream.Priority = value;
    }

    /// <inheritdoc />
    public override bool CanRead => _stream.CanRead;

    /// <inheritdoc />
    public override bool CanWrite => _stream.CanWrite;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    internal bool IsCompleted => _stream.ReadsClosed.IsCompleted && _stream.WritesClosed.IsCompleted;

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException ex)
        {
            throw MapException(ex);
        }
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => WriteAsync(buffer, completeWrites: false, cancellationToken);

    /// <summary>Writes data to the stream, and optionally completes the writes.</summary>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, bool completeWrites, CancellationToken cancellationToken = default)
    {
        try
        {
            await _stream.WriteAsync(buffer, completeWrites, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException ex)
        {
            throw MapException(ex);
        }
    }

    /// <summary>Completes the writes: sends the end of the stream (FIN) once the pending data is sent.</summary>
    public void CompleteWrites() => _stream.CompleteWrites();

    /// <summary>Aborts one or both sides of the stream with an application error code.</summary>
    /// <param name="abortDirection">The sides to abort.</param>
    /// <param name="errorCode">The WebTransport application error code.</param>
    public void Abort(MsQuicAbortDirection abortDirection, uint errorCode) => _stream.Abort(abortDirection, Http3ErrorCode.FromWebTransportErrorCode(errorCode));

    // When the session ends, its streams are reset with WT_SESSION_GONE
    internal void AbortSessionGone()
    {
        if (_disposed == 0)
        {
            _stream.Abort(MsQuicAbortDirection.Both, Http3ErrorCode.WebTransportSessionGone);
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        Session.RemoveStream(this);
        await _stream.DisposeAsync().ConfigureAwait(false);

        // Stream.DisposeAsync calls Dispose(true), which returns since the stream is already disposed
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private async Task MapAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (MsQuicException ex)
        {
            throw MapException(ex);
        }
    }

    internal Exception MapException(MsQuicException exception)
    {
        if (exception.Error is MsQuicError.StreamAborted && exception.ApplicationErrorCode is { } code)
        {
            if (code == Http3ErrorCode.WebTransportSessionGone)
                return new WebTransportException(WebTransportError.SessionClosed, "The session of the stream is closed.", exception);

            return new WebTransportException(WebTransportError.StreamAborted, $"The peer aborted the stream.", exception) { StreamErrorCode = Http3ErrorCode.ToWebTransportErrorCode(code) };
        }

        if (exception.Error is MsQuicError.OperationAborted)
            return Session.CloseException ?? new WebTransportException(WebTransportError.OperationAborted, exception.Message, exception);

        return new WebTransportException(WebTransportError.ConnectionFailed, exception.Message, exception);
    }
}
