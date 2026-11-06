# Meziantou.Framework.Imaging.Charts

Draws the charts of [Meziantou.Framework.Charts](https://www.nuget.org/packages/Meziantou.Framework.Charts) with [Meziantou.Framework.Imaging](https://www.nuget.org/packages/Meziantou.Framework.Imaging): images in any format the library encodes (PNG, WebP, JPEG...), or vector drawing on a canvas.

## Usage

<!-- ref:../../tests/Meziantou.Framework.Imaging.Charts.Tests/ChartImageRendererTests.cs;region=ImagingCharts_ToImage;format=md-fenced-code-block;dedent=true -->
```csharp
Fruit[] fruits = [new("Apples", 12), new("Bananas", 7), new("Cherries", 18)];
var plot = new Plot { Title = "Fruits sold", Width = 400, Height = 250 };
plot.Add(new BarMark<Fruit>(fruits, x: fruit => fruit.Name, y: fruit => fruit.Count));

using var image = plot.ToImage();
Assert.Equal((400, 250), (image.Width, image.Height));

// Save to a file (image.Save("fruits.png")) or to a stream, in any format
using var png = new MemoryStream();
image.Save(png, new PngEncoder());

// Twice the pixels, for high-density screens
using var retina = plot.ToImage(new ChartImageOptions { Scale = 2 });
Assert.Equal((800, 500), (retina.Width, retina.Height));
```
<!-- endref -->

The plot is laid out with the fonts that draw it, so the margins and the labels fit the text. The package embeds
Liberation Sans (`ChartFonts`), whose widths are those of Helvetica and Arial: an image lays out like the SVG of the same
plot. Pass another font in `ChartImageOptions.Font` for other scripts.

## Draw on a canvas

<!-- ref:../../tests/Meziantou.Framework.Imaging.Charts.Tests/ChartImageRendererTests.cs;region=ImagingCharts_DrawChart;format=md-fenced-code-block;dedent=true -->
```csharp
using var report = new Image<Rgba32>(500, 300, Colors.White); // or Image.Load<Rgba32>("report.png")
var plot = new Plot { Width = 300, Height = 200, Theme = ChartTheme.Light with { Background = null } };
plot.Add(new ArcMark<Fruit>([new("Apples", 12), new("Bananas", 7)], value: fruit => fruit.Count) { Fill = fruit => fruit.Name });

var size = ChartImageRenderer.MeasureChart(plot);
Assert.Equal((300, 200), (size.Width, size.Height));

// Drawn as vector shapes and text: the transform and the clip of the canvas apply
report.Draw(canvas => canvas.DrawChart(plot, new PointD(180, 80)));
```
<!-- endref -->
