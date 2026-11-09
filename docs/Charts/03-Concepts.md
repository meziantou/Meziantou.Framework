---
title: Concepts
description: Marks, channels, scales, coordinates, transforms, axes and legends, and how they combine into charts.
---

## Marks

A mark draws rows of data with shapes. Each mark is a layer of the [Plot](cref:Meziantou.Framework.Charts.Plot), and
the marks are drawn in the order they are added:

| Mark | Draws |
|---|---|
| [BarMark\<T\>](cref:Meziantou.Framework.Charts.BarMark{T}) | a bar per row, in the band of a category |
| [RectMark\<T\>](cref:Meziantou.Framework.Charts.RectMark{T}) | a rectangle per row: bands, intervals or bins |
| [LineMark\<T\>](cref:Meziantou.Framework.Charts.LineMark{T}) | a line per series |
| [AreaMark\<T\>](cref:Meziantou.Framework.Charts.AreaMark{T}) | the area between a line and a baseline, per series |
| [DotMark\<T\>](cref:Meziantou.Framework.Charts.DotMark{T}) | a dot per row: a circle or another symbol |
| [RuleXMark\<T\>](cref:Meziantou.Framework.Charts.RuleXMark{T}), [RuleYMark\<T\>](cref:Meziantou.Framework.Charts.RuleYMark{T}) | vertical and horizontal lines |
| [TickMark\<T\>](cref:Meziantou.Framework.Charts.TickMark{T}) | a short line per row, across a band |
| [TextMark\<T\>](cref:Meziantou.Framework.Charts.TextMark{T}) | a text per row |
| [AnnotationMark\<T\>](cref:Meziantou.Framework.Charts.AnnotationMark{T}) | a text per row, with an arrow pointing at a point |
| [ArcMark\<T\>](cref:Meziantou.Framework.Charts.ArcMark{T}) | a slice per row: pies, donuts, polar areas |
| [BoxMark\<T\>](cref:Meziantou.Framework.Charts.BoxMark{T}) | a box plot per category |
| [CandlestickMark\<T\>](cref:Meziantou.Framework.Charts.CandlestickMark{T}) | a candlestick per row |
| [FunnelMark\<T\>](cref:Meziantou.Framework.Charts.FunnelMark{T}) | a funnel, a bar per stage |
| [WaterfallMark\<T\>](cref:Meziantou.Framework.Charts.WaterfallMark{T}) | running totals as floating bars |
| [ViolinMark\<T\>](cref:Meziantou.Framework.Charts.ViolinMark{T}) | a violin per category: the mirrored density of its values |
| [RidgelineMark\<T\>](cref:Meziantou.Framework.Charts.RidgelineMark{T}) | a ridgeline plot: the density of the values of each category, as overlapping areas |
| [HexbinMark\<T\>](cref:Meziantou.Framework.Charts.HexbinMark{T}) | hexagons colored by the number of points in them |
| [ContourMark\<T\>](cref:Meziantou.Framework.Charts.ContourMark{T}) | the contours of the density of points |
| [ParallelCoordinatesMark\<T\>](cref:Meziantou.Framework.Charts.ParallelCoordinatesMark{T}) | parallel coordinates: an axis per dimension, each with its scale, and a line per row |
| [PackMark\<T\>](cref:Meziantou.Framework.Charts.PackMark{T}), [TreemapMark\<T\>](cref:Meziantou.Framework.Charts.TreemapMark{T}), [PartitionMark\<T\>](cref:Meziantou.Framework.Charts.PartitionMark{T}) | a tree, as packed circles, a treemap, or a sunburst and an icicle |
| [TreeMark\<T\>](cref:Meziantou.Framework.Charts.TreeMark{T}) | a tree as a node-link diagram: a tidy tree or a dendrogram |
| [SankeyMark\<T\>](cref:Meziantou.Framework.Charts.SankeyMark{T}) | the flows between nodes, a row per link |
| [ChordMark\<T\>](cref:Meziantou.Framework.Charts.ChordMark{T}) | the flows between nodes around a circle, a row per flow |
| [NetworkMark\<T\>](cref:Meziantou.Framework.Charts.NetworkMark{T}) | a network graph, a row per link, with a force-directed layout |
| [GeoMark\<T\>](cref:Meziantou.Framework.Charts.GeoMark{T}) | a geometry per row on a map: regions, lines and points, such as the features of a GeoJSON document |

