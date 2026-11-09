namespace Meziantou.Framework.Mermaid.Tests;

public sealed class IshikawaDiagramParserTests
{
    [Fact]
    public void EffectAndCauses()
    {
        #region Mermaid_IshikawaDiagram
        var diagram = (IshikawaDiagram)MermaidDocument.Parse("""
            ishikawa-beta
                Blurry photo
                Process
                    Out of focus
                    Shutter speed too slow
                User
                    Shaky hands
                Equipment
                    Lens
                        Damaged lens
                        Dirty lens
                    Sensor
                        Dirty sensor
                Environment
                    Subject moved too quickly
                    Too dark
            """).Content;
        #endregion

        Assert.Equal("Blurry photo", diagram.Effect?.Text);
        Assert.Equal(["Process", "User", "Equipment", "Environment"], diagram.Causes.Select(cause => cause.Label.Text));
        Assert.Equal(["Lens", "Sensor"], diagram.Causes[2].Causes.Select(cause => cause.Label.Text));
        Assert.Equal(["Damaged lens", "Dirty lens"], diagram.Causes[2].Causes[0].Causes.Select(cause => cause.Label.Text));
    }

    [Fact]
    public void LevelsAreRelativeToTheFirstCause()
    {
        // The effect can be more indented than the causes, and a cause less indented than the first one is a main cause
        var diagram = (IshikawaDiagram)MermaidDocument.Parse("""
            ishikawa-beta
                    Effect
                Cause A
                    Sub A1
                        Deep
                  Odd
              Less
                Back
            """).Content;
        Assert.Equal("Effect", diagram.Effect?.Text);
        Assert.Equal(["Cause A", "Less", "Back"], diagram.Causes.Select(cause => cause.Label.Text));
        Assert.Equal(["Sub A1", "Odd"], diagram.Causes[0].Causes.Select(cause => cause.Label.Text));
        Assert.Equal("Deep", diagram.Causes[0].Causes[0].Causes.Single().Label.Text);
    }

    [Fact]
    public void TabsCountAsOneCharacter()
    {
        var diagram = (IshikawaDiagram)MermaidDocument.Parse("ishikawa-beta\n  Effect\n\tTab\n    Four\n  Two\n").Content;
        var tab = diagram.Causes.Single();
        Assert.Equal("Tab", tab.Label.Text);
        Assert.Equal(["Four", "Two"], tab.Causes.Select(cause => cause.Label.Text));
    }

    [Fact]
    public void TextsCommentsAndKeyword()
    {
        var diagram = (IshikawaDiagram)MermaidDocument.Parse("ISHIKAWA Effect on the keyword line\n  %% comment\n\n  A %% not a comment   \n  B<br/>line<br />three<br>four\n").Content;
        Assert.Equal("Effect on the keyword line", diagram.Effect?.Text);
        Assert.Equal(["A %% not a comment", "B\nline\nthree\nfour"], diagram.Causes.Select(cause => cause.Label.Text));
    }

    [Fact]
    public void EmptyDiagram()
    {
        var diagram = (IshikawaDiagram)MermaidDocument.Parse("ishikawa\n").Content;
        Assert.Null(diagram.Effect);
        Assert.Empty(diagram.Causes);
    }

    [Fact]
    public void FrontMatter()
    {
        var diagram = (IshikawaDiagram)MermaidDocument.Parse("""
            ---
            title: Causes
            config:
              ishikawa:
                diagramPadding: 8
            ---
            ishikawa-beta
              Effect
            """).Content;
        Assert.Equal(("Causes", 8.0), (diagram.Title, diagram.Padding));
    }

    [Theory]
    [InlineData("ishikawa-beta\n  Effect\n  ishikawa cause", "A line cannot start with the keyword 'ishikawa'")]
    [InlineData("ishikawa-beta\n  Effect\n  Ishikawa-Beta", "A line cannot start with the keyword 'ishikawa'")]
    [InlineData("ishikawa-beta\n Effect\n  a\n   b\n    c\n     d", "The causes are nested deeper than 3 levels.")]
    public void InvalidIshikawaDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text, new MermaidParseOptions { MaxDepth = 3 }));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooManyCauses()
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse("ishikawa-beta\n Effect\n a\n b\n c", new MermaidParseOptions { MaxNodes = 2 }));
        Assert.Contains("The diagram has more than 2 causes.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new IshikawaDiagram { Title = "Causes", Padding = 12, Effect = "Effect\non two lines" };
        var cause = diagram.AddCause("Cause %% with a percent");
        cause.AddCause("Sub-cause").AddCause("Deep cause");
        diagram.AddCause("<b>raw</b> #quot;");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (IshikawaDiagram)parsed.Content;
        Assert.Equal(("Causes", 12.0, "Effect\non two lines"), (result.Title, result.Padding, result.Effect?.Text));
        Assert.Equal(["Cause %% with a percent", "Sub-cause", "Deep cause", "<b>raw</b> #quot;"], result.EnumerateCauses().Select(cause => cause.Label.Text));
        Assert.Equal("Deep cause", result.Causes[0].Causes[0].Causes.Single().Label.Text);
    }

    [Fact]
    public void WriteEmptyDiagram()
    {
        Assert.Equal("ishikawa-beta\n", MermaidWriter.Write(new IshikawaDiagram()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing\t")]
    [InlineData("%% comment")]
    [InlineData("a %%{init: {}}%%")]
    [InlineData("ishikawa cause")]
    [InlineData("line\r")]
    public void WriteRejectsUnreadableTexts(string label)
    {
        var diagram = new IshikawaDiagram { Effect = "Effect" };
        diagram.AddCause(label);
        Assert.Throws<ArgumentException>(nameof(label), () => MermaidWriter.Write(diagram));
    }

    [Fact]
    public void WriteRejectsUnsupportedDiagrams()
    {
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(new IshikawaDiagram { AccessibleTitle = "Title", Effect = "Effect" }));
        var withoutEffect = new IshikawaDiagram();
        withoutEffect.AddCause("Cause");
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(withoutEffect));
        var cycle = new IshikawaDiagram { Effect = "Effect" };
        var cause = cycle.AddCause("Cause");
        cause.Causes.Add(cause);
        Assert.Throws<ArgumentException>("cause", () => MermaidWriter.Write(cycle));
    }
}
