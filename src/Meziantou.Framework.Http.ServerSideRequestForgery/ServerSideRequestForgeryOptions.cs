using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

public sealed class ServerSideRequestForgeryOptions
{
    private static readonly IPNetwork[] DefaultUnsafeIpNetworks =
    [
        // "This network" / unspecified IPv4 range. Not globally routable and should never be a trusted remote target.
        IPNetwork.Parse("0.0.0.0/8"),
        // RFC1918 private IPv4 range.
        IPNetwork.Parse("10.0.0.0/8"),
        // Carrier-grade NAT shared address space (RFC6598), internal to providers.
        IPNetwork.Parse("100.64.0.0/10"),
        // Loopback range; always local to the current host.
        IPNetwork.Parse("127.0.0.0/8"),
        // Link-local IPv4 range (APIPA), including cloud metadata-style addresses.
        IPNetwork.Parse("169.254.0.0/16"),
        // RFC1918 private IPv4 range.
        IPNetwork.Parse("172.16.0.0/12"),
        // IETF protocol assignments (RFC6890), including the NAT64 discovery addresses. Not globally routable.
        IPNetwork.Parse("192.0.0.0/24"),
        // 6to4 relay anycast range (RFC3068), deprecated and not a valid remote target.
        IPNetwork.Parse("192.88.99.0/24"),
        // RFC1918 private IPv4 range.
        IPNetwork.Parse("192.168.0.0/16"),
        // Benchmark/testing inter-network range (RFC2544), not for public routing.
        IPNetwork.Parse("198.18.0.0/15"),
        // IPv4 multicast range.
        IPNetwork.Parse("224.0.0.0/4"),
        // Reserved/experimental IPv4 range.
        IPNetwork.Parse("240.0.0.0/4"),
        // IPv6 unspecified address and the deprecated IPv4-compatible range (::a.b.c.d), which embeds an IPv4 address.
        IPNetwork.Parse("::/96"),
        // IPv6 loopback address. Already covered by ::/96 above, but listed explicitly because it is the range most often looked for.
        IPNetwork.Parse("::1/128"),
        // NAT64 well-known prefix (RFC6052). Embeds an IPv4 address, so it reaches IPv4 targets from an IPv6-only network.
        IPNetwork.Parse("64:ff9b::/96"),
        // Teredo tunneling range (RFC4380). Embeds an IPv4 address.
        IPNetwork.Parse("2001::/32"),
        // 6to4 range (RFC3056). Embeds an IPv4 address in the second and third groups.
        IPNetwork.Parse("2002::/16"),
        // IPv6 unique local addresses (ULA), private scope.
        IPNetwork.Parse("fc00::/7"),
        // IPv6 link-local addresses.
        IPNetwork.Parse("fe80::/10"),
        // IPv6 multicast range.
        IPNetwork.Parse("ff00::/8"),
    ];

    private static readonly string[] DefaultSafeSchemes = [Uri.UriSchemeHttps, Uri.UriSchemeWss];

    public ICollection<string> SafeSchemes { get; } = new HashSet<string>(DefaultSafeSchemes, StringComparer.OrdinalIgnoreCase);

    public ICollection<IPNetwork> UnsafeIpNetworks { get; } = [.. DefaultUnsafeIpNetworks];

    public ICollection<IPNetwork> SafeIpNetworks { get; } = [];

    public IpAddressResolutionStrategy ResolutionStrategy { get; set; } = IpAddressResolutionStrategy.PreferIpv4;

    public bool DisallowMixedSafeAndUnsafeIpAddresses { get; set; } = true;

    public ILogger Logger { get; set; } = NullLogger.Instance;
}
