using System.Net;
using System.Reflection;
using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.Framework.DnsClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using DnsClientType = Meziantou.Framework.DnsClient.DnsClient;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class UpstreamDnsClientFactoryTests
{
    private static readonly FieldInfo DnsClientOptionsField = typeof(DnsClientType).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!;

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
                    Url = new Uri("https://1.1.1.1/dns-query"),
                },
            ],
        });

        using var factory = new UpstreamDnsClientFactory(options, NullLogger<UpstreamDnsClientFactory>.Instance);
        var upstreams = factory.GetUpstreams();

        var upstream = Assert.Single(upstreams);
        Assert.Equal("Custom (https://1.1.1.1/dns-query)", upstream.DisplayName);
        Assert.Equal("https://1.1.1.1/dns-query", upstream.Endpoint);
    }

    [Fact]
    public void UpstreamDnsClientFactory_UsesConfiguredDnssecValidationMode()
    {
        var options = Options.Create(new DnsProxyOptions
        {
            DnssecValidationMode = DnssecValidationMode.Local,
            Upstreams =
            [
                new UpstreamServerOption
                {
                    Name = "Custom",
                    Url = new Uri("https://1.1.1.1/dns-query"),
                },
            ],
        });

        using var factory = new UpstreamDnsClientFactory(options, NullLogger<UpstreamDnsClientFactory>.Instance);
        var upstream = Assert.Single(factory.GetUpstreams());
        var clientOptions = Assert.IsType<DnsClientOptions>(DnsClientOptionsField.GetValue(upstream.Client));

        Assert.Equal(DnssecValidationMode.Local, clientOptions.DnssecValidationMode);
    }

    [Theory]
    [InlineData("Cloudflare DoH", "https://cloudflare-dns.com/dns-query", "https://cloudflare-dns.com/dns-query")]
    [InlineData("Quad9 DoH", "https://dns.quad9.net/dns-query", "https://dns.quad9.net/dns-query")]
    [InlineData("NextDNS DoH", "https://dns.nextdns.io", "https://dns.nextdns.io/")]
    public void UpstreamDnsClientFactory_DefaultDohUpstream_CreatesClient(string name, string url, string expectedEndpoint)
    {
        var options = Options.Create(new DnsProxyOptions
        {
            BootstrapDnsServers = [],
            Upstreams =
            [
                new UpstreamServerOption
                {
                    Name = name,
                    Url = new Uri(url),
                },
            ],
        });

        using var factory = new UpstreamDnsClientFactory(options, NullLogger<UpstreamDnsClientFactory>.Instance);
        var upstream = Assert.Single(factory.GetUpstreams());

        Assert.Equal($"{name} ({url})", upstream.DisplayName);
        Assert.Equal(expectedEndpoint, upstream.Endpoint);
        Assert.NotNull(upstream.Client);
    }

    [Fact]
    public void UpstreamDnsClientFactory_UsesBootstrapDnsServers()
    {
        var options = Options.Create(new DnsProxyOptions
        {
            BootstrapDnsServers = ["127.0.0.1"],
            Upstreams =
            [
                new UpstreamServerOption
                {
                    Name = "Custom",
                    Url = new Uri("https://dns.example/dns-query"),
                },
            ],
        });

        using var factory = new UpstreamDnsClientFactory(options, NullLogger<UpstreamDnsClientFactory>.Instance);
        var upstream = Assert.Single(factory.GetUpstreams());
        var clientOptions = Assert.IsType<DnsClientOptions>(DnsClientOptionsField.GetValue(upstream.Client));
        var resolver = Assert.IsType<Func<string, IReadOnlyList<IPAddress>>>(clientOptions.ServerAddressResolver);

        Assert.Equal([IPAddress.Parse("192.0.2.1")], resolver("192.0.2.1"));
    }
}
