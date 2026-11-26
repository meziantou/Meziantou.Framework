using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The network side of a <c>WebTransport</c> object of the scripts (Streams.js): the session the
/// <see cref="DocumentLoader"/> establishes (https://w3c.github.io/webtransport/#initialize-webtransport-over-http). The
/// results are given by tasks of the event loop of the global object. A request that the page waits for (connecting,
/// opening a stream, writing, sending a datagram, closing) is an operation of the event loop
/// (<see cref="DomEventLoop.BeginOperation"/>); waiting for the server (a read, an incoming stream, a datagram) is not, as for
/// a WebSocket that waits for a message.
/// </summary>
#pragma warning disable CA1001 // The CancellationTokenSource has no timer: it is canceled when the session ends, and needs no disposal
internal sealed class WebTransportHostSession
{
    // The sessions of a global object, closed when its document is unloaded or its worker ends
    private static readonly ConditionalWeakTable<GlobalScope, List<WebTransportHostSession>> ActiveSessions = [];

    private readonly GlobalScope _global;
    private readonly DomEventLoop _eventLoop;
    private readonly IWebTransportSessionEvents _events;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Lock _streamsLock = new();
    private readonly List<WebTransportHostStream> _streams = [];
    private WebTransportConnection? _connection;
    private SessionState _state = SessionState.Connecting;

    private WebTransportHostSession(GlobalScope global, IWebTransportSessionEvents events)
    {
        _global = global;
        _eventLoop = global.EventLoop;
        _events = events;
    }

    private enum SessionState
    {
        Connecting,
        Connected,
        Ended,
    }

    /// <summary>Gets the maximum size of a datagram, or 0 before the session is established.</summary>
    public int MaxDatagramSize => _connection?.MaxDatagramSize ?? 0;

    /// <summary>
    /// Starts to establish a session (https://w3c.github.io/webtransport/#initialize-webtransport-over-http): the request is
    /// blocked by the content security policies (connect-src) or as mixed content, else it is given to the loader.
    /// <paramref name="usesCertificateHashes"/> is true when the page gave a non-empty <c>serverCertificateHashes</c>, even
    /// when none of its algorithms is supported (<see cref="WebTransportConnectionRequest.ServerCertificateHashes"/> has the
    /// SHA-256 hashes only).
    /// </summary>
    public static WebTransportHostSession Connect(GlobalScope global, WebTransportConnectionRequest request, bool usesCertificateHashes, IWebTransportSessionEvents events)
    {
        var session = new WebTransportHostSession(global, events);
        var client = global.EnvironmentDocument;
        var fetchRequest = new FetchRequest(UrlParser.Parse(request.Url, baseUrl: null)!)
        {
            Client = client,
            Origin = client.SerializeOrigin(),
            Destination = RequestDestination.Empty,
            RedirectMode = FetchRedirectMode.Error,
            UsesWebTransportCertificateHashes = usesCertificateHashes,
        };

        // https://w3c.github.io/webtransport/#verify-a-certificate-hash: no certificate matches a list of hashes whose
        // algorithms are all unsupported, so the session fails; the PKI is not used instead
        if (Fetching.IsBlocked(fetchRequest) || (usesCertificateHashes && request.ServerCertificateHashes.Count == 0) || client.EnvironmentOptions?.DocumentLoader is not { } loader)
        {
            session._state = SessionState.Ended;
            session.QueueTask(() => events.Failed("The WebTransport session is blocked"));
            return session;
        }

        // The origin of the client is the first header of the CONNECT request
        var connectionRequest = new WebTransportConnectionRequest
        {
            Url = request.Url,
            Headers = [new("origin", client.SerializeOrigin()), .. request.Headers],
            Protocols = request.Protocols,
            ServerCertificateHashes = request.ServerCertificateHashes,
            CongestionControl = request.CongestionControl,
            AllowPooling = request.AllowPooling,
            RequireUnreliable = request.RequireUnreliable,
            AnticipatedConcurrentIncomingBidirectionalStreams = request.AnticipatedConcurrentIncomingBidirectionalStreams,
            AnticipatedConcurrentIncomingUnidirectionalStreams = request.AnticipatedConcurrentIncomingUnidirectionalStreams,
        };

        ActiveSessions.GetOrCreateValue(global).Add(session);
        global.EventLoop.BeginOperation();
        _ = session.ConnectAsync(loader, connectionRequest);
        return session;
    }

