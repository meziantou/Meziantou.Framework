using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS RRSIG record for DNSSEC signature (RFC 4034).</summary>
public sealed class DnsRrsigRecord : DnsRecord
{
    /// <summary>Gets the type of the RRset that is covered by this signature.</summary>
    public DnsQueryType TypeCovered { get; internal set; }

    /// <summary>Gets the cryptographic algorithm used to create the signature.</summary>
    public byte Algorithm { get; internal set; }

    /// <summary>Gets the number of labels in the original RRSIG owner name.</summary>
    public byte Labels { get; internal set; }

    /// <summary>Gets the original TTL of the covered RRset.</summary>
    public uint OriginalTtl { get; internal set; }

    /// <summary>Gets the signature expiration time as seconds since Unix epoch.</summary>
    public uint SignatureExpiration { get; internal set; }

    /// <summary>Gets the signature inception time as seconds since Unix epoch.</summary>
    public uint SignatureInception { get; internal set; }

    /// <summary>Gets the key tag of the DNSKEY record that created the signature.</summary>
    public ushort KeyTag { get; internal set; }

    /// <summary>Gets the signer's domain name.</summary>
    public string SignerName { get; internal set; } = "";

    /// <summary>Gets the cryptographic signature data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Signature { get; internal set; } = [];
}
