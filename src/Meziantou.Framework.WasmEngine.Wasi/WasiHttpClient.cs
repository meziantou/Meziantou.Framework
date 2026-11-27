using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// The HTTP client of a WASI component (<c>wasi:http/client</c>, see <see cref="WasiOptions.HttpClient"/>): it sends
/// the requests of the component. <see cref="WasiHostHttpClient"/> sends them over the network of the machine, with the
/// rules of a <see cref="WasiHostHttpClientOptions"/>; <see cref="WasiInMemoryHttpClient"/> gives them to a handler of
/// the process.
/// </summary>
/// <remarks>
/// WASI validates the requests of the component before calling the client (the method, the scheme, the authority, the
/// path, the headers). The content of a request is the body that the component writes, read as it sends it. The content
/// of the response is read by the component as the client receives it; its trailers are
/// <see cref="HttpResponseMessage.TrailingHeaders"/>. An exception reports an error to the component: a
/// <see cref="HttpRequestException"/> (whose inner exception can be a <see cref="SocketException"/>), a
/// <see cref="TimeoutException"/>, or an <see cref="UnauthorizedAccessException"/> for a request that the client
/// denies.
/// </remarks>
public abstract class WasiHttpClient
{
    /// <summary>
    /// Gets the key of the trailers of a request in <see cref="HttpRequestMessage.Options"/>: a task that completes with
    /// the trailers that the component sends after the body (or null), once the content of the request is read. The
    /// clients that cannot send trailers (HTTP/1.1 of .NET) ignore them.
    /// </summary>
    public static HttpRequestOptionsKey<Task<HttpHeaders?>> RequestTrailers { get; } = new("Meziantou.Framework.WasmEngine.Wasi.RequestTrailers");

    /// <summary>The key of the timeout of the connection that the component sets (<c>connect-timeout</c>) in <see cref="HttpRequestMessage.Options"/>.</summary>
    internal static HttpRequestOptionsKey<TimeSpan> ConnectTimeoutOption { get; } = new("Meziantou.Framework.WasmEngine.Wasi.ConnectTimeout");

    /// <summary>Sends a request, and returns the response once its headers are received.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The token that is cancelled when the component cancels the request.</param>
    /// <returns>The response.</returns>
    public abstract Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>Decides whether a component can send a request (see <see cref="WasiHostHttpClientOptions.RequestCheck"/>).</summary>
/// <param name="request">The request: its method, its URI and its headers.</param>
/// <returns>Whether the component can send the request: otherwise it fails with <c>HTTP-request-denied</c>.</returns>
public delegate bool WasiHttpRequestCheck(HttpRequestMessage request);

/// <summary>The rules of a <see cref="WasiHostHttpClient"/>: which requests the component can send over the network of the machine.</summary>
/// <remarks>By default, the component cannot send any request: give it only the access it needs.</remarks>
public sealed class WasiHostHttpClientOptions
{
    /// <summary>Gets a check that allows every request.</summary>
    public static WasiHttpRequestCheck AllowAll { get; } = _ => true;

    /// <summary>Returns a check that allows the requests to some hosts (compared without case), such as <c>api.example.com</c>.</summary>
    /// <param name="hosts">The host names or IP addresses, without port.</param>
    public static WasiHttpRequestCheck AllowHosts(params string[] hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        // The IPv6 addresses are compared without their brackets
        var allowed = new HashSet<string>(hosts.Select(host => host.Trim('[', ']')), StringComparer.OrdinalIgnoreCase);
        return request => request.RequestUri is { } uri && allowed.Contains(uri.Host.Trim('[', ']'));
    }

    /// <summary>Gets the check of the requests, or null to deny every request.</summary>
    public WasiHttpRequestCheck? RequestCheck { get; init; }

    /// <summary>
    /// Gets the check of the IP addresses that the client connects to (<see cref="WasiSocketAddressUse.TcpConnect"/>), or
    /// null to allow every address. Check the addresses to prevent a host name from leading to the machines of the local
    /// network (such as <see cref="WasiHostNetworkOptions.AllowLoopback"/>, or a check that denies the private addresses).
    /// </summary>
    public WasiSocketAddressCheck? AddressCheck { get; init; }

