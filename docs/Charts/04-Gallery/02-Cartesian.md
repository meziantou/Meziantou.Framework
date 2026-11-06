---
title: Cartesian charts
description: Bar charts (grouped, stacked, normalized, horizontal), lines, steps, areas, streamgraphs, scatter plots, bubbles, reference lines, lollipops and dumbbells.
---

## Bar chart

A [BarMark\<T\>](cref:Meziantou.Framework.Charts.BarMark{T}) draws a bar per row, from zero to its value, in the band of its category.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_BarChart;format=md-fenced-code-block;dedent=true -->
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

![A bar chart of the sales of four regions](../images/BarChart.svg)

## Grouped bar chart

[Group](cref:Meziantou.Framework.Charts.BarMark{T}.Group) divides each band into side-by-side bars. A numeric channel that groups or stacks bars, such as a year, is used as a category.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_GroupedBarChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Sales by region and year" };
plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
{
    Group = sale => sale.Year,
    Fill = sale => sale.Year,
});
```
<!-- endref -->

![Bars of three years side by side for each region](../images/GroupedBarChart.svg)

## Stacked bar chart

A [StackTransform](cref:Meziantou.Framework.Charts.StackTransform) stacks the bars of a category. By default, positive values go up from zero and negative values go down.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_StackedBarChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Sales by region, stacked by year" };
plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
{
    Fill = sale => sale.Year,
    Transform = new StackTransform(),
});
```
<!-- endref -->

![The bars of three years stacked for each region](../images/StackedBarChart.svg)

## 100% stacked bar chart

[StackOffset.Expand](cref:Meziantou.Framework.Charts.StackOffset.Expand) normalizes each stack from 0 to 1.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_NormalizedStackedBarChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot
{
    Title = "Share of each year",
    YScale = new LinearScale { TickFormat = NumberFormats.Percent() },
};
plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
{
    Fill = sale => sale.Year,
    Transform = new StackTransform { Offset = StackOffset.Expand },
});
```
<!-- endref -->

![Stacked bars that each go from 0% to 100%](../images/NormalizedStackedBarChart.svg)

## Horizontal bar chart

Transposed coordinates draw the x scale vertically: the bars are horizontal. The categories read from the top down.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_HorizontalBarChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot
{
    Title = "Sales by region",
    Coordinates = new CartesianCoordinates { Transposed = true },
    Height = 260,
};
plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount)
{
    CornerRadius = 3,
});
plot.YAxis.Label = "Amount";
```
<!-- endref -->

![Horizontal bars, one per region](../images/HorizontalBarChart.svg)

## Line chart

A [LineMark\<T\>](cref:Meziantou.Framework.Charts.LineMark{T}) joins the rows in their order. Times use a time scale, whose labels show the largest unit each tick does not fall on: the year, then the months.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_LineChart;format=md-fenced-code-block;dedent=true -->
```csharp
var paris = SampleData.Readings.Where(reading => reading.City == "Paris");
var plot = new Plot { Title = "Monthly temperature in Paris" };
plot.Add(new LineMark<Reading>(paris, x: reading => reading.Date, y: reading => reading.Temperature) { MarkerRadius = 3 });
plot.YAxis.Label = "°C";
```
<!-- endref -->

![The monthly temperature of Paris as a line with dots](../images/LineChart.svg)

## Multi-series line chart

The [Stroke](cref:Meziantou.Framework.Charts.Mark{T}.Stroke) channel draws a line per value, in its color. [Curve.MonotoneX](cref:Meziantou.Framework.Charts.Curve.MonotoneX) smooths the lines without overshooting the data.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_MultiSeriesLineChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Monthly temperature" };
plot.Add(new RuleYMark(0));
plot.Add(new LineMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature)
{
    Stroke = reading => reading.City,
    Curve = Curve.MonotoneX,
});
```
<!-- endref -->

![Three smooth lines, one per city, and a line at zero](../images/MultiSeriesLineChart.svg)

## Step line

[Curve.Step](cref:Meziantou.Framework.Charts.Curve.Step), [Curve.StepBefore](cref:Meziantou.Framework.Charts.Curve.StepBefore) and [Curve.StepAfter](cref:Meziantou.Framework.Charts.Curve.StepAfter) join the points with horizontal steps.

![Three step lines](../images/StepLineChart.svg)

## Area chart

An [AreaMark\<T\>](cref:Meziantou.Framework.Charts.AreaMark{T}) fills the area between a line and zero, or another baseline (Y1).

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_AreaChart;format=md-fenced-code-block;dedent=true -->
```csharp
var oslo = SampleData.Readings.Where(reading => reading.City == "Madrid");
var plot = new Plot { Title = "Monthly temperature in Madrid" };
plot.Add(new AreaMark<Reading>(oslo, x: reading => reading.Date, y: reading => reading.Temperature)
{
    FillOpacity = 0.3,
    StrokeColor = "#4269d0",
    Curve = Curve.MonotoneX,
});
```
<!-- endref -->

![The monthly temperature of Madrid as a filled area](../images/AreaChart.svg)

## Stacked area chart

A stack transform stacks the areas of the series.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_StackedAreaChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Sales by year, stacked by region" };
plot.Add(new AreaMark<Sale>(SampleData.Sales, x: sale => sale.Year, y: sale => sale.Amount)
{
    Fill = sale => sale.Region,
    Transform = new StackTransform(),
});
plot.XScale = new LinearScale { Ticks = [2022, 2023, 2024], TickFormat = year => year.ToString("0", CultureInfo.InvariantCulture) };
```
<!-- endref -->

