using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// A session of the emulated WebTransport server: the <c>session</c> object given to the handlers (<c>WebTransportSession</c>
/// of webtransport_h3_server.py, with the members of aioquic the handlers use), and the state shared with the browser side
/// (<see cref="WptWebTransportConnection"/>, <see cref="WptWebTransportStream"/>).
/// </summary>
/// <remarks>
/// <para>
/// Each session has its own QUIC connection: the session is the CONNECT stream (<c>0</c>), and the streams have the
/// identifiers of the QUIC streams (https://www.rfc-editor.org/rfc/rfc9000#section-2.1): the bidirectional streams of the
/// browser are <c>4</c>, <c>8</c>..., the ones of the server <c>1</c>, <c>5</c>...; the unidirectional streams start after
/// the control and QPACK streams of HTTP/3: <c>14</c>, <c>18</c>... for the browser, <c>15</c>, <c>19</c>... for the server.
/// </para>
/// <para>
/// The methods of the handlers run on the thread of the server (see <see cref="WptWebTransportServer.Post"/>), the methods of
/// the browser on any thread: the operations of the browser that wait are completed by tasks of the event loop of the test
/// page (<see cref="WptWebTransportServer.QueueClientTask"/>), and an operation that waits for a change of the state checks
/// the state again when it is woken up.
/// </para>
/// </remarks>
internal sealed class WptWebTransportSession
{
    /// <summary>The maximum size of a datagram: a QUIC packet of 1280 bytes (the minimum MTU of IPv6) without the headers.</summary>
    public const int MaxDatagramSize = 1200;

    /// <summary>The number of streams of each type the browser can open at the same time (the initial MAX_STREAMS of aioquic).</summary>
    private const int ClientStreamLimit = 128;

    /// <summary>The number of streams of each type the server can open at the same time, unless the page anticipates more (the default of Chromium).</summary>
    private const int DefaultIncomingStreamLimit = 100;

    /// <summary>The path of the stash of the handlers: the handlers of all sessions share it (<c>WebTransportSession._stash_path</c>).</summary>
    private const string StashPath = "/webtransport/handlers";

    /// <summary>The bytes of the headers of a QUIC packet, for the statistics.</summary>
    private const int PacketOverhead = 30;

    private static readonly TimeSpan RoundTripTime = TimeSpan.FromMilliseconds(1);

    private readonly WptWebTransportServer _server;
    private readonly WptWebTransportHandler _handler;
    private readonly Url _url;
    private readonly WebTransportConnectionRequest _request;
    private readonly Lock _lock = new();
    private readonly Dictionary<long, WptWebTransportStream> _streams = [];
    private readonly Queue<WptWebTransportStream> _incomingStreams = new();
    private readonly Queue<byte[]> _incomingDatagrams = new();
    private readonly List<TaskCompletionSource> _openWaiters = [];
    private TaskCompletionSource? _acceptWaiter;
    private TaskCompletionSource? _datagramWaiter;
    private long _nextClientBidirectionalId = 4;
    private long _nextClientUnidirectionalId = 14;
    private long _nextServerBidirectionalId = 1;
    private long _nextServerUnidirectionalId = 15;
    private int _openClientBidirectionalStreams;
    private int _openClientUnidirectionalStreams;
    private bool _clientClosed;
    private bool _serverClosed;
    private bool _connectionClosed;
    private bool _disposed;
    private bool _sessionClosedCalled;
    private long _bytesSent;
    private long _packetsSent;
    private long _bytesReceived;
    private long _packetsReceived;

    public WptWebTransportSession(WptWebTransportServer server, Url url, WebTransportConnectionRequest request, WptWebTransportHandler handler)
    {
        _server = server;
        _url = url;
        _request = request;
        _handler = handler;
        RequestHeaders = CreateRequestHeaders(url, request);
        RemoteMaxStreamsBidi = Math.Max(DefaultIncomingStreamLimit, request.AnticipatedConcurrentIncomingBidirectionalStreams ?? 0);
        RemoteMaxStreamsUni = Math.Max(DefaultIncomingStreamLimit, request.AnticipatedConcurrentIncomingUnidirectionalStreams ?? 0);
        ClosedSource = new TaskCompletionSource<WebTransportConnectionCloseInfo>();
        ObserveException(ClosedSource.Task);
        DrainingSource = new TaskCompletionSource();
    }

