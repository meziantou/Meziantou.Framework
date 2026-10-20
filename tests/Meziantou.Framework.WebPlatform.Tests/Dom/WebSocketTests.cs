using System.Net;
using System.Net.Sockets;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using NetWebSocket = System.Net.WebSockets.WebSocket;
using WebSocketCloseStatus = System.Net.WebSockets.WebSocketCloseStatus;
using WebSocketMessageType = System.Net.WebSockets.WebSocketMessageType;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The WebSocket interface (https://websockets.spec.whatwg.org/), with a loopback server and with a stub loader.</summary>
public sealed class WebSocketTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private BrowsingContext CreateContext(DocumentLoader loader, CookieJar? cookieJar = null) => _contexts.Create(new BrowsingContextOptions
    {
        DocumentLoader = loader,
        ScriptEngine = _engine,
        CookieJar = cookieJar,
        UnhandledScriptError = (_, error) => _errors.Add(error),
    });

    private Document Load(BrowsingContext context, string html, string url = "http://example.com/")
    {
        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>Runs the tasks of the event loop until the condition is true (the messages of an open WebSocket are not awaited by RunUntilIdleAsync).</summary>
    private async Task WaitUntilAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            _contexts.EventLoop.RunUntilIdle();
            if (condition())
                return;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(describe());

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task WebSocket_ExchangesMessagesWithAServer()
    {
        using var server = new LoopbackServer();
        var cookies = new CookieJar();
        cookies.SetCookie("http://127.0.0.1/", "session=abc");
        var context = CreateContext(DocumentLoader.Create(), cookies);
        var serverTask = Task.Run(async () =>
        {
            var (socket, request) = await server.AcceptAsync(protocol: "chat");
            using (socket)
            {
                // Echo a text message, then a binary message twice
                var text = await ReceiveAsync(socket);
                await socket.SendAsync(Encoding.UTF8.GetBytes("echo:" + Encoding.UTF8.GetString(text.Data)), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                var binary = await ReceiveAsync(socket);
                await socket.SendAsync(binary.Data, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
                await socket.SendAsync(binary.Data, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);

                // The server closes the connection
                await socket.CloseOutputAsync((WebSocketCloseStatus)4000, "bye", CancellationToken.None);
                await ReceiveAsync(socket);
                return request;
            }
        }, TestContext.Current.CancellationToken);

        var document = Load(context, $$"""
            <script>
              const ws = new WebSocket('ws://127.0.0.1:{{server.Port}}/path?q', ['chat', 'other']);
              const log = [];
              document.documentElement.setAttribute('data-initial', ws.url + '|' + ws.readyState + '|' + ws.binaryType);
              ws.onopen = () => {
                log.push('open:' + ws.readyState + ':' + ws.protocol + ':' + ws.extensions);
                ws.send('hello');
                ws.send(new Uint8Array([1, 2, 3]));
                log.push('buffered:' + ws.bufferedAmount);
              };
              let binaryMessages = 0;
              ws.onmessage = e => {
                if (typeof e.data === 'string') {
                  log.push('text:' + e.data + ':' + e.origin);
                } else if (binaryMessages++ === 0) {
                  log.push('blob:' + (e.data instanceof Blob) + ':' + e.data.size);
                  ws.binaryType = 'arraybuffer';
                } else {
                  log.push('arraybuffer:' + new Uint8Array(e.data).join(','));
                }
              };
              ws.onerror = () => log.push('error');
              ws.onclose = e => {
                log.push('close:' + e.code + ':' + e.reason + ':' + e.wasClean + ':' + ws.readyState + ':' + ws.bufferedAmount);
                document.title = log.join('|');
              };
            </script>
            """, "http://127.0.0.1/");

        // The state when the constructor returns (the loopback server can complete the exchange before the end of Load)
        Assert.Equal($"ws://127.0.0.1:{server.Port}/path?q|0|blob", document.DocumentElement!.GetAttribute("data-initial"));

        await WaitUntilAsync(() => document.Title.Contains("close:", StringComparison.Ordinal), () => document.Title);
        Assert.Equal($"open:1:chat:|buffered:8|text:echo:hello:ws://127.0.0.1:{server.Port}|blob:true:3|arraybuffer:1,2,3|close:4000:bye:true:3:0", document.Title);

        // The opening handshake: an HTTP/1.1 upgrade request with the headers of the fetch algorithm
        var request = await serverTask;
        Assert.Equal("GET /path?q HTTP/1.1", request.RequestLine);
        Assert.Equal("websocket", request.Headers["Upgrade"]);
        Assert.Equal("Upgrade", request.Headers["Connection"]);
        Assert.Equal("13", request.Headers["Sec-WebSocket-Version"]);
        Assert.Equal("chat, other", request.Headers["Sec-WebSocket-Protocol"]);
        Assert.Equal("permessage-deflate; client_max_window_bits", request.Headers["Sec-WebSocket-Extensions"]);
        Assert.Equal("http://127.0.0.1", request.Headers["Origin"]);
        Assert.Equal("session=abc", request.Headers["Cookie"]);
        Assert.Equal(16, Convert.FromBase64String(request.Headers["Sec-WebSocket-Key"]).Length);
        Assert.False(request.Headers.ContainsKey("Referer"));
    }

    [Theory]
    [InlineData("ws.close()", null, null, 1005)]
    [InlineData("ws.close(1000)", 1000, "", 1000)]
    [InlineData("ws.close(3001, 'done')", 3001, "done", 3001)]
    public async Task Close_SendsACloseFrame(string close, int? expectedCode, string? expectedReason, int expectedEventCode)
    {
        using var server = new LoopbackServer();
        var context = CreateContext(DocumentLoader.Create());
        var serverTask = Task.Run(async () =>
        {
            var (socket, _) = await server.AcceptAsync();
            using (socket)
            {
                // The server echoes the Close frame (without status code when it has none)
                var message = await ReceiveAsync(socket);
                Assert.Equal(WebSocketMessageType.Close, message.Type);
                var hasStatusCode = server.Frames!.LastCloseFrameHasStatusCode;
                var observed = (socket.CloseStatus, socket.CloseStatusDescription);
                if (hasStatusCode)
                {
                    await socket.CloseOutputAsync(socket.CloseStatus!.Value, socket.CloseStatusDescription, CancellationToken.None);
                }
                else
                {
                    // A Close frame without body (NetWebSocket would send the 1005 status code for WebSocketCloseStatus.Empty)
                    await server.Frames.WriteAsync(new byte[] { 0x88, 0x00 });
                }

                return (hasStatusCode, observed.CloseStatus, observed.CloseStatusDescription);
            }
        }, TestContext.Current.CancellationToken);

        var document = Load(context, $$"""
            <script>
              const ws = new WebSocket('ws://127.0.0.1:{{server.Port}}/');
              ws.onopen = () => { {{close}}; document.title = 'closing:' + ws.readyState; };
              ws.onclose = e => document.title += '|close:' + e.code + ':' + e.reason + ':' + e.wasClean + ':' + ws.readyState;
            </script>
            """);

        await WaitUntilAsync(() => document.Title.Contains("close:", StringComparison.Ordinal), () => document.Title);
        Assert.Equal($"closing:2|close:{expectedEventCode}:{expectedReason}:true:3", document.Title);
        var (hasStatusCode, status, description) = await serverTask;
        Assert.Equal(expectedCode is not null, hasStatusCode);
        if (expectedCode is not null)
        {
            Assert.Equal(expectedCode, (int)status!.Value);
            Assert.Equal(expectedReason, description);
        }
    }

    [Fact]
    public async Task PermessageDeflate_IsNegotiated()
    {
        using var server = new LoopbackServer();
        var context = CreateContext(DocumentLoader.Create());
        var serverTask = Task.Run(async () =>
        {
            var (socket, _) = await server.AcceptAsync(deflate: true);
            using (socket)
            {
                var message = await ReceiveAsync(socket);
                await socket.SendAsync(message.Data.Concat(message.Data).ToArray(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                await ReceiveAsync(socket);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }, TestContext.Current.CancellationToken);

        var document = Load(context, $$"""
            <script>
              const ws = new WebSocket('ws://127.0.0.1:{{server.Port}}/');
              ws.onopen = () => ws.send('abc'.repeat(1000));
              ws.onmessage = e => { document.title = ws.extensions + '|' + e.data.length; ws.close(); };
            </script>
            """);

        await WaitUntilAsync(() => document.Title.Contains('|', StringComparison.Ordinal), () => document.Title);
        Assert.Equal("permessage-deflate; client_max_window_bits=12|6000", document.Title);
        await serverTask;
    }

    [Fact]
    public void Constructor_ValidatesTheUrlAndTheProtocols()
    {
        var context = CreateContext(new StubLoader());
        var document = Load(context, """
            <script>
              const results = [];
              function test(f) {
                try { results.push(f()); } catch (e) { results.push(e.name); }
              }
              test(() => new WebSocket('http://example.com/a').url);
              test(() => new WebSocket('https://example.com/b').url);
              test(() => new WebSocket('/relative').url);
              test(() => new WebSocket('ftp://example.com/'));
              test(() => new WebSocket('ws://example.com/#fragment'));
              test(() => new WebSocket('ws://exa mple.com/'));
              test(() => new WebSocket('ws://example.com/', ['a', 'A']));
              test(() => new WebSocket('ws://example.com/', 'a b'));
              test(() => new WebSocket('ws://example.com/', ''));
              test(() => new WebSocket('ws://example.com/', ['a', 'b']).protocol);
              document.title = results.join('|');
            </script>
            """);
        Assert.Equal("ws://example.com/a|wss://example.com/b|ws://example.com/relative|SyntaxError|SyntaxError|SyntaxError|SyntaxError|SyntaxError|SyntaxError|", document.Title);
    }

    [Fact]
    public void Handshake_IsFetchedWithTheWebSocketMode()
    {
        var loader = new StubLoader();
        var context = CreateContext(loader);
        Load(context, """<script>new WebSocket('wss://example.com:8443/socket?x', 'chat');</script>""", "https://example.com/page");

        var request = Assert.Single(loader.WebSocketRequests);
        Assert.Equal("https://example.com:8443/socket?x", request.Url);
        Assert.Equal(RequestMode.WebSocket, request.Mode);
        Assert.Equal(RequestDestination.Empty, request.Destination);
        Assert.Null(request.Referrer);
        Assert.False(request.FollowRedirects);
        Assert.True(request.IncludeCredentials);
        Assert.Equal("https://example.com", request.Origin);
        Assert.Contains(new KeyValuePair<string, string>("Origin", "https://example.com"), request.Headers);
        Assert.Contains(new KeyValuePair<string, string>("Sec-WebSocket-Protocol", "chat"), request.Headers);
        Assert.Contains(new KeyValuePair<string, string>("Sec-Fetch-Mode", "websocket"), request.Headers);
        Assert.Contains(new KeyValuePair<string, string>("Cache-Control", "no-cache"), request.Headers);
        Assert.DoesNotContain(request.Headers, header => header.Key == "Referer");
    }

    [Theory]
    [InlineData("valid", "open|close:1006:false")]
    [InlineData("status", "error|close:1006:false")]
    [InlineData("accept", "error|close:1006:false")]
    [InlineData("upgrade", "error|close:1006:false")]
    [InlineData("connection", "error|close:1006:false")]
    [InlineData("protocol", "error|close:1006:false")]
    [InlineData("no-protocol", "error|close:1006:false")]
    [InlineData("extension", "error|close:1006:false")]
    [InlineData("deflate-parameter", "error|close:1006:false")]
    [InlineData("network-error", "error|close:1006:false")]
    public void Handshake_ValidatesTheResponse(string variant, string expected)
    {
        var loader = new StubLoader
        {
            Respond = request =>
            {
                if (variant == "network-error")
                    return null;

                var key = request.Headers.Single(header => header.Key == "Sec-WebSocket-Key").Value;
                var headers = new List<KeyValuePair<string, string>>
                {
                    new("Upgrade", variant == "upgrade" ? "h2c" : "WebSocket"),
                    new("Connection", variant == "connection" ? "keep-alive" : "keep-alive, Upgrade"),
                    new("Sec-WebSocket-Accept", variant == "accept" ? "invalid" : WebSocketHandshake.ComputeAccept(key)),
                };

                if (variant != "no-protocol")
                {
                    headers.Add(new("Sec-WebSocket-Protocol", variant == "protocol" ? "other" : "chat"));
                }

                if (variant is "extension" or "deflate-parameter")
                {
                    headers.Add(new("Sec-WebSocket-Extensions", variant == "extension" ? "x-unknown" : "permessage-deflate; client_max_window_bits=20"));
                }

                return new DocumentResource(ReadOnlyMemory<byte>.Empty) { StatusCode = variant == "status" ? 200 : 101, Headers = headers, Connection = new MemoryStream() };
            },
        };

        var context = CreateContext(loader);
        var document = Load(context, """
            <script>
              const log = [];
              const ws = new WebSocket('ws://example.com/', 'chat');
              ws.onopen = () => { log.push('open'); document.title = log.join('|'); };
              ws.onerror = () => log.push('error');
              ws.onclose = e => { log.push('close:' + e.code + ':' + e.wasClean); document.title = log.join('|'); };
            </script>
            """);
        Assert.Equal(expected, document.Title);
    }

    [Fact]
    public void CloseAndSend_ValidateTheirArguments()
    {
        var context = CreateContext(new StubLoader());
        var document = Load(context, """
            <script>
              const results = [];
              function test(f) {
                try { f(); results.push('ok'); } catch (e) { results.push(e.name); }
              }
              const ws = new WebSocket('ws://example.com/');
              test(() => ws.send('data'));
              test(() => ws.close(1001));
              test(() => ws.close(2999));
              test(() => ws.close(5000));
              test(() => ws.close(1000, 'a'.repeat(124)));
              test(() => ws.close(1000, 'a'.repeat(123)));
              results.push(ws.readyState);
              test(() => ws.close());
              ws.onclose = e => {
                results.push('close:' + e.code + ':' + e.wasClean);
                test(() => ws.send('after'));
                results.push(ws.bufferedAmount);
                document.title = results.join('|');
              };
            </script>
            """);

        // close() while connecting fails the connection
        Assert.Equal("InvalidStateError|InvalidAccessError|InvalidAccessError|InvalidAccessError|SyntaxError|ok|2|ok|close:1006:false|ok|5", document.Title);
    }

    [Fact]
    public void MixedContent_FailsTheConnection()
    {
        var loader = new StubLoader();
        var context = CreateContext(loader);
        var document = Load(context, """
            <script>
              const ws = new WebSocket('ws://example.com/');
              ws.onerror = () => document.title = 'error';
              ws.onclose = e => document.title += '|' + e.code;
            </script>
            """, "https://example.com/");
        Assert.Equal("error|1006", document.Title);
        Assert.Empty(loader.WebSocketRequests);
    }

    [Fact]
    public async Task Unload_ClosesTheConnectionWithGoingAway()
    {
        using var server = new LoopbackServer();
        var context = CreateContext(DocumentLoader.Create());
        var serverTask = Task.Run(async () =>
        {
            var (socket, _) = await server.AcceptAsync();
            using (socket)
            {
                var message = await ReceiveAsync(socket);
                return (message.Type, socket.CloseStatus);
            }
        }, TestContext.Current.CancellationToken);

        var document = Load(context, $$"""
            <script>
              const ws = new WebSocket('ws://127.0.0.1:{{server.Port}}/');
              ws.onopen = () => document.title = 'open';
            </script>
            """);
        await WaitUntilAsync(() => document.Title == "open", () => document.Title);

        context.Navigate("http://example.com/other", _ => "");
        _contexts.EventLoop.RunUntilIdle();
        var (type, status) = await serverTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(WebSocketMessageType.Close, type);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, status);
    }

    private static async Task<(WebSocketMessageType Type, byte[] Data)> ReceiveAsync(NetWebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var data = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            data.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return (result.MessageType, data.ToArray());
        }
    }

    /// <summary>A WebSocket server on the loopback interface, which accepts one connection.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<IDisposable> _resources = [];

        public LoopbackServer()
        {
            _listener.Start();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>Observes the Close frames of the client (<see cref="NetWebSocket"/> reports a Close frame without status code as a normal closure).</summary>
        public WebSocketFrameObserver? Frames { get; private set; }

        public async Task<(NetWebSocket Socket, HandshakeRequest Request)> AcceptAsync(string? protocol = null, bool deflate = false)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _resources.Add(client);
            var stream = client.GetStream();

            // The request line and the headers, then the response
            var head = new List<byte>();
            var buffer = new byte[1];
            while (head.Count < 4 || !head.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
            {
                if (await stream.ReadAsync(buffer) == 0)
                    throw new IOException("The connection was closed during the handshake");

                head.Add(buffer[0]);
            }

            var lines = Encoding.Latin1.GetString([.. head]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var headers = lines.Skip(1).Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var accept = WebSocketHandshake.ComputeAccept(headers["Sec-WebSocket-Key"]);
            var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n";
            if (protocol is not null)
            {
                response += "Sec-WebSocket-Protocol: " + protocol + "\r\n";
            }

            if (deflate)
            {
                response += "Sec-WebSocket-Extensions: permessage-deflate; client_max_window_bits=12\r\n";
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(response + "\r\n"));
            Frames = new WebSocketFrameObserver(stream);
            var socket = NetWebSocket.CreateFromStream(Frames, new System.Net.WebSockets.WebSocketCreationOptions
            {
                IsServer = true,
                SubProtocol = protocol,
                KeepAliveInterval = TimeSpan.Zero,
                DangerousDeflateOptions = deflate ? new System.Net.WebSockets.WebSocketDeflateOptions { ClientMaxWindowBits = 12 } : null,
            });
            return (socket, new HandshakeRequest(lines[0], headers));
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Dispose();
            foreach (var resource in _resources)
            {
                resource.Dispose();
            }
        }
    }

    private sealed record HandshakeRequest(string RequestLine, Dictionary<string, string> Headers);

    /// <summary>A loader that records the opening handshakes, and answers them with <see cref="Respond"/> (a network error by default).</summary>
    private sealed class StubLoader : DocumentLoader
    {
        public List<DocumentRequest> WebSocketRequests { get; } = [];

        public Func<DocumentRequest, DocumentResource?>? Respond { get; init; }

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<DocumentResource?>(null);

        public override ValueTask<DocumentResource?> ConnectWebSocketAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            WebSocketRequests.Add(request);
            return ValueTask.FromResult(Respond?.Invoke(request));
        }
    }
}
