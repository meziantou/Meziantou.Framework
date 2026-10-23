# Meziantou.Framework.Pdf

A fully managed PDF library for .NET, written from ISO 32000-2 (PDF 2.0): read, write and modify PDF documents.

- **Object model**: `PdfDictionary`, `PdfArray`, `PdfStream`, `PdfName`, `PdfString`, numbers, references
  (`Meziantou.Framework.Pdf.Objects`), with the indirect objects of a document in `PdfDocument.Objects`
- **Reader**: cross-reference tables and streams, hybrid files, object streams, incremental updates, linearized files;
  the files whose cross-reference information is wrong are repaired by scanning them (`PdfDocument.IsRepaired`)
- **Filters**: `FlateDecode` and `LZWDecode` (with the PNG and TIFF predictors), `ASCIIHexDecode`, `ASCII85Decode`,
  `RunLengthDecode`, `CCITTFaxDecode` (Group 3 and Group 4), `JBIG2Decode` (generic, refinement, text and halftone
  regions, symbol and pattern dictionaries, arithmetic, MMR and Huffman coding); `DCTDecode` and `JPXDecode` through the
  codecs of `Meziantou.Framework.Imaging`
- **Writer**: full saves (the objects reachable from the catalog, renumbered, with object streams and a
  cross-reference stream, or a cross-reference table) and incremental updates that keep the bytes of the original file;
  the output depends only on the document and the options
- **Document API**: pages (`PdfDocument.Pages`), a content builder for paths, colors, text, images and marked content
  (`PdfPage.CreateContentBuilder`), shadings and functions (`PdfShading`, `PdfFunction`), patterns, form XObjects,
  transparency (`PdfExtGState`: alpha, blend modes, soft masks; transparency groups), the standard 14 fonts and embedded TrueType and OpenType fonts, images (`PdfImage`: JPEG files as they are, other images
  of `Meziantou.Framework.Imaging` with their alpha channel and ICC profile), the document information and the XMP
  metadata, the outline (bookmarks), links and annotations, text extraction (`PdfPage.ExtractText`)

## Create a document

Coordinates are in points (1/72 inch), from the lower-left corner of the page.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_CreateDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Information.Title = "Invoice";
var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

var page = document.Pages.Add(PdfSize.A4);
page.CreateContentBuilder()
    .SetFillRgb(0.2, 0.4, 0.8)
    .Rectangle(50, 750, 200, 40)
    .Fill()
    .SetFillGray(0)
    .DrawText("Hello, PDF!", font, 24, 60, 700);

using var stream = new MemoryStream();
document.Save(stream);
```
<!-- endref -->

## Embed a font

`PdfFont.Load` reads a TrueType or OpenType font. Each document embeds the subset of the glyphs it shows, as a composite
font whose codes are the glyph identifiers, with a `ToUnicode` CMap so that the text can be extracted and searched.
The text is shaped with the OpenType layout tables of the font (ligatures, kerning, marks, complex scripts such as
Arabic or Devanagari) and ordered by the Unicode Bidirectional Algorithm; a text whose glyphs cannot give it back
(several glyphs for a character, right-to-left text) is marked with its replacement text (`/ActualText`).

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_EmbeddedFont;format=md-fenced-code-block;dedent=true -->
```csharp
// A .ttf, .otf, .ttc, .woff or .woff2 file
var font = PdfFont.Load(fontData);

var document = new PdfDocument();
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .DrawText("Office \u2014 AVATAR", font, 24, 60, 700);

