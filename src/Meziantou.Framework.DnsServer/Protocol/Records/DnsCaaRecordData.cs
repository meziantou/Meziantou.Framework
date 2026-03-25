namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS CAA (certification authority authorization) record (RFC 8659).</summary>
public sealed class DnsCaaRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the flags.</summary>
    public byte Flags { get; set; }

    /// <summary>Gets or sets the property tag (e.g., "issue", "issuewild", "iodef").</summary>
    public string Tag { get; set; } = "";

    /// <summary>Gets or sets the property value.</summary>
    public string Value { get; set; } = "";
}
