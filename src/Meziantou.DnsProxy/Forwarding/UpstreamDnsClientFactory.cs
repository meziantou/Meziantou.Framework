using Meziantou.Framework.DnsClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Forwarding;

internal sealed class UpstreamDnsClientFactory : IDisposable
{
    private readonly IReadOnlyList<UpstreamDnsClientInfo> _upstreams;

    public UpstreamDnsClientFactory(IOptions<DnsProxyOptions> options, ILogger<UpstreamDnsClientFactory> logger)
    {
        var upstreams = new List<UpstreamDnsClientInfo>();
        foreach (var upstream in options.Value.Upstreams)
        {
            if (string.IsNullOrWhiteSpace(upstream.Endpoint))
            {
                continue;
            }

            var protocol = Enum.TryParse<DnsClientProtocol>(upstream.Protocol, ignoreCase: true, out var parsedProtocol)
                ? parsedProtocol
                : DnsClientProtocol.Https;
            var displayName = string.IsNullOrWhiteSpace(upstream.Name) ? upstream.Endpoint : $"{upstream.Name} ({upstream.Endpoint})";
            SocketsHttpHandler? httpHandler = protocol == DnsClientProtocol.Https ? CreateHttpHandler(upstream.UseHttp3) : null;
            DnsClient dnsClient;
            DnsClientProtocol effectiveProtocol = protocol;
            try
            {
                dnsClient = protocol == DnsClientProtocol.Https
                    ? new DnsClient(upstream.Endpoint, protocol, new DnsClientOptions { HttpHandler = httpHandler })
                    : new DnsClient(upstream.Endpoint, protocol);
            }
            catch (PlatformNotSupportedException ex) when (protocol == DnsClientProtocol.Quic)
            {
                httpHandler?.Dispose();
                httpHandler = CreateHttpHandler(useHttp3: false);
                dnsClient = new DnsClient($"https://{upstream.Endpoint}/dns-query", DnsClientProtocol.Https, new DnsClientOptions { HttpHandler = httpHandler });
                effectiveProtocol = DnsClientProtocol.Https;
                logger.LogWarning(ex, "DNS over QUIC is not supported on this platform for {Upstream}. Falling back to DNS over HTTPS.", upstream.Endpoint);
            }

            if (effectiveProtocol == DnsClientProtocol.Https && !upstream.Endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                upstreams.Add(new UpstreamDnsClientInfo(displayName, $"https://{upstream.Endpoint}/dns-query", dnsClient, httpHandler));
            }
            else
            {
                upstreams.Add(new UpstreamDnsClientInfo(displayName, upstream.Endpoint, dnsClient, httpHandler));
            }
        }

        _upstreams = upstreams;
    }

    public IReadOnlyList<UpstreamDnsClientInfo> GetUpstreams() => _upstreams;

    public void Dispose()
    {
        foreach (var upstream in _upstreams)
        {
            upstream.Dispose();
        }
    }

    private static SocketsHttpHandler CreateHttpHandler(bool useHttp3)
    {
        var handler = new SocketsHttpHandler();
        if (useHttp3)
        {
            handler.EnableMultipleHttp2Connections = true;
            handler.SslOptions.ApplicationProtocols =
            [
                new System.Net.Security.SslApplicationProtocol("h3"),
                new System.Net.Security.SslApplicationProtocol("h2"),
            ];
        }

        return handler;
    }
}
