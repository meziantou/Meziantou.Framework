---
title: Fonts
description: The fonts bundled with the library, the web fonts of the pages, and the fonts installed on the machine.
---

The fonts decide the size of the text, so they change the layout as well as the pixels. By default, a page only uses the
fonts bundled with the library and its own web fonts: the same page gives the same layout and the same screenshots on
every machine.

## Bundled fonts

| Family | Font | Metric-compatible with |
| --- | --- | --- |
| `serif` (the default) | Tinos | Times New Roman |
| `sans-serif` | Arimo | Arial |
| `monospace` | Cousine | Courier New |
| `math` | STIX Two Math | |

The bundled fonts cover the Latin, Greek and Cyrillic scripts, in regular, bold, italic and bold italic. A page that
names `Arial`, `Helvetica`, `Times New Roman`, `Times`, `Courier New` or `Courier` gets the metric-compatible bundled
font: its text has the same size as with the original font. `cursive`, `fantasy` and `emoji` have no bundled font.

## Web fonts

The `@font-face` rules of the pages load their fonts through the [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader) (TrueType, OpenType, WOFF and WOFF2),
and `document.fonts` (the CSS Font Loading API) loads them from scripts. A font face can also come from data
(`new FontFace(name, bytes)`).

## Fonts of the machine

[BrowsingContextOptions.UseSystemFonts](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.UseSystemFonts) lets `font-family` and `local()` name the fonts installed on the machine:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_SystemFonts;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    UseSystemFonts = true, // font-family and local() can name the fonts installed on the machine
    RenderingBackend = new ImagingRenderingBackend(),
});

context.Navigate("https://example.com/", _ => """<p style="font-family: 'Segoe UI', sans-serif">Hello</p>""");
using var screenshot = context.TakeScreenshot(); // the pixels depend on the fonts of the machine
```
<!-- endref -->

- The bundled fonts keep their names and the generic families: only the families that the bundled fonts do not have come
  from the machine, and the default font does not change.
- The layout and the screenshots then depend on the machine. Keep it off when you compare screenshots, or when the pages
  are untrusted.
