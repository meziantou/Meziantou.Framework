using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

internal static class ServerSideRequestForgeryConnectPipeline
{
    // Reserved TLD (RFC2606): never a real destination, so a proxy returned for it is the handler's proxy address.
    private static readonly Uri[] ProxyProbeUris = [new("https://ssrf-proxy-probe.invalid/"), new("http://ssrf-proxy-probe.invalid/")];

    internal static void Configure(SocketsHttpHandler handler, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ResolutionStrategy);
        ArgumentNullException.ThrowIfNull(dnsIpAddressResolver);

        handler.ConnectCallback = (context, cancellationToken) => ConnectGuardingAgainstProxyAsync(handler, context, options, dnsIpAddressResolver, cancellationToken);
    }

    internal static async ValueTask<IPAddress> ResolveAndSelectIpAddressAsync(Uri requestUri, DnsEndPoint dnsEndPoint, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver, CancellationToken cancellationToken)
    {
        var safeAddresses = await ResolveSafeIpAddressesAsync(requestUri, dnsEndPoint, options, dnsIpAddressResolver, cancellationToken).ConfigureAwait(false);
        return await SelectIpAddressAsync(requestUri, safeAddresses, options, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<List<IPAddress>> ResolveSafeIpAddressesAsync(Uri requestUri, DnsEndPoint dnsEndPoint, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(dnsEndPoint);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ResolutionStrategy);
        ArgumentNullException.ThrowIfNull(dnsIpAddressResolver);
        var logger = options.Logger;

        if (!IsAllowedScheme(requestUri, options))
        {
            Log.RejectedUnsafeScheme(logger, FormatRequestOrigin(requestUri), requestUri.Scheme);
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("unsafe_scheme");
            throw new ServerSideRequestForgeryException($"The URI scheme '{requestUri.Scheme}' is not allowed.");
        }

        if (!HostsMatch(dnsEndPoint.Host, requestUri.IdnHost))
        {
            Log.RejectedHostMismatch(logger, FormatRequestOrigin(requestUri), dnsEndPoint.Host, requestUri.IdnHost);
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("host_mismatch");
            throw new ServerSideRequestForgeryException("The host resolved for the connection does not match the request URI authority.");
        }

        // Security decision (TOCTOU): resolve DNS for each outbound connection attempt.
        // Caching DNS would allow stale validation decisions and could reopen SSRF vectors
        // if a hostname changes after an earlier check but before later use.
        var resolvedAddresses = await dnsIpAddressResolver.ResolveAsync(dnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        if (resolvedAddresses.Count == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        return FilterSafeAddresses(requestUri, resolvedAddresses, options, logger);
    }

    internal static ValueTask<IPAddress> SelectIpAddressAsync(Uri requestUri, List<IPAddress> safeAddresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
    {
        return SelectIpAddressAsync(requestUri, safeAddresses, options, reportStrategyFailure: true, cancellationToken);
    }

    // reportStrategyFailure: whether a strategy that has no candidate left is a rejection worth reporting. It is on
    // the first selection, where it means the policy refused every validated address. It is not once a connect has
    // already failed: the caller then re-asks over the addresses that remain, and running out of them is an ordinary
    // connection failure, not an SSRF rejection. Reporting it there would put a Warning and a rejected_requests
    // increment on every unreachable host.
    private static async ValueTask<IPAddress> SelectIpAddressAsync(Uri requestUri, List<IPAddress> safeAddresses, ServerSideRequestForgeryOptions options, bool reportStrategyFailure, CancellationToken cancellationToken)
    {
        var logger = options.Logger;
        IPAddress selectedAddress;
        try
        {
            selectedAddress = await options.ResolutionStrategy.ResolveAsync(safeAddresses, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ServerSideRequestForgeryException ex) when (reportStrategyFailure)
        {
            Log.RejectedResolutionStrategyFailure(logger, FormatRequestOrigin(requestUri), ex.Message);
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("resolution_strategy_failure");
            throw;
        }

        if (!safeAddresses.Exists(address => address.Equals(selectedAddress)))
        {
            Log.RejectedSelectedAddressNotInSafeSet(logger, FormatRequestOrigin(requestUri));
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("selected_address_not_validated");
            throw new ServerSideRequestForgeryException("The resolution strategy selected an address that was not part of the validated safe set.");
        }

        return selectedAddress;
    }

    private static ValueTask<Stream> ConnectGuardingAgainstProxyAsync(SocketsHttpHandler handler, SocketsHttpConnectionContext context, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestUri = context.InitialRequestMessage?.RequestUri ?? throw new InvalidOperationException("The request URI cannot be null.");
        EnsureConnectionIsNotToAProxy(handler, requestUri, context.DnsEndPoint, options);
        return ConnectAsync(context, options, dnsIpAddressResolver, cancellationToken);
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestUri = context.InitialRequestMessage?.RequestUri ?? throw new InvalidOperationException("The request URI cannot be null.");
        var safeAddresses = await ResolveSafeIpAddressesAsync(requestUri, context.DnsEndPoint, options, dnsIpAddressResolver, cancellationToken).ConfigureAwait(false);

        // Every address here has already been validated. Ask the strategy again after each failure, over the
        // addresses that are left, so the fallback keeps whatever constraint the strategy expresses: Ipv4Only
        // never falls back to IPv6, and PreferIpv4 falls back to IPv6 only once every IPv4 address has failed.
        var remainingAddresses = safeAddresses;
        Exception? lastConnectException = null;
        while (remainingAddresses.Count > 0)
        {
            IPAddress selectedAddress;
            try
            {
                selectedAddress = await SelectIpAddressAsync(requestUri, remainingAddresses, options, reportStrategyFailure: lastConnectException is null, cancellationToken).ConfigureAwait(false);
            }
            catch (ServerSideRequestForgeryException) when (lastConnectException is not null)
            {
                // The strategy has no candidate left among the addresses that have not already failed.
                break;
            }

            // The returned NetworkStream owns the socket lifetime once the connection succeeds.
#pragma warning disable CA2000 // Dispose objects before losing scope
            var socket = CreateConnectSocket(selectedAddress.AddressFamily);
#pragma warning restore CA2000
            try
            {
                await socket.ConnectAsync(new IPEndPoint(selectedAddress, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                lastConnectException = ex;
                remainingAddresses.Remove(selectedAddress);
            }
        }

        throw lastConnectException ?? new SocketException((int)SocketError.HostNotFound);
    }

    internal static Socket CreateConnectSocket(AddressFamily addressFamily)
    {
        // Mirrors the defaults the runtime applies on its own connect path (HttpConnectionPool.ConnectToTcpHostAsync).
        // Replacing ConnectCallback opts out of those defaults, and leaving Nagle's algorithm enabled adds latency
        // to every request whose body is written in small chunks.
        return new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    }

    private static List<IPAddress> FilterSafeAddresses(Uri requestUri, IReadOnlyList<IPAddress> resolvedAddresses, ServerSideRequestForgeryOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(resolvedAddresses);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var safeAddresses = new List<IPAddress>(resolvedAddresses.Count);
        var hasUnsafeAddress = false;
        foreach (var address in resolvedAddresses)
        {
            if (IsSafeAddress(address, options))
            {
                safeAddresses.Add(address);
            }
            else
            {
                hasUnsafeAddress = true;
            }
        }

        if (safeAddresses.Count == 0)
        {
            Log.RejectedAllResolvedAddressesUnsafe(logger, FormatRequestOrigin(requestUri));
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("all_resolved_addresses_unsafe");
            throw new ServerSideRequestForgeryException("No safe IP addresses were found after validation.");
        }

        if (hasUnsafeAddress && options.DisallowMixedSafeAndUnsafeIpAddresses)
        {
            Log.RejectedMixedResolvedAddresses(logger, FormatRequestOrigin(requestUri));
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("mixed_addresses_disallowed");
            throw new ServerSideRequestForgeryException("The hostname resolved to a mix of safe and unsafe IP addresses.");
        }

        return safeAddresses;
    }

    private static bool IsSafeAddress(IPAddress address, ServerSideRequestForgeryOptions options)
    {
        if (address.AddressFamily is not AddressFamily.InterNetwork and not AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var normalizedAddress = NormalizeAddress(address);
        if (options.SafeIpNetworks.Any(network => network.Contains(normalizedAddress)))
        {
            return true;
        }

        return !options.UnsafeIpNetworks.Any(network => network.Contains(normalizedAddress));
    }

    internal static void EnsureConnectionIsNotToAProxy(SocketsHttpHandler handler, Uri requestUri, DnsEndPoint dnsEndPoint, ServerSideRequestForgeryOptions options)
    {
        if (!IsConnectionToAProxy(handler, requestUri, dnsEndPoint))
            return;

        Log.RejectedProxyConnection(options.Logger, FormatRequestOrigin(requestUri), dnsEndPoint.Host);
        ServerSideRequestForgeryMetrics.IncrementRejectedRequest("proxy_connection");
        throw new ServerSideRequestForgeryException("The connection targets a proxy. The request's real destination is established by the proxy and is not visible here, so it cannot be validated. Set SocketsHttpHandler.UseProxy to false, or send requests that need SSRF protection through a handler that does not use a proxy.");
    }

    private static bool IsConnectionToAProxy(SocketsHttpHandler handler, Uri requestUri, DnsEndPoint dnsEndPoint)
    {
        // Reading the proxy here rather than when the callback is installed keeps the check correct when the
        // proxy is assigned after ConfigureSsrf, or when HttpClient.DefaultProxy changes later.
        var proxy = handler.UseProxy ? handler.Proxy ?? HttpClient.DefaultProxy : null;
        if (proxy is null)
            return false;

        // Asking about the request URI alone is not enough. For an https target the pool substitutes the proxy's
        // own URI as the initial request, and GetProxy then returns that same URI - which is indistinguishable
        // from "this destination is bypassed". So also probe with destinations that are certainly not the proxy,
        // which reveals the proxy's address even when the request URI has been substituted.
        if (ProxyAddressMatchesEndPoint(proxy, requestUri, dnsEndPoint, unknownMeansProxied: true))
            return true;

        foreach (var probeUri in ProxyProbeUris)
        {
            if (ProxyAddressMatchesEndPoint(proxy, probeUri, dnsEndPoint, unknownMeansProxied: false))
                return true;
        }

        return false;
    }

    private static bool ProxyAddressMatchesEndPoint(IWebProxy proxy, Uri destination, DnsEndPoint dnsEndPoint, bool unknownMeansProxied)
    {
        Uri? proxyUri;
        try
        {
            proxyUri = proxy.GetProxy(destination);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A proxy that cannot answer for the real request is treated as one that applies, so the connection
            // fails closed. A probe destination it dislikes tells us nothing and must not fail an ordinary request.
            return unknownMeansProxied;
        }

        // IWebProxy reports "no proxy for this destination" either by returning null (HttpClient.DefaultProxy)
        // or by returning the destination itself (WebProxy when the address is bypassed).
        if (proxyUri is null || proxyUri == destination)
            return false;

        return proxyUri.Port == dnsEndPoint.Port
            && string.Equals(NormalizeHost(proxyUri.IdnHost), NormalizeHost(dnsEndPoint.Host), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedScheme(Uri requestUri, ServerSideRequestForgeryOptions options)
    {
        return options.SafeSchemes.Any(scheme => string.Equals(scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HostsMatch(string endpointHost, string requestUriHost)
    {
        return string.Equals(NormalizeHost(endpointHost), NormalizeHost(requestUriHost), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHost(string host)
    {
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
        {
            host = host[1..^1];
        }

        return host.TrimEnd('.');
    }

    /// <summary>Formats the destination for a log message, keeping only the parts that identify it.</summary>
    /// <remarks>
    /// The full <see cref="Uri"/> must never reach a log: <see cref="Uri.ToString"/> keeps the userinfo and the
    /// query string, so a rejected request carrying basic-auth credentials, a bearer token or a signed-URL
    /// signature would write that secret at Warning level. The origin is what identifies the destination, and it
    /// is the only part a rejection needs.
    /// </remarks>
    internal static string FormatRequestOrigin(Uri requestUri)
    {
        return $"{requestUri.Scheme}://{requestUri.IdnHost}:{requestUri.Port}";
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        return address;
    }
}
