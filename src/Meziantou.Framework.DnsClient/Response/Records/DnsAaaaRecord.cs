using System.Net;

namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS AAAA record containing an IPv6 address (RFC 3596).</summary>
public sealed class DnsAaaaRecord : DnsRecord
{
    /// <summary>Gets the IPv6 address.</summary>
    public IPAddress Address { get; internal set; } = null!;
}
