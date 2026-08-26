using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

internal static class ServerSideRequestForgeryConnectPipeline
{
    internal static void Configure(SocketsHttpHandler handler, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ResolutionStrategy);
        ArgumentNullException.ThrowIfNull(dnsIpAddressResolver);

        handler.ConnectCallback = (context, cancellationToken) => ConnectAsync(context, options, dnsIpAddressResolver, cancellationToken);
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
            Log.RejectedUnsafeScheme(logger, requestUri, requestUri.Scheme);
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("unsafe_scheme");
            throw new ServerSideRequestForgeryException($"The URI scheme '{requestUri.Scheme}' is not allowed.");
        }

        if (!HostsMatch(dnsEndPoint.Host, requestUri.IdnHost))
        {
            Log.RejectedHostMismatch(logger, requestUri, dnsEndPoint.Host, requestUri.IdnHost);
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

    internal static async ValueTask<IPAddress> SelectIpAddressAsync(Uri requestUri, List<IPAddress> safeAddresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
    {
        var logger = options.Logger;
        IPAddress selectedAddress;
        try
        {
            selectedAddress = await options.ResolutionStrategy.ResolveAsync(safeAddresses, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ServerSideRequestForgeryException ex)
        {
            Log.RejectedResolutionStrategyFailure(logger, requestUri, ex.Message);
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("resolution_strategy_failure");
            throw;
        }

        if (!safeAddresses.Exists(address => address.Equals(selectedAddress)))
        {
            Log.RejectedSelectedAddressNotInSafeSet(logger, requestUri);
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("selected_address_not_validated");
            throw new ServerSideRequestForgeryException("The resolution strategy selected an address that was not part of the validated safe set.");
        }

        return selectedAddress;
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
                selectedAddress = await SelectIpAddressAsync(requestUri, remainingAddresses, options, cancellationToken).ConfigureAwait(false);
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
            Log.RejectedAllResolvedAddressesUnsafe(logger, requestUri);
            ServerSideRequestForgeryMetrics.IncrementRejectedRequest("all_resolved_addresses_unsafe");
            throw new ServerSideRequestForgeryException("No safe IP addresses were found after validation.");
        }

        if (hasUnsafeAddress && options.DisallowMixedSafeAndUnsafeIpAddresses)
        {
            Log.RejectedMixedResolvedAddresses(logger, requestUri);
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

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        return address;
    }
}
