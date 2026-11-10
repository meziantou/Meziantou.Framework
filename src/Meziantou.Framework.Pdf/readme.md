# Meziantou.Framework.Pdf

A fully managed PDF library for .NET, written from ISO 32000-2 (PDF 2.0): read, write, modify, render and sign PDF documents.

- **Object model**: `PdfDictionary`, `PdfArray`, `PdfStream`, `PdfName`, `PdfString`, numbers, references
  (`Meziantou.Framework.Pdf.Objects`), with the indirect objects of a document in `PdfDocument.Objects`
- **Reader**: cross-reference tables and streams, hybrid files, object streams, incremental updates, linearized files;
  the files whose cross-reference information is wrong are repaired by scanning them (`PdfDocument.IsRepaired`); a
  seekable stream is read on demand (`PdfDocument.OpenSeekable`), without copying the file to memory, so that a file
  can be larger than 2 GB; the encrypted payload of PDF 2.0 unencrypted wrapper documents (`PdfDocument.EncryptedPayload`)
- **Filters**: `FlateDecode` and `LZWDecode` (with the PNG and TIFF predictors), `ASCIIHexDecode`, `ASCII85Decode`,
  `RunLengthDecode`, `CCITTFaxDecode` (Group 3 and Group 4), `JBIG2Decode` (generic, refinement, text and halftone
  regions, symbol and pattern dictionaries, arithmetic, MMR and Huffman coding); `DCTDecode` and `JPXDecode` through the
  codecs of `Meziantou.Framework.Imaging` (with the `/ColorTransform` of `DCTDecode`); the damaged data of a stream gives
  the bytes decoded before the damage
- **Writer**: full saves (the objects reachable from the catalog, renumbered, with object streams and a
  cross-reference stream, or a cross-reference table), linearized files for fast web view (`PdfSaveOptions.Linearize`)
  and incremental updates that keep the bytes of the original file (with object streams for files that have
  cross-reference streams); the file is streamed to the output through a bounded buffer; the output depends only on the
  document and the options, except the random keys and vectors of the encryption
- **Conformance**: PDF/A-1 (b, a), PDF/A-2 (b, u, a), PDF/A-3 (b, u, a) and PDF/A-4 (4, 4e, 4f) output
  (`PdfDocument.PdfAConformance`), the conversion of existing documents to PDF/A-2 and PDF/A-3 (b, u)
  (`PdfDocument.ConvertToPdfA`: actions, appearances, substitute fonts, LZW, metadata), PDF/UA-1 and PDF/UA-2 (`PdfDocument.PdfUAConformance`), and Tagged PDF: a structure tree with a role map, alternate descriptions,
  languages and artifacts (`PdfDocument.StructureTree`, `PdfContentBuilder.BeginStructureElement`); the documents are
  validated by veraPDF in the tests
- **Encryption**: the standard security handler, revisions 2 to 6 (RC4 with 40-bit and 128-bit keys, AES-128,
  AES-256), with crypt filters, for reading (`PdfReadOptions.Password`) and writing (`PdfDocument.Encryption`), and the
  public-key security handler for the RSA certificates of recipients (`PdfEncryptionRecipient`,
  `PdfReadOptions.Certificate`)
- **Document API**: pages (`PdfDocument.Pages`), a content builder for paths, colors, text, images and marked content
  (`PdfPage.CreateContentBuilder`), shadings and functions (`PdfShading`, `PdfFunction`), patterns, form XObjects,
  transparency (`PdfExtGState`: alpha, blend modes, soft masks; transparency groups), the standard 14 fonts and embedded TrueType and OpenType fonts, images (`PdfImage`: JPEG files as they are, other images
  of `Meziantou.Framework.Imaging` with their alpha channel and ICC profile, compressed without loss with the PNG predictors or as JPEG, black and white images compressed with CCITT Group 4 or JBIG2), the document information and the XMP
  metadata, the outline (bookmarks), links and annotations (notes, text markups, free texts, stamps, flags), page
  labels, embedded files (`PdfDocument.EmbeddedFiles`, file attachment annotations), text boxes
  (`PdfContentBuilder.DrawTextBox`: Unicode line breaking, soft hyphens and pluggable hyphenation), tables
  (`PdfContentBuilder.DrawTable`: fixed, relative and automatic columns, borders, header rows repeated on each page), pages moved, duplicated, removed and imported from other documents (merging), a
  page as a form XObject, the page boxes and display coordinates, text extraction (`PdfPage.ExtractText`, with the
  right-to-left lines put in the order of the text by the Unicode Bidirectional Algorithm), positioned
  text (`PdfPage.ExtractTextLayout`: lines, words and glyphs with their bounding boxes), the reading order of the
  layout (`PdfTextReadingOrder.Layout`: columns and blocks found from the blank spaces of the page), image extraction
  (`PdfPage.GetImages`), content drawn over or under the content of a page (`PdfContentPlacement`), text watermarks
  (`PdfDocument.AddWatermark`), interactive forms created, read, filled and flattened (`PdfDocument.Form`), redaction
  (`PdfPage.Redact`: the text, paths, image pixels and annotations of areas removed from the content, not hidden)
