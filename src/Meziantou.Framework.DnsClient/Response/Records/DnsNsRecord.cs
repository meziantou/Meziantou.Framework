namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS NS record containing a name server (RFC 1035).
/// </summary>
public sealed class DnsNsRecord : DnsRecord
{
    /// <summary>Gets the name server domain name.</summary>
    public string NameServer { get; internal set; } = "";
}
