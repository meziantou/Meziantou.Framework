namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS MX record containing mail exchange information (RFC 1035).</summary>
public sealed class DnsMxRecord : DnsRecord
{
    /// <summary>Gets the preference value. Lower values are preferred.</summary>
    public ushort Preference { get; internal set; }

    /// <summary>Gets the mail exchange server domain name.</summary>
    public string Exchange { get; internal set; } = "";
}