- **Rendering** (`Meziantou.Framework.Pdf.Rendering`): the pages drawn to images on the canvas of
  `Meziantou.Framework.Imaging`: paths, clipping, optional content and annotation appearances; embedded TrueType, CFF,
  Type 1 and Type 3 fonts and substituted fonts; every color space and image filter; functions, the seven shading types
  and tiling patterns; transparency groups, soft masks and blend modes
- **Signatures** (`Meziantou.Framework.Pdf.Signing`): PAdES baseline B-B, B-T, B-LT and B-LTA signatures (CAdES
  detached signatures, RFC 3161 time stamps, the document security store, document time stamps), certification and
  field locks, visible signatures, appended as incremental updates, and the verification of the signatures of a
  document and of the changes made after them

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
A text already shaped and laid out elsewhere is shown with `ShowGlyphs`: the glyph identifiers at their positions, and
the text they show so that it can still be extracted.

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

`PdfDocument.OpenSeekable` reads the objects of a seekable stream when they are first used: the stream belongs to the
caller and stays open while the document is used.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_OpenSeekable;format=md-fenced-code-block;dedent=true -->
```csharp
// The objects are read from the stream when they are first used: it stays open while the document is used
using var stream = File.OpenRead(path);
var document = PdfDocument.OpenSeekable(stream);
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

## Tagged PDF and PDF/UA

The structure tree gives the logical structure of the content to assistive technologies: each element has a standard
type (`Document`, `H1`, `P`, `Figure`, `Table`...), and its content is the marked content the content builder tags
with it. The content that is not part of the structure, such as page numbers, is an artifact. With PDF/UA, the save
checks the title, the language and the structure, and writes the identification of the level.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_TaggedDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var font = PdfFont.Load(fontData);
var document = new PdfDocument
{
    PdfAConformance = PdfAConformance.PdfA2A,
    PdfUAConformance = PdfUAConformance.PdfUA1,
    Language = "en-US",
};
document.Information.Title = "Annual report";

// The logical structure: a heading, a paragraph and a figure in a Document element
var root = document.StructureTree.AddElement("Document");
var heading = root.AddElement("H1");
var paragraph = root.AddElement("P");
var figure = root.AddElement("Figure");
figure.AlternateDescription = "A blue square";

// The content of each element, and the page number as an artifact
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .BeginStructureElement(heading).DrawText("AVATAR", font, 24, 60, 760).EndMarkedContent()
    .BeginStructureElement(paragraph).DrawText("Office", font, 12, 60, 720).EndMarkedContent()
    .BeginStructureElement(figure).SetFillRgb(0, 0, 1).Rectangle(60, 600, 100, 100).Fill().EndMarkedContent()
    .BeginArtifact(PdfArtifactType.Pagination).DrawText("1", font, 10, 300, 40).EndMarkedContent();

var pdf = document.ToByteArray();
```
<!-- endref -->

### PDF/UA-2

PDF/UA-2 (ISO 14289-2) is based on PDF 2.0, so it is combined with PDF/A-4 only. The save checks that the structure
tree has a single `Document` element and no `H` or `Note` element, puts the standard types of PDF 2.0 in the PDF 2.0
namespace, and refuses the destinations of the document that go to a page only: give them the structure element they
show (`PdfDestination.WithStructureElement`), which is written as a `GoTo` action with a structure destination. The
embedded files need a description.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_PdfUA2;format=md-fenced-code-block;dedent=true -->
```csharp
var font = PdfFont.Load(fontData);
var document = new PdfDocument
{
    PdfAConformance = PdfAConformance.PdfA4,
    PdfUAConformance = PdfUAConformance.PdfUA2,
    Language = "en-US",
};
document.Information.Title = "Annual report";

// A single Document element, and headings H1 to H6
var root = document.StructureTree.AddElement("Document");
var heading = root.AddElement("H1");
var page = document.Pages.Add(PdfSize.A4);
page.CreateContentBuilder().BeginStructureElement(heading).DrawText("Results", font, 24, 60, 760).EndMarkedContent();

// The destinations in the document go to structure elements
document.Outlines.Add("Results", PdfDestination.Fit(page).WithStructureElement(heading));

var pdf = document.ToByteArray();
```
<!-- endref -->

