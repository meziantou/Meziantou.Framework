using Meziantou.DnsProxy;
using Meziantou.DnsProxy.History;
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
        Assert.Equal(10_000, options.DiagnosticsHistoryCapacity);
        Assert.Equal(TimeSpan.FromMinutes(30), options.FilterRefreshInterval);
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
                Assert.Equal("Cloudflare", item.Name);
                Assert.Equal("cloudflare-dns.com", item.Endpoint);
                Assert.Equal("Quic", item.Protocol);
            },
            item =>
            {
                Assert.Equal("Quad9", item.Name);
                Assert.Equal("dns.quad9.net", item.Endpoint);
                Assert.Equal("Quic", item.Protocol);
            },
            item =>
            {
                Assert.Equal("NextDNS", item.Name);
                Assert.Equal("dns.nextdns.io", item.Endpoint);
                Assert.Equal("Quic", item.Protocol);
            });
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
