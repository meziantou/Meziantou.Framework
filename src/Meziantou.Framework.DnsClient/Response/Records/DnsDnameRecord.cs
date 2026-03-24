namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS DNAME record for delegation name (RFC 6672).</summary>
public sealed class DnsDnameRecord : DnsRecord
{
    /// <summary>Gets the target domain name to which the queried name is aliased.</summary>
    public string Target { get; internal set; } = "";
}
