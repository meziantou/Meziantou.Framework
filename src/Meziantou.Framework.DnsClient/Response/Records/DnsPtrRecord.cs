namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS PTR record containing a domain name pointer (RFC 1035).
/// </summary>
public sealed class DnsPtrRecord : DnsRecord
{
    /// <summary>Gets the domain name pointed to.</summary>
    public string DomainName { get; internal set; } = "";
}
