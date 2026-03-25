namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS CNAME record (RFC 1035).</summary>
public sealed class DnsCnameRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the canonical name.</summary>
    public string CanonicalName { get; set; } = "";
}
