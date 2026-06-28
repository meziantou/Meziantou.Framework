namespace Meziantou.DnsProxy;

internal sealed class UpstreamServerOption
{
    public string Name { get; set; } = "";

    public Uri Url { get; set; } = new("https://cloudflare-dns.com/dns-query");

    public int Priority { get; set; }
}
