---
title: PDF documents
description: Validate PDF documents page by page as images, with a JSON file of their metadata.
---

`Meziantou.Framework.SnapshotTesting.Pdf` stores a PDF document as one PNG image per page and a JSON file of its
metadata, instead of comparing its bytes. A change shows which page differs, every page can be reviewed as an image, and
a document generated again with a new timestamp or a new internal layout still matches when it looks the same. It is
based on `Meziantou.Framework.Pdf`, a fully managed PDF library with no native dependency.

```bash
dotnet add package Meziantou.Framework.SnapshotTesting.Pdf
```

## Set up

Call [AddPdf()](cref:Meziantou.Framework.SnapshotTesting.Pdf.SnapshotSettingsPdfExtensions.AddPdf) on [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) in a module initializer ([AddPdf](cref:Meziantou.Framework.SnapshotTesting.Pdf.SnapshotSettingsPdfExtensions.AddPdf) is in the
[Meziantou.Framework.SnapshotTesting.Pdf](cref:Meziantou.Framework.SnapshotTesting.Pdf) namespace):

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/SnapshotConfiguration.cs;region=SnapshotTestingPdf_Setup;format=md-fenced-code-block;dedent=true -->
```csharp
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddPdf();
    }
}
```
<!-- endref -->

Then validate documents as the bytes of a file (`byte[]` or `Stream`) with [SnapshotType.Pdf](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Pdf), or as a [PdfDocument](cref:Meziantou.Framework.Pdf.PdfDocument):

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/PdfSnapshotTests.cs;region=SnapshotTestingPdf_ValidateBytes;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
document.Information.Title = "Invoice";
var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
document.Pages.Add(PdfSize.A5).CreateContentBuilder()
    .SetFillRgb(0.2, 0.4, 0.8)
    .Rectangle(40, 520, 200, 40)
    .Fill()
    .SetFillGray(0)
    .DrawText("Hello, PDF!", font, 24, 50, 480);
var bytes = document.ToByteArray();

// Writes PdfSnapshotTests_ReadmeExample_ValidateBytes_0.verified.json (the metadata)
// and PdfSnapshotTests_ReadmeExample_ValidateBytes_1.verified.png (the first page)
Snapshot.Validate(bytes, SnapshotType.Pdf);
```
<!-- endref -->

[AddPdf()](cref:Meziantou.Framework.SnapshotTesting.Pdf.SnapshotSettingsPdfExtensions.AddPdf) registers:

- **A serializer** for [PdfDocument](cref:Meziantou.Framework.Pdf.PdfDocument) values, and for `byte[]` and `Stream` values validated as [SnapshotType.Pdf](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Pdf). Data
  that is not a PDF document is stored as it is, in a `.pdf` file.
- **The image comparer** of [`Meziantou.Framework.SnapshotTesting.Imaging`](02-Images.md) for [SnapshotType.Png](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Png), so the
  pages are compared by their pixels. A comparer already registered for PNG snapshots, for instance by [AddImaging()](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotSettingsImagingExtensions.AddImaging),
  is kept.

To register the serializer without the comparer, call `Serializers.AddPdfSerializer()`, with optional
[PdfSnapshotSettings](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings).

## Snapshot files

A document produces one file for its metadata and one file per page:

- `<Name>_0.verified.json`: the metadata of the document;
- `<Name>_1.verified.png` to `<Name>_<n>.verified.png`: the pages, so the file of page `n` has the index `n`.

A document without pages produces only `<Name>.verified.json`. The metadata of the document above is:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/__snapshots__/PdfSnapshotTests_ReadmeExample_ValidateBytes_0.verified.json;format=md-fenced-code-block;language=json -->
```json
{
  "version": "1.7",
  "pageCount": 1,
  "information": {
    "title": "Invoice",
    "isEncrypted": false
  },
  "pages": [
    {
      "number": 1,
      "width": 419.528,
      "height": 595.276,
      "rotation": 0
    }
  ]
}
```
<!-- endref -->

The JSON file always contains the PDF version, the number of pages and, for every page, the width and height of its crop
box in points, its rotation, and its media box when it differs from the crop box. By default, it also contains the
document information: title, author, subject, keywords, creator, producer, language, PDF/A and PDF/UA conformance, and
whether the document is encrypted.

The pages are rendered at 72 dots per inch (one pixel per point) on a white background, with the appearances of the
annotations. The fonts the document does not embed are replaced by the fonts bundled with `Meziantou.Framework.Pdf`,
not by the fonts of the system, so the pages render the same on every machine.

## Settings

Pass [PdfSnapshotSettings](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings) to [AddPdf](cref:Meziantou.Framework.SnapshotTesting.Pdf.SnapshotSettingsPdfExtensions.AddPdf) to change the rendering of the pages and the content of the JSON file:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/PdfSnapshotTests.cs;region=SnapshotTestingPdf_Settings;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = SnapshotSettings.Default with { };
settings.AddPdf(new PdfSnapshotSettings
{
    Dpi = 36,                              // Half the size of the default 72 dots per inch
    Metadata = PdfSnapshotMetadata.All,    // Information, dates, text, outlines and annotations
});

Snapshot.Validate(document, SnapshotType.Pdf, settings);
```
<!-- endref -->

| Property | Default | Description |
| --- | --- | --- |
| [Dpi](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings.Dpi) | `72` | The resolution the pages are rendered at, in dots per inch |
| [RenderAnnotations](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings.RenderAnnotations) | `true` | Draws the appearances of the annotations on the pages |
| [UseSystemFonts](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings.UseSystemFonts) | `false` | Replaces the fonts the document does not embed by the fonts of the system. The rendering then depends on the machine |
| [Password](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings.Password) | `null` | The user or owner password of an encrypted document |
| [Metadata](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings.Metadata) | [DocumentInformation](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotMetadata.DocumentInformation) | The optional parts of the JSON file |

[Metadata](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotSettings.Metadata) is a combination of [PdfSnapshotMetadata](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotMetadata) flags:

| Flag | Adds |
| --- | --- |
| [DocumentInformation](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotMetadata.DocumentInformation) | The title, author, subject, keywords, creator, producer, language, conformance and encryption |
| [Dates](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotMetadata.Dates) | The creation and modification dates |
| `Text` | The text of every page, one entry per line |
| [Outlines](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotMetadata.Outlines) | The bookmarks |
| [Annotations](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotMetadata.Annotations) | The subtype, rectangle and contents of the annotations, and the URI of links |
| [All](cref:Meziantou.Framework.SnapshotTesting.Pdf.PdfSnapshotMetadata.All) | Everything above |

The dates are not written by default: generators usually set them to the current time, so they change every time a
document is generated. When you include them, scrub them: the JSON file is a text snapshot, so the
[scrubbers](../03-WritingTests/06-DeterministicSnapshots.md) of the settings apply to it.

## Allow small rendering differences

The pages are compared pixel by pixel by default. To accept minor rendering differences, pass image comparison settings
(see [Allow small differences](02-Images.md#allow-small-differences) for the available measures):

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/PdfSnapshotTests.cs;region=SnapshotTestingPdf_ImageComparisonSettings;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
settings.AddPdf(imageComparisonSettings: new ImageComparisonSettings
{
    Ssimulacra2Threshold = 90, // SSIMULACRA 2: 100 = identical, about 90 = not visible
});
```
<!-- endref -->
