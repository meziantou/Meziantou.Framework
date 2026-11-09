---
title: Overview
description: The charts of Mermaid, how they are drawn, and the Mermaid keyword of each.
---

Some types of Mermaid diagrams are charts of data. The package reads them into a model of its own, which keeps what the
Mermaid text says so that [MermaidWriter](cref:Meziantou.Framework.Mermaid.MermaidWriter) writes it back, and draws
them with [Meziantou.Framework.Charts](../../Charts/01-Introduction.md):

| Chart | Type | Mermaid keyword |
| --- | --- | --- |
| [Pie chart](02-PieCharts.md) | [PieChart](cref:Meziantou.Framework.Mermaid.PieChart) | `pie` |
| [XY chart](03-XyCharts.md) | [XyChart](cref:Meziantou.Framework.Mermaid.XyChart) | `xychart`, `xychart-beta` |
| [Quadrant chart](04-QuadrantCharts.md) | [QuadrantChart](cref:Meziantou.Framework.Mermaid.QuadrantChart) | `quadrantChart` |
| [Sankey chart](05-SankeyCharts.md) | [SankeyChart](cref:Meziantou.Framework.Mermaid.SankeyChart) | `sankey`, `sankey-beta` |
| [Radar chart](06-RadarCharts.md) | [RadarChart](cref:Meziantou.Framework.Mermaid.RadarChart) | `radar-beta` |
| [Treemap](07-Treemaps.md) | [TreemapChart](cref:Meziantou.Framework.Mermaid.TreemapChart) | `treemap`, `treemap-beta` |

Every chart derives from [MermaidChart](cref:Meziantou.Framework.Mermaid.MermaidChart), which gives it a
[Title](cref:Meziantou.Framework.Mermaid.MermaidChart.Title), an accessible
[Description](cref:Meziantou.Framework.Mermaid.MermaidChart.Description), the
[Theme](cref:Meziantou.Framework.Mermaid.MermaidChart.Theme) of Mermaid whose colors it uses, and the SVG output.

## Customize the drawing

[ToPlot](cref:Meziantou.Framework.Mermaid.MermaidChart.ToPlot) returns the [Plot](cref:Meziantou.Framework.Charts.Plot)
that draws the chart. Change it to go beyond what Mermaid can say, then draw it as any other plot:

<!-- ref:../../../tests/Meziantou.Framework.Mermaid.Tests/PieChartTests.cs;region=Mermaid_ToPlot;format=md-fenced-code-block;dedent=true -->
```csharp
var chart = (PieChart)MermaidDocument.Parse("""
    pie title Pets adopted by volunteers
        "Dogs" : 386
        "Cats" : 85
        "Rats" : 15
    """).Content;

var plot = chart.ToPlot();
plot.Subtitle = "Source: the shelter, 2025";
plot.Width = 600;
var svg = plot.ToSvg();
```
<!-- endref -->

The charts look like the other charts of Meziantou.Framework.Charts, in the colors of the Mermaid theme: they are not
drawn like Mermaid's own output.