## PDF/A

PDF/A is the PDF of long-term archiving. The save checks what the library can check (no encryption, embedded fonts,
a structure tree for the level A, the version), adds the sRGB output intent, and writes the XMP metadata from the
document information with the identification of the level. The content must follow the other rules of the level:
the conformance tests validate the documents the library writes with veraPDF.

| Level | Based on | Specific rules the save checks or writes |
| --- | --- | --- |
| PDF/A-1b, PDF/A-1a | PDF 1.4 | A 1.4 header and a cross-reference table, without object streams; an sRGB output intent of version 2; the `CIDSet` of the font subsets. No transparency (alpha, blend modes, soft masks, images with an alpha channel, transparency groups), no LZW or JPEG 2000 filter, no layers, no embedded files, no ICC profile of version 4 |
| PDF/A-2b, -2u, -2a | PDF 1.7 | The embedded files are PDF/A-1 or PDF/A-2 documents |
| PDF/A-3b, -3u, -3a | PDF 1.7 | The embedded files, of any format, have a MIME type and a relationship |
| PDF/A-4, PDF/A-4e | PDF 2.0 | No document information dictionary; the embedded files are PDF/A-1, -2 or -4 documents |
| PDF/A-4f | PDF 2.0 | PDF/A-4 with at least one embedded file, of any format |

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_PdfA;format=md-fenced-code-block;dedent=true -->
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

## Encrypt a document

`PdfDocument.Encryption` encrypts the strings and streams of the document when it is saved, with AES-256 by default.
A document read from an encrypted file keeps its encryption: set the property to `null` to save it in clear, or to
another `PdfEncryption` to change its passwords. `PdfDocument.Permissions` gives the operations the password the
document was opened with allows; the library does not enforce them. `new PdfEncryption(new PdfEncryptionRecipient(certificate))`
encrypts the document for certificates instead of passwords, and `PdfReadOptions.Certificate` opens it with the private
key of a recipient.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_Encryption;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .DrawText("Confidential", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);

// AES-256 by default: the user password opens the document with the permissions, the owner password with all
document.Encryption = new PdfEncryption("user password", "owner password")
{
    Permissions = PdfPermissions.Print | PdfPermissions.PrintHighQuality,
};
var file = document.ToByteArray();

var read = PdfDocument.Open(file, new PdfReadOptions { Password = "user password" });
var text = read.Pages[0].ExtractText();
var canCopy = read.Permissions.HasFlag(PdfPermissions.CopyContent);
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

## Redact a page

`PdfPage.Redact` removes what a page shows in areas: the glyphs, paths and image pixels are removed from the content,
so that the text cannot be extracted or found in the saved file, and the areas are painted black.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_Redact;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
var page = document.Pages.Add(PdfSize.A4);
page.CreateContentBuilder()
    .DrawText("Name: John Smith", font, 12, 72, 720);

// Removes the content under the rectangle and paints it black; the document can then only be saved as a whole
var result = page.Redact([new PdfRectangle(108, 715, 200, 732)]);
var redacted = PdfDocument.Open(document.ToByteArray());

Assert.Equal("Name:", redacted.Pages[0].ExtractText().Trim());
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

## Merge documents

`PdfDocument.Pages.Add` with a page of another document copies the page and the objects it uses; the objects shared by
the pages of a document, such as the fonts, are copied once.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_MergeDocuments;format=md-fenced-code-block;dedent=true -->
```csharp
public static byte[] Merge(IEnumerable<byte[]> files)
{
    var result = new PdfDocument();
    foreach (var file in files)
    {
        // The source documents must stay open until the result is saved
        var source = PdfDocument.Open(file);
        foreach (var page in source.Pages)
        {
            result.Pages.Add(page);
        }
    }

    return result.ToByteArray();
}
```
<!-- endref -->

## Create a form

