using Meziantou.DnsProxy;
using Meziantou.DnsProxy.History;
using Meziantou.Framework.DnsClient;
using Microsoft.Extensions.Options;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class DnsProxyOptionsTests
{
    [Fact]
    public void DnsProxyOptions_Defaults_AreExpected()
    {
        var options = new DnsProxyOptions();

        Assert.Equal(5053, options.DnsPort);
        Assert.Equal(5080, options.HttpPort);
        Assert.Equal(0, options.DnsOverHttpsPort);
        Assert.Equal("/dns-query", options.DnsOverHttpsPath);
        Assert.Equal(0, options.DnsOverTlsPort);
        Assert.Equal(0, options.DnsOverQuicPort);
        Assert.True(string.IsNullOrEmpty(options.CertificatePath));
        Assert.True(string.IsNullOrEmpty(options.CertificatePassword));
        Assert.False(options.HasSecureServerListenerConfigured);
        Assert.Equal(10_000, options.DiagnosticsHistoryCapacity);
        Assert.Equal(TimeSpan.FromMinutes(30), options.FilterRefreshInterval);
        Assert.Equal(DnsProxyOptions.GetDefaultBlockListCacheFolderPath(), options.BlockListCacheFolderPath);
        Assert.Equal(TimeSpan.FromMinutes(5), options.PositiveCacheDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), options.NegativeCacheDuration);
        Assert.Equal(TimeSpan.FromHours(1), options.MaximumCacheDuration);
        Assert.Equal(DnssecValidationMode.None, options.DnssecValidationMode);
        Assert.Collection(options.BootstrapDnsServers,
            item => Assert.Equal("9.9.9.9", item),
            item => Assert.Equal("149.112.112.112", item),
            item => Assert.Equal("1.1.1.1", item),
            item => Assert.Equal("1.0.0.1", item),
            item => Assert.Equal("2620:fe::fe", item),
            item => Assert.Equal("2620:fe::9", item),
            item => Assert.Equal("2606:4700:4700::1111", item),
            item => Assert.Equal("2606:4700:4700::1001", item));
        Assert.Collection(options.Filters,
            item =>
            {
                Assert.Equal("https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt", item.Url);
                Assert.Equal("AdBlock", item.Format);
            },
            item =>
            {
                Assert.Equal("https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts", item.Url);
                Assert.Equal("Hosts", item.Format);
            });
        Assert.Collection(options.Upstreams,
            item =>
            {
                Assert.Equal("Cloudflare H3", item.Name);
                Assert.Equal(new Uri("h3://cloudflare-dns.com/dns-query"), item.Url);
                Assert.Equal(0, item.Priority);
            },
            item =>
            {
                Assert.Equal("NextDNS DoQ", item.Name);
                Assert.Equal(new Uri("quic://dns.nextdns.io"), item.Url);
                Assert.Equal(1, item.Priority);
            },
            item =>
            {
                Assert.Equal("Quad9 DoQ", item.Name);
                Assert.Equal(new Uri("quic://dns.quad9.net"), item.Url);
                Assert.Equal(2, item.Priority);
            },
            item =>
            {
                Assert.Equal("Cloudflare DoH", item.Name);
                Assert.Equal(new Uri("https://cloudflare-dns.com/dns-query"), item.Url);
                Assert.Equal(3, item.Priority);
            },
            item =>
            {
                Assert.Equal("NextDNS DoH", item.Name);
                Assert.Equal(new Uri("https://dns.nextdns.io"), item.Url);
                Assert.Equal(4, item.Priority);
            },
            item =>
            {
                Assert.Equal("Quad9 DoH", item.Name);
                Assert.Equal(new Uri("https://dns.quad9.net/dns-query"), item.Url);
                Assert.Equal(5, item.Priority);
            });
    }

    [Fact]
    public void DnsProxyOptions_HasSecureServerListenerConfigured_WhenAnySecurePortIsSet()
    {
        Assert.False(new DnsProxyOptions().HasSecureServerListenerConfigured);
        Assert.True(new DnsProxyOptions { DnsOverHttpsPort = 443 }.HasSecureServerListenerConfigured);
        Assert.True(new DnsProxyOptions { DnsOverTlsPort = 853 }.HasSecureServerListenerConfigured);
        Assert.True(new DnsProxyOptions { DnsOverQuicPort = 853 }.HasSecureServerListenerConfigured);
    }

    [Fact]
    public void RequestHistoryStore_UsesConfiguredCapacity()
    {
        var options = Options.Create(new DnsProxyOptions { DiagnosticsHistoryCapacity = 2 });
        var store = new RequestHistoryStore(options);

        for (var i = 0; i < 3; i++)
        {
            store.Add(new RequestHistoryEntry(
                TimestampUtc: DateTimeOffset.UtcNow.AddSeconds(i),
                Client: "127.0.0.1",
                Protocol: "Udp",
                QuestionName: $"example{i}.com",
                QuestionType: "A",
                Result: "Forwarded",
                Upstream: "Cloudflare",
                LatencyMs: 1,
                ResponseCode: "NoError",
                Answers: ["example.com A 1.2.3.4"]));
        }

        var snapshot = store.GetSnapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.DoesNotContain(snapshot, item => item.QuestionName == "example0.com");
    }
}