    /// <summary>
    /// The context cleanup steps (https://w3c.github.io/webtransport/#webtransport-context-cleanup-steps): the sessions of
    /// an unloaded document or of an ended worker are closed. When <paramref name="cleanUpScriptObjects"/> is true (a
    /// document, whose objects other windows of its origin can still use), the <c>WebTransport</c> objects are cleaned up at
    /// once (their promises are settled and their streams errored), as no task of the global object runs afterwards. The
    /// script of an ended worker cannot run anymore, and no other global object sees its objects.
    /// </summary>
    public static void MakeDisappear(GlobalScope global, bool cleanUpScriptObjects)
    {
        if (!ActiveSessions.TryGetValue(global, out var sessions))
            return;

        foreach (var session in sessions.ToArray())
        {
            session.Close(0, "");
            if (cleanUpScriptObjects)
            {
                session._events.Failed("The WebTransport session was closed: its document was unloaded");
            }
        }
    }

    /// <summary>
    /// Terminates the sessions of a document that is unloaded, which may enter the back/forward cache: the sessions end
    /// abruptly at once (their closed promises are rejected before the document becomes inactive).
    /// </summary>
    public static void Terminate(GlobalScope global)
    {
        if (!ActiveSessions.TryGetValue(global, out var sessions))
            return;

        foreach (var session in sessions.ToArray())
        {
            // The connection is disposed before the streams are released: the session resets its streams
            // (WT_SESSION_GONE), while a released stream would end its writes cleanly
            if (session._connection is { } connection)
            {
                _ = DisposeConnectionAsync(connection);
            }

            session.End();

            session._events.Failed("The WebTransport session was terminated: its document was unloaded");
        }
    }

