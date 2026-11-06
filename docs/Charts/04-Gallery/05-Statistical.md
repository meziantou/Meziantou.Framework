---
title: Statistical charts
description: Histograms, box plots, heatmaps, waterfalls, funnels, candlesticks and sparklines.
---

## Histogram

A [BinTransform](cref:Meziantou.Framework.Charts.BinTransform) counts the values in bins with round edges; a [RectMark\<T\>](cref:Meziantou.Framework.Charts.RectMark{T}) draws them. The number of bins follows Sturges' rule by default; Scott's rule, the Freedman–Diaconis rule, a count or explicit edges are options.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/StatisticalChartTests.cs;region=Charts_Histogram;format=md-fenced-code-block;dedent=true -->
```csharp
var values = SampleData.Normal(500, mean: 170, deviation: 8, seed: 42);
var plot = new Plot { Title = "Height of 500 people" };
plot.Add(new RectMark<double>(values) { X = height => height, Transform = new BinTransform() });
plot.XAxis.Label = "Height (cm)";
plot.YAxis.Label = "People";
```
<!-- endref -->

![A histogram of heights, peaking around 170 cm](../images/Histogram.svg)

## Box plot

A [BoxMark\<T\>](cref:Meziantou.Framework.Charts.BoxMark{T}) draws the quartiles, the median, Tukey's whiskers (1.5 times the interquartile range) and the outliers of each category. [BoxStatistics](cref:Meziantou.Framework.Charts.BoxStatistics) computes them.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/StatisticalChartTests.cs;region=Charts_BoxPlot;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Distribution by group" };
plot.Add(new BoxMark<Measurement>(SampleData.Measurements, x: measurement => measurement.Group, y: measurement => measurement.Value)
{
    Fill = measurement => measurement.Group,
});
plot.Legend.Visible = false;
```
<!-- endref -->

![Three box plots with whiskers and outliers](../images/BoxPlot.svg)

## Heatmap

A rect mark on two band scales, with a numeric fill: the color scale is sequential, and the legend is a ramp.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/StatisticalChartTests.cs;region=Charts_Heatmap;format=md-fenced-code-block;dedent=true -->
```csharp
string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
var cells = days.SelectMany((day, d) => Enumerable.Range(8, 12).Select(hour => new HeatCell(
    day,
    hour.ToString("00", CultureInfo.InvariantCulture) + "h",
    Math.Round((d < 5 ? 40 : 15) + (30 * Math.Sin((hour - 8) * Math.PI / 11)), MidpointRounding.AwayFromZero))));

var plot = new Plot { Title = "Visits by hour", Height = 320 };
plot.Add(new RectMark<HeatCell>(cells)
{
    X = cell => cell.Hour,
    Y = cell => cell.Day,
    Fill = cell => cell.Value,
    Text = cell => cell.Value.ToString(CultureInfo.InvariantCulture),
});
plot.ColorScale = new SequentialColorScale { Scheme = ColorScheme.Blues };
plot.Legend.Title = "Visits";
```
<!-- endref -->

![A grid of visits by day and hour, darker when higher](../images/Heatmap.svg)

## Waterfall

A [WaterfallMark\<T\>](cref:Meziantou.Framework.Charts.WaterfallMark{T}) draws the running total: increases in the positive color of the theme, decreases in its negative color, totals in its neutral color.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/StatisticalChartTests.cs;region=Charts_Waterfall;format=md-fenced-code-block;dedent=true -->
```csharp
Step[] steps =
[
    new("Revenue", 420), new("Cost of sales", -180), new("Gross profit", 0, IsTotal: true),
    new("Marketing", -60), new("Salaries", -90), new("Other income", 25), new("Net profit", 0, IsTotal: true),
];

var plot = new Plot { Title = "From revenue to net profit" };
plot.Add(new WaterfallMark<Step>(steps, x: step => step.Label, y: step => step.Change) { IsTotal = step => step.IsTotal });
```
<!-- endref -->

![Bars going up and down from revenue to net profit](../images/Waterfall.svg)

## Funnel

A [FunnelMark\<T\>](cref:Meziantou.Framework.Charts.FunnelMark{T}) draws a centered bar per stage, joined by trapezoids.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/StatisticalChartTests.cs;region=Charts_Funnel;format=md-fenced-code-block;dedent=true -->
```csharp
Share[] stages = [new("Visits", 12000), new("Sign-ups", 4200), new("Trials", 1800), new("Purchases", 650)];
var plot = new Plot { Title = "Conversion funnel", Height = 300 };
plot.Add(new FunnelMark<Share>(stages, stage: stage => stage.Name, value: stage => stage.Value));
```
<!-- endref -->

![Four centered bars narrowing from visits to purchases](../images/Funnel.svg)

## Candlestick

A [CandlestickMark\<T\>](cref:Meziantou.Framework.Charts.CandlestickMark{T}) draws the open, high, low and close prices of each row.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/StatisticalChartTests.cs;region=Charts_Candlestick;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Daily prices" };
plot.Add(new CandlestickMark<Candle>(
    SampleData.Candles,
    x: candle => candle.Date,
    open: candle => candle.Open,
    high: candle => candle.High,
    low: candle => candle.Low,
    close: candle => candle.Close));
```
<!-- endref -->

![Daily candlesticks in green and red](../images/Candlestick.svg)

## Sparkline

A small plot without axes and with fixed margins.

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/StatisticalChartTests.cs;region=Charts_Sparkline;format=md-fenced-code-block;dedent=true -->
```csharp
double[] values = [3, 5, 4, 6, 8, 7, 9, 12, 10, 11, 14, 13];
var plot = new Plot { Width = 120, Height = 32, Margin = new ChartMargin(4), Theme = ChartTheme.Light with { Background = null } };
plot.Add(new LineMark<int>(Enumerable.Range(0, values.Length), x: i => i, y: i => values[i]) { Curve = Curve.MonotoneX });
plot.Add(new DotMark<int>([values.Length - 1], x: i => i, y: i => values[i]) { Radius = 2, FillColor = "#e5484d" });
plot.XAxis.Visible = false;
plot.YAxis.Visible = false;
```
<!-- endref -->

![A small line with a red dot at its end](../images/Sparkline.svg)