[RuleXMark](cref:Meziantou.Framework.Charts.RuleXMark) and [RuleYMark](cref:Meziantou.Framework.Charts.RuleYMark) take
values directly: `new RuleYMark(0)` draws a line at zero.

## Channels

A channel is a function of a row: the x and y positions, the fill and stroke colors, the size, the label and the
tooltip. Its values are numbers (any numeric type), times (`DateTime`, `DateTimeOffset`, `DateOnly`, in UTC) or
categories (strings, and anything else, formatted with the invariant culture). `null` and `double.NaN` are missing
values: the row is not drawn, and a line breaks around it.

A constant color is a property: [FillColor](cref:Meziantou.Framework.Charts.Mark{T}.FillColor) and
[StrokeColor](cref:Meziantou.Framework.Charts.Mark{T}.StrokeColor) take a [SceneColor](cref:Meziantou.Framework.VectorGraphics.SceneColor),
which converts from `#rrggbb` or a CSS color name such as `"steelblue"`. A channel such as
[Fill](cref:Meziantou.Framework.Charts.Mark{T}.Fill) goes through the color scale instead.

## Scales

The scales map the values of the channels: the x and y scales to positions, the color scale to colors, the size scale
to radii. Unless the plot sets it, a scale is inferred from the values: a band scale for the categories of bars, a
point scale for other categories, a time scale for times, and a linear scale for numbers. The domain of a linear scale
is extended to round values, and includes zero when a mark has a baseline, such as bars and areas.

| Scale | Use |
|---|---|
| [LinearScale](cref:Meziantou.Framework.Charts.LinearScale) | numbers |
| [LogScale](cref:Meziantou.Framework.Charts.LogScale), [SymlogScale](cref:Meziantou.Framework.Charts.SymlogScale) | values that span several orders of magnitude; symlog accepts zero and negative values |
| [PowScale](cref:Meziantou.Framework.Charts.PowScale), [SqrtScale](cref:Meziantou.Framework.Charts.SqrtScale) | numbers, raised to a power |
| [TimeScale](cref:Meziantou.Framework.Charts.TimeScale) | times, with ticks on calendar boundaries |
| [BandScale](cref:Meziantou.Framework.Charts.BandScale), [PointScale](cref:Meziantou.Framework.Charts.PointScale) | categories, as bands or as points |
| [OrdinalColorScale](cref:Meziantou.Framework.Charts.OrdinalColorScale) | a color per category |
| [SequentialColorScale](cref:Meziantou.Framework.Charts.SequentialColorScale), [DivergingColorScale](cref:Meziantou.Framework.Charts.DivergingColorScale) | numbers, on a ramp of colors interpolated in OKLab |
| [SizeScale](cref:Meziantou.Framework.Charts.SizeScale) | numbers, as radii whose areas are proportional to the values |

A value that a scale cannot map is missing too, and gets no tick: zero and negative values on a log scale, and values
that the transform of the scale makes infinite, such as zero on a power scale with a negative exponent.

Set a scale to change its domain, its ticks or its format. For instance, a percentage axis that starts at zero:

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_NormalizedStackedBarChart;format=md-fenced-code-block;dedent=true -->
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

[NumberFormats](cref:Meziantou.Framework.Charts.NumberFormats) has common formats: SI prefixes (`1.5k`), a fixed
number of decimals, and percentages.

## Coordinates

The coordinates turn the positions of the x and y scales into points:

- [CartesianCoordinates](cref:Meziantou.Framework.Charts.CartesianCoordinates): x is horizontal and y is vertical. With
  [Transposed](cref:Meziantou.Framework.Charts.Coordinates.Transposed), x is vertical: bars become horizontal.
- [PolarCoordinates](cref:Meziantou.Framework.Charts.PolarCoordinates): x is the angle and y is the radius. Set the start
  and end angles for a gauge, and an inner radius for a donut. Transposed, y is the angle: bars become radial bars.

