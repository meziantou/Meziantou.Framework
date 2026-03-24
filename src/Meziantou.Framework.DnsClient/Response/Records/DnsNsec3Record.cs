using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS NSEC3 record for hashed authenticated denial of existence (RFC 5155, DNSSEC).</summary>
public sealed class DnsNsec3Record : DnsRecord
{
    /// <summary>Gets the hash algorithm.</summary>
    public byte HashAlgorithm { get; internal set; }

    /// <summary>Gets the flags.</summary>
    public byte Flags { get; internal set; }

    /// <summary>Gets the number of iterations.</summary>
    public ushort Iterations { get; internal set; }

    /// <summary>Gets the salt.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Salt { get; internal set; } = [];

    /// <summary>Gets the next hashed owner name.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] NextHashedOwnerName { get; internal set; } = [];

    /// <summary>Gets the set of RR types present at the original owner name.</summary>
    public IReadOnlyList<DnsQueryType> TypeBitMaps { get; internal set; } = [];
}
