namespace Meziantou.Framework.Mermaid.Tests;

public sealed class KanbanDiagramParserTests
{
    [Fact]
    public void ColumnsAndCards()
    {
        #region Mermaid_KanbanDiagram
        var diagram = (KanbanDiagram)MermaidDocument.Parse("""
            ---
            config:
              kanban:
                ticketBaseUrl: 'https://tracker.example.com/browse/#TICKET#'
            ---
            kanban
              todo[To do]
                docs[Write the documentation]
                parser[Write the parser]@{ ticket: DIA-12, priority: 'High' }
              doing[In progress]
                renderer[Draw the cards]@{ assigned: 'Alice', priority: 'Very High' }
              [Done]
                [Design the model]
            """).Content;
        #endregion

        Assert.Equal("https://tracker.example.com/browse/#TICKET#", diagram.TicketLinkFormat);
        Assert.Equal(["todo", "doing", "Done"], diagram.Columns.Select(column => column.Id));
        Assert.Equal("To do", diagram.Columns[0].Label?.Text);
        Assert.Null(diagram.Columns[2].Label);
        var parser = diagram.Columns[0].Cards[1];
        Assert.Equal(("parser", "DIA-12", (KanbanPriority?)KanbanPriority.High), (parser.Id, parser.Ticket, parser.Priority));
        var renderer = diagram.Columns[1].Cards.Single();
        Assert.Equal(("Alice", (KanbanPriority?)KanbanPriority.VeryHigh), (renderer.Assigned, renderer.Priority));
        Assert.Equal("Design the model", diagram.Columns[2].Cards.Single().Id);
    }

    [Fact]
    public void TicketsCannotCompleteAScriptLink()
    {
        var document = MermaidDocument.Parse("""
            ---
            config:
              kanban:
                ticketBaseUrl: '#TICKET#'
            ---
            kanban
              todo[To do]
                card[Card]@{ ticket: "javascript:alert(1)" }
            """);

        Assert.Equal("#TICKET#", ((KanbanDiagram)document.Content).TicketLinkFormat);
        Assert.DoesNotContain("href=\"javascript:", ((KanbanDiagram)document.Content).ToSvg(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShapesTextsAndDecorations()
    {
        var document = MermaidDocument.Parse("""
            kanban
            column(Rounded)
              :::urgent wide
              a((Circle))
              b{{"Quoted #quot;text#quot;"}}
              c["`**Bold**
                markdown`"]
              ::icon(fa fa-book)
              d
                e[Deeper is a card of the column]
              f[Card]@{ label: 'Label', icon: 'book', other: 1 }
            """);
        var diagram = (KanbanDiagram)document.Content;
        var column = diagram.Columns.Single();
        Assert.Equal(["urgent", "wide"], column.CssClasses);
        Assert.Equal(["a", "b", "c", "d", "e", "f"], column.Cards.Select(card => card.Id));
        Assert.Equal("Circle", column.Cards[0].Label?.Text);
        Assert.Equal("Quoted \"text\"", column.Cards[1].Label?.Text);
        Assert.Equal(DiagramText.Markdown("**Bold**\nmarkdown"), column.Cards[2].Label);
        Assert.Null(column.Cards[3].Label);
        Assert.Equal("Label", column.Cards[5].Label?.Text);
        Assert.HasCount(3, document.Warnings);
    }

    [Theory]
    [InlineData("kanban\n  a\nb", "Items without column detected")]
    [InlineData("kanban\n  a[text", "The item is not closed.")]
    [InlineData("kanban\n  a[]", "The text cannot be empty.")]
    [InlineData("kanban\n  kanbanBoard", "the identifiers cannot start with 'kanban'")]
    [InlineData("kanban\n  :::cls", "The classes decorate no item.")]
    [InlineData("kanban\n  a@{ shape: Rect }", "Shape names should be lowercase.")]
    [InlineData("kanban\n  a@{ ticket: x", "The data is not closed by '}'.")]
    [InlineData("kanban\n  a{b}", "Unexpected '{b}'.")]
    public void InvalidKanbanDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new KanbanDiagram { Title = "Board", ColumnWidth = 250, TicketLinkFormat = "https://example.com/#TICKET#" };
        var todo = diagram.AddColumn("todo", "To [do]");
        todo.CssClasses.Add("hot");
        var card = todo.AddCard("card", "Line 1\nLine \"2\"");
        card.Ticket = "T-1";
        card.Assigned = "O'Brien";
        card.Priority = KanbanPriority.VeryLow;
        todo.AddCard("same", "same");
        var odd = diagram.AddColumn(" odd [id] ", DiagramText.Markdown("**Odd**"));
        odd.AddCard("kanban card");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (KanbanDiagram)parsed.Content;
        Assert.Equal(("Board", 250.0, "https://example.com/#TICKET#"), (result.Title, result.ColumnWidth, result.TicketLinkFormat));
        Assert.Equal(["todo", " odd [id] "], result.Columns.Select(column => column.Id));
        Assert.Equal(("To [do]", ["hot"]), (result.Columns[0].Label?.Text, result.Columns[0].CssClasses.ToArray()));
        var resultCard = result.Columns[0].Cards[0];
        Assert.Equal(("Line 1\nLine \"2\"", "T-1", "O'Brien", (KanbanPriority?)KanbanPriority.VeryLow), (resultCard.Label?.Text, resultCard.Ticket, resultCard.Assigned, resultCard.Priority));
        Assert.Null(result.Columns[0].Cards[1].Label);
        Assert.Equal("**Odd**", result.Columns[1].Label?.Text);
        Assert.Equal("kanban card", result.Columns[1].Cards[0].Id);
    }

    [Fact]
    public void WriteRejectsAccessibility()
    {
        var diagram = new KanbanDiagram { AccessibleTitle = "Title" };
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(diagram));
    }
}
