namespace Meziantou.Framework.Diagrams.Tests;

public sealed class KanbanDiagramTests
{
    [Fact]
    public void KanbanDiagram()
    {
        #region Diagrams_KanbanDiagram
        var diagram = new KanbanDiagram { TicketLinkFormat = "https://tracker.example.com/browse/#TICKET#" };
        var todo = diagram.AddColumn("todo", "To do");
        todo.AddCard("docs", "Write the documentation");
        var parser = todo.AddCard("parser", "Write the parser");
        parser.Priority = KanbanPriority.High;
        parser.Ticket = "DIA-12";

        var doing = diagram.AddColumn("doing", "In progress");
        var renderer = doing.AddCard("renderer", DiagramText.Markdown("Draw the **cards**"));
        renderer.Assigned = "Alice";
        renderer.Priority = KanbanPriority.VeryHigh;

        var done = diagram.AddColumn("done", "Done");
        done.AddCard("model", "Design the model").Priority = KanbanPriority.Low;
        #endregion

        var svg = diagram.ToSvg();
        Assert.Contains("https://tracker.example.com/browse/DIA-12", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg, "KanbanDiagram");
    }

    [Fact]
    public void ThemesAndWrapping()
    {
        var diagram = new KanbanDiagram { Theme = DiagramTheme.Dark, ColumnWidth = 150 };
        var column = diagram.AddColumn("column");
        column.AddCard("long", "A card with a long text, which wraps on several lines in the narrow column").Priority = KanbanPriority.VeryLow;
        column.AddCard("medium").Priority = KanbanPriority.Medium;
        diagram.AddColumn("empty", "Empty column");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void TicketsCannotCompleteAScriptLink()
    {
        var diagram = new KanbanDiagram { TicketLinkFormat = "#TICKET#" };
        var column = diagram.AddColumn("todo", "To do");
        column.AddCard("script", "Script").Ticket = "javascript:alert(1)";
        column.AddCard("safe", "Safe").Ticket = "MC-1";

        var svg = diagram.ToSvg();
        Assert.DoesNotContain("href=\"javascript:", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"MC-1\"", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "MC-1", null)]
    [InlineData("https://tracker.example.com/browse/#TICKET#", "MC-1", "https://tracker.example.com/browse/MC-1")]
    [InlineData("#TICKET#", "MC-1", "MC-1")]
    [InlineData("#TICKET#", "https://example.com/1", "https://example.com/1")]
    [InlineData("#TICKET#", "MAILTO:a@example.com", "MAILTO:a@example.com")]
    [InlineData("/browse/#TICKET#", "javascript:alert(1)", "/browse/javascript:alert(1)")]
    [InlineData("#TICKET#", "javascript:alert(1)", null)]
    [InlineData("#TICKET#", " JavaScript:alert(1)", null)]
    [InlineData("#TICKET#", "java\tscript:alert(1)", null)]
    [InlineData("#TICKET#", "\u0001javascript:alert(1)", null)]
    [InlineData("#TICKET#:x", "data", null)]
    public void TicketLinks(string? format, string ticket, string? expected)
    {
        Assert.Equal(expected, Rendering.KanbanDiagramRenderer.GetTicketLink(format, ticket));
    }

    [Fact]
    public void ColumnWidthIsBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new KanbanDiagram { ColumnWidth = 10 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new KanbanDiagram { ColumnWidth = 5000 });
    }

    [Fact]
    public void IdentifiersAreRequired()
    {
        Assert.Throws<ArgumentException>("id", () => new KanbanColumn(""));
        Assert.Throws<ArgumentException>("id", () => new KanbanCard(""));
    }
}
