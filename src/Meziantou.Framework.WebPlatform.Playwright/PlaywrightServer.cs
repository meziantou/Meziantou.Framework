using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Transport;

namespace Meziantou.Framework.WebPlatform.Playwright;

/// <summary>
/// A Playwright server (as <c>npx playwright run-server</c>) whose browser is the managed browser engine of
/// Meziantou.Framework.WebPlatform: the Playwright clients connect to its WebSocket endpoint with
/// <c>BrowserType.ConnectAsync(wsEndpoint)</c> and automate the pages it hosts in the process.
/// </summary>
/// <remarks>
/// <para>
/// Each connection gets a browser; each browser context of the clients runs its pages on its own thread, with its own
/// script engine, cookies and storage. The protocol of Playwright changes with each minor version: the clients must use the
/// version of <see cref="PlaywrightVersion"/> (see <see cref="PlaywrightServerOptions.AllowAnyClientVersion"/>).
/// </para>
/// <para>
/// The scripts of the pages run in the process (see the threat model of the script engine), and the documents are fetched by
/// <see cref="PlaywrightServerOptions.DocumentLoader"/>: expose the server to trusted clients only.
/// </para>
/// </remarks>
public sealed partial class PlaywrightServer : IAsyncDisposable
{
    private readonly PlaywrightServerOptions _options;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _connections = [];
    private readonly SemaphoreSlim? _connectionSlots;
    private TcpListener? _listener;
    private Task? _acceptLoop;

    /// <summary>Creates a server. Call <see cref="StartAsync"/> to listen.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public PlaywrightServer(PlaywrightServerOptions? options = null)
    {
        _options = options ?? new PlaywrightServerOptions();
        if (!_options.Path.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("The path of the endpoint must start with '/'", nameof(options));

        if (_options.MaxConnections is { } maxConnections)
        {
            _connectionSlots = new SemaphoreSlim(maxConnections, maxConnections);
        }
    }

    /// <summary>Gets the TCP port the server listens on, once it is started.</summary>
    public int Port { get; private set; }

    /// <summary>Gets the WebSocket endpoint of the server, once it is started (e.g. <c>ws://127.0.0.1:3000/</c>), to give to <c>BrowserType.ConnectAsync</c>.</summary>
    public Uri WsEndpoint
    {
        get
        {
            if (_listener is null)
                throw new InvalidOperationException("The server is not started");

            var address = _options.BindAddress;
            var host = address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? "localhost"
                : address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + address + "]"
                : address.ToString();
            return new Uri("ws://" + host + ":" + Port.ToString(CultureInfo.InvariantCulture) + _options.Path);
        }
    }

    /// <summary>Starts listening for the connections of the clients.</summary>
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
                HttpUpgradeRequest? request;
                using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token))
                {
                    handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    request = await HttpUpgradeRequest.ReadAsync(stream, handshakeTimeout.Token).ConfigureAwait(false);
                }

                if (request is null)
                    return;

                if (!request.IsWebSocketUpgrade)
                {
                    if (request.Method == "GET" && request.Path == "/json")
                    {
                        var json = new System.Text.Json.Nodes.JsonObject { ["wsEndpointPath"] = _options.Path }.ToJsonString();
                        await HttpUpgradeRequest.WriteResponseAsync(stream, "200 OK", "application/json", json, _stopping.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await HttpUpgradeRequest.WriteResponseAsync(stream, "200 OK", "text/plain", "Running", _stopping.Token).ConfigureAwait(false);
                    }

                    return;
                }

                if (request.Path != _options.Path)
                {
                    await HttpUpgradeRequest.WriteResponseAsync(stream, "400 Bad Request", "text/plain", "Bad Request", _stopping.Token).ConfigureAwait(false);
                    return;
                }

                var userAgent = request.GetHeader("User-Agent");
                if (!_options.AllowAnyClientVersion && GetVersionMismatchMessage(userAgent) is { } mismatch)
                {
                    await HttpUpgradeRequest.WriteResponseAsync(stream, "428 Precondition Required", "text/plain", mismatch, _stopping.Token).ConfigureAwait(false);
                    return;
                }

                if (_connectionSlots is not null)
                {
                    await _connectionSlots.WaitAsync(_stopping.Token).ConfigureAwait(false);
                }

                try
                {
                    var accept = HttpUpgradeRequest.ComputeWebSocketAccept(request.GetHeader("Sec-WebSocket-Key")!);
                    var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stopping.Token).ConfigureAwait(false);
                    using var webSocket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(30) });
                    await RunConnectionAsync(webSocket, request.GetHeader("x-playwright-browser"), _stopping.Token).ConfigureAwait(false);
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

    /// <summary>
    /// Runs the connection of a client on a WebSocket accepted by the host (e.g. by ASP.NET Core), until the client disconnects
    /// or <paramref name="cancellationToken"/> is canceled. The host checks the version of the client first (see <see cref="IsCompatibleClient"/>).
    /// </summary>
    /// <param name="webSocket">The WebSocket of the client.</param>
    /// <param name="cancellationToken">Closes the connection.</param>
    public Task HandleConnectionAsync(WebSocket webSocket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webSocket);
        return RunConnectionAsync(webSocket, browserName: null, cancellationToken);
    }

    private async Task RunConnectionAsync(WebSocket webSocket, string? browserName, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        using var connection = new PlaywrightConnection(_options) { RequestedBrowserName = browserName };
        await connection.RunAsync(webSocket, linked.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns whether a client can connect, given the <c>User-Agent</c> header of its WebSocket request (e.g.
    /// <c>Playwright/x.y.z (arm64; macOS 26.0) csharp/10.0</c>): its major and minor versions must be the ones of
    /// <see cref="PlaywrightVersion"/>. A user agent that is not the one of a Playwright client is accepted.
    /// </summary>
    /// <param name="userAgent">The value of the <c>User-Agent</c> header, or null.</param>
    public static bool IsCompatibleClient(string? userAgent) => GetVersionMismatchMessage(userAgent) is null;

    private static string? GetVersionMismatchMessage(string? userAgent)
    {
        if (userAgent is null || VersionRegex().Match(userAgent) is not { Success: true } match)
            return null;

        var expected = GetMajorMinor(PlaywrightVersion);
        var received = GetMajorMinor(match.Groups["version"].Value);
        if (received == expected)
            return null;

        return "Playwright version mismatch:\n  - server version: v" + expected + "\n  - client version: v" + received + "\n\nThe Playwright server of Meziantou.Framework.WebPlatform implements the protocol of Playwright " + PlaywrightVersion + ".\n";

        static string GetMajorMinor(string version) => string.Join('.', version.Split('.').Take(2));
    }

    [GeneratedRegex(@"^Playwright/(?<version>\d+\.\d+\.\d+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionRegex();

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
