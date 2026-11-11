---
title: PDF rendering
description: Draw a scene, a chart or a diagram as a PDF document or on a PDF page, with the fonts of the scene embedded.
---

`Meziantou.Framework.Pdf.VectorGraphics` draws scenes with [Meziantou.Framework.Pdf](../Pdf/01-Introduction.md), as
vector shapes and text.

<InstallNuGetPackage Id="Meziantou.Framework.Pdf.VectorGraphics" />

The types are in the [Meziantou.Framework.Pdf.VectorGraphics](cref:Meziantou.Framework.Pdf.VectorGraphics) namespace.

## Draw a scene as PDF

[ToPdf](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfRenderer.ToPdf) draws a scene, a chart or a diagram as a
document of one page of its size. The title and the description of the scene become the title and the subject of the
document. Lay a scene out with the measurer of
[CreateMeasurer](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfRenderer.CreateMeasurer), so its texts fit the
fonts that draw them; a chart or a diagram passed to `ToPdf` is laid out with it:

<!-- ref:../../tests/Meziantou.Framework.Pdf.VectorGraphics.Tests/ScenePdfRendererTests.cs;region=PdfVectorGraphics_ToPdf;format=md-fenced-code-block;dedent=true -->
```csharp
// The fonts of the texts: the layout measures the texts with them, and the PDF embeds the subsets it uses
var fonts = SceneFontCollection.Load(Path.Combine(testData, "LiberationSans-Regular.woff2"), Path.Combine(testData, "LiberationSans-Bold.woff2"));

var plot = new Plot { Title = "Sales by region", Width = 400, Height = 300 };
plot.Add(new BarMark<(string Region, double Amount)>([("North", 12), ("South", 7)], x: sale => sale.Region, y: sale => sale.Amount));

var document = plot.ToPdf(new ScenePdfOptions { Fonts = fonts });
var pdf = document.ToByteArray();
```
<!-- endref -->

[DrawScene](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfRenderer.DrawScene) draws a scene on a page of an
existing document, over its content, with its top-left corner at a position of the page (whose y axis points up), and
returns the rectangle it covers.

[ScenePdfOptions.Scale](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfOptions.Scale) is the number of points per
unit of the drawing. It is 0.75 by default: a unit is a CSS pixel, so a chart of 640 by 400 pixels is a page of
480 by 300 points, the size a browser gives its SVG.

## Fonts

Each text is drawn with the face of its family of the [fonts of the scene](05-Fonts.md)
([Scene.Fonts](cref:Meziantou.Framework.VectorGraphics.Scene.Fonts)), then of
[ScenePdfOptions.Fonts](cref:Meziantou.Framework.Pdf.VectorGraphics.ScenePdfOptions.Fonts), and the document embeds
the subsets of the faces it uses, so it displays the same everywhere and its text can be extracted. The characters a
family does not have are drawn with the other families of the fonts. A face that cannot be embedded (a license that
forbids it, CFF2 or bitmap outlines) is replaced by Helvetica.

Without fonts, the texts are drawn with the standard fonts Helvetica, Helvetica-Bold, Helvetica-Oblique and
Helvetica-BoldOblique, which are not embedded: their widths are the ones
[AfmTextMeasurer](cref:Meziantou.Framework.VectorGraphics.AfmTextMeasurer) measures, so the texts fit. These fonts only
have the characters of Western European languages: the other characters are drawn as question marks. Use fonts for
other scripts.

## What is drawn

The shapes, the paths (arcs and quadratic curves are converted to cubic curves), the images, the linear and radial
gradients, the opacity (a group is drawn as a transparency group), the clipping rectangles and paths and the transforms
are drawn as in the SVG. A JPEG image is embedded as it is; the other formats are decoded and compressed again. The hyperlinks of
the groups become link annotations of the page; only absolute addresses are linked. The tooltips, the CSS classes and
the data attributes are not drawn. A gradient whose stops have different opacities is drawn with their average
opacity.
