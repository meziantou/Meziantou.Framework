using System.Net;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

internal sealed class FakeDnsIpAddressResolver(IReadOnlyList<IPAddress> addresses) : IDnsIpAddressResolver
{
    public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        _ = host;
        _ = cancellationToken;
        return ValueTask.FromResult(addresses);
    }
}
