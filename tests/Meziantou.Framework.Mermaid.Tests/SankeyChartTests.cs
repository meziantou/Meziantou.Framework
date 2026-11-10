using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.SankeyDiagrams;
using Meziantou.Framework.Mermaid.Internals.Charts;

namespace Meziantou.Framework.Mermaid.Tests;

public sealed class SankeyChartTests
{
    [Fact]
    public void SankeyChart()
    {
        #region Mermaid_SankeyChart
        var diagram = new SankeyChart { Title = "Energy flows (TWh)", Height = 300 };
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
        ChartAssert.Svg(diagram, "SankeyChart");
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
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void LinksTakeTheColorOfTheirSourceOrTarget()
    {
        var red = SceneColor.Parse("#ff0000");
        var blue = SceneColor.Parse("#0000ff");
        var diagram = new SankeyChart();
        diagram.AddLink("A", "B", 10);
        diagram.NodeColors["A"] = red;
        diagram.NodeColors["B"] = blue;
        diagram.LinkColoring = SankeyLinkColoring.Target;
        Assert.Equal(blue.WithOpacity(0.5), GetLinkColor(diagram));

        diagram.LinkColoring = SankeyLinkColoring.Source;
        Assert.Equal(red.WithOpacity(0.5), GetLinkColor(diagram));

        // A gradient cannot be drawn: the links take the color of their source
        diagram.LinkColoring = SankeyLinkColoring.Gradient;
        Assert.Equal(red.WithOpacity(0.5), GetLinkColor(diagram));

        diagram.LinkColor = SceneColor.Parse("#00ff00");
        Assert.Equal(SceneColor.Parse("#00ff00").WithOpacity(0.5), GetLinkColor(diagram));
        Assert.Equal([red, blue], GetElements<SceneRectangle>(diagram.CreateScene()).Select(node => GetColor(node.Fill)));
    }

    [Fact]
    public void NodesTakeTheColorsOfTableau10()
    {
        var diagram = new SankeyChart();
        diagram.AddLink("A", "B", 10);
        diagram.AddLink("B", "C", 10);
        diagram.NodeColors["B"] = SceneColor.Parse("#123456");
        Assert.Equal(
            [SceneColor.Parse("#4e79a7"), SceneColor.Parse("#123456"), SceneColor.Parse("#f28e2c")],
            GetElements<SceneRectangle>(diagram.CreateScene()).Select(node => GetColor(node.Fill)));
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
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void LabelsShowTheValuesAsMermaid()
    {
        var diagram = CreateDiagram();
        diagram.ValuePrefix = "$";
        diagram.ValueSuffix = " M";
        var scene = diagram.CreateScene();
        Assert.Equal(["A $40 M", "B $30 M", "C $10 M", "D $30 M", "E $17.35 M", "F $12.35 M"], GetElements<SceneText>(scene).Select(text => text.Text));
        Assert.Equal("A → B: $30 M", GetElements<ScenePath>(scene).First().Tooltip);

        diagram.ShowValues = false;
        Assert.Equal(["A", "B", "C", "D", "E", "F"], GetElements<SceneText>(diagram.CreateScene()).Select(text => text.Text));
    }

    [Fact]
    public void WithoutValues()
    {
        var diagram = CreateDiagram();
        diagram.ShowValues = false;
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void AreaIsWidthByHeight()
    {
        var diagram = new SankeyChart { Width = 300, Height = 200, NodeWidth = 20, NodePadding = 5, ShowValues = false };
        diagram.AddLink("A", "B", 1);
        var plot = diagram.ToPlot();
        Assert.Equal(320, plot.Width);
        Assert.Equal(220, plot.Height);
        Assert.Equal(typeof(SankeyMark<>), Assert.Single(plot.Marks).GetType().GetGenericTypeDefinition());
        Assert.Equal([(10.0, 10.0, 20.0, 200.0), (290.0, 10.0, 20.0, 200.0)], GetElements<SceneRectangle>(diagram.CreateScene()).Select(node => (node.X, node.Y, node.Width, node.Height)));

        diagram.Title = "Title";
        plot = diagram.ToPlot();
        Assert.Equal(320, plot.Width);
        Assert.True(plot.Height > 220);
        var nodes = GetElements<SceneRectangle>(diagram.CreateScene());
        Assert.Equal(200, nodes[0].Height);
        Assert.Equal(plot.Height - 10, nodes[0].Y + nodes[0].Height);
    }

    [Fact]
    public void LinksThatCloseACycleAreNotDrawn()
    {
        var diagram = new SankeyChart { ShowValues = false };
        diagram.AddLink("A", "B", 10);
        diagram.AddLink("B", "A", 5);
        diagram.AddLink("C", "C", 1);
        diagram.AddLink("B", "D", 3);
        Assert.Equal(["A", "B", "C", "D"], diagram.GetNodes());
        var scene = diagram.CreateScene();
        Assert.Equal(["A → B: 10", "B → D: 3"], GetElements<ScenePath>(scene).Select(link => link.Tooltip));
        Assert.Equal(["A", "B", "D"], GetElements<SceneText>(scene).Select(text => text.Text));
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void OnlyACycle()
    {
        var diagram = new SankeyChart();
        diagram.AddLink("A", "A", 10);
        Assert.Empty(GetElements<ScenePath>(diagram.CreateScene()));
        Assert.DoesNotContain("NaN", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatedLinksAndEmptyNames()
    {
        var diagram = new SankeyChart();
        diagram.AddLink("A", "B", 10);
        diagram.AddLink("A", "B", 5);
        diagram.AddLink("", "B", 1);
        diagram.AddLink("B", "1", 2);
        var scene = diagram.CreateScene();
        Assert.HasCount(4, GetElements<ScenePath>(scene));
        Assert.Equal(["A 15", "B 16", "1", "1 2"], GetElements<SceneText>(scene).Select(text => text.Text));
        AssertFinite(diagram);

        diagram.ShowValues = false;
        Assert.Equal(["A", "B", "", "1"], GetElements<SceneText>(diagram.CreateScene()).Select(text => text.Text));
    }

    [Fact]
    public void NamesThatLookTheSameStayDistinctNodes()
    {
        var diagram = new SankeyChart { ShowValues = false };
        diagram.AddLink("A  B", "C", 10);
        diagram.AddLink("A B", "C", 5);
        diagram.AddLink("A\nB", "C", 1);
        var scene = diagram.CreateScene();
        Assert.HasCount(4, GetElements<SceneRectangle>(scene));
        Assert.Equal(["A B", "C", "A B ", "A B  "], GetElements<SceneText>(scene).Select(text => text.Text));
    }

    [Fact]
    public void ExtremeValuesStayFinite()
    {
        var diagram = new SankeyChart();
        diagram.AddLink("A", "B", double.Epsilon);
        diagram.AddLink("A", "C", 1e300);
        diagram.AddLink("D", "E", 0);
        for (var i = 0; i < 10; i++)
        {
            diagram.AddLink("C", "F", 1e300);
        }

        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);

        var tiny = new SankeyChart();
        tiny.AddLink("A", "B", double.Epsilon);
        tiny.AddLink("B", "C", 1e-250);
        AssertFinite(tiny);

        var zeros = new SankeyChart();
        zeros.AddLink("A", "B", 0);
        zeros.AddLink("B", "C", 0);
        AssertFinite(zeros);

        var extremeSizes = new SankeyChart { Width = 1, Height = 1, NodeWidth = 1000, NodePadding = 1000 };
        extremeSizes.AddLink("A", "B", 1);
        extremeSizes.AddLink("A", "C", 2);
        extremeSizes.AddLink("B", "C", 3);
        AssertFinite(extremeSizes);

        var huge = new SankeyChart { Width = 10_000, Height = 10_000, NodeWidth = 0, NodePadding = 0 };
        huge.AddLink("A", "B", 1e300);
        AssertFinite(huge);

        ChartAssert.Svg(new SankeyChart());
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
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(-12.5, "-12.5")]
    public void NumbersAreWrittenAsJavaScript(double value, string expected)
    {
        Assert.Equal(expected, JavaScriptNumber.ToString(value));
    }

    [Theory]
    [InlineData(40, "40")]
    [InlineData(17.345, "17.35")]
    [InlineData(0.004, "0")]
    [InlineData(0.005, "0.01")]
    [InlineData(1e-7, "0")]
    [InlineData(1e300, "1e+300")]
    public void ValuesAreRoundedAsMermaid(double value, string expected)
    {
        Assert.Equal(expected, SankeyChartPlot.FormatValue(value));
    }

    [Fact]
    public void LinksAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyLink("A", "B", -1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyLink("A", "B", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyLink("A", "B", 1) { Value = 1e301 });
        Assert.Throws<ArgumentNullException>("source", () => new SankeyLink(null!, "B", 1));
        Assert.Throws<ArgumentNullException>("value", () => new SankeyLink("A", "B", 1) { Target = null! });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SankeyChart { Width = 0 });
        Assert.Throws<ArgumentNullException>("value", () => new SankeyChart { ValuePrefix = null! });
    }

    private static void AssertFinite(SankeyChart diagram)
    {
        var scene = diagram.CreateScene();
        foreach (var rectangle in GetElements<SceneRectangle>(scene))
        {
            Assert.True(double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y) && double.IsFinite(rectangle.Width) && double.IsFinite(rectangle.Height));
        }

        foreach (var text in GetElements<SceneText>(scene))
        {
            Assert.True(double.IsFinite(text.X) && double.IsFinite(text.Y));
        }

        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
    }

    private static SceneColor GetColor(ScenePaint? paint) => Assert.IsType<SolidPaint>(paint).Color;

    private static SceneColor GetLinkColor(SankeyChart diagram) => GetColor(Assert.Single(GetElements<ScenePath>(diagram.CreateScene())).Stroke);

    private static List<T> GetElements<T>(Scene scene)
        where T : SceneElement
    {
        var result = new List<T>();
        void Visit(SceneGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is T element)
                {
                    result.Add(element);
                }
                else if (child is SceneGroup childGroup)
                {
                    Visit(childGroup);
                }
            }
        }

        Visit(scene.Root);
        return result;
    }

    private static SankeyChart CreateDiagram()
    {
        var diagram = new SankeyChart();
        diagram.AddLink("A", "B", 30);
        diagram.AddLink("A", "C", 10);
        diagram.AddLink("B", "D", 25);
        diagram.AddLink("C", "D", 5);
        diagram.AddLink("C", "E", 5);
        diagram.AddLink("F", "E", 12.345);
        return diagram;
    }
}
