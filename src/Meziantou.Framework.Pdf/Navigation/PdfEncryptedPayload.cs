using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The encrypted payload of an unencrypted wrapper document (ISO 32000-2, 7.6.7): an encrypted PDF document embedded in
/// a document that is not encrypted, so that a reader without the cryptographic filter of the payload can show the pages
/// of the wrapper (usually a page that explains how to open the payload).
/// </summary>
/// <remarks>
/// The file specification of the payload has the relationship <see cref="PdfAssociatedFileRelationship.EncryptedPayload"/>
/// and an encrypted payload dictionary (<c>/EP</c>, Table 28) naming the cryptographic filter; the <c>/Collection</c> of
/// the catalog names the payload (<c>/D</c>) and hides the list of the files (<c>/View /H</c>).
/// </remarks>
public sealed class PdfEncryptedPayload
{
    internal static readonly PdfName EPName = PdfName.Create("EP");
    internal static readonly PdfName EncryptedPayloadName = PdfName.Create("EncryptedPayload");
    internal static readonly PdfName CollectionName = PdfName.Create("Collection");
    internal static readonly PdfName ViewName = PdfName.Create("View");

    internal PdfEncryptedPayload(PdfEmbeddedFile file, PdfDictionary dictionary)
    {
        File = file;
        Dictionary = dictionary;
    }

    /// <summary>Gets the embedded file that holds the payload.</summary>
    public PdfEmbeddedFile File { get; }

    /// <summary>Gets the encrypted payload dictionary (<c>/EP</c> of the file specification).</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the name of the cryptographic filter the payload is encrypted with (<c>/Subtype</c>), so that a reader can tell whether it can open it.</summary>
    public string CryptographicFilter => Dictionary.GetName(PdfNames.Subtype)?.Value ?? "";

    /// <summary>Gets the version of the cryptographic filter (<c>/Version</c>), or <see langword="null"/>.</summary>
    public string? Version => Dictionary.GetName(PdfNames.Version)?.Value;

    /// <summary>Gets the bytes of the payload document.</summary>
    /// <returns>The bytes, decoded with the limits of the wrapper document.</returns>
    /// <exception cref="InvalidPdfContentException">The file specification has no embedded file stream, or the stream cannot be decoded.</exception>
    /// <exception cref="PdfResourceLimitException">The decoded payload exceeds <see cref="PdfReadOptions.MaxDecodedStreamLength"/> of the wrapper document.</exception>
    public byte[] GetData() => File.GetData();

    /// <summary>Reads the payload document.</summary>
    /// <param name="options">The options of the payload, with its password; <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The payload document.</returns>
    /// <exception cref="InvalidPdfContentException">The payload is not a PDF document.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The payload is encrypted with a security handler the library does not support: only the standard security handler is.</exception>
    /// <exception cref="PdfPasswordException">The password of the options does not open the payload.</exception>
    public PdfDocument OpenDocument(PdfReadOptions? options = null) => PdfDocument.Open(GetData(), options);
}
