using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Proxy;

/// <summary>
/// Decides whether a client address is allowed to query the proxy, based on <see cref="DnsProxyOptions.AllowedClientNetworks"/>.
/// </summary>
internal sealed class ClientAccessPolicy
{
    private readonly IPNetwork[] _allowedNetworks;

    public ClientAccessPolicy(IOptions<DnsProxyOptions> options, ILogger<ClientAccessPolicy> logger)
    {
        _allowedNetworks = ParseNetworks(options.Value.AllowedClientNetworks, logger);
    }

    /// <summary>Gets a value indicating whether the policy restricts anything. When <see langword="false"/>, every client is allowed.</summary>
    public bool HasRestrictions => _allowedNetworks.Length > 0;

    public bool IsAllowed(IPAddress? clientAddress)
    {
        if (_allowedNetworks.Length == 0)
        {
            return true;
        }

        // The proxy's own diagnostics and DNS over HTTPS endpoints reach the handler over loopback.
        if (clientAddress is null || IPAddress.IsLoopback(clientAddress))
        {
            return true;
        }

        var address = Normalize(clientAddress);
        foreach (var network in _allowedNetworks)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        return false;
    }

    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static IPNetwork[] ParseNetworks(IEnumerable<string> values, ILogger logger)
    {
        var networks = new List<IPNetwork>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (TryParseNetwork(value.Trim(), out var network))
            {
                networks.Add(network);
            }
            else
            {
                logger.LogWarning("Skipping invalid allowed client network {Network}", value);
            }
        }

        return [.. networks];
    }

    private static bool TryParseNetwork(string value, out IPNetwork network)
    {
        if (IPNetwork.TryParse(value, out network))
        {
            return true;
        }

        // A bare address is a single host.
        if (IPAddress.TryParse(value, out var address))
        {
            var normalized = Normalize(address);
            network = new IPNetwork(normalized, normalized.GetAddressBytes().Length * 8);
            return true;
        }

        network = default;
        return false;
    }
}
