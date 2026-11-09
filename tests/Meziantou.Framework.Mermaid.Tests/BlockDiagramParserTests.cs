namespace Meziantou.Framework.Mermaid.Tests;

public sealed class BlockDiagramParserTests
{
    private static BlockDiagram Parse(string text) => (BlockDiagram)MermaidDocument.Parse(text).Content;

    [Fact]
    public void BlocksColumnsAndComposites()
    {
        #region Mermaid_BlockDiagram
        var diagram = (BlockDiagram)MermaidDocument.Parse("""
            block
              columns 3
              frontend["Frontend"] api<["API"]>(right) backend["Backend"]
              space:2 down<[" "]>(down)
              block:storage:3
                columns 2
                database[("Database")] cache("Cache")
              end
              backend --> database
            """).Content;
        #endregion

        Assert.Equal(3, diagram.Columns);
        Assert.Equal(["frontend", "api", "backend", null, "down", "storage"], diagram.Items.Select(item => (item as BlockDiagramBlock)?.Id));
        Assert.Equal(2, diagram.Items[3].Span);
        Assert.Equal(BlockArrowDirections.Right, ((BlockDiagramArrow)diagram.Items[1]).Directions);
        var storage = (BlockDiagramComposite)diagram.Items[5];
        Assert.Equal((3, 2), (storage.Span, storage.Columns));
        Assert.Equal(FlowchartNodeShape.Cylinder, ((BlockDiagramBlock)storage.Items[0]).Shape);
        var edge = Assert.Single(diagram.Edges);
        Assert.Equal(("backend", "database", FlowchartArrowHead.Arrow), (edge.From.Id, edge.To.Id, edge.EndArrowHead));
    }

    [Theory]
    [InlineData("a --> b", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Arrow)]
    [InlineData("a --- b", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.None)]
    [InlineData("a<-->b", FlowchartLineStyle.Solid, FlowchartArrowHead.Arrow, FlowchartArrowHead.Arrow)]
    [InlineData("a --x b", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Cross)]
    [InlineData("a --o b", FlowchartLineStyle.Solid, FlowchartArrowHead.None, FlowchartArrowHead.Circle)]
    [InlineData("a ==> b", FlowchartLineStyle.Thick, FlowchartArrowHead.None, FlowchartArrowHead.Arrow)]
    [InlineData("a -.-> b", FlowchartLineStyle.Dotted, FlowchartArrowHead.None, FlowchartArrowHead.Arrow)]
    [InlineData("a ~~~ b", FlowchartLineStyle.Invisible, FlowchartArrowHead.None, FlowchartArrowHead.None)]
    public void Edges(string statement, FlowchartLineStyle lineStyle, FlowchartArrowHead start, FlowchartArrowHead end)
    {
        var edge = Assert.Single(Parse("block\n" + statement).Edges);
        Assert.Equal((lineStyle, start, end), (edge.LineStyle, edge.StartArrowHead, edge.EndArrowHead));
    }

    [Fact]
    public void ShapesLabelsAndStyles()
    {
        var diagram = Parse("""
            block
              a(("Circle")) b{{"Hexagon"}} c[/"Lean"/] d[\"Lean"\] e[/"Trapezoid"\] f[\"Trapezoid"/]
              g>"Odd"] h{"Diamond"} i[["Sub"]] j((("Double"))) k(["Stadium"]) l["`**bold**`"]
              a-- "label" -->b-->c
              block
                m
              end
              classDef blue fill:#6e6ce6,stroke:#333;
              class a,b blue
              style c fill:#bbf
            """);
        Assert.Equal(
            [FlowchartNodeShape.Circle, FlowchartNodeShape.Hexagon, FlowchartNodeShape.LeanRight, FlowchartNodeShape.LeanLeft, FlowchartNodeShape.TrapezoidBaseBottom, FlowchartNodeShape.TrapezoidBaseTop, FlowchartNodeShape.Odd, FlowchartNodeShape.Diamond, FlowchartNodeShape.FramedRectangle, FlowchartNodeShape.DoubleCircle, FlowchartNodeShape.Stadium, FlowchartNodeShape.Rectangle],
            diagram.Items.Take(12).Select(item => ((BlockDiagramBlock)item).Shape));
        Assert.Equal(DiagramText.Markdown("**bold**"), ((BlockDiagramBlock)diagram.Items[11]).Label);
        Assert.Equal("label", diagram.Edges[0].Label?.Text);
        Assert.Equal(("b", "c"), (diagram.Edges[1].From.Id, diagram.Edges[1].To.Id));
        Assert.Equal("m", ((BlockDiagramBlock)Assert.IsType<BlockDiagramComposite>(diagram.Items[12]).Items[0]).Id);
        Assert.Equal(["blue"], diagram.Find("a")!.CssClasses);
        Assert.Equal((SceneColor)"#bbbbff", diagram.Find("c")!.Style?.Fill);
    }

