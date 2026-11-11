# Meziantou.Framework.VectorGraphics

Describes a drawing as a scene of shapes, paths, texts, images and groups, writes it as deterministic SVG, and measures, wraps
and truncates texts with the metrics of Helvetica or any OpenType font. [Meziantou.Framework.Charts](https://www.nuget.org/packages/Meziantou.Framework.Charts)
and Meziantou.Framework.Diagrams lay themselves out into a scene; [Meziantou.Framework.Imaging.VectorGraphics](https://www.nuget.org/packages/Meziantou.Framework.Imaging.VectorGraphics)
draws it as an image.

## Usage

<!-- ref:../../tests/Meziantou.Framework.VectorGraphics.Tests/SvgSceneRendererTests.cs;region=VectorGraphics_CreateScene;format=md-fenced-code-block;dedent=true -->
```csharp
var scene = new Scene(width: 200, height: 80, fontFamily: AfmTextMeasurer.Default.FontFamily) { Title = "A box", Background = SceneColor.White };

// Shapes are filled and stroked; texts are positioned by their baseline
scene.Root.Add(new SceneRectangle(10, 10, 180, 60) { Fill = (SceneColor)"#e0f2fe", Stroke = (SceneColor)"#0369a1", StrokeWidth = 2, CornerRadius = 8 });
scene.Root.Add(new SceneText(100, 45, "Hello", new SceneFont(16, Weight: SceneFont.BoldWeight), (SceneColor)"#0c4a6e") { Anchor = SceneTextAnchor.Middle });

// Paths use the commands of SVG
var arrow = new PathBuilder().MoveTo(20, 60).LineTo(60, 60).LineTo(55, 55).MoveTo(60, 60).LineTo(55, 65).Build();
scene.Root.Add(new ScenePath(arrow) { Stroke = (SceneColor)"#0369a1" });

var svg = SvgSceneRenderer.Render(scene);
```
<!-- endref -->

Wrap a text to fit a width:

<!-- ref:../../tests/Meziantou.Framework.VectorGraphics.Tests/TextMeasurerTests.cs;region=VectorGraphics_Wrap;format=md-fenced-code-block;dedent=true -->
```csharp
var measurer = AfmTextMeasurer.Default;
var font = new SceneFont(12);

// At most 100 pixels per line, broken between words, and at the line feeds of the text
var lines = TextFitting.Wrap(measurer, "A label that is too long for its box\nSecond paragraph", font, maxWidth: 100);
Assert.Equal(["A label that is too", "long for its box", "Second paragraph"], lines);

// Or shortened with an ellipsis
Assert.Equal("A label that i…", TextFitting.Truncate(measurer, "A label that is too long", font, maxWidth: 80));
```
<!-- endref -->
