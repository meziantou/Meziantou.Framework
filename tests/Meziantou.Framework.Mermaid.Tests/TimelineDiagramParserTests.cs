namespace Meziantou.Framework.Mermaid.Tests;

public sealed class TimelineDiagramParserTests
{
    private static TimelineDiagram Parse(string text) => (TimelineDiagram)MermaidDocument.Parse(text).Content;

    [Fact]
    public void PeriodsAndEvents()
    {
        #region Mermaid_TimelineDiagram
        var diagram = (TimelineDiagram)MermaidDocument.Parse("""
            timeline
                title History of Social Media Platform
                2002 : LinkedIn
                2004 : Facebook
                     : Google
                2005 : YouTube : Twitch
                2006
            """).Content;
        #endregion

        Assert.Equal("History of Social Media Platform", diagram.Title);
        Assert.Equal(["2002", "2004", "2005", "2006"], diagram.Periods.Select(period => period.Label.Text));
        Assert.Equal(["Facebook", "Google"], diagram.Periods[1].Events.Select(item => item.Text));
        Assert.Equal(["YouTube", "Twitch"], diagram.Periods[2].Events.Select(item => item.Text));
        Assert.Empty(diagram.Periods[3].Events);
        Assert.Equal(TimelineDirection.LeftToRight, diagram.Direction);
    }

    [Fact]
    public void SectionsDirectionAndText()
    {
        var diagram = Parse("""
            ---
            config:
              timeline:
                disableMulticolor: true
            ---
            timeline TD
                # a comment
                section 2023 Q1 <br> Release
                  Bullet 1 : sub-point <br> 1a : 12:30 is a time : #35;hash # not a comment
                Industrial &amp; Co # a comment
                %% another comment
                section Q2
                  Bullet 2
            """);
        Assert.Equal(TimelineDirection.TopToBottom, diagram.Direction);
        Assert.True(diagram.DisableMulticolor);
        Assert.Empty(diagram.Periods);
        Assert.Equal(["2023 Q1 \n Release", "Q2"], diagram.Sections.Select(section => section.Title.Text));
        var periods = diagram.Sections[0].Periods;
        Assert.Equal(["sub-point \n 1a", "12:30 is a time", "#hash # not a comment"], periods[0].Events.Select(item => item.Text));
        Assert.Equal("Industrial & Co", periods[1].Label.Text);
        Assert.Equal("Bullet 2", diagram.Sections[1].Periods[0].Label.Text);
    }

    [Theory]
    [InlineData("timeline\n: event", "An event follows a period.")]
    [InlineData("timeline\n2002 :event", "An event starts with a colon and a space")]
    [InlineData("timeline\n2002 :  ", "The text of the event is expected.")]
    [InlineData("timeline\nsection : x", "The title of the section is expected.")]
    [InlineData("timeline\ntimeline", "The diagram has one 'timeline' keyword")]
    [InlineData("timeline RL", "a statement is alone on its line.")]
    [InlineData("timeline\nACCTITLE: x", "'accTitle' is followed by a colon")]
    public void InvalidTimelines(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new TimelineDiagram { Title = "History", Direction = TimelineDirection.TopToBottom, DisableMulticolor = true, Theme = DiagramTheme.Dark };
        diagram.AddPeriod("Before & <after>", "An event: with a colon", " Spaces ");
        var section = diagram.AddSection("Title of the section");
        section.AddPeriod("Two\nlines", "#1 event\nwith two lines");
        section.AddPeriod("Empty");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (TimelineDiagram)parsed.Content;
        Assert.Equal(("History", TimelineDirection.TopToBottom, true), (result.Title, result.Direction, result.DisableMulticolor));
        Assert.Equal("Before & <after>", result.Periods[0].Label.Text);
        Assert.Equal(["An event: with a colon", " Spaces "], result.Periods[0].Events.Select(item => item.Text));
        Assert.Equal("Title of the section", result.Sections[0].Title.Text);
        Assert.Equal(["Two\nlines", "Empty"], result.Sections[0].Periods.Select(period => period.Label.Text));
        Assert.Equal(["#1 event\nwith two lines"], result.Sections[0].Periods[0].Events.Select(item => item.Text));
        Assert.Equal("""
            ---
            config:
              theme: dark
              timeline:
                disableMulticolor: true
            ---
            timeline TD
                title History
                Before &amp; &lt;after&gt; : An event#58; with a colon : #32;Spaces#32;
                section Title of the section
                    Two<br>lines : #35;1 event<br>with two lines
                    Empty

            """, text);
    }

    [Fact]
    public void PeriodsNamedAsKeywords()
    {
        var diagram = Parse("timeline\ntitle: event\nSection\nsectionX : other");
        Assert.Null(diagram.Title);
        Assert.Equal(["title", "Section", "sectionX"], diagram.Periods.Select(period => period.Label.Text));

        var text = MermaidWriter.Write(diagram);
        Assert.Equal("timeline\n    title: event\n    Section\n    sectionX : other\n", text);
        Assert.Equal(text, MermaidWriter.Write(MermaidDocument.Parse(text).Content));
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotExpress()
    {
        foreach (var label in new[] { "a: b", "#1", " space", "title of the period", "100%%", "Timeline", "accDescr" })
        {
            var diagram = new TimelineDiagram();
            diagram.AddPeriod(label);
            Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
        }
    }
}