    /// <summary>Gets the source of <see cref="WebTransportConnection.Closed"/>.</summary>
    public TaskCompletionSource<WebTransportConnectionCloseInfo> ClosedSource { get; }

    /// <summary>Gets the source of <see cref="WebTransportConnection.Draining"/>.</summary>
    public TaskCompletionSource DrainingSource { get; }

    // ----------------------------------------------------------------------------------------------------------------
    // The API of the handlers
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary><c>session.session_id</c>: the identifier of the CONNECT stream.</summary>
    public long SessionId => 0;

    /// <summary>
    /// <c>session.request_headers</c>: the headers of the CONNECT request, with the pseudo-headers (<c>:method</c>,
    /// <c>:scheme</c>, <c>:authority</c>, <c>:path</c>, <c>:protocol</c>), then the headers of the browser, then
    /// <c>wt-available-protocols</c> when the page offers protocols.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> RequestHeaders { get; }

    /// <summary><c>session.dict_for_handlers</c>: the values of the handler for the session.</summary>
    public Dictionary<string, object?> DictForHandlers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// <c>session._http._quic._remote_max_streams_bidi</c>: the number of bidirectional streams the browser allows the server
    /// to open, raised to the number the page anticipates (<c>anticipatedConcurrentIncomingBidirectionalStreams</c>).
    /// </summary>
    public int RemoteMaxStreamsBidi { get; }

    /// <summary><c>session._http._quic._remote_max_streams_uni</c>: see <see cref="RemoteMaxStreamsBidi"/>.</summary>
    public int RemoteMaxStreamsUni { get; }

    /// <summary>The <c>:path</c> of the request: the path and the query of the URL.</summary>
    public string Path => HeaderList.Get(RequestHeaders, ":path") ?? "";

    /// <summary><c>urlsplit(path).query</c></summary>
    public string Query => GetQuery(Path);

    /// <summary><c>dict(parse_qsl(urlsplit(path).query))</c>: the parameters of the query (the last value of a name wins).</summary>
    public OrderedDictionary<string, string> QueryParameters => ParseQuery(Query);

    /// <summary><c>session.stream_is_unidirectional(stream_id)</c></summary>
    public static bool StreamIsUnidirectional(long streamId) => (streamId & 2) != 0;

    /// <summary><c>session.stash.take(key)</c></summary>
    public object? StashTake(string key) => _server.Stash.TakeValue(key, StashPath);

    /// <summary><c>session.stash.put(key, value)</c></summary>
    public void StashPut(string key, object value) => _server.Stash.Put(key, value, StashPath);

    /// <summary><c>session.create_bidirectional_stream()</c>: the browser receives the stream at once (aioquic sends the header of the stream).</summary>
    public long CreateBidirectionalStream() => CreateServerStream(bidirectional: true);

    /// <summary><c>session.create_unidirectional_stream()</c></summary>
    public long CreateUnidirectionalStream() => CreateServerStream(bidirectional: false);

    /// <summary><c>session.send_stream_data(stream_id, data, end_stream)</c>. The data sent to a stream the browser stopped reading is lost.</summary>
    public void SendStreamData(long streamId, byte[] data, bool endStream = false)
    {
        lock (_lock)
        {
            var stream = GetStream(streamId);
            if (!stream.CanRead)
                throw new InvalidOperationException("The stream " + streamId.ToString(CultureInfo.InvariantCulture) + " cannot be written by the server.");

            if (stream.ServerFin || stream.ServerReset)
                throw new InvalidOperationException("The stream " + streamId.ToString(CultureInfo.InvariantCulture) + " was ended by the server.");

            stream.ServerFin = endStream;
            if (_connectionClosed || stream.ReadAborted || stream.ReadCompleted)
                return;

            stream.EnqueueReceived(data);
            _bytesReceived += data.Length;
            _packetsReceived += GetPacketCount(data.Length);
            Signal(stream.ReadWaiter);
            stream.ReadWaiter = null;
        }
    }

