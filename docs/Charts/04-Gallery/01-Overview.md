---
title: Overview
description: The chart types, grouped by family, and the data the examples use.
---

Each chart type is a combination of marks, scales, coordinates and transforms (see [Concepts](../03-Concepts.md)). The
pages of the gallery show an example of each:

- [Cartesian charts](02-Cartesian.md): bars, lines, areas, scatter plots, bubbles, reference lines, lollipops, dumbbells,
  combo charts, two y axes, population pyramids, slope charts, error bars and Gantt charts.
- [Radial charts](03-Radial.md): pies, donuts, polar areas, radial bars, radar charts and gauges.
- [Hierarchical charts](04-Hierarchical.md): packed circles, treemaps, sunbursts, icicles, tree diagrams and dendrograms.
- [Statistical charts](05-Statistical.md): histograms, box plots, density plots, violin plots, trend lines, moving
  averages, heatmaps, waterfalls, funnels, candlesticks, bullet charts, calendar heatmaps, Marimekko charts and sparklines.
- [Small multiples](06-SmallMultiples.md): grids of charts that share their scales, one per facet.
- [Flows](07-Flows.md): Sankey diagrams.

The examples chart records like these:

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/Samples/Sale.cs;region=Charts_SaleRecord;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Sale(string Region, int Year, double Amount);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/Samples/Reading.cs;region=Charts_ReadingRecord;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Reading(DateOnly Date, string City, double Temperature);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Charts.Tests/Samples/Share.cs;region=Charts_ShareRecord;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Share(string Name, double Value);
```
<!-- endref -->

When an example uses `SampleData`, it is a list of such rows: `SampleData.Sales` has the sales of four regions over
three years, and `SampleData.Readings` the monthly temperatures of three cities in 2024.
