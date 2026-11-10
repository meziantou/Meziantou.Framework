# Meziantou.Framework.Mermaid

Reads the text format of [Mermaid](https://mermaid.js.org) into the diagrams of
[Meziantou.Framework.Diagrams](https://www.nuget.org/packages/Meziantou.Framework.Diagrams) and into charts drawn with
[Meziantou.Framework.Charts](https://www.nuget.org/packages/Meziantou.Framework.Charts) (pie, XY, quadrant, sankey and
radar charts, treemaps), and writes them back as Mermaid: the diagrams, their front matter, their `%%{init}%%`
directives, their themes and their accessibility statements.

## Usage

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

Write a diagram as Mermaid:

<!-- ref:../../tests/Meziantou.Framework.Mermaid.Tests/MermaidDocumentTests.cs;region=Mermaid_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;

var text = MermaidWriter.Write(flowchart);
```
<!-- endref -->
