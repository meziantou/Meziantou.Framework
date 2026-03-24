namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS DS (delegation signer) record for DNSSEC (RFC 4034).</summary>
public sealed class DnsDsRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the key tag.</summary>
    public ushort KeyTag { get; set; }

    /// <summary>Gets or sets the algorithm number.</summary>
    public byte Algorithm { get; set; }

    /// <summary>Gets or sets the digest type.</summary>
    public byte DigestType { get; set; }

    /// <summary>Gets or sets the digest data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Digest { get; set; } = [];
}
