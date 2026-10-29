---
title: Rendering
description: Take screenshots of pages and elements, print pages as images or PDF, draw on canvases, and choose the fonts.
---

The library lays the pages out and paints them, but it draws no pixel by itself: the core package has no graphics
dependency. A rendering backend draws the pages. The `Meziantou.Framework.WebPlatform.Imaging` package provides one, with
[Meziantou.Framework.Imaging](../../01-Imaging/01-Introduction.md): fully managed, with no native dependency, so the same
page gives the same pixels on every platform.

```bash
dotnet add package Meziantou.Framework.WebPlatform.Imaging
```

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_TakeScreenshots;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 1280, Height = 720, DevicePixelRatio = 2 },
    DocumentLoader = DocumentLoader.Create(),
    RenderingBackend = new ImagingRenderingBackend(),
});

context.Navigate("https://example.com/");
await context.EventLoop.RunUntilIdleAsync();

await File.WriteAllBytesAsync("page.png", context.TakeScreenshotAsPng());            // the viewport: 2560x1440 pixels
using var title = context.Document.QuerySelector("h1")!.TakeScreenshot();           // an element, as an Image<Rgba32>

// Print the document: the print media type, the @page rules, one image per page
var pages = context.RenderPagesAsPng(new PrintOptions { PageWidth = 794, PageHeight = 1123 }); // A4 at 96 dpi
```
<!-- endref -->

## What the backend does

Give an `ImagingRenderingBackend` to `BrowsingContextOptions.RenderingBackend`. It is used for:

- the screenshots of the pages and of the elements, and the pages printed as images;
- the 2D canvases (`canvas.getContext("2d")`, `OffscreenCanvas`) and the images of the paint worklets;
- the decoding of the images of the pages: PNG (and animated PNG), JPEG, GIF, WebP, AVIF, JPEG XL, BMP and ICO, with their
  EXIF orientation and their color profile;
- the encoding of the canvases as JPEG and WebP (`toDataURL()`, `toBlob()`).

Without a backend, the layout and the geometry work the same, but the images are not decoded (only their size is read),
`getContext("2d")` returns null, and the documents do not keep the bytes of their images. A backend can be shared by
several browsing contexts.

## In this section

- [Screenshots](02-Screenshots.md): the viewport, the full page, an element or an area, as an image, PNG or JPEG.
- [Printing pages](03-PrintingPages.md): lay a document out for printing, with the `@page` rules, one image per page.
- [PDF output](04-PdfOutput.md): print a document to a PDF with selectable text, links and bookmarks.
- [Canvas](05-Canvas.md): draw on the canvases of a page from .NET or from its scripts.
- [Fonts](06-Fonts.md): the bundled fonts, the web fonts and the fonts of the machine.
