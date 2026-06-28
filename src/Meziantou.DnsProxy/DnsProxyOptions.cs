using Meziantou.Framework.DnsClient;

namespace Meziantou.DnsProxy;

internal sealed class DnsProxyOptions
{
    public const string SectionName = "DnsProxy";

    public int DnsPort { get; set; } = 5053;

    public int HttpPort { get; set; } = 5080;

    public int DnsOverHttpsPort { get; set; }

    public int DnsOverTlsPort { get; set; }

    public int DnsOverQuicPort { get; set; }

    public string DnsOverHttpsPath { get; set; } = "/dns-query";

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public bool HasSecureServerListenerConfigured => DnsOverHttpsPort > 0 || DnsOverTlsPort > 0 || DnsOverQuicPort > 0;

    public int DiagnosticsHistoryCapacity { get; set; } = 10_000;

    public TimeSpan FilterRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);

    public DnssecValidationMode DnssecValidationMode { get; set; }

    public List<string> BootstrapDnsServers { get; set; } =
    [
        "9.9.9.9",
        "149.112.112.112",
        "1.1.1.1",
        "1.0.0.1",
        "2620:fe::fe",
        "2620:fe::9",
        "2606:4700:4700::1111",
        "2606:4700:4700::1001",
    ];

    public List<UpstreamServerOption> Upstreams { get; set; } =
    [
        new UpstreamServerOption { Name = "Cloudflare H3", Url = new("h3://cloudflare-dns.com/dns-query"), Priority = 0 },
        new UpstreamServerOption { Name = "NextDNS DoQ", Url = new("quic://dns.nextdns.io"), Priority = 1 },
        new UpstreamServerOption { Name = "Quad9 DoQ", Url = new("quic://dns.quad9.net"), Priority = 2 },
        new UpstreamServerOption { Name = "Cloudflare DoH", Url = new("https://cloudflare-dns.com/dns-query"), Priority = 3 },
        new UpstreamServerOption { Name = "NextDNS DoH", Url = new("https://dns.nextdns.io"), Priority = 4 },
        new UpstreamServerOption { Name = "Quad9 DoH", Url = new("https://dns.quad9.net/dns-query"), Priority = 5 },
    ];

    public List<FilterListOption> Filters { get; set; } =
    [
        new FilterListOption { Url = "https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt", Format = "AdBlock" },
        new FilterListOption { Url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts", Format = "Hosts" },
    ];

    public List<RewriteRuleOption> Rewrites { get; set; } = [];
}
