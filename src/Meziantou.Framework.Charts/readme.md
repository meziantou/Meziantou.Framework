# Meziantou.Framework.Charts

Creates charts and writes them as SVG, with a grammar of graphics: a plot is made of marks (bars, lines, dots, arcs,
rectangles...), whose channels are mapped by scales and placed by Cartesian or polar coordinates. Bar, line, area,
scatter, bubble, pie, donut, radar, gauge, packed circles, treemap, sunburst, icicle, histogram, box plot, heatmap,
waterfall, funnel and candlestick charts are combinations of these pieces. The output is deterministic.

## Usage

The types are in the `Meziantou.Framework.Charts` namespace.

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/Samples/Sale.cs;region=Charts_SaleRecord;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Sale(string Region, int Year, double Amount);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_BarChart;format=md-fenced-code-block;dedent=true -->
```csharp
Sale[] sales =
[
    new("North", 2024, 150),
    new("South", 2024, 95),
    new("East", 2024, 105),
    new("West", 2024, 160),
];

var plot = new Plot { Title = "Sales by region", Subtitle = "2024, in thousands of euros" };
plot.Add(new BarMark<Sale>(sales, x: sale => sale.Region, y: sale => sale.Amount));
var svg = plot.ToSvg();
```
<!-- endref -->

The scales, axes, margins and legend are inferred from the marks. Stack bars with a transform:

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_StackedBarChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Sales by region, stacked by year" };
plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
{
    Fill = sale => sale.Year,
    Transform = new StackTransform(),
});
```
<!-- endref -->

A pie is an arc mark, in the polar coordinates the plot uses by default for it:

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/RadialChartTests.cs;region=Charts_PieChart;format=md-fenced-code-block;dedent=true -->
```csharp
Share[] shares = [new("Chrome", 64), new("Safari", 19), new("Edge", 5), new("Firefox", 3), new("Other", 9)];
var plot = new Plot { Title = "Browser market share", Width = 400, Height = 400 };
plot.Add(new ArcMark<Share>(shares, value: share => share.Value)
{
    Fill = share => share.Name,
    Label = share => share.Value.ToString(CultureInfo.InvariantCulture) + "%",
});
```
<!-- endref -->

## Text measurement

The layout measures the texts with the metrics of Helvetica (`AfmTextMeasurer`), embedded in
`Meziantou.Framework.Fonts`: no font file is needed, and the result is the same on every machine. `FontFaceTextMeasurer`
measures with a font file instead, for other scripts or fonts.

## Images

`Meziantou.Framework.Imaging.Charts` draws the same charts as PNG, WebP or JPEG images, or on an image canvas. Both
renderers draw the `Scene` that `Plot.CreateScene` lays out.
