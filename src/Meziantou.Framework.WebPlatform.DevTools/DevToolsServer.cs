using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Transport;

namespace Meziantou.Framework.WebPlatform.DevTools;

/// <summary>
/// A Chrome DevTools Protocol (CDP) server whose browser is the managed browser engine of Meziantou.Framework.WebPlatform: the
/// CDP clients see a headless Chromium browser. Playwright connects to <see cref="HttpEndpoint"/> with
/// <c>BrowserType.ConnectOverCDPAsync</c> (<c>chromium.connectOverCDP()</c> in Node.js), Puppeteer to
/// <see cref="WebSocketEndpoint"/> with <c>puppeteer.connect({ browserWSEndpoint })</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each connection gets its own browser: its default browser context and the browser contexts it creates
/// (<c>Target.createBrowserContext</c>). Each browser context runs its pages on its own thread, with its own script engine,
/// cookies and storage. The browser contexts are closed when the client disconnects.
/// </para>
/// <para>
/// The scripts of the pages run in the process (see the threat model of the script engine), and the documents are fetched by
/// <see cref="DevToolsServerOptions.DocumentLoader"/>: expose the server to trusted clients only.
/// </para>
/// </remarks>
public sealed class DevToolsServer : IAsyncDisposable
{
    /// <summary>The version of Chromium reported to the clients (<c>Browser.getVersion</c>).</summary>
    public const string ChromiumVersion = "146.0.0.0";

    /// <summary>The version of the protocol reported to the clients.</summary>
    public const string ProtocolVersion = "1.3";

    internal const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/" + ChromiumVersion + " Safari/537.36";

    private readonly DevToolsServerOptions _options;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _connections = [];
    private readonly SemaphoreSlim? _connectionSlots;
    private readonly string _browserId = CdpConnection.CreateId().ToLowerInvariant();
    private TcpListener? _listener;
    private Task? _acceptLoop;

    /// <summary>Creates a server. Call <see cref="StartAsync"/> to listen.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public DevToolsServer(DevToolsServerOptions? options = null)
    {
        _options = options ?? new DevToolsServerOptions();
        if (_options.MaxConnections is { } maxConnections)
        {
            _connectionSlots = new SemaphoreSlim(maxConnections, maxConnections);
        }
    }

    /// <summary>Gets the TCP port the server listens on, once it is started.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Gets the HTTP endpoint of the server, once it is started (e.g. <c>http://127.0.0.1:9222/</c>), to give to
    /// <c>BrowserType.ConnectOverCDPAsync</c>: the clients read the WebSocket endpoint from <c>/json/version</c>.
    /// </summary>
    public Uri HttpEndpoint => new("http://" + GetHost() + "/");

    /// <summary>
    /// Gets the WebSocket endpoint of the browser, once the server is started (e.g. <c>ws://127.0.0.1:9222/devtools/browser/&lt;id&gt;</c>),
    /// the <c>webSocketDebuggerUrl</c> of <c>/json/version</c>. Its path is random: the clients that do not know it cannot connect.
    /// </summary>
    public Uri WebSocketEndpoint => new("ws://" + GetHost() + BrowserPath);

    private string BrowserPath => "/devtools/browser/" + _browserId;

    private string GetHost()
    {
        if (_listener is null)
            throw new InvalidOperationException("The server is not started");

        var address = _options.BindAddress;
        var host = address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? "localhost"
            : address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + address + "]"
            : address.ToString();
        return host + ":" + Port.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Starts listening for the connections of the clients.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_listener is not null)
            throw new InvalidOperationException("The server is already started");

        var listener = new TcpListener(_options.BindAddress, _options.Port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _acceptLoop = AcceptConnectionsAsync(listener);
        return Task.CompletedTask;
    }

    private async Task AcceptConnectionsAsync(TcpListener listener)
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            var connection = HandleClientAsync(client);
            lock (_connections)
            {
                _connections.RemoveAll(task => task.IsCompleted);
                _connections.Add(connection);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            try
            {
                HttpRequest? request;
                using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token))
                {
                    handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    request = await HttpRequest.ReadAsync(stream, handshakeTimeout.Token).ConfigureAwait(false);
                }

                if (request is null)
                    return;

                // As Chromium, the requests whose Host is a domain name are rejected: a web page cannot reach the server with
                // a DNS rebinding attack
                if (!IsAllowedHost(request.GetHeader("Host")))
                {
                    await HttpRequest.WriteResponseAsync(stream, "500 Internal Server Error", "text/plain", "Host header is specified and is not an IP address or localhost.", _stopping.Token).ConfigureAwait(false);
                    return;
                }

