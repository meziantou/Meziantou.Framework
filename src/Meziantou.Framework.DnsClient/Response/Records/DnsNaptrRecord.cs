namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS NAPTR record for naming authority pointer (RFC 3403).</summary>
public sealed class DnsNaptrRecord : DnsRecord
{
    /// <summary>Gets the order value.</summary>
    public ushort Order { get; internal set; }

    /// <summary>Gets the preference value.</summary>
    public ushort Preference { get; internal set; }

    /// <summary>Gets the flags string.</summary>
    public string Flags { get; internal set; } = "";

    /// <summary>Gets the service string.</summary>
    public string Services { get; internal set; } = "";

    /// <summary>Gets the regular expression.</summary>
    public string Regexp { get; internal set; } = "";

    /// <summary>Gets the replacement domain name.</summary>
    public string Replacement { get; internal set; } = "";
}
