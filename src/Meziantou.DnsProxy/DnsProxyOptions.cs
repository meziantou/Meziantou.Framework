using Meziantou.Framework.DnsClient;

namespace Meziantou.DnsProxy;

internal sealed class DnsProxyOptions
{
    public const string SectionName = "DnsProxy";

    public int DnsPort { get; set; } = 5053;

    public int HttpPort { get; set; } = 5080;

    /// <summary>
    /// The addresses the DNS listeners bind to. Defaults to loopback only. Use <c>0.0.0.0</c> and <c>::</c> to serve other machines,
    /// in which case <see cref="AllowedClientNetworks"/> should also be configured.
    /// </summary>
    public List<string> BindAddresses { get; set; } = [];

    /// <summary>
    /// The client networks allowed to query the proxy, in CIDR notation (for instance <c>192.168.1.0/24</c>). A bare address is
    /// treated as a single host. When empty, every client that can reach a listener is allowed. Loopback clients are always allowed.
    /// </summary>
    public List<string> AllowedClientNetworks { get; set; } = [];

    public int DnsOverHttpsPort { get; set; }

    public int DnsOverTlsPort { get; set; }

    public int DnsOverQuicPort { get; set; }

    public string DnsOverHttpsPath { get; set; } = "/dns-query";

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public bool HasSecureServerListenerConfigured => DnsOverHttpsPort > 0 || DnsOverTlsPort > 0 || DnsOverQuicPort > 0;

    public int DiagnosticsHistoryCapacity { get; set; } = 10_000;

    public TimeSpan FilterRefreshInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The maximum time allowed to download a single filter list.</summary>
    public TimeSpan FilterDownloadTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The maximum size of a single filter list. Larger lists are rejected instead of being buffered in memory.</summary>
    public long MaxFilterListSizeInBytes { get; set; } = 64 * 1024 * 1024;

    public string BlockListCacheFolderPath { get; set; } = GetDefaultBlockListCacheFolderPath();

    public TimeSpan PositiveCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan NegativeCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaximumCacheDuration { get; set; } = TimeSpan.FromHours(1);

    public int MaxCacheEntries { get; set; } = 10_000;

    public int MaxDnsQueriesPerClientPerMinute { get; set; } = 600;

    public DnssecValidationMode DnssecValidationMode { get; set; }

    public List<string> BootstrapDnsServers { get; set; } = [];

    public List<UpstreamServerOption> Upstreams { get; set; } = [];

    public List<FilterListOption> Filters { get; set; } = [];

    public List<CustomDnsRecordOption> CustomRecords { get; set; } = [];

    /// <summary>
    /// Fills the collections that configuration did not provide.
    /// </summary>
    /// <remarks>
    /// These defaults cannot live in the property initializers: configuration binding <em>appends</em> to a
    /// pre-populated collection instead of replacing it, so every configured upstream, filter list and bootstrap
    /// server would be added on top of the defaults, and settings such as <c>DnsProxy__Upstreams__0__Url</c> would
    /// never override anything.
    /// </remarks>
    public void ApplyDefaults()
    {
        if (BindAddresses.Count == 0)
        {
            BindAddresses.AddRange(["127.0.0.1", "::1"]);
        }

        if (BootstrapDnsServers.Count == 0)
        {
            BootstrapDnsServers.AddRange(
            [
                "9.9.9.9",
                "149.112.112.112",
                "1.1.1.1",
                "1.0.0.1",
                "2620:fe::fe",
                "2620:fe::9",
                "2606:4700:4700::1111",
                "2606:4700:4700::1001",
            ]);
        }

        if (Upstreams.Count == 0)
        {
            Upstreams.AddRange(
            [
                new UpstreamServerOption { Name = "Cloudflare H3", Url = new("h3://cloudflare-dns.com/dns-query"), Priority = 0 },
                new UpstreamServerOption { Name = "NextDNS DoQ", Url = new("quic://dns.nextdns.io"), Priority = 1 },
                new UpstreamServerOption { Name = "Quad9 DoQ", Url = new("quic://dns.quad9.net"), Priority = 2 },
                new UpstreamServerOption { Name = "Cloudflare DoH", Url = new("https://cloudflare-dns.com/dns-query"), Priority = 3 },
                new UpstreamServerOption { Name = "NextDNS DoH", Url = new("https://dns.nextdns.io"), Priority = 4 },
                new UpstreamServerOption { Name = "Quad9 DoH", Url = new("https://dns.quad9.net/dns-query"), Priority = 5 },
            ]);
        }

        if (Filters.Count == 0)
        {
            Filters.AddRange(
            [
                new FilterListOption { Url = "https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt", Format = "AdBlock" },
                new FilterListOption { Url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts", Format = "Hosts" },
            ]);
        }
    }

    internal static string GetDefaultBlockListCacheFolderPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            localApplicationData = AppContext.BaseDirectory;
        }

        return Path.Combine(localApplicationData, "meziantou", "dnsproxy", "block-lists");
    }
}
