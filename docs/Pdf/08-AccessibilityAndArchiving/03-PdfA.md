---
title: PDF/A
description: Write PDF/A documents for long-term archiving, choose the level, and attach the structured data of an invoice with PDF/A-3.
---

PDF/A is the PDF of long-term archiving: a document that shows the same in decades, without anything external (fonts,
color profiles) and without encryption or scripts.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_PdfA;format=md-fenced-code-block;dedent=true -->
```csharp
// PDF/A embeds the fonts: the standard 14 fonts cannot be used
var font = PdfFont.Load(fontData);
var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
document.Information.Title = "Archived";
document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Office", font, 24, 60, 700);

// The save adds the sRGB output intent and the PDF/A identification of the XMP metadata
var pdf = document.ToByteArray();
```
<!-- endref -->

The save checks what the library can check (no encryption, embedded fonts, a structure tree for the level A, the
version), adds the sRGB output intent, and writes the XMP metadata from the document information with the
identification of the level. Set at least a title.

## Choose the level

The part (1 to 4) is the version of PDF/A; the letter the level of conformance:

- **b** (basic): the document shows the same, but its text may not be extractable as Unicode.
- **u** (Unicode): b, and every text can be extracted as Unicode.
- **a** (accessible): u, and the document is tagged: see [Tagged PDF and PDF/UA](02-TaggedPdfAndPdfUA.md).

| Level | Based on | Specific rules the save checks or writes |
| --- | --- | --- |
| PDF/A-1b, PDF/A-1a | PDF 1.4 | A 1.4 header and a cross-reference table, without object streams; an sRGB output intent of version 2. No transparency (opacity, blend modes, soft masks, images with an alpha channel, transparency groups), no LZW or JPEG 2000 compression, no layers, no embedded files, no ICC profile of version 4 |
| PDF/A-2b, -2u, -2a | PDF 1.7 | The embedded files are PDF/A-1 or PDF/A-2 documents |
| PDF/A-3b, -3u, -3a | PDF 1.7 | The embedded files, of any format, have a MIME type and a relationship |
| PDF/A-4, PDF/A-4e | PDF 2.0 | No document information dictionary; the embedded files are PDF/A-1, -2 or -4 documents |
| PDF/A-4f | PDF 2.0 | PDF/A-4 with at least one embedded file, of any format |

PDF/A-2 suits most documents: it allows transparency and compression, which PDF/A-1 does not. Choose PDF/A-1 only when
an archive requires it, PDF/A-3 or PDF/A-4f to attach files of other formats, and PDF/A-4 for PDF 2.0.

## Attach structured data

Electronic invoices often carry their data as an XML file in a PDF/A-3 document: people read the pages, and programs the
XML. Give the file its MIME type and its relationship to the document:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_PdfA3Attachment;format=md-fenced-code-block;dedent=true -->
```csharp
var font = PdfFont.Load(fontData);
var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA3B };
document.Information.Title = "Invoice 2026-001";
document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Invoice 2026-001", font, 18, 60, 760);

// PDF/A-3 accepts embedded files of any format, with a MIME type and their relationship to the document
document.EmbeddedFiles.Add("invoice.xml", invoiceXml, new PdfEmbeddedFileOptions
{
    Description = "The invoice as structured data",
    MimeType = "application/xml",
    Relationship = PdfAssociatedFileRelationship.Alternative,
    ModificationDate = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero),
});

var pdf = document.ToByteArray();
```
<!-- endref -->

`Alternative` says that the file is another representation of the content; `Data` (the data the content was made
from), `Source` and `Supplement` are the other relationships. A standard of electronic invoices names the file and the
relationship it requires.
