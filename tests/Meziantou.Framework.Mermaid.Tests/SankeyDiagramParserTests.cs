namespace Meziantou.Framework.Mermaid.Tests;

public sealed class SankeyDiagramParserTests
{
    private static SankeyDiagram Parse(string text) => (SankeyDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void Links()
    {
        #region Mermaid_SankeyDiagram
        var diagram = (SankeyDiagram)MermaidDocument.Parse("""
            sankey

            Solar,Electricity,60
            Wind,Electricity,90
            Gas,Electricity,120
            Gas,Heating,80
            Electricity,"Homes, offices",110
            Electricity,Industry,140
            Heating,"Homes, offices",80
            """).Diagram;
        #endregion

        Assert.HasCount(7, diagram.Links);
        Assert.Equal(("Electricity", "Homes, offices", 110.0), (diagram.Links[4].Source, diagram.Links[4].Target, diagram.Links[4].Value));
        Assert.Equal(["Solar", "Electricity", "Wind", "Gas", "Heating", "Homes, offices", "Industry"], diagram.GetNodes());
    }

    [Fact]
    public void QuotesSpacesAndNumbersAsMermaid()
    {
        var diagram = Parse("sankey-beta\n a , b ,  3 \n\"C, \"\"quoted\"\"\",B,2.5\n\"Multi\n\n\nline\",B,\"5\"\nA,B,1x\nA,B,1e3\nA,B, .5\nAgricultural 'waste',\u00E9,0\n");
        Assert.Equal(
        [
            ("a", "b", 3.0), ("C, \"quoted\"", "B", 2.5), ("Multi\nline", "B", 5), ("A", "B", 1), ("A", "B", 1000), ("A", "B", 0.5), ("Agricultural 'waste'", "\u00E9", 0),
        ], diagram.Links.Select(link => (link.Source, link.Target, link.Value)));
    }

    [Fact]
    public void Configuration()
    {
        var diagram = Parse("""
            ---
            title: Flows
            config:
              sankey:
                width: 800
                height: 300
                nodeWidth: 15
                nodePadding: 20
                linkColor: source
                nodeAlignment: left
                showValues: false
                prefix: $
                suffix: ' M'
                labelStyle: outlined
                nodeColors:
                  A: "#ff0000"
                  B: blue
            ---
            sankey
            A,B,1
            """);
        Assert.Equal(("Flows", 800.0, 300.0, 15.0, 20.0), (diagram.Title, diagram.Width, diagram.Height, diagram.NodeWidth, diagram.NodePadding));
        Assert.Equal((SankeyLinkColoring.Source, SankeyNodeAlignment.Left, false, "$", " M", SankeyLabelStyle.Outlined), (diagram.LinkColoring, diagram.NodeAlignment, diagram.ShowValues, diagram.ValuePrefix, diagram.ValueSuffix, diagram.LabelStyle));
        Assert.Equal(SceneColor.Parse("#ff0000"), diagram.NodeColors["A"]);
        Assert.Equal(SceneColor.Parse("blue"), diagram.NodeColors["B"]);

        var colored = Parse("---\nconfig:\n  sankey:\n    linkColor: '#a1a1a1'\n    nodeAlignment: center\n---\nsankey\nA,B,1");
        Assert.Equal((SceneColor.Parse("#a1a1a1"), SankeyNodeAlignment.Center), (colored.LinkColor, colored.NodeAlignment));

        var document = MermaidDocument.Parse("---\nconfig:\n  sankey:\n    linkColor: nope\n    nodeAlignment: top\n---\nsankey\nA,B,1");
        Assert.HasCount(2, document.Warnings);
    }

    [Fact]
    public void EmptySankey()
    {
        Assert.Empty(Parse("sankey").Links);
    }

    [Theory]
    [InlineData("sankey\nA,B", "A row has three columns")]
    [InlineData("sankey\nA,B,1,2", "A row has three columns")]
    [InlineData("sankey\nA,\"B\"x,1", "after the quoted text.")]
    [InlineData("sankey\nA,B\"x,1", "A quote must be in a quoted field")]
    [InlineData("sankey\nA,\"B,1", "The quoted text is not closed.")]
    [InlineData("sankey\nA,B,abc", "The value 'abc' is not a number.")]
    [InlineData("sankey\nA,B,", "The value '' is not a number.")]
    [InlineData("sankey\nA,B,-3", "is negative")]
    [InlineData("sankey\nA,B,Infinity", "The value is too large.")]
    [InlineData("sankey A,B,1", "the rows start on the next line.")]
    [InlineData("sankey\nA,B,1\nB,C,1\nC,A,1", "(4, 1): The link from 'C' to 'A' closes a cycle")]
    [InlineData("sankey\nA,A,1", "closes a cycle")]
    public void InvalidSankeys(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new SankeyDiagram { Title = "Flows", Width = 700, NodeAlignment = SankeyNodeAlignment.Right, LinkColoring = SankeyLinkColoring.Target, ValuePrefix = "\"$\"", LabelStyle = SankeyLabelStyle.Outlined, NodePadding = 5 };
        diagram.NodeColors["Quote \"q\""] = SceneColor.Parse("#123456");
        diagram.AddLink("Quote \"q\"", "comma, here", 1e21);
        diagram.AddLink("comma, here", "%start", 5e-324);
        diagram.AddLink("%start", "line\nbreak", 0);
        diagram.AddLink("", "empty", 1.5);

        var text = MermaidWriter.Write(diagram);
        Assert.Contains("\"Quote \"\"q\"\"\",\"comma, here\",1E+21\n", text, StringComparison.Ordinal);
        Assert.Contains("\" \",empty,1.5\n", text, StringComparison.Ordinal);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (SankeyDiagram)parsed.Diagram;
        Assert.Equal([("Quote \"q\"", "comma, here", 1e21), ("comma, here", "%start", 5e-324), ("%start", "line\nbreak", 0), ("", "empty", 1.5)], result.Links.Select(link => (link.Source, link.Target, link.Value)));
        Assert.Equal(("Flows", 700.0, SankeyNodeAlignment.Right, SankeyLinkColoring.Target, "\"$\"", SankeyLabelStyle.Outlined, 5.0), (result.Title, result.Width, result.NodeAlignment, result.LinkColoring, result.ValuePrefix, result.LabelStyle, result.NodePadding));
        Assert.Equal(SceneColor.Parse("#123456"), result.NodeColors["Quote \"q\""]);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotRead()
    {
        var accessible = new SankeyDiagram { Description = "Description" };
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(accessible));

        var spaces = new SankeyDiagram();
        spaces.AddLink(" A", "B", 1);
        Assert.Throws<ArgumentException>("name", () => MermaidWriter.Write(spaces));

        var directive = new SankeyDiagram();
        directive.AddLink("%%{init}%%", "B", 1);
        Assert.Throws<ArgumentException>("name", () => MermaidWriter.Write(directive));
    }
}