`PdfInteractiveForm.AddTextField`, `AddCheckBox`, `AddRadioButtons`, `AddComboBox`, `AddListBox` and
`AddSignatureField` create the fields, their widgets and their appearances. The fonts of the text fields can be
embedded: they are added to the default resources of the form (`PdfInteractiveForm.AddFont`), whole for the fields
that can be edited. Check boxes and radio buttons draw their symbols as paths.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfInteractiveFormTests.cs;region=Pdf_CreateForm;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var page = document.Pages.Add(PdfSize.A4);
var form = document.Form;
form.AddTextField(page, "name", new PdfRectangle(50, 780, 300, 800), new PdfTextFieldOptions { Value = "Jane Doe", IsRequired = true });
form.AddTextField(page, "address.city", new PdfRectangle(50, 750, 300, 770));
form.AddCheckBox(page, "agree", new PdfRectangle(50, 720, 65, 735), new PdfCheckBoxOptions { IsChecked = true });
form.AddRadioButtons("size",
[
    new PdfRadioButton(page, new PdfRectangle(50, 690, 65, 705), "S"),
    new PdfRadioButton(page, new PdfRectangle(80, 690, 95, 705), "L"),
], new PdfRadioButtonOptions { Value = "S" });
form.AddComboBox(page, "country", new PdfRectangle(50, 650, 200, 670), [new PdfChoiceOption("fr", "France"), new PdfChoiceOption("de", "Germany")], new PdfChoiceFieldOptions { SelectedValues = ["fr"] });
form.AddSignatureField(page, "signature", new PdfRectangle(50, 550, 250, 620));

var data = document.ToByteArray();
```
<!-- endref -->

## Fill a form

`PdfDocument.Form` reads the fields of an interactive form. Setting a value writes the value and the appearance of the
widgets; `PdfInteractiveForm.Flatten` draws the fields on their pages and removes them.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfInteractiveFormTests.cs;region=Pdf_FillForm;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(data);
var form = document.Form;
form.FindField("name")!.Value = "Jane Doe";
((PdfCheckBoxField)form.FindField("agree")!).IsChecked = true;
form.FindField("size")!.Value = "L";
form.FindField("country")!.Value = "de";
((PdfChoiceField)form.FindField("colors")!).SetSelectedValues(["red", "blue"]);

// The values and the appearances of the fields are appended: the signatures stay valid
using var output = new MemoryStream();
document.SaveIncremental(output);
```
<!-- endref -->

## Embedded files

`PdfDocument.EmbeddedFiles` holds the files of the document (the `/EmbeddedFiles` name tree), which the save also
associates with the document (`/AF`), as PDF/A-3 and PDF/A-4f require. A file attachment annotation holds a file of a
page and shows it as an icon. The dates of a file are written only when they are given.

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_EmbeddedFiles;format=md-fenced-code-block;dedent=true -->
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

## Render a page

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=PdfRendering_RenderPage;format=md-fenced-code-block;dedent=true -->
```csharp
var page = document.Pages[0];

// 144 dpi: two pixels per point
using var image = PdfRenderer.RenderPage(page, new PdfRenderOptions { Dpi = 144 });
using var png = new MemoryStream();
image.Save(png, new Imaging.Formats.PngEncoder());
```
<!-- endref -->

The image is the crop box of the page, rotated as the page is shown, on a white background (`PdfRenderOptions.Background`,
`null` for a transparent one). `PdfRenderOptions.RenderAnnotations` draws the appearances of the annotations, and
generates one for the annotations and the form fields that have none. `PdfRenderOptions.Region` renders part of the
page, `Width` and `Height` fit it to a size in pixels instead of `Dpi`, and `PathAntialiasing` and `TextAntialiasing`
turn the antialiasing off. `PdfRenderOptions.Intent` renders for viewing, printing or exporting (the annotations with
the Print flag, the layers whose usage is for printing), `OptionalContentConfiguration` selects a configuration of the
layers and `OptionalContentGroupStates` turns layers on or off by their name. An overload of `RenderPage` takes a
`CancellationToken`, and another draws on an image of the application, of any pixel format (`GetImageSize` gives its
size).

- **Graphics**: paths, line styles and dashes, clipping (paths and text), the page boxes and rotation, optional content
  (layers, their configurations, intents and usage), the appearance streams of the annotations (as transparency groups,
  with their constant opacity and the NoZoom and NoRotate flags)
