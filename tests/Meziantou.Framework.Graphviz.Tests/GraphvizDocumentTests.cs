namespace Meziantou.Framework.Graphviz.Tests;

public sealed class GraphvizDocumentTests
{
    [Fact]
    public void Parse()
    {
        #region Graphviz_Parse
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
        #endregion

        Assert.Equal("Orders", document.Name);
        Assert.True(document.IsDirected);
        Assert.False(document.IsStrict);
        Assert.Empty(document.Warnings);
        Assert.Equal(DiagramDirection.LeftToRight, flowchart.Direction);
        Assert.Equal(["start", "check", "cancel"], flowchart.Nodes.Select(node => node.Id));
        Assert.Equal(FlowchartNodeShape.Stadium, flowchart.Nodes[0].Shape);
        Assert.Equal(FlowchartNodeShape.Diamond, flowchart.Nodes[1].Shape);
        Assert.Equal(FlowchartNodeShape.RoundedRectangle, flowchart.Nodes[2].Shape);
        Assert.Equal("#ff0000", flowchart.Nodes[2].Style?.Stroke?.ToString());

        var cluster = Assert.Single(flowchart.Subgraphs);
        Assert.Equal("cluster_fulfillment", cluster.Id);
        Assert.Equal("Fulfillment", cluster.Label?.Text);
        Assert.Equal(["ship", "notify"], cluster.Nodes.Select(node => node.Id));

        Assert.HasCount(4, flowchart.Edges);
        Assert.Equal("yes", flowchart.Edges[1].Label?.Text);
        Assert.Equal(FlowchartLineStyle.Dotted, flowchart.Edges[3].LineStyle);
        Assert.Equal(FlowchartArrowHead.Arrow, flowchart.Edges[3].EndArrowHead);
        Assert.Equal(FlowchartArrowHead.None, flowchart.Edges[3].StartArrowHead);
        Assert.Contains("Order received", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors()
    {
        #region Graphviz_Errors
        var text = """
            digraph {
                a -> b
                b -- c
            }
            """;

        // TryParse returns the position of the first error instead of throwing a GraphvizParseException
        Assert.False(GraphvizDocument.TryParse(text, options: null, out _, out var error));
        Assert.Equal((3, 7), (error.Line, error.Column));
        #endregion

        Assert.Equal("The edges of a directed graph (digraph) are written '->', not '--'.", error.Message);
        var exception = Assert.Throws<GraphvizParseException>(() => GraphvizDocument.Parse(text));
        Assert.Equal(error, exception.Error);
    }

    [Theory]
    [InlineData("", "(1, 1): The text has no graph.")]
    [InlineData("  \n\n", "(3, 1): The text has no graph.")]
    [InlineData("foo {}", "(1, 1): Expected 'graph' or 'digraph', but found 'foo'.")]
    [InlineData("strict { }", "(1, 8): Expected 'graph' or 'digraph', but found '{'.")]
    [InlineData("digraph {", "(1, 10): The graph is not closed: '}' is missing.")]
    [InlineData("graph { a -> b }", "(1, 11): The edges of an undirected graph (graph) are written '--', not '->'.")]
    [InlineData("digraph { a [label] }", "(1, 19): Expected '=' after the attribute 'label', but found ']'.")]
    [InlineData("digraph { node }", "(1, 16): Expected '[' after 'node', but found '}'.")]
    [InlineData("digraph { a -> node }", "(1, 16): 'node' is a keyword: quote it to use it as a name.")]
    [InlineData("digraph { a [label=\"x] }", "(1, 20): The string is not closed: '\"' is missing.")]
    [InlineData("digraph { /* a }", "(1, 11): The comment is not closed: '*/' is missing.")]
    [InlineData("digraph { a [label=<<b>x</b>] }", "(1, 20): The HTML string is not closed: '>' is missing.")]
    [InlineData("digraph { \"\" }", "(1, 11): The name of a node cannot be empty.")]
    [InlineData("digraph { a -> b + c }", "(1, 18): Expected a name, a number or a string, but found '+'.")]
    [InlineData("digraph { \"a\" + b }", "(1, 17): Only quoted strings can be joined with '+', but found 'b'.")]
    [InlineData("digraph { a @ b }", "(1, 13): Unexpected character '@'.")]
    [InlineData("digraph { a -> - }", "(1, 16): Unexpected character '-'.")]
    [InlineData("digraph { subgraph }", "(1, 20): Expected '{' or the name of the subgraph, but found '}'.")]
    [InlineData("digraph {\r\n  a -> b\r\n  b -- c\r\n}", "(3, 5): The edges of a directed graph (digraph) are written '->', not '--'.")]
    public void InvalidText(string text, string expected)
    {
        var exception = Assert.Throws<GraphvizParseException>(() => GraphvizDocument.Parse(text));
        Assert.Equal(expected, exception.Error.ToString());
    }

    [Fact]
    public void Limits()
    {
        Assert.Equal("(1, 1): The text is too long: the limit is 5 characters.", GetError("digraph {}", new GraphvizParseOptions { MaxTextSize = 5 }));
        Assert.Equal("(1, 17): The graph has too many nodes: the limit is 2.", GetError("digraph { a; b; c }", new GraphvizParseOptions { MaxNodes = 2 }));
        Assert.Equal("(1, 11): The graph has too many edges: the limit is 2.", GetError("digraph { a -> {b c d} }", new GraphvizParseOptions { MaxEdges = 2 }));
        Assert.Equal("(1, 22): The subgraphs are nested too deeply: the limit is 1.", GetError("digraph { subgraph { subgraph { a } } }", new GraphvizParseOptions { MaxDepth = 1 }));

        static string GetError(string text, GraphvizParseOptions options)
        {
            Assert.False(GraphvizDocument.TryParse(text, options, out _, out var error));
            return error.ToString();
        }
    }

    [Fact]
    public void TryParse_Null()
    {
        Assert.False(GraphvizDocument.TryParse(null, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void UndirectedGraph()
    {
        var document = GraphvizDocument.Parse("""
            graph {
                a -- b -- c;
                c -- d [dir=forward];
            }
            """);

        Assert.False(document.IsDirected);
        var edges = document.Diagram.Edges;
        Assert.HasCount(3, edges);
        Assert.Equal(FlowchartArrowHead.None, edges[0].EndArrowHead);
        Assert.Equal(FlowchartArrowHead.None, edges[0].StartArrowHead);
        Assert.Equal(("b", "c"), (edges[1].Source.Id, edges[1].Target.Id));
        Assert.Equal(FlowchartArrowHead.Arrow, edges[2].EndArrowHead);
    }

    [Fact]
    public void StrictGraph()
    {
        var directed = GraphvizDocument.Parse("""
            strict digraph {
                a -> b;
                a -> b [label="x"];
                b -> a;
            }
            """);

        Assert.True(directed.IsStrict);
        Assert.HasCount(2, directed.Diagram.Edges);
        Assert.Equal("x", directed.Diagram.Edges[0].Label?.Text);

        var undirected = GraphvizDocument.Parse("strict graph { a -- b; b -- a }");
        Assert.Single(undirected.Diagram.Edges);
    }

    [Fact]
    public void DefaultAttributesAreScoped()
    {
        var flowchart = GraphvizDocument.Parse("""
            digraph {
                node [shape=box];
                a;
                subgraph {
                    node [shape=circle];
                    b;
                }
                c;
                edge [color=red];
                a -> b;
            }
            """).Diagram;

        Assert.Equal(["a", "b", "c"], flowchart.Nodes.Select(node => node.Id));
        Assert.Equal([FlowchartNodeShape.Rectangle, FlowchartNodeShape.Circle, FlowchartNodeShape.Rectangle], flowchart.Nodes.Select(node => node.Shape));
        Assert.Empty(flowchart.Subgraphs);
        Assert.Equal("#ff0000", flowchart.Edges[0].Style?.Stroke?.ToString());
    }

    [Fact]
    public void EdgesBetweenGroups()
    {
        var flowchart = GraphvizDocument.Parse("""
            digraph {
                a -> {b c} -> d;
                {e f} -> g;
            }
            """).Diagram;

        Assert.Equal(["a", "b", "c", "d", "e", "f", "g"], flowchart.Nodes.Select(node => node.Id));
        Assert.Equal(["a->b", "a->c", "b->d", "c->d", "e->g", "f->g"], flowchart.Edges.Select(edge => edge.Source.Id + "->" + edge.Target.Id));
    }

    [Fact]
    public void Clusters()
    {
        var flowchart = GraphvizDocument.Parse("""
            digraph {
                a -> b;
                subgraph cluster_outer {
                    label="Outer";
                    b;
                    subgraph cluster_inner {
                        label="Inner";
                        c;
                    }
                    subgraph group {
                        d;
                    }
                }
                subgraph other {
                    cluster=true;
                    label="Other";
                    a;
                }
            }
            """).Diagram;

        Assert.Empty(flowchart.Nodes);
        Assert.Equal(["cluster_outer", "other"], flowchart.Subgraphs.Select(subgraph => subgraph.Id));
        var outer = flowchart.Subgraphs[0];
        Assert.Equal("Outer", outer.Label?.Text);
        Assert.Equal(["b", "d"], outer.Nodes.Select(node => node.Id));
        var inner = Assert.Single(outer.Subgraphs);
        Assert.Equal("Inner", inner.Label?.Text);
        Assert.Equal(["c"], inner.Nodes.Select(node => node.Id));
        Assert.Equal(["a"], flowchart.Subgraphs[1].Nodes.Select(node => node.Id));
    }

    [Fact]
    public void ClustersInheritTheAttributesOfTheirGraph()
    {
        // As in Graphviz, a cluster starts with the attributes the graph has when the cluster is created
        var before = GraphvizDocument.Parse("""
            digraph {
                label="Title";
                subgraph cluster_a { a }
                subgraph cluster_b { label="B"; b }
            }
            """).Diagram;

        Assert.Equal("Title", before.Title);
        Assert.Equal(["Title", "B"], before.Subgraphs.Select(subgraph => subgraph.Label?.Text));

        var after = GraphvizDocument.Parse("digraph { subgraph cluster_a { a } label=\"Title\" }").Diagram;
        Assert.Equal("Title", after.Title);
        Assert.Equal(string.Empty, after.Subgraphs[0].Label?.Text);
    }

    [Fact]
    public void ClusterNamedAsANode()
    {
        var flowchart = GraphvizDocument.Parse("digraph { cluster_a; subgraph cluster_a { b } }").Diagram;
        Assert.Equal("cluster_a", flowchart.Nodes[0].Id);
        Assert.Equal("cluster_a_1", flowchart.Subgraphs[0].Id);
    }

    [Fact]
    public void Labels()
    {
        var flowchart = GraphvizDocument.Parse("""
            digraph G {
                a [label="first\nsecond\lthird\l"];
                b [label="\N in \G"];
                c [label="multi" + "part"];
                d [label="line \
            continued"];
                e [label="back\\slash \"quoted\""];
                f [label="\N"];
                g [label=""];
                a -> b [label="\E: \T to \H"];
                b -> c [label=""];
            }
            """).Diagram;

        Assert.Equal(["first\nsecond\nthird", "b in G", "multipart", "line continued", "back\\slash \"quoted\"", null, string.Empty], flowchart.Nodes.Select(node => node.Label?.Text));
        Assert.Equal("a->b: a to b", flowchart.Edges[0].Label?.Text);
        Assert.Null(flowchart.Edges[1].Label);
    }

    [Fact]
    public void HtmlLabels()
    {
        var document = GraphvizDocument.Parse("""
            digraph {
                a [label=<<b>Bold</b> &amp; text<br/>second   line>];
                t [label=<<table><tr><td>A</td><td>B</td></tr><tr><td>C</td></tr></table>>];
            }
            """);

        Assert.Equal(["Bold & text\nsecond line", "A B\nC"], document.Diagram.Nodes.Select(node => node.Label?.Text));
        Assert.Equal(["(2, 8): The HTML-like labels are drawn as plain text: their tables, fonts and images are not supported."], document.Warnings.Select(warning => warning.ToString()));
    }

    [Fact]
    public void RecordLabels()
    {
        var document = GraphvizDocument.Parse("""
            digraph {
                r [shape=record, label="<f0> left|<f1> mid\ dle|{<f2> top|bottom}"];
                m [shape=Mrecord, label="a|b"];
                n [shape=record];
            }
            """);

        var nodes = document.Diagram.Nodes;
        Assert.Equal("left\nmid dle\ntop\nbottom", nodes[0].Label?.Text);
        Assert.Equal(FlowchartNodeShape.Rectangle, nodes[0].Shape);
        Assert.Equal("a\nb", nodes[1].Label?.Text);
        Assert.Equal(FlowchartNodeShape.RoundedRectangle, nodes[1].Shape);
        Assert.Null(nodes[2].Label);
        Assert.Single(document.Warnings);
    }

    [Theory]
    [InlineData("red", "#ff0000")]
    [InlineData("Red", "#ff0000")]
    [InlineData("green", "#00ff00")]
    [InlineData("gray", "#bebebe")]
    [InlineData("gray50", "#808080")]
    [InlineData("grey0", "#000000")]
    [InlineData("steelblue", "#4682b4")]
    [InlineData("#1a2b3c", "#1a2b3c")]
    [InlineData("#1a2b3c80", "#1a2b3c80")]
    [InlineData("0.000 1.000 1.000", "#ff0000")]
    [InlineData("0.5,1,1", "#00ffff")]
    [InlineData("red:blue", "#ff0000")]
    [InlineData("blue;0.3:red", "#0000ff")]
    [InlineData("/x11/navyblue", "#000080")]
    [InlineData("transparent", "#00000000")]
    public void Colors(string value, string expected)
    {
        var document = GraphvizDocument.Parse($"digraph {{ a [color=\"{value}\"] }}");
        Assert.Equal(expected, document.Diagram.Nodes[0].Style?.Stroke?.ToString());
        Assert.Empty(document.Warnings);
    }

    [Theory]
    [InlineData("notacolor")]
    [InlineData("/accent3/1")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("red2")]
    public void InvalidColors(string value)
    {
        var document = GraphvizDocument.Parse($"digraph {{ a [color=\"{value}\"] }}");
        Assert.Null(document.Diagram.Nodes[0].Style);
        Assert.Equal($"(1, 14): The color '{value}' is not supported and is ignored.", Assert.Single(document.Warnings).ToString());
    }

    [Fact]
    public void NodeStyles()
    {
        var nodes = GraphvizDocument.Parse("""
            digraph {
                a [style="filled,rounded,dashed", shape=box, fillcolor=yellow, color=blue, penwidth=3, fontcolor=white, fontsize=20, URL="https://example.com/\N", tooltip="Tip", class="first second"];
                b [style=filled];
                c [style="invis,bold"];
                d [style="dotted,setlinewidth(4)"];
                e [fillcolor=red];
            }
            """).Diagram.Nodes;

        var a = nodes[0];
        Assert.Equal(FlowchartNodeShape.RoundedRectangle, a.Shape);
        Assert.Equal("#ffff00", a.Style?.Fill?.ToString());
        Assert.Equal("#0000ff", a.Style?.Stroke?.ToString());
        Assert.Equal(3, a.Style?.StrokeWidth);
        Assert.Equal([5.0, 2.0], a.Style?.StrokeDashArray);
        Assert.Equal("#ffffff", a.Style?.TextColor?.ToString());
        Assert.Equal(20, a.Style?.FontSize);
        Assert.Equal("https://example.com/a", a.Link);
        Assert.Equal("Tip", a.Tooltip);
        Assert.Equal(["first", "second"], a.CssClasses);

        Assert.Equal("#d3d3d3", nodes[1].Style?.Fill?.ToString());
        Assert.Equal(FlowchartNodeShape.Stadium, nodes[1].Shape);
        Assert.Equal(0, nodes[2].Style?.Opacity);
        Assert.Equal(2, nodes[2].Style?.StrokeWidth);
        Assert.Equal([1.0, 3.0], nodes[3].Style?.StrokeDashArray);
        Assert.Equal(4, nodes[3].Style?.StrokeWidth);

        // As in Graphviz, the fill color applies only to filled nodes
        Assert.Null(nodes[4].Style);
    }

    [Fact]
    public void EdgeStyles()
    {
        var document = GraphvizDocument.Parse("""
            digraph {
                a -> b [style=dashed];
                a -> c [style=dotted];
                a -> d [style=bold];
                a -> e [style=invis];
                a -> f [style="dashed,bold", penwidth=3];
                a -> g [minlen=3, color="red", fontcolor=blue, fontsize=8, arrowhead=dot, arrowtail=tee, dir=both, class=critical];
                a -> h [dir=back];
                a -> i [arrowhead=none];
                a -> j [arrowhead=diamond];
            }
            """);

        var edges = document.Diagram.Edges;
        Assert.Equal(FlowchartLineStyle.Dotted, edges[0].LineStyle);
        Assert.Equal([5.0, 2.0], edges[0].Style?.StrokeDashArray);
        Assert.Equal(FlowchartLineStyle.Dotted, edges[1].LineStyle);
        Assert.Null(edges[1].Style);
        Assert.Equal(FlowchartLineStyle.Thick, edges[2].LineStyle);
        Assert.Equal(FlowchartLineStyle.Invisible, edges[3].LineStyle);
        Assert.Equal(FlowchartLineStyle.Dotted, edges[4].LineStyle);
        Assert.Equal(3, edges[4].Style?.StrokeWidth);

        var g = edges[5];
        Assert.Equal(3, g.Length);
        Assert.Equal("#ff0000", g.Style?.Stroke?.ToString());
        Assert.Equal("#0000ff", g.Style?.TextColor?.ToString());
        Assert.Equal(8, g.Style?.FontSize);
        Assert.Equal(FlowchartArrowHead.Circle, g.EndArrowHead);
        Assert.Equal(FlowchartArrowHead.Cross, g.StartArrowHead);
        Assert.Equal(["critical"], g.CssClasses);

        Assert.Equal((FlowchartArrowHead.Arrow, FlowchartArrowHead.None), (edges[6].StartArrowHead, edges[6].EndArrowHead));
        Assert.Equal((FlowchartArrowHead.None, FlowchartArrowHead.None), (edges[7].StartArrowHead, edges[7].EndArrowHead));
        Assert.Equal(FlowchartArrowHead.Arrow, edges[8].EndArrowHead);
        Assert.Equal(
            [
                "(7, 79): The arrowhead 'tee' is not supported: it is drawn as a cross.",
                "(10, 13): The arrowhead 'diamond' is not supported: it is drawn as an arrow.",
            ],
            document.Warnings.Select(warning => warning.ToString()));
    }

    [Fact]
    public void CompoundEdges()
    {
        var text = """
            digraph {
                compound=true;
                subgraph cluster_a { a1; a2 }
                subgraph cluster_b { b1 }
                a1 -> b1 [lhead=cluster_b];
                a2 -> b1 [ltail=cluster_a, lhead=cluster_b];
                a1 -> a2 [lhead=cluster_a];
            }
            """;

        var document = GraphvizDocument.Parse(text);
        var edges = document.Diagram.Edges;
        Assert.IsType<FlowchartNode>(edges[0].Source);
        Assert.Equal("cluster_b", Assert.IsType<FlowchartSubgraph>(edges[0].Target).Id);
        Assert.Equal("cluster_a", Assert.IsType<FlowchartSubgraph>(edges[1].Source).Id);
        Assert.Equal("cluster_b", Assert.IsType<FlowchartSubgraph>(edges[1].Target).Id);
        Assert.IsType<FlowchartNode>(edges[2].Target);
        Assert.Equal("(7, 15): The cluster 'cluster_a' does not exist, does not contain the end of the edge, or contains both ends: the edge is not clipped.", Assert.Single(document.Warnings).ToString());

        // Without compound=true, Graphviz does not clip the edges
        var withoutCompound = GraphvizDocument.Parse(text.Replace("compound=true;", string.Empty, StringComparison.Ordinal)).Diagram;
        Assert.All(withoutCompound.Edges, edge => Assert.IsType<FlowchartNode>(edge.Target));
    }

    [Fact]
    public void GraphAttributes()
    {
        var flowchart = GraphvizDocument.Parse("""digraph { nodesep=1; ranksep="0.5 equally"; splines=ortho; bgcolor=lightyellow; a }""").Diagram;
        Assert.Equal(72, flowchart.NodeSpacing);
        Assert.Equal(36, flowchart.RankSpacing);
        Assert.Equal(EdgeCurve.Step, flowchart.Curve);
        Assert.Equal("#ffffe0", flowchart.Theme.Background?.ToString());

        var linear = GraphvizDocument.Parse("digraph { splines=false; rankdir=bt }").Diagram;
        Assert.Equal(EdgeCurve.Linear, linear.Curve);
        Assert.Equal(DiagramDirection.BottomToTop, linear.Direction);
    }

    [Fact]
    public void DefaultTheme()
    {
        var document = GraphvizDocument.Parse("digraph { a }", new GraphvizParseOptions { DefaultTheme = DiagramTheme.Dark });
        Assert.Same(DiagramTheme.Dark, document.Diagram.Theme);
    }

    [Fact]
    public void Warnings()
    {
        var document = GraphvizDocument.Parse("""
            digraph {
                layout=neato;
                a [fontname="Arial", width=2];
                b [fontname="Arial"];
                a:p1:n -> b:s;
                subgraph { rank=same; a; b }
            }
            """);

        Assert.Equal(
            [
                "(5, 6): The ports of the nodes (node:port:compass) are not supported: the edges are attached to the nodes.",
                "(2, 5): The layout engine 'neato' is not supported: the graph is drawn with the layered layout of dot.",
                "(6, 16): The ranks of the subgraphs (rank=same, min, max, source, sink) are not supported: the layout places the nodes.",
                "(3, 8): The attribute 'fontname' of the nodes is not supported and is ignored.",
                "(3, 26): The attribute 'width' of the nodes is not supported and is ignored.",
            ],
            document.Warnings.Select(warning => warning.ToString()));
        Assert.Single(document.Diagram.Edges);
    }

    [Fact]
    public void CommentsAndPreprocessorLines()
    {
        var text = (char)0xFEFF + "#line 1\n// comment\ndigraph {\n# 2 \"file\"\n  a /* inline */ -> b; // end\n}\n";
        var document = GraphvizDocument.Parse(text);
        Assert.Equal(["a", "b"], document.Diagram.Nodes.Select(node => node.Id));
        Assert.Single(document.Diagram.Edges);
        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void Numerals()
    {
        var document = GraphvizDocument.Parse("digraph { -1.5 -> .5 -> 2abc }");
        Assert.Equal(["-1.5", ".5", "2", "abc"], document.Diagram.Nodes.Select(node => node.Id));
        Assert.Equal("(1, 25): The number '2' is followed by 'a' with no space: they are read as two identifiers.", Assert.Single(document.Warnings).ToString());
    }

    [Fact]
    public void OnlyTheFirstGraphIsRead()
    {
        var document = GraphvizDocument.Parse("digraph { a } digraph { b }");
        Assert.Equal(["a"], document.Diagram.Nodes.Select(node => node.Id));
        Assert.Equal("(1, 15): Only the first graph of the text is read.", Assert.Single(document.Warnings).ToString());
    }

    [Fact]
    public void KeywordsAreCaseInsensitive()
    {
        var document = GraphvizDocument.Parse("STRICT DiGraph { NODE [shape=box]; SubGraph Cluster_A { a } }");
        Assert.True(document.IsStrict);
        Assert.Equal("Cluster_A", document.Diagram.Subgraphs[0].Id);
        Assert.Equal(FlowchartNodeShape.Rectangle, document.Diagram.Subgraphs[0].Nodes[0].Shape);
    }

    [Theory]
    [InlineData("box", FlowchartNodeShape.Rectangle)]
    [InlineData("ellipse", FlowchartNodeShape.Stadium)]
    [InlineData("circle", FlowchartNodeShape.Circle)]
    [InlineData("doublecircle", FlowchartNodeShape.DoubleCircle)]
    [InlineData("point", FlowchartNodeShape.FilledCircle)]
    [InlineData("diamond", FlowchartNodeShape.Diamond)]
    [InlineData("hexagon", FlowchartNodeShape.Hexagon)]
    [InlineData("octagon", FlowchartNodeShape.Hexagon)]
    [InlineData("parallelogram", FlowchartNodeShape.LeanRight)]
    [InlineData("trapezium", FlowchartNodeShape.TrapezoidBaseBottom)]
    [InlineData("invtrapezium", FlowchartNodeShape.TrapezoidBaseTop)]
    [InlineData("triangle", FlowchartNodeShape.Triangle)]
    [InlineData("invtriangle", FlowchartNodeShape.FlippedTriangle)]
    [InlineData("house", FlowchartNodeShape.TrapezoidalPentagon)]
    [InlineData("cylinder", FlowchartNodeShape.Cylinder)]
    [InlineData("note", FlowchartNodeShape.TaggedRectangle)]
    [InlineData("folder", FlowchartNodeShape.Folder)]
    [InlineData("tab", FlowchartNodeShape.Folder)]
    [InlineData("box3d", FlowchartNodeShape.StackedRectangle)]
    [InlineData("component", FlowchartNodeShape.LinedRectangle)]
    [InlineData("plaintext", FlowchartNodeShape.Text)]
    [InlineData("none", FlowchartNodeShape.Text)]
    [InlineData("star", FlowchartNodeShape.Bang)]
    [InlineData("cds", FlowchartNodeShape.Odd)]
    [InlineData("Mdiamond", FlowchartNodeShape.Diamond)]
    public void Shapes(string shape, FlowchartNodeShape expected)
    {
        var document = GraphvizDocument.Parse($"digraph {{ a [shape={shape}] }}");
        Assert.Equal(expected, document.Diagram.Nodes[0].Shape);
        Assert.Empty(document.Warnings);
    }

    [Theory]
    [InlineData("3", FlowchartNodeShape.Triangle)]
    [InlineData("4", FlowchartNodeShape.Rectangle)]
    [InlineData("8", FlowchartNodeShape.Hexagon)]
    public void PolygonShapes(string sides, FlowchartNodeShape expected)
    {
        var document = GraphvizDocument.Parse($"digraph {{ a [shape=polygon, sides={sides}] }}");
        Assert.Equal(expected, document.Diagram.Nodes[0].Shape);
    }

    [Fact]
    public void UnknownShape()
    {
        var document = GraphvizDocument.Parse("digraph { a [shape=blob]; b [shape=blob] }");
        Assert.All(document.Diagram.Nodes, node => Assert.Equal(FlowchartNodeShape.Rectangle, node.Shape));
        Assert.Equal("(1, 14): The shape 'blob' is not a shape of Graphviz: the node is drawn as a box.", Assert.Single(document.Warnings).ToString());
    }

    [Fact]
    public void ToSvg()
    {
        var svg = GraphvizDocument.Parse("""
            digraph {
                subgraph cluster_x { label="X"; a -> b }
                b -> c [label="edge"];
                c -> c;
                d [shape=point];
                compound=true;
                c -> a [lhead=cluster_x];
            }
            """).Diagram.ToSvg();

        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("edge", svg, StringComparison.Ordinal);
    }
}
