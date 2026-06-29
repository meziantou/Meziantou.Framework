using System.Net;
using Meziantou.DnsProxy;
using Meziantou.DnsProxy.History;
using Meziantou.DnsProxy.Proxy;
using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DnsServerQuestion = Meziantou.Framework.DnsServer.Protocol.DnsQuestion;

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
        Assert.Equal(TimeSpan.FromMinutes(5), options.PositiveCacheDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), options.NegativeCacheDuration);
        Assert.Equal(TimeSpan.FromHours(1), options.MaximumCacheDuration);
        Assert.Equal(DnssecValidationMode.None, options.DnssecValidationMode);
        Assert.Empty(options.CustomRecords);
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
    public void CustomDnsRecordProvider_ReturnsSingleARecord()
    {
        var provider = CreateCustomDnsRecordProvider(
            new CustomDnsRecordOption { Domain = "Sample.Local.", Type = "A", Value = "192.168.1.11" });
        var response = new DnsMessage();

        Assert.True(provider.TryApply(new DnsServerQuestion("sample.local", DnsQueryType.A), response));

        var answer = Assert.Single(response.Answers);
        Assert.Equal("sample.local", answer.Name);
        Assert.Equal(DnsQueryType.A, answer.Type);
        Assert.Equal(DnsQueryClass.IN, answer.Class);
        Assert.Equal(60u, answer.TimeToLive);
        Assert.Equal(IPAddress.Parse("192.168.1.11"), Assert.IsType<DnsARecordData>(answer.Data).Address);
    }

    [Fact]
    public void CustomDnsRecordProvider_ReturnsMultipleValuesForSameEntry()
    {
        var provider = CreateCustomDnsRecordProvider(
            new CustomDnsRecordOption
            {
                Domain = "sample.local",
                Type = "A",
                Values = ["192.168.1.11", "192.168.1.12"],
            });
        var response = new DnsMessage();

        Assert.True(provider.TryApply(new DnsServerQuestion("sample.local", DnsQueryType.A), response));

        Assert.Equal(
            [IPAddress.Parse("192.168.1.11"), IPAddress.Parse("192.168.1.12")],
            response.Answers.Select(answer => Assert.IsType<DnsARecordData>(answer.Data).Address).ToArray());
    }

    [Fact]
    public void CustomDnsRecordProvider_SelectsQuestionType()
    {
        var provider = CreateCustomDnsRecordProvider(
            new CustomDnsRecordOption { Domain = "sample.local", Type = "A", Value = "192.168.1.11" },
            new CustomDnsRecordOption { Domain = "sample.local", Type = "AAAA", Value = "::1" });
        var response = new DnsMessage();

        Assert.True(provider.TryApply(new DnsServerQuestion("sample.local", DnsQueryType.AAAA), response));

        var answer = Assert.Single(response.Answers);
        Assert.Equal(DnsQueryType.AAAA, answer.Type);
        Assert.Equal(IPAddress.IPv6Loopback, Assert.IsType<DnsAaaaRecordData>(answer.Data).Address);
    }

    [Fact]
    public void CustomDnsRecordProvider_AnyReturnsAllTypesForDomain()
    {
        var provider = CreateCustomDnsRecordProvider(
            new CustomDnsRecordOption { Domain = "sample.local", Type = "A", Value = "192.168.1.11" },
            new CustomDnsRecordOption { Domain = "sample.local", Type = "TXT", Value = "hello" },
            new CustomDnsRecordOption { Domain = "other.local", Type = "A", Value = "192.168.1.13" });
        var response = new DnsMessage();

        Assert.True(provider.TryApply(new DnsServerQuestion("sample.local", DnsQueryType.ANY), response));

        Assert.Equal([DnsQueryType.A, DnsQueryType.TXT], response.Answers.Select(answer => answer.Type).ToArray());
    }

    [Fact]
    public void CustomDnsRecordProvider_SkipsInvalidRecords()
    {
        var provider = CreateCustomDnsRecordProvider(
            new CustomDnsRecordOption { Domain = "sample.local", Type = "A", Value = "::1" },
            new CustomDnsRecordOption { Domain = "sample.local", Type = "NotAType", Value = "192.168.1.11" },
            new CustomDnsRecordOption { Domain = "", Type = "A", Value = "192.168.1.12" });
        var response = new DnsMessage();

        Assert.False(provider.TryApply(new DnsServerQuestion("sample.local", DnsQueryType.A), response));
        Assert.Empty(response.Answers);
    }

    [Fact]
    public void CustomDnsRecordProvider_SupportsStructuredRecordTypes()
    {
        var provider = CreateCustomDnsRecordProvider(
            new CustomDnsRecordOption { Domain = "mx.local", Type = "MX", Value = "10 mail.sample.local" },
            new CustomDnsRecordOption { Domain = "srv.local", Type = "SRV", Value = "20 30 443 service.sample.local" },
            new CustomDnsRecordOption { Domain = "caa.local", Type = "CAA", Value = "0 issue letsencrypt.org" },
            new CustomDnsRecordOption { Domain = "ns.local", Type = "NS", Value = "ns.sample.local" },
            new CustomDnsRecordOption { Domain = "ptr.local", Type = "PTR", Value = "host.sample.local" },
            new CustomDnsRecordOption { Domain = "cname.local", Type = "CNAME", Value = "target.sample.local" });

        AssertCustomRecord<DnsMxRecordData>(provider, "mx.local", DnsQueryType.MX, data =>
        {
            Assert.Equal(10, data.Preference);
            Assert.Equal("mail.sample.local", data.Exchange);
        });
        AssertCustomRecord<DnsSrvRecordData>(provider, "srv.local", DnsQueryType.SRV, data =>
        {
            Assert.Equal(20, data.Priority);
            Assert.Equal(30, data.Weight);
            Assert.Equal(443, data.Port);
            Assert.Equal("service.sample.local", data.Target);
        });
        AssertCustomRecord<DnsCaaRecordData>(provider, "caa.local", DnsQueryType.CAA, data =>
        {
            Assert.Equal(0, data.Flags);
            Assert.Equal("issue", data.Tag);
            Assert.Equal("letsencrypt.org", data.Value);
        });
        AssertCustomRecord<DnsNsRecordData>(provider, "ns.local", DnsQueryType.NS, data => Assert.Equal("ns.sample.local", data.NameServer));
        AssertCustomRecord<DnsPtrRecordData>(provider, "ptr.local", DnsQueryType.PTR, data => Assert.Equal("host.sample.local", data.DomainName));
        AssertCustomRecord<DnsCnameRecordData>(provider, "cname.local", DnsQueryType.CNAME, data => Assert.Equal("target.sample.local", data.CanonicalName));
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

    private static CustomDnsRecordProvider CreateCustomDnsRecordProvider(params CustomDnsRecordOption[] customRecords)
    {
        return new CustomDnsRecordProvider(
            Options.Create(new DnsProxyOptions { CustomRecords = [.. customRecords] }),
            NullLogger<CustomDnsRecordProvider>.Instance);
    }

    private static void AssertCustomRecord<T>(CustomDnsRecordProvider provider, string domain, DnsQueryType type, Action<T> assert)
        where T : DnsResourceRecordData
    {
        var response = new DnsMessage();

        Assert.True(provider.TryApply(new DnsServerQuestion(domain, type), response));
        assert(Assert.IsType<T>(Assert.Single(response.Answers).Data));
    }
}
