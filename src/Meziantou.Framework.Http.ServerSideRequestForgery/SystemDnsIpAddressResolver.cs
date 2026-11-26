using System.Net;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

internal sealed class SystemDnsIpAddressResolver : IDnsIpAddressResolver
{
    public static SystemDnsIpAddressResolver Instance { get; } = new();

    private SystemDnsIpAddressResolver()
    {
    }

    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (IPAddress.TryParse(NormalizeHostLiteral(host), out var ipAddress))
        {
            return [ipAddress];
        }

        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }

    // A slice rather than a substring: parsing a bracketed IPv6 literal does not need a copy of it.
    private static ReadOnlySpan<char> NormalizeHostLiteral(ReadOnlySpan<char> host)
    {
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
        {
            return host[1..^1];
        }

        return host;
    }
}
