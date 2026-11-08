using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The data that validates the signatures of a document long after they were made (PAdES B-LT, ETSI EN 319 142-1, 5.4):
/// the certificates of their chains and the revocation evidence, written to the document security store (ISO 32000-2,
/// 12.8.4.3) by <see cref="PdfSigner.AddValidationData(PdfDocument, PdfValidationData)"/>, and read by <see cref="PdfSignatureVerifier.GetValidationData"/>.
/// </summary>
public sealed class PdfValidationData
{
    /// <summary>Gets the certificates: the chains of the signers and of the time stamp authorities, and of the responders of the revocation evidence.</summary>
    public X509Certificate2Collection Certificates { get; } = [];

    /// <summary>Gets the certificate revocation lists, DER encoded (RFC 5280).</summary>
    public IList<byte[]> CertificateRevocationLists { get; } = [];

    /// <summary>Gets the OCSP responses, DER encoded <c>OCSPResponse</c> (RFC 6960).</summary>
    public IList<byte[]> OcspResponses { get; } = [];
}
