using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The WebTransport server of wptserve (https://github.com/web-platform-tests/wpt/blob/master/tools/webtransport/h3/webtransport_h3_server.py,
/// built on aioquic) emulated by the runner: the sessions of <see cref="DocumentLoader.ConnectWebTransportAsync"/> are
/// in-memory, and their server side runs the handlers of <c>webtransport/handlers/*.py</c> ported to .NET (see
/// <see cref="WptWebTransportHandlers"/>). Each session has its own QUIC connection, as a session that does not allow pooling.
/// </summary>
/// <remarks>
/// <para>
/// The callbacks of the handlers run one at a time on the thread pool, in the order of the events, as aioquic runs them
/// on the thread of its event loop: the runner does not advance the virtual clock while a callback is queued or running
/// (<see cref="IsBusy"/>). The browser side is completed by tasks of the event loop of the test page
/// (<see cref="QueueClientTask"/>): the continuation of the library (which queues a task on the event loop of the
/// <c>WebTransport</c> object) runs synchronously, so the runner never sees an idle event loop between the network
/// event and the task.
/// </para>
/// <para>
/// The handlers use the HTTP/3 error codes, and the browser the WebTransport application error codes
/// (https://www.ietf.org/archive/id/draft-ietf-webtrans-http3-14.html#section-4.4): see <see cref="ToHttpErrorCode"/>.
/// </para>
/// </remarks>
internal sealed class WptWebTransportServer
{
    /// <summary>The first HTTP/3 error code of the WebTransport application error codes (<c>WEBTRANSPORT_APPLICATION_ERROR</c>).</summary>
    public const long FirstHttpErrorCode = 0x52e4a40fa8db;

    /// <summary>The last HTTP/3 error code of the WebTransport application error codes.</summary>
    public const long LastHttpErrorCode = 0x52e5ac983162;

    private readonly Action<Action> _queueTask;
    private readonly Action<string> _log;
    private readonly Lock _lock = new();
    private readonly Queue<Action> _callbacks = new();
    private bool _running;

    /// <summary>Creates the server.</summary>
    /// <param name="queueTask">Queues a task on the event loop of the test page (it completes the operations of the browser).</param>
    /// <param name="stash">The stash of the WPT server.</param>
    /// <param name="log">Writes a line to the log of the test.</param>
    public WptWebTransportServer(Action<Action> queueTask, WptStash stash, Action<string> log)
    {
        _queueTask = queueTask;
        _log = log;
        Stash = stash;
    }

    /// <summary>Gets the stash of the WPT server.</summary>
    public WptStash Stash { get; }

    /// <summary>Gets a value indicating whether a callback of a handler is queued or running: the runner must not advance the virtual clock.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return _running || _callbacks.Count > 0;
            }
        }
    }

    /// <summary>
    /// Establishes a session, as wptserve does (<c>_handshake_webtransport</c>): the handler of the path of the URL may change
    /// the response (<c>connect_received</c>); a response whose status is not 2xx rejects the session. Returns null when the
    /// session cannot be established: the URL is not served by the server, the handler does not exist, or the page gave
    /// hashes of certificates (the certificate of wptserve is not valid for at most 14 days, so it never matches them).
    /// </summary>
    public ValueTask<WebTransportConnection?> ConnectAsync(WebTransportConnectionRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<WebTransportConnection?>(cancellationToken);

        var url = new Url(request.Url);
        if (!WptServer.IsWebTransportHost(url) || request.ServerCertificateHashes.Count > 0)
            return ValueTask.FromResult<WebTransportConnection?>(null);

        if (WptWebTransportHandlers.Get(url.Pathname) is not { } createHandler)
        {
            Log("WebTransport handler not emulated: " + url.Pathname);
            return ValueTask.FromResult<WebTransportConnection?>(null);
        }

        // The module of the handler is executed for each session: its global variables are not shared between the sessions
        var session = new WptWebTransportSession(this, url, request, createHandler());
        var result = new TaskCompletionSource<WebTransportConnection?>();
        var registration = cancellationToken.Register(() => QueueClientTask(() => result.TrySetCanceled(cancellationToken)));
        Post(() =>
        {
            var connection = session.Handshake();
            QueueClientTask(() =>
            {
                registration.Dispose();
                if (!result.TrySetResult(connection) && connection is not null)
                {
                    // The session was canceled meanwhile: the browser closes the connection
                    session.DisposeFromClient();
                }
            });
        });

        return new ValueTask<WebTransportConnection?>(result.Task);
    }

    /// <summary>Queues a callback of a handler (or an event of the server), after the callbacks already queued.</summary>
    public void Post(Action callback)
    {
        bool start;
        lock (_lock)
        {
            _callbacks.Enqueue(callback);
            start = !_running;
            _running = true;
        }

        if (start)
        {
            ThreadPool.UnsafeQueueUserWorkItem(_ => RunCallbacks(), state: null);
        }
    }

    private void RunCallbacks()
    {
        while (true)
        {
            Action callback;
            lock (_lock)
            {
                if (!_callbacks.TryDequeue(out callback!))
                {
                    // The tasks queued by the callbacks are in the queue of the event loop before the server becomes idle
                    _running = false;
                    return;
                }
            }

            try
            {
                callback();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log("WebTransport server error: " + ex);
            }
        }
    }

    /// <summary>Queues a task on the event loop of the test page, which completes an operation of the browser.</summary>
    public void QueueClientTask(Action callback) => _queueTask(callback);

    public void Log(string line) => _log(line);

    /// <summary>Converts a WebTransport application error code to an HTTP/3 error code (draft-ietf-webtrans-http3, section 4.4).</summary>
    public static long ToHttpErrorCode(uint code) => FirstHttpErrorCode + code + (code / 0x1e);

    /// <summary>
    /// Converts an HTTP/3 error code to a WebTransport application error code, or null when it is not one: outside of the
    /// range, or a reserved code point (<c>0x1f * N + 0x21</c>).
    /// </summary>
    public static uint? ToWebTransportErrorCode(long code)
    {
        if (code is < FirstHttpErrorCode or > LastHttpErrorCode || (code - 0x21) % 0x1f == 0)
            return null;

        var shifted = code - FirstHttpErrorCode;
        return (uint)(shifted - (shifted / 0x1f));
    }
}
