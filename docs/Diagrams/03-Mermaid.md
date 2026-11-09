---
title: Mermaid
description: Read Mermaid text into diagrams, handle the errors and the warnings, and write diagrams as Mermaid.
---

`Meziantou.Framework.Diagrams.Mermaid` reads and writes the text format of [Mermaid](https://mermaid.js.org) 12.

<InstallNuGetPackage Id="Meziantou.Framework.Diagrams.Mermaid" />

The types are in the [Meziantou.Framework.Diagrams.Mermaid](cref:Meziantou.Framework.Diagrams.Mermaid) namespace.

## Read a diagram

[MermaidDocument.Parse](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidDocument.Parse) reads a text and returns its
[Diagram](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidDocument.Diagram); cast it to the type of the diagram, such
as [FlowchartDiagram](cref:Meziantou.Framework.Diagrams.FlowchartDiagram), to read or change it:

<!-- ref:../../tests/Meziantou.Framework.Diagrams.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Parse;format=md-fenced-code-block;dedent=true -->
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
var flowchart = (FlowchartDiagram)document.Diagram;
var svg = flowchart.ToSvg();
```
<!-- endref -->

Every type of diagram reads the parts of Mermaid they share:

- The YAML front matter between two `---` lines: its `title` is the title of the diagram, and its `config` the
  configuration.
- The `%%{init: ...}%%` directives, which also set the configuration.
- The `%%` comments, on their own line.
- The accessibility statements `accTitle:` and `accDescr:` (or `accDescr { ... }` on several lines), which become the
  title and the description of the SVG.

The configuration sets the theme (`theme`, `themeVariables`, see [themes](05-Theming.md)), `fontFamily`, `fontSize`,
and the options of the diagram, such as `flowchart.curve`, `flowchart.nodeSpacing`, `flowchart.rankSpacing`,
`flowchart.wrappingWidth` and `flowchart.diagramPadding`.

## Errors

A text that is not valid Mermaid throws a [MermaidParseException](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidParseException),
whose [Error](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidParseException.Error) gives the line and the column of the
problem. [TryParse](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidDocument.TryParse) returns it instead:

<!-- ref:../../tests/Meziantou.Framework.Diagrams.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Errors;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    flowchart TD
        A --> B
        B --> [oops]
    """;

// TryParse returns the position of the first error instead of throwing a MermaidParseException
Assert.False(MermaidDocument.TryParse(text, options: null, out _, out var error));
Assert.Equal((3, 11), (error.Line, error.Column));
```
<!-- endref -->

[MermaidParseOptions](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidParseOptions) limits the size of the text, the
number of nodes and edges and the depth of the nesting, as Mermaid does: keep the limits when the text comes from users.

## Warnings

Some valid Mermaid has no equivalent in the diagrams: the JavaScript callbacks of `click`, the hand-drawn look, the
icons, the animations of the edges. The parser ignores them and reports them in
[Warnings](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidDocument.Warnings), with their position. A link whose
address could run a script (`javascript:`) is ignored too: only `http`, `https`, `mailto` and relative addresses are kept.

## Write a diagram

[MermaidWriter.Write](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidWriter.Write) writes a diagram as Mermaid text,
which Mermaid and the parser read back. Use it to put a diagram built in C# in a Markdown page that renders Mermaid:

<!-- ref:../../tests/Meziantou.Framework.Diagrams.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;

var text = MermaidWriter.Write(flowchart);
```
<!-- endref -->

The writer throws an `ArgumentException` for what Mermaid cannot express, such as an identifier with spaces or a
keyword (`end`, `style`...). It writes the theme when it is one of the themes of Mermaid; a modified theme is not written.

## Supported diagrams

[SupportedDiagramTypes](cref:Meziantou.Framework.Diagrams.Mermaid.MermaidDocument.SupportedDiagramTypes) lists the
keywords the parser reads. A text of another type of Mermaid diagram throws an error that says the type is not
supported yet. See [diagram types](04-DiagramTypes/01-Overview.md).
