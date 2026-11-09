namespace Meziantou.Framework.Mermaid.Tests;

public sealed class MermaidDocumentTests
{
    [Fact]
    public void Parse()
    {
        #region Mermaid_Parse
        var text = """
            ---
            title: Order processing
            config:
              theme: forest
            ---
            flowchart LR
                start([Order received]) --> check{Payment valid?}
                check -->|yes| ship[Ship the order]
                check -->|no| cancel[Cancel the order]
            """;

        var document = MermaidDocument.Parse(text);
        var flowchart = (FlowchartDiagram)document.Diagram;
        var svg = flowchart.ToSvg();
        #endregion

        Assert.Equal("flowchart", document.DiagramType);
        Assert.Equal("Order processing", flowchart.Title);
        Assert.Same(DiagramTheme.Forest, flowchart.Theme);
        Assert.Equal(DiagramDirection.LeftToRight, flowchart.Direction);
        Assert.Equal(["start", "check", "ship", "cancel"], flowchart.Nodes.Select(node => node.Id));
        Assert.Equal(FlowchartNodeShape.Stadium, flowchart.Nodes[0].Shape);
        Assert.Equal(FlowchartNodeShape.Diamond, flowchart.Nodes[1].Shape);
        Assert.Equal("yes", flowchart.Edges[1].Label?.Text);
        Assert.Empty(document.Warnings);
        Assert.Contains("Order processing", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors()
    {
        #region Mermaid_Errors
        var text = """
            flowchart TD
                A --> B
                B --> [oops]
            """;

        // TryParse returns the position of the first error instead of throwing a MermaidParseException
        Assert.False(MermaidDocument.TryParse(text, options: null, out _, out var error));
        Assert.Equal((3, 11), (error.Line, error.Column));
        #endregion

        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Equal(error, exception.Error);
        Assert.Contains("(3, 11)", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "(1, 1): The text has no diagram.")]
    [InlineData("   \n\n", "(3, 1): The text has no diagram.")]
    [InlineData("zenuml\n    title Demo", "(1, 1): The diagram type 'zenuml' is not supported yet.")]
    [InlineData("hello", "(1, 1): No diagram type was found: 'hello' is not a type of Mermaid diagram.")]
    [InlineData("---\ntitle: a\nflowchart TD", "(1, 1): The front matter is not closed by a line '---'.")]
    [InlineData("---\n[a\n---\nflowchart TD", "(2, 1): The YAML is not valid")]
    [InlineData("%%{init: {'theme': 'dark'}\nflowchart TD", "(1, 1): The directive is not closed by '}%%'.")]
    [InlineData("flowchart XY", "(1, 11): 'XY' is not a direction")]
    public void InvalidTexts(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.StartsWith(expected, exception.Message, StringComparison.Ordinal);
        Assert.False(MermaidDocument.TryParse(text, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void Directives()
    {
        var document = MermaidDocument.Parse("""
            %%{init: {"theme": "dark", "flowchart": {"curve": "linear", "nodeSpacing": 80}}}%%
            flowchart TD
                %% A comment
                A --> B
            """);
        var flowchart = (FlowchartDiagram)document.Diagram;
        Assert.Same(DiagramTheme.Dark, flowchart.Theme);
        Assert.Equal(EdgeCurve.Linear, flowchart.Curve);
        Assert.Equal(80, flowchart.NodeSpacing);
        Assert.HasCount(2, flowchart.Nodes);
    }

    [Fact]
    public void ThemeVariables()
    {
        #region Mermaid_ThemeVariables
        var document = MermaidDocument.Parse("""
            %%{init: {'theme': 'base', 'themeVariables': {'primaryColor': '#bbf7d0', 'lineColor': '#166534', 'fontSize': '14px'}}}%%
            flowchart LR
                A --> B
            """);

        // The base theme derives the other colors from primaryColor, as Mermaid does
        var theme = document.Diagram.Theme;
        Assert.Equal((SceneColor)"#bbf7d0", theme.NodeBackground);
        Assert.Equal((SceneColor)"#166534", theme.LineColor);
        Assert.Equal(14, theme.FontSize);
        #endregion

        Assert.NotEqual(theme.PrimaryColor, theme.SecondaryColor);
        Assert.NotEqual(theme.PrimaryColor, theme.PrimaryBorderColor);

        var overridden = MermaidDocument.Parse("""
            %%{init: {'theme': 'default', 'themeVariables': {'mainBkg': 'rgb(255, 0, 0)', 'nodeBorder': 'blue', 'fontFamily': 'Arial', 'cScale0': 'hsl(0, 0%, 50%)'}}}%%
            flowchart LR
                A
            """).Diagram.Theme;
        Assert.Equal((SceneColor)"#ff0000", overridden.NodeBackground);
        Assert.Equal((SceneColor)"#0000ff", overridden.NodeBorderColor);
        Assert.Equal("Arial", overridden.FontFamily);
        Assert.Equal((SceneColor)"#808080", overridden.Palette[0]);
        Assert.Equal(DiagramTheme.Default.PrimaryColor, overridden.PrimaryColor);
    }

    [Fact]
    public void Warnings()
    {
        var document = MermaidDocument.Parse("""
            %%{init: {'theme': 'redux', 'look': 'handDrawn'}}%%
            flowchart LR
                A --> B
                click A callback "Tooltip"
                click B href "javascript:alert(1)"
            """);
        Assert.Equal(
            [
                "(1, 1): The theme 'redux' is not supported; the default theme is used.",
                "(1, 1): The hand-drawn look is not supported; the classic look is used.",
                "(4, 5): The JavaScript callbacks of 'click' are not supported.",
                "(5, 5): The link 'javascript:alert(1)' is not allowed: only http, https, mailto and relative addresses are.",
            ],
            document.Warnings.Select(warning => warning.ToString()));
        var flowchart = (FlowchartDiagram)document.Diagram;
        Assert.Equal("Tooltip", flowchart.Nodes[0].Tooltip);
        Assert.Null(flowchart.Nodes[1].Link);
    }

    [Fact]
    public void Limits()
    {
        var text = "flowchart LR\n" + string.Join('\n', Enumerable.Range(0, 20).Select(i => $"a{i} --> b{i}"));
        Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text, new MermaidParseOptions { MaxEdges = 10 }));
        Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text, new MermaidParseOptions { MaxNodes = 10 }));
        Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text, new MermaidParseOptions { MaxTextSize = 10 }));
        var nested = "flowchart LR\n" + string.Concat(Enumerable.Range(0, 5).Select(i => $"subgraph s{i}\n")) + "a\n" + string.Concat(Enumerable.Repeat("end\n", 5));
        Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(nested, new MermaidParseOptions { MaxDepth = 3 }));
        Assert.NotNull(MermaidDocument.Parse(nested));
    }

    [Fact]
    public void DefaultTheme()
    {
        var document = MermaidDocument.Parse("graph TD\nA", new MermaidParseOptions { DefaultTheme = DiagramTheme.Neutral });
        Assert.Same(DiagramTheme.Neutral, document.Diagram.Theme);
        Assert.Contains("flowchart", MermaidDocument.SupportedDiagramTypes);
    }

    [Fact]
    public void Accessibility()
    {
        var flowchart = (FlowchartDiagram)MermaidDocument.Parse("""
            flowchart LR
                accTitle: The title
                accDescr {
                    A description
                    on two lines
                }
                A --> B
            """).Diagram;
        Assert.Equal("The title", flowchart.AccessibleTitle);
        Assert.Equal("A description\non two lines", flowchart.Description);
        var svg = flowchart.ToSvg();
        Assert.Contains("<title>The title</title>", svg, StringComparison.Ordinal);
        Assert.Contains("<desc>A description\non two lines</desc>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        #region Mermaid_Write
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
        var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
        var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
        flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;

        var text = MermaidWriter.Write(flowchart);
        #endregion

        Assert.Equal(
            """
            flowchart LR
                start(["Start"])
                end_node((("End")))
                start -.->|"done"| end_node

            """.ReplaceLineEndings("\n"),
            text);
        Assert.Equal(text, MermaidWriter.Write(MermaidDocument.Parse(text).Diagram));
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        var flowchart = new FlowchartDiagram();
        flowchart.AddNode("has space");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(flowchart));

        var keyword = new FlowchartDiagram();
        keyword.AddNode("end");
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(keyword));

        var heads = new FlowchartDiagram();
        heads.AddEdge("a", "b").StartArrowHead = FlowchartArrowHead.Circle;
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(heads));
        Assert.Throws<ArgumentNullException>(() => MermaidWriter.Write(null!));
    }

    [Fact]
    public void ErrorsAndWarningsFormat()
    {
        Assert.Equal("(2, 3): message", new MermaidParseError("message", 2, 3).ToString());
        Assert.Equal("(2, 3): message", new MermaidWarning("message", 2, 3).ToString());
        Assert.Equal((1, 1), (new MermaidParseException().Error.Line, new MermaidParseException("a").Error.Column));
        Assert.IsType<InvalidOperationException>(new MermaidParseException("a", new InvalidOperationException()).InnerException);
        Assert.False(MermaidDocument.TryParse(null, out _));
    }

    [Fact]
    public void LoneSurrogateInTitleIsReplaced()
    {
        // YAML cannot hold a lone surrogate, so the front matter writes the replacement character
        var diagram = new FlowchartDiagram { Title = "a\uD800b" };
        diagram.AddNode("A");
        var text = MermaidWriter.Write(diagram);
        Assert.Equal("a\uFFFDb", MermaidDocument.Parse(text).Diagram.Title);
    }

    [Fact]
    public void EmptyTitleIsNotWritten()
    {
        var diagram = new FlowchartDiagram { Title = "" };
        diagram.AddNode("A");
        var text = MermaidWriter.Write(diagram);
        Assert.DoesNotContain("title", text, StringComparison.Ordinal);
        Assert.Equal(text, MermaidWriter.Write(MermaidDocument.Parse(text).Diagram));
    }

    [Theory]
    [InlineData("%%{init: {}}%% x")]
    [InlineData("100%% sure")]
    [InlineData("%%%")]
    public void TitleWithPercentSignsRoundTrips(string title)
    {
        // %% starts a directive or a comment anywhere, so the front matter must not contain it
        var diagram = new FlowchartDiagram { Title = title };
        diagram.AddNode("A");
        var text = MermaidWriter.Write(diagram);
        Assert.DoesNotContain("%%", text, StringComparison.Ordinal);
        Assert.Equal(title, MermaidDocument.Parse(text).Diagram.Title);
    }
}
