using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The WebSocket server of wptserve (pywebsocket3, https://github.com/web-platform-tests/wpt/tree/master/tools/third_party/pywebsocket3)
/// emulated by the runner: the connections of <see cref="DocumentLoader.ConnectWebSocketAsync"/> are in-memory pipes, whose
/// server side runs the handlers of <c>websockets/handlers/*_wsh.py</c> ported to .NET (see <see cref="WptWebSocketHandlers"/>).
/// The handlers read and write the frames of the protocol themselves, as pywebsocket does, so they can send raw bytes (e.g.
/// an invalid handshake response).
/// </summary>
/// <remarks>
/// The handlers run on the thread pool, in parallel with the event loop: the runner does not advance the virtual clock while
/// a connection is busy (<see cref="IsBusy"/>), i.e. while bytes are waiting to be read by one side, or a side has read bytes
/// and has not asked for more (it is processing them). The delays of the handlers (<c>time.sleep()</c>) use the virtual clock.
/// </remarks>
internal sealed partial class WptWebSocketServer(WptTestRunner runner)
{
    private readonly Lock _lock = new();
    private readonly List<WptWebSocketConnection> _connections = [];

    /// <summary>Gets the runner (the virtual clock, the stash and the log).</summary>
    public WptTestRunner Runner => runner;

    /// <summary>Whether a connection is busy: the runner must not advance the virtual clock (the handler is about to respond).</summary>
    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                _connections.RemoveAll(connection => connection.IsClosed);
                return _connections.Exists(connection => connection.IsBusy);
            }
        }
    }

    /// <summary>
    /// Opens a connection and performs the opening handshake as pywebsocket does (https://www.rfc-editor.org/rfc/rfc6455#section-4.2):
    /// the handler of the path of the URL (<c>/echo</c> is <c>handlers/echo_wsh.py</c>) may change the response or write raw
    /// bytes. The response is read from the connection, as a browser does: an invalid response is a network error.
    /// </summary>
    public ValueTask<DocumentResource?> ConnectAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        var url = new Url(request.Url);
        if (!WptServer.IsWebSocketHost(url))
            return ValueTask.FromResult<DocumentResource?>(null);

        var connection = new WptWebSocketConnection(this, url);
        lock (_lock)
        {
            _connections.Add(connection);
        }

        var headers = WptTestRunner.GetRequestHeaders(url, request);
        var wsRequest = new WptWebSocketRequest(connection, url, headers, isSecure: url.Protocol == "https:");

        // The server runs in parallel: a handler may read the bytes of the browser during the handshake
        _ = Task.Run(() => HandshakeAsync(wsRequest), CancellationToken.None);

        // The result is given by a task of the event loop of the test page: the continuation of the library (which queues a
        // task on the event loop of the WebSocket) runs synchronously, so the runner never sees an idle event loop meanwhile
        var result = new TaskCompletionSource<DocumentResource?>();
        _ = Task.Run(() => ReadResponseAsync(request, connection, result, cancellationToken), CancellationToken.None);
        return new ValueTask<DocumentResource?>(result.Task);
    }

    /// <summary>Reads the response of the server, as the browser reads it.</summary>
    private async Task ReadResponseAsync(DocumentRequest request, WptWebSocketConnection connection, TaskCompletionSource<DocumentResource?> result, CancellationToken cancellationToken)
    {
        DocumentResource? response = null;
        Exception? exception = null;
        try
        {
            response = await ReadResponseAsync(connection.ClientStream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            exception = ex;
        }

        runner.QueueTask(() =>
        {
            // The cookies of the response are stored by the task, as the browsers process the response after the current task
            // (e.g. a script blocked by a synchronous XMLHttpRequest does not see them)
            if (response is not null)
            {
                request.StoreCookies(response.Headers.Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)).Select(header => header.Value));
            }

            if (response is not { StatusCode: 101 })
            {
                connection.ClientStream.Dispose();
            }

            if (exception is not null)
            {
                result.SetException(exception);
            }
            else
            {
                result.SetResult(response);
            }
        });
    }

    /// <summary>https://github.com/web-platform-tests/wpt/blob/master/tools/third_party/pywebsocket3/pywebsocket3/handshake/base.py (do_handshake), then the data transfer in parallel.</summary>
    private async Task HandshakeAsync(WptWebSocketRequest request)
    {
        try
        {
            await HandshakeCoreAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ex is not WptWebSocketConnectionTerminatedException)
            {
                runner.Log("WebSocket handshake error: " + request.Url.Pathname + ": " + ex);
            }

            request.Connection.CloseServer();
        }
    }

    private async Task HandshakeCoreAsync(WptWebSocketRequest request)
    {
        var connection = request.Connection;
        var path = Uri.UnescapeDataString(request.Url.Pathname);
        var handler = WptWebSocketHandlers.Get(path);
        if (handler is null)
        {
            runner.Log("WebSocket handler not emulated: " + path);
            connection.WriteRaw("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"u8);
            connection.CloseServer();
            return;
        }

        try
        {
            request.ValidateHandshakeRequest();
            if (handler.DoExtraHandshake is { } doExtraHandshake)
            {
                await doExtraHandshake(request).ConfigureAwait(false);
            }

            request.ValidateSelectedProtocol();
        }
        catch (WptWebSocketAbortException)
        {
            // The connection is abandoned without response (the bytes written by the handler are sent)
            connection.CloseServer();
            return;
        }
        catch (WptWebSocketHandshakeException ex)
        {
            runner.Log("WebSocket handshake error: " + path + ": " + ex.Message);
            connection.WriteRaw(Encoding.ASCII.GetBytes("HTTP/1.1 " + ex.Status.ToString(CultureInfo.InvariantCulture) + " " + (ex.Status == 403 ? "Forbidden" : "Bad Request") + "\r\nContent-Length: 0\r\n\r\n"));
            connection.CloseServer();
            return;
        }

        // The response of pywebsocket (hybi.py, _create_handshake_response): the extensions are declined
        var response = new StringBuilder();
        response.Append("HTTP/1.1 101 Switching Protocols\r\n");
        response.Append("Upgrade: websocket\r\n");
        response.Append("Connection: Upgrade\r\n");
        response.Append("Sec-WebSocket-Accept: ").Append(ComputeAccept(request.Header("Sec-WebSocket-Key")!)).Append("\r\n");
        if (request.WsProtocol is { } protocol)
        {
            response.Append("Sec-WebSocket-Protocol: ").Append(protocol).Append("\r\n");
        }

        foreach (var (name, value) in request.ExtraHeaders)
        {
            response.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        response.Append("\r\n");
        connection.WriteRaw(Encoding.UTF8.GetBytes(response.ToString()));

        // The data transfer runs in parallel (dispatch.py, transfer_data)
        _ = Task.Run(() => TransferDataAsync(request, handler));
    }

    private async Task TransferDataAsync(WptWebSocketRequest request, WptWebSocketHandler handler)
    {
        var connection = request.Connection;
        try
        {
            await handler.TransferData(request).ConfigureAwait(false);
            if (!request.ServerTerminated)
            {
                await request.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
        catch (WptWebSocketAbortException)
        {
        }
        catch (WptWebSocketBadOperationException)
        {
            await TryCloseAsync(request, 1011).ConfigureAwait(false);
        }
        catch (WptWebSocketInvalidFrameException)
        {
            await TryCloseAsync(request, 1002).ConfigureAwait(false);
        }
        catch (WptWebSocketUnsupportedFrameException)
        {
            await TryCloseAsync(request, 1003).ConfigureAwait(false);
        }
        catch (WptWebSocketInvalidUtf8Exception)
        {
            await TryCloseAsync(request, 1007).ConfigureAwait(false);
        }
        catch (WptWebSocketConnectionTerminatedException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            runner.Log("WebSocket handler error: " + request.Url.Pathname + ": " + ex);
        }
        finally
        {
            // The server closes the TCP connection
            connection.CloseServer();
        }

        static async Task TryCloseAsync(WptWebSocketRequest request, int code)
        {
            try
            {
                await request.CloseConnectionAsync(code, "").ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WptWebSocketException or IOException)
            {
            }
        }
    }

    /// <summary>
    /// Reads the response of the opening handshake from the connection, as a browser does: the status line and the headers
    /// (https://www.rfc-editor.org/rfc/rfc9112#section-4). The rest of the bytes are the frames. Returns null when the
    /// response is not a valid HTTP/1.1 response (e.g. the connection was closed before the end of its headers).
    /// </summary>
    private static async Task<DocumentResource?> ReadResponseAsync(WptWebSocketClientStream stream, CancellationToken cancellationToken)
    {
        // The bytes are read one at a time: the bytes after the headers are the frames, read by the library
        var head = new List<byte>();
        var buffer = new byte[1];
        while (!(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
        {
            if (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) == 0)
                return null;

            head.Add(buffer[0]);
        }

        var lines = Encoding.Latin1.GetString([.. head]).Split("\r\n");
        var match = StatusLineRegex().Match(lines[0]);
        if (!match.Success)
            return null;

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
                continue;

            var index = line.IndexOf(':', StringComparison.Ordinal);
            if (index <= 0)
                return null;

            headers.Add(new(line[..index], line[(index + 1)..].Trim(' ', '\t')));
        }

        var status = int.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture);
        return new DocumentResource(ReadOnlyMemory<byte>.Empty)
        {
            StatusCode = status,
            StatusText = match.Groups["reason"].Value,
            Headers = headers,
            Connection = status == 101 ? stream : null,
        };
    }

    [GeneratedRegex(@"^HTTP/1\.1 (?<status>[0-9]{3})(?: (?<reason>.*))?$")]
    private static partial Regex StatusLineRegex();

#pragma warning disable CA5350 // SHA-1 is the hash of the protocol
    public static string ComputeAccept(string key) => Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
#pragma warning restore CA5350
}

/// <summary>A connection of the emulated WebSocket server: two in-memory pipes, one for each direction.</summary>
internal sealed class WptWebSocketConnection
{
    public WptWebSocketConnection(WptWebSocketServer server, Url url)
    {
        Server = server;
        Url = url;

        // The server is running (the handshake) until it waits for bytes
        ClientToServer = new WptWebSocketPipe { ReaderProcessing = true };
        ServerToClient = new WptWebSocketPipe();
        ClientStream = new WptWebSocketClientStream(this);
    }

    public WptWebSocketServer Server { get; }

    public Url Url { get; }

    /// <summary>The bytes written by the browser, read by the handler.</summary>
    public WptWebSocketPipe ClientToServer { get; }

    /// <summary>The bytes written by the handler, read by the browser.</summary>
    public WptWebSocketPipe ServerToClient { get; }

    /// <summary>The stream of the browser (<see cref="DocumentResource.Connection"/>).</summary>
    public WptWebSocketClientStream ClientStream { get; }

    /// <summary>Whether the handler waits on the virtual clock (<c>time.sleep()</c>): the connection is not busy.</summary>
    public bool Sleeping { get; set; }

    /// <summary>
    /// Whether the server sent a Close frame: the connection is busy until one side closes it, as the browser closes it once
    /// the closing handshake is complete (and the server usually closes it first).
    /// </summary>
    public bool ServerSentCloseFrame { get; set; }

    public bool IsClosed => ClientToServer.IsDetached && ServerToClient.IsDetached;

    public bool IsBusy => ServerToClient.IsBusy || (!Sleeping && ClientToServer.IsBusy) || (ServerSentCloseFrame && !ClientToServer.IsDetached && !ServerToClient.IsDetached);

    /// <summary>Writes bytes to the browser (<c>request.connection.write()</c>).</summary>
    public void WriteRaw(ReadOnlySpan<byte> bytes) => ServerToClient.Write(bytes);

    /// <summary>The server closes the TCP connection: the browser reads the end of the stream, and the handler no longer reads.</summary>
    public void CloseServer()
    {
        ServerToClient.Complete();
        ClientToServer.Detach();
    }

    /// <summary>The browser closes the TCP connection.</summary>
    public void CloseClient()
    {
        ClientToServer.Complete();
        ServerToClient.Detach();
    }
}

/// <summary>
/// The bytes of one direction of a connection. The pipe is busy while bytes are waiting to be read, or while its reader has
/// read bytes and has not asked for more (it is processing them), unless the reader is detached (it no longer reads). The
/// end of the stream is processed until the reader detaches.
/// </summary>
internal sealed class WptWebSocketPipe
{
    private readonly Lock _lock = new();
    private readonly Queue<byte> _bytes = new();
    private TaskCompletionSource? _waiter;
    private bool _completed;
    private bool _readerProcessing;
    private volatile bool _detached;

    /// <summary>Whether the reader has read bytes and has not asked for more yet (initially, whether the reader is running).</summary>
    public bool ReaderProcessing
    {
        init => _readerProcessing = value;
    }

    /// <summary>Whether the reader no longer reads (its side of the connection is closed).</summary>
    public bool IsDetached => _detached;

    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return !_detached && (_bytes.Count > 0 || _readerProcessing);
            }
        }
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        TaskCompletionSource? waiter;
        lock (_lock)
        {
            if (_completed || _detached)
                return;

            foreach (var b in bytes)
            {
                _bytes.Enqueue(b);
            }

            waiter = _waiter;
            _waiter = null;
        }

        waiter?.TrySetResult();
    }

    /// <summary>The writer closes its side: the reader reads the end of the stream once the bytes are read.</summary>
    public void Complete()
    {
        TaskCompletionSource? waiter;
        lock (_lock)
        {
            // The reader processes the end of the stream (a waiting reader is woken up asynchronously)
            _completed = true;
            _readerProcessing = !_detached;
            waiter = _waiter;
            _waiter = null;
        }

        waiter?.TrySetResult();
    }

    /// <summary>The reader no longer reads.</summary>
    public void Detach()
    {
        TaskCompletionSource? waiter;
        lock (_lock)
        {
            _detached = true;
            _bytes.Clear();
            waiter = _waiter;
            _waiter = null;
        }

        waiter?.TrySetResult();
    }

    /// <summary>Reads the bytes that are available, or waits for bytes. Returns 0 at the end of the stream.</summary>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_lock)
            {
                if (_bytes.Count > 0 && !_detached)
                {
                    var count = Math.Min(buffer.Length, _bytes.Count);
                    var span = buffer.Span;
                    for (var i = 0; i < count; i++)
                    {
                        span[i] = _bytes.Dequeue();
                    }

                    _readerProcessing = true;
                    return count;
                }

                // The end of the stream is processed by the reader until it detaches (e.g. the browser closes the connection)
                _readerProcessing = _completed && !_detached;
                if (_completed || _detached)
                    return 0;

                _waiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _waiter.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

}