    private async Task ConnectAsync(DocumentLoader loader, WebTransportConnectionRequest request)
    {
        WebTransportConnection? connection = null;
        try
        {
            connection = await loader.ConnectWebTransportAsync(request, _cancellation.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failure of the loader is a failure of the session
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        QueueTask(() =>
        {
            if (_state != SessionState.Connecting)
            {
                if (connection is not null)
                {
                    _ = DisposeConnectionAsync(connection);
                }

                return;
            }

            if (connection is null)
            {
                End();
                _events.Failed("The WebTransport session could not be established");
                return;
            }

            _connection = connection;
            _state = SessionState.Connected;
            _events.Ready(connection.Protocol, connection.ResponseHeaders);
            _ = AcceptStreamsAsync(connection);
            _ = ReceiveDatagramsAsync(connection);
            _ = WatchClosedAsync(connection);
            _ = WatchDrainingAsync(connection);
        });
        _eventLoop.EndOperation();
    }

    private async Task AcceptStreamsAsync(WebTransportConnection connection)
    {
        try
        {
            while (true)
            {
                var stream = await connection.AcceptStreamAsync(_cancellation.Token).ConfigureAwait(false);
                QueueTask(() =>
                {
                    if (_state != SessionState.Connected)
                    {
                        _ = stream.DisposeAsync().AsTask();
                        return;
                    }

                    _events.IncomingStream(CreateStream(stream));
                });
            }
        }
#pragma warning disable CA1031 // The loop ends with the session
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private async Task ReceiveDatagramsAsync(WebTransportConnection connection)
    {
        try
        {
            while (true)
            {
                var datagram = await connection.ReceiveDatagramAsync(_cancellation.Token).ConfigureAwait(false);
                QueueTask(() =>
                {
                    if (_state == SessionState.Connected)
                    {
                        _events.Datagram(datagram);
                    }
                });
            }
        }
#pragma warning disable CA1031 // The loop ends with the session
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    // https://w3c.github.io/webtransport/#session-termination: the server ended the session, cleanly or not
    private async Task WatchClosedAsync(WebTransportConnection connection)
    {
        WebTransportConnectionCloseInfo? closeInfo = null;
        try
        {
            closeInfo = await connection.Closed.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An abrupt end of the session
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        QueueTask(() =>
        {
            if (_state != SessionState.Connected)
                return;

            End();
            _ = DisposeConnectionAsync(connection);
            if (closeInfo is not null)
            {
                _events.Closed(closeInfo.CloseCode, closeInfo.Reason);
            }
            else
            {
                _events.Failed("The WebTransport session ended abruptly");
            }
        });
    }

    private async Task WatchDrainingAsync(WebTransportConnection connection)
    {
        try
        {
            await connection.Draining.WaitAsync(_cancellation.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The session ended before draining
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        QueueTask(() =>
        {
            if (_state == SessionState.Connected)
            {
                _events.Draining();
            }
        });
    }

    /// <summary>Closes the session with a code and a reason (https://w3c.github.io/webtransport/#dom-webtransport-close, step 8).</summary>
    public void Close(uint closeCode, string reason)
    {
        if (_state == SessionState.Ended)
            return;

        // The close of the session starts before the streams are released: it resets them (WT_SESSION_GONE), while a
        // released stream would end its writes cleanly
        if (_connection is { } connection)
        {
            _eventLoop.BeginOperation();
            _ = CloseAsync(connection, closeCode, reason);
        }

        End();
    }

    private async Task CloseAsync(WebTransportConnection connection, uint closeCode, string reason)
    {
        try
        {
            await connection.CloseAsync(closeCode, reason).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The session is closed anyway
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        await DisposeConnectionAsync(connection).ConfigureAwait(false);
        QueueTask(static () => { });
        _eventLoop.EndOperation();
    }

    /// <summary>Abandons a session that is being established (close() while connecting).</summary>
    public void Abort()
    {
        if (_state == SessionState.Connecting)
        {
            End();
        }
        else
        {
            Close(0, "");
        }
    }

    /// <summary>Opens a stream; the callback gets the stream, or null and "quota" or "closed".</summary>
    public void OpenStream(bool bidirectional, bool waitUntilAvailable, Action<WebTransportHostStream?, string?> callback)
    {
        if (_state != SessionState.Connected || _connection is not { } connection)
        {
            QueueTask(() => callback(null, "closed"));
            return;
        }

        _eventLoop.BeginOperation();
        _ = OpenStreamAsync(connection, bidirectional, waitUntilAvailable, callback);
    }

    private async Task OpenStreamAsync(WebTransportConnection connection, bool bidirectional, bool waitUntilAvailable, Action<WebTransportHostStream?, string?> callback)
    {
        WebTransportConnectionStream? stream = null;
        string? error = null;
        try
        {
            stream = await connection.OpenStreamAsync(bidirectional, waitUntilAvailable, _cancellation.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            error = "quota";
        }
#pragma warning disable CA1031 // The session is closed
        catch (Exception)
#pragma warning restore CA1031
        {
            error = "closed";
        }

        QueueTask(() =>
        {
            if (stream is null || _state != SessionState.Connected)
            {
                if (stream is not null)
                {
                    _ = stream.DisposeAsync().AsTask();
                }

                callback(null, error ?? "closed");
                return;
            }

            callback(CreateStream(stream), null);
        });
        _eventLoop.EndOperation();
    }

    /// <summary>Sends a datagram; the callback gets whether it was sent.</summary>
    public void SendDatagram(byte[] datagram, Action<bool> callback)
    {
        if (_state != SessionState.Connected || _connection is not { } connection)
        {
            QueueTask(() => callback(false));
            return;
        }

        _eventLoop.BeginOperation();
        _ = SendDatagramAsync(connection, datagram, callback);
    }

    private async Task SendDatagramAsync(WebTransportConnection connection, byte[] datagram, Action<bool> callback)
    {
        var sent = true;
        try
        {
            await connection.SendDatagramAsync(datagram, _cancellation.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A datagram that cannot be sent is lost
        catch (Exception)
#pragma warning restore CA1031
        {
            sent = false;
        }

        QueueTask(() => callback(sent));
        _eventLoop.EndOperation();
    }

    /// <summary>Gets the statistics of the connection; the callback gets null when the session is not established.</summary>
    public void GetStatistics(Action<WebTransportConnectionStatistics?> callback)
    {
        if (_connection is not { } connection || _state != SessionState.Connected)
        {
            QueueTask(() => callback(null));
            return;
        }

        _eventLoop.BeginOperation();
        _ = GetStatisticsAsync(connection, callback);
    }

    private async Task GetStatisticsAsync(WebTransportConnection connection, Action<WebTransportConnectionStatistics?> callback)
    {
        WebTransportConnectionStatistics? statistics = null;
        try
        {
            statistics = await connection.GetStatisticsAsync(_cancellation.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The statistics are not available
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        QueueTask(() => callback(statistics));
        _eventLoop.EndOperation();
    }

    internal void QueueTask(Action task)
    {
        if (!_eventLoop.IsDisposed)
        {
            _global.QueueGlobalTask(task);
        }
    }

    internal void BeginOperation() => _eventLoop.BeginOperation();

    internal void EndOperation() => _eventLoop.EndOperation();

    internal void RemoveStream(WebTransportHostStream stream)
    {
        lock (_streamsLock)
        {
            _streams.Remove(stream);
        }
    }

    private WebTransportHostStream CreateStream(WebTransportConnectionStream stream)
    {
        var hostStream = new WebTransportHostStream(this, stream);
        lock (_streamsLock)
        {
            _streams.Add(hostStream);
        }

        return hostStream;
    }

    // The session ended: its waits are canceled, it is no longer active, and its streams are released
    private void End()
    {
        _state = SessionState.Ended;
        _cancellation.Cancel();
        if (ActiveSessions.TryGetValue(_global, out var sessions))
        {
            sessions.Remove(this);
        }

        WebTransportHostStream[] streams;
        lock (_streamsLock)
        {
            streams = [.. _streams];
            _streams.Clear();
        }

        foreach (var stream in streams)
        {
            stream.Release();
        }
    }

    private static async Task DisposeConnectionAsync(WebTransportConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The connection is released anyway
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
#pragma warning restore CA1001
