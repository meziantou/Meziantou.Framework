namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Represents a <c>$dnsrewrite</c> directive that replaces the DNS response for a matched query.
/// </summary>
public sealed class DnsFilterRewriteRule
{
    /// <summary>
    /// Gets the response code to use. Defaults to <see cref="DnsFilterRewriteResponseCode.NoError"/>.
    /// </summary>
    public DnsFilterRewriteResponseCode ResponseCode { get; init; }

    /// <summary>
    /// Gets the DNS record type for the rewritten response (e.g., <c>A</c>, <c>AAAA</c>, <c>CNAME</c>).
    /// May be <see langword="null"/> when only the response code is set.
    /// </summary>
    public DnsFilterQueryType? RecordType { get; init; }

    /// <summary>
    /// Gets the value for the rewritten response (e.g., an IP address or domain name).
    /// May be <see langword="null"/> when only the response code is set.
    /// </summary>
    public string? Value { get; init; }
}