/// <summary>The stream of the browser: it reads the bytes of the server and writes the bytes of the browser.</summary>
internal sealed class WptWebSocketClientStream(WptWebSocketConnection connection) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => connection.ServerToClient.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => connection.ClientToServer.Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer) => connection.ClientToServer.Write(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        connection.ClientToServer.Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        connection.ClientToServer.Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        connection.CloseClient();
        base.Dispose(disposing);
    }
}

/// <summary>
/// The request of a WebSocket handler (the <c>request</c> object of pywebsocket, with its <c>ws_stream</c>): the headers of the
/// handshake, the subprotocols, and the messages (https://github.com/web-platform-tests/wpt/blob/master/tools/third_party/pywebsocket3/pywebsocket3/stream.py).
/// The frames of the browser are masked, the frames of the server are not.
/// </summary>
internal sealed partial class WptWebSocketRequest(WptWebSocketConnection connection, Url url, List<KeyValuePair<string, string>> headers, bool isSecure)
{
    private readonly List<byte[]> _receivedFragments = [];
    private int _originalOpcode;

    public WptWebSocketConnection Connection => connection;

    public Url Url => url;

    /// <summary>The headers of the request (<c>request.headers_in</c>).</summary>
    public List<KeyValuePair<string, string>> Headers => headers;

