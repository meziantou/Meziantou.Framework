namespace Meziantou.DnsProxy;

internal sealed class DnsProxyOptions
{
    public const string SectionName = "DnsProxy";

    public int DnsPort { get; set; } = 5053;

    public int HttpPort { get; set; } = 5080;

    public int DiagnosticsHistoryCapacity { get; set; } = 10_000;

    public TimeSpan FilterRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);

    public List<UpstreamServerOption> Upstreams { get; set; } =
    [
        new UpstreamServerOption { Name = "Cloudflare", Endpoint = "cloudflare-dns.com", Protocol = "Quic" },
        new UpstreamServerOption { Name = "Quad9", Endpoint = "dns.quad9.net", Protocol = "Quic" },
        new UpstreamServerOption { Name = "NextDNS", Endpoint = "dns.nextdns.io", Protocol = "Quic" },
    ];

    public List<FilterListOption> Filters { get; set; } =
    [
        new FilterListOption { Url = "https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt", Format = "AdBlock" },
        new FilterListOption { Url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts", Format = "Hosts" },
    ];

    public List<RewriteRuleOption> Rewrites { get; set; } = [];
}
