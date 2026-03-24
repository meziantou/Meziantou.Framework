namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS RP (responsible person) record (RFC 1183).</summary>
public sealed class DnsRpRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the mailbox domain name.</summary>
    public string Mailbox { get; set; } = "";

    /// <summary>Gets or sets the domain name of a TXT record with more information.</summary>
    public string TxtDomainName { get; set; } = "";
}