    /// <summary><c>request.unparsed_uri</c> and <c>request.uri</c>: the path and the query.</summary>
    public string Uri => url.Pathname + url.Search;

    /// <summary><c>request.is_https()</c></summary>
    public bool IsHttps => isSecure;

    /// <summary><c>request.ws_origin</c></summary>
    public string? WsOrigin => Header("Origin");

    /// <summary><c>request.ws_requested_protocols</c>: the subprotocols of the request, or null.</summary>
    public List<string>? WsRequestedProtocols { get; set; }

    /// <summary><c>request.ws_protocol</c>: the subprotocol selected by the handler, or null.</summary>
    public string? WsProtocol { get; set; }

    /// <summary><c>request.extra_headers</c>: the headers added to the response.</summary>
    public List<KeyValuePair<string, string>> ExtraHeaders { get; } = [];

    /// <summary><c>request.ws_close_code</c>: the status code of the Close frame of the browser (1005 when it has none), or null.</summary>
    public int? WsCloseCode { get; private set; }

    /// <summary><c>request.ws_close_reason</c></summary>
    public string? WsCloseReason { get; private set; }

    /// <summary><c>request.client_terminated</c>: a Close frame was received.</summary>
    public bool ClientTerminated { get; private set; }

    /// <summary><c>request.server_terminated</c>: a Close frame was sent.</summary>
    public bool ServerTerminated { get; private set; }

