using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class QuadrantChartDiagramTests
{
    [Fact]
    public void QuadrantChartDiagram()
    {
        #region Diagrams_QuadrantChartDiagram
        var diagram = new QuadrantChartDiagram
        {
            Title = "Reach and engagement of campaigns",
            XAxisLeftLabel = "Low Reach",
            XAxisRightLabel = "High Reach",
            YAxisBottomLabel = "Low Engagement",
            YAxisTopLabel = "High Engagement",
            Quadrant1Label = "We should expand",
            Quadrant2Label = "Need to promote",
            Quadrant3Label = "Re-evaluate",
            Quadrant4Label = "May be improved",
        };
        diagram.AddPoint("Campaign A", 0.3, 0.6);
        diagram.AddPoint("Campaign B", 0.45, 0.23);
        diagram.AddPoint("Campaign C", 0.57, 0.69).Style.Color = SceneColor.Parse("#ff3300");
        diagram.AddPoint("Campaign D", 0.78, 0.34).ClassName = "important";
        diagram.ClassDefinitions["important"] = new QuadrantChartPointStyle { Radius = 10, Color = SceneColor.Parse("#109060") };
        #endregion

        Assert.HasCount(4, diagram.Points);
        Assert.Equal("Campaign D", diagram.Points[3].ToString());
        DiagramAssert.Svg(diagram, "QuadrantChartDiagram");
    }

    [Fact]
    public void WithoutPoints()
    {
        // The texts of the quadrants are in their middle, and the x axis is at the top by default
        var diagram = new QuadrantChartDiagram
        {
            XAxisLeftLabel = "Urgent",
            XAxisRightLabel = "Not Urgent",
            YAxisBottomLabel = "Not Important",
            YAxisTopLabel = "Important",
            Quadrant1Label = "Plan",
            Quadrant2Label = "Do",
            Quadrant3Label = "Delegate",
            Quadrant4Label = "Delete",
            Width = 400,
            Height = 400,
        };
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void SingleAxisTextsStylesAndDarkTheme()
    {
        var diagram = new QuadrantChartDiagram
        {
            Theme = DiagramTheme.Dark,
            XAxisLeftLabel = "Effort \u27F6",
            YAxisBottomLabel = "Value",
            YAxisPosition = QuadrantChartYAxisPosition.Right,
            Quadrant1Label = "**Quick wins**",
        };
        diagram.Colors.Quadrant1Fill = SceneColor.Parse("#336633");
        diagram.Colors.PointTextFill = SceneColor.Parse("#ffcc00");
        var point = diagram.AddPoint("Bordered", 0, 1);
        point.Style.Radius = 25;
        point.Style.StrokeColor = SceneColor.Parse("#10f0f0");
        point.Style.StrokeWidth = 5;
        diagram.AddPoint("Huge radius is clipped", 1, 0).Style.Radius = 400;
        diagram.AddPoint(new DiagramText("*markdown*", DiagramTextFormat.Markdown), 0.5, 0.5).ClassName = "missing";
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void EmptyAndZeroSizedDiagrams()
    {
        DiagramAssert.Svg(new QuadrantChartDiagram());
        var zero = new QuadrantChartDiagram { Width = 0, Height = 0, XAxisLeftLabel = "x" };
        zero.AddPoint("p", 0.5, 0.5);
        var scene = zero.CreateScene();
        Assert.True(double.IsFinite(scene.Width) && scene.Width > 0);
    }

    [Fact]
    public void InvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>("x", () => new QuadrantChartPoint("p", 1.5, 0));
        Assert.Throws<ArgumentOutOfRangeException>("y", () => new QuadrantChartPoint("p", 0, double.NaN));
        Assert.Throws<ArgumentNullException>("label", () => new QuadrantChartPoint(null!, 0, 0));
        var point = new QuadrantChartPoint("p", 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => point.X = -0.1);
        Assert.Throws<ArgumentNullException>("value", () => point.Label = null!);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => point.Style.Radius = -1);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => point.Style.StrokeWidth = 2_000_000);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new QuadrantChartDiagram { Width = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new QuadrantChartDiagram { PointLabelFontSize = double.PositiveInfinity });
    }

    [Fact]
    public void AdjustAddsToTheChannels()
    {
        Assert.Equal(SceneColor.Parse("#f1f1ff"), QuadrantChartDiagramRenderer.Adjust(SceneColor.Parse("#ECECFF"), 5));
        Assert.Equal(SceneColor.Parse("#0e0e00"), QuadrantChartDiagramRenderer.Adjust(SceneColor.Parse("#131300"), -5));
    }
}
