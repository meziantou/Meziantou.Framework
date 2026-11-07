---
title: PDF output
description: Print a document to a PDF with selectable text, embedded fonts and images, links, bookmarks, metadata and a structure tree.
---

The `Meziantou.Framework.WebPlatform.PdfRendering` package prints a document to PDF with
<NuGetReference Id="Meziantou.Framework.Pdf" />. The document is laid out for printing,
as for [Printing pages](03-PrintingPages.md), and each page becomes a PDF page of vector graphics and text:

<InstallNuGetPackage Id="Meziantou.Framework.WebPlatform.PdfRendering" />

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.PdfRendering.Tests/PdfPrintTests.cs;region=PdfPrintExtensions_PrintToPdf;format=md-fenced-code-block;dedent=true -->
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

[PrintToPdf](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintExtensions.PrintToPdf) returns a [PdfDocument](cref:Meziantou.Framework.Pdf.PdfDocument), or writes the PDF to a stream. It takes the [PrintOptions](cref:Meziantou.Framework.WebPlatform.Dom.PrintOptions) of the printing (the size
of the pages, the margins, the header and the footer) and the [PdfPrintOptions](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions) of the PDF:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.PdfRendering.Tests/PdfPrintTests.cs;region=WebPlatformPdf_PrintOptions;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new ImagingRenderingBackend() });
context.Navigate("https://example.com/invoices/42", _ => """
    <!DOCTYPE html>
    <title>Invoice 42</title>
    <h1>Invoice 42</h1>
    <p>Thank you for your order.</p>
    """);
context.EventLoop.RunUntilIdle();

using var stream = new MemoryStream(); // or a FileStream
context.PrintToPdf(stream, new PrintOptions
{
    PageWidth = 794,    // A4 at 96 dpi, in CSS px, for the pages without @page size
    PageHeight = 1123,
    MarginTop = 48,
    MarginBottom = 48,
    DisplayHeaderFooter = true,
    FooterTemplate = """<div style="width: 100%; font-size: 10px; text-align: center"><span class="pageNumber"></span> / <span class="totalPages"></span></div>""",
});
```
<!-- endref -->

## What the PDF contains

- **Text** that can be selected, searched and extracted: the glyphs of the fonts the layout used, embedded as subsets, at
  the positions the layout computed.
- **Images**, embedded once each. A JPEG image is embedded as it is. The browsing context needs a [RenderingBackend](cref:Meziantou.Framework.WebPlatform.Rendering.RenderingBackend)
  ([ImagingRenderingBackend](cref:Meziantou.Framework.WebPlatform.Imaging.ImagingRenderingBackend)) to keep the bytes of the images.
- **Links**: the `a` elements are link annotations; a link to a fragment of the document goes to its place in the PDF.
- **Bookmarks**: the headings are the outline of the document.
- **Metadata**: the title, the language, the author, the description and the keywords of the document.
- **Structure**: the PDF is tagged. Its structure tree is made from the elements (headings, paragraphs, lists, tables,
  figures with their alternate text, links), and the decorations are artifacts.

## PDF options

| [PdfPrintOptions](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions) | Default | Effect |
| --- | --- | --- |
| [Tagged](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions.Tagged) | true | Writes the structure tree |
| [IncludeLinks](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions.IncludeLinks) | true | Writes the link annotations |
| [IncludeOutlines](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions.IncludeOutlines) | true | Writes the bookmarks |
| [PdfUAConformance](cref:Meziantou.Framework.Pdf.PdfUAConformance) | none | Writes a PDF/UA-1 document (accessible PDF) |
| [PdfAConformance](cref:Meziantou.Framework.Pdf.PdfAConformance) | none | Writes a PDF/A document (archiving) |
| [Title](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions.Title) | the `title` element | The title of the PDF |
| [RasterizationScale](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions.RasterizationScale) | 2 | The pixels per CSS pixel of what PDF cannot draw |

## What is drawn as an image

PDF cannot draw everything that CSS can: the blurred shadows, the filters, the masks, the compositing operators other
than the default one (source-over), the 3D transforms, the conic gradients and the color glyphs are drawn as images, at [RasterizationScale](cref:Meziantou.Framework.WebPlatform.PdfRendering.PdfPrintOptions.RasterizationScale)
pixels per CSS pixel, in place.
