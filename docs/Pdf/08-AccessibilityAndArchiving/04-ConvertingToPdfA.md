---
title: Converting to PDF/A
description: Turn an existing PDF document into a PDF/A-2 or PDF/A-3 document, see what the conversion changed, and what it cannot fix.
---

[PdfDocument.ConvertToPdfA](cref:Meziantou.Framework.Pdf.PdfDocument.ConvertToPdfA) changes an existing document, such as a file a user sends, so that it can be saved as
PDF/A-2b, PDF/A-2u, PDF/A-3b or PDF/A-3u. It removes what PDF/A does not allow and completes what it needs, without
moving anything on the pages, then checks the document as the save does:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ConvertToPdfA;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var result = document.ConvertToPdfA(PdfAConformance.PdfA2B);
if (result.Succeeded)
{
    // The save writes a PDF/A-2b document
    var pdf = document.ToByteArray();
}
else
{
    // What the conversion could not fix, such as a font that is not embedded and has no substitute
    var reasons = string.Join(Environment.NewLine, result.Violations);
}
```
<!-- endref -->

When [Succeeded](cref:Meziantou.Framework.Pdf.PdfAConversionResult.Succeeded) is `true`, [PdfAConformance](cref:Meziantou.Framework.Pdf.PdfDocument.PdfAConformance) is set to the level, and the save writes a PDF/A document.
[Changes](cref:Meziantou.Framework.Pdf.PdfAConversionResult.Changes) lists what the conversion did, and [Violations](cref:Meziantou.Framework.Pdf.PdfAConversionResult.Violations) what it could not fix. The fixes change the
document even when some requirements cannot be met: open the file again to start over.

## What the conversion fixes

- **Encryption**: removed. Open the document with its password first.
- **Version**: a document of PDF 2.0 is set to PDF 1.7, the version of PDF/A-2 and PDF/A-3.
- **Actions**: the JavaScript of the document, the actions PDF/A does not allow (Launch, JavaScript, Sound, Movie,
  ResetForm, ImportData, Hide, ...), the actions of the widgets and every additional action (`/AA`) are removed. The
  links to pages and to web sites are kept.
- **Forms**: the XFA form is removed; when the form asks the viewer to generate the appearances of the fields, they are
  generated and the flag is removed.
- **Annotations**: the 3D, Sound, Screen, Movie and RichMedia annotations are removed, the appearances other than the
  normal one are removed, and the missing appearances are generated for the shapes, the lines, the ink, the text
  markups and the fields.
- **Fonts**: a standard font that is not embedded (Helvetica, Times, Courier and their styles, and the names readers
  accept for them, such as Arial) gets an embedded substitute: Liberation Sans, Serif or Mono, which have the widths of
  the standard fonts, so the text does not move. It is embedded as a subset of the glyphs of its encoding, with a
  `ToUnicode` map.
- **Streams**: the streams encoded with LZWDecode, which PDF/A-2 does not allow, are decoded; the save compresses them
  with FlateDecode.
- **Images and graphics states**: the alternate images, the OPI entries, the PostScript XObjects and the transfer
  functions are removed, and the interpolation of the images is turned off.
- **Embedded files**: PDF/A-2 embeds PDF/A documents only, so the other files are removed (use PDF/A-3 to keep them).
  PDF/A-3 gets their missing names, MIME type (`application/octet-stream`) and relationship (`Unspecified`), and
  associates them with the document.
- **Colors**: a document without output intent gets the sRGB one, or the profile of
  [OutputIntentProfile](cref:Meziantou.Framework.Pdf.PdfAConversionOptions.OutputIntentProfile). When the output intent is not RGB, an sRGB `DefaultRGB` color space gives DeviceRGB its
  meaning.
- **Metadata**: the XMP metadata is removed, and the save writes it again from the document information, whose characters
  that XML does not allow are removed. Set [KeepXmpMetadata](cref:Meziantou.Framework.Pdf.PdfAConversionOptions.KeepXmpMetadata) to keep it when it has no property of another schema.
- **Optional content**: each configuration gets a name, and none keeps automatic states (`AS`).

## What it cannot fix

The conversion reports these, and does not set the level:

- A font that is not embedded and is not a Latin standard font (such as Garamond, Symbol, ZapfDingbats or a CID font):
  the library has no substitute for it. Embed it with the tool that made the document.
- DeviceCMYK without a CMYK output intent: pass the profile of the printing condition in
  [OutputIntentProfile](cref:Meziantou.Framework.Pdf.PdfAConversionOptions.OutputIntentProfile).
- An annotation without appearance that the library cannot draw, such as a note or a stamp: remove it.
- For the level u, text whose font gives no Unicode value: convert to the level b.
- A stream whose data is in an external file, or encoded with LZW data that cannot be decoded.

The conversion is not a validator: it fixes and checks what the save checks (see [PDF/A](03-PdfA.md#what-the-save-checks)).
Validate the converted documents with [veraPDF](https://verapdf.org).
