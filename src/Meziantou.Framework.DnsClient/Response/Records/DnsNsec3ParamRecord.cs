namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS NSEC3PARAM record (RFC 5155, DNSSEC).
/// </summary>
public sealed class DnsNsec3ParamRecord : DnsRecord
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
}
