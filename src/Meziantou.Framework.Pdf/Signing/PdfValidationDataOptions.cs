using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The options of <see cref="PdfSigner.AddValidationDataAsync"/>: the validation data the application has, and whether
/// the missing data is downloaded from the addresses of the certificates (PAdES B-LT, ETSI EN 319 142-1, 5.4).
/// </summary>
public sealed class PdfValidationDataOptions
{
    /// <summary>
    /// Gets the certificates that complete the chains of the signatures, of their time stamps and of the document time
    /// stamps (intermediate and root certificates), and any other certificate to store.
    /// </summary>
    public X509Certificate2Collection Certificates { get; } = [];

    /// <summary>Gets the certificate revocation lists, DER encoded (RFC 5280).</summary>
    public IList<byte[]> CertificateRevocationLists { get; } = [];

    /// <summary>Gets the OCSP responses, DER encoded <c>OCSPResponse</c> (RFC 6960).</summary>
    public IList<byte[]> OcspResponses { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the data that is missing is downloaded: the issuers of the certificates from
    /// their <c>caIssuers</c> addresses (RFC 5280, 4.2.2.1), and the revocation evidence of each certificate that is
    /// not self-signed, an OCSP response from its responder (RFC 6960) or else the revocation lists of its distribution
    /// points (RFC 5280, 4.2.1.13). The default is <see langword="false"/>: nothing is downloaded.
    /// </summary>
    public bool DownloadMissingData { get; init; }

    /// <summary>Gets the client the data is downloaded with; <see langword="null"/> for a client of the library.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    /// Gets a value indicating whether the store gets a VRI dictionary for each signature and document time stamp
    /// (ISO 32000-2, 12.8.4.4): the certificates and the revocation evidence of its chains. The default is <see langword="false"/>.
    /// </summary>
    public bool IncludeVri { get; init; }
}
