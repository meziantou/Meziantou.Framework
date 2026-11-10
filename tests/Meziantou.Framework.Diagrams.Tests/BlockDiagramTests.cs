namespace Meziantou.Framework.Diagrams.Tests;

public sealed class BlockDiagramTests
{
    [Fact]
    public void BlockDiagram()
    {
        #region Diagrams_BlockDiagram
        var diagram = new BlockDiagram { Columns = 3 };
        diagram.AddBlock("frontend", "Frontend");
        diagram.Items.Add(new BlockDiagramArrow("toBackend", "API", BlockArrowDirections.Right));
        var backend = diagram.AddBlock("backend", "Backend");
        diagram.Items.Add(new BlockDiagramSpace { Span = 2 });
        diagram.Items.Add(new BlockDiagramArrow("down", " ", BlockArrowDirections.Down));

        // A composite block spans 3 columns, with its own grid
        var storage = new BlockDiagramComposite("storage", "Storage") { Span = 3, Columns = 2 };
        var database = new BlockDiagramBlock("database", "Database") { Shape = FlowchartNodeShape.Cylinder };
        var cache = new BlockDiagramBlock("cache", "Cache") { Shape = FlowchartNodeShape.RoundedRectangle };
        storage.Items.Add(database);
        storage.Items.Add(cache);
        diagram.Items.Add(storage);

        diagram.AddEdge(backend, database, "SQL");
        #endregion

        Assert.Same(cache, diagram.Find("cache"));
        Assert.Equal(["frontend", "toBackend", "backend", "down", "storage", "database", "cache"], diagram.EnumerateBlocks().Select(block => block.Id));
        DiagramAssert.Svg(diagram, "BlockDiagram");
    }

    [Fact]
    public void ShapesArrowsAndEdges()
    {
        var diagram = new BlockDiagram();
        diagram.ClassDefinitions["blue"] = new DiagramStyle { Fill = (SceneColor)"#6e6ce6" };
        var circle = diagram.AddBlock("circle", "Circle", FlowchartNodeShape.Circle);
        var diamond = diagram.AddBlock("diamond", null, FlowchartNodeShape.Diamond);
        diamond.CssClasses.Add("blue");
        var hexagon = diagram.AddBlock("hexagon", DiagramText.Markdown("**Hex**"), FlowchartNodeShape.Hexagon);
        hexagon.Style = new DiagramStyle { Stroke = (SceneColor)"#ff0000" };
        diagram.Items.Add(new BlockDiagramArrow("both", "x", BlockArrowDirections.Left | BlockArrowDirections.Right | BlockArrowDirections.Down));
        diagram.Items.Add(new BlockDiagramComposite("empty"));
        diagram.AddEdge(circle, diamond).LineStyle = FlowchartLineStyle.Dotted;
        var thick = diagram.AddEdge(diamond, hexagon, "thick");
        thick.LineStyle = FlowchartLineStyle.Thick;
        thick.StartArrowHead = FlowchartArrowHead.Arrow;
        diagram.AddEdge(circle, hexagon).LineStyle = FlowchartLineStyle.Invisible;

        var svg = diagram.ToSvg();
        Assert.Contains("fill=\"#6e6ce6\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#ff0000\"", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void SpansAndColumnsArePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new BlockDiagramSpace { Span = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new BlockDiagram { Columns = 0 });
    }
}
