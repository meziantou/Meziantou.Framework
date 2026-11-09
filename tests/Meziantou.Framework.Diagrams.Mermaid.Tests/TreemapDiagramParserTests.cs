namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class TreemapDiagramParserTests
{
    private static TreemapDiagram Parse(string text) => (TreemapDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void Hierarchy()
    {
        #region Mermaid_TreemapDiagram
        var diagram = (TreemapDiagram)MermaidDocument.Parse("""
            treemap
              title Sales by product
              "Electronics"
                  "Phones": 50
                  "Computers": 30
                  "Accessories": 20
              "Clothing"
                  "Men's": 40
                  "Women's": 40
            """).Diagram;
        #endregion

        Assert.Equal("Sales by product", diagram.Title);
        Assert.Equal(["Electronics", "Clothing"], diagram.Nodes.Select(node => node.Name));
        Assert.Equal([("Phones", 50.0), ("Computers", 30.0), ("Accessories", 20.0)], diagram.Nodes[0].Children.Select(node => (node.Name, node.Value!.Value)));
        Assert.Null(diagram.Nodes[0].Value);
    }

    [Fact]
    public void IndentationAsMermaid()
    {
        // A row is a child of the last section less indented than it: a leaf has no children
        var diagram = Parse("""
            treemap-beta
            "A"
                "B"
                    "C": 3
              "D": 4
            "E": 1
                "F": 2
            """);
        Assert.Equal(["A", "E", "F"], diagram.Nodes.Select(node => node.Name));
        Assert.Equal(["B", "D"], diagram.Nodes[0].Children.Select(node => node.Name));
        Assert.Equal(["C"], diagram.Nodes[0].Children[0].Children.Select(node => node.Name));
    }

    [Fact]
    public void ValuesClassesAndConfiguration()
    {
        var diagram = Parse("""
            ---
            config:
              treemap:
                padding: 4
                nodeWidth: 50
                nodeHeight: 0
                showValues: false
                valueFormat: '$0,0'
                diagramPadding: 20
            ---
            treemap
              accTitle: Accessible
              accDescr: Described
              'Quote "1"':::section_1
                "Comma" , 1,000.5
                "Underscore": 1_000:::leaf
                "Points": 1.2.3 :::leaf
                "Leading point":.5
            classDef section_1 fill:red,color:blue,stroke:#FFD600;
            classDef leaf fill:rgb(1\,2\,3)
            """);
        Assert.Equal((4.0, 50.0, 50.0, false, "$0,0", 20.0), (diagram.InnerPadding, diagram.NodeWidth, diagram.NodeHeight, diagram.ShowValues, diagram.ValueFormat, diagram.Padding));
        Assert.Equal(("Accessible", "Described"), (diagram.AccessibleTitle, diagram.Description));
        var section = Assert.Single(diagram.Nodes);
        Assert.Equal(("Quote \"1\"", "section_1"), (section.Name, section.CssClass));
        Assert.Equal([1000.5, 1, 1.2, 0.5], section.Children.Select(node => node.Value!.Value));
        Assert.Equal("leaf", section.Children[1].CssClass);
        Assert.Equal(SceneColor.Parse("red"), diagram.ClassDefinitions["section_1"].Fill);
        Assert.Equal(SceneColor.Parse("blue"), diagram.ClassDefinitions["section_1"].TextColor);
        Assert.Equal(SceneColor.Parse("rgb(1,2,3)"), diagram.ClassDefinitions["leaf"].Fill);
    }

    [Fact]
    public void NameOnSeveralLines()
    {
        var diagram = Parse("treemap\n\"First\nsecond\"\n    \"Leaf\": 1\n");
        Assert.Equal("First\nsecond", diagram.Nodes[0].Name);
        Assert.Equal("Leaf", diagram.Nodes[0].Children[0].Name);
    }

    [Fact]
    public void EmptyTreemap()
    {
        Assert.Empty(Parse("treemap").Nodes);
    }

    [Theory]
    [InlineData("treemap\nA: 1", "a quoted name is expected.")]
    [InlineData("treemap\n\"A\": _1", "a value is expected.")]
    [InlineData("treemap\n\"A\": ,5", "a value is expected.")]
    [InlineData("treemap\n\"A\": .", "is not a number.")]
    [InlineData("treemap\n\"A\": 1x", "Unexpected 'x'.")]
    [InlineData("treemap\n\"A", "The string is not closed.")]
    [InlineData("treemap\n\"A\":::", "A class name is expected after ':::'.")]
    [InlineData("treemap\nclassDef x fill:red", "A class name of at least two letters")]
    [InlineData("treemap \"A\": 1", "Unexpected '\"A\":'.")]
    public void InvalidTreemaps(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HugeValueIsRejected()
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse("treemap\n\"A\": " + new string('9', 310)));
        Assert.Contains("The value is too large.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeepNestingIsRejected()
    {
        var text = "treemap\n" + string.Concat(Enumerable.Range(0, 40).Select(i => new string(' ', i) + "\"S" + i + "\"\n"));
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains("nested deeper than 32 levels", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new TreemapDiagram { Title = "Budget", Description = "The budget", InnerPadding = 5, NodeWidth = 80, ValueFormat = ".1f", ShowValues = false, Theme = DiagramTheme.Forest };
        diagram.ClassDefinitions["important"] = new DiagramStyle { Fill = SceneColor.Parse("#ff9966"), StrokeWidth = 2 };
        diagram.ClassDefinitions["empty"] = new DiagramStyle();
        var operations = diagram.AddNode("Operations \"main\"");
        operations.CssClass = "important";
        operations.AddChild("Salaries", 1e21);
        operations.AddChild("It's small", 1.5e-7);
        operations.AddChild("Line\nbreak", 0);
        diagram.AddNode("Empty");

        var text = MermaidWriter.Write(diagram);
        Assert.Contains("\"Salaries\": 1000000000000000000000\n", text, StringComparison.Ordinal);
        Assert.Contains("\"It's small\": 0.00000015\n", text, StringComparison.Ordinal);
        Assert.Contains("'Operations \"main\"':::important\n", text, StringComparison.Ordinal);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (TreemapDiagram)parsed.Diagram;
        Assert.Equal(("Budget", "The budget", 5.0, 80.0, ".1f", false), (result.Title, result.Description, result.InnerPadding, result.NodeWidth, result.ValueFormat, result.ShowValues));
        Assert.Same(DiagramTheme.Forest, result.Theme);
        Assert.Equal([1e21, 1.5e-7, 0], result.Nodes[0].Children.Select(node => node.Value!.Value));
        Assert.Equal("Line\nbreak", result.Nodes[0].Children[2].Name);
        Assert.Equal(["important", "empty"], result.ClassDefinitions.Keys);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotRead()
    {
        var both = new TreemapDiagram();
        both.AddNode("\"double\" and 'single'");
        Assert.Throws<ArgumentException>("name", () => MermaidWriter.Write(both));

        var leafWithChildren = new TreemapDiagram();
        leafWithChildren.AddNode("Leaf", 1).AddChild("Child", 2);
        Assert.Throws<ArgumentException>("node", () => MermaidWriter.Write(leafWithChildren));

        var comment = new TreemapDiagram();
        comment.AddNode("a\n  %% b");
        Assert.Throws<ArgumentException>("name", () => MermaidWriter.Write(comment));

        var className = new TreemapDiagram();
        className.ClassDefinitions["x"] = new DiagramStyle();
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(className));
    }
}
