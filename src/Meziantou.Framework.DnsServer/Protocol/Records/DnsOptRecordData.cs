namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS OPT pseudo-record for EDNS (RFC 6891).</summary>
public sealed class DnsOptRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the EDNS options.</summary>
    public IReadOnlyList<DnsEdnsOption> Options { get; set; } = [];
}
