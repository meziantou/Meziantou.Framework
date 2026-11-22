# Meziantou.Framework.Graphviz

Reads the DOT language of [Graphviz](https://graphviz.org) into the flowcharts of
[Meziantou.Framework.Diagrams](https://www.nuget.org/packages/Meziantou.Framework.Diagrams), drawn as deterministic SVG
without installing Graphviz, and writes flowcharts as DOT: directed, undirected and strict graphs, default attributes,
clusters, edges between groups of nodes, shapes, styles, colors, and record and HTML-like labels drawn as text. The
parts of DOT the flowcharts cannot draw, such as the ports, the other layout engines or `rank=same`, are reported as
warnings.

## Usage

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

Write a flowchart as DOT:

<!-- ref:../../tests/Meziantou.Framework.Graphviz.Tests/GraphvizWriterTests.cs;region=Graphviz_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;

var dot = GraphvizWriter.Write(flowchart);
```
<!-- endref -->
