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
See [image rendering](../VectorGraphics/03-ImageRendering.md) for the options, the fonts, and drawing on a canvas.
