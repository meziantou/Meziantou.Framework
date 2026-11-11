---
title: Getting started
description: Install the package, build a scene of shapes and texts, and write it as SVG.
---

## Install the package

Add the `Meziantou.Framework.VectorGraphics` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.VectorGraphics" />

The types are in the [Meziantou.Framework.VectorGraphics](cref:Meziantou.Framework.VectorGraphics) namespace.

## Build a scene

A [Scene](cref:Meziantou.Framework.VectorGraphics.Scene) has a size, a font family and a list of elements, drawn in
order. [SvgSceneRenderer](cref:Meziantou.Framework.VectorGraphics.SvgSceneRenderer) writes it as SVG:

<!-- ref:../../tests/Meziantou.Framework.VectorGraphics.Tests/SvgSceneRendererTests.cs;region=VectorGraphics_CreateScene;format=md-fenced-code-block;dedent=true -->
```csharp
var scene = new Scene(width: 200, height: 80, fontFamily: AfmTextMeasurer.Default.FontFamily) { Title = "A box", Background = SceneColor.White };

// Shapes are filled and stroked; texts are positioned by their baseline
scene.Root.Add(new SceneRectangle(10, 10, 180, 60) { Fill = (SceneColor)"#e0f2fe", Stroke = (SceneColor)"#0369a1", StrokeWidth = 2, CornerRadius = 8 });
scene.Root.Add(new SceneText(100, 45, "Hello", new SceneFont(16, Bold: true), (SceneColor)"#0c4a6e") { Anchor = SceneTextAnchor.Middle });

// Paths use the commands of SVG
var arrow = new PathBuilder().MoveTo(20, 60).LineTo(60, 60).LineTo(55, 55).MoveTo(60, 60).LineTo(55, 65).Build();
scene.Root.Add(new ScenePath(arrow) { Stroke = (SceneColor)"#0369a1" });

var svg = SvgSceneRenderer.Render(scene);
```
<!-- endref -->

The coordinates are in pixels, with the origin at the top-left corner and the y axis pointing down. A
[SceneText](cref:Meziantou.Framework.VectorGraphics.SceneText) is one line of text, positioned by its baseline and
anchored at its start, its middle or its end; its [SceneFont](cref:Meziantou.Framework.VectorGraphics.SceneFont) sets
the size, the weight and the style. The family is the one of the scene.

## Paths

A [PathBuilder](cref:Meziantou.Framework.VectorGraphics.PathBuilder) adds the commands of SVG one by one, and common
shapes in one call: [AddRectangle](cref:Meziantou.Framework.VectorGraphics.PathBuilder.AddRectangle),
[AddRoundedRectangle](cref:Meziantou.Framework.VectorGraphics.PathBuilder.AddRoundedRectangle),
[AddCircle](cref:Meziantou.Framework.VectorGraphics.PathBuilder.AddCircle),
[AddEllipse](cref:Meziantou.Framework.VectorGraphics.PathBuilder.AddEllipse) and
[AddPolygon](cref:Meziantou.Framework.VectorGraphics.PathBuilder.AddPolygon). Each shape is a closed contour, so one
path can hold several of them.
[PathData.Translate](cref:Meziantou.Framework.VectorGraphics.PathData.Translate) moves a path, and
[Append](cref:Meziantou.Framework.VectorGraphics.PathBuilder.Append) copies the commands of a path into another one:

<!-- ref:../../tests/Meziantou.Framework.VectorGraphics.Tests/SvgSceneRendererTests.cs;region=VectorGraphics_PathShapes;format=md-fenced-code-block;dedent=true -->
```csharp
// A triangle, an ellipse and a rounded rectangle in a single path
var path = new PathBuilder()
    .AddPolygon((0, 0), (10, 0), (5, 8))
    .AddEllipse(centerX: 30, centerY: 5, radiusX: 10, radiusY: 5)
    .AddRoundedRectangle(x: 50, y: 0, width: 20, height: 10, radius: 2)
    .Build();

// Paths can be moved, and combined into a new path
var moved = path.Translate(offsetX: 0, offsetY: 20);
var both = new PathBuilder().Append(path).Append(moved).Build();
Assert.HasCount(2 * path.Commands.Count, both.Commands);
```
<!-- endref -->

