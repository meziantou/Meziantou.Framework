namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS TLSA record for DANE certificate association (RFC 6698).</summary>
public sealed class DnsTlsaRecord : DnsRecord
{
    /// <summary>Gets the certificate usage field (0-3).</summary>
    public byte CertificateUsage { get; internal set; }

    /// <summary>Gets the selector field (0-1).</summary>
    public byte Selector { get; internal set; }

    /// <summary>Gets the matching type field (0-2).</summary>
    public byte MatchingType { get; internal set; }

    /// <summary>Gets the certificate association data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] CertificateAssociationData { get; internal set; } = [];
}
