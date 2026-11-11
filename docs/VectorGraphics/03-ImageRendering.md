---
title: Image rendering
description: Draw a scene, a chart or a diagram as a PNG, WebP or JPEG image, or on a canvas, and choose its fonts.
---

`Meziantou.Framework.Imaging.VectorGraphics` draws scenes with [Meziantou.Framework.Imaging](../Imaging/01-Introduction.md).

<InstallNuGetPackage Id="Meziantou.Framework.Imaging.VectorGraphics" />

The types are in the [Meziantou.Framework.Imaging.VectorGraphics](cref:Meziantou.Framework.Imaging.VectorGraphics)
namespace.

## Render a scene

[ToImage](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.ToImage) renders a scene as an image of its
size, and [DrawScene](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.DrawScene) draws it on a canvas,
where the transform, the clip and the layers of the canvas apply. Lay the scene out with the measurer of
[CreateMeasurer](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.CreateMeasurer), so its texts fit
the fonts that draw them:

<!-- ref:../../tests/Meziantou.Framework.Imaging.VectorGraphics.Tests/SceneRendererTests.cs;region=ImagingVectorGraphics_ToImage;format=md-fenced-code-block;dedent=true -->
```csharp
// Lay the scene out with the fonts that draw the image, so its texts fit
var measurer = SceneImageRenderer.CreateMeasurer();
var label = "Hello";
var font = new SceneFont(16);
var width = measurer.Measure(label, font).Width + 20;

var scene = new Scene(width, 40, measurer.FontFamily) { Background = SceneColor.White };
scene.Root.Add(new SceneText(10, 26, label, font, SceneColor.Black));

using var image = scene.ToImage(new SceneImageOptions { Scale = 2 });
Assert.Equal(80, image.Height);

// Or draw it on an existing image
using var poster = new Image<Rgba32>(300, 200, Colors.White);
poster.Draw(canvas => canvas.DrawScene(scene, new PointD(50, 50)));
```
<!-- endref -->

[Scale](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.Scale) multiplies the pixels, for
high-density screens. The image ignores hyperlinks, tooltips, CSS classes and `data-*` attributes.

## Charts and diagrams

The same methods take an [ISceneSource](cref:Meziantou.Framework.VectorGraphics.ISceneSource), such as a chart or a
diagram: they lay it out with the fonts of the options, then draw it.
[MeasureScene](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.MeasureScene) gives the rectangle that
[DrawScene](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.DrawScene) covers, to place the drawing
before drawing it. See the image rendering of [charts](../Charts/07-ImageRendering.md) and
[diagrams](../Diagrams/06-ImageRendering.md).

## Fonts

The package embeds Liberation Sans in its four styles ([SceneFonts](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneFonts)),
subset to the Latin characters. Its widths are those of Helvetica and Arial, the widths the SVG uses by default, so an
image lays out like the SVG of the same drawing; the kerning differs a little.

For other scripts, pass a font that has them in [Font](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.Font).
[BoldFont](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.BoldFont),
[ItalicFont](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.ItalicFont) and
[BoldItalicFont](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.BoldItalicFont) set the fonts of the
other styles; when only [Font](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.Font) is set, it draws
every style.

To use several families, or fonts for other scripts after a Latin font, set
[Fonts](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageOptions.Fonts) to a font collection: the layout
measures each text with the face of its family, and the image draws it with the same face. The fonts of a scene
([Scene.Fonts](cref:Meziantou.Framework.VectorGraphics.Scene.Fonts)) take precedence over the fonts of the options. See
[fonts of a scene](05-Fonts.md).