    /// <summary>Gets the timeout of the connections when the request does not set one (<c>connect-timeout</c>). The default is 30 seconds.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// An HTTP client that sends the requests over the network of the machine (<see cref="SocketsHttpHandler"/>), with the
/// rules of a <see cref="WasiHostHttpClientOptions"/>.
/// </summary>
/// <remarks>
/// The client does not follow the redirections, does not decompress the contents, does not keep cookies and does not use
/// a proxy: the component receives the responses of the servers as they are. It is not supported on the browser.
/// </remarks>
[UnsupportedOSPlatform("browser")]
public sealed class WasiHostHttpClient : WasiHttpClient, IDisposable
{
    private readonly WasiHostHttpClientOptions _options;
    private readonly HttpMessageInvoker _invoker;

    /// <summary>Initializes a new instance of the <see cref="WasiHostHttpClient"/> class.</summary>
    /// <param name="options">The rules of the client.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The invoker disposes the handler")]
    public WasiHostHttpClient(WasiHostHttpClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,

            // The connections have the timeout of the request (connect-timeout), or of the options
            ConnectTimeout = Timeout.InfiniteTimeSpan,
            ConnectCallback = ConnectAsync,
        };

        _invoker = new HttpMessageInvoker(handler, disposeHandler: true);
    }

    /// <summary>Connects to the first allowed address of the host, in the timeout of the connection.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The stream owns the socket, and a socket that fails to connect is disposed")]
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var timeout = context.InitialRequestMessage.Options.TryGetValue(ConnectTimeoutOption, out var requestTimeout) ? requestTimeout : _options.ConnectTimeout;
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connection.CancelAfter(timeout);
        try
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, connection.Token).ConfigureAwait(false);
            var allowed = addresses.Select(address => new IPEndPoint(address, context.DnsEndPoint.Port)).Where(endPoint => _options.AddressCheck?.Invoke(endPoint, WasiSocketAddressUse.TcpConnect) != false).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException(HttpRequestError.ConnectionError, "The address of '" + host + "' is not allowed", new SocketException((int)SocketError.AccessDenied));

            SocketException? lastError = null;
            foreach (var endPoint in allowed)
            {
                var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(endPoint, connection.Token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException ex)
                {
                    socket.Dispose();
                    lastError = ex;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw lastError!;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError, "The connection timed out", new SocketException((int)SocketError.TimedOut));
        }
    }

    /// <inheritdoc/>
    public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_options.RequestCheck?.Invoke(request) != true)
            return Task.FromException<HttpResponseMessage>(new UnauthorizedAccessException("The request is not allowed"));

        return _invoker.SendAsync(request, cancellationToken);
    }

    /// <summary>Closes the connections of the client.</summary>
    public void Dispose() => _invoker.Dispose();
}

/// <summary>
/// An HTTP client that gives the requests to a handler of the process, without the network: a
/// <see cref="HttpMessageHandler"/> (such as a mock), or a function. It works on every platform, including the browser.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs" ref:region="WasiHttp_InMemoryClient" ref:dedent="true">
/// // The requests of the component go to a function of the process, without the network
/// var client = new WasiInMemoryHttpClient((request, cancellationToken) =&gt; Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
/// {
///     Content = new StringContent("Hello from " + request.RequestUri!.Host),
/// }));
///
/// var wasi = new WasiComponentContext(new WasiOptions { HttpClient = client });
/// </code>
/// </example>
public sealed class WasiInMemoryHttpClient : WasiHttpClient
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    /// <summary>Initializes a new instance of the <see cref="WasiInMemoryHttpClient"/> class with a function.</summary>
    /// <param name="handler">The function that returns the response of a request.</param>
    public WasiInMemoryHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <summary>Initializes a new instance of the <see cref="WasiInMemoryHttpClient"/> class with a message handler.</summary>
    /// <param name="handler">The handler of the requests: the client does not dispose it.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The invoker does not own the handler: it has nothing to dispose")]
    public WasiInMemoryHttpClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        _handler = invoker.SendAsync;
    }

    /// <inheritdoc/>
    public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _handler(request, cancellationToken);
    }
}
