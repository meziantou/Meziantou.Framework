---
title: Canvas
description: Draw on the 2D canvases of a page from .NET or from its scripts, and read their pixels.
---

With a rendering backend, the `canvas` elements have a 2D context, as in a browser. The scripts of the page draw on it,
and so can .NET, with the same API:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Imaging.Tests/DocumentationImageTests.cs;region=WebPlatformImaging_CanvasChart;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    RenderingBackend = new ImagingRenderingBackend(),
    ScriptingEnabled = true, // as in a browser, a canvas shows its fallback content when scripting is disabled
});
var document = context.Navigate("https://example.com/", _ => """<canvas id="chart" width="320" height="160"></canvas>""");
context.EventLoop.RunUntilIdle();

// Without a rendering backend, GetContext("2d") returns null, as in a browser without canvas support
var canvas = (HtmlCanvasElement)document.GetElementById("chart")!;
var context2D = (CanvasRenderingContext2D)canvas.GetContext("2d")!;
context2D.FillStyle = "#f8fafc";
context2D.FillRect(0, 0, 320, 160);

double[] values = [40, 90, 65, 120, 80];
for (var i = 0; i < values.Length; i++)
{
    context2D.FillStyle = i == 3 ? "#ea580c" : "#0ea5e9";
    context2D.FillRect(20 + (i * 60), 140 - values[i], 40, values[i]);
}

context2D.StrokeStyle = "#334155";
context2D.LineWidth = 2;
context2D.BeginPath();
context2D.MoveTo(10, 141);
context2D.LineTo(310, 141);
context2D.Stroke();

using var screenshot = canvas.TakeScreenshot();
```
<!-- endref -->

![A bar chart of five blue and orange bars over a dark axis](images/CanvasChart.png)

- [CanvasRenderingContext2D](cref:Meziantou.Framework.WebPlatform.Dom.CanvasRenderingContext2D) has all the members of the canvas of the browsers: paths, rectangles, text, images,
  gradients and patterns, transformations, shadows, filters, compositing, [GetImageData](cref:Meziantou.Framework.WebPlatform.Dom.CanvasRenderingContext2DBase.GetImageData) and [PutImageData](cref:Meziantou.Framework.WebPlatform.Dom.CanvasRenderingContext2DBase.PutImageData).
- The bitmap of a canvas has 8 bits per component by default. Request [CanvasColorType.Float16](cref:Meziantou.Framework.WebPlatform.Dom.CanvasColorType.Float16)
  in [CanvasRenderingContext2DSettings.ColorType](cref:Meziantou.Framework.WebPlatform.Dom.CanvasRenderingContext2DSettings.ColorType) to use half-float pixels. [ToDataUrl()](cref:Meziantou.Framework.WebPlatform.Dom.HtmlCanvasElement.ToDataUrl) and [ToBlob()](cref:Meziantou.Framework.WebPlatform.Dom.HtmlCanvasElement.ToBlob) serialize it as PNG,
  JPEG or WebP.
- A canvas is rendered only when scripting is enabled: as in a browser, a page without scripting shows the fallback
  content of the `canvas` element. Set [ScriptingEnabled](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ScriptingEnabled) (or give a script engine) when you draw from .NET.
- [OffscreenCanvas](cref:Meziantou.Framework.WebPlatform.Dom.OffscreenCanvas) works the same way, also in the workers.
- Without a backend, `GetContext("2d")` returns null, as in a browser without canvas support.

## Half-float pixels

The Imaging backend stores `float16` canvases as half-precision floating-point pixels. [GetContextAttributes](cref:Meziantou.Framework.WebPlatform.Dom.CanvasRenderingContext2DBase.GetContextAttributes)
reports that backing format, including after a resize or reset. Request [ImageDataPixelFormat.RgbaFloat16](cref:Meziantou.Framework.WebPlatform.Dom.ImageDataPixelFormat.RgbaFloat16)
when reading pixels to retain their precision: the default readback is still `rgba-unorm8`.

[PutImageData](cref:Meziantou.Framework.WebPlatform.Dom.CanvasRenderingContext2DBase.PutImageData) preserves half-float samples, including negative
colors and highlights above one. Color-space conversion is performed before quantization and does not clip half-float colors to the destination gamut.
Drawing images, creating image bitmaps, cropping, resizing and transferring an offscreen bitmap also retain floating-point pixels.
PNG serialization uses 16-bit integer samples; values outside zero to one are clipped. Decoding these PNG images and creating image bitmaps from them retain their higher precision. Incomplete 16-bit PNG images retain the decoded rows and interlace passes at that precision; pixels not decoded remain transparent. JPEG and WebP use 8-bit samples.

Solid paints preserve floating-point colors. Gradients and intermediate layers, filters and shadows still use the drawing backend's 16-bit normalized colors.
Drawing a floating-point image onto an 8-bit canvas clips its color channels to zero to one.
The canvas layers (`beginLayer()`) are not implemented.
