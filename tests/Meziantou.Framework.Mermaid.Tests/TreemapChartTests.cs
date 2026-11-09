using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.TreemapDiagrams;

namespace Meziantou.Framework.Mermaid.Tests;

public sealed class TreemapChartTests
{
    [Fact]
    public void TreemapChart()
    {
        #region Mermaid_TreemapChart
        var diagram = new TreemapChart { Title = "Sales by product" };
        var electronics = diagram.AddNode("Electronics");
        electronics.AddChild("Phones", 50);
        electronics.AddChild("Computers", 30);
        electronics.AddChild("Accessories", 20);
        var clothing = diagram.AddNode("Clothing");
        clothing.AddChild("Men's", 40);
        clothing.AddChild("Women's", 40);
        #endregion

        Assert.Equal(["Electronics", "Phones", "Computers", "Accessories", "Clothing", "Men's", "Women's"], diagram.EnumerateNodes().Select(node => node.Name));
        ChartAssert.Svg(diagram, "TreemapChart");
    }

    [Fact]
    public void ClassesAndValueFormat()
    {
        var diagram = new TreemapChart { ValueFormat = "$0,0", InnerPadding = 4, NodeWidth = 60, NodeHeight = 30 };
        diagram.ClassDefinitions["important"] = new DiagramStyle { Fill = SceneColor.Parse("#ff9966"), Stroke = SceneColor.Parse("#333333"), StrokeWidth = 2, TextColor = SceneColor.Parse("#0000ff") };
        diagram.ClassDefinitions["faded"] = new DiagramStyle { Opacity = 0.5 };
        var budget = diagram.AddNode("Budget");
        var operations = budget.AddChild("Operations");
        operations.AddChild("Salaries", 700_000).CssClass = "important";
        operations.AddChild("Equipment", 200_000).CssClass = "faded";
        operations.AddChild("Supplies", 100_000);
        var marketing = budget.AddChild("Marketing with a name too long for its header");
        marketing.CssClass = "important";
        marketing.AddChild("Advertising", 400_000);
        marketing.AddChild("Events", 100_000);

        var svg = diagram.ToSvg();
        Assert.Contains("fill=\"#ff9966", svg, StringComparison.Ordinal);
        Assert.Contains(">$700,000<", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Salaries (", svg, StringComparison.Ordinal);
        Assert.Contains("Budget ($1,500,000)", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void LeafValuesUseTheValueFormat()
    {
        var diagram = new TreemapChart();
        var section = diagram.AddNode("Section");
        section.AddChild("Leaf", 1234567.891);
        section.AddChild("Other", 1000);

        var svg = diagram.ToSvg();
        Assert.Contains(">1,234,567.891<", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("1,234,570", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyLeavesWithoutValues()
    {
        var diagram = new TreemapChart { ShowValues = false, Theme = DiagramTheme.Dark };
        for (var i = 0; i < 3; i++)
        {
            var section = diagram.AddNode("Section " + i);
            for (var j = 0; j < 8; j++)
            {
                section.AddChild("Leaf " + i + "." + j, (j + 1) * (i + 1));
            }
        }

        var svg = diagram.ToSvg();
        Assert.DoesNotContain("(", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void TopLevelLeavesAndNestedSections()
    {
        var diagram = new TreemapChart { Title = "Disk usage", DiagramPadding = 30, Theme = DiagramTheme.Neutral };
        diagram.AddNode("Swap", 8);
        var home = diagram.AddNode("home");
        var documents = home.AddChild("Documents");
        documents.AddChild("Reports", 12);
        documents.AddChild("Invoices", 3);
        home.AddChild("Pictures", 20);
        diagram.AddNode("Logs", 4);
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void ToPlot()
    {
        var diagram = new TreemapChart { Title = "Sales", NodeWidth = 50, NodeHeight = 30, DiagramPadding = 0, InnerPadding = 4 };
        diagram.AddNode("A").AddChild("B", 1);

        var plot = diagram.ToPlot();
        var mark = Assert.IsType<TreemapMark<TreemapNode?>>(Assert.Single(plot.Marks));
        Assert.Equal((4.0, 10.0, 35.0, true), (mark.PaddingInner, mark.PaddingOuter, mark.PaddingTop, mark.ShowValues));
        Assert.Equal(["A", "B"], mark.Root.Descendants().Skip(1).Select(node => node.Name));
        Assert.False(plot.Legend.Visible);
        Assert.Equal(480.0, plot.Width);
        var margin = Assert.IsType<ChartMargin>(plot.Margin);
        Assert.Equal((500.0, 300.0), (plot.Width - margin.Left - margin.Right, plot.Height - margin.Top - margin.Bottom));
        Assert.Equal((-10.0, -10.0, -10.0), (margin.Left, margin.Right, margin.Bottom));
    }

    [Fact]
    public void ToPlotCustomizesTheMark()
    {
        #region Mermaid_TreemapChart_ToPlot
        var chart = new TreemapChart { Title = "Sales by product" };
        var electronics = chart.AddNode("Electronics");
        electronics.AddChild("Phones", 50);
        electronics.AddChild("Computers", 30);
        chart.AddNode("Clothing").AddChild("Shoes", 40);

        var plot = chart.ToPlot();
        var mark = (TreemapMark<TreemapNode?>)plot.Marks[0];
        mark.Tiling = TreemapTiling.SliceDice;
        var svg = plot.ToSvg();
        #endregion

        Assert.Contains("Phones", svg, StringComparison.Ordinal);
        Assert.Equal(TreemapTiling.Squarified, ((TreemapMark<TreemapNode?>)chart.ToPlot().Marks[0]).Tiling);
    }

    [Fact]
    public void EmptyAndZeroValues()
    {
        ChartAssert.Svg(new TreemapChart());

        var diagram = new TreemapChart { Theme = DiagramTheme.Forest };
        diagram.AddNode("Empty section");
        diagram.AddNode("Zero", 0);
        diagram.AddNode("Zeros").AddChild("A", 0);
        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void EdgeCasesDrawWithFiniteCoordinates()
    {
        var huge = new TreemapChart();
        for (var i = 0; i < 5; i++)
        {
            huge.AddNode("Huge " + i, 1e300);
        }

        huge.AddNode("Tiny", 1e-300);
        AssertFinite(huge);

        var deep = new TreemapChart { NodeWidth = 1, NodeHeight = 1, InnerPadding = 1000, DiagramPadding = 0 };
        var node = deep.AddNode("Level 0");
        for (var i = 1; i < 1500; i++)
        {
            node = node.AddChild("Level " + i);
        }

        node.AddChild("Leaf", 1);
        AssertFinite(deep);

        var names = new TreemapChart { Title = "" };
        var section = names.AddNode("");
        section.AddChild("", 1);
        section.AddChild("  \n  ", 2);
        names.AddNode("", 3);
        AssertFinite(names);

        var cycle = new TreemapChart();
        var parent = cycle.AddNode("Parent");
        var leaf = parent.AddChild("Leaf", 1);
        parent.Children.Add(leaf);
        parent.Children.Add(parent);
        var withValue = cycle.AddNode("Leaf with children", 2);
        withValue.AddChild("Child", 3);
        cycle.ClassDefinitions["broken"] = new DiagramStyle { Opacity = double.NaN };
        leaf.CssClass = "broken";
        var root = Assert.IsType<TreemapMark<TreemapNode?>>(Assert.Single(cycle.ToPlot().Marks)).Root;
        Assert.Single(root.Descendants(), item => item.Name == "Leaf");
        AssertFinite(cycle);

        static void AssertFinite(TreemapChart chart)
        {
            var svg = chart.ToSvg();
            Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
            Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
            Assert.Equal("svg", System.Xml.Linq.XDocument.Parse(svg).Root!.Name.LocalName);
        }
    }

    [Theory]
    [InlineData(",", 1234567.891, "1,234,567.891")]
    [InlineData(",", 0.1, "0.1")]
    [InlineData(",", 1e21, "1e+21")]
    [InlineData(",", 0, "0")]
    [InlineData(".1f", 3.14159, "3.1")]
    [InlineData(".1%", 0.355, "35.5%")]
    [InlineData("$0,0", 700000, "$700,000")]
    [InlineData("$.1%", 0.35, "$35.0%")]
    [InlineData("$,.2f", 1234.5, "$1.2e+3")]
    [InlineData("$", 12.5, "$12.5")]
    [InlineData("d", 2.5, "3")]
    [InlineData("d", 1e22, "10000000000000000000000")]
    [InlineData("e", 12345, "1.234500e+4")]
    [InlineData(".2e", 0.000123, "1.23e-4")]
    [InlineData("g", 0.000001234, "0.00000123400")]
    [InlineData(".3g", 1234.5, "1.23e+3")]
    [InlineData("r", 1234.5678, "1234.57")]
    [InlineData(".2r", 0.0001234, "0.00012")]
    [InlineData("s", 1234567, "1.23457M")]
    [InlineData(".3s", 0.00012345, "123\u00B5")]
    [InlineData("s", 1e-30, "0.000001y")]
    [InlineData("~s", 1500, "1.5k")]
    [InlineData("p", 0.1234, "12.3400%")]
    [InlineData("b", 10, "1010")]
    [InlineData("o", 64, "100")]
    [InlineData("x", 255, "ff")]
    [InlineData("X", 255, "FF")]
    [InlineData("#x", 255, "0xff")]
    [InlineData("c", 65, "65")]
    [InlineData("08.2f", 3.14159, "00003.14")]
    [InlineData("010,", 1234567, "01,234,567")]
    [InlineData(">10", 42, "        42")]
    [InlineData("<10", 42, "42        ")]
    [InlineData("^10", 42, "    42    ")]
    [InlineData("*^9,", 1234, "**1,234**")]
    [InlineData("+", 5, "+5")]
    [InlineData("(", 5, "5")]
    [InlineData(" ", 5, " 5")]
    [InlineData("n", 1234.5, "1,234.50")]
    [InlineData("invalid!", 1234, "1,234")]
    [InlineData(".", 1, "1")]
    [InlineData("~e", 1.5, "1.5e+0")]
    [InlineData(".0f", 0.5, "1")]
    [InlineData(".0f", 2.5, "3")]
    [InlineData(".20f", 0.1, "0.10000000000000000555")]
    [InlineData("", 1234.5, "1,234.5")]
    [InlineData("~", 1234.5, "1234.5")]
    [InlineData(".0e", 2.5, "3e+0")]
    [InlineData(".1f", 0.25, "0.3")]
    [InlineData(".2f", 1.005, "1.00")]
    [InlineData(".0f", 1e20, "100000000000000000000")]
    [InlineData(".3r", 2.5e-7, "0.000000250")]
    [InlineData(".2s", 2.5e10, "25G")]
    [InlineData("e", 0, "0.000000e+0")]
    [InlineData(".25g", 1.0 / 3, "0.333333333333333314830")]
    [InlineData("%", 1, "100.000000%")]
    public void ValueFormatIsTheOneOfD3Format(string format, double value, string expected)
    {
        // The expected texts are the ones of d3-format 3.1.3, with the special cases of Mermaid: an empty format is ",", and the formats that start with $
        Assert.Equal(expected, TreemapValueFormatter.Create(format).Format(value));
    }

    [Fact]
    public void NodesAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapNode("A", -1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapNode("A", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapNode("A") { Value = double.PositiveInfinity });
        Assert.Throws<ArgumentNullException>("name", () => new TreemapNode(null!));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapChart { NodeWidth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapChart { InnerPadding = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapChart { DiagramPadding = double.NaN });
        Assert.Throws<ArgumentNullException>("value", () => new TreemapChart { ValueFormat = null! });
    }
}
