using System.Buffers;
using System.Collections;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A WebSocket connection (https://websockets.spec.whatwg.org/#the-websocket-interface): a two-way communication with a
/// server, established by an opening handshake that is fetched as specified (cookies, <c>Origin</c>, mixed content, bad
/// ports...), whose connection is opened by <see cref="DocumentLoader.ConnectWebSocketAsync"/>. The frames of the WebSocket
/// protocol (https://www.rfc-editor.org/rfc/rfc6455) are read and written by <see cref="NetWebSocket"/>, with the
/// permessage-deflate extension when the server accepts it.
/// </summary>
/// <remarks>
/// <para>
/// The relevant global object of the WebSocket is the current global object when it is created (the window or the worker
/// of the script that creates it): its events are fired by tasks of its event loop. A WebSocket created without current
/// global object cannot connect: its <see cref="ReadyState"/> is <see cref="Closed"/>.
/// </para>
/// <para>
/// A WebSocket that is connecting or closing is an asynchronous operation of its event loop (see
/// <see cref="DomEventLoop.RunUntilIdleAsync"/>), as are the messages that are being sent. An open connection that waits
/// for messages is not: <see cref="DomEventLoop.RunUntilIdleAsync"/> does not wait for the next message. The WebSockets of
/// a document are closed when the document is unloaded, with the <c>1001</c> (going away) status code, and so are the
/// WebSockets of a worker when it ends.
/// </para>
/// </remarks>
/// <seealso href="https://websockets.spec.whatwg.org/#websocket"/>
[IdlInterface("WebSocket", "https://websockets.spec.whatwg.org/#websocket")]
#pragma warning disable CA1001 // The fetch controller and the connection are disposed when the connection is closed
public sealed class WebSocket : EventTarget
#pragma warning restore CA1001
{
    private const int ReceiveBufferSize = 16 * 1024;

    /// <summary>The WebSockets of each global object that are not closed, for the unloading document cleanup steps.</summary>
    private static readonly ConditionalWeakTable<GlobalScope, List<WebSocket>> ActiveWebSockets = [];

    /// <summary>The state of a WebSocket whose connection has not yet been established.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-connecting"/>
    [IdlMember("CONNECTING", "https://websockets.spec.whatwg.org/#dom-websocket-connecting")]
    public const ushort Connecting = 0;

    /// <summary>The state of a WebSocket whose connection is established: the messages can be sent and received.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-open"/>
    [IdlMember("OPEN", "https://websockets.spec.whatwg.org/#dom-websocket-open")]
    public const ushort Open = 1;

    /// <summary>The state of a WebSocket whose closing handshake was started, or whose <see cref="Close"/> method was called.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-closing"/>
    [IdlMember("CLOSING", "https://websockets.spec.whatwg.org/#dom-websocket-closing")]
    public const ushort Closing = 2;

    /// <summary>The state of a WebSocket whose connection is closed or could not be opened.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-closed"/>
    [IdlMember("CLOSED", "https://websockets.spec.whatwg.org/#dom-websocket-closed")]
    public const ushort Closed = 3;

    private readonly GlobalScope? _global;
    private readonly DomEventLoop? _eventLoop;
    private readonly UrlRecord _url;
    private readonly string[] _protocols;
    private readonly Queue<OutgoingMessage> _sendQueue = new();
    private CancellationTokenSource? _fetchController;
    private FetchRequest? _request;
    private ConnectingQueue? _connectingQueue;
    private bool _holdsConnectingSlot;
    private string? _key;
    private NetWebSocket? _connection;
    private WebSocketFrameObserver? _frameObserver;
    private bool _holdsOperation;

    // The state of the WebSocket protocol (https://www.rfc-editor.org/rfc/rfc6455), which changes in parallel with the ready
    // state: the ready state changes in the tasks queued for the scripts
    private bool _isEstablished;
    private bool _closingHandshakeStarted;
    private bool _closeFrameSent;
    private bool _closeFrameReceived;
    private bool _isSending;
    private bool _isConnectionClosed;
    private bool _failed;
    private ushort _receivedCloseCode;
    private string _receivedCloseReason = "";

    /// <summary>
    /// Creates a WebSocket and starts to establish its connection (https://websockets.spec.whatwg.org/#dom-websocket-websocket).
    /// </summary>
    /// <param name="url">
    /// The URL of the server: a <c>ws:</c> or <c>wss:</c> URL (an <c>http:</c> or <c>https:</c> URL is converted), possibly
    /// relative to the API base URL of the relevant global object, without fragment.
    /// </param>
    /// <param name="protocols">
    /// The subprotocols the server can select: null (none), a string, or a sequence of strings (e.g. a <see cref="string"/>
    /// array). The connection fails when the server does not select one of them.
    /// </param>
    /// <exception cref="DomException">
    /// A <c>SyntaxError</c> when the URL is not valid, has another scheme or a fragment, or when a subprotocol is not a valid
    /// token or appears twice (ASCII case-insensitively).
    /// </exception>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-websocket"/>
    [IdlMember("constructor", "https://websockets.spec.whatwg.org/#dom-websocket-websocket")]
    public WebSocket(string url, object? protocols = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _global = RelevantGlobal;
        _eventLoop = _global?.EventLoop;

        // Steps 1 to 7: the URL record
        var urlRecord = UrlParser.Parse(url, _global?.ApiBaseUrl) ?? throw DomException.Syntax("The URL is not valid: " + url);
        if (urlRecord.Scheme == "http")
        {
            urlRecord.Scheme = "ws";
        }
        else if (urlRecord.Scheme == "https")
        {
            urlRecord.Scheme = "wss";
        }

        if (urlRecord.Scheme is not ("ws" or "wss"))
            throw DomException.Syntax("The URL scheme must be 'ws' or 'wss': " + url);

        if (urlRecord.Fragment is not null)
            throw DomException.Syntax("The URL of a WebSocket must not have a fragment: " + url);

        // Steps 8 and 9: the subprotocols
        _protocols = GetProtocols(protocols);
        _url = urlRecord;

        // Step 12: the connection is established in parallel
        Establish();
    }

    /// <summary>Gets the URL of the WebSocket, serialized (https://websockets.spec.whatwg.org/#dom-websocket-url).</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-url"/>
    [IdlMember("url", "https://websockets.spec.whatwg.org/#dom-websocket-url")]
    public string Url => _url.Serialize();

    /// <summary>
    /// Gets the state of the connection (https://websockets.spec.whatwg.org/#dom-websocket-readystate):
    /// <see cref="Connecting"/>, <see cref="Open"/>, <see cref="Closing"/> or <see cref="Closed"/>.
    /// </summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-readystate"/>
    [IdlMember("readyState", "https://websockets.spec.whatwg.org/#dom-websocket-readystate")]
    public ushort ReadyState { get; private set; }

    /// <summary>
    /// Gets the number of bytes of the messages queued by <see cref="Send(object)"/> that were not yet transmitted to the
    /// network as of the last task (https://websockets.spec.whatwg.org/#dom-websocket-bufferedamount). It only increases
    /// once the connection is closed.
    /// </summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-bufferedamount"/>
    [IdlMember("bufferedAmount", "https://websockets.spec.whatwg.org/#dom-websocket-bufferedamount")]
    public ulong BufferedAmount { get; private set; }

    /// <summary>Gets the extensions selected by the server (the <c>Sec-WebSocket-Extensions</c> header of its response), or the empty string.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-extensions"/>
    [IdlMember("extensions", "https://websockets.spec.whatwg.org/#dom-websocket-extensions")]
    public string Extensions { get; private set; } = "";

    /// <summary>Gets the subprotocol selected by the server (the <c>Sec-WebSocket-Protocol</c> header of its response), or the empty string.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-protocol"/>
    [IdlMember("protocol", "https://websockets.spec.whatwg.org/#dom-websocket-protocol")]
    public string Protocol { get; private set; } = "";

    /// <summary>Gets or sets how the binary messages are exposed: as a <see cref="Blob"/> (the default) or as a <see cref="byte"/> array (an <c>ArrayBuffer</c>).</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-binarytype"/>
    [IdlMember("binaryType", "https://websockets.spec.whatwg.org/#dom-websocket-binarytype")]
    public BinaryType BinaryType { get; set; }

    /// <summary>Gets or sets the event handler of the <c>open</c> event.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#handler-websocket-onopen"/>
    [IdlMember("onopen", "https://websockets.spec.whatwg.org/#handler-websocket-onopen")]
    public Action<Event>? OnOpen
    {
        get => GetEventHandler("open");
        set => SetEventHandler("open", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#handler-websocket-onerror"/>
    [IdlMember("onerror", "https://websockets.spec.whatwg.org/#handler-websocket-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Gets or sets the event handler of the <c>close</c> event.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#handler-websocket-onclose"/>
    [IdlMember("onclose", "https://websockets.spec.whatwg.org/#handler-websocket-onclose")]
    public Action<Event>? OnClose
    {
        get => GetEventHandler("close");
        set => SetEventHandler("close", value);
    }

    /// <summary>Gets or sets the event handler of the <c>message</c> event.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#handler-websocket-onmessage"/>
    [IdlMember("onmessage", "https://websockets.spec.whatwg.org/#handler-websocket-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>
    /// Closes the connection (https://websockets.spec.whatwg.org/#dom-websocket-close): starts the closing handshake, or
    /// fails the connection when it is not established yet.
    /// </summary>
    /// <param name="code">The status code of the Close frame: <c>1000</c> or a code from <c>3000</c> to <c>4999</c>, or null for a Close frame without status code.</param>
    /// <param name="reason">The reason of the Close frame (at most 123 bytes in UTF-8), or null.</param>
    /// <exception cref="DomException">An <c>InvalidAccessError</c> when the code is not valid, or a <c>SyntaxError</c> when the reason is too long.</exception>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-close"/>
    [IdlMember("close", "https://websockets.spec.whatwg.org/#dom-websocket-close")]
    public void Close(ushort? code = null, string? reason = null)
    {
        // Step 1
        if (code is { } value && value != 1000 && value is not (>= 3000 and <= 4999))
            throw new DomException("The close code must be 1000 or between 3000 and 4999", "InvalidAccessError");

        // Step 2
        if (reason is not null)
        {
            reason = InfraString.ToScalarValueString(reason);
            if (Encoding.UTF8.GetByteCount(reason) > 123)
                throw DomException.Syntax("The close reason must not be longer than 123 bytes");
        }

        // Step 3
        if (ReadyState is Closing or Closed)
            return;

        if (!_isEstablished)
        {
            Fail();
        }
        else if (!_closingHandshakeStarted)
        {
            // A Close frame without status code has no reason (the reason requires a code)
            StartClosingHandshake(code, code is null ? null : reason);
        }

        ReadyState = Closing;
        UpdateOperation();
    }

    /// <summary>
    /// Sends a message (https://websockets.spec.whatwg.org/#dom-websocket-send): a text message for a string, a binary message
    /// for a <see cref="Blob"/> or bytes (a <see cref="byte"/> array, a <see cref="ReadOnlyMemory{T}"/> of bytes, or a buffer
    /// source of the script engine). The message is not sent when the closing handshake was started, but
    /// <see cref="BufferedAmount"/> increases anyway.
    /// </summary>
    /// <param name="data">The message.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the connection is not established yet.</exception>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-websocket-send"/>
    [IdlMember("send", "https://websockets.spec.whatwg.org/#dom-websocket-send")]
    public void Send(object? data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (ReadyState == Connecting)
            throw DomException.InvalidState("The WebSocket is not open");

        var (bytes, type) = data switch
        {
            string text => (Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(text)), WebSocketMessageType.Text),
            Blob blob => (blob.Data.ToArray(), WebSocketMessageType.Binary),
            byte[] buffer => (buffer.ToArray(), WebSocketMessageType.Binary),
            ReadOnlyMemory<byte> memory => (memory.ToArray(), WebSocketMessageType.Binary),
            Scripting.JsBufferSource bufferSource => (bufferSource.Bytes.ToArray(), WebSocketMessageType.Binary),
            _ => (Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(data.ToString() ?? "")), WebSocketMessageType.Text),
        };

        BufferedAmount += (ulong)bytes.Length;
        if (_isEstablished && !_closingHandshakeStarted && !_isConnectionClosed)
        {
            _sendQueue.Enqueue(new OutgoingMessage(bytes, type, CloseCode: null, CloseReason: null));
            SendQueuedMessages();
        }
    }

    /// <summary>
    /// Sends a text message (https://websockets.spec.whatwg.org/#dom-websocket-send).
    /// </summary>
    /// <param name="data">The text of the message.</param>
    [NotInIdl("Convenience overload of send(USVString) for .NET callers")]
    public void Send(string data) => Send((object)data);

    /// <summary>
    /// The unloading document cleanup steps (https://html.spec.whatwg.org/multipage/document-lifecycle.html#unloading-document-cleanup-steps)
    /// and the end of a worker: makes the WebSockets of a global object disappear.
    /// </summary>
    internal static void MakeDisappear(GlobalScope global)
    {
        if (!ActiveWebSockets.TryGetValue(global, out var webSockets))
            return;

        ActiveWebSockets.Remove(global);
        foreach (var webSocket in webSockets.ToArray())
        {
            webSocket.MakeDisappear();
        }
    }

    /// <summary>Whether a WebSocket of the global object is connecting or open (its closing handshake was not started).</summary>
    internal static bool HasConnectingOrOpenWebSockets(GlobalScope global)
        => ActiveWebSockets.TryGetValue(global, out var webSockets) && webSockets.Exists(webSocket => webSocket.ReadyState is Connecting or Open);

    /// <summary>https://websockets.spec.whatwg.org/#make-disappear</summary>
    private void MakeDisappear()
    {
        if (_isConnectionClosed)
            return;

        if (!_isEstablished)
        {
            Fail();
            return;
        }

        // The events are no longer fired, and the connection is closed once its Close frame is sent: the document goes away
        // (its event loop may stop), so the closing handshake is not completed
        var connection = _connection!;
        var sendCloseFrame = !_closingHandshakeStarted || !_closeFrameSent;
        _closingHandshakeStarted = true;
        _sendQueue.Clear();
        _connection = null;
        _isConnectionClosed = true;
        ReadyState = Closed;
        RemoveFromActiveWebSockets();
        UpdateOperation();
        _ = CloseAndDisposeAsync(connection, sendCloseFrame);

        static async Task CloseAndDisposeAsync(NetWebSocket connection, bool sendCloseFrame)
        {
            try
            {
                if (sendCloseFrame)
                {
                    await connection.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "", CancellationToken.None).ConfigureAwait(false);
                }
            }
#pragma warning disable CA1031 // The connection is closed anyway
            catch (Exception)
#pragma warning restore CA1031
            {
            }
            finally
            {
                connection.Dispose();
            }
        }
    }

    /// <summary>
    /// The subprotocols of the constructor (https://websockets.spec.whatwg.org/#dom-websocket-websocket, steps 8 and 9): a
    /// <c>SyntaxError</c> is thrown when one of them is not a token or appears twice.
    /// </summary>
    private static string[] GetProtocols(object? protocols)
    {
        string[] result = protocols switch
        {
            null => [],
            string protocol => [protocol],
            IEnumerable<string> strings => [.. strings],
            IEnumerable items => [.. items.Cast<object?>().Select(item => item as string ?? Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? "")],
            _ => [protocols.ToString() ?? ""],
        };

        for (var i = 0; i < result.Length; i++)
        {
            if (!FetchHeaders.IsToken(result[i]))
                throw DomException.Syntax("The subprotocol is not valid: " + result[i]);

            for (var j = 0; j < i; j++)
            {
                if (InfraAscii.EqualsIgnoreAsciiCase(result[i], result[j]))
                    throw DomException.Syntax("The subprotocol is specified more than once: " + result[i]);
            }
        }

        return result;
    }

    /// <summary>https://websockets.spec.whatwg.org/#concept-websocket-establish</summary>
    private void Establish()
    {
        if (_global is null || _eventLoop is null)
        {
            // There is no event loop to fire the events: the connection fails silently
            _isConnectionClosed = true;
            ReadyState = Closed;
            return;
        }

        var client = _global.EnvironmentDocument;

        // Step 1: the URL of the request has the http or https scheme
        var requestUrl = _url.Clone();
        requestUrl.Scheme = _url.Scheme == "ws" ? "http" : "https";

        // Steps 2 to 10: the request and the headers of the handshake
        _key = WebSocketHandshake.CreateKey();
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Upgrade", "websocket"),
            new("Connection", "Upgrade"),
            new("Sec-WebSocket-Key", _key),
            new("Sec-WebSocket-Version", "13"),
        };

        if (_protocols.Length > 0)
        {
            headers.Add(new("Sec-WebSocket-Protocol", string.Join(", ", _protocols)));
        }

        headers.Add(new("Sec-WebSocket-Extensions", WebSocketHandshake.PermessageDeflateOffer));
        var request = new FetchRequest(requestUrl)
        {
            HeaderList = headers,
            Client = client,
            Origin = client.SerializeOrigin(),
            Referrer = "no-referrer",
            Mode = RequestMode.WebSocket,
            CredentialsMode = FetchCredentialsMode.Include,
            CacheMode = "no-store",
            RedirectMode = FetchRedirectMode.Error,

            // The credentials of the URL are sent when the server requests them (there is no prompt)
            UseUrlCredentials = requestUrl.IncludesCredentials,
        };

        ActiveWebSockets.GetOrCreateValue(_global).Add(this);
        UpdateOperation();
        _request = request;
        _connectingQueue = ConnectingQueue.Get(client);
        if (_connectingQueue is null || _connectingQueue.Enter(this))
        {
            StartFetch();
        }
    }

    /// <summary>The key of the connections that are established one at a time: the host and the port of the URL.</summary>
    private string ConnectingKey => _url.Host!.Value.Serialized + ":" + (_url.Port >= 0 ? _url.Port : UrlSchemes.GetDefaultPort(_url.Scheme)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Fetches the opening handshake, once the other connections to the same host are established or have failed.</summary>
    private void StartFetch()
    {
        _holdsConnectingSlot = true;
        if (_isConnectionClosed || _request is not { } request)
        {
            ReleaseConnectingSlot();
            return;
        }

        _request = null;
        _fetchController = new CancellationTokenSource();
        _ = FetchAsync(request, _fetchController.Token);
    }

    /// <summary>The connection is established or has failed: the next connection to the same host can be established.</summary>
    private void ReleaseConnectingSlot()
    {
        if (_holdsConnectingSlot)
        {
            _holdsConnectingSlot = false;
            _connectingQueue?.Exit(this);
        }
        else
        {
            _connectingQueue?.Remove(this);
        }
    }

    private async Task FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        FetchResponse response;
        try
        {
            response = await Fetching.FetchAsync(request, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failure is a network error
        catch (Exception)
#pragma warning restore CA1031
        {
            response = FetchResponse.NetworkError();
        }

        // The response is processed in another task: the connection is never established during the task that creates the
        // WebSocket (e.g. close() fails a WebSocket that was just created), as the network is in parallel
        var connection = response.Internal.Connection;
        if (_eventLoop!.IsDisposed)
        {
            connection?.Dispose();
            _holdsConnectingSlot = false;
            _connectingQueue?.Exit(this);
            return;
        }

        _eventLoop.QueueTask(() => ProcessResponse(response, connection));
    }

    /// <summary>https://websockets.spec.whatwg.org/#concept-websocket-establish, step 11 (processResponse)</summary>
    private void ProcessResponse(FetchResponse response, Stream? connection)
    {
        _fetchController?.Dispose();
        _fetchController = null;
        ReleaseConnectingSlot();
        if (_isConnectionClosed)
        {
            connection?.Dispose();
            return;
        }

        // Steps 11.1 to 11.3
        var internalResponse = response.Internal;
        if (response.IsNetworkError || internalResponse.Status != 101 || connection is null
            || !WebSocketHandshake.Validate(internalResponse.HeaderList, _key!, _protocols, out var protocol, out var extensions, out var deflateOptions))
        {
            connection?.Dispose();
            Fail();
            return;
        }

        // The WebSocket connection is established (https://websockets.spec.whatwg.org/#feedback-from-the-protocol)
        NetWebSocket webSocket;
#pragma warning disable CA2000 // The connection is disposed when it is closed
        var observer = new WebSocketFrameObserver(connection);
        try
        {
            webSocket = NetWebSocket.CreateFromStream(observer, new WebSocketCreationOptions
            {
                IsServer = false,
                SubProtocol = protocol.Length == 0 ? null : protocol,

                // The user agent does not send pings (https://websockets.spec.whatwg.org/#ping-and-pong-frames)
                KeepAliveInterval = TimeSpan.Zero,
                DangerousDeflateOptions = deflateOptions,
            });
#pragma warning restore CA2000
        }
#pragma warning disable CA1031 // The connection cannot be used
        catch (Exception)
#pragma warning restore CA1031
        {
            connection.Dispose();
            Fail();
            return;
        }

        _connection = webSocket;
        _frameObserver = observer;
        _isEstablished = true;
        QueueWebSocketTask(() =>
        {
            // close() may have been called since the connection was established: the closing handshake is in progress
            if (ReadyState != Connecting)
                return;

            ReadyState = Open;
            Extensions = extensions;
            Protocol = protocol;
            UpdateOperation();
            FireEvent(new Event("open"));
        });

        // The messages are received after the open task is queued: their tasks run after it
        _ = ReceiveAsync(webSocket, observer);
    }

    /// <summary>
    /// Receives the messages of the connection, in parallel: each message (and the Close frame) is processed by a task of the
    /// event loop (https://websockets.spec.whatwg.org/#feedback-from-the-protocol).
    /// </summary>
    private async Task ReceiveAsync(NetWebSocket webSocket, WebSocketFrameObserver observer)
    {
        var buffer = new byte[ReceiveBufferSize];
        var message = new ArrayBufferWriter<byte>();
        try
        {
            while (true)
            {
                var result = await webSocket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // A Close frame without status code is reported as a normal closure
                    var status = observer.LastCloseFrameHasStatusCode ? webSocket.CloseStatus : null;
                    var description = webSocket.CloseStatusDescription;
                    _eventLoop!.QueueTask(() => OnCloseFrameReceived(webSocket, status, description));
                    return;
                }

                message.Write(buffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                    continue;

                var data = message.WrittenSpan.ToArray();
                message.ResetWrittenCount();
                var isText = result.MessageType == WebSocketMessageType.Text;
                QueueWebSocketTask(() => OnMessageReceived(isText, data));
            }
        }
#pragma warning disable CA1031 // The errors of the connection close it
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // A connection closed without Close frame (e.g. the server dropped it) is closed abnormally; a protocol error (e.g.
            // an invalid frame, or a text message that is not valid UTF-8) fails the connection
            var abnormal = ex is WebSocketException { WebSocketErrorCode: WebSocketError.ConnectionClosedPrematurely } || ex.InnerException is IOException || ex is IOException;
            _eventLoop!.QueueTask(() => OnConnectionError(webSocket, abnormal));
        }
    }

    /// <summary>https://websockets.spec.whatwg.org/#feedback-from-the-protocol: a WebSocket message has been received.</summary>
    private void OnMessageReceived(bool isText, byte[] data)
    {
        if (ReadyState != Open)
            return;

        object dataForEvent = isText
            ? Encoding.UTF8.GetString(data)
            : BinaryType == BinaryType.ArrayBuffer ? data : new Blob([data]);
        FireEvent(new MessageEvent("message", new MessageEventInit { Data = dataForEvent, Origin = _url.SerializeOrigin() }));
    }

    /// <summary>A Close frame has been received (https://www.rfc-editor.org/rfc/rfc6455#section-5.5.1).</summary>
    private void OnCloseFrameReceived(NetWebSocket webSocket, WebSocketCloseStatus? status, string? description)
    {
        if (!ReferenceEquals(webSocket, _connection) || _isConnectionClosed)
            return;

        _closeFrameReceived = true;
        _receivedCloseCode = status is null or WebSocketCloseStatus.Empty ? (ushort)1005 : (ushort)status.Value;
        _receivedCloseReason = description ?? "";

        // The closing handshake was started by the server: the Close frame is echoed with its status code and its reason, as in
        // the browsers, after the messages that are being sent (https://www.rfc-editor.org/rfc/rfc6455#section-5.5.1)
        if (!_closingHandshakeStarted)
        {
            // https://websockets.spec.whatwg.org/#feedback-from-the-protocol: the closing handshake is started
            QueueWebSocketTask(() =>
            {
                if (ReadyState == Open)
                {
                    ReadyState = Closing;
                    UpdateOperation();
                }
            });
            StartClosingHandshake(status is null or WebSocketCloseStatus.Empty ? null : (ushort)status.Value, description);
        }

        CompleteClosingHandshake();
    }

    private void OnConnectionError(NetWebSocket webSocket, bool abnormalClosure)
    {
        if (!ReferenceEquals(webSocket, _connection) || _isConnectionClosed)
            return;

        if (abnormalClosure)
        {
            CloseConnection(1006, "", wasClean: false);
        }
        else
        {
            Fail();
        }
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc6455#section-7.1.2: starts the closing handshake by sending a Close frame, after the
    /// messages that are queued.
    /// </summary>
    private void StartClosingHandshake(ushort? code, string? reason)
    {
        _closingHandshakeStarted = true;
        _sendQueue.Enqueue(new OutgoingMessage([], WebSocketMessageType.Close, code, reason));
        SendQueuedMessages();
    }

    /// <summary>The closing handshake is complete once a Close frame has been sent and received: the connection is closed cleanly.</summary>
    private void CompleteClosingHandshake()
    {
        if (_closeFrameSent && _closeFrameReceived && !_isConnectionClosed)
        {
            CloseConnection(_receivedCloseCode, _receivedCloseReason, wasClean: true);
        }
    }

    /// <summary>
    /// Sends the queued messages, one at a time. A message whose transmission completes synchronously is followed by the
    /// next one; otherwise, the next message is sent by a task queued when the transmission completes.
    /// </summary>
    private void SendQueuedMessages()
    {
        while (!_isSending && !_isConnectionClosed && _connection is { } connection && _sendQueue.TryDequeue(out var message))
        {
            // bufferedAmount is the amount that was not transmitted as of the last time the event loop reached step 1: it decreases
            // in a task, queued before the message is handed to the connection. The task of a message received in reply (e.g.
            // the echo of the message) is queued after it, whichever thread observes the end of the transmission or the reply first.
            var length = (ulong)message.Data.Length;
            var handedToConnection = true;
            if (length > 0)
            {
                _eventLoop!.QueueTask(() =>
                {
                    if (handedToConnection)
                    {
                        BufferedAmount -= length;
                    }
                });
            }

            ValueTask transmission;
            try
            {
                if (message.Type != WebSocketMessageType.Close)
                {
                    transmission = connection.SendAsync(message.Data.AsMemory(), message.Type, endOfMessage: true, CancellationToken.None);
                }
                else if (message.CloseCode is { } code)
                {
                    transmission = new ValueTask(connection.CloseOutputAsync((WebSocketCloseStatus)code, message.CloseReason ?? "", CancellationToken.None));
                }
                else
                {
                    // A Close frame without body (https://websockets.spec.whatwg.org/#dom-websocket-close, step 3)
                    _frameObserver!.WriteNextCloseFrameWithoutStatusCode = true;
                    transmission = new ValueTask(connection.CloseOutputAsync(WebSocketCloseStatus.Empty, statusDescription: null, CancellationToken.None));
                }
            }
#pragma warning disable CA1031 // A failure of the connection fails it
            catch (Exception)
#pragma warning restore CA1031
            {
                handedToConnection = false;
                Fail();
                return;
            }

            if (transmission.IsCompleted)
            {
                OnMessageTransmitted(connection, message, transmission.IsCompletedSuccessfully);
                continue;
            }

            _isSending = true;
            UpdateOperation();
            _ = WaitForTransmissionAsync(connection, message, transmission);
        }
    }

    private async Task WaitForTransmissionAsync(NetWebSocket connection, OutgoingMessage message, ValueTask transmission)
    {
        var succeeded = true;
        try
        {
            await transmission.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failure of the connection fails it
        catch (Exception)
#pragma warning restore CA1031
        {
            succeeded = false;
        }

        _eventLoop!.QueueTask(() =>
        {
            _isSending = false;
            OnMessageTransmitted(connection, message, succeeded);
            SendQueuedMessages();
            UpdateOperation();
        });
    }

    /// <param name="connection">The connection that transmitted the message.</param>
    /// <param name="message">The message.</param>
    /// <param name="succeeded">Whether the transmission succeeded.</param>
    private void OnMessageTransmitted(NetWebSocket connection, OutgoingMessage message, bool succeeded)
    {
        if (!ReferenceEquals(connection, _connection) || _isConnectionClosed)
            return;

        if (!succeeded)
        {
            Fail();
            return;
        }

        if (message.Type == WebSocketMessageType.Close)
        {
            _closeFrameSent = true;
            CompleteClosingHandshake();
        }
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc6455#section-7.1.7: fail the WebSocket connection.</summary>
    private void Fail()
    {
        if (_isConnectionClosed)
            return;

        _failed = true;
        CloseConnection(1006, "", wasClean: false);
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc6455#section-7.1.1 (close the WebSocket connection) and
    /// https://websockets.spec.whatwg.org/#feedback-from-the-protocol (the WebSocket connection is closed).
    /// </summary>
    private void CloseConnection(ushort code, string reason, bool wasClean)
    {
        _isConnectionClosed = true;
        _sendQueue.Clear();
        _request = null;
        ReleaseConnectingSlot();
        if (_fetchController is { } fetchController)
        {
            _fetchController = null;
            fetchController.Cancel();
            fetchController.Dispose();
        }

        RemoveFromActiveWebSockets();
        var failed = _failed;
        void FireCloseEvents()
        {
            if (failed)
            {
                FireEvent(new Event("error"));
            }

            FireEvent(new CloseEvent("close", new CloseEventInit { WasClean = wasClean, Code = code, Reason = reason }));
        }

        _eventLoop!.QueueTask(() =>
        {
            ReadyState = Closed;
            UpdateOperation();

            // The events of a WebSocket closed while its document is in the back/forward cache (e.g. by a pagehide event
            // handler) are fired once the document is restored
            if (_global is Window { Document: { IsInBackForwardCache: true } cachedDocument })
            {
                cachedDocument.DeferTaskUntilReactivated(FireCloseEvents);
                return;
            }

            if (_global is not { CanRunScript: true })
                return;

            FireCloseEvents();
        });

        // The connection is closed once the task is queued
        _connection?.Dispose();
        _connection = null;
        _frameObserver = null;
    }

    private void RemoveFromActiveWebSockets()
    {
        if (_global is not null && ActiveWebSockets.TryGetValue(_global, out var webSockets))
        {
            webSockets.Remove(this);
        }
    }

    /// <summary>Queues a task of the WebSocket task source, which runs only if the scripts of the relevant global object can run.</summary>
    private void QueueWebSocketTask(Action task) => _global?.QueueGlobalTask(task);

    /// <summary>
    /// The connection is an asynchronous operation of the event loop while it is connecting or closing, or a message is being
    /// transmitted (see <see cref="DomEventLoop.HasPendingOperations"/>).
    /// </summary>
    private void UpdateOperation()
    {
        if (_eventLoop is null)
            return;

        var pending = ReadyState is Connecting or Closing || _isSending;
        if (pending == _holdsOperation)
            return;

        _holdsOperation = pending;
        if (pending)
        {
            _eventLoop.BeginOperation();
        }
        else
        {
            _eventLoop.EndOperation();
        }
    }

    /// <summary>
    /// The connections that are being established (https://www.rfc-editor.org/rfc/rfc6455#section-4.1, item 2 of the client
    /// requirements): there is at most one connection in the CONNECTING state to a host and a port (the host of the URL, which
    /// is not resolved), the other ones wait until it is established or has failed. The queue of a top-level browsing context
    /// is shared by the windows of its browsing context group and by their workers (which run on other threads).
    /// </summary>
    private sealed class ConnectingQueue
    {
        private static readonly ConditionalWeakTable<BrowsingContext, ConnectingQueue> Queues = [];

        private readonly Lock _lock = new();
        private readonly Dictionary<string, List<WebSocket>> _connections = new(StringComparer.Ordinal);

        /// <summary>The queue of the top-level browsing context of a client (a document, or the document of a worker), or null.</summary>
        public static ConnectingQueue? Get(Document client)
        {
            return client.EnvironmentBrowsingContext?.Top is { } top ? Queues.GetValue(top, static _ => new ConnectingQueue()) : null;
        }

        /// <summary>Adds a connection: returns true when it can be established now, otherwise it is started when its turn comes.</summary>
        public bool Enter(WebSocket webSocket)
        {
            lock (_lock)
            {
                if (_connections.TryGetValue(webSocket.ConnectingKey, out var waiting))
                {
                    waiting.Add(webSocket);
                    return false;
                }

                _connections.Add(webSocket.ConnectingKey, []);
                return true;
            }
        }

        /// <summary>The connection being established is established or has failed: the next one is started.</summary>
        public void Exit(WebSocket webSocket)
        {
            while (true)
            {
                WebSocket? next;
                lock (_lock)
                {
                    if (!_connections.TryGetValue(webSocket.ConnectingKey, out var waiting))
                        return;

                    if (waiting.Count == 0)
                    {
                        _connections.Remove(webSocket.ConnectingKey);
                        return;
                    }

                    next = waiting[0];
                    waiting.RemoveAt(0);
                }

                // The connection is started by a task of its event loop; a connection whose event loop ended is skipped
                if (next._eventLoop is { IsDisposed: false } eventLoop)
                {
                    eventLoop.QueueTask(next.StartFetch);
                    return;
                }
            }
        }

        /// <summary>Removes a connection that is waiting for its turn (it was closed).</summary>
        public void Remove(WebSocket webSocket)
        {
            lock (_lock)
            {
                if (_connections.TryGetValue(webSocket.ConnectingKey, out var waiting))
                {
                    waiting.Remove(webSocket);
                }
            }
        }
    }

    /// <summary>A message queued by <see cref="Send(object)"/>, or the Close frame of the closing handshake.</summary>
    private sealed record OutgoingMessage(byte[] Data, WebSocketMessageType Type, ushort? CloseCode, string? CloseReason);
}

/// <summary>How the binary messages of a <see cref="WebSocket"/> are exposed (https://websockets.spec.whatwg.org/#enumdef-binarytype).</summary>
/// <seealso href="https://websockets.spec.whatwg.org/#enumdef-binarytype"/>
[IdlInterface("BinaryType", "https://websockets.spec.whatwg.org/#enumdef-binarytype")]
public enum BinaryType
{
    /// <summary>A <see cref="Dom.FileApi.Blob"/>.</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-binarytype-blob"/>
    [IdlMember("blob", "https://websockets.spec.whatwg.org/#dom-binarytype-blob")]
    Blob,

    /// <summary>A <see cref="byte"/> array (an <c>ArrayBuffer</c> in JavaScript).</summary>
    /// <seealso href="https://websockets.spec.whatwg.org/#dom-binarytype-arraybuffer"/>
    [IdlMember("arraybuffer", "https://websockets.spec.whatwg.org/#dom-binarytype-arraybuffer")]
    ArrayBuffer,
}
