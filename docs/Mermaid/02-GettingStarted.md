---
title: Getting started
description: Install the package, read a Mermaid diagram, draw it as SVG and write it back as Mermaid.
---

## Install the package

Add the `Meziantou.Framework.Mermaid` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Mermaid" />

The types are in the [Meziantou.Framework.Mermaid](cref:Meziantou.Framework.Mermaid) namespace.

## Read and draw a diagram

[MermaidDocument.Parse](cref:Meziantou.Framework.Mermaid.MermaidDocument.Parse) reads a text and returns its
[Content](cref:Meziantou.Framework.Mermaid.MermaidDocument.Content). Cast it to the type of the diagram, such as
[FlowchartDiagram](cref:Meziantou.Framework.Diagrams.FlowchartDiagram), to read or change it, and write it as SVG:

<!-- ref:../../tests/Meziantou.Framework.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    ---
    title: Order processing
    config:
      theme: forest
    ---
    flowchart LR
        start([Order received]) --> check{Payment valid?}
        check -->|yes| ship[Ship the order]
        check -->|no| cancel[Cancel the order]
    """;

var document = MermaidDocument.Parse(text);
var flowchart = (FlowchartDiagram)document.Content;
var svg = flowchart.ToSvg();
```
<!-- endref -->

To draw it as PNG, WebP or JPEG, see [image rendering](../Diagrams/06-ImageRendering.md).

## Write Mermaid text

[MermaidWriter.Write](cref:Meziantou.Framework.Mermaid.MermaidWriter.Write) writes a diagram as Mermaid text, which
Mermaid and the parser read back:

<!-- ref:../../tests/Meziantou.Framework.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;

var text = MermaidWriter.Write(flowchart);
```
<!-- endref -->

See [reading and writing](03-ReadingAndWriting.md) for the errors, the warnings and what the writer cannot express.
