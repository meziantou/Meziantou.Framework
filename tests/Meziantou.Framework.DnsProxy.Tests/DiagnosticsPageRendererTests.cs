using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Diagnostics;
using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.DnsProxy.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class DiagnosticsPageRendererTests
{
    [Fact]
    public void Render_ContainsConfiguredPorts()
    {
        var options = new DnsProxyOptions
        {
            DnsPort = 5353,
            HttpPort = 5090,
            FilterRefreshInterval = TimeSpan.FromMinutes(5),
            Filters = [],
            Rewrites = [],
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
        };

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient();
        using var serviceProvider = serviceCollection.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var filterEngineProvider = new FilterEngineProvider(httpClientFactory, Options.Create(options), NullLogger<FilterEngineProvider>.Instance);

        using var upstreamFactory = new UpstreamDnsClientFactory(Options.Create(options), NullLogger<UpstreamDnsClientFactory>.Instance);

        var historyEntry = new RequestHistoryEntry(
            DateTimeOffset.UtcNow,
            "127.0.0.1",
            "Udp",
            "example.com",
            "A",
            "Forwarded",
            "Custom",
            12,
            "NoError",
            ["example.com A 1.2.3.4"]);

        var html = DiagnosticsPageRenderer.Render(
            options,
            filterEngineProvider,
            upstreamFactory.GetUpstreams(),
            [historyEntry]);

        Assert.Contains("<span class='mono'>DnsPort</span>: 5353", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>HttpPort</span>: 5090", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>FilterRefreshInterval</span>: 00:05:00", html, StringComparison.Ordinal);
        Assert.Contains("example.com A 1.2.3.4", html, StringComparison.Ordinal);
    }
}
