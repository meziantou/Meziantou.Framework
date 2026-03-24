using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS NSEC record for authenticated denial of existence (RFC 4034, DNSSEC).
/// </summary>
public sealed class DnsNsecRecord : DnsRecord
{
    /// <summary>Gets the next owner name in the canonical ordering of the zone.</summary>
    public string NextDomainName { get; internal set; } = "";

    /// <summary>Gets the set of RR types present at the NSEC owner name.</summary>
    public IReadOnlyList<DnsQueryType> TypeBitMaps { get; internal set; } = [];
}
