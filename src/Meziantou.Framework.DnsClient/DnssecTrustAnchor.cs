namespace Meziantou.Framework.DnsClient;

/// <summary>Represents a DNSSEC trust anchor as a DS-style key digest.</summary>
public sealed class DnssecTrustAnchor
{
    /// <summary>Initializes a new instance of the <see cref="DnssecTrustAnchor"/> class.</summary>
    /// <param name="name">The DNS name the trust anchor applies to.</param>
    /// <param name="keyTag">The DNSKEY key tag.</param>
    /// <param name="algorithm">The DNSSEC algorithm number.</param>
    /// <param name="digestType">The DS digest type.</param>
    /// <param name="digest">The DS digest bytes.</param>
    public DnssecTrustAnchor(string name, ushort keyTag, byte algorithm, byte digestType, byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(digest);

        Name = name;
        KeyTag = keyTag;
        Algorithm = algorithm;
        DigestType = digestType;
        Digest = digest.AsSpan().ToArray();
    }

    /// <summary>Gets the DNS name the trust anchor applies to.</summary>
    public string Name { get; }

    /// <summary>Gets the DNSKEY key tag.</summary>
    public ushort KeyTag { get; }

    /// <summary>Gets the DNSSEC algorithm number.</summary>
    public byte Algorithm { get; }

    /// <summary>Gets the DS digest type.</summary>
    public byte DigestType { get; }

    /// <summary>Gets the DS digest bytes.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Digest { get; }
}