- [GeoCoordinates](cref:Meziantou.Framework.Charts.GeoCoordinates): x is the longitude and y is the latitude, in degrees,
  and a [GeoProjection](cref:Meziantou.Framework.Geo.GeoProjection) places them on a map, scaled to fit the plot area.
  The map has no axes; it can have a graticule and the outline of the globe.

Plots that have an [ArcMark\<T\>](cref:Meziantou.Framework.Charts.ArcMark{T}) use polar coordinates by default, and
plots that have a [GeoMark\<T\>](cref:Meziantou.Framework.Charts.GeoMark{T}) use geographic coordinates: see the
[maps](04-Gallery/09-Maps.md).

## Transforms

A transform derives the rows a mark draws from its data:

- [StackTransform](cref:Meziantou.Framework.Charts.StackTransform) stacks the rows that share an x value: stacked bars,
  stacked areas, normalized stacks and streamgraphs.
- [BinTransform](cref:Meziantou.Framework.Charts.BinTransform) counts the values in bins: histograms.
- [GroupTransform](cref:Meziantou.Framework.Charts.GroupTransform) combines the rows of a group with a sum, a mean, a
  count, a minimum, a maximum or a median.
- [CumulativeTransform](cref:Meziantou.Framework.Charts.CumulativeTransform) computes running totals.
- [WindowTransform](cref:Meziantou.Framework.Charts.WindowTransform) reduces a window of rows around each row: moving
  averages.
- [RegressionTransform](cref:Meziantou.Framework.Charts.RegressionTransform) fits a polynomial to each series: trend lines.
- [DensityTransform](cref:Meziantou.Framework.Charts.DensityTransform) estimates the density of the x values: density plots.

The series of a stack are given by the `Series` channel of the mark, else by its fill or stroke channel.

## Facets and the secondary axis

The `FacetX` and `FacetY` channels of the marks split a chart into a grid of small charts that share the scales: see
[Small multiples](04-Gallery/06-SmallMultiples.md). [Plot.Facets](cref:Meziantou.Framework.Charts.Plot.Facets) sets the
number of columns, the gap between the charts and the order of the facets.

A mark that sets [UseSecondaryYScale](cref:Meziantou.Framework.Charts.Mark.UseSecondaryYScale) maps its y channels with
the secondary y scale, whose axis is on the right in Cartesian coordinates: see the
[secondary axis](04-Gallery/02-Cartesian.md#secondary-axis) example.

## Axes, legends and margins

The x and y scales get an axis, and the color and size scales get a legend, unless the chart does not use them as
measures (pies, hierarchies). [Plot.XAxis](cref:Meziantou.Framework.Charts.Plot.XAxis) and
[Plot.YAxis](cref:Meziantou.Framework.Charts.Plot.YAxis) set the label, the grid, the rotation of the tick labels and
the visibility of an axis; [Plot.Legend](cref:Meziantou.Framework.Charts.Plot.Legend) sets the title and the position
of the legend, and [Plot.SizeLegend](cref:Meziantou.Framework.Charts.Plot.SizeLegend) the title and the values of the
legend of the sizes, which is drawn next to it. The margins are computed from the measured texts: when the tick labels of the horizontal axis overlap,
they are rotated by -45 degrees, and only some of them are drawn when they still overlap.
[Plot.Margin](cref:Meziantou.Framework.Charts.Plot.Margin) sets the margins instead.

## The scene

[CreateScene](cref:Meziantou.Framework.Charts.Plot.CreateScene) lays the plot out into a
[Scene](cref:Meziantou.Framework.VectorGraphics.Scene): rectangles, circles, lines, paths, texts and groups, in
pixels. The SVG renderer and the image renderer both draw this scene, so they share the layout. You can also read it to
draw the chart with your own renderer:

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/PlotTests.cs;region=Charts_CreateScene;format=md-fenced-code-block;dedent=true -->
```csharp
var plot = new Plot { Title = "Sales" };
plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount));

// The layout, as drawing primitives: rectangles, paths, lines, texts and groups, in pixels
var scene = plot.CreateScene();
var bars = scene.Root.Children.OfType<SceneGroup>().Single(group => group.CssClass == "marks");
Assert.HasCount(4, bars.Children.OfType<SceneGroup>().Single().Children.OfType<SceneRectangle>());
```
<!-- endref -->
