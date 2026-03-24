using Meziantou.Framework.DnsServer.Protocol;

namespace Meziantou.Framework.DnsServer.Handler;

/// <summary>Represents the delegate used to handle DNS requests.</summary>
/// <param name="context">The DNS request context.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>A task that represents the asynchronous operation, containing the DNS response message.</returns>
public delegate ValueTask<DnsMessage> DnsRequestDelegate(DnsRequestContext context, CancellationToken cancellationToken);
