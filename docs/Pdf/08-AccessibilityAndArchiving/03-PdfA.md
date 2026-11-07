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
version, the actions, the annotations, the device color spaces), adds the sRGB output intent, and writes the XMP
metadata from the document information with the identification of the level. Set at least a title.

The XMP metadata keeps the properties of the existing packet that the information does not set, such as the custom
schema of an electronic invoice and its PDF/A extension schema: see [Metadata](../04-MetadataAndNavigation/02-Metadata.md).
A save that changes nothing in the metadata leaves its stream unchanged, so an incremental save does not rewrite it.

## What the save checks

Besides the rules of each level in the table below, the save refuses a document that:

- has actions that the level does not allow: Launch, Sound, Movie, ResetForm, ImportData, Hide, Rendition, Trans,
  SetOCGState and GoTo3DView; JavaScript before PDF/A-4; the named actions other than NextPage, PrevPage, FirstPage and
  LastPage. The actions of the document, of the pages, of the annotations, of the outline and of the form are checked.
- has additional actions (`/AA`) on the document, the pages or the fields (PDF/A-4: on the annotations, only those of
  the cursor and the focus), or an action on a widget.
- has an annotation without appearance (from PDF/A-2; the links, the popups and the annotations without size need none),
  or with an appearance other than the normal one.
- asks the viewer to generate the appearances of the form (`NeedAppearances`).
- uses DeviceRGB or DeviceCMYK without an output intent of the same color space or a `DefaultRGB` or `DefaultCMYK`
  color space. DeviceGray goes with any output intent. With the sRGB output intent, use an ICC-based color space for
  CMYK colors.
- uses a font without embedding it, including a font of the form that a viewer renders a field without appearance with.

The save does not generate the missing appearances: it reports them.

## Output intents

A document without output intent gets the sRGB one. A document that already has output intents, such as the one of
PDF/X with a CMYK profile, keeps them: the PDF/A output intent shares their profile, as PDF/A requires one profile for
every output intent. The profile is then the color space of the output intent: DeviceCMYK goes with a CMYK profile,
DeviceRGB does not.

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

[Alternative](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Alternative) says that the file is another representation of the content; [Data](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Data) (the data the content was made
from), [Source](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Source) and [Supplement](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Supplement) are the other relationships. A standard of electronic invoices names the file and the
relationship it requires.
