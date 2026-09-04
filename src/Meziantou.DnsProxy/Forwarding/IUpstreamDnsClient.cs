using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;

namespace Meziantou.DnsProxy.Forwarding;

/// <summary>A single configured upstream DNS server.</summary>
internal interface IUpstreamDnsClient
{
    string DisplayName { get; }

    Task<DnsResponseMessage> SendAsync(DnsQueryMessage query, CancellationToken cancellationToken);
}
