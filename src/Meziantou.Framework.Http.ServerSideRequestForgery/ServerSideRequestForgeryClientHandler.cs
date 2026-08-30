namespace Meziantou.Framework.Http.ServerSideRequestForgery;

/// <summary>An HTTP message handler that applies SSRF protection to a <see cref="SocketsHttpHandler"/> and rejects the requests that would bypass it over HTTP/3.</summary>
/// <remarks>
/// <see cref="SocketHttpHandlerServerSideRequestForgeryExtensions.ConfigureSsrf(SocketsHttpHandler, ServerSideRequestForgeryOptions)"/>
/// validates a destination from <see cref="SocketsHttpHandler.ConnectCallback"/>, which the runtime uses only for
/// TCP connections. An HTTP/3 connection is established over QUIC and never reaches it, so a handler that allows
/// HTTP/3 is not protected. Prefer this handler over calling <c>ConfigureSsrf</c> directly.
/// </remarks>
/// <example>
/// <code>
/// var options = new ServerSideRequestForgeryOptions();
/// var handler = new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options);
/// using var client = new HttpClient(handler, disposeHandler: true);
/// </code>
/// </example>
public sealed class ServerSideRequestForgeryClientHandler : DelegatingHandler
{
    /// <summary>Initializes a new instance of the <see cref="ServerSideRequestForgeryClientHandler"/> class.</summary>
    /// <param name="innerHandler">The handler that establishes the connections. SSRF protection is configured on it.</param>
    /// <param name="options">The SSRF policy to apply.</param>
    public ServerSideRequestForgeryClientHandler(SocketsHttpHandler innerHandler, ServerSideRequestForgeryOptions options)
        : this(innerHandler, options, SystemDnsIpAddressResolver.Instance)
    {
    }

    internal ServerSideRequestForgeryClientHandler(SocketsHttpHandler innerHandler, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver)
        : base(ConfigureInnerHandler(innerHandler, options, dnsIpAddressResolver))
    {
    }

    private static SocketsHttpHandler ConfigureInnerHandler(SocketsHttpHandler innerHandler, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);
        ArgumentNullException.ThrowIfNull(options);

        return innerHandler.ConfigureSsrf(options, dnsIpAddressResolver);
    }

    /// <summary>Sends an HTTP request, rejecting it when it would be sent over a transport this library cannot validate.</summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureRequestCannotUseHttp3(request);
        return base.Send(request, cancellationToken);
    }

    /// <summary>Sends an HTTP request, rejecting it when it would be sent over a transport this library cannot validate.</summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureRequestCannotUseHttp3(request);
        return base.SendAsync(request, cancellationToken);
    }

    internal static void EnsureRequestCannotUseHttp3(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // SocketsHttpHandler attempts HTTP/3 when the request asks for version 3 or higher, and also when its policy
        // allows a higher version over TLS - the server then only has to advertise an h3 alternative service, and
        // Alt-Svc may name any host and port. Neither case can be caught later: the QUIC connection is opened by
        // ConnectHelper.ConnectQuicAsync, which resolves the endpoint itself and never calls ConnectCallback.
        // Leaving both alone is what a request must look like for the connect-time validation to see every connection.
        if (request.Version.Major < 3 && request.VersionPolicy != HttpVersionPolicy.RequestVersionOrHigher)
            return;

        throw new ServerSideRequestForgeryException($"The request allows HTTP/3 (Version = {request.Version}, VersionPolicy = {request.VersionPolicy}). An HTTP/3 connection is established over QUIC, which does not go through SocketsHttpHandler.ConnectCallback, so its destination cannot be validated. Set the request Version to 2.0 or lower and VersionPolicy to RequestVersionOrLower; HTTP/2 is still negotiated over TLS. HttpClient.DefaultRequestVersion and HttpClient.DefaultVersionPolicy set this for every request.");
    }
}
