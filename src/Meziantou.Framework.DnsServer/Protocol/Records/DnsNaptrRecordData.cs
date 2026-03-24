namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS NAPTR (naming authority pointer) record (RFC 3403).</summary>
public sealed class DnsNaptrRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the order.</summary>
    public ushort Order { get; set; }

    /// <summary>Gets or sets the preference.</summary>
    public ushort Preference { get; set; }

    /// <summary>Gets or sets the flags.</summary>
    public string Flags { get; set; } = "";

    /// <summary>Gets or sets the services.</summary>
    public string Services { get; set; } = "";

    /// <summary>Gets or sets the regular expression.</summary>
    public string Regexp { get; set; } = "";

    /// <summary>Gets or sets the replacement domain name.</summary>
    public string Replacement { get; set; } = "";
}