    /// <summary>A value of the handler (e.g. <c>request.ws_cookie</c>).</summary>
    public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    public WptStash Stash => connection.Server.Runner.Stash;

    /// <summary><c>request.headers_in.get(name)</c></summary>
    public string? Header(string name) => HeaderList.Get(headers, name);

    /// <summary>The validation of the request (hybi.py, _validate_request, _check_version, _get_key, _set_protocol).</summary>
    public void ValidateHandshakeRequest()
    {
        if (Header("Upgrade") is not { } upgrade || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
            throw new WptWebSocketHandshakeException("Invalid Upgrade header");

        if (Header("Connection") is not { } connectionHeader || !connectionHeader.Split(',').Any(token => token.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase)))
            throw new WptWebSocketHandshakeException("Invalid Connection header");

        if (Header("Host") is null)
            throw new WptWebSocketHandshakeException("No Host header");

        if (Header("Sec-WebSocket-Version") != "13")
            throw new WptWebSocketHandshakeException("Unsupported version");

        if (Header("Sec-WebSocket-Key") is not { } key || !KeyRegex().IsMatch(key))
            throw new WptWebSocketHandshakeException("Invalid key");

        WsRequestedProtocols = Header("Sec-WebSocket-Protocol") is { } protocols ? ParseTokenList(protocols) : null;
    }