    /// <summary><c>session.stop_stream(stream_id, code)</c>: STOP_SENDING, with an HTTP/3 error code.</summary>
    public void StopStream(long streamId, long code)
    {
        lock (_lock)
        {
            var stream = GetStream(streamId);
            if (!stream.CanWrite)
                throw new InvalidOperationException("The stream " + streamId.ToString(CultureInfo.InvariantCulture) + " is not written by the browser.");

            // The STOP_SENDING of a stream whose data is received has no effect
            if (stream.StopSendingReceived || stream.WriteAborted || stream.FinDelivered || _connectionClosed)
                return;

            var errorCode = WptWebTransportServer.ToWebTransportErrorCode(code);
            stream.StopSendingReceived = true;
            stream.StopSendingErrorCode = errorCode;
            var pendingWrite = stream.PendingWrite;
            stream.PendingWrite = null;
            ReleaseIfCompleted(stream);
            _server.QueueClientTask(() =>
            {
                pendingWrite?.TrySetException(new WebTransportStreamAbortedException(errorCode));
                stream.WritesClosedSource.TrySetException(new WebTransportStreamAbortedException(errorCode));
            });
        }
    }

    /// <summary><c>session.reset_stream(stream_id, code)</c>: RESET_STREAM, with an HTTP/3 error code. The data the browser did not read is lost.</summary>
    public void ResetStream(long streamId, long code)
    {
        lock (_lock)
        {
            var stream = GetStream(streamId);
            if (!stream.CanRead)
                throw new InvalidOperationException("The stream " + streamId.ToString(CultureInfo.InvariantCulture) + " cannot be written by the server.");

            if (stream.ServerReset || stream.ReadCompleted || stream.ReadAborted || _connectionClosed)
                return;

            stream.ServerReset = true;
            stream.ServerResetErrorCode = WptWebTransportServer.ToWebTransportErrorCode(code);
            stream.ClearReceived();
            Signal(stream.ReadWaiter);
            stream.ReadWaiter = null;
        }
    }

    /// <summary><c>session.send_datagram(data)</c></summary>
    public void SendDatagram(byte[] data)
    {
        lock (_lock)
        {
            if (IsTerminated)
                return;

            _incomingDatagrams.Enqueue(data);
            _bytesReceived += data.Length;
            _packetsReceived++;
            Signal(_datagramWaiter);
            _datagramWaiter = null;
        }
    }

    /// <summary>
    /// <c>session.close(close_info)</c>: the server ends the CONNECT stream, after a CLOSE_WEBTRANSPORT_SESSION capsule
    /// when <paramref name="closeInfo"/> is not null. The browser sees the code 0 and an empty reason when there is no capsule.
    /// </summary>
    public void Close(WebTransportConnectionCloseInfo? closeInfo)
    {
        lock (_lock)
        {
            if (IsTerminated)
                return;

            _serverClosed = true;
            Terminate(closeInfo ?? new WebTransportConnectionCloseInfo(0, ""), exception: null);
        }
    }

    /// <summary><c>session.initiate_draining()</c>: the server sends a DRAIN_WEBTRANSPORT_SESSION capsule.</summary>
    public void InitiateDraining()
    {
        lock (_lock)
        {
            if (!IsTerminated)
            {
                _server.QueueClientTask(() => DrainingSource.TrySetResult());
            }
        }
    }

    /// <summary>
    /// <c>session._http._quic.close()</c>: the server closes the QUIC connection. The session ends abruptly for the browser,
    /// and the server calls <c>session_closed(None, abruptly=True)</c>.
    /// </summary>
    public void CloseConnection()
    {
        lock (_lock)
        {
            if (_connectionClosed || _clientClosed)
                return;

            var wasTerminated = IsTerminated;
            _serverClosed = true;
            _connectionClosed = true;
            if (!wasTerminated)
            {
                Terminate(closeInfo: null, new IOException("The server closed the QUIC connection."));
            }
        }

        _server.Post(() => CallSessionClosed(closeInfo: null, abruptly: true));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The events of the server
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Answers the CONNECT request (<c>_handshake_webtransport</c>): <c>connect_received</c>, then <c>session_established</c>
    /// when the status of the response is <c>200</c>. Returns the browser side of the session, or null when the status of
    /// the response is not 2xx.
    /// </summary>
    public WptWebTransportConnection? Handshake()
    {
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("server", "webtransport-h3-server"),
            new("sec-webtransport-http3-draft", "draft02"),
        };
        Invoke("connect_received", () => _handler.ConnectReceived?.Invoke(RequestHeaders, responseHeaders));

        var statusIndex = responseHeaders.FindIndex(header => header.Key == ":status");
        var status = statusIndex >= 0 ? responseHeaders[statusIndex].Value : "200";
        if (!int.TryParse(status, NumberStyles.None, CultureInfo.InvariantCulture, out var statusCode) || statusCode is < 200 or > 299)
            return null;

        // The negotiated protocol is a string item (https://www.rfc-editor.org/rfc/rfc9651#name-items) offered by the page:
        // the browser exposes it as WebTransport.protocol, not as a response header
        var protocol = StructuredFields.GetItem(responseHeaders, "wt-protocol") is { Value: string value } && _request.Protocols.Contains(value, StringComparer.Ordinal) ? value : "";
        var headers = responseHeaders.Where(header => header.Key is not [':', ..] && !header.Key.Equals("wt-protocol", StringComparison.OrdinalIgnoreCase)).ToArray();
        lock (_lock)
        {
            _packetsSent++;
            _packetsReceived++;
        }

        var connection = new WptWebTransportConnection(this, protocol, headers);
        if (status == "200")
        {
            Invoke("session_established", () => _handler.SessionEstablished?.Invoke(this));
        }

        return connection;
    }

