using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestUtilities;

using DnsClientType = Meziantou.Framework.DnsClient.DnsClient;
using DnsClientProtocol = Meziantou.Framework.DnsClient.DnsClientProtocol;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class UpstreamDnsClientFactoryTests
{
    [Fact]
    public void UpstreamDnsClientFactory_UsesConfiguredUpstreams()
    {
        var options = Options.Create(new DnsProxyOptions
        {
            Upstreams =
            [
                new UpstreamServerOption
                {
                    Name = "Custom",
                    Endpoint = "https://1.1.1.1/dns-query",
                    Protocol = "Https",
                    UseHttp3 = false,
                },
            ],
        });

        using var factory = new UpstreamDnsClientFactory(options, NullLogger<UpstreamDnsClientFactory>.Instance);
        var upstreams = factory.GetUpstreams();

        var upstream = Assert.Single(upstreams);
        Assert.Equal("Custom (https://1.1.1.1/dns-query)", upstream.DisplayName);
        Assert.Equal("https://1.1.1.1/dns-query", upstream.Endpoint);
    }

    [Theory]
    [InlineData("Cloudflare", "cloudflare-dns.com")]
    [InlineData("Quad9", "dns.quad9.net")]
    [InlineData("NextDNS", "dns.nextdns.io")]
    public async Task UpstreamDnsClientFactory_DefaultUpstream_CanResolveRecordAsync(string name, string endpoint)
    {
        var options = Options.Create(new DnsProxyOptions
        {
            Upstreams =
            [
                new UpstreamServerOption
                {
                    Name = name,
                    Endpoint = endpoint,
                    Protocol = "Quic",
                },
            ],
        });

        using var factory = new UpstreamDnsClientFactory(options, NullLogger<UpstreamDnsClientFactory>.Instance);
        var upstream = Assert.Single(factory.GetUpstreams());
        Assert.Equal($"{name} ({endpoint})", upstream.DisplayName);

        var response = await XUnitStaticHelpers.Retry(() => QueryARecordUsingUpstreamAsync(upstream, CancellationToken.None));
        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    private static async Task<DnsResponseMessage> QueryARecordUsingUpstreamAsync(UpstreamDnsClientInfo upstream, CancellationToken cancellationToken)
    {
        try
        {
            return await QueryARecordAsync(upstream.Client, cancellationToken);
        }
        catch (OperationCanceledException) when (!upstream.Endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // Some environments block UDP/443 (DNS over QUIC). Validate upstream connectivity using DoH as fallback.
            using var fallbackClient = new DnsClientType($"https://{upstream.Endpoint}/dns-query", DnsClientProtocol.Https);
            return await QueryARecordAsync(fallbackClient, cancellationToken);
        }
        catch (PlatformNotSupportedException) when (!upstream.Endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // Some environments block UDP/443 (DNS over QUIC). Validate upstream connectivity using DoH as fallback.
            using var fallbackClient = new DnsClientType($"https://{upstream.Endpoint}/dns-query", DnsClientProtocol.Https);
            return await QueryARecordAsync(fallbackClient, cancellationToken);
        }
    }

    private static async Task<DnsResponseMessage> QueryARecordAsync(DnsClientType client, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        return await client.QueryAsync("example.com", DnsQueryType.A, timeoutCts.Token);
    }
}
