using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of a <see cref="WebSocketStream"/> (https://whatpr.org/websockets/48.html#dictdef-websocketstreamoptions).</summary>
/// <seealso href="https://whatpr.org/websockets/48.html#dictdef-websocketstreamoptions"/>
[IdlInterface("WebSocketStreamOptions", "https://whatpr.org/websockets/48.html#dictdef-websocketstreamoptions")]
[Experimental(ExperimentalApis.WebSocketStream, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class WebSocketStreamOptions
{
    /// <summary>Gets the subprotocols the server can select, or null (none).</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketstreamoptions-protocols"/>
    [IdlMember("protocols", "https://whatpr.org/websockets/48.html#dom-websocketstreamoptions-protocols")]
    public IReadOnlyList<string>? Protocols { get; init; }

    /// <summary>Gets the signal that aborts the opening handshake, or null.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketstreamoptions-signal"/>
    [IdlMember("signal", "https://whatpr.org/websockets/48.html#dom-websocketstreamoptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>The status code and the reason of the closing handshake of a <see cref="WebSocketStream"/> (https://whatpr.org/websockets/48.html#dictdef-websocketcloseinfo).</summary>
/// <seealso href="https://whatpr.org/websockets/48.html#dictdef-websocketcloseinfo"/>
[IdlInterface("WebSocketCloseInfo", "https://whatpr.org/websockets/48.html#dictdef-websocketcloseinfo")]
[Experimental(ExperimentalApis.WebSocketStream, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class WebSocketCloseInfo
{
    /// <summary>Gets the status code, or null (a Close frame without status code, or <c>1000</c> when there is a reason).</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketcloseinfo-closecode"/>
    [IdlMember("closeCode", "https://whatpr.org/websockets/48.html#dom-websocketcloseinfo-closecode")]
    public ushort? CloseCode { get; init; }

    /// <summary>Gets the reason.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketcloseinfo-reason"/>
    [IdlMember("reason", "https://whatpr.org/websockets/48.html#dom-websocketcloseinfo-reason")]
    public string Reason { get; init; } = "";
}

/// <summary>
/// The connection of a <see cref="WebSocketStream"/> once it is established (https://whatpr.org/websockets/48.html#dictdef-websocketopeninfo):
/// the extensions and the subprotocol selected by the server. For the scripts, it also has the <c>readable</c> and the
/// <c>writable</c> streams of the messages.
/// </summary>
/// <seealso href="https://whatpr.org/websockets/48.html#dictdef-websocketopeninfo"/>
[IdlInterface("WebSocketOpenInfo", "https://whatpr.org/websockets/48.html#dictdef-websocketopeninfo")]
[Experimental(ExperimentalApis.WebSocketStream, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class WebSocketOpenInfo
{
    /// <summary>Gets the extensions selected by the server, or the empty string.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketopeninfo-extensions"/>
    [IdlMember("extensions", "https://whatpr.org/websockets/48.html#dom-websocketopeninfo-extensions")]
    public string Extensions { get; init; } = "";

    /// <summary>Gets the subprotocol selected by the server, or the empty string.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketopeninfo-protocol"/>
    [IdlMember("protocol", "https://whatpr.org/websockets/48.html#dom-websocketopeninfo-protocol")]
    public string Protocol { get; init; } = "";
}

/// <summary>
/// A WebSocket connection whose messages are read and written with streams (https://whatpr.org/websockets/48.html#the-websocketstream-interface):
/// the <c>opened</c> promise gives a <c>ReadableStream</c> of the received messages (a string for a text message, a
/// <c>Uint8Array</c> for a binary message) and a <c>WritableStream</c> to which the messages are written (a string is sent as
/// a text message, a <c>BufferSource</c> as a binary message), and the <c>closed</c> promise gives the status code and the
/// reason of the closing handshake.
/// </summary>
/// <remarks>
/// <para>
/// The connection is a <see cref="WebSocket"/> of the current global object (the window or the worker of the script that
/// creates the stream): it is established, closed and made to disappear (when its document is unloaded or its worker ends)
/// as a WebSocket is, and its promises are settled by tasks of the event loop of that global object.
/// </para>
/// <para>
/// The readable stream applies backpressure: a message is read from the network only when the queue of the readable stream
/// is not full (its high water mark is one message), so a server that sends faster than the page reads is slowed down by
/// the network. A write is fulfilled once the message is sent. The streams exist only for the scripts: the
/// <see cref="Opened"/> task of the .NET API gives the extensions and the subprotocol.
/// </para>
/// </remarks>
/// <seealso href="https://whatpr.org/websockets/48.html#websocketstream"/>
[IdlInterface("WebSocketStream", "https://whatpr.org/websockets/48.html#websocketstream")]
[Experimental(ExperimentalApis.WebSocketStream, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class WebSocketStream
{
    private readonly UrlRecord _url;
    private readonly WebSocket? _webSocket;
    private readonly TaskCompletionSource<WebSocketOpenInfo> _opened = new();
    private readonly TaskCompletionSource<WebSocketCloseInfo> _closed = new();
    private readonly Queue<object> _receivedMessages = new();
    private readonly List<TaskCompletionSource> _writableCloseRequests = [];
    private AbortSignal? _signal;
    private Action? _abortAlgorithm;
    private IWebSocketStreamEndpoints? _endpoints;
    private bool _wasEverConnected;
    private bool _handshakeAborted;
    private bool _isClosed;
    private Exception? _closeError;

    /// <summary>
    /// Creates a WebSocketStream and starts to establish its connection (https://whatpr.org/websockets/48.html#dom-websocketstream-websocketstream).
    /// </summary>
    /// <param name="url">
    /// The URL of the server: a <c>ws:</c> or <c>wss:</c> URL (an <c>http:</c> or <c>https:</c> URL is converted), possibly
    /// relative to the API base URL of the current global object, without fragment.
    /// </param>
    /// <param name="options">The subprotocols and the signal that aborts the opening handshake.</param>
    /// <exception cref="DomException">
    /// A <c>SyntaxError</c> when the URL is not valid, has another scheme or a fragment, or when a subprotocol is not a valid
    /// token or appears twice (ASCII case-insensitively).
    /// </exception>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketstream-websocketstream"/>
    [IdlMember("constructor", "https://whatpr.org/websockets/48.html#dom-websocketstream-websocketstream")]
    public WebSocketStream(string url, WebSocketStreamOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(url);

        // Steps 1 to 6: the URL record and the subprotocols
        _url = WebSocket.ParseUrl(url, GlobalScope.Current);
        var protocols = WebSocket.GetProtocols(options?.Protocols);

        // The promises are marked as handled, as in Chromium: a script that only reads one of them does not report the
        // rejection of the other one
        Scripting.PromiseSlots.Register(_opened.Task);
        Scripting.PromiseSlots.Register(_closed.Task);
        Scripting.PromiseSlots.MarkAsHandled(_opened.Task);
        Scripting.PromiseSlots.MarkAsHandled(_closed.Task);

        // Step 8: the signal
        if (options?.Signal is { } signal)
        {
            if (signal.Aborted)
            {
                var exception = ToException(signal.AbortReason);
                _opened.SetException(exception);
                _closed.SetException(exception);
                _handshakeAborted = true;
                _isClosed = true;
                return;
            }

            _signal = signal;
            _abortAlgorithm = OnAbort;
            signal.AddAbortAlgorithm(_abortAlgorithm);
        }

        // Step 10: the connection is established in parallel
        _webSocket = new WebSocket(_url, protocols);
        _webSocket.AddEventListener("open", _ => OnOpen());
        _webSocket.AddEventListener("message", e => OnMessage(((MessageEvent)e).Data));
        _webSocket.AddEventListener("close", e => OnClose((CloseEvent)e));

        // A WebSocket created without current global object cannot connect: there is no event loop to settle the promises
        if (_webSocket.ReadyState == WebSocket.Closed)
        {
            _isClosed = true;
            var error = new WebSocketError("The WebSocketStream has no global object", closeCode: null, reason: "");
            _closeError = error;
            _opened.SetException(error);
            _closed.SetException(error);
        }
    }

    /// <summary>Gets the URL of the connection, serialized.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketstream-url"/>
    [IdlMember("url", "https://whatpr.org/websockets/48.html#dom-websocketstream-url")]
    public string Url => _url.Serialize();

    /// <summary>
    /// Gets a task that completes when the connection is established, with the extensions and the subprotocol selected by
    /// the server. It fails with a <see cref="WebSocketError"/> when the connection cannot be established, or with the abort
    /// reason of the signal.
    /// </summary>
    /// <remarks>For the scripts, the <c>opened</c> promise also gives the <c>readable</c> and <c>writable</c> streams of the messages.</remarks>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketstream-opened"/>
    [NotInIdl("The binding of the opened attribute adds the readable and the writable streams of the scripts (Scripting/WebIdl/WebIdlRealm.WebSockets.cs)")]
    public Task<WebSocketOpenInfo> Opened => _opened.Task;

    /// <summary>
    /// Gets a task that completes when the connection is closed cleanly, with the status code and the reason of the closing
    /// handshake. It fails with a <see cref="WebSocketError"/> when the connection is not closed cleanly, or with the abort
    /// reason of the signal.
    /// </summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketstream-closed"/>
    [IdlMember("closed", "https://whatpr.org/websockets/48.html#dom-websocketstream-closed")]
    public Task<WebSocketCloseInfo> Closed => _closed.Task;

    /// <summary>
    /// Closes the connection (https://whatpr.org/websockets/48.html#dom-websocketstream-close): starts the closing handshake,
    /// or fails the connection when it is not established yet.
    /// </summary>
    /// <param name="closeInfo">
    /// The status code (<c>1000</c> or a code from <c>3000</c> to <c>4999</c>; <c>1000</c> when only a reason is given) and the
    /// reason (at most 123 bytes in UTF-8) of the Close frame, or null for a Close frame without status code.
    /// </param>
    /// <exception cref="DomException">An <c>InvalidAccessError</c> when the code is not valid, or a <c>SyntaxError</c> when the reason is too long.</exception>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketstream-close"/>
    [IdlMember("close", "https://whatpr.org/websockets/48.html#dom-websocketstream-close")]
    public void Close(WebSocketCloseInfo? closeInfo = null) => CloseWebSocket(closeInfo?.CloseCode, closeInfo?.Reason ?? "");

    /// <summary>
    /// Attaches the readable and the writable streams of the scripts, created when the <c>opened</c> promise is fulfilled:
    /// they get the messages received meanwhile, and the end of the connection if it is closed.
    /// </summary>
    internal void AttachEndpoints(IWebSocketStreamEndpoints endpoints)
    {
        _endpoints = endpoints;
        while (_receivedMessages.TryDequeue(out var message))
        {
            endpoints.Enqueue(message);
        }

        if (_isClosed && !_handshakeAborted)
        {
            if (_closeError is { } error)
            {
                endpoints.Error(error);
            }
            else
            {
                endpoints.Close(ClosedStreamError());
            }
        }
    }

    /// <summary>The pull algorithm of the readable stream (https://whatpr.org/websockets/48.html#pull-bytes): the next message is received.</summary>
    internal void Pull() => _webSocket?.ReleaseBackpressure();

    /// <summary>
    /// The write algorithm of the writable stream (https://whatpr.org/websockets/48.html#write-chunk): sends a text message
    /// (a string) or a binary message (bytes). The task completes once the message is sent, and fails with an
    /// <c>InvalidStateError</c> when the connection is closed before.
    /// </summary>
    internal Task Write(object data)
    {
        var (bytes, type) = data switch
        {
            string text => (Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(text)), WebSocketMessageType.Text),
            byte[] buffer => (buffer, WebSocketMessageType.Binary),
            _ => throw new ArgumentException("The data must be a string or bytes", nameof(data)),
        };

        var completion = new TaskCompletionSource();
        if (_webSocket is null)
        {
            completion.SetException(ClosedStreamError());
            return completion.Task;
        }

        _webSocket.SendForStream(bytes, type, succeeded =>
        {
            if (succeeded)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(DomException.InvalidState("The WebSocket connection was closed before the message was sent"));
            }
        });
        return completion.Task;
    }

    /// <summary>
    /// The close algorithm of the writable stream (https://whatpr.org/websockets/48.html#close-stream): starts the closing
    /// handshake. The task completes once the connection is closed (it fails when it is not closed cleanly).
    /// </summary>
    internal Task CloseWritable()
    {
        if (_isClosed)
            return _closeError is { } error ? Task.FromException(error) : Task.CompletedTask;

        var completion = new TaskCompletionSource();
        _writableCloseRequests.Add(completion);
        CloseWebSocket(code: null, reason: "");
        return completion.Task;
    }

    /// <summary>
    /// The cancel algorithm of the readable stream and the abort algorithm of the writable stream
    /// (https://whatpr.org/websockets/48.html#close-using-reason): a <see cref="WebSocketError"/> gives the status code and the
    /// reason of the Close frame, any other reason closes the connection without status code.
    /// </summary>
    internal void CloseUsingReason(object? reason)
    {
        ushort? code = null;
        var reasonString = "";
        if (reason is WebSocketError error)
        {
            code = error.CloseCode;
            reasonString = error.Reason;
        }

        try
        {
            CloseWebSocket(code, reasonString);
        }
        catch (DomException)
        {
            CloseWebSocket(code: null, reason: "");
        }
    }

    /// <summary>https://whatpr.org/websockets/48.html#close-the-websocket</summary>
    private void CloseWebSocket(ushort? code, string reason)
    {
        reason = InfraString.ToScalarValueString(reason);
        WebSocketError.ValidateCloseCodeAndReason(code, reason);
        if (code is null && reason.Length > 0)
        {
            code = 1000;
        }

        _webSocket?.Close(code, code is null ? null : reason);
    }

    /// <summary>The abort steps of the signal: the opening handshake is aborted (https://whatpr.org/websockets/48.html#dom-websocketstream-websocketstream, step 8.2).</summary>
    private void OnAbort()
    {
        if (_webSocket is null || _wasEverConnected || _isClosed || _webSocket.ReadyState != WebSocket.Connecting)
            return;

        var exception = ToException(_signal!.AbortReason);
        _handshakeAborted = true;
        _isClosed = true;
        RemoveAbortAlgorithm();
        _webSocket.Close();
        _opened.TrySetException(exception);
        _closed.TrySetException(exception);
    }

    /// <summary>https://whatpr.org/websockets/48.html#feedback-from-the-protocol: the WebSocket connection is established.</summary>
    private void OnOpen()
    {
        if (_handshakeAborted)
            return;

        _wasEverConnected = true;
        RemoveAbortAlgorithm();
        _opened.TrySetResult(new WebSocketOpenInfo { Extensions = _webSocket!.Extensions, Protocol = _webSocket.Protocol });
    }

    /// <summary>https://whatpr.org/websockets/48.html#feedback-from-the-protocol: a WebSocket message has been received.</summary>
    private void OnMessage(object? data)
    {
        if (data is null || _isClosed)
            return;

        if (_endpoints is { } endpoints)
        {
            endpoints.Enqueue(data);
        }
        else
        {
            _receivedMessages.Enqueue(data);
        }
    }

    /// <summary>https://whatpr.org/websockets/48.html#feedback-from-the-protocol: the WebSocket connection is closed.</summary>
    private void OnClose(CloseEvent closeEvent)
    {
        if (_handshakeAborted || _isClosed)
            return;

        _isClosed = true;
        RemoveAbortAlgorithm();
        if (!_wasEverConnected)
        {
            _opened.TrySetException(new WebSocketError("The WebSocket connection could not be established", closeCode: null, reason: ""));
        }

        // The status code of a connection that is not closed cleanly is the one of the Close frame received from the server,
        // if any (https://www.rfc-editor.org/rfc/rfc6455#section-7.1.5)
        var code = closeEvent.Code;
        var reason = closeEvent.Reason;
        if (!closeEvent.WasClean && _webSocket!.ReceivedCloseFrame is { } closeFrame)
        {
            (code, reason) = closeFrame;
        }

        if (closeEvent.WasClean)
        {
            _endpoints?.Close(ClosedStreamError());
            foreach (var request in _writableCloseRequests)
            {
                request.TrySetResult();
            }

            _writableCloseRequests.Clear();
            _closed.TrySetResult(new WebSocketCloseInfo { CloseCode = code, Reason = reason });
            return;
        }

        var error = new WebSocketError("The WebSocket connection was not closed cleanly", code, reason);
        _closeError = error;
        _endpoints?.Error(error);
        foreach (var request in _writableCloseRequests)
        {
            request.TrySetException(error);
        }

        _writableCloseRequests.Clear();
        _closed.TrySetException(error);
    }

    private void RemoveAbortAlgorithm()
    {
        if (_signal is { } signal && _abortAlgorithm is { } algorithm)
        {
            signal.RemoveAbortAlgorithm(algorithm);
        }

        _signal = null;
        _abortAlgorithm = null;
    }

    /// <summary>The error of the writable stream of a connection closed cleanly: "a closed WebSocketStream cannot be written to".</summary>
    private static DomException ClosedStreamError() => DomException.InvalidState("The WebSocketStream is closed: it cannot be written to");

    private static Exception ToException(object? reason) => reason as Exception ?? new AbortSignalException(reason);
}

/// <summary>The readable and the writable streams of a <see cref="WebSocketStream"/> (the streams of the scripts).</summary>
internal interface IWebSocketStreamEndpoints
{
    /// <summary>Enqueues a message in the readable stream: a string (a text message) or bytes (a binary message).</summary>
    void Enqueue(object message);

    /// <summary>The connection is closed cleanly: the readable stream is closed and the writable stream is errored with <paramref name="writableError"/>.</summary>
    void Close(Exception writableError);

    /// <summary>The connection is not closed cleanly: the streams are errored with <paramref name="error"/>.</summary>
    void Error(Exception error);
}
