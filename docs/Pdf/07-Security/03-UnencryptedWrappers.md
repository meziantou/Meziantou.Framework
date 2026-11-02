---
title: Unencrypted wrapper documents
description: Embed an encrypted PDF document in a PDF 2.0 document that any reader opens, and find and open the payload of such a document.
---

An unencrypted wrapper document (ISO 32000-2, 7.6.7) is a document that is not encrypted, whose pages any reader shows,
and that embeds an encrypted PDF document: the encrypted payload. It is used when the payload is encrypted with a
cryptographic filter that not every reader has: the pages of the wrapper tell the readers without it how to open the
payload.

## Create a wrapper

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_UnencryptedWrapper;format=md-fenced-code-block;dedent=true -->
```csharp
// The wrapper is not encrypted: its pages tell a reader without the cryptographic filter how to open the payload
var wrapper = new PdfDocument();
wrapper.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .DrawText("This document is encrypted: open it with a PDF 2.0 reader", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 60, 700);
wrapper.SetEncryptedPayload("document.pdf", encryptedPdf, cryptographicFilter: "Standard");
var file = wrapper.ToByteArray();

// A reader finds the payload, and opens it with its password
var payload = PdfDocument.Open(file).EncryptedPayload;
var document = payload?.OpenDocument(new PdfReadOptions { Password = "password" });
```
<!-- endref -->

[SetEncryptedPayload](cref:Meziantou.Framework.Pdf.PdfDocument.SetEncryptedPayload) embeds the payload as a file whose relationship is
[PdfAssociatedFileRelationship.EncryptedPayload](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.EncryptedPayload), with the name of its cryptographic filter, and names it in the collection of the
catalog, which hides the list of the files. The document is saved as PDF 2.0 at least.

- The payload is embedded as it is: encrypt it first, for instance with [PdfDocument.Encryption](cref:Meziantou.Framework.Pdf.PdfDocument.Encryption).
- Do not encrypt the wrapper itself: it would not be an unencrypted wrapper any more.
- A document has one encrypted payload: a second call throws `InvalidOperationException`.

## Open the payload of a wrapper

[PdfDocument.EncryptedPayload](cref:Meziantou.Framework.Pdf.PdfDocument.EncryptedPayload) is `null` for a document that is not a wrapper. Otherwise:

- [CryptographicFilter](cref:Meziantou.Framework.Pdf.PdfEncryptedPayload.CryptographicFilter) and [Version](cref:Meziantou.Framework.Pdf.PdfEncryptedPayload.Version) tell which cryptographic filter encrypts the payload.
- [GetData](cref:Meziantou.Framework.Pdf.PdfEncryptedPayload.GetData) gives the bytes of the payload, decoded with the read options of the wrapper: its
  [MaxDecodedStreamLength](cref:Meziantou.Framework.Pdf.PdfReadOptions.MaxDecodedStreamLength) bounds the payload too.
- [OpenDocument](cref:Meziantou.Framework.Pdf.PdfEncryptedPayload.OpenDocument) opens the payload with its own options and password. The library opens the payloads
  encrypted with the standard security handler (passwords); another security handler throws
  [UnsupportedPdfFeatureException](cref:Meziantou.Framework.Pdf.UnsupportedPdfFeatureException).
