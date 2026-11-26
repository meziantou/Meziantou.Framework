using System.Buffers;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The network side of the streams of a <c>WebTransport</c> object (WebTransportSendStream and WebTransportReceiveStream of
/// Streams.js): a stream of the <see cref="WebTransportConnection"/>. The results are given by tasks of the event loop.
/// </summary>
#pragma warning disable CA1001 // The CancellationTokenSource has no timer: it is canceled when the session ends, and needs no disposal
internal sealed class WebTransportHostStream
{
    private const int ReadSize = 64 * 1024;

    private readonly WebTransportHostSession _session;
    private readonly WebTransportConnectionStream _stream;
    private readonly CancellationTokenSource _cancellation = new();
    private int _released;

    public WebTransportHostStream(WebTransportHostSession session, WebTransportConnectionStream stream)
    {
        _session = session;
        _stream = stream;
    }

    /// <summary>Gets a value indicating whether the stream is bidirectional.</summary>
    public bool IsBidirectional => _stream.IsBidirectional;

    /// <summary>
    /// Reads the next chunk; the callback gets the bytes, or null at the end of the stream, or the error code of the server
    /// when it reset the stream (null when it does not map to a WebTransport code), or a failure.
    /// </summary>
    public void Read(Action<ReadResult> callback)
    {
        _ = ReadAsync(callback);
    }

    private async Task ReadAsync(Action<ReadResult> callback)
    {
        ReadResult result;
        try
        {
            // The chunk is a copy of the bytes read: the buffer of the read goes back to the pool
            var buffer = ArrayPool<byte>.Shared.Rent(ReadSize);
            try
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(0, ReadSize), _cancellation.Token).ConfigureAwait(false);
                result = read == 0 ? new ReadResult(Chunk: null, IsReset: false, ErrorCode: null, IsFailure: false) : new ReadResult(buffer.AsSpan(0, read).ToArray(), IsReset: false, ErrorCode: null, IsFailure: false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (WebTransportStreamAbortedException ex)
        {
            result = new ReadResult(Chunk: null, IsReset: true, ex.ErrorCode, IsFailure: false);
        }
#pragma warning disable CA1031 // The session is closed: the stream is errored by the cleanup of the session
        catch (Exception)
#pragma warning restore CA1031
        {
            result = new ReadResult(Chunk: null, IsReset: false, ErrorCode: null, IsFailure: true);
        }

        _session.QueueTask(() => callback(result));
    }

    /// <summary>Writes bytes, or the end of the stream; the callback gets null, or the error code of a STOP_SENDING of the server, or a failure.</summary>
    public void Write(ReadOnlyMemory<byte> bytes, bool endOfStream, Action<WriteResult> callback)
    {
        _session.BeginOperation();
        _ = WriteAsync(bytes, endOfStream, callback);
    }

    private async Task WriteAsync(ReadOnlyMemory<byte> bytes, bool endOfStream, Action<WriteResult> callback)
    {
        WriteResult result;
        try
        {
            await _stream.WriteAsync(bytes, endOfStream, _cancellation.Token).ConfigureAwait(false);
            result = new WriteResult(IsSuccess: true, IsStopSending: false, ErrorCode: null);
        }
        catch (WebTransportStreamAbortedException ex)
        {
            result = new WriteResult(IsSuccess: false, IsStopSending: true, ex.ErrorCode);
        }
#pragma warning disable CA1031 // The session is closed: the stream is errored by the cleanup of the session
        catch (Exception)
#pragma warning restore CA1031
        {
            result = new WriteResult(IsSuccess: false, IsStopSending: false, ErrorCode: null);
        }

        _session.QueueTask(() => callback(result));
        _session.EndOperation();
    }

    /// <summary>Calls the callback, from a task, when the server sends STOP_SENDING.</summary>
    public void OnStopSending(Action<uint?> callback)
    {
        _ = WatchStopSendingAsync(callback);
    }

    private async Task WatchStopSendingAsync(Action<uint?> callback)
    {
        try
        {
            await _stream.WritesClosed.WaitAsync(_cancellation.Token).ConfigureAwait(false);
        }
        catch (WebTransportStreamAbortedException ex)
        {
            _session.QueueTask(() => callback(ex.ErrorCode));
        }
#pragma warning disable CA1031 // The writes ended otherwise
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    public void AbortRead(uint errorCode) => _stream.AbortRead(errorCode);

    public void AbortWrite(uint errorCode) => _stream.AbortWrite(errorCode);

    // The page can set the send order of a stream whose session ended: the setter never throws
    public void SetSendOrder(long sendOrder)
    {
        if (Volatile.Read(ref _released) == 0)
        {
            _stream.SetSendOrder(sendOrder);
        }
    }

    /// <summary>The session ended: the stream is released.</summary>
    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1)
            return;

        _cancellation.Cancel();
        _session.RemoveStream(this);
        _ = DisposeAsync();
    }

    private async Task DisposeAsync()
    {
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The stream is released anyway
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>The result of a read.</summary>
    internal readonly record struct ReadResult(ReadOnlyMemory<byte>? Chunk, bool IsReset, uint? ErrorCode, bool IsFailure);

    /// <summary>The result of a write.</summary>
    internal readonly record struct WriteResult(bool IsSuccess, bool IsStopSending, uint? ErrorCode);
}
#pragma warning restore CA1001
