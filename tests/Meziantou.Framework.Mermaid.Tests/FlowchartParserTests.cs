namespace Meziantou.Framework.Mermaid.Tests;

public sealed class FlowchartParserTests
{
    private static FlowchartDiagram Parse(string text) => (FlowchartDiagram)MermaidDocument.Parse(text).Content;

    [Theory]
    [InlineData("A[text]", FlowchartNodeShape.Rectangle)]
    [InlineData("A(text)", FlowchartNodeShape.RoundedRectangle)]
    [InlineData("A([text])", FlowchartNodeShape.Stadium)]
    [InlineData("A[[text]]", FlowchartNodeShape.FramedRectangle)]
    [InlineData("A[(text)]", FlowchartNodeShape.Cylinder)]
    [InlineData("A((text))", FlowchartNodeShape.Circle)]
    [InlineData("A(((text)))", FlowchartNodeShape.DoubleCircle)]
    [InlineData("A>text]", FlowchartNodeShape.Odd)]
    [InlineData("A{text}", FlowchartNodeShape.Diamond)]
    [InlineData("A{{text}}", FlowchartNodeShape.Hexagon)]
    [InlineData("A[/text/]", FlowchartNodeShape.LeanRight)]
    [InlineData("A[\\text\\]", FlowchartNodeShape.LeanLeft)]
    [InlineData("A[/text\\]", FlowchartNodeShape.TrapezoidBaseBottom)]
    [InlineData("A[\\text/]", FlowchartNodeShape.TrapezoidBaseTop)]
    [InlineData("A@{ shape: cyl, label: \"text\" }", FlowchartNodeShape.Cylinder)]
    [InlineData("A@{ shape: notch-pent, label: text }", FlowchartNodeShape.TrapezoidalPentagon)]
    [InlineData("A@{ shape: database, label: 'text' }", FlowchartNodeShape.Cylinder)]
    [InlineData("A[\"text\"]", FlowchartNodeShape.Rectangle)]
    [InlineData("A(\"text\")", FlowchartNodeShape.RoundedRectangle)]
    public void Shapes(string node, FlowchartNodeShape expected)
    {
        var flowchart = Parse("flowchart TD\n" + node);
        Assert.Equal(expected, flowchart.Nodes[0].Shape);
        Assert.Equal("text", flowchart.Nodes[0].Label?.Text);
    }

