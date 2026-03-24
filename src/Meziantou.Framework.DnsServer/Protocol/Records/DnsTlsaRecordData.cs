namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS TLSA (certificate association) record for DANE (RFC 6698).</summary>
public sealed class DnsTlsaRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the certificate usage field.</summary>
    public byte CertificateUsage { get; set; }

    /// <summary>Gets or sets the selector field.</summary>
    public byte Selector { get; set; }

    /// <summary>Gets or sets the matching type field.</summary>
    public byte MatchingType { get; set; }

    /// <summary>Gets or sets the certificate association data.</summary>
    public byte[] CertificateAssociationData { get; set; } = [];
}