    /// <summary>The validation of the subprotocol selected by the handler (base.py, do_handshake).</summary>
    public void ValidateSelectedProtocol()
    {
        if (WsRequestedProtocols is not null)
        {
            if (WsProtocol is null)
                throw new WptWebSocketHandshakeException("do_extra_handshake must choose one subprotocol from ws_requested_protocols and set it to ws_protocol");

            if (!FetchHeaders.IsToken(WsProtocol))
                throw new WptWebSocketHandshakeException("Invalid subprotocol name: " + WsProtocol);
        }
        else if (WsProtocol is not null)
        {
            throw new WptWebSocketHandshakeException("ws_protocol must be None when the client didn't request any subprotocol");
        }
    }

    /// <summary>base.py, parse_token_list</summary>
    private static List<string> ParseTokenList(string value)
    {
        var tokens = new List<string>();
        foreach (var part in value.Split(','))
        {
            var token = part.Trim(' ', '\t');
            if (token.Length == 0)
                continue;

            if (!FetchHeaders.IsToken(token))
                throw new WptWebSocketHandshakeException("Invalid token list: " + value);

            tokens.Add(token);
        }

        if (tokens.Count == 0)
            throw new WptWebSocketHandshakeException("No valid token found");

        return tokens;
    }

    /// <summary><c>time.sleep(seconds)</c>, measured by the virtual clock: the connection is not busy meanwhile.</summary>
    public Task SleepAsync(double seconds)
    {
        // The timer is queued before the connection is idle, so that the runner advances the clock to it
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Server.Runner.QueueTimer(TimeSpan.FromSeconds(seconds), () =>
        {
            // The handler is running again before the runner checks whether the connection is busy
            connection.Sleeping = false;
            completion.TrySetResult();
        });
        connection.Sleeping = true;
        return completion.Task;
    }

    /// <summary><c>request.connection.write(bytes)</c></summary>
    public void Write(ReadOnlySpan<byte> bytes) => connection.WriteRaw(bytes);

    /// <summary><c>request.connection.read(length)</c>: reads exactly <paramref name="length"/> bytes.</summary>
    public async Task<byte[]> ReadAsync(int length)
    {
        var result = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await connection.ClientToServer.ReadAsync(result.AsMemory(offset), CancellationToken.None).ConfigureAwait(false);
            if (read == 0)
                throw new WptWebSocketConnectionTerminatedException();

            offset += read;
        }

