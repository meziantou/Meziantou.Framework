using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// A stream of a session of the emulated WebTransport server: the stream of the browser, and the state of both sides. The
/// identifier is the identifier of the QUIC stream (its two lower bits are its initiator and its direction). The state is
/// guarded by the lock of the session.
/// </summary>
internal sealed class WptWebTransportStream : WebTransportConnectionStream
{
    private readonly WptWebTransportSession _session;
    private readonly Queue<byte[]> _received = new();
    private int _receivedOffset;

    public WptWebTransportStream(WptWebTransportSession session, long id)
    {
        _session = session;
        Id = id;
        WritesClosedSource = new TaskCompletionSource();
        WptWebTransportSession.ObserveException(WritesClosedSource.Task);
        if (!CanWrite)
        {
            WritesClosedSource.SetResult();
        }
    }

    /// <summary>Gets the identifier of the QUIC stream.</summary>
    public long Id { get; }

    /// <summary>Gets a value indicating whether the browser opened the stream.</summary>
    public bool IsClientInitiated => (Id & 1) == 0;

    public override bool IsBidirectional => (Id & 2) == 0;

    public override bool CanRead => IsBidirectional || !IsClientInitiated;

    public override bool CanWrite => IsBidirectional || IsClientInitiated;

    public override Task WritesClosed => WritesClosedSource.Task;

    /// <summary>Gets the source of <see cref="WritesClosed"/>.</summary>
    public TaskCompletionSource WritesClosedSource { get; }

    // The side written by the browser

    /// <summary>Gets or sets the write of the browser that is not delivered to the handler yet.</summary>
    public TaskCompletionSource? PendingWrite { get; set; }

    /// <summary>Gets or sets a value indicating whether the browser wrote the end of the stream.</summary>
    public bool FinWritten { get; set; }

    /// <summary>Gets or sets a value indicating whether the end of the stream written by the browser was delivered to the handler.</summary>
    public bool FinDelivered { get; set; }

    /// <summary>Gets or sets a value indicating whether the browser aborted the side it writes (RESET_STREAM).</summary>
    public bool WriteAborted { get; set; }

    /// <summary>Gets or sets a value indicating whether the handler sent STOP_SENDING.</summary>
    public bool StopSendingReceived { get; set; }

    /// <summary>Gets or sets the WebTransport error code of the STOP_SENDING of the handler.</summary>
    public uint? StopSendingErrorCode { get; set; }

    // The side written by the handler

    /// <summary>Gets or sets a value indicating whether the handler wrote the end of the stream.</summary>
    public bool ServerFin { get; set; }

    /// <summary>Gets or sets a value indicating whether the handler aborted the side it writes (RESET_STREAM).</summary>
    public bool ServerReset { get; set; }

    /// <summary>Gets or sets the WebTransport error code of the RESET_STREAM of the handler.</summary>
    public uint? ServerResetErrorCode { get; set; }

    /// <summary>Gets or sets a value indicating whether the browser stopped reading (STOP_SENDING).</summary>
    public bool ReadAborted { get; set; }

    /// <summary>Gets or sets a value indicating whether the browser read the end of the stream, or the reset of the handler.</summary>
    public bool ReadCompleted { get; set; }

    /// <summary>Gets or sets the read of the browser that waits for data.</summary>
    public TaskCompletionSource? ReadWaiter { get; set; }

    /// <summary>Gets or sets a value indicating whether the stream no longer counts in the streams the browser can open.</summary>
    public bool Released { get; set; }

    /// <summary>Gets or sets a value indicating whether the browser disposed the stream.</summary>
    public bool Disposed { get; set; }

    /// <summary>Gets a value indicating whether data written by the handler is waiting to be read.</summary>
    public bool HasReceivedData => _received.Count > 0;

    /// <summary>Gets a value indicating whether both sides of the stream are closed.</summary>
    public bool IsCompleted => Disposed
        || ((!CanWrite || FinDelivered || WriteAborted || StopSendingReceived)
            && (!CanRead || ReadCompleted || ReadAborted));

    public void EnqueueReceived(byte[] data)
    {
        if (data.Length > 0)
        {
            _received.Enqueue(data);
        }
    }

    public int DequeueReceived(Span<byte> destination)
    {
        var written = 0;
        while (written < destination.Length && _received.TryPeek(out var chunk))
        {
            var count = Math.Min(destination.Length - written, chunk.Length - _receivedOffset);
            chunk.AsSpan(_receivedOffset, count).CopyTo(destination[written..]);
            written += count;
            _receivedOffset += count;
            if (_receivedOffset == chunk.Length)
            {
                _received.Dequeue();
                _receivedOffset = 0;
            }
        }

        return written;
    }

    public void ClearReceived()
    {
        _received.Clear();
        _receivedOffset = 0;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => _session.ReadAsync(this, buffer, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, bool endOfStream, CancellationToken cancellationToken) => _session.WriteAsync(this, buffer, endOfStream, cancellationToken);

    public override void AbortRead(uint errorCode) => _session.AbortRead(this);

    public override void AbortWrite(uint errorCode) => _session.AbortWrite(this, errorCode);

    public override ValueTask DisposeAsync()
    {
        _session.DisposeStream(this);
        return ValueTask.CompletedTask;
    }
}
