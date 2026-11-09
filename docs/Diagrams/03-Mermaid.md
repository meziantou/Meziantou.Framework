---
title: Mermaid
description: Draw diagrams written in the Mermaid text format, and write diagrams as Mermaid.
---

The `Meziantou.Framework.Mermaid` package reads the text format of [Mermaid](https://mermaid.js.org) into the
diagrams of this library, and writes them back as Mermaid:

<InstallNuGetPackage Id="Meziantou.Framework.Mermaid" />

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
var flowchart = (FlowchartDiagram)document.Diagram;
var svg = flowchart.ToSvg();
```
<!-- endref -->

Each page of the [diagram types](04-DiagramTypes/01-Overview.md) gives the Mermaid syntax the type reads. See the
[Mermaid documentation](../Mermaid/01-Introduction.md) for the shared syntax, the errors, the warnings and the writer.
