using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class TreemapDiagramTests
{
    [Fact]
    public void TreemapDiagram()
    {
        #region Diagrams_TreemapDiagram
        var diagram = new TreemapDiagram { Title = "Sales by product" };
        var electronics = diagram.AddNode("Electronics");
        electronics.AddChild("Phones", 50);
        electronics.AddChild("Computers", 30);
        electronics.AddChild("Accessories", 20);
        var clothing = diagram.AddNode("Clothing");
        clothing.AddChild("Men's", 40);
        clothing.AddChild("Women's", 40);
        #endregion

        Assert.Equal(["Electronics", "Phones", "Computers", "Accessories", "Clothing", "Men's", "Women's"], diagram.EnumerateNodes().Select(node => node.Name));
        DiagramAssert.Svg(diagram, "TreemapDiagram");
    }

    [Fact]
    public void ClassesAndValueFormat()
    {
        var diagram = new TreemapDiagram { ValueFormat = "$0,0", InnerPadding = 4, NodeWidth = 60, NodeHeight = 30 };
        diagram.ClassDefinitions["important"] = new DiagramStyle { Fill = SceneColor.Parse("#ff9966"), Stroke = SceneColor.Parse("#333333"), StrokeWidth = 2, TextColor = SceneColor.Parse("#0000ff") };
        var budget = diagram.AddNode("Budget");
        var operations = budget.AddChild("Operations");
        operations.AddChild("Salaries", 700_000).CssClass = "important";
        operations.AddChild("Equipment", 200_000);
        operations.AddChild("Supplies", 100_000);
        var marketing = budget.AddChild("Marketing with a name too long for its header");
        marketing.CssClass = "important";
        marketing.AddChild("Advertising", 400_000);
        marketing.AddChild("Events", 100_000);
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void ManyLeavesWithoutValues()
    {
        var diagram = new TreemapDiagram { ShowValues = false, Theme = DiagramTheme.Dark };
        for (var i = 0; i < 3; i++)
        {
            var section = diagram.AddNode("Section " + i);
            for (var j = 0; j < 8; j++)
            {
                section.AddChild("Leaf " + i + "." + j, (j + 1) * (i + 1));
            }
        }

        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void EmptyAndZeroValues()
    {
        DiagramAssert.Svg(new TreemapDiagram());

        var diagram = new TreemapDiagram { Theme = DiagramTheme.Forest };
        diagram.AddNode("Empty section");
        diagram.AddNode("Zero", 0);
        diagram.AddNode("Zeros").AddChild("A", 0);
        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void LayoutIsTheOneOfD3Hierarchy()
    {
        // The rectangles that d3-hierarchy 3.1.3 gives Mermaid for the same tree, at the default size and paddings
        var diagram = new TreemapDiagram();
        var a = diagram.AddNode("A");
        a.AddChild("B", 0.001);
        a.AddChild("C", 1000);
        var d = a.AddChild("D");
        d.AddChild("E", 3);
        d.AddChild("F", 0);
        d.AddChild("G", 7);
        a.AddChild("H", 2);
        diagram.AddNode("I", 5);
        diagram.AddNode("J");

        var root = TreemapLayout.Compute(diagram);
        var rectangles = TreemapLayout.PreOrder(root).Skip(1).Select(node => (node.Name, node.X0, node.Y0, node.Width, node.Height)).ToArray();
        Assert.Equal(
        [
            ("A", 10, 35, 975, 355), ("C", 20, 70, 944, 310), ("D", 974, 70, 1, 257), ("G", 974, 105, 0, 212), ("E", 974, 105, 0, 212), ("F", 974, 105, 0, 212),
            ("H", 974, 337, 1, 43), ("B", 974, 385, 1, 0), ("I", 993, 35, 0, 355), ("J", 993, 395, 0, 0),
        ], rectangles);
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
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapDiagram { NodeWidth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new TreemapDiagram { InnerPadding = -1 });
        Assert.Throws<ArgumentNullException>("value", () => new TreemapDiagram { ValueFormat = null! });
    }

    [Fact]
    public void NodeInTheDiagramTwiceIsDrawnOnce()
    {
        var diagram = new TreemapDiagram();
        var section = diagram.AddNode("Section");
        var leaf = section.AddChild("Leaf", 1);
        section.Children.Add(leaf);
        section.Children.Add(section);
        Assert.Single(TreemapLayout.PreOrder(TreemapLayout.Compute(diagram)), node => node.Name == "Leaf");
    }
}
