namespace Meziantou.Framework.Mermaid.Tests;

public sealed class CynefinDiagramParserTests
{
    private static CynefinDiagram Parse(string text) => (CynefinDiagram)MermaidDocument.Parse(text).Content;

    [Fact]
    public void Cynefin()
    {
        #region Mermaid_CynefinDiagram
        var diagram = (CynefinDiagram)MermaidDocument.Parse("""
            cynefin-beta
              title Strategy categorization
              complex
                "Market research"
              complicated
                "Competitive analysis"
              clear
                "Standard pricing"
              chaotic
                "Crisis management"
              complex --> complicated : "Pattern identified"
              clear --> chaotic : "Complacency"
            """).Content;
        #endregion

        Assert.Equal("Strategy categorization", diagram.Title);
        Assert.Equal([(CynefinDomain.Complex, "Market research"), (CynefinDomain.Complicated, "Competitive analysis"), (CynefinDomain.Clear, "Standard pricing"), (CynefinDomain.Chaotic, "Crisis management")], diagram.Items.Select(item => (item.Domain, item.Label.Text)));
        Assert.Equal([(CynefinDomain.Complex, CynefinDomain.Complicated, "Pattern identified"), (CynefinDomain.Clear, CynefinDomain.Chaotic, "Complacency")], diagram.Transitions.Select(transition => (transition.From, transition.To, transition.Label?.Text)));
    }

    [Fact]
    public void ConfigurationAndThemeVariables()
    {
        var diagram = Parse("""
            ---
            config:
              cynefin:
                width: 1000
                height: 700
                padding: 10
                showDomainDescriptions: false
                boundaryAmplitude: 0
                seed: 7
              themeVariables:
                cynefin:
                  complexBg: "#112233"
                  cliffWidth: 6
                  boundaryWidth: 0
            ---
            cynefin-beta:
              accTitle: Accessible
              accDescr: Described
            """);
        Assert.Equal((1000.0, 700.0, 10.0, false, 0.0, 7), (diagram.Width, diagram.Height, diagram.Padding, diagram.ShowDomainDescriptions, diagram.BoundaryAmplitude, diagram.Seed));
        Assert.Equal((SceneColor.Parse("#112233"), (double?)6, (double?)null), (diagram.Colors.ComplexBackground, diagram.Colors.CliffWidth, diagram.Colors.BoundaryWidth));
        Assert.Equal(("Accessible", "Described"), (diagram.AccessibleTitle, diagram.Description));
    }

    [Fact]
    public void GrammarOfMermaid()
    {
        var document = MermaidDocument.Parse("""
            cynefin-beta complex "a" 'b \'quoted\''
              complex %% A second block replaces the items of the first one
                "c\nd" %% comment
                "e\\f"
              clear
              confusion "x"
              chaotic-->complex:"Stabilized"
              complex --> complex : "Ignored"
              clear --> chaotic : ""
              title   Collapsed    spaces
            """);
        var diagram = (CynefinDiagram)document.Content;
        Assert.Equal([(CynefinDomain.Complex, "c\nd"), (CynefinDomain.Complex, "e\\f"), (CynefinDomain.Confusion, "x")], diagram.Items.Select(item => (item.Domain, item.Label.Text)).OrderBy(item => item.Domain));
        Assert.Equal([(CynefinDomain.Chaotic, CynefinDomain.Complex, "Stabilized"), (CynefinDomain.Clear, CynefinDomain.Chaotic, null)], diagram.Transitions.Select(transition => (transition.From, transition.To, transition.Label?.Text)));
        Assert.Equal("Collapsed spaces", diagram.Title);
        Assert.Contains(document.Warnings, warning => warning.Message.Contains("to itself is ignored", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cynefin-beta\n  complexity", "Unexpected 'ity'")]
    [InlineData("cynefin-beta\n  Complex", "Unexpected 'Complex'")]
    [InlineData("cynefin-beta\n  \"orphan\"", "Unexpected '\"orphan\"'")]
    [InlineData("cynefin-beta\n  complex -> clear", "Unexpected '->'")]
    [InlineData("cynefin-beta\n  complex --> clear : \"x\" complex", "the end of the line is expected.")]
    [InlineData("cynefin-beta\n  complex --> clear :\n", "Unexpected 'end of line': a quoted label is expected.")]
    [InlineData("cynefin-beta\n  complex --> \"x\"", "the domain the transition goes to is expected.")]
    [InlineData("cynefin-beta\n  complex\n  \"unclosed", "The string is not closed.")]
    [InlineData("cynefin-beta\n  complex ; clear", "Unexpected ';'")]
    [InlineData("cynefin-beta-x", "is not a type of Mermaid diagram")]
    public void InvalidCynefinDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new CynefinDiagram { Title = "Title", Description = "Two\nlines", Width = 640, Padding = 20, ShowDomainDescriptions = false, BoundaryAmplitude = 2.5, Seed = -3 };
        diagram.AddItem(CynefinDomain.Clear, "Quote \" and backslash \\");
        diagram.AddItem(CynefinDomain.Complex, "%%{init: {}}%% is not a directive");
        diagram.AddItem(CynefinDomain.Clear, "Line\nbreak");
        diagram.AddTransition(CynefinDomain.Chaotic, CynefinDomain.Complex, "Stabilized");
        diagram.AddTransition(CynefinDomain.Clear, CynefinDomain.Clear);
        diagram.AddTransition(CynefinDomain.Confusion, CynefinDomain.Clear, string.Empty);
        diagram.Colors.ConfusionBackground = SceneColor.Parse("#abcdef80");
        diagram.Colors.ArrowWidth = 3;
        diagram.Colors.CliffWidth = 0;

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (CynefinDiagram)parsed.Content;
        Assert.Equal(("Title", "Two\nlines", 640.0, 20.0, false, 2.5, -3), (result.Title, result.Description, result.Width, result.Padding, result.ShowDomainDescriptions, result.BoundaryAmplitude, result.Seed));
        Assert.Equal([(CynefinDomain.Clear, "Quote \" and backslash \\"), (CynefinDomain.Clear, "Line\nbreak"), (CynefinDomain.Complex, "%%{init: {}}%% is not a directive")], result.Items.Select(item => (item.Domain, item.Label.Text)));
        Assert.Equal([(CynefinDomain.Chaotic, CynefinDomain.Complex, "Stabilized"), (CynefinDomain.Confusion, CynefinDomain.Clear, null)], result.Transitions.Select(transition => (transition.From, transition.To, transition.Label?.Text)));
        Assert.Equal((SceneColor.Parse("#abcdef80"), (double?)3, (double?)null), (result.Colors.ConfusionBackground, result.Colors.ArrowWidth, result.Colors.CliffWidth));
    }

    [Fact]
    public void WriteEmptyDiagram()
    {
        Assert.Equal("cynefin-beta\n", MermaidWriter.Write(new CynefinDiagram()));
        Assert.Empty(Parse("cynefin-beta\n").Items);
    }
}
