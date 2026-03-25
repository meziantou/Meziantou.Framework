namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS MX (mail exchange) record (RFC 1035).</summary>
public sealed class DnsMxRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the preference value (lower is preferred).</summary>
    public ushort Preference { get; set; }

    /// <summary>Gets or sets the mail exchange domain name.</summary>
    public string Exchange { get; set; } = "";
}