    [Theory]
    [InlineData("block\nblock\na", "The composite block 'block-1' is not closed by 'end'.")]
    [InlineData("block\nend", "(2, 1): 'end' closes no composite block.")]
    [InlineData("block\na[\"\"]", "The text of a block cannot be empty.")]
    [InlineData("block\na[label]", "the text of a block is quoted.")]
    [InlineData("block\na<[\"x\"]>(north)", "'north' is not a direction")]
    [InlineData("block\ncolumns many", "'columns' is followed by a number of columns")]
    [InlineData("block\na --> end", "'end' is a keyword and cannot be a block.")]
    public void InvalidBlockDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new BlockDiagram { Columns = 2 };
        var a = diagram.AddBlock("a", "The \"first\"", FlowchartNodeShape.RoundedRectangle);
        a.Span = 2;
        var b = diagram.AddBlock("b");
        var composite = new BlockDiagramComposite("group", "Group") { Columns = 1 };
        composite.Items.Add(new BlockDiagramSpace { Span = 3 });
        composite.Items.Add(new BlockDiagramArrow("arrow", "Go", BlockArrowDirections.Left | BlockArrowDirections.Right | BlockArrowDirections.Up));
        diagram.Items.Add(composite);
        var anonymous = new BlockDiagramComposite("not valid");
        anonymous.Items.Add(new BlockDiagramBlock("inner", DiagramText.Markdown("*md*")));
        diagram.Items.Add(anonymous);
        var edge = diagram.AddEdge(a, b, "uses");
        edge.LineStyle = FlowchartLineStyle.Dotted;
        edge.StartArrowHead = FlowchartArrowHead.Arrow;
        diagram.AddEdge(b, composite).LineStyle = FlowchartLineStyle.Thick;
        diagram.ClassDefinitions["hot"] = new DiagramStyle { Fill = (SceneColor)"#ff0000" };
        b.CssClasses.Add("hot");
        b.Style = new DiagramStyle { Stroke = SceneColor.Black };

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (BlockDiagram)parsed.Content;
        var resultA = result.Find("a")!;
        Assert.Equal(("The \"first\"", FlowchartNodeShape.RoundedRectangle, 2), (resultA.Label?.Text, resultA.Shape, resultA.Span));
        var resultGroup = (BlockDiagramComposite)result.Find("group")!;
        Assert.Equal(("Group", 1, 3), (resultGroup.Label?.Text, resultGroup.Columns, resultGroup.Items[0].Span));
        Assert.Equal(BlockArrowDirections.Left | BlockArrowDirections.Right | BlockArrowDirections.Up, ((BlockDiagramArrow)result.Find("arrow")!).Directions);
        Assert.Equal(DiagramText.Markdown("*md*"), result.Find("inner")!.Label);
        Assert.Equal((FlowchartLineStyle.Dotted, FlowchartArrowHead.Arrow, "uses"), (result.Edges[0].LineStyle, result.Edges[0].StartArrowHead, result.Edges[0].Label?.Text));
        Assert.Equal(["hot"], result.Find("b")!.CssClasses);
    }
}
