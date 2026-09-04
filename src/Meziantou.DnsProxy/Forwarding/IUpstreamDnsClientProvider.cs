namespace Meziantou.DnsProxy.Forwarding;

/// <summary>Provides the upstream servers to forward queries to, in priority order.</summary>
internal interface IUpstreamDnsClientProvider
{
    IReadOnlyList<IUpstreamDnsClient> GetUpstreams();
}
