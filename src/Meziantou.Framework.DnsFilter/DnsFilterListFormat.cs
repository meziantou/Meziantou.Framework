namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Specifies the format of a DNS filter list.
/// </summary>
public enum DnsFilterListFormat
{
    /// <summary>
    /// Automatically detect the format based on list content.
    /// </summary>
    AutoDetect,

    /// <summary>
    /// Hosts file format (e.g., <c>0.0.0.0 ads.example.com</c>).
    /// Used by Pi-hole, StevenBlack/hosts, and similar lists.
    /// </summary>
    Hosts,

    /// <summary>
    /// Domains-only format with one domain per line.
    /// </summary>
    DomainsOnly,

    /// <summary>
    /// AdGuard/Adblock DNS filtering syntax (e.g., <c>||example.org^</c>).
    /// Supports modifiers like <c>$important</c>, <c>$dnstype</c>, <c>$denyallow</c>, <c>$dnsrewrite</c>, <c>$client</c>, and <c>$ctag</c>.
    /// </summary>
    AdBlock,
}