    private void CallSessionClosed(WebTransportConnectionCloseInfo? closeInfo, bool abruptly)
    {
        if (_sessionClosedCalled)
            return;

        _sessionClosedCalled = true;
        Invoke("session_closed", () => _handler.SessionClosed?.Invoke(this, closeInfo, abruptly));
    }

    private void Invoke(string callback, Action action)
    {
        // The server logs the exceptions of the handlers and continues (WebTransportEventHandler._run_callback)
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _server.Log("WebTransport handler error: " + _url.Pathname + " (" + callback + "): " + ex);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The operations of the browser
    // ----------------------------------------------------------------------------------------------------------------

    public async ValueTask<WebTransportConnectionStream> OpenStreamAsync(bool bidirectional, bool waitUntilAvailable, CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource waiter;
            lock (_lock)
            {
                ThrowIfTerminated();
                var openStreams = bidirectional ? _openClientBidirectionalStreams : _openClientUnidirectionalStreams;
                if (openStreams < ClientStreamLimit)
                {
                    long id;
                    if (bidirectional)
                    {
                        _openClientBidirectionalStreams++;
                        id = _nextClientBidirectionalId;
                        _nextClientBidirectionalId += 4;
                    }
                    else
                    {
                        _openClientUnidirectionalStreams++;
                        id = _nextClientUnidirectionalId;
                        _nextClientUnidirectionalId += 4;
                    }

                    var stream = new WptWebTransportStream(this, id);
                    _streams.Add(id, stream);
                    return stream;
                }

                if (!waitUntilAvailable)
                    throw new InvalidOperationException("The server does not allow the browser to open more streams.");

                waiter = new TaskCompletionSource();
                _openWaiters.Add(waiter);
            }

            await WaitAsync(waiter, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<WebTransportConnectionStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource waiter;
            lock (_lock)
            {
                ThrowIfTerminated();
                if (_incomingStreams.TryDequeue(out var stream))
                    return stream;

                waiter = _acceptWaiter = new TaskCompletionSource();
            }

            await WaitAsync(waiter, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask SendDatagramAsync(ReadOnlyMemory<byte> datagram)
    {
        lock (_lock)
        {
            if (IsTerminated)
                return ValueTask.FromException(CreateTerminatedException());

            // A datagram larger than a packet is not sent
            if (datagram.Length > MaxDatagramSize)
                return ValueTask.CompletedTask;

            _bytesSent += datagram.Length;
            _packetsSent++;
        }

        var data = datagram.ToArray();
        _server.Post(() =>
        {
            if (!IsConnectionClosed())
            {
                Invoke("datagram_received", () => _handler.DatagramReceived?.Invoke(this, data));
            }
        });

        return ValueTask.CompletedTask;
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveDatagramAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource waiter;
            lock (_lock)
            {
                ThrowIfTerminated();
                if (_incomingDatagrams.TryDequeue(out var datagram))
                    return datagram;

                waiter = _datagramWaiter = new TaskCompletionSource();
            }

            await WaitAsync(waiter, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The browser closes the session: a CLOSE_WEBTRANSPORT_SESSION capsule, then the end of the CONNECT stream.</summary>
    public void CloseFromClient(WebTransportConnectionCloseInfo closeInfo)
    {
        lock (_lock)
        {
            if (_clientClosed || _connectionClosed)
                return;

            var wasTerminated = IsTerminated;
            _clientClosed = true;
            if (!wasTerminated)
            {
                Terminate(closeInfo, exception: null);
            }
        }

        // The capsule is received after the data the browser sent before
        _server.Post(() => CallSessionClosed(closeInfo, abruptly: false));
    }

    /// <summary>The browser disposes the session: unless it was closed by the browser, the server sees the end of the QUIC connection.</summary>
    public void DisposeFromClient()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            var wasTerminated = IsTerminated;
            _clientClosed = true;
            if (!wasTerminated)
            {
                Terminate(closeInfo: null, new IOException("The session was closed abruptly."));
            }
        }

        _server.Post(() => CallSessionClosed(closeInfo: null, abruptly: true));
    }

    public WebTransportConnectionStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new WebTransportConnectionStatistics
            {
                BytesSent = _bytesSent,
                BytesSentOverhead = _packetsSent * PacketOverhead,
                BytesAcknowledged = _bytesSent,
                PacketsSent = _packetsSent,
                BytesReceived = _bytesReceived,
                PacketsReceived = _packetsReceived,
                SmoothedRtt = RoundTripTime,
                RttVariation = RoundTripTime / 2,
                MinRtt = RoundTripTime,
            };
        }
    }

    public async ValueTask<int> ReadAsync(WptWebTransportStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!stream.CanRead)
            throw new InvalidOperationException("The stream cannot be read.");

        while (true)
        {
            TaskCompletionSource waiter;
            lock (_lock)
            {
                if (stream.ReadAborted)
                    throw new InvalidOperationException("The reading side of the stream was aborted.");

                if (stream.ServerReset)
                {
                    stream.ReadCompleted = true;
                    ReleaseIfCompleted(stream);
                    throw new WebTransportStreamAbortedException(stream.ServerResetErrorCode);
                }

                if (stream.HasReceivedData && !buffer.IsEmpty)
                    return stream.DequeueReceived(buffer.Span);

                if (stream.ServerFin)
                {
                    stream.ReadCompleted = true;
                    ReleaseIfCompleted(stream);
                    return 0;
                }

                ThrowIfTerminated();
                waiter = new TaskCompletionSource();
                stream.ReadWaiter = waiter;
            }

            await WaitAsync(waiter, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask WriteAsync(WptWebTransportStream stream, ReadOnlyMemory<byte> buffer, bool endOfStream, CancellationToken cancellationToken)
    {
        if (!stream.CanWrite)
            throw new InvalidOperationException("The stream cannot be written.");

        TaskCompletionSource completion;
        lock (_lock)
        {
            if (stream.StopSendingReceived)
                throw new WebTransportStreamAbortedException(stream.StopSendingErrorCode);

            ThrowIfTerminated();
            if (stream.WriteAborted || stream.FinWritten)
                throw new InvalidOperationException("The writing side of the stream is closed.");

            stream.FinWritten = endOfStream;
            completion = new TaskCompletionSource();
            stream.PendingWrite = completion;
            _bytesSent += buffer.Length;
            _packetsSent += GetPacketCount(buffer.Length);
        }

        var data = buffer.ToArray();
        _server.Post(() => DeliverStreamData(stream, data, endOfStream, completion));
        await WaitAsync(completion, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Delivers the data of a write of the browser to the handler (<c>stream_data_received</c>).</summary>
    private void DeliverStreamData(WptWebTransportStream stream, byte[] data, bool endOfStream, TaskCompletionSource completion)
    {
        lock (_lock)
        {
            // The browser aborted the stream, or the handler stopped it, before the data is received
            if (stream.PendingWrite != completion)
                return;

            stream.PendingWrite = null;
            stream.FinDelivered = endOfStream;
            ReleaseIfCompleted(stream);
        }

        // The server acknowledges the data when it receives it, before the handler processes it: the write of the browser
        // completes before the effects of the handler (e.g. the STOP_SENDING of the stream). The write fails when the
        // browser aborts the stream before it sees the acknowledgment (e.g. the end of a stream aborted while it is sent).
        _server.QueueClientTask(() =>
        {
            bool aborted;
            lock (_lock)
            {
                aborted = stream.WriteAborted;
            }

            if (aborted)
            {
                completion.TrySetException(new IOException("The writing side of the stream was aborted."));
            }
            else
            {
                completion.TrySetResult();
                if (endOfStream)
                {
                    stream.WritesClosedSource.TrySetResult();
                }
            }
        });

        if (data.Length > 0 || endOfStream)
        {
            Invoke("stream_data_received", () => _handler.StreamDataReceived?.Invoke(this, stream.Id, data, endOfStream));
        }
    }

    /// <summary>The browser stops reading (STOP_SENDING). The server has no callback for it (aioquic drops the data the handler sends next).</summary>
    public void AbortRead(WptWebTransportStream stream)
    {
        lock (_lock)
        {
            if (!stream.CanRead || stream.ReadAborted || stream.ReadCompleted)
                return;

            stream.ReadAborted = true;
            stream.ClearReceived();
            Signal(stream.ReadWaiter);
            stream.ReadWaiter = null;
            ReleaseIfCompleted(stream);
        }
    }

    /// <summary>The browser aborts the side it writes (RESET_STREAM): the handler is called with the HTTP/3 error code (<c>stream_reset</c>).</summary>
    public void AbortWrite(WptWebTransportStream stream, uint errorCode)
    {
        lock (_lock)
        {
            if (!stream.CanWrite || stream.WriteAborted || stream.StopSendingReceived || stream.WritesClosedSource.Task.IsCompleted)
                return;

            stream.WriteAborted = true;
            var pendingWrite = stream.PendingWrite;
            stream.PendingWrite = null;
            ReleaseIfCompleted(stream);
            _server.QueueClientTask(() =>
            {
                pendingWrite?.TrySetException(new IOException("The writing side of the stream was aborted."));
                stream.WritesClosedSource.TrySetResult();
            });

            if (IsTerminated)
                return;
        }

        var code = WptWebTransportServer.ToHttpErrorCode(errorCode);
        _server.Post(() =>
        {
            if (!IsConnectionClosed())
            {
                Invoke("stream_reset", () => _handler.StreamReset?.Invoke(this, stream.Id, code));
            }
        });
    }

    public void DisposeStream(WptWebTransportStream stream)
    {
        lock (_lock)
        {
            stream.Disposed = true;
            stream.ClearReceived();
            ReleaseIfCompleted(stream);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------------------------

    private bool IsTerminated => _clientClosed || _serverClosed;

    private bool IsConnectionClosed()
    {
        lock (_lock)
        {
            return _connectionClosed;
        }
    }

    private long CreateServerStream(bool bidirectional)
    {
        lock (_lock)
        {
            long id;
            if (bidirectional)
            {
                id = _nextServerBidirectionalId;
                _nextServerBidirectionalId += 4;
            }
            else
            {
                id = _nextServerUnidirectionalId;
                _nextServerUnidirectionalId += 4;
            }

            var stream = new WptWebTransportStream(this, id);
            _streams.Add(id, stream);
            if (!IsTerminated)
            {
                _incomingStreams.Enqueue(stream);
                Signal(_acceptWaiter);
                _acceptWaiter = null;
            }

            return id;
        }
    }

    private WptWebTransportStream GetStream(long streamId) => _streams.TryGetValue(streamId, out var stream) ? stream : throw new InvalidOperationException("Unknown stream " + streamId.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Ends the session for the browser (must be called under the lock): <see cref="ClosedSource"/> completes, then the
    /// operations that wait are woken up (they throw). When the server ends the session, the writes that the server has
    /// not received fail.
    /// </summary>
    private void Terminate(WebTransportConnectionCloseInfo? closeInfo, Exception? exception)
    {
        var waiters = new List<TaskCompletionSource>();
        var pendingWrites = new List<TaskCompletionSource>();
        waiters.AddRange(_openWaiters);
        _openWaiters.Clear();
        if (_acceptWaiter is not null)
        {
            waiters.Add(_acceptWaiter);
            _acceptWaiter = null;
        }

        if (_datagramWaiter is not null)
        {
            waiters.Add(_datagramWaiter);
            _datagramWaiter = null;
        }

        foreach (var stream in _streams.Values)
        {
            if (stream.ReadWaiter is not null)
            {
                waiters.Add(stream.ReadWaiter);
                stream.ReadWaiter = null;
            }

            if (_serverClosed && stream.PendingWrite is not null)
            {
                pendingWrites.Add(stream.PendingWrite);
                stream.PendingWrite = null;
            }
        }

        var writeException = CreateTerminatedException();
        _server.QueueClientTask(() =>
        {
            if (exception is not null)
            {
                ClosedSource.TrySetException(exception);
            }
            else
            {
                ClosedSource.TrySetResult(closeInfo!);
            }

            foreach (var waiter in waiters)
            {
                waiter.TrySetResult();
            }

            foreach (var pendingWrite in pendingWrites)
            {
                pendingWrite.TrySetException(writeException);
            }
        });
    }

    private void ThrowIfTerminated()
    {
        if (IsTerminated)
            throw CreateTerminatedException();
    }

    private IOException CreateTerminatedException()
    {
        if (_connectionClosed)
            return new IOException("The server closed the QUIC connection.");

        return new IOException(_serverClosed && !_clientClosed ? "The server closed the session." : "The session is closed.");
    }

    /// <summary>A stream of the browser that is closed no longer counts in the streams it can open (must be called under the lock).</summary>
    private void ReleaseIfCompleted(WptWebTransportStream stream)
    {
        if (!stream.IsClientInitiated || stream.Released || !stream.IsCompleted)
            return;

        stream.Released = true;
        if (stream.IsBidirectional)
        {
            _openClientBidirectionalStreams--;
        }
        else
        {
            _openClientUnidirectionalStreams--;
        }

        foreach (var waiter in _openWaiters)
        {
            Signal(waiter);
        }

        _openWaiters.Clear();
    }

    /// <summary>Wakes up an operation of the browser that waits: it checks the state again in a task of the event loop.</summary>
    private void Signal(TaskCompletionSource? waiter)
    {
        if (waiter is not null)
        {
            _server.QueueClientTask(() => waiter.TrySetResult());
        }
    }

    /// <summary>Waits for a signal (<see cref="Signal"/>). The continuation runs synchronously in the task that signals.</summary>
    private static async Task WaitAsync(TaskCompletionSource waiter, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            await waiter.Task.ConfigureAwait(false);
            return;
        }

        using var registration = cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
        await waiter.Task.ConfigureAwait(false);
    }

    public static void ObserveException(Task task) => _ = task.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static long GetPacketCount(int length) => Math.Max(1, (length + MaxDatagramSize - 1) / MaxDatagramSize);

    private static List<KeyValuePair<string, string>> CreateRequestHeaders(Url url, WebTransportConnectionRequest request)
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new(":method", "CONNECT"),
            new(":scheme", "https"),
            new(":authority", url.Host),
            new(":path", url.Pathname + url.Search),
            new(":protocol", "webtransport"),
        };
        headers.AddRange(request.Headers);
        if (request.Protocols.Count > 0)
        {
            headers.Add(new("wt-available-protocols", StructuredFields.SerializeList([.. request.Protocols.Select(protocol => new StructuredFieldMember(new StructuredFieldItem(protocol, []), InnerList: null))])));
        }

        return headers;
    }

    /// <summary><c>urlsplit(path).query</c>: the query of a path, without the fragment.</summary>
    public static string GetQuery(string path)
    {
        var index = path.IndexOf('?', StringComparison.Ordinal);
        if (index < 0)
            return "";

        var query = path[(index + 1)..];
        var fragment = query.IndexOf('#', StringComparison.Ordinal);
        return fragment < 0 ? query : query[..fragment];
    }

    /// <summary>
    /// <c>dict(parse_qsl(query))</c>: the pairs separated by <c>&amp;</c>, without the pairs that have no <c>=</c> or an empty
    /// value; <c>+</c> is a space, and the percent-encoded bytes are decoded as UTF-8.
    /// </summary>
    public static OrderedDictionary<string, string> ParseQuery(string query)
    {
        var result = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&'))
        {
            var index = pair.IndexOf('=', StringComparison.Ordinal);
            if (index < 0 || index == pair.Length - 1)
                continue;

            result[Decode(pair[..index])] = Decode(pair[(index + 1)..]);
        }

        return result;

        static string Decode(string value) => Encoding.UTF8.GetString(PercentEncoding.PercentDecode(value.Replace('+', ' ')));
    }
}
