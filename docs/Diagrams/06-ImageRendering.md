---
title: Image rendering
description: Render diagrams as PNG, WebP or JPEG images, or draw them on an existing image.
---

`Meziantou.Framework.Imaging.VectorGraphics` draws the diagrams with [Meziantou.Framework.Imaging](../Imaging/01-Introduction.md),
as it draws the charts: a diagram is an [ISceneSource](cref:Meziantou.Framework.VectorGraphics.ISceneSource).

<InstallNuGetPackage Id="Meziantou.Framework.Imaging.VectorGraphics" />

<!-- ref:../../tests/Meziantou.Framework.Diagrams.Tests/FlowchartTests.cs;region=Diagrams_ToImage;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
flowchart.AddEdge("draft", "review");
flowchart.AddEdge("review", "publish");

// Meziantou.Framework.Imaging.VectorGraphics draws any diagram: image.Save("flow.png")
using var image = flowchart.ToImage(new SceneImageOptions { Scale = 2 });
```
<!-- endref -->

The diagram is laid out with the fonts that draw the image, Liberation Sans by default, so its texts fit their shapes.
See [image rendering](../VectorGraphics/03-ImageRendering.md) for the options and the fonts.

## Draw on a canvas

[DrawScene](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.DrawScene) draws a diagram on an
existing image, where the transform, the clip and the layers of the canvas apply. The size of a diagram depends on its
texts: [MeasureScene](cref:Meziantou.Framework.Imaging.VectorGraphics.SceneImageRenderer.MeasureScene) lays it out with
the same fonts and gives the rectangle it covers, to place it:

<!-- ref:../../tests/Meziantou.Framework.Diagrams.Tests/FlowchartTests.cs;region=Diagrams_DrawOnCanvas;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram();
flowchart.AddEdge("start", "end");

// The size depends on the texts: measure the diagram to place it
var size = SceneImageRenderer.MeasureScene(flowchart);
using var report = new Image<Rgba32>(400, 400, Colors.White); // or Image.Load<Rgba32>("report.png")
var position = new PointD(report.Width - size.Width - 10, 10);
report.Draw(canvas => canvas.DrawScene(flowchart, position));
```
<!-- endref -->
