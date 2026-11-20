using Meziantou.Framework.Graphviz.Internals;

namespace Meziantou.Framework.Graphviz.Tests;

public sealed class GraphvizWriterTests
{
    [Fact]
    public void Write()
    {
        #region Graphviz_Write
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
        var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
        var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
        flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;

        var dot = GraphvizWriter.Write(flowchart);
        #endregion

        Assert.Equal(
            """
            digraph {
                start [label="Start", shape=ellipse];
                end_node [label="End", shape=doublecircle];
                start -> end_node [label="done", style=dotted];
                rankdir=LR;
            }

            """.ReplaceLineEndings("\n"),
            dot);
    }

    [Fact]
    public void Write_SubgraphsClassesAndCompoundEdges()
    {
        var flowchart = new FlowchartDiagram { Title = "Deploy", Curve = EdgeCurve.Linear };
        flowchart.ClassDefinitions["important"] = new DiagramStyle { Fill = SceneColor.Parse("#ffcccc"), Stroke = SceneColor.Parse("red") };
        var build = flowchart.AddNode("build", "Build\nand test");
        build.CssClasses.Add("important");
        var servers = flowchart.AddSubgraph("servers", "Servers");
        servers.Nodes.Add(new FlowchartNode("web", "Web", FlowchartNodeShape.Cylinder));
        var edge = flowchart.AddEdge(build, servers, "deploy");
        edge.LineStyle = FlowchartLineStyle.Thick;
        edge.StartArrowHead = FlowchartArrowHead.Circle;

        var dot = GraphvizWriter.Write(flowchart);

        Assert.Equal(
            """
            digraph {
                build [label="Build\nand test", shape=box, style=filled, fillcolor="#ffcccc", color="#ff0000", class=important];
                subgraph servers {
                    web [label="Web", shape=cylinder];
                    label="Servers";
                    cluster=true;
                }
                build -> web [lhead=servers, label="deploy", dir=both, arrowtail=dot, style=bold];
                label="Deploy";
                splines=line;
                compound=true;
            }

            """.ReplaceLineEndings("\n"),
            dot);

        var document = GraphvizDocument.Parse(dot);
        Assert.Empty(document.Warnings);
        var read = document.Diagram;
        Assert.Equal("Deploy", read.Title);
        Assert.Equal("Build\nand test", read.Nodes[0].Label?.Text);
        Assert.Equal("#ffcccc", read.Nodes[0].Style?.Fill?.ToString());
        Assert.Equal(["important"], read.Nodes[0].CssClasses);
        Assert.Equal("servers", Assert.IsType<FlowchartSubgraph>(read.Edges[0].Target).Id);
        Assert.Equal(FlowchartArrowHead.Circle, read.Edges[0].StartArrowHead);
        Assert.Equal(FlowchartLineStyle.Thick, read.Edges[0].LineStyle);
        Assert.Equal(dot, GraphvizWriter.Write(read));
    }

    [Fact]
    public void Write_DashesOfSolidEdgesAndDefaultClass()
    {
        var flowchart = new FlowchartDiagram { DefaultEdgeStyle = new DiagramStyle { StrokeDashArray = [5, 2] } };
        flowchart.ClassDefinitions["default"] = new DiagramStyle { Fill = SceneColor.Parse("#ffcccc"), Stroke = SceneColor.Parse("red") };
        flowchart.AddEdge("a", "b");
        flowchart.AddEdge("b", "c").Style = new DiagramStyle { StrokeDashArray = [1, 3] };
        flowchart.AddSubgraph("group", "Group");

        var dot = GraphvizWriter.Write(flowchart);

        Assert.Equal(
            """
            digraph {
                a [shape=box, style=filled, fillcolor="#ffcccc", color="#ff0000"];
                b [shape=box, style=filled, fillcolor="#ffcccc", color="#ff0000"];
                c [shape=box, style=filled, fillcolor="#ffcccc", color="#ff0000"];
                subgraph group {
                    label="Group";
                    cluster=true;
                }
                a -> b [style=dashed];
                b -> c [style=dotted];
            }

            """.ReplaceLineEndings("\n"),
            dot);
    }

    [Fact]
    public void Write_TextWriter()
    {
        var flowchart = new FlowchartDiagram();
        flowchart.AddEdge("a", "b");
        using var writer = new StringWriter();
        GraphvizWriter.Write(flowchart, writer);
        Assert.Equal(GraphvizWriter.Write(flowchart), writer.ToString());
    }