## Images, clipping and radial gradients

A [SceneImage](cref:Meziantou.Framework.VectorGraphics.SceneImage) draws a PNG, JPEG, GIF or WebP image, stretched to
its rectangle: the SVG embeds its bytes. [SceneGroup.ClipPath](cref:Meziantou.Framework.VectorGraphics.SceneGroup.ClipPath)
clips a group to any path, in the coordinates of its parent, and
[RadialGradientPaint](cref:Meziantou.Framework.VectorGraphics.RadialGradientPaint) paints a gradient from a focal
point to a circle:

<!-- ref:../../tests/Meziantou.Framework.VectorGraphics.Tests/SvgSceneRendererTests.cs;region=VectorGraphics_ImagesAndClipping;format=md-fenced-code-block;dedent=true -->
```csharp
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");
var scene = new Scene(width: 100, height: 100, fontFamily: "sans-serif");

// A radial gradient, from white at the center to blue on the circle
scene.Root.Add(new SceneRectangle(0, 0, 100, 100) { Fill = new RadialGradientPaint(50, 50, 50, [new(0, SceneColor.White), new(1, (SceneColor)"#0369a1")]) });

// An image, stretched to its rectangle and clipped to a circle
var avatar = scene.Root.Add(new SceneGroup { ClipPath = new PathBuilder().AddCircle(50, 50, 30).Build() });
avatar.Add(new SceneImage(20, 20, 60, 60, png));

var svg = SvgSceneRenderer.Render(scene);
```
<!-- endref -->

The image and PDF renderers decode the images: an image they cannot decode is not drawn.

## Groups

A [SceneGroup](cref:Meziantou.Framework.VectorGraphics.SceneGroup) applies a transform, a clip rectangle and an opacity
to its children. Its [Hyperlink](cref:Meziantou.Framework.VectorGraphics.SceneGroup.Hyperlink) makes the group a link in
the SVG; the address is written as is, so only set addresses you trust. Every element has a
[CssClass](cref:Meziantou.Framework.VectorGraphics.SceneElement.CssClass), a
[Tooltip](cref:Meziantou.Framework.VectorGraphics.SceneElement.Tooltip) and `data-*` attributes
([SetDataAttribute](cref:Meziantou.Framework.VectorGraphics.SceneElement.SetDataAttribute)) for the scripts of a web
page.

[SceneTransform](cref:Meziantou.Framework.VectorGraphics.SceneTransform) creates translations, rotations and scales;
[Then](cref:Meziantou.Framework.VectorGraphics.SceneTransform.Then) combines two transforms, in the order they apply:

<!-- ref:../../tests/Meziantou.Framework.VectorGraphics.Tests/SvgSceneRendererTests.cs;region=VectorGraphics_Transforms;format=md-fenced-code-block;dedent=true -->
```csharp
// Scale by 2 around the origin, then move by (10, 20)
var transform = SceneTransform.Scale(2, 2).Then(SceneTransform.Translate(10, 20));
Assert.Equal((16.0, 28.0), transform.Apply(3, 4));
Assert.Equal(new SceneTransform(2, 0, 0, 2, 10, 20), transform);
```
<!-- endref -->

## SVG options

[SvgRenderOptions](cref:Meziantou.Framework.VectorGraphics.SvgRenderOptions) sets the number of decimals, whether the
`xmlns` attribute is written (turn it off for SVG inlined in HTML), and the prefix of the identifiers of the gradients
and clip paths: give each drawing of an HTML page its own prefix, so the identifiers do not collide.
