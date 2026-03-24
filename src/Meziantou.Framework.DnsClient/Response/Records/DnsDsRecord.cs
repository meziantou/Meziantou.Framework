namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS DS record for delegation signer (RFC 4034, DNSSEC).</summary>
public sealed class DnsDsRecord : DnsRecord
{
    /// <summary>Gets the key tag of the referenced DNSKEY record.</summary>
    public ushort KeyTag { get; internal set; }

    /// <summary>Gets the algorithm number of the referenced DNSKEY record.</summary>
    public byte Algorithm { get; internal set; }

    /// <summary>Gets the digest type.</summary>
    public byte DigestType { get; internal set; }

    /// <summary>Gets the digest data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Digest { get; internal set; } = [];
}
