namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS NS (name server) record (RFC 1035).</summary>
public sealed class DnsNsRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the name server domain name.</summary>
    public string NameServer { get; set; } = "";
}
