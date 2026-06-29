using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Diagnostics;
using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.DnsProxy.History;
using Meziantou.Framework.DnsClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class DiagnosticsPageRendererTests
{
    [Fact]
    public void FilteringPauseState_ClearsExpiredPause()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 06, 28, 12, 0, 0, TimeSpan.Zero));
        var filteringPauseState = new FilteringPauseState(timeProvider);

        var disabledUntilUtc = filteringPauseState.DisableFor(TimeSpan.FromMinutes(15));
        Assert.Equal(new DateTimeOffset(2026, 06, 28, 12, 15, 0, TimeSpan.Zero), disabledUntilUtc);
        Assert.True(filteringPauseState.IsDisabled);

        timeProvider.SetUtcNow(disabledUntilUtc);

        Assert.Null(filteringPauseState.DisabledUntilUtc);
        Assert.False(filteringPauseState.IsDisabled);
    }

    [Fact]
    public void Render_ContainsConfiguredPorts()
    {
        var options = new DnsProxyOptions
        {
            DnsPort = 5353,
            HttpPort = 5090,
            DnsOverHttpsPort = 5443,
            DnsOverHttpsPath = "/dns-query",
            DnsOverTlsPort = 5853,
            DnsOverQuicPort = 8853,
            CertificatePath = "certs/proxy.pfx",
            FilterRefreshInterval = TimeSpan.FromMinutes(5),
            BlockListCacheFolderPath = "/cache/block-lists",
            PositiveCacheDuration = TimeSpan.FromMinutes(2),
            NegativeCacheDuration = TimeSpan.FromMinutes(3),
            MaximumCacheDuration = TimeSpan.FromMinutes(10),
            DnssecValidationMode = DnssecValidationMode.Local,
            Filters = [],
            CustomRecords =
            [
                new CustomDnsRecordOption { Domain = "sample.local", Type = "A", Value = "192.168.1.11" },
            ],
            Upstreams =
            [
                new UpstreamServerOption
                {
                    Name = "Custom",
                    Url = new Uri("https://1.1.1.1/dns-query"),
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
            new FilteringPauseState(TimeProvider.System),
            upstreamFactory.GetUpstreams(),
            [historyEntry]);

        Assert.Contains("<span class='mono'>DnsPort</span>: 5353", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>HttpPort</span>: 5090", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>DnsOverHttpsPort</span>: 5443", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>DnsOverHttpsPath</span>: /dns-query", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>DnsOverTlsPort</span>: 5853", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>DnsOverQuicPort</span>: 8853", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>CertificatePath</span>: certs/proxy.pfx", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>FilterRefreshInterval</span>: 00:05:00", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>BlockListCacheFolderPath</span>: /cache/block-lists", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>PositiveCacheDuration</span>: 00:02:00", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>NegativeCacheDuration</span>: 00:03:00", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>MaximumCacheDuration</span>: 00:10:00", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>DnssecValidationMode</span>: Local", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mono'>CustomRecords</span>: sample.local =&gt; A:192.168.1.11", html, StringComparison.Ordinal);
        Assert.Contains("Filtering is enabled.", html, StringComparison.Ordinal);
        Assert.Contains("Disable filtering for 15 minutes", html, StringComparison.Ordinal);
        Assert.Contains("example.com A 1.2.3.4", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ContainsFilteringPauseStatus()
    {
        var options = new DnsProxyOptions
        {
            Filters = [],
            CustomRecords = [],
            Upstreams = [],
        };
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient();
        using var serviceProvider = serviceCollection.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var filterEngineProvider = new FilterEngineProvider(httpClientFactory, Options.Create(options), NullLogger<FilterEngineProvider>.Instance);
        var filteringPauseState = new FilteringPauseState(TimeProvider.System);
        var disabledUntilUtc = filteringPauseState.DisableFor(TimeSpan.FromMinutes(15));

        var html = DiagnosticsPageRenderer.Render(
            options,
            filterEngineProvider,
            filteringPauseState,
            [],
            []);

        Assert.Contains("Filtering is disabled until", html, StringComparison.Ordinal);
        Assert.Contains(disabledUntilUtc.ToString("u", CultureInfo.InvariantCulture), html, StringComparison.Ordinal);
        Assert.Contains("<button type='submit' disabled>Disable filtering for 15 minutes</button>", html, StringComparison.Ordinal);
    }
}