        return result;
    }

    /// <summary><c>ws_stream.send_message(message, binary=...)</c>: a string is a text message (unless <paramref name="binary"/>), bytes are a binary message.</summary>
    public void SendMessage(object message, bool? binary = null)
    {
        if (ServerTerminated)
            throw new WptWebSocketBadOperationException("Requested send_message after sending out a closing handshake");

        var payload = message switch
        {
            string text => Encoding.UTF8.GetBytes(text),
            byte[] bytes => bytes,
            _ => throw new WptWebSocketBadOperationException("Message must be an instance of str or bytes"),
        };

        var isBinary = binary ?? message is byte[];
        WriteFrame(isBinary ? 0x2 : 0x1, payload);
    }

    /// <summary><c>ws_stream.send_ping(body)</c></summary>
    public void SendPing(string body) => WriteFrame(0x9, Encoding.UTF8.GetBytes(body));

    /// <summary>
    /// <c>ws_stream.receive_message()</c>: the next text (a string) or binary (bytes) message, or null when a Close frame
    /// is received (it is acknowledged with the result of the passive closing handshake handler). The pings are answered.
    /// </summary>
    public async Task<object?> ReceiveMessageAsync()
    {
        if (ClientTerminated)
            throw new WptWebSocketBadOperationException("Requested receive_message after receiving a closing handshake");

        while (true)
        {
            var (fin, rsv, opcode, payload) = await ReceiveFrameAsync().ConfigureAwait(false);
            var isControl = opcode >= 0x8;
            if (isControl && payload.Length > 125)
                throw new WptWebSocketInvalidFrameException("Payload data size of control frames must be 125 bytes or less");

            if (rsv != 0)
                throw new WptWebSocketUnsupportedFrameException("Unsupported flag is set");

            byte[]? message;
            if (opcode == 0x0)
            {
                if (_receivedFragments.Count == 0)
                    throw new WptWebSocketInvalidFrameException("Received a continuation frame but fragmentation not started");

                _receivedFragments.Add(payload);
                if (!fin)
                    continue;

                message = [.. _receivedFragments.SelectMany(fragment => fragment)];
                _receivedFragments.Clear();
            }
            else
            {
                if (_receivedFragments.Count > 0)
                    throw new WptWebSocketInvalidFrameException("New fragmentation started without terminating existing fragmentation");

                _originalOpcode = opcode;
                if (!fin)
                {
                    if (isControl)
                        throw new WptWebSocketInvalidFrameException("Control frames must not be fragmented");

                    _receivedFragments.Add(payload);
                    continue;
                }

                message = payload;
            }

            switch (_originalOpcode)
            {
                case 0x1:
                    try
                    {
                        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(message);
                    }
                    catch (DecoderFallbackException)
                    {
                        throw new WptWebSocketInvalidUtf8Exception();
                    }

                case 0x2:
                    return message;
                case 0x8:
                    await ProcessCloseMessageAsync(message).ConfigureAwait(false);
                    return null;
                case 0x9:
                    WriteFrame(0xA, message);
                    break;
                case 0xA:
                    break;
                default:
                    throw new WptWebSocketUnsupportedFrameException("Opcode " + _originalOpcode.ToString(CultureInfo.InvariantCulture) + " is not supported");
            }
        }
    }

    /// <summary>stream.py, _process_close_message</summary>
    private async Task ProcessCloseMessageAsync(byte[] message)
    {
        ClientTerminated = true;
        if (message.Length == 0)
        {
            WsCloseCode = 1005;
        }
        else if (message.Length == 1)
        {
            throw new WptWebSocketInvalidFrameException("If a close frame has status code, the length of status code must be 2 octet");
        }
        else
        {
            WsCloseCode = BinaryPrimitives.ReadUInt16BigEndian(message);
            WsCloseReason = Encoding.UTF8.GetString(message.AsSpan(2));
        }

        // The acknowledgment of a closing handshake initiated by the server
        if (ServerTerminated)
            return;

        var handler = WptWebSocketHandlers.Get(System.Uri.UnescapeDataString(url.Pathname));
        var (code, reason) = handler?.PassiveClosingHandshake is { } passiveClosingHandshake ? await passiveClosingHandshake(this).ConfigureAwait(false) : (1000, "");
        if (code is null)
        {
            reason = "";
        }

        SendClosingHandshake(code, reason ?? "");
    }

    /// <summary>
    /// <c>ws_stream.close_connection(code, reason)</c>: sends a Close frame (without body when <paramref name="code"/> is
    /// null), and waits for the Close frame of the browser, except for the <c>1001</c> and <c>1002</c> status codes.
    /// </summary>
    public async Task CloseConnectionAsync(int? code = 1000, string reason = "")
    {
        if (ServerTerminated)
            return;

        SendClosingHandshake(code, code is null ? "" : reason);
        if (code is 1001 or 1002)
            return;

        if (await ReceiveMessageAsync().ConfigureAwait(false) is not null)
            throw new WptWebSocketConnectionTerminatedException();
    }

    private void SendClosingHandshake(int? code, string reason)
    {
        byte[] body = [];
        if (code is { } value)
        {
            if (value is < 1000 or > 4999 or 1005 or 1006 or 1015)
                throw new WptWebSocketBadOperationException("Status code is out of range or reserved");

            body = new byte[2 + Encoding.UTF8.GetByteCount(reason)];
            BinaryPrimitives.WriteUInt16BigEndian(body, (ushort)value);
            Encoding.UTF8.GetBytes(reason, body.AsSpan(2));
        }

        ServerTerminated = true;
        WriteFrame(0x8, body);
    }

    /// <summary>Writes an unmasked frame (https://www.rfc-editor.org/rfc/rfc6455#section-5.2).</summary>
    public void WriteFrame(int opcode, ReadOnlySpan<byte> payload)
    {
        var header = new List<byte> { (byte)(0x80 | opcode) };
        if (payload.Length < 126)
        {
            header.Add((byte)payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            header.Add(126);
            header.Add((byte)(payload.Length >> 8));
            header.Add((byte)payload.Length);
        }
        else
        {
            header.Add(127);
            for (var shift = 56; shift >= 0; shift -= 8)
            {
                header.Add((byte)((long)payload.Length >> shift));
            }
        }

        if (opcode == 0x8)
        {
            connection.ServerSentCloseFrame = true;
        }

        connection.WriteRaw([.. header, .. payload]);
    }

    /// <summary>Reads a frame of the browser (stream.py, parse_frame): the frames of a client must be masked.</summary>
    private async Task<(bool Fin, int Rsv, int Opcode, byte[] Payload)> ReceiveFrameAsync()
    {
        var header = await ReadAsync(2).ConfigureAwait(false);
        var fin = (header[0] & 0x80) != 0;
        var rsv = (header[0] >> 4) & 0x7;
        var opcode = header[0] & 0xF;
        var masked = (header[1] & 0x80) != 0;
        long length = header[1] & 0x7F;
        if (length == 126)
        {
            length = BinaryPrimitives.ReadUInt16BigEndian(await ReadAsync(2).ConfigureAwait(false));
        }
        else if (length == 127)
        {
            var extended = BinaryPrimitives.ReadUInt64BigEndian(await ReadAsync(8).ConfigureAwait(false));
            if (extended >= 1UL << 63)
                throw new WptWebSocketInvalidFrameException("Extended payload length >= 2^63");

            length = (long)extended;
        }

        if (!masked)
            throw new WptWebSocketInvalidFrameException("The client frames must be masked");

        var mask = await ReadAsync(4).ConfigureAwait(false);
        var payload = await ReadAsync(checked((int)length)).ConfigureAwait(false);
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] ^= mask[i % 4];
        }

        return (fin, rsv, opcode, payload);
    }

    [GeneratedRegex("^[+/0-9A-Za-z]{21}[AQgw]==$")]
    private static partial Regex KeyRegex();
}

#pragma warning disable CA1032 // The exceptions of the handlers are only created by the emulated server

/// <summary>The exceptions of pywebsocket (msgutil.py, stream.py, handshake/base.py).</summary>
internal class WptWebSocketException(string message) : Exception(message);

/// <summary><c>AbortedByUserException</c>: the connection is closed without closing handshake.</summary>
internal sealed class WptWebSocketAbortException() : WptWebSocketException("Abort the connection");

/// <summary><c>HandshakeException</c>: the handshake fails with an HTTP error.</summary>
internal sealed class WptWebSocketHandshakeException(string message, int status = 400) : WptWebSocketException(message)
{
    public int Status => status;
}

internal sealed class WptWebSocketBadOperationException(string message) : WptWebSocketException(message);

internal sealed class WptWebSocketInvalidFrameException(string message) : WptWebSocketException(message);

internal sealed class WptWebSocketUnsupportedFrameException(string message) : WptWebSocketException(message);

internal sealed class WptWebSocketInvalidUtf8Exception() : WptWebSocketException("Invalid UTF-8");

internal sealed class WptWebSocketConnectionTerminatedException() : WptWebSocketException("The connection is terminated");

#pragma warning restore CA1032