// The document embeds the glyphs it shows, with their text
var pdf = document.ToByteArray();
```
<!-- endref -->

## Gradients, patterns and transparency

`PdfShading` creates the smooth shadings of ISO 32000-2 (function-based, axial, radial, and the four kinds of meshes),
painted with `PaintShading` or used as a color by a `PdfShadingPattern`. `PdfFunction` creates the functions they use
(sampled, exponential, stitching and PostScript calculator functions; `PdfFunction.Gradient` for the stops of a
gradient). `PdfDocument.CreateForm` creates a form XObject, a content painted several times or a transparency group,
and `PdfDocument.CreateTilingPattern` a tiling pattern. `PdfExtGState` sets the alpha, the blend mode and the soft mask.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_AdvancedGraphics;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var builder = document.Pages.Add(PdfSize.A4).CreateContentBuilder();

// A gradient from red to blue, painted in a rectangle
var gradient = PdfShading.Axial(PdfDeviceColorSpace.Rgb, 50, 0, 250, 0,
    PdfFunction.Gradient((0, PdfColor.FromRgb(1, 0, 0)), (1, PdfColor.FromRgb(0, 0, 1))));
builder.SaveGraphicsState()
    .Rectangle(50, 600, 200, 100).Clip().EndPath()
    .PaintShading(gradient)
    .RestoreGraphicsState();

// A form painted twice, the second time at half opacity with the Multiply blend mode
var logo = document.CreateForm(new PdfRectangle(0, 0, 100, 100));
logo.CreateContentBuilder().SetFillRgb(1, 0.8, 0).Circle(50, 50, 50).Fill();
builder.DrawForm(logo)
    .SaveGraphicsState()
    .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5, BlendMode = PdfBlendMode.Multiply })
    .Translate(50, 0)
    .DrawForm(logo)
    .RestoreGraphicsState();

// A checkerboard: a tiling pattern used as the fill color
var checkerboard = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20);
checkerboard.CreateContentBuilder().SetFillGray(0).Rectangle(0, 0, 10, 10).Rectangle(10, 10, 10, 10).Fill();
builder.SetFillPattern(checkerboard).Rectangle(300, 600, 200, 100).Fill();
```
<!-- endref -->

## Read a document

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ReadDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var pageCount = document.Pages.Count;
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

## Modify a document incrementally

An incremental update appends the objects that changed to the original file: the bytes of the original are kept, so
that its digital signatures stay valid.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_IncrementalUpdate;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
document.Information.Author = "Reviewer";

using var output = new MemoryStream();
document.SaveIncremental(output);
```
<!-- endref -->

## Outline and links

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_OutlineAndLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var cover = document.Pages.Add(PdfSize.A4);
var content = document.Pages.Add(PdfSize.A4);

var chapter = document.Outlines.Add("Chapter 1", PdfDestination.Fit(content));
chapter.Children.Add("Section 1.1", PdfDestination.Xyz(content, left: 0, top: 500));

cover.Annotations.AddLink(new PdfRectangle(50, 50, 250, 80), PdfDestination.Fit(content));
cover.Annotations.AddLink(new PdfRectangle(50, 100, 250, 130), new Uri("https://www.meziantou.net"));
```
<!-- endref -->

## Object model

The high-level types read and write the objects of the document: anything the API does not cover can be done with
the objects.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_LowLevelObjects;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Pages.Add(PdfSize.Letter);

// The catalog is a dictionary of the object model: entries the API has no member for can be set directly
document.Catalog[PdfName.Create("PageLayout")] = PdfName.Create("TwoColumnLeft");
var viewerPreferences = new PdfDictionary
{
    [PdfName.Create("DisplayDocTitle")] = PdfBoolean.True,
};
document.Catalog[PdfName.Create("ViewerPreferences")] = document.Objects.Add(viewerPreferences);
```
<!-- endref -->

## Errors and limits

| Situation | Exception |
| --- | --- |
| The data is not a PDF document, or it is damaged beyond repair | `InvalidPdfContentException` |
| The document uses a feature the library does not support (encryption, CMYK JPEG images), or a font cannot be embedded (CFF2 or bitmap outlines, a license that does not allow it) | `UnsupportedPdfFeatureException` |
| The document exceeds a limit of `PdfReadOptions` (decoded length of a stream, nesting depth) | `PdfResourceLimitException` |

The three exceptions derive from `PdfException`. A reference to an object that does not exist is the null object, as the
specification says; a content stream that cannot be decoded throws when its data is read.

## Limitations

- Encrypted documents are not read yet, and the library does not encrypt.
- The standard 14 fonts show the text of `WinAnsiEncoding` (or the encodings of Symbol and ZapfDingbats); an embedded
  font shows any text it has the glyphs of. The fonts with CFF2 outlines, and the instances of variable fonts, are not
  embedded. PDF/A, Tagged PDF and PDF/UA are not supported yet.
- Pages are not rendered.
- The text extraction follows the order of the content and joins the glyphs into lines by their positions; it does
  not detect columns.
