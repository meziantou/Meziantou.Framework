using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class SankeyDiagramTests
{
    [Fact]
    public void SankeyDiagram()
    {
        #region Diagrams_SankeyDiagram
        var diagram = new SankeyDiagram { Title = "Energy flows (TWh)", Height = 300 };
        diagram.AddLink("Solar", "Electricity", 60);
        diagram.AddLink("Wind", "Electricity", 90);
        diagram.AddLink("Gas", "Electricity", 120);
        diagram.AddLink("Gas", "Heating", 80);
        diagram.AddLink("Electricity", "Homes", 110);
        diagram.AddLink("Electricity", "Industry", 140);
        diagram.AddLink("Electricity", "Losses", 20);
        diagram.AddLink("Heating", "Homes", 80);
        #endregion

        Assert.Equal(["Solar", "Electricity", "Wind", "Gas", "Heating", "Homes", "Industry", "Losses"], diagram.GetNodes());
        DiagramAssert.Svg(diagram, "SankeyDiagram");
    }

    [Theory]
    [InlineData(SankeyLinkColoring.Source, SankeyNodeAlignment.Left)]
    [InlineData(SankeyLinkColoring.Target, SankeyNodeAlignment.Right)]
    [InlineData(SankeyLinkColoring.Gradient, SankeyNodeAlignment.Center)]
    public void ColoringAndAlignment(SankeyLinkColoring coloring, SankeyNodeAlignment alignment)
    {
        var diagram = CreateDiagram();
        diagram.LinkColoring = coloring;
        diagram.NodeAlignment = alignment;
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void OutlinedLabelsAndOptions()
    {
        var diagram = CreateDiagram();
        diagram.LabelStyle = SankeyLabelStyle.Outlined;
        diagram.LinkColor = SceneColor.Parse("#a1a1a1");
        diagram.NodeColors["B"] = SceneColor.Parse("#ff0000");
        diagram.ValuePrefix = "$";
        diagram.ValueSuffix = " M";
        diagram.NodeWidth = 20;
        diagram.NodePadding = 30;
        diagram.Width = 500;
        diagram.Theme = DiagramTheme.Dark;
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void WithoutValues()
    {
        var diagram = CreateDiagram();
        diagram.ShowValues = false;
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void LinksThatCloseACycleAreNotDrawn()
    {
        var diagram = new SankeyDiagram();
        diagram.AddLink("A", "B", 10);
        diagram.AddLink("B", "A", 5);
        diagram.AddLink("C", "C", 1);
        var layout = SankeyLayout.Compute(diagram);
        Assert.Equal([("A", "B")], layout.Links.Select(link => (link.Source.Name, link.Target.Name)));
        Assert.Equal(["A", "B", "C"], layout.Nodes.Select(node => node.Name));
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void ExtremeValuesStayFinite()
    {
        var diagram = new SankeyDiagram();
        diagram.AddLink("A", "B", double.Epsilon);
        diagram.AddLink("A", "C", 1e300);
        diagram.AddLink("D", "E", 0);
        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);

        var zeros = new SankeyDiagram();
        zeros.AddLink("A", "B", 0);
        Assert.DoesNotContain("NaN", zeros.ToSvg(), StringComparison.Ordinal);
        DiagramAssert.Svg(new SankeyDiagram());
    }

    [Fact]
    public void LayoutIsTheOneOfD3Sankey()
    {
        // The nodes that d3-sankey 0.12.3 gives Mermaid for the same links, at the default size, without the values
        var diagram = new SankeyDiagram { ShowValues = false };
        diagram.AddLink("A", "B", 3);
        diagram.AddLink("A", "C", 1);
        diagram.AddLink("B", "D", 2);
        diagram.AddLink("C", "D", 1);
        diagram.AddLink("B", "E", 1);
        var layout = SankeyLayout.Compute(diagram);
        Assert.Equal(
        [
            ("A", 0, 0, 2.669943, 390.669943), ("B", 1, 295, 0, 291), ("C", 1, 295, 303, 400), ("D", 2, 590, 0, 291), ("E", 2, 590, 303, 400),
        ], layout.Nodes.Select(node => (node.Name, node.Layer, node.X0, Math.Round(node.Y0, 6, MidpointRounding.AwayFromZero), Math.Round(node.Y1, 6, MidpointRounding.AwayFromZero))));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(0.1, "0.1")]
    [InlineData(124.73, "124.73")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(0.0000015, "0.0000015")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    [InlineData(double.Epsilon, "5e-324")]
    [InlineData(double.MaxValue, "1.7976931348623157e+308")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(-12.5, "-12.5")]
    public void NumbersAreWrittenAsJavaScript(double value, string expected)
    {
        Assert.Equal(expected, SankeyJavaScriptNumber.ToString(value));
    }

    [Fact]
    public void LinksAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyLink("A", "B", -1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyLink("A", "B", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyLink("A", "B", 1) { Value = 1e301 });
        Assert.Throws<ArgumentNullException>("source", () => new SankeyLink(null!, "B", 1));
        Assert.Throws<ArgumentNullException>("value", () => new SankeyLink("A", "B", 1) { Target = null! });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyDiagram { Width = 0 });
        Assert.Throws<ArgumentNullException>("value", () => new SankeyDiagram { ValuePrefix = null! });
    }

    private static SankeyDiagram CreateDiagram()
    {
        var diagram = new SankeyDiagram();
        diagram.AddLink("A", "B", 30);
        diagram.AddLink("A", "C", 10);
        diagram.AddLink("B", "D", 25);
        diagram.AddLink("C", "D", 5);
        diagram.AddLink("C", "E", 5);
        diagram.AddLink("F", "E", 12.345);
        return diagram;
    }
}
