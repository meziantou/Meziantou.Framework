namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS NSEC3PARAM record for DNSSEC (RFC 5155).</summary>
public sealed class DnsNsec3ParamRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the hash algorithm.</summary>
    public byte HashAlgorithm { get; set; }

    /// <summary>Gets or sets the flags.</summary>
    public byte Flags { get; set; }

    /// <summary>Gets or sets the number of iterations.</summary>
    public ushort Iterations { get; set; }

    /// <summary>Gets or sets the salt.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Salt { get; set; } = [];
}
