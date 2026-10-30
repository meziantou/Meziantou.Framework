---
title: Metadata
description: Set and read the title, author, subject, keywords and dates of a document, its custom entries, its XMP metadata and its language.
---

Readers show the title of a document in their window and its properties in a dialog; search engines and document
management systems index them. Set them before saving:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_Metadata;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument { Language = "en-US" };
document.Information.Title = "Quarterly report";
document.Information.Author = "Finance team";
document.Information.Subject = "The results of the third quarter";
document.Information.Keywords = "finance, report";
document.Information.CreationDate = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
document.Information.SetText("Department", "Finance"); // a custom entry
document.Pages.Add(PdfSize.A4);

// PDF 2.0 prefers the XMP metadata: write it from the information above
document.UpdateXmpMetadata();
var pdf = document.ToByteArray();

var read = PdfDocument.Open(pdf);
var title = read.Information.Title;
var department = read.Information.GetText("Department");
var xmp = read.GetXmpMetadata(); // the XMP packet: UTF-8 XML, or null
```
<!-- endref -->

## The document information

[PdfDocument.Information](cref:Meziantou.Framework.Pdf.PdfDocument.Information) reads and writes the document information dictionary:

- [Title](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.Title), [Author](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.Author), [Subject](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.Subject), [Keywords](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.Keywords), [Creator](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.Creator) (the application that created the original document) and
  [Producer](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.Producer) (the application that converted it to PDF).
- [CreationDate](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.CreationDate) and [ModificationDate](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.ModificationDate): `null` when they are missing or are not valid dates.
- [GetText](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.GetText) and [SetText](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.SetText) read and write any other entry, such as a custom one.

Setting a value to `null` removes the entry. The library sets none of them by itself, not even the dates or the producer,
so that the same document gives the same file: set [CreationDate](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.CreationDate) and [ModificationDate](cref:Meziantou.Framework.Pdf.PdfDocumentInformation.ModificationDate) when you want them.

## XMP metadata

PDF 2.0 deprecates every entry of the document information but the dates, in favor of the XMP metadata, an XML packet
that other tools read too.

| Method | Does |
| --- | --- |
| [UpdateXmpMetadata](cref:Meziantou.Framework.Pdf.PdfDocument.UpdateXmpMetadata) | Replaces the XMP metadata with a packet built from [Information](cref:Meziantou.Framework.Pdf.PdfDocument.Information): title, author, subject, keywords, producer, creator tool and dates |
| [GetXmpMetadata](cref:Meziantou.Framework.Pdf.PdfDocument.GetXmpMetadata) | Returns the packet (UTF-8 XML), or `null` when the document has none |
| [SetXmpMetadata](cref:Meziantou.Framework.Pdf.PdfDocument.SetXmpMetadata) | Sets a packet you wrote |
| [RemoveXmpMetadata](cref:Meziantou.Framework.Pdf.PdfDocument.RemoveXmpMetadata) | Removes it |

Call [UpdateXmpMetadata](cref:Meziantou.Framework.Pdf.PdfDocument.UpdateXmpMetadata) after the information is set: it does not follow later changes. A PDF/A document gets its XMP
metadata from the information when it is saved, with the identification of its level: see
[PDF/A](../08-AccessibilityAndArchiving/03-PdfA.md).

## Language

[PdfDocument.Language](cref:Meziantou.Framework.Pdf.PdfDocument.Language) is the natural language of the document, a BCP 47 tag such as `en-US` or `fr-FR`. Screen readers
use it to choose their pronunciation, and PDF/UA requires it. An element of the structure tree can have a language of
its own: see [Tagged PDF and PDF/UA](../08-AccessibilityAndArchiving/02-TaggedPdfAndPdfUA.md).

## Version

[PdfDocument.Version](cref:Meziantou.Framework.Pdf.PdfDocument.Version) is the version of the document: the version of the header of the file, or the version of the
catalog when it is later. It can be set; a save raises it when the document or the options need a later version (AES-256
encryption needs PDF 2.0, for instance). [PdfVersion](cref:Meziantou.Framework.Pdf.PdfVersion) has the versions from [Pdf14](cref:Meziantou.Framework.Pdf.PdfVersion.Pdf14) to [Pdf20](cref:Meziantou.Framework.Pdf.PdfVersion.Pdf20), and [TryParse](cref:Meziantou.Framework.Pdf.PdfVersion.TryParse) reads a
version written as in a header, such as `1.7`.
