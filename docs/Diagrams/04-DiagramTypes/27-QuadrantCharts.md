---
title: Quadrant charts
description: Place points on two axes over four quadrants, as for a priority or a risk matrix.
---

A [QuadrantChartDiagram](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram) draws
[points](cref:Meziantou.Framework.Diagrams.QuadrantChartPoint) on two axes that go from 0 to 1, over a square split into
four quadrants (Mermaid `quadrantChart`):

<!-- ref:../../../tests/Meziantou.Framework.Diagrams.Tests/QuadrantChartDiagramTests.cs;region=Diagrams_QuadrantChartDiagram;format=md-fenced-code-block;dedent=true -->
```csharp
var diagram = new QuadrantChartDiagram
{
    Title = "Reach and engagement of campaigns",
    XAxisLeftLabel = "Low Reach",
    XAxisRightLabel = "High Reach",
    YAxisBottomLabel = "Low Engagement",
    YAxisTopLabel = "High Engagement",
    Quadrant1Label = "We should expand",
    Quadrant2Label = "Need to promote",
    Quadrant3Label = "Re-evaluate",
    Quadrant4Label = "May be improved",
};
diagram.AddPoint("Campaign A", 0.3, 0.6);
diagram.AddPoint("Campaign B", 0.45, 0.23);
diagram.AddPoint("Campaign C", 0.57, 0.69).Style.Color = SceneColor.Parse("#ff3300");
diagram.AddPoint("Campaign D", 0.78, 0.34).ClassName = "important";
diagram.ClassDefinitions["important"] = new QuadrantChartPointStyle { Radius = 10, Color = SceneColor.Parse("#109060") };
```
<!-- endref -->

![A quadrant chart of the reach and the engagement of four campaigns](../images/QuadrantChartDiagram.svg)

## Quadrants and axes

The quadrants are numbered as in mathematics:
[Quadrant1Label](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.Quadrant1Label) is the text of the top right
quadrant, then the top left, the bottom left and the bottom right ones. Without points, the texts of the quadrants are
in their middle; with points, at their top, so they do not hide the points.

Each axis has two texts: [XAxisLeftLabel](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.XAxisLeftLabel) and
[XAxisRightLabel](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.XAxisRightLabel), drawn in the middle of their
half of the axis, or only the first one, drawn at its start. The texts of the y axis are turned to read from the bottom.
[XAxisPosition](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.XAxisPosition) puts the x axis at the top, the
default, or at the bottom; as in Mermaid, it is always at the bottom when the chart has points.
[YAxisPosition](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.YAxisPosition) puts the y axis on the left or on
the right.

[Width](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.Width) and
[Height](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.Height) are the size of the chart with the texts of the
axes, 500 pixels by default; the [Title](cref:Meziantou.Framework.Diagrams.Diagram.Title) is drawn above it. The
paddings and the font sizes of Mermaid are properties too, such as
[QuadrantLabelFontSize](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.QuadrantLabelFontSize) and
[PointRadius](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.PointRadius).

## Points and styles

A point has a text drawn below it, and a [Style](cref:Meziantou.Framework.Diagrams.QuadrantChartPoint.Style): its
radius, its fill color, and the color and the width of its border. A point can also take the style of a class: its
[ClassName](cref:Meziantou.Framework.Diagrams.QuadrantChartPoint.ClassName) is a key of
[ClassDefinitions](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.ClassDefinitions). The values of the point
take precedence over the ones of its class, which take precedence over the default. The first points are drawn above
the next ones, as in Mermaid.

The quadrants are filled with the primary color of the [theme](../05-Theming.md), a little lighter from the first
quadrant to the fourth one, and the points are black, as Mermaid draws them (the text color on a dark theme).
[Colors](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram.Colors) replaces any of these colors, as the `quadrant*`
theme variables of Mermaid do.

## Mermaid syntax

<!-- ref:../../../tests/Meziantou.Framework.Mermaid.Tests/QuadrantChartDiagramParserTests.cs;region=Mermaid_QuadrantChartDiagram;format=md-fenced-code-block;dedent=true -->
```csharp
var diagram = (QuadrantChartDiagram)MermaidDocument.Parse("""
    quadrantChart
        title Reach and engagement of campaigns
        x-axis Low Reach --> High Reach
        y-axis Low Engagement --> High Engagement
        quadrant-1 We should expand
        quadrant-2 Need to promote
        quadrant-3 Re-evaluate
        quadrant-4 May be improved
        Campaign A: [0.3, 0.6]
        Campaign B:::important: [0.45, 0.23] radius: 12, color: #ff3300
        classDef important stroke-color: #310085, stroke-width: 2px
    """).Diagram;
```
<!-- endref -->

The parser follows the grammar of Mermaid, and accepts and rejects the same texts. A statement ends at a line feed or a
semicolon, and the keywords ignore the case. `x-axis left --> right` sets the texts of the x axis, `x-axis left -->`
adds an arrow to the left one; `y-axis bottom --> top` the texts of the y axis. A point is
`text:::class: [x, y] styles`, where the class is optional, a coordinate is `0`, `1`, or `0.` followed by digits, and
the styles are `radius: 10`, `color: #ff3300`, `stroke-color: #10f0f0` and `stroke-width: 5px`, separated by commas.
`classDef name styles` defines a class.

A text is made of words, spaces, dashes and most punctuation, but no colon. A text between double quotes can contain any
character but a double quote, and `` "`text`" `` is a markdown text. As in Mermaid, `title`, `x-axis` and the other
keywords start their statement wherever a word starts, and `%%` starts a comment, so such a text needs quotes. The
parser reads the `quadrantChart` section of the configuration (`chartWidth`, `chartHeight`, `pointRadius`,
`xAxisPosition`...), and the `quadrant*` theme variables.

The writer quotes the texts that need it. It throws for a text with a double quote, a markdown text with a backquote, a
class name with other characters than letters, digits and underscores, or a color that is not opaque.
