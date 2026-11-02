using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// A signature of a document, as <see cref="PdfSignatureVerifier"/> found and checked it: the integrity of the bytes it
/// covers, the cryptographic signature, the time stamp and the information of the signature dictionary.
/// </summary>
/// <remarks>
/// The verification does not decide whether the signer is trusted: build the chain of <see cref="Certificate"/> with
/// <see cref="X509Chain"/> and the trust anchors and revocation policy of the application.
/// </remarks>
public sealed class PdfSignatureInfo
{
    internal PdfSignatureInfo()
    {
    }

    /// <summary>Gets the fully qualified name of the signature field.</summary>
    public string FieldName { get; internal set; } = "";

    /// <summary>Gets the format of the signature (<c>/SubFilter</c>): <c>ETSI.CAdES.detached</c>, <c>adbe.pkcs7.detached</c>, <c>adbe.pkcs7.sha1</c> or <c>ETSI.RFC3161</c>.</summary>
    public string? SubFilter { get; internal set; }

    /// <summary>Gets a value indicating whether the signature is a document time stamp (<c>ETSI.RFC3161</c>), not the signature of a person.</summary>
    public bool IsDocumentTimestamp => SubFilter == "ETSI.RFC3161";

    /// <summary>Gets the name of the signer (<c>/Name</c>), as it was signed.</summary>
    public string? SignerName { get; internal set; }

    /// <summary>Gets the reason of the signature (<c>/Reason</c>).</summary>
    public string? Reason { get; internal set; }

    /// <summary>Gets the place of the signature (<c>/Location</c>).</summary>
    public string? Location { get; internal set; }

    /// <summary>Gets the contact information of the signer (<c>/ContactInfo</c>).</summary>
    public string? ContactInfo { get; internal set; }

    /// <summary>Gets the time of the signature the signer claims (<c>/M</c>): it is not proven, unlike <see cref="TimestampTime"/>.</summary>
    public DateTimeOffset? SigningTime { get; internal set; }

    /// <summary>Gets the certificate of the signer, or of the time stamp authority of a document time stamp.</summary>
    public X509Certificate2? Certificate { get; internal set; }

    /// <summary>Gets the certificates the signature embeds.</summary>
    public X509Certificate2Collection Certificates { get; } = [];

    /// <summary>Gets the byte ranges of the file the signature covers: offsets and lengths.</summary>
    public IReadOnlyList<(long Offset, long Length)> ByteRanges { get; internal set; } = [];

    /// <summary>Gets a value indicating whether the signature covers the whole file: no revision was appended after it.</summary>
    public bool CoversWholeDocument { get; internal set; }

    /// <summary>Gets a value indicating whether the bytes the signature covers are those that were signed: their digest matches.</summary>
    public bool IsDocumentIntact { get; internal set; }

    /// <summary>Gets a value indicating whether the cryptographic signature is valid for the certificate of the signer.</summary>
    public bool IsSignatureValid { get; internal set; }

    /// <summary>Gets the time of the time stamp of the signature (PAdES B-T), the earliest valid one when it has several, or of a document time stamp.</summary>
    public DateTimeOffset? TimestampTime { get; internal set; }

    /// <summary>Gets a value indicating whether the time stamps are valid, all of them: <see langword="null"/> for a signature without time stamp.</summary>
    public bool? IsTimestampValid { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the ESS signing certificate attribute (RFC 5035) identifies <see cref="Certificate"/>:
    /// <see langword="null"/> when the signature has no such attribute and its format does not require it. A PAdES
    /// signature (<c>ETSI.CAdES.detached</c>) requires it.
    /// </summary>
    public bool? IsSigningCertificateValid { get; internal set; }

    /// <summary>Gets the changes the signature allows after it when it is a certification signature (DocMDP), or <see langword="null"/>.</summary>
    public PdfCertificationLevel? CertificationLevel { get; internal set; }

    /// <summary>Gets the fields the signature locks (FieldMDP), or <see langword="null"/>.</summary>
    public PdfFieldLock? FieldLock { get; internal set; }

    /// <summary>
    /// Gets the objects the revisions appended after the signature added or changed; empty when the signature covers the
    /// whole document. It is a summary of the changes for the application to review, built from the structure of the
    /// document, not a proof that the document shows the same content.
    /// </summary>
    public IReadOnlyList<PdfModification> Modifications { get; internal set; } = [];

    /// <summary>
    /// Gets a value indicating whether the <see cref="Modifications"/> are allowed by the certification level and the
    /// field lock of the signature. Without certification and lock, every change is allowed: check <see cref="CoversWholeDocument"/>.
    /// </summary>
    public bool AreModificationsPermitted { get; internal set; } = true;

    /// <summary>Gets the reason the signature could not be checked, or <see langword="null"/>.</summary>
    public string? Error { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the signature is valid: intact, cryptographically valid, with a valid time stamp
    /// when it has one, an ESS signing certificate that identifies the certificate of the signer when it has one, and no
    /// change after it that its certification or its field lock forbids.
    /// </summary>
    public bool IsValid => Error is null && IsDocumentIntact && IsSignatureValid && IsTimestampValid is null or true && IsSigningCertificateValid is null or true && AreModificationsPermitted;
}
