namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS PTR (pointer) record (RFC 1035).</summary>
public sealed class DnsPtrRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the domain name.</summary>
    public string DomainName { get; set; } = "";
}
