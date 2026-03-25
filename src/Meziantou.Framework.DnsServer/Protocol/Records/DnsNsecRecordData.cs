namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS NSEC (next secure) record for DNSSEC (RFC 4034).</summary>
public sealed class DnsNsecRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the next domain name.</summary>
    public string NextDomainName { get; set; } = "";

    /// <summary>Gets or sets the type bit maps indicating which record types exist.</summary>
    public IReadOnlyList<DnsQueryType> TypeBitMaps { get; set; } = [];
}
