namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class VennDiagramParserTests
{
    [Fact]
    public void SetsUnionsTextsAndStyles()
    {
        #region Mermaid_VennDiagram
        var diagram = (VennDiagram)MermaidDocument.Parse("""
            venn-beta
              title Team skills
              set A["Frontend"]:20
                text A1["React"]
                text A2["Design Systems"]
              set B["Backend"]:12
                text B1["API"]
              union A,B["Shared"]:3
                text AB1["OpenAPI"]
              style A fill:#ff6b6b
              style A,B color:#333
              style A1 color:red
            """).Diagram;
        #endregion

        Assert.Equal("Team skills", diagram.Title);
        Assert.Equal(["A", "B", "A, B"], diagram.Areas.Select(area => area.ToString()));
        Assert.Equal(["Frontend", "Backend", "Shared"], diagram.Areas.Select(area => area.Label));
        Assert.Equal([20.0, 12.0, 3.0], diagram.Areas.Select(area => area.Size!.Value));
        Assert.Equal(["A1:A", "A2:A", "B1:B", "AB1:A,B"], diagram.TextNodes.Select(node => node.Id + ":" + string.Join(',', node.Sets)));
        Assert.Equal(["React", "Design Systems", "API", "OpenAPI"], diagram.TextNodes.Select(node => node.Label));
        Assert.Equal((SceneColor)"#ff6b6b", diagram.Styles[0].Style.Fill);
        Assert.Equal(["A", "B"], diagram.Styles[1].Targets);
        Assert.Equal((SceneColor)"#333333", diagram.Styles[1].Style.TextColor);
        Assert.Equal((SceneColor)"red", diagram.Styles[2].Style.TextColor);
    }

    [Fact]
    public void LabelsAndDefaultSizes()
    {
        var diagram = (VennDiagram)MermaidDocument.Parse("""
            venn-beta
              set "Foo Bar"
              set B[ Beta label ]
              set C[""]
              set D["  "]
              union B,"Foo Bar"
            """).Diagram;
        Assert.Equal(["Foo Bar", "B", "C", "D", "B, Foo Bar"], diagram.Areas.Select(area => area.ToString()));

        // A label is trimmed; [""] has no label, but [" "] is an empty label, as in Mermaid
        Assert.Equal(["Beta label", null, ""], diagram.Areas.Skip(1).Take(3).Select(area => area.Label));
        Assert.Null(diagram.Areas[0].Size);
        Assert.Equal(2.5, diagram.Areas[4].EffectiveSize);
    }

    [Fact]
    public void StatementsNeedNoLineBreaks()
    {
        var diagram = (VennDiagram)MermaidDocument.Parse("venn-beta set A set B union A,B[\"AB\"] style A,B fill:red").Diagram;
        Assert.HasCount(3, diagram.Areas);
        Assert.Equal("AB", diagram.Areas[2].Label);
        Assert.Single(diagram.Styles);
    }

    [Fact]
    public void KeywordsIgnoreTheCase()
    {
        var diagram = (VennDiagram)MermaidDocument.Parse("venn-beta\n  SET A\n  Set settings\n  UNION A,settings\n  TITLE Title\n").Diagram;
        Assert.Equal(["A", "settings", "A, settings"], diagram.Areas.Select(area => area.ToString()));
        Assert.Equal("Title", diagram.Title);
    }

    [Fact]
    public void IndentedTextsBelongToTheLastArea()
    {
        var diagram = (VennDiagram)MermaidDocument.Parse("""
            venn-beta
            set A
              text a1
            set B
              text "b 1"["Label"]
              text 42
            text A x
            union A,B
                text ab
            text A,B z
            """).Diagram;

        // A text at the start of a line names its sets, and ends the indented texts
        Assert.Equal(["a1:A", "b 1:B", "42:B", "x:A", "ab:A,B", "z:A,B"], diagram.TextNodes.Select(node => node.Id + ":" + string.Join(',', node.Sets)));
        Assert.Equal("Label", diagram.TextNodes[1].Label);
    }

    [Fact]
    public void Comments()
    {
        var diagram = (VennDiagram)MermaidDocument.Parse("venn-beta\n  %% a comment\n  set A %% a comment\n  set B[\"B %% not a comment\"]\n  set C %% a comment\n").Diagram;

        Assert.Equal(["A", "B", "C"], diagram.Areas.Select(area => area.ToString()));
        Assert.Equal("B %% not a comment", diagram.Areas[1].Label);
    }

    [Fact]
    public void TitleAndEntities()
    {
        var diagram = (VennDiagram)MermaidDocument.Parse("venn-beta\n  title \"Team #quot;overlap#quot;\" #35;1\n  set \"a#quot;b\"[\"x #amp; y\"]\ntext \"a#quot;b\" t[\"#59;\"]\n").Diagram;
        Assert.Equal("\"Team \"overlap\"\" #1", diagram.Title);
        Assert.Equal("a\"b", diagram.Areas[0].Sets[0]);
        Assert.Equal("x & y", diagram.Areas[0].Label);
        Assert.Equal(";", diagram.TextNodes[0].Label);
    }

    [Fact]
    public void StyleValues()
    {
        var document = MermaidDocument.Parse("""
            venn-beta
              set A
              set B
              style A fill: rgb(255, 0, 0), fill-opacity: 0.5, stroke: rgba(0,0,255,0.5), stroke-width: 3, color: "#00ff00"
              style B fill:#f00;
              style B stroke-width: "4px", fill-opacity: "50%", font-size: 12, fill: notacolor
            """);
        var diagram = (VennDiagram)document.Diagram;
        var a = diagram.Styles[0].Style;
        Assert.Equal((SceneColor)"#ff0000", a.Fill);
        Assert.Equal(0.5, a.FillOpacity);
        Assert.Equal(new SceneColor(0, 0, 255, 128), a.Stroke);
        Assert.Equal(3, a.StrokeWidth);
        Assert.Equal((SceneColor)"#00ff00", a.TextColor);

        // Mermaid removes the last semicolon of a line with style, a colon and a '#'
        Assert.Equal((SceneColor)"#ff0000", diagram.Styles[1].Style.Fill);
        Assert.Equal((4.0, 0.5), (diagram.Styles[2].Style.StrokeWidth, diagram.Styles[2].Style.FillOpacity));
        Assert.Null(diagram.Styles[2].Style.Fill);
        Assert.Equal(2, document.Warnings.Count);
    }

    [Fact]
    public void Configuration()
    {
        var diagram = (VennDiagram)MermaidDocument.Parse("""
            ---
            title: From the front matter
            config:
              theme: forest
              venn:
                width: 1600
                height: 900
              themeVariables:
                venn1: "#ff0000"
                venn8: "#00ff00"
                vennSetTextColor: "#0000ff"
            ---
            venn-beta
              set A
            """).Diagram;
        Assert.Equal(("From the front matter", 1600.0, 900.0), (diagram.Title, diagram.Width, diagram.Height));
        Assert.Same(DiagramTheme.Forest, diagram.Theme);
        Assert.Equal((SceneColor)"#ff0000", diagram.SetColors[0]);
        Assert.Equal((SceneColor)"#00ff00", diagram.SetColors[7]);
        Assert.Equal((SceneColor)"#0000ff", diagram.TextColor);
    }

    [Theory]
    [InlineData("venn-beta\n  union A,B", "(2, 3): Unknown set identifier: A, B.")]
    [InlineData("venn-beta\n  set A\n  union A", "(3, 3): A union requires multiple identifiers.")]
    [InlineData("venn-beta\n  set A:-1", "(2, 9): The size must be a positive number.")]
    [InlineData("venn-beta\n  set", "(2, 6): Expected an identifier, found a line break.")]
    [InlineData("venn-beta\n  set set-x", "(2, 7): Expected an identifier, found 'set'.")]
    [InlineData("venn-beta\n  text A\n", "(2, 9): Expected the identifier of a text, found a line break.")]
    [InlineData("venn-beta\n  text A 5[\"x\"]", "(2, 11): Expected a statement, found '[\"x\"]'.")]
    [InlineData("venn-beta\n  set A[\"x]", "(2, 8): Unexpected character '['.")]
    [InlineData("venn-beta\n  set \"A", "(2, 7): Unexpected character '\"'.")]
    [InlineData("venn-beta\n  A", "(2, 3): Expected a statement, found 'A'.")]
    [InlineData("venn-beta\n  title x;y", "(2, 10): Unexpected character ';'.")]
    [InlineData("venn-beta\n  style A fill", "(2, 15): Expected ':', found a line break.")]
    [InlineData("venn-beta\n  style A fill: red stroke: blue", "(2, 27): Expected a statement, found ':'.")]
    [InlineData("venn-beta\n  set A[\"#quot;\"]:1 #quot;", "(2, 21): Unexpected character '#'.")]
    [InlineData("venn-betaX", "(1, 1): No diagram type was found")]
    [InlineData("venn-beta\n  set \"%%a\"", "(2, 12): Expected an identifier, found a line break.")]
    [InlineData("venn-beta\n  set A\n    text A,B x", "(3, 11): Expected a statement, found ','.")]
    public void InvalidVennDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooManySets()
    {
        var text = "venn-beta\n" + string.Concat(Enumerable.Range(0, 33).Select(i => "set S" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"));
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains("A Venn diagram can have at most 32 sets.", exception.Message, StringComparison.Ordinal);

        exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse("venn-beta\n set A\n set B\n text A t\n", new MermaidParseOptions { MaxNodes = 2 }));
        Assert.Contains("The diagram has more than 2 sets, unions and texts.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new VennDiagram { Title = " A #title; with %% and\nlines ", Width = 1000, Height = 500, Padding = 4 };
        diagram.SetColors[2] = "#123456";
        diagram.TextColor = "#654321";
        diagram.AddSet("set", " label \"quoted\" ", 12.5);
        diagram.AddSet("title", "", 1e-7);
        diagram.AddSet("plain_id-1", "style x:#y;z");
        diagram.AddSet(" spaced\tid ", size: 0);
        diagram.AddOverlap(["set", "title"], "a & b", 1e21);
        diagram.AddText(["set", "title"], "node", "%%{init}%%");
        diagram.AddText(["set"], "5");
        diagram.AddText(["title"], "Text");
        diagram.AddStyle(["set", "title"], new VennStyle { Fill = "#ff000080", FillOpacity = -0.5, Stroke = "#00ff00", StrokeWidth = 2.5, TextColor = "#0000ff" });
        diagram.AddStyle(["Text"], new VennStyle());

        var text = MermaidWriter.Write(diagram);
        var parsed = (VennDiagram)MermaidDocument.Parse(text).Diagram;
        Assert.Equal(text, MermaidWriter.Write(parsed));
        Assert.Equal((diagram.Title, 1000.0, 500.0, 4.0), (parsed.Title, parsed.Width, parsed.Height, parsed.Padding));
        Assert.Equal((SceneColor)"#123456", parsed.SetColors[2]);
        Assert.Equal((SceneColor)"#654321", parsed.TextColor);
        Assert.Equal(diagram.Areas.Select(area => (string.Join('|', area.Sets), area.Label, area.Size)), parsed.Areas.Select(area => (string.Join('|', area.Sets), area.Label, area.Size)));
        Assert.Equal(diagram.TextNodes.Select(node => (string.Join('|', node.Sets), node.Id, node.Label)), parsed.TextNodes.Select(node => (string.Join('|', node.Sets), node.Id, node.Label)));
        Assert.Equal(diagram.Styles[0].Style, parsed.Styles.Single().Style);
    }

    [Fact]
    public void WriteSimpleDiagram()
    {
        var diagram = new VennDiagram();
        diagram.AddSet("A", "Alpha", 20);
        diagram.AddSet("B");
        diagram.AddOverlap(["A", "B"], "AB");
        diagram.AddText(["A"], "a1", "React");
        diagram.AddText(["A", "B"], "ab");
        diagram.AddStyle(["A"], new VennStyle { Fill = "#ff6b6b" });
        Assert.Equal("""
            venn-beta
                set A["Alpha"]:20
                    text a1["React"]
                set B
                union A,B["AB"]
                    text ab
                style A fill: #ff6b6b

            """.Replace("\r\n", "\n", StringComparison.Ordinal), MermaidWriter.Write(diagram));
    }

    [Fact]
    public void WriteTextsInTheirOrder()
    {
        // A text that does not follow its area is written with its sets, at the start of a line
        var diagram = new VennDiagram();
        diagram.AddSet("A");
        diagram.AddSet("B");
        diagram.AddText(["B"], "b");
        diagram.AddText(["A"], "a");
        var text = MermaidWriter.Write(diagram);
        Assert.Contains("\ntext A a\n", text, StringComparison.Ordinal);
        var parsed = (VennDiagram)MermaidDocument.Parse(text).Diagram;
        Assert.Equal(["b", "a"], parsed.TextNodes.Select(node => node.Id));
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotRead()
    {
        var withDescription = new VennDiagram { Description = "Description" };
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(withDescription));

        var undeclared = new VennDiagram();
        undeclared.AddSet("A");
        undeclared.AddOverlap(["A", "B"]);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(undeclared));

        var tooMany = new VennDiagram();
        for (var i = 0; i < 33; i++)
        {
            tooMany.AddSet("S" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(tooMany));
    }

    [Fact]
    public void WriteEmptyDiagram()
    {
        Assert.Equal("venn-beta\n", MermaidWriter.Write(new VennDiagram()));
    }
}
