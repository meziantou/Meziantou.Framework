# Meziantou.Framework.Diagrams

Draws diagrams as deterministic SVG, inspired by [Mermaid](https://mermaid.js.org): flowcharts with the 53 shapes of
Mermaid, swimlanes, class, state, entity relationship, requirement, use case, C4, block, architecture and sequence
diagrams, Gantt charts, timelines, git graphs, kanban boards, packet diagrams, user journeys, event models, railroad
diagrams, mind maps, tree views, Ishikawa diagrams, Venn diagrams, Wardley maps and Cynefin diagrams, with styles and
classes, in the themes of Mermaid. [Meziantou.Framework.Mermaid](https://www.nuget.org/packages/Meziantou.Framework.Mermaid)
reads and writes the Mermaid text format, and draws the charts of Mermaid with Meziantou.Framework.Charts, and [Meziantou.Framework.Imaging.VectorGraphics](https://www.nuget.org/packages/Meziantou.Framework.Imaging.VectorGraphics)
renders the diagrams as images.

## Usage

<!-- ref:../../tests/Meziantou.Framework.Diagrams.Tests/FlowchartTests.cs;region=Diagrams_Flowchart;format=md-fenced-code-block;dedent=true -->
```csharp
var flowchart = new FlowchartDiagram { Title = "Order processing" };
var start = flowchart.AddNode("start", "Order received", FlowchartNodeShape.Stadium);
var check = flowchart.AddNode("check", "Is the payment valid?", FlowchartNodeShape.Diamond);
var ship = flowchart.AddNode("ship", "Ship the order");
var cancel = flowchart.AddNode("cancel", "Cancel the order");

flowchart.AddEdge(start, check);
flowchart.AddEdge(check, ship, "yes");
flowchart.AddEdge(check, cancel, "no");

var svg = flowchart.ToSvg();
```
<!-- endref -->
