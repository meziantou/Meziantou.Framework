---
title: Embedded files
description: Attach files to a document or to a page, describe them, and read the attached files of a document.
---

A document can carry files: the data of a table, the source of a drawing, the structured version of an invoice. Files of
the document are listed by readers in their attachment panel; a file of a page is shown as an icon on the page.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_EmbeddedFiles;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var page = document.Pages.Add(PdfSize.A4);

// A file of the document: the name tree of the catalog, associated with the document
var csv = "Product,Price\nBook,12\n"u8.ToArray();
document.EmbeddedFiles.Add("prices.csv", csv, new PdfEmbeddedFileOptions
{
    Description = "The prices of the table",
    MimeType = "text/csv",
    Relationship = PdfAssociatedFileRelationship.Data,
    ModificationDate = new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero),
});

// A file of a page: a file attachment annotation, drawn as an icon
page.Annotations.AddFileAttachment(PdfRectangle.FromSize(60, 700, 16, 20), "notes.txt", "Notes"u8, icon: PdfFileAttachmentIcon.Paperclip);

var read = PdfDocument.Open(document.ToByteArray());
var prices = read.EmbeddedFiles[0];
var notes = ((PdfFileAttachmentAnnotation)read.Pages[0].Annotations[0]).File!;
```
<!-- endref -->

## Attach a file

- `EmbeddedFiles.Add` attaches a file to the document, with its name and its content. The save also associates the
  file with the document, as PDF/A-3 and PDF/A-4f require.
- `Annotations.AddFileAttachment` attaches a file to a page, as an icon in a rectangle: [PushPin](cref:Meziantou.Framework.Pdf.PdfFileAttachmentIcon.PushPin) (the default),
  [Paperclip](cref:Meziantou.Framework.Pdf.PdfFileAttachmentIcon.Paperclip), [Graph](cref:Meziantou.Framework.Pdf.PdfFileAttachmentIcon.Graph) or [Tag](cref:Meziantou.Framework.Pdf.PdfFileAttachmentIcon.Tag). The overload that takes a [PdfEmbeddedFile](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile) attaches a file already embedded.
- [PdfEmbeddedFileOptions](cref:Meziantou.Framework.Pdf.PdfEmbeddedFileOptions) describes the file: [Description](cref:Meziantou.Framework.Pdf.PdfEmbeddedFileOptions.Description), [MimeType](cref:Meziantou.Framework.Pdf.PdfEmbeddedFileOptions.MimeType), [CreationDate](cref:Meziantou.Framework.Pdf.PdfEmbeddedFileOptions.CreationDate), [ModificationDate](cref:Meziantou.Framework.Pdf.PdfEmbeddedFileOptions.ModificationDate), and
  [Relationship](cref:Meziantou.Framework.Pdf.PdfEmbeddedFileOptions.Relationship), its relationship to the document ([Source](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Source), [Data](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Data), [Alternative](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Alternative), [Supplement](cref:Meziantou.Framework.Pdf.PdfAssociatedFileRelationship.Supplement)...), which PDF/A-3
  requires. The dates are written only when they are given.

## Read the files

[EmbeddedFiles](cref:Meziantou.Framework.Pdf.PdfDocument.EmbeddedFiles) lists the files of the document, and [PdfFileAttachmentAnnotation.File](cref:Meziantou.Framework.Pdf.PdfFileAttachmentAnnotation.File) the file of an annotation. A
[PdfEmbeddedFile](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile) gives [FileName](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.FileName), [Description](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.Description), [MimeType](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.MimeType), [Size](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.Size), the dates and [Relationship](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.Relationship), and [GetData](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.GetData) reads
its content. `EmbeddedFiles.Remove` removes a file from the document.

The content of a file comes from the document: treat it as untrusted input, as the document itself.

## Files and PDF/A

PDF/A-1 does not allow embedded files, PDF/A-2 and PDF/A-4 allow only PDF/A documents, PDF/A-3 and PDF/A-4f allow files
of any format. See [PDF/A](../08-AccessibilityAndArchiving/03-PdfA.md) for an invoice that carries its XML data.
