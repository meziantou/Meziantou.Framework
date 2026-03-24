namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS SSHFP (SSH fingerprint) record (RFC 4255).</summary>
public sealed class DnsSshfpRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the algorithm number.</summary>
    public byte Algorithm { get; set; }

    /// <summary>Gets or sets the fingerprint type.</summary>
    public byte FingerprintType { get; set; }

    /// <summary>Gets or sets the fingerprint data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Fingerprint { get; set; } = [];
}
