using System.Net;

namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS A record containing an IPv4 address (RFC 1035).</summary>
public sealed class DnsARecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the IPv4 address.</summary>
    public IPAddress Address { get; set; } = null!;
}
