namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS RP record for responsible person (RFC 1183).
/// </summary>
public sealed class DnsRpRecord : DnsRecord
{
    /// <summary>Gets the mailbox domain name of the responsible person.</summary>
    public string Mailbox { get; internal set; } = "";

    /// <summary>Gets the domain name of a TXT record with additional information.</summary>
    public string TxtDomainName { get; internal set; } = "";
}
