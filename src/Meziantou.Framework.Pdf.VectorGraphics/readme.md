# Meziantou.Framework.Pdf.VectorGraphics

Draws the scenes of [Meziantou.Framework.VectorGraphics](https://www.nuget.org/packages/Meziantou.Framework.VectorGraphics)
(charts, diagrams, your own drawings) as PDF with [Meziantou.Framework.Pdf](https://www.nuget.org/packages/Meziantou.Framework.Pdf):
vector shapes, gradients, clipping, transparency and links. Each text is drawn with the face of its family of the fonts
of the scene, and the PDF embeds the subsets of the faces it uses. Without fonts, the texts are drawn with Helvetica,
whose widths the default text measurer uses.

## Usage

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
