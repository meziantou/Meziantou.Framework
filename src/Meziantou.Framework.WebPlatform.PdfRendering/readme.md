# Meziantou.Framework.WebPlatform.PdfRendering

Prints the documents of [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md) to PDF with
[Meziantou.Framework.Pdf](../Meziantou.Framework.Pdf/readme.md): the document is laid out with the `print` media type and
paginated (`@page` rules, page breaks, page-margin boxes), and each page becomes a page of vector graphics and text.

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.PdfRendering.Tests/PdfPrintTests.cs;region=PdfPrintExtensions_PrintToPdf;format=md-fenced-code-block;dedent=true -->
```csharp
// The rendering backend keeps and decodes the images, and rasterizes what PDF cannot draw
using var context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new ImagingRenderingBackend() });
context.Navigate("https://example.com/", _ => """
    <!DOCTYPE html>
    <html lang="en">
    <title>Monthly report</title>
    <style>@page { size: A4; margin: 2cm }</style>
    <h1>Monthly report</h1>
    <p>See the <a href="https://example.com/details">details</a>.</p>
    """);
context.EventLoop.RunUntilIdle();

// A tagged PDF document (PDF/UA-1): text, links, bookmarks and the structure of the document
var pdf = context.PrintToPdf(options: new PdfPrintOptions { PdfUAConformance = PdfUAConformance.PdfUA1 });
var bytes = pdf.ToByteArray();
```
<!-- endref -->

- The text is shown with the font files the layout used, embedded as subsets, at the positions the layout computed: it can
  be selected, searched and extracted.
- The images are embedded (the JPEG images as they are). The browsing context needs a `RenderingBackend`, such as the
  `ImagingRenderingBackend` of [Meziantou.Framework.WebPlatform.Imaging](../Meziantou.Framework.WebPlatform.Imaging/readme.md),
  to keep the bytes of the images.
- The links are link annotations (a fragment of the document is a destination in the PDF document), the headings are the
  outline (bookmarks), and the title, language, author, description and keywords of the document are its metadata.
- The document is a tagged PDF: its structure tree is made from the elements of the document (headings, paragraphs, lists,
  tables, figures with their alternate text, links), and the decorations are artifacts. Set `PdfUAConformance` and
  `PdfAConformance` to write PDF/UA-1 and PDF/A documents.
- What PDF cannot draw is drawn as an image (`RasterizationScale` pixels per CSS px): the blurred shadows, the filters, the
  masks and the compositing operators other than source-over, the 3D transforms, the conic gradients and the color glyphs.

See [rendering.md](../Meziantou.Framework.WebPlatform/docs/rendering.md#pdf-output).
