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

    private static string NormalizeHostLiteral(string host)
    {
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
        {
            return host[1..^1];
        }

        return host;
    }
}
