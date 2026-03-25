namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS DNAME (delegation name) record (RFC 6672).</summary>
public sealed class DnsDnameRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the target domain name.</summary>
    public string Target { get; set; } = "";
}