- **Text**: embedded TrueType, CFF (`Type1C`, `CIDFontType0C`, OpenType) and Type 1 fonts, Type 3 fonts, the fonts that
  are not embedded replaced by a font of the system or of the application, the text rendering modes
- **Colors**: the device color spaces, `ICCBased` (through the ICC profiles of Imaging), `CalRGB`, `CalGray`, `Lab`,
  `Indexed`, `Separation` and `DeviceN`; `DeviceCMYK` is converted with a CMYK profile
- **Images**: every filter (`DCTDecode` and `JPXDecode` through the codecs of Imaging, CMYK JPEG images included,
  `CCITTFaxDecode`, `JBIG2Decode`), 1 to 16 bits per component, `/Decode`, stencil masks, explicit masks, color key
  masks and soft masks, inline images
- **Shadings and patterns**: functions (sampled, exponential, stitching, PostScript calculator), the seven shading
  types, shading patterns and colored and uncolored tiling patterns
- **Transparency**: constant alpha, the blend modes, transparency groups (isolated or not, knockout or not, the soft
  masks and the page group included), soft masks (alpha and luminosity, with their backdrop and transfer function), the
  transfer functions of the graphics state. In a knockout group, each painting operator, image, shading, text-showing
  operator and group is an element; the fill and the stroke of a path are one element, whose stroke composites over its
  fill, and the alpha of a paint of an element counts as its shape; the alpha-is-shape parameter (`AIS`) makes the
  constant alpha and the soft mask shape. The glyphs of a text-showing operator knock out one another, unless the text
  knockout parameter (`TK`) is false. A transfer function applies to the result of a group. Overprint is not
  simulated
- **Halftones**: ignored by default, as viewers do. With `PdfRenderOptions.UseHalftones`, the halftones of the graphics
  states (`HT`: types 1, 5, 6, 10 and 16, the predefined spot functions; `HTO`) turn the red, green and blue components
  of the fully opaque objects on or off, in the pixels of the image; the default halftone leaves the colors as they are

### Fonts that are not embedded

A font that the document does not embed is replaced, in this order, by:

1. the font `PdfRenderOptions.FontResolver` returns for it (`PdfFontRequest`: its name, weight, style and kind);
2. for a standard font (Helvetica, Times, Courier and their aliases, such as Arial), the bundled Liberation font of its
   kind and style, whose glyphs have the same widths;
3. a font of the system that matches its name, weight and style, then one of its kind (serif, sans serif, fixed
   pitch), unless `PdfRenderOptions.UseSystemFonts` is `false`;
4. the bundled Liberation font of its kind.

The bundled fonts (SIL Open Font License 1.1, see `THIRD-PARTY-NOTICES.md`) have the characters of the Latin encodings
of PDF. With `UseSystemFonts` set to `false`, the rendering does not depend on the fonts of the machine. Symbol and
ZapfDingbats are only replaced by the resolver or the fonts of the system. The glyphs of a replacement font are scaled
horizontally to the widths of the document.

### Limits of the rendering

The rendering of a page is bounded: `PdfRenderOptions.MaxOperations` limits the operators, glyphs and pixels it
processes, and the images, shadings and nested forms have limits of their own. A page beyond a limit throws a
`PdfResourceLimitException`. The errors of the content (an unknown operator, a damaged image or font) skip what they
affect, as viewers do.

## Sign a document

Digital signatures (ISO 32000-2, 12.8): PAdES baseline signatures (ETSI EN 319 142-1) and the verification of the
signatures of a document.

- **Signing**: B-B, a CAdES detached signature (`ETSI.CAdES.detached`) with the ESS signing certificate, and B-T, its
  RFC 3161 time stamp; RSA (PKCS #1 v1.5 or PSS) and ECDSA keys, SHA-256, SHA-384 and SHA-512; certification
  signatures (DocMDP) and field locks (FieldMDP); visible signatures (`PdfSignatureAppearance`); the validation data of
  B-LT (`PdfSigner.AddValidationData`; `PdfSigner.AddValidationDataAsync` completes the chains, downloads the
  missing OCSP responses, revocation lists and issuers when asked, and writes VRI dictionaries) and the document time
  stamps of B-LTA (`PdfSigner.TimestampAsync`); every
  operation is appended as an incremental update, so that the signatures of the document stay valid; encrypted
  documents are signed with their keys
- **Verification**: the integrity of the bytes each signature covers, the CMS signature (one signer), the ESS signing
  certificate, every time stamp, whether the signature covers the whole file, and the changes made after it compared
  with its certification and its field lock (`adbe.pkcs7.detached`, `ETSI.CAdES.detached`, `adbe.pkcs7.sha1`, document
  time stamps `ETSI.RFC3161`); the information of a signature is read from the revision it signs

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_Sign;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .DrawText("Contract", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);

// The certificate has its private key: X509CertificateLoader.LoadPkcs12FromFile loads one from a .pfx file
var signed = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { Reason = "I approve this contract" });