![The sales of four regions stacked as areas over three years](../images/StackedAreaChart.svg)

## Streamgraph

[StackOffset.Silhouette](cref:Meziantou.Framework.Charts.StackOffset.Silhouette) centers the stacks on zero, and [StackOrder.InsideOut](cref:Meziantou.Framework.Charts.StackOrder.InsideOut) puts the series that peak first in the middle.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_Streamgraph;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Streamgraph" };
plot.Add(new AreaMark<Reading>(SampleData.Readings, x: reading => reading.Date, y: reading => reading.Temperature + 15)
{
    Fill = reading => reading.City,
    Curve = Curve.MonotoneX,
    Transform = new StackTransform { Offset = StackOffset.Silhouette, Order = StackOrder.InsideOut },
});
plot.YAxis.Visible = false;
```
<!-- endref -->

![Three streams centered on a horizontal axis](../images/Streamgraph.svg)

## Scatter plot

A [DotMark\<T\>](cref:Meziantou.Framework.Charts.DotMark{T}) draws a dot per row. The fill channel colors the dots and adds a legend; the title channel adds a tooltip.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_ScatterPlot;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Fuel efficiency by weight" };
plot.Add(new DotMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg)
{
    Fill = car => car.Origin,
    Title = car => car.Name,
});
plot.XAxis.Label = "Weight (kg)";
plot.YAxis.Label = "Miles per gallon";
plot.XAxis.Grid = true;
```
<!-- endref -->

![Dots of cars by weight and fuel efficiency, colored by origin](../images/ScatterPlot.svg)

## Bubble chart

The [Size](cref:Meziantou.Framework.Charts.DotMark{T}.Size) channel goes through the size scale: the area of a dot is proportional to its value.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_BubbleChart;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Power, weight and efficiency" };
plot.Add(new DotMark<Car>(SampleData.Cars, x: car => car.Weight, y: car => car.Mpg)
{
    Size = car => car.Horsepower,
    Fill = car => car.Origin,
    FillOpacity = 0.7,
    StrokeColor = ChartColor.White,
});
plot.SizeScale = new SizeScale { MaxRadius = 20 };
```
<!-- endref -->

![Bubbles whose areas are proportional to the power of the cars](../images/BubbleChart.svg)

## Reference lines

[RuleYMark](cref:Meziantou.Framework.Charts.RuleYMark) draws horizontal lines and [RuleXMark\<T\>](cref:Meziantou.Framework.Charts.RuleXMark{T}) vertical lines, across the plot area.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_ReferenceLines;format=md-fenced-code-block;dedent=true -->
```csharp
var paris = SampleData.Readings.Where(reading => reading.City == "Paris");
var plot = new Plot { Title = "Months above 15 °C" };
plot.Add(new LineMark<Reading>(paris, x: reading => reading.Date, y: reading => reading.Temperature));
plot.Add(new RuleYMark(15) { StrokeColor = "#e5484d", StrokeDashArray = [4, 4] });
plot.Add(new RuleXMark<DateOnly>([new DateOnly(2024, 7, 1)], x: date => date) { StrokeColor = "#8c959f" });
```
<!-- endref -->

![A line chart with a dashed threshold and a vertical marker](../images/ReferenceLines.svg)

## Lollipop chart

A rule from zero to the value, then a dot at the value.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_LollipopChart;format=md-fenced-code-block;dedent=true -->
```csharp
var sales = SampleData.Sales.Where(sale => sale.Year == 2024).ToArray();
var plot = new Plot { Title = "Lollipop" };
plot.Add(new RuleXMark<Sale>(sales, x: sale => sale.Region) { Y1 = _ => 0, Y2 = sale => sale.Amount, StrokeColor = "#8c959f" });
plot.Add(new DotMark<Sale>(sales, x: sale => sale.Region, y: sale => sale.Amount) { Radius = 6 });
plot.YScale = new LinearScale { Zero = true };
```
<!-- endref -->

![Stems from zero ending with dots](../images/LollipopChart.svg)

## Dumbbell chart

A rule between two values and a dot at each end, in transposed coordinates.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_DumbbellChart;format=md-fenced-code-block;dedent=true -->
```csharp
var start = SampleData.Sales.Where(sale => sale.Year == 2022).ToDictionary(sale => sale.Region, sale => sale.Amount);
var end = SampleData.Sales.Where(sale => sale.Year == 2024).ToArray();
var plot = new Plot { Title = "From 2022 to 2024", Coordinates = new CartesianCoordinates { Transposed = true }, Height = 240 };
plot.Add(new RuleXMark<Sale>(end, x: sale => sale.Region) { Y1 = sale => start[sale.Region], Y2 = sale => sale.Amount, StrokeColor = "#8c959f", StrokeWidth = 2 });
plot.Add(new DotMark<Sale>(end, x: sale => sale.Region, y: sale => start[sale.Region]) { FillColor = "#9498a0", Radius = 5 });
plot.Add(new DotMark<Sale>(end, x: sale => sale.Region, y: sale => sale.Amount) { Radius = 5 });
```
<!-- endref -->

![Pairs of dots joined by a line for each region](../images/DumbbellChart.svg)

