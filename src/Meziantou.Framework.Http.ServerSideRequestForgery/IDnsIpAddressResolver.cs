using System.Net;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

internal interface IDnsIpAddressResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
