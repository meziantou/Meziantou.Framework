# Meziantou.Framework.SnapshotTesting.Pdf

`Meziantou.Framework.SnapshotTesting.Pdf` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with PDF snapshots based on [`Meziantou.Framework.Pdf`](https://www.nuget.org/packages/Meziantou.Framework.Pdf), a fully managed PDF library with no native dependency. A PDF document is not compared byte for byte: each page is rendered to a PNG image, compared by its pixels, and the metadata of the document is written to a JSON file. A change shows which page differs, and every page can be reviewed as an image.

## Setup

Call `AddPdf()` on your `SnapshotSettings` to register the PDF serializer and the comparer of the pages, for instance in
a module initializer (`ModuleInitializer` is in the `System.Runtime.CompilerServices` namespace, `SnapshotSettings` in
`Meziantou.Framework.SnapshotTesting` and `AddPdf` in `Meziantou.Framework.SnapshotTesting.Pdf`):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/SnapshotConfiguration.cs;region=SnapshotTestingPdf_Setup;format=md-fenced-code-block;dedent=true -->
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

Then validate documents from your tests, as the bytes of a file (`byte[]` or `Stream`) or as a `PdfDocument`:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/PdfSnapshotTests.cs;region=SnapshotTestingPdf_ValidateBytes;format=md-fenced-code-block;dedent=true -->
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

`AddPdf()` registers:

- a serializer for `PdfDocument` values, and for `byte[]` and `Stream` values validated as `SnapshotType.Pdf`. Data that is not a PDF document is stored as is, in a `.pdf` file;
- the `ImageComparer` of [`Meziantou.Framework.SnapshotTesting.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting.Imaging) for `SnapshotType.Png`, so the pages are compared by their pixels. A comparer already registered for PNG snapshots, for instance by `AddImaging()`, is kept.

> [!IMPORTANT]
> Register once for the whole test assembly, not inside a test method. `SnapshotSettings.Default` is
> shared by every test in the process and its collections are not safe for concurrent modification.

## Snapshot files

A document produces one file for its metadata and one file per page, numbered from 0:

- `<Name>_0.verified.json`: the metadata of the document;
- `<Name>_1.verified.png` to `<Name>_<n>.verified.png`: the pages, so the file of page `n` has the index `n`.

A document without pages produces only `<Name>.verified.json`.

The pages are rendered at 72 dots per inch (one pixel per point) on a white background, with the appearances of the
annotations. The fonts the document does not embed are replaced by the fonts bundled with `Meziantou.Framework.Pdf`,
not by the fonts of the system, so the pages render the same on every machine.

The JSON file always contains the PDF version, the number of pages and, for every page, the width and height of its crop
box in points, its rotation, and its media box when it differs from the crop box. By default, it also contains the
document information: title, author, subject, keywords, creator, producer, language, PDF/A and PDF/UA conformance, and
whether the document is encrypted.

## Settings

`PdfSnapshotSettings` configures the rendering of the pages and the content of the JSON file. `Metadata` selects the
optional parts of the JSON file (`PdfSnapshotMetadata` flags): `DocumentInformation`, `Dates` (creation and modification
dates), `Text` (the text of every page, one entry per line), `Outlines` (the bookmarks) and `Annotations` (subtype,
rectangle, contents and the URI of links).

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/PdfSnapshotTests.cs;region=SnapshotTestingPdf_Settings;format=md-fenced-code-block;dedent=true -->
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
| `Dpi` | `72` | The resolution the pages are rendered at, in dots per inch. |
| `RenderAnnotations` | `true` | Draws the appearances of the annotations on the pages. |
| `UseSystemFonts` | `false` | Replaces the fonts the document does not embed by the fonts of the system. The rendering then depends on the machine. |
| `Password` | `null` | The user or owner password of an encrypted document. |
| `Metadata` | `DocumentInformation` | The optional parts of the JSON file. |

The dates are not written by default: generators usually set them to the current time, so they change every time a
document is generated. When you include them, the JSON file is a text snapshot, so the scrubbers of the
`SnapshotSettings` apply to it.

## Page comparison

The pages are compared pixel by pixel by default. To allow minor rendering differences, pass image comparison settings
(see the [readme of `Meziantou.Framework.SnapshotTesting.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting.Imaging)
for the available measures):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Pdf.Tests/PdfSnapshotTests.cs;region=SnapshotTestingPdf_ImageComparisonSettings;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
settings.AddPdf(imageComparisonSettings: new ImageComparisonSettings
{
    Ssimulacra2Threshold = 90, // SSIMULACRA 2: 100 = identical, about 90 = not visible
});
```
<!-- endref -->
