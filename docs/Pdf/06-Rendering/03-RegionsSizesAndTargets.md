---
title: Regions, sizes and targets
description: Render part of a page, fit a page to a size in pixels, draw on an image of the application, choose the antialiasing, and cancel a rendering.
---

By default, [PdfRenderer](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderer) renders the crop box of a page to a new
[Image\<Rgba32\>](cref:Meziantou.Framework.Imaging.Image{TPixel}) at the resolution of
[Dpi](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.Dpi). The options below change what is rendered, at
which size, and on which image.

## Render a region of a page

[Region](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.Region) is a rectangle of the default user space of
the page, the coordinates of its boxes: only that part is rendered. It is intersected with the media box, and the image is
turned by the rotation of the page, as the whole page would be. A region that does not intersect the media box throws
`ArgumentException`.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=PdfRendering_RenderRegion;format=md-fenced-code-block;dedent=true -->
```csharp
// The square of 50 x 50 points at (50, 50) of the page, as an image 100 pixels wide
var options = new PdfRenderOptions
{
    Region = new PdfRectangle(50, 50, 100, 100),
    Width = 100,
};
using var image = page.Render(options);
```
<!-- endref -->

## Fit a page to a size

[Width](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.Width) and
[Height](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.Height) choose the size of the image instead of the
resolution: the resolution is the one that gives the page (or the region), as it is shown after its rotation, that width
or that height. With both, the page is fitted inside them and keeps its proportions, so one side of the image can be
shorter. They are the simplest way to make thumbnails: `Width = 200` and `Height = 200` give images of at most 200 x
200 pixels, whatever the size and the orientation of the pages.

[PdfRenderer.GetImageSize](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderer.GetImageSize) gives the size of the image
a page renders to with options, without rendering it. The image is limited to 32,768 pixels on each side: a larger size
throws [PdfResourceLimitException](cref:Meziantou.Framework.Pdf.PdfResourceLimitException).

## Render to an image of the application

[RenderPage](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderer.RenderPage) also draws on an image you allocate, of any
pixel format: a [Gray8](cref:Meziantou.Framework.Imaging.Gray8) image gives a grayscale rendering, a 16-bit image keeps the
precision of the colors, and an image you reuse saves an allocation per page.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=PdfRendering_RenderToImage;format=md-fenced-code-block;dedent=true -->
```csharp
// A grayscale image of the size of the page, allocated by the application
var size = PdfRenderer.GetImageSize(page);
using var gray = new Image<Gray8>(size.Width, size.Height);
PdfRenderer.RenderPage(page, gray);
```
<!-- endref -->

[PdfRenderSession.RenderPage](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderSession.RenderPage) has the same
overloads, to render the pages of a document with shared caches.

The page is drawn from the top left corner of the image, at the size `GetImageSize` gives; what exceeds the image is not
drawn. The background of the options is painted first, over the pixels of the image, then the page: set
[Background](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.Background) to `null` to draw the page over what the
image shows. The colors are converted from sRGB to the pixel format; the renderer has no CMYK output.

## Antialiasing

The edges of the shapes are antialiased: a pixel is painted in proportion to the area the shape covers.
[PathAntialiasing](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.PathAntialiasing) (paths, images and
shadings) and [TextAntialiasing](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.TextAntialiasing) (glyphs) set
to `false` draw hard edges instead, every pixel painted entirely or not at all: for comparisons of images that must not
depend on fractions of pixels, or for bilevel output.

## Cancel a rendering

A page can take long to render, and a request can be abandoned before it ends. Pass a `CancellationToken`: the
rendering checks it as it draws the operators, glyphs, images and shadings of the page, and throws
`OperationCanceledException`.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=PdfRendering_Cancellation;format=md-fenced-code-block;dedent=true -->
```csharp
// A page that takes too long is abandoned: OperationCanceledException
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var image = PdfRenderer.RenderPage(page, new PdfRenderOptions { Dpi = 150 }, timeout.Token);
```
<!-- endref -->

The cancellation does not replace [MaxOperations](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.MaxOperations),
which bounds the work of a hostile page whatever the time it takes. When the image is yours, the shapes drawn before the
cancellation stay on it.