var signature = PdfSignatureVerifier.Verify(signed).Single();
var isValid = signature.IsValid && signature.CoversWholeDocument;
```
<!-- endref -->

The signature field is invisible, on the first page, unless `PdfSignatureOptions.Appearance` places it: its `Draw`
callback draws the appearance, or, without it, the library draws the name of the signer, the date, the reason, the
location and an optional `Image`. The field name
is unique: an existing signature field that is not signed yet is signed, another existing name throws.
`PdfSignatureOptions` also sets the information of the signature dictionary (reason, location, contact, name, time),
the other certificates of the chain to embed, the certification level and the field lock. A document certified with
`PdfCertificationLevel.NoChanges` cannot be signed again.

### Time stamp a signature (PAdES B-T)

<!-- ref:../../tests/Meziantou.Framework.Pdf.Tests/PdfSignerTests.cs;region=PdfSigning_Timestamp;format=md-fenced-code-block;dedent=true -->
```csharp
// PAdES B-T: the signature is stamped by an RFC 3161 time stamp authority
var options = new PdfSignatureOptions(certificate)
{
    TimestampAuthority = new Uri("http://timestamp.example.com/"),
    HttpClient = httpClient,
};
var signed = await PdfSigner.SignAsync(document, options, XunitCancellationToken);
```
<!-- endref -->

### Trust

`PdfSignatureVerifier` checks that the document was not modified and that the signature matches the certificate it
embeds; it does not decide whether the signer is trusted. Build the chain of `PdfSignatureInfo.Certificate` with
`X509Chain`, with the trust anchors and the revocation policy of the application, and check `CoversWholeDocument`:
a signature that does not cover the whole file was followed by incremental updates, which `Modifications` lists.

## Errors and limits

| Situation | Exception |
| --- | --- |
| The data is not a PDF document, or it is damaged beyond repair | `InvalidPdfContentException` |
| The document uses a feature the library does not support (a security handler other than the standard one, a stream filter it does not know, CMYK JPEG data decoded with `PdfStream.GetDecodedData`), or a font cannot be embedded (CFF2 or bitmap outlines, a license that does not allow it) | `UnsupportedPdfFeatureException` |
| The document exceeds a limit of `PdfReadOptions` (decoded length of a stream, nesting depth) | `PdfResourceLimitException` |
| The document is encrypted, and `PdfReadOptions.Password` is neither its user nor its owner password | `PdfPasswordException` |

The four exceptions derive from `PdfException`. A reference to an object that does not exist is the null object, as the
specification says; a content stream that cannot be decoded throws when its data is read.

## Limitations

- The public-key security handler needs RSA certificates (no key agreement); the writer encrypts the embedded files with
  the method of the other streams.
- The standard 14 fonts show the text of `WinAnsiEncoding` (or the encodings of Symbol and ZapfDingbats); an embedded
  font shows any text it has the glyphs of. The fonts with CFF2 outlines, and the instances of variable fonts, are not
  embedded. The save checks what the conformance levels require of the document as a whole; the
  content (the order of the structure, the colors of the annotations of PDF/A-1, the values of the numbers) follows
  the other rules of the level.
- The text extraction follows the order of the content, or finds the columns from the blank spaces of the page
  (`PdfTextReadingOrder.Layout`, read from left to right). The predefined CMaps other than `Identity-H` and `Identity-V` split the codes and give their
  text, but not their CIDs: their glyphs get the default width of the font.
- The fields of a saved document whose default appearance names an embedded font are filled with that font only when
  it is given again (`PdfInteractiveForm.AddFont`); otherwise their values are drawn with Helvetica. The text boxes
  break the lines at the spaces, without the Unicode line breaking rules nor hyphenation.
- The renderer composites in sRGB: overprint is not simulated, and the halftones are applied to the red, green and blue
  components only, when `PdfRenderOptions.UseHalftones` asks for them.
