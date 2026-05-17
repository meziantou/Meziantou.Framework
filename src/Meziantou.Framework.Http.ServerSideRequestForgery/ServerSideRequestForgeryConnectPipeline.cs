using System.Net;
using System.Net.Sockets;

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
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(dnsEndPoint);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ResolutionStrategy);
        ArgumentNullException.ThrowIfNull(dnsIpAddressResolver);

        if (!IsAllowedScheme(requestUri, options))
        {
            throw new ServerSideRequestForgeryException($"The URI scheme '{requestUri.Scheme}' is not allowed.");
        }

        if (!HostsMatch(dnsEndPoint.Host, requestUri.IdnHost))
        {
            throw new ServerSideRequestForgeryException("The host resolved for the connection does not match the request URI authority.");
        }

        // Security decision (TOCTOU): resolve DNS for each outbound connection attempt.
        // Caching DNS would allow stale validation decisions and could reopen SSRF vectors
        // if a hostname changes after an earlier check but before later use.
        var resolvedAddresses = await dnsIpAddressResolver.ResolveAsync(dnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        var safeAddresses = FilterSafeAddresses(resolvedAddresses, options);
        var selectedAddress = await options.ResolutionStrategy.ResolveAsync(safeAddresses, options, cancellationToken).ConfigureAwait(false);

        if (!safeAddresses.Exists(address => address.Equals(selectedAddress)))
        {
            throw new ServerSideRequestForgeryException("The resolution strategy selected an address that was not part of the validated safe set.");
        }

        return selectedAddress;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, ServerSideRequestForgeryOptions options, IDnsIpAddressResolver dnsIpAddressResolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestUri = context.InitialRequestMessage?.RequestUri ?? throw new InvalidOperationException("The request URI cannot be null.");
        var selectedAddress = await ResolveAndSelectIpAddressAsync(requestUri, context.DnsEndPoint, options, dnsIpAddressResolver, cancellationToken).ConfigureAwait(false);

        // The returned NetworkStream owns the socket lifetime once the connection succeeds.
#pragma warning disable CA2000 // Dispose objects before losing scope
        var socket = new Socket(selectedAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
#pragma warning restore CA2000
        try
        {
            await socket.ConnectAsync(new IPEndPoint(selectedAddress, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static List<IPAddress> FilterSafeAddresses(IReadOnlyList<IPAddress> resolvedAddresses, ServerSideRequestForgeryOptions options)
    {
        ArgumentNullException.ThrowIfNull(resolvedAddresses);
        ArgumentNullException.ThrowIfNull(options);

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
            throw new ServerSideRequestForgeryException("No safe IP addresses were found after validation.");
        }

        if (hasUnsafeAddress && options.DisallowMixedSafeAndUnsafeIpAddresses)
        {
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
