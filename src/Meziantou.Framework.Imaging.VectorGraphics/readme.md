# Meziantou.Framework.Imaging.VectorGraphics

Draws the scenes of [Meziantou.Framework.VectorGraphics](https://www.nuget.org/packages/Meziantou.Framework.VectorGraphics)
(charts, diagrams, your own drawings) with [Meziantou.Framework.Imaging](https://www.nuget.org/packages/Meziantou.Framework.Imaging):
images in any format the library encodes (PNG, WebP, JPEG...), or vector drawing on a canvas. It embeds Liberation Sans,
whose widths are those of Helvetica and Arial, so an image lays out like the SVG of the same drawing.

## Usage

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
