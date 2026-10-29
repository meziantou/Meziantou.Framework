---
title: Creating documents
description: Create a document, add pages of any size, write their content with the content builder, and save the file.
---

A document is created empty, pages are added to it, and the content of each page is written by a content builder: paths
and colors, text, images, gradients and transparency. Then the document is saved.

## Create a document

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_CreateCard;format=md-fenced-code-block;dedent=true -->
```csharp
public static void CreateCard(string outputPath)
{
    var document = new PdfDocument();
    document.Information.Title = "Welcome";

    // A page of 300 by 180 points (1 point is 1/72 inch); the origin is its lower-left corner
    var page = document.Pages.Add(new PdfSize(300, 180));
    var bold = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
    var regular = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

    page.CreateContentBuilder()
        .SetFillRgb(0.13, 0.33, 0.62).Rectangle(0, 120, 300, 60).Fill() // a band at the top of the page
        .SetFillGray(1).DrawText("Welcome", bold, 28, 20, 140) // x and y are the start of the baseline
        .SetFillGray(0.2).DrawText("Your first PDF document", regular, 14, 20, 80)
        .SetStrokeRgb(0.13, 0.33, 0.62).SetLineWidth(2).MoveTo(20, 65).LineTo(280, 65).Stroke();

    document.Save(outputPath);
}
```
<!-- endref -->

![A page with a blue band at the top holding the white word Welcome, the text "Your first PDF document" below it, and a blue line](images/CreateCard.png)

`new PdfDocument()` is an empty PDF 1.7 document without pages. The version is raised when the document uses a feature
of a later version (AES-256 encryption needs PDF 2.0, for instance). A document supports one operation at a time: it is
not thread-safe. Fonts, images and graphics states do not belong to a document: create them once, and use them in as
many documents and threads as needed.

## Pages and sizes

`Pages.Add` adds a page of a size, `Pages.Insert` inserts one, and `Pages.RemoveAt` removes one:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_PageSizes;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var letter = document.Pages.Add(PdfSize.Letter); // 8.5 x 11 inches: 612 x 792 points
var landscape = document.Pages.Add(PdfSize.A4.ToLandscape()); // 297 x 210 mm
var label = document.Pages.Add(PdfSize.FromMillimeters(100, 50));
var cover = document.Pages.Insert(0, PdfSize.A4); // the new first page

// The crop box is the region viewers show and print: here, the label without a margin of 18 points (1/4 inch)
label.CropBox = new PdfRectangle(18, 18, label.MediaBox.Width - 18, label.MediaBox.Height - 18);

// A page drawn in portrait, shown turned 90 degrees clockwise
letter.Rotation = 90;

document.Pages.RemoveAt(document.Pages.IndexOf(cover));
```
<!-- endref -->

- `PdfSize` has the common sizes (`A3`, `A4`, `A5`, `Letter`, `Legal`), `FromMillimeters`, and `ToLandscape`, which
  puts the greater dimension in the width.
- The media box (`MediaBox`) is the size of the page. The crop box (`CropBox`) is the region viewers show and print; it
  is the media box unless it is set.
- `Rotation` turns the page clockwise when it is shown, by 0, 90, 180 or 270 degrees. The content keeps its coordinates.

## Coordinates

Positions and sizes are in points, 1/72 inch: an A4 page is 595 by 842 points, a Letter page 612 by 792. The origin is
the lower-left corner of the page, x grows to the right and y upward, so the top of a page is at its height. Text is
positioned by the start of its baseline.

The transformation of the graphics state changes these coordinates for what follows: `Translate`, `Rotate` (degrees,
counterclockwise), `Scale` and `Transform` (any `PdfMatrix`). See
[Shapes and colors](02-ShapesAndColors.md#move-turn-and-clip).

## The content builder

`PdfPage.CreateContentBuilder` returns a builder that appends a content stream to the page. Its methods write the
operators of PDF, and return the builder so that the calls can be chained. The fonts, images, forms and patterns it uses
are added to the resources of the page.

- **The graphics state** holds the current colors, line style, transformation, clip, font and transparency.
  `SaveGraphicsState` saves it, and `RestoreGraphicsState` goes back to it: wrap the changes that apply to one part of
  the page between them. `GraphicsStateDepth` is the number of saves not restored yet.
- **The builder checks the structure.** The text operators are only allowed between `BeginText` and `EndText`, a text is
  shown only once a font is set, and `RestoreGraphicsState` and `EndMarkedContent` need a matching call: a mistake
  throws `InvalidOperationException` when it is made, instead of writing a file that readers reject.
- **The content of a page can be extended.** When the page already has content, a new builder wraps it between a save
  and a restore of the graphics state first, so that the new content starts from the default state. See
  [Modifying documents](../05-ReadingAndModifying/04-ModifyingDocuments.md).
- **Operators without a method** can be written with `AppendRawContent`, which the builder does not check, and marked
  content with `BeginMarkedContent`, `MarkPoint` and `EndMarkedContent`.

## Save the document

`Save` writes the document to a file or a stream, `SaveAsync` to a stream, and `ToByteArray` to an array. The save
writes the objects that can be reached from the catalog, so the objects of a removed page are not written. The options
choose how the file is written:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_SaveOptions;format=md-fenced-code-block;dedent=true -->
```csharp
// The default: compressed streams, object streams and a cross-reference stream (PDF 1.5 and later)
var compact = document.ToByteArray();

// A file for older readers: a cross-reference table and a PDF 1.4 header
var compatible = document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false, Version = PdfVersion.Pdf14 });

// Content streams left as text, to read them in an editor
var readable = document.ToByteArray(new PdfSaveOptions { CompressStreams = false });
```
<!-- endref -->

| Option | Default | Effect |
| --- | --- | --- |
| `CompressStreams` | `true` | Compresses the streams that have no filter with `FlateDecode` |
| `UseObjectStreams` | `true` | Writes the objects in object streams with a cross-reference stream (PDF 1.5); `false` writes a cross-reference table |
| `Version` | `null` | The version of the header; `null` for the version of the document, raised to what the options need |

The output depends only on the document and the options: the library writes no date, no random value and not its own
version (`Information.Producer` is empty unless you set it), so a test can compare the files it writes.

## In this section

- [Shapes and colors](02-ShapesAndColors.md): paths, fills and outlines, colors, line styles, transformations and clips.
- [Text](03-Text.md): the standard fonts, positioning, aligning and wrapping text, and the text state.
- [Fonts](04-Fonts.md): embed TrueType and OpenType fonts, for any language.
- [Images](05-Images.md): draw JPEG, PNG and other images.
- [Gradients and patterns](06-GradientsAndPatterns.md): shadings, tiling patterns and shading patterns.
- [Reusable content](07-ReusableContent.md): forms, painted as many times as needed.
- [Transparency](08-Transparency.md): opacity, blend modes, soft masks and transparency groups.
