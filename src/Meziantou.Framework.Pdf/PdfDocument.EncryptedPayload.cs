using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Gets the encrypted payload of the document when it is an unencrypted wrapper document (ISO 32000-2, 7.6.7), or
    /// <see langword="null"/>: the embedded file whose relationship is <see cref="PdfAssociatedFileRelationship.EncryptedPayload"/>
    /// and that has an encrypted payload dictionary, the one the <c>/Collection</c> of the catalog names first.
    /// </summary>
    public PdfEncryptedPayload? EncryptedPayload
    {
        get
        {
            var name = Catalog.Get<PdfDictionary>(PdfEncryptedPayload.CollectionName)?.Get<PdfString>(PdfNames.D)?.ToText();
            PdfEncryptedPayload? found = null;
            foreach (var file in EmbeddedFiles)
            {
                if (file.Relationship != PdfAssociatedFileRelationship.EncryptedPayload || file.Dictionary.Get<PdfDictionary>(PdfEncryptedPayload.EPName) is not { } dictionary)
                    continue;

                var payload = new PdfEncryptedPayload(file, dictionary);
                if (name is null || string.Equals(file.FileName, name, StringComparison.Ordinal))
                    return payload;

                found ??= payload;
            }

            return found;
        }
    }

    /// <summary>
    /// Makes the document an unencrypted wrapper document (ISO 32000-2, 7.6.7): embeds an encrypted PDF document as its
    /// encrypted payload. The pages of the document are what a reader without the cryptographic filter shows: add a page
    /// that explains how to open the payload. The document is saved as PDF 2.0 at least, without encryption.
    /// </summary>
    /// <param name="fileName">The name of the payload file, such as <c>document.pdf</c>.</param>
    /// <param name="data">The encrypted PDF document.</param>
    /// <param name="cryptographicFilter">The name of the cryptographic filter the payload is encrypted with (<c>/Subtype</c> of the encrypted payload dictionary).</param>
    /// <param name="version">The version of the cryptographic filter, or <see langword="null"/>.</param>
    /// <param name="options">The description and the dates of the payload file, or <see langword="null"/>: its MIME type is <c>application/pdf</c> and its relationship <see cref="PdfAssociatedFileRelationship.EncryptedPayload"/>, whatever the options say.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> or <paramref name="cryptographicFilter"/> is empty, or <paramref name="fileName"/> is the name of another file of the document.</exception>
    /// <exception cref="InvalidOperationException">The document already has an encrypted payload.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs" ref:region="Pdf_UnencryptedWrapper" ref:dedent="true">
    /// // The wrapper is not encrypted: its pages tell a reader without the cryptographic filter how to open the payload
    /// var wrapper = new PdfDocument();
    /// wrapper.Pages.Add(PdfSize.A4).CreateContentBuilder()
    ///     .DrawText("This document is encrypted: open it with a PDF 2.0 reader", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 60, 700);
    /// wrapper.SetEncryptedPayload("document.pdf", encryptedPdf, cryptographicFilter: "Standard");
    /// var file = wrapper.ToByteArray();
    ///
    /// // A reader finds the payload, and opens it with its password
    /// var payload = PdfDocument.Open(file).EncryptedPayload;
    /// var document = payload?.OpenDocument(new PdfReadOptions { Password = "password" });
    /// </code>
    /// </example>
    public PdfEncryptedPayload SetEncryptedPayload(string fileName, ReadOnlySpan<byte> data, string cryptographicFilter, string? version = null, PdfEmbeddedFileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentException.ThrowIfNullOrEmpty(cryptographicFilter);
        if (EncryptedPayload is not null)
            throw new InvalidOperationException("The document already has an encrypted payload");

        var file = EmbeddedFiles.Add(fileName, data, new PdfEmbeddedFileOptions
        {
            Description = options?.Description,
            CreationDate = options?.CreationDate,
            ModificationDate = options?.ModificationDate,
            MimeType = "application/pdf",
            Relationship = PdfAssociatedFileRelationship.EncryptedPayload,
        });

        // Table 28: the encrypted payload dictionary
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfEncryptedPayload.EncryptedPayloadName,
            [PdfNames.Subtype] = PdfName.Create(cryptographicFilter),
        };
        if (version is not null)
        {
            dictionary[PdfNames.Version] = PdfName.Create(version);
        }

        file.Dictionary[PdfEncryptedPayload.EPName] = dictionary;

        // 7.6.7: the collection names the payload and hides the list of the files (12.3.5)
        Catalog[PdfEncryptedPayload.CollectionName] = new PdfDictionary
        {
            [PdfNames.Type] = PdfEncryptedPayload.CollectionName,
            [PdfNames.D] = PdfString.FromText(fileName),
            [PdfEncryptedPayload.ViewName] = PdfName.Create("H"),
        };

        if (Version < PdfVersion.Pdf20)
        {
            Version = PdfVersion.Pdf20;
        }

        return new PdfEncryptedPayload(file, dictionary);
    }
}
