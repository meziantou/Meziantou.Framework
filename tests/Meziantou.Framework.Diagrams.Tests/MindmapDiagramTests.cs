namespace Meziantou.Framework.Diagrams.Tests;

public sealed class MindmapDiagramTests
{
    [Fact]
    public void MindmapDiagram()
    {
        #region Diagrams_MindmapDiagram
        var diagram = new MindmapDiagram();
        var root = diagram.SetRoot("Diagrams", MindmapNodeShape.Circle);
        var graphs = root.AddChild("Graphs", MindmapNodeShape.Hexagon);
        graphs.AddChild("Flowcharts");
        graphs.AddChild("Class diagrams");
        graphs.AddChild("State diagrams");
        var timelines = root.AddChild("Timelines", MindmapNodeShape.RoundedSquare);
        timelines.AddChild("Gantt charts");
        timelines.AddChild("Sequence diagrams");
        var charts = root.AddChild(DiagramText.Markdown("**Charts**"), MindmapNodeShape.Square);
        charts.AddChild("Pie", MindmapNodeShape.Cloud);
        charts.AddChild("Sankey", MindmapNodeShape.Bang);
        #endregion

        Assert.Equal(["Diagrams", "Graphs", "Flowcharts", "Class diagrams", "State diagrams", "Timelines", "Gantt charts", "Sequence diagrams", "**Charts**", "Pie", "Sankey"], diagram.EnumerateNodes().Select(node => node.Label.Text));
        DiagramAssert.Svg(diagram, "MindmapDiagram");
    }

    [Fact]
    public void ColorsByDepthAndClasses()
    {
        var diagram = new MindmapDiagram { ColorBy = MindmapColorMode.Depth, Theme = DiagramTheme.Dark, MaxNodeWidth = 80, NodePadding = 6 };
        var root = diagram.SetRoot("Root");
        root.CssClasses.Add("important");
        var child = root.AddChild("A long text that wraps on several lines");
        child.Icon = "fa fa-book";
        child.AddChild("Leaf").AddChild("Deeper").AddChild("Deepest").AddChild("Bottom");
        root.AddChild("B");
        root.AddChild("C");

        var svg = diagram.ToSvg();
        Assert.Contains("important", svg, StringComparison.Ordinal);
        Assert.Contains("edge-depth-4", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void EmptyMindmap()
    {
        var diagram = new MindmapDiagram();
        Assert.Empty(diagram.EnumerateNodes());
        Assert.StartsWith("<svg", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void SizesArePositive()
    {
        var diagram = new MindmapDiagram();
        Assert.Throws<ArgumentOutOfRangeException>("value", () => diagram.MaxNodeWidth = 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => diagram.NodePadding = -1);
    }
}
