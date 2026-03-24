namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS CAA record for certification authority authorization (RFC 8659).</summary>
public sealed class DnsCaaRecord : DnsRecord
{
    /// <summary>Gets the flags byte.</summary>
    public byte Flags { get; internal set; }

    /// <summary>Gets the property tag (e.g., "issue", "issuewild", "iodef").</summary>
    public string Tag { get; internal set; } = "";

    /// <summary>Gets the property value.</summary>
    public string Value { get; internal set; } = "";
}
