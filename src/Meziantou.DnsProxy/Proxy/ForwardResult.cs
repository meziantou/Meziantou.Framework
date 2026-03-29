using Meziantou.Framework.DnsClient.Response;

namespace Meziantou.DnsProxy.Proxy;

internal readonly record struct ForwardResult(bool IsSuccess, string UpstreamEndpoint, DnsResponseMessage? Response, long LatencyMs)
{
    public static ForwardResult Success(string upstreamEndpoint, DnsResponseMessage response, long latencyMs) => new(true, upstreamEndpoint, response, latencyMs);

    public static ForwardResult Failure() => new(false, "-", null, 0);
}
