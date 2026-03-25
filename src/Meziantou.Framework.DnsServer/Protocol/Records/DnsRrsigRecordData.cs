namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS RRSIG (DNSSEC signature) record (RFC 4034).</summary>
public sealed class DnsRrsigRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the type covered by this signature.</summary>
    public DnsQueryType TypeCovered { get; set; }

    /// <summary>Gets or sets the algorithm number.</summary>
    public byte Algorithm { get; set; }

    /// <summary>Gets or sets the number of labels in the original RRSIG RR owner name.</summary>
    public byte Labels { get; set; }

    /// <summary>Gets or sets the original TTL.</summary>
    public uint OriginalTtl { get; set; }

    /// <summary>Gets or sets the signature expiration time (seconds since epoch).</summary>
    public uint SignatureExpiration { get; set; }

    /// <summary>Gets or sets the signature inception time (seconds since epoch).</summary>
    public uint SignatureInception { get; set; }

    /// <summary>Gets or sets the key tag.</summary>
    public ushort KeyTag { get; set; }

    /// <summary>Gets or sets the signer's name.</summary>
    public string SignerName { get; set; } = "";

    /// <summary>Gets or sets the signature data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Signature { get; set; } = [];
}
