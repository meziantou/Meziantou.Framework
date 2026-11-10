using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>A kanban board: columns of cards, with their assignee, their ticket and their priority (Mermaid <c>kanban</c>).</summary>
public sealed class KanbanDiagram : Diagram
{
    private double _columnWidth = 200;

    /// <summary>Gets the columns, from left to right.</summary>
    public IList<KanbanColumn> Columns { get; } = new List<KanbanColumn>();

    /// <summary>
    /// Gets or sets the address of a ticket, where <c>#TICKET#</c> is replaced by the ticket of the card, or
    /// <see langword="null"/> for none: the tickets then link nowhere (Mermaid <c>ticketBaseUrl</c>). A ticket links only
    /// to an http, https, mailto or relative address, once <c>#TICKET#</c> is replaced: another address, such as a
    /// <c>javascript:</c> one, is not written.
    /// </summary>
    public string? TicketLinkFormat { get; set; }

    /// <summary>Gets or sets the width of a column, in pixels. The default is 200 (Mermaid <c>sectionWidth</c>).</summary>
    public double ColumnWidth
    {
        get => _columnWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 50);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 2000);
            _columnWidth = value;
        }
    }

    /// <summary>Adds a column.</summary>
    /// <param name="id">The identifier, which is the title when there is no label.</param>
    /// <param name="label">The title, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The column.</returns>
    public KanbanColumn AddColumn(string id, DiagramText? label = null)
    {
        var column = new KanbanColumn(id, label);
        Columns.Add(column);
        return column;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => KanbanDiagramRenderer.Render(this, context, group);
}
