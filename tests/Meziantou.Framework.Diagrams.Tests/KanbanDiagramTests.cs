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
