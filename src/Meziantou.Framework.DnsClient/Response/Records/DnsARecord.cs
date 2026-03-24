using System.Net;

namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS A record containing an IPv4 address (RFC 1035).
/// </summary>
public sealed class DnsARecord : DnsRecord
{
    /// <summary>Gets the IPv4 address.</summary>
    public IPAddress Address { get; internal set; } = null!;
}