    [Theory]
    [InlineData("""
        digraph G {
            rankdir=RL; nodesep=0.3; ranksep=1.25; bgcolor="#fafafa"; splines=ortho;
            node [shape=box, style="filled,rounded", fillcolor=lightblue];
            a [label="A\lB\l", URL="https://example.com", tooltip="tip", fontsize=9, penwidth=2];
            b [shape=record, label="{x|<p> y}"];
            c [label=<<i>html</i>>, style="dashed,invis"];
            subgraph cluster_0 { label="Zero"; style="filled,dotted"; fillcolor=yellow; pencolor=blue; d; subgraph cluster_1 { e } }
            a -> b -> c [label="\E", color=red, minlen=2];
            d -> e [dir=both, arrowtail=odot, style=dashed];
            e -> a [dir=none, style=invis];
        }
        """)]
    [InlineData("""
        strict graph { a -- b; b -- a [label="x\ny\n"]; "node" -- "with \"quotes\"" -- "back\\slash"; -1 -- .5 }
        """)]
    [InlineData("""
        digraph { compound=true; subgraph cluster_a { a1; subgraph cluster_b { b1 } } a1 -> b1 [lhead=cluster_b]; b1 -> a1 [ltail=cluster_a]; x -> a1 [lhead=cluster_a] }
        """)]
    [InlineData("""
        digraph { subgraph s { cluster=true; a } subgraph { cluster=true; b } <x\> -> "y" }
        """)]
    public void RoundTrip(string text)
    {
        var document = GraphvizDocument.Parse(text);
        var written = GraphvizWriter.Write(document.Diagram);
        var read = GraphvizDocument.Parse(written);
        Assert.Equal(written, GraphvizWriter.Write(read.Diagram));
        Assert.Equal(document.Diagram.Nodes.Select(node => (node.Id, node.Label?.Text, node.Shape)), read.Diagram.Nodes.Select(node => (node.Id, node.Label?.Text, node.Shape)));
        Assert.Equal(document.Diagram.EnumerateSubgraphs().Select(subgraph => subgraph.Id), read.Diagram.EnumerateSubgraphs().Select(subgraph => subgraph.Id));
        Assert.HasCount(document.Diagram.Edges.Count, read.Diagram.Edges);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("_a1", "_a1")]
    [InlineData("été", "été")]
    [InlineData("-1.5", "-1.5")]
    [InlineData(".5", ".5")]
    [InlineData("1.", "1.")]
    [InlineData("node", "\"node\"")]
    [InlineData("Graph", "\"Graph\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("1a", "\"1a\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("a\\b", "\"a\\b\"")]
    [InlineData("a\\", "<a\\>")]
    [InlineData("<b>\\", "<<b>\\>")]
    public void FormatId(string id, string expected)
    {
        Assert.Equal(expected, DotWriter.FormatId(id));
    }

    [Fact]
    public void FormatId_Unrepresentable()
    {
        Assert.Throws<ArgumentException>(() => DotWriter.FormatId("<a\\"));
    }

    [Fact]
    public void ShapesAreStable()
    {
        foreach (var shape in Enum.GetValues<FlowchartNodeShape>())
        {
            var dot = DotShapes.ToDot(shape);
            var read = DotShapes.FromDot(dot, rounded: shape is FlowchartNodeShape.RoundedRectangle, sides: null);
            Assert.NotNull(read);
            Assert.Equal(dot, DotShapes.ToDot(read.Value));
        }
    }

    [Fact]
    public void NodesOnlyInEdgesAreDeclared()
    {
        var flowchart = new FlowchartDiagram();
        flowchart.Edges.Add(new FlowchartEdge(new FlowchartNode("x", "X"), new FlowchartNode("y")));
        var read = GraphvizDocument.Parse(GraphvizWriter.Write(flowchart)).Diagram;
        Assert.Equal(["x", "y"], read.Nodes.Select(node => node.Id));
        Assert.Equal("X", read.Nodes[0].Label?.Text);
    }

    [Fact]
    public void EdgesToEmptySubgraphsAreNotWritten()
    {
        var flowchart = new FlowchartDiagram();
        var empty = flowchart.AddSubgraph("cluster_empty");
        flowchart.AddEdge(flowchart.AddNode("a"), empty);
        var read = GraphvizDocument.Parse(GraphvizWriter.Write(flowchart)).Diagram;
        Assert.Empty(read.Edges);
        Assert.Single(read.Subgraphs);
    }
}
