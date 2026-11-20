---
title: Getting started
description: Install the package, read a DOT graph, draw it as SVG, handle the errors and the warnings, and write DOT.
---

## Install the package

Add the `Meziantou.Framework.Graphviz` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Graphviz" />

The types are in the [Meziantou.Framework.Graphviz](cref:Meziantou.Framework.Graphviz) namespace.

## Read a graph

[GraphvizDocument.Parse](cref:Meziantou.Framework.Graphviz.GraphvizDocument.Parse) reads a DOT text. Its
[Diagram](cref:Meziantou.Framework.Graphviz.GraphvizDocument.Diagram) is a
[FlowchartDiagram](cref:Meziantou.Framework.Diagrams.FlowchartDiagram) that you can change and draw:

<!-- ref:../../tests/Meziantou.Framework.Graphviz.Tests/GraphvizDocumentTests.cs;region=Graphviz_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    digraph Orders {
        rankdir=LR;
        node [shape=box, style=rounded];
        start [label="Order received", shape=ellipse];
        check [label="Payment valid?", shape=diamond];
        subgraph cluster_fulfillment {
            label="Fulfillment";
            ship [label="Ship the order"];
            notify [label="Notify the customer"];
        }
        cancel [label="Cancel the order", color=red];
        start -> check;
        check -> ship [label="yes"];
        ship -> notify;
        check -> cancel [label="no", style=dashed];
    }
    """;

var document = GraphvizDocument.Parse(text);
var flowchart = document.Diagram;
var svg = flowchart.ToSvg();
```
<!-- endref -->

The clusters (the subgraphs whose name starts with `cluster`) become the
[Subgraphs](cref:Meziantou.Framework.Diagrams.FlowchartDiagram.Subgraphs) of the flowchart. The document also gives the
[Name](cref:Meziantou.Framework.Graphviz.GraphvizDocument.Name) of the graph, and whether it is
[directed](cref:Meziantou.Framework.Graphviz.GraphvizDocument.IsDirected) and
[strict](cref:Meziantou.Framework.Graphviz.GraphvizDocument.IsStrict). Only the first graph of a text is read.

[ToSvg](cref:Meziantou.Framework.Diagrams.Diagram.ToSvg) draws the flowchart as SVG; see
[image rendering](../Diagrams/06-ImageRendering.md) to draw it as PNG, WebP or JPEG, and [themes](../Diagrams/05-Theming.md)
for its colors. [GraphvizParseOptions.DefaultTheme](cref:Meziantou.Framework.Graphviz.GraphvizParseOptions.DefaultTheme)
sets the theme of the diagrams a parser reads.

## Errors

A text that is not valid DOT throws a [GraphvizParseException](cref:Meziantou.Framework.Graphviz.GraphvizParseException),
whose [Error](cref:Meziantou.Framework.Graphviz.GraphvizParseException.Error) gives the line and the column of the
problem. [TryParse](cref:Meziantou.Framework.Graphviz.GraphvizDocument.TryParse) returns it instead:

<!-- ref:../../tests/Meziantou.Framework.Graphviz.Tests/GraphvizDocumentTests.cs;region=Graphviz_Errors;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    digraph {
        a -> b
        b -- c
    }
    """;

// TryParse returns the position of the first error instead of throwing a GraphvizParseException
Assert.False(GraphvizDocument.TryParse(text, options: null, out _, out var error));
Assert.Equal((3, 7), (error.Line, error.Column));
```
<!-- endref -->

[GraphvizParseOptions](cref:Meziantou.Framework.Graphviz.GraphvizParseOptions) limits the size of the text, the number
of nodes and edges, and the depth of the nesting of the subgraphs. The edges between groups of nodes multiply:
`{a b c} -> {d e f}` is nine edges, and each counts. Keep the limits when the text comes from users.

## Warnings

The parts of DOT the flowcharts cannot draw are approximated or ignored, and reported in
[Warnings](cref:Meziantou.Framework.Graphviz.GraphvizDocument.Warnings) with their position: an attribute that is not
supported, such as `fontname` or `width`, a port, an HTML-like label drawn as plain text, an arrowhead drawn as an
arrow. Each kind of warning is reported once, at its first occurrence.

## Write DOT

[GraphvizWriter.Write](cref:Meziantou.Framework.Graphviz.GraphvizWriter.Write) writes a flowchart as DOT:

<!-- ref:../../tests/Meziantou.Framework.Graphviz.Tests/GraphvizWriterTests.cs;region=Graphviz_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;

var dot = GraphvizWriter.Write(flowchart);
```
<!-- endref -->

The writer writes a `digraph`, and the subgraphs of the flowchart as clusters. A shape that Graphviz does not have is
written as the closest shape of Graphviz, the styles of the classes are written on each element, and an edge to a
subgraph is written as an edge to a node of the subgraph, clipped at its frame (`lhead`, `ltail`). Reading the text it
writes gives a flowchart that it writes the same way.
