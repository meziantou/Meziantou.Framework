using System.Net;

namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS AAAA record containing an IPv6 address (RFC 3596).</summary>
public sealed class DnsAaaaRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the IPv6 address.</summary>
    public IPAddress Address { get; set; } = null!;
}