    [Fact]
    public void QuotesInShapeDataAreEntityCodes()
    {
        // As the lexer of Mermaid, a double quote always ends the string of the data: a quote in a label is #quot;
        Assert.Equal("6\" screen", Parse("flowchart TD\nA@{ shape: rect, label: \"6#quot; screen\" }").Nodes[0].Label?.Text);
        Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse("flowchart TD\nA@{ shape: rect, label: \"6\\\" screen\" }"));
    }

    [Theory]
    [InlineData("-->", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 1)]
    [InlineData("---", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.None, 1)]
    [InlineData("--->", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 2)]
    [InlineData("----", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.None, 2)]
    [InlineData("---->", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 3)]
    [InlineData("-.->", FlowchartLineStyle.Dotted, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 1)]
    [InlineData("-.-", FlowchartLineStyle.Dotted, FlowchartArrowHead.None, FlowchartArrowHead.None, 1)]
    [InlineData("-..->", FlowchartLineStyle.Dotted, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 2)]
    [InlineData("==>", FlowchartLineStyle.Thick, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 1)]
    [InlineData("===", FlowchartLineStyle.Thick, FlowchartArrowHead.None, FlowchartArrowHead.None, 1)]
    [InlineData("===>", FlowchartLineStyle.Thick, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 2)]
    [InlineData("~~~", FlowchartLineStyle.Invisible, FlowchartArrowHead.None, FlowchartArrowHead.None, 1)]
    [InlineData("--o", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Circle, 1)]
    [InlineData("--x", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Cross, 1)]
    [InlineData("<-->", FlowchartLineStyle.Solid, FlowchartArrowHead.Arrow, FlowchartArrowHead.Arrow, 1)]
    [InlineData("o--o", FlowchartLineStyle.Solid, FlowchartArrowHead.Circle, FlowchartArrowHead.Circle, 1)]
    [InlineData("x--x", FlowchartLineStyle.Solid, FlowchartArrowHead.Cross, FlowchartArrowHead.Cross, 1)]
    [InlineData("<-.->", FlowchartLineStyle.Dotted, FlowchartArrowHead.Arrow, FlowchartArrowHead.Arrow, 1)]
    [InlineData("<==>", FlowchartLineStyle.Thick, FlowchartArrowHead.Arrow, FlowchartArrowHead.Arrow, 1)]
    [InlineData("-- text -->", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 1)]
    [InlineData("-- text ---", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.None, 1)]
    [InlineData("-. text .->", FlowchartLineStyle.Dotted, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 1)]
    [InlineData("== text ==>", FlowchartLineStyle.Thick, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 1)]
    [InlineData("<-- text -->", FlowchartLineStyle.Solid, FlowchartArrowHead.Arrow, FlowchartArrowHead.Arrow, 1)]
    [InlineData("-->|text|", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Arrow, 1)]
    [InlineData("---|\"text\"|", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.None, 1)]
    public void Edges(string link, FlowchartLineStyle lineStyle, FlowchartArrowHead start, FlowchartArrowHead end, int length)
    {
        var flowchart = Parse("flowchart LR\nA " + link + " B");
        var edge = Assert.Single(flowchart.Edges);
        Assert.Equal((lineStyle, start, end, length), (edge.LineStyle, edge.StartArrowHead, edge.EndArrowHead, edge.Length));
        Assert.Equal(link.Contains("text", StringComparison.Ordinal) ? "text" : null, edge.Label?.Text);
        Assert.Equal(["A", "B"], flowchart.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void Chains()
    {
        var flowchart = Parse("flowchart LR\nA & B --> C & D --> E; F-->G");
        Assert.Equal(["A->C", "A->D", "B->C", "B->D", "C->E", "D->E", "F->G"], flowchart.Edges.Select(edge => edge.Source.Id + "->" + edge.Target.Id));
    }

    [Fact]
    public void Subgraphs()
    {
        var flowchart = Parse("""
            flowchart TB
                c1-->a2
                subgraph one
                    a1-->a2
                end
                subgraph two [The second]
                    direction LR
                    b1-->b2
                    subgraph three
                        b3
                    end
                end
                subgraph "With spaces"
                    c2
                end
                one --> two
            """);
        Assert.Equal(["one", "two", "subGraph3"], flowchart.Subgraphs.Select(subgraph => subgraph.Id));
        Assert.Equal(["a1", "a2"], flowchart.Subgraphs[0].Nodes.Select(node => node.Id));
        Assert.Equal("The second", flowchart.Subgraphs[1].Label?.Text);
        Assert.Equal(DiagramDirection.LeftToRight, flowchart.Subgraphs[1].Direction);
        Assert.Equal("three", Assert.Single(flowchart.Subgraphs[1].Subgraphs).Id);
        Assert.Equal("With spaces", flowchart.Subgraphs[2].Label?.Text);
        Assert.Equal(["c1"], flowchart.Nodes.Select(node => node.Id));
        Assert.IsType<FlowchartSubgraph>(flowchart.Edges[^1].Source);
        Assert.IsType<FlowchartSubgraph>(flowchart.Edges[^1].Target);
    }

    [Fact]
    public void StylesAndClasses()
    {
        var flowchart = Parse("""
            flowchart LR
                A:::warm --> B
                classDef warm fill:#f96,stroke:#333,stroke-width:4px,color:#fff,font-weight:bold
                classDef cold,frozen fill:rgb(0, 100, 255),stroke-dasharray: 5 5
                class B cold
                style A fill:#f9f,font-style:italic,font-size:12px,opacity:0.5
                linkStyle 0 stroke:#ff3,stroke-width:4px,color:red
                linkStyle default interpolate linear stroke:#999
            """);
        Assert.Equal((SceneColor)"#ff9966", flowchart.ClassDefinitions["warm"].Fill);
        Assert.Equal(4, flowchart.ClassDefinitions["warm"].StrokeWidth);
        Assert.True(flowchart.ClassDefinitions["warm"].Bold);
        Assert.Equal((SceneColor)"#0064ff", flowchart.ClassDefinitions["frozen"].Fill);
        Assert.Equal([5.0, 5.0], flowchart.ClassDefinitions["cold"].StrokeDashArray);
        Assert.Equal(["warm"], flowchart.Nodes[0].CssClasses);
        Assert.Equal(["cold"], flowchart.Nodes[1].CssClasses);
        Assert.Equal((SceneColor)"#ff99ff", flowchart.Nodes[0].Style?.Fill);
        Assert.Equal((true, 12.0, 0.5), (flowchart.Nodes[0].Style!.Italic, flowchart.Nodes[0].Style!.FontSize, flowchart.Nodes[0].Style!.Opacity));
        Assert.Equal((SceneColor)"#ffff33", flowchart.Edges[0].Style?.Stroke);
        Assert.Equal((SceneColor)"#ff0000", flowchart.Edges[0].Style?.TextColor);
        Assert.Equal((SceneColor)"#999999", flowchart.DefaultEdgeStyle?.Stroke);
        Assert.Equal(EdgeCurve.Linear, flowchart.Curve);
    }

    [Fact]
    public void TextDecoding()
    {
        var flowchart = Parse("""
            flowchart LR
                A["A #quot;quoted#quot; text #9829; &amp; more"]
                B["first<br>second<br/>third"]
                C["`**bold** and _italic_`"]
                D["<b>html</b> <i>tags</i> <span>removed</span>"]
            """);
        Assert.Equal("A \"quoted\" text ♥ & more", flowchart.Nodes[0].Label?.Text);
        Assert.Equal("first\nsecond\nthird", flowchart.Nodes[1].Label?.Text);
        Assert.Equal(DiagramText.Markdown("**bold** and _italic_"), flowchart.Nodes[2].Label);
        Assert.Equal(DiagramText.Markdown("**html** *tags* removed"), flowchart.Nodes[3].Label);
    }

    [Fact]
    public void EdgeIdsAndData()
    {
        var flowchart = Parse("""
            flowchart LR
                A e1@--> B
                e1@{ curve: stepAfter }
                class e1 animated
            """);
        var edge = Assert.Single(flowchart.Edges);
        Assert.Equal("e1", edge.Id);
        Assert.Equal(EdgeCurve.StepAfter, edge.Curve);
        Assert.Equal(["animated"], edge.CssClasses);
    }

    [Fact]
    public void Clicks()
    {
        var flowchart = Parse("""
            flowchart LR
                A --> B --> C
                click A href "https://example.com" "Tooltip" _blank
                click B "https://example.com/b"
                click C call callback("arg") "Callback tooltip"
            """);
        Assert.Equal(("https://example.com", "Tooltip"), (flowchart.Nodes[0].Link, flowchart.Nodes[0].Tooltip));
        Assert.Equal("https://example.com/b", flowchart.Nodes[1].Link);
        Assert.Equal((null, "Callback tooltip"), (flowchart.Nodes[2].Link, flowchart.Nodes[2].Tooltip));
    }

    [Theory]
    [InlineData("flowchart TD\nA --> ", "(2, 7): A node is expected after the edge.")]
    [InlineData("flowchart TD\nA[text", "(2, 3): The text of the node is not closed by ']'.")]
    [InlineData("flowchart TD\nsubgraph a\nb", "(2, 1): The subgraph 'a' is not closed by 'end'.")]
    [InlineData("flowchart TD\nend", "(2, 1): 'end' closes no subgraph.")]
    [InlineData("flowchart TD\nA -- text B", "(2, 3): The edge is not closed")]
    [InlineData("flowchart TD\nA <-- text --x B", "(2, 3): The two ends of the edge have different heads.")]
    [InlineData("flowchart TD\nA -- text ==> B", "(2, 3): The edge is not closed")]
    [InlineData("flowchart TD\nA --> B\nlinkStyle 3 stroke:red", "(3, 1): The edge 3 of linkStyle does not exist")]
    [InlineData("flowchart TD\nA B", "(2, 3): Unexpected 'B'")]
    [InlineData("flowchart TD\nA@{ shape: [ }", "(2, 3): The data is not a valid YAML mapping")]
    [InlineData("flowchart TD\nA[\"text]", "(2, 3): The string is not closed.")]
    public void InvalidFlowcharts(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.StartsWith(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEverything()
    {
        var flowchart = new FlowchartDiagram { Title = "A \"title\"", Direction = DiagramDirection.BottomToTop, Curve = EdgeCurve.MonotoneX, NodeSpacing = 30, AccessibleTitle = "Accessible", Description = "First\nSecond", Theme = DiagramTheme.Dark };
        flowchart.ClassDefinitions["hot"] = new DiagramStyle { Fill = (SceneColor)"#ff000080", StrokeDashArray = [2, 3], Bold = true, Italic = false, FontSize = 12, Opacity = 0.5, TextColor = SceneColor.White, Stroke = SceneColor.Black, StrokeWidth = 1.5 };
        var group = flowchart.AddSubgraph("group", DiagramText.Markdown("**Group**"));
        group.Direction = DiagramDirection.RightToLeft;
        group.Style = new DiagramStyle { Fill = SceneColor.White };
        var inner = new FlowchartNode("inner", "Inner <tag> & #hash", FlowchartNodeShape.Cloud) { Link = "https://example.com/?a=1&b=2", Tooltip = "Tip \"quoted\"" };
        inner.CssClasses.Add("hot");
        group.Nodes.Add(inner);
        var plain = flowchart.AddNode("plain", shape: FlowchartNodeShape.Hexagon);
        var edge = flowchart.AddEdge(plain, inner, "label\nsecond line");
        edge.Id = "e1";
        edge.CssClasses.Add("hot");
        edge.Curve = EdgeCurve.Step;
        edge.Style = new DiagramStyle { Stroke = SceneColor.Black };
        flowchart.AddEdge(plain, group).LineStyle = FlowchartLineStyle.Invisible;
        var thick = flowchart.AddEdge(inner, plain);
        thick.LineStyle = FlowchartLineStyle.Thick;
        thick.StartArrowHead = FlowchartArrowHead.Cross;
        thick.EndArrowHead = FlowchartArrowHead.Cross;
        thick.Length = 3;
        flowchart.DefaultEdgeStyle = new DiagramStyle { Stroke = SceneColor.White };

        var text = MermaidWriter.Write(flowchart);
        var parsed = MermaidDocument.Parse(text);
        Assert.Empty(parsed.Warnings);
        var result = (FlowchartDiagram)parsed.Content;
        Assert.Equal(text, MermaidWriter.Write(result));
        Assert.Equal(flowchart.Title, result.Title);
        Assert.Same(DiagramTheme.Dark, result.Theme);
        Assert.Equal(flowchart.Description, result.Description);
        var resultInner = (FlowchartNode)result.Find("inner")!;
        Assert.Equal(inner.Label, resultInner.Label);
        Assert.Equal(inner.Link, resultInner.Link);
        Assert.Equal(inner.Tooltip, resultInner.Tooltip);
        Assert.Equal(FlowchartNodeShape.Cloud, resultInner.Shape);
        Assert.Equal(flowchart.ClassDefinitions["hot"], result.ClassDefinitions["hot"] with { StrokeDashArray = flowchart.ClassDefinitions["hot"].StrokeDashArray });
        Assert.Equal([2.0, 3.0], result.ClassDefinitions["hot"].StrokeDashArray);
        Assert.Equal(edge.Label, result.Edges[0].Label);
        Assert.Equal((EdgeCurve?)EdgeCurve.Step, result.Edges[0].Curve);
        Assert.Equal(["hot"], result.Edges[0].CssClasses);
        Assert.Equal((FlowchartLineStyle.Thick, FlowchartArrowHead.Cross, 3), (result.Edges[2].LineStyle, result.Edges[2].StartArrowHead, result.Edges[2].Length));
        Assert.IsType<FlowchartSubgraph>(result.Edges[1].Target);
    }
}