                if (!request.IsWebSocketUpgrade)
                {
                    await WriteHttpResponseAsync(stream, request).ConfigureAwait(false);
                    return;
                }

                if (request.Path != BrowserPath)
                {
                    await HttpRequest.WriteResponseAsync(stream, "404 Not Found", "text/plain", "Not Found", _stopping.Token).ConfigureAwait(false);
                    return;
                }

                if (_connectionSlots is not null)
                {
                    await _connectionSlots.WaitAsync(_stopping.Token).ConfigureAwait(false);
                }

                try
                {
                    var accept = HttpRequest.ComputeWebSocketAccept(request.GetHeader("Sec-WebSocket-Key")!);
                    var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stopping.Token).ConfigureAwait(false);
                    using var webSocket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(30) });
                    await RunConnectionAsync(webSocket, _stopping.Token).ConfigureAwait(false);
                }
                finally
                {
                    _connectionSlots?.Release();
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or WebSocketException)
            {
            }
        }
    }

    /// <summary>Returns whether the <c>Host</c> header of a request is missing, an IP address or <c>localhost</c> (with an optional port).</summary>
    internal static bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return true;

        var name = host;
        if (name.StartsWith('[', StringComparison.Ordinal))
        {
            var end = name.IndexOf(']', StringComparison.Ordinal);
            if (end < 0)
                return false;

            return IPAddress.TryParse(name.AsSpan(1, end - 1), out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;
        }

        var colon = name.LastIndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            name = name[..colon];
        }

        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(name, out var ipv4) && ipv4.AddressFamily == AddressFamily.InterNetwork);
    }

    /// <summary>The HTTP endpoints of the browser (<c>/json/version</c> and <c>/json/list</c>).</summary>
    private async Task WriteHttpResponseAsync(Stream stream, HttpRequest request)
    {
        if (request.Method is not ("GET" or "PUT"))
        {
            await HttpRequest.WriteResponseAsync(stream, "405 Method Not Allowed", "text/plain", "Method Not Allowed", _stopping.Token).ConfigureAwait(false);
            return;
        }

        var path = request.Path.TrimEnd('/');
        switch (path)
        {
            case "/json/version":
                // The host of the WebSocket endpoint is the one the client used (as Chromium does)
                var host = request.GetHeader("Host") is { Length: > 0 } hostHeader ? hostHeader : GetHost();
                var version = new JsonObject
                {
                    ["Browser"] = "HeadlessChrome/" + ChromiumVersion,
                    ["Protocol-Version"] = ProtocolVersion,
                    ["User-Agent"] = BrowserUserAgent,
                    ["V8-Version"] = "",
                    ["WebKit-Version"] = "537.36",
                    ["webSocketDebuggerUrl"] = "ws://" + host + BrowserPath,
                };
                await HttpRequest.WriteResponseAsync(stream, "200 OK", "application/json; charset=UTF-8", version.ToJsonString(), _stopping.Token).ConfigureAwait(false);
                return;

            case "/json" or "/json/list":
                // Each connection has its own browser: there is no target before a client connects
                await HttpRequest.WriteResponseAsync(stream, "200 OK", "application/json; charset=UTF-8", "[]", _stopping.Token).ConfigureAwait(false);
                return;

            default:
                await HttpRequest.WriteResponseAsync(stream, "404 Not Found", "text/plain", "Not Found", _stopping.Token).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>
    /// Runs the connection of a client on a WebSocket accepted by the host (e.g. by ASP.NET Core), until the client disconnects
    /// or <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    /// <param name="webSocket">The WebSocket of the client.</param>
    /// <param name="cancellationToken">Closes the connection.</param>
    public Task HandleConnectionAsync(WebSocket webSocket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webSocket);
        return RunConnectionAsync(webSocket, cancellationToken);
    }

    private async Task RunConnectionAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        using var connection = new CdpConnection(_options);
        await connection.RunAsync(webSocket, linked.Token).ConfigureAwait(false);
    }

    /// <summary>Stops listening, and closes the connections (their browser contexts are closed).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stopping.IsCancellationRequested)
            return;

        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            await _acceptLoop.ConfigureAwait(false);
        }

        Task[] connections;
        lock (_connections)
        {
            connections = [.. _connections];
        }

        await Task.WhenAll(connections).ConfigureAwait(false);
        _listener?.Dispose();
        _stopping.Dispose();
        _connectionSlots?.Dispose();
    }
}
