---
title: Reading and writing
description: The parts of Mermaid every diagram shares, the configuration, the errors, the warnings, and writing Mermaid text.
---

## Shared syntax

Every type of diagram reads the parts of Mermaid they share:

- The YAML front matter between two `---` lines: its `title` is the title of the diagram, and its `config` the
  configuration.
- The `%%{init: ...}%%` directives, which also set the configuration.
- The `%%` comments, on their own line.
- The accessibility statements `accTitle:` and `accDescr:` (or `accDescr { ... }` on several lines), which become the
  title and the description of the SVG.

The configuration sets the theme (`theme`, `themeVariables`, see [themes](../Diagrams/05-Theming.md)), `fontFamily`, `fontSize`,
and the options of the diagram, such as `flowchart.curve`, `flowchart.nodeSpacing`, `flowchart.rankSpacing`,
`flowchart.wrappingWidth` and `flowchart.diagramPadding`.

## Errors

A text that is not valid Mermaid throws a [MermaidParseException](cref:Meziantou.Framework.Mermaid.MermaidParseException),
whose [Error](cref:Meziantou.Framework.Mermaid.MermaidParseException.Error) gives the line and the column of the
problem. [TryParse](cref:Meziantou.Framework.Mermaid.MermaidDocument.TryParse) returns it instead:

<!-- ref:../../tests/Meziantou.Framework.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Errors;format=md-fenced-code-block;dedent=true -->
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

[MermaidParseOptions](cref:Meziantou.Framework.Mermaid.MermaidParseOptions) limits the size of the text, the
number of nodes and edges and the depth of the nesting, as Mermaid does: keep the limits when the text comes from users.

## Warnings

Some valid Mermaid has no equivalent in the diagrams: the JavaScript callbacks of `click`, the hand-drawn look, the
icons, the animations of the edges. The parser ignores them and reports them in
[Warnings](cref:Meziantou.Framework.Mermaid.MermaidDocument.Warnings), with their position. A link whose
address could run a script (`javascript:`) is ignored too: only `http`, `https`, `mailto` and relative addresses are kept.

## Write a diagram

[MermaidWriter.Write](cref:Meziantou.Framework.Mermaid.MermaidWriter.Write) writes a diagram as Mermaid text,
which Mermaid and the parser read back. Use it to put a diagram built in C# in a Markdown page that renders Mermaid:

<!-- ref:../../tests/Meziantou.Framework.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Write;format=md-fenced-code-block;dedent=true -->
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

[SupportedDiagramTypes](cref:Meziantou.Framework.Mermaid.MermaidDocument.SupportedDiagramTypes) lists the
keywords the parser reads. A text of another type of Mermaid diagram throws an error that says the type is not
supported yet. See the [diagram types](../Diagrams/04-DiagramTypes/01-Overview.md) and the [charts](04-Charts/01-Overview.md).
