namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A stream of a <see cref="WebTransportConnection"/>, implemented by the <see cref="DocumentLoader"/>. The error codes are
/// the 32-bit application error codes of WebTransport.
/// </summary>
/// <remarks>
/// The library calls at most one <see cref="ReadAsync"/> and one <see cref="WriteAsync"/> at a time, from any thread.
/// A read or a write of a stream the server aborted throws a <see cref="WebTransportStreamAbortedException"/>.
/// </remarks>
public abstract class WebTransportConnectionStream : IAsyncDisposable
{
    /// <summary>Gets a value indicating whether the stream is bidirectional.</summary>
    public abstract bool IsBidirectional { get; }

    /// <summary>Gets a value indicating whether the stream can be read: a bidirectional stream, or a unidirectional stream the server opened.</summary>
    public abstract bool CanRead { get; }

    /// <summary>Gets a value indicating whether the stream can be written: a bidirectional stream, or a unidirectional stream the page opened.</summary>
    public abstract bool CanWrite { get; }

    /// <summary>
    /// Gets a task that completes when the writing side is closed, and faults with a
    /// <see cref="WebTransportStreamAbortedException"/> when the server sends STOP_SENDING, even when no write is pending.
    /// </summary>
    public abstract Task WritesClosed { get; }

    /// <summary>Reads data. Returns 0 at the end of the stream.</summary>
    public abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Writes data, and optionally the end of the stream (FIN).</summary>
    public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, bool endOfStream, CancellationToken cancellationToken);

    /// <summary>Stops reading: sends STOP_SENDING with the error code.</summary>
    public abstract void AbortRead(uint errorCode);

    /// <summary>Aborts the writing side: sends RESET_STREAM with the error code.</summary>
    public abstract void AbortWrite(uint errorCode);

    /// <summary>Sets the priority of the stream: the data of the streams with a higher send order is sent first.</summary>
    public virtual void SetSendOrder(long sendOrder)
    {
    }

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();
}
