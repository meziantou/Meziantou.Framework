namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS CNAME record containing a canonical name (RFC 1035).</summary>
public sealed class DnsCnameRecord : DnsRecord
{
    /// <summary>Gets the canonical domain name.</summary>
    public string CanonicalName { get; internal set; } = "";
}
