---
title: Introduction
description: What the Mermaid library reads and writes, and how it relates to the diagram library.
---

`Meziantou.Framework.Mermaid` reads the text format of [Mermaid](https://mermaid.js.org) 12 and writes it back. A
Mermaid text becomes an object you can read, change and draw as SVG or as an image, without a browser or JavaScript.

## What you can do with it

- **Read Mermaid text**, with its front matter, its `%%{init}%%` directives, its comments, its themes and its
  accessibility statements, and draw it. See [getting started](02-GettingStarted.md).
- **Report the problems** of a text written by a user: the parser gives the line and the column of an error, and lists
  the valid features it cannot draw as warnings. See [reading and writing](03-ReadingAndWriting.md).
- **Write Mermaid text** from a diagram built in C#, for instance to embed it in a Markdown page of GitHub. Writing
  what was read gives the same text.

## How it relates to the other libraries

The diagrams are the models of [Meziantou.Framework.Diagrams](../Diagrams/01-Introduction.md): a flowchart read from
Mermaid is a [FlowchartDiagram](cref:Meziantou.Framework.Diagrams.FlowchartDiagram) that you can also build in C#.
See the [diagram types](../Diagrams/04-DiagramTypes/01-Overview.md) for what each type supports.

The charts of Mermaid (pie, XY, quadrant, sankey and radar charts, treemaps) are models of this package, drawn with
[Meziantou.Framework.Charts](../Charts/01-Introduction.md): a pie chart read from Mermaid is a
[PieChart](cref:Meziantou.Framework.Mermaid.PieChart), and its [ToPlot](cref:Meziantou.Framework.Mermaid.MermaidChart.ToPlot)
method returns the plot that draws it. See the [charts](04-Charts/01-Overview.md).

## Namespaces

The reader and the writer are in the [Meziantou.Framework.Mermaid](cref:Meziantou.Framework.Mermaid) namespace, and
the diagrams in [Meziantou.Framework.Diagrams](cref:Meziantou.Framework.Diagrams).

## Next steps

- [Getting started](02-GettingStarted.md): install the package, read a diagram and draw it.
- [Reading and writing](03-ReadingAndWriting.md): the shared syntax, the errors, the warnings and the writer.
- [Charts](04-Charts/01-Overview.md): the charts of Mermaid, drawn with Meziantou.Framework.Charts.
