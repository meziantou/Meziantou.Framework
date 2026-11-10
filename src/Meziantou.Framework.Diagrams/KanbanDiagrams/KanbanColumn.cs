namespace Meziantou.Framework.Diagrams;

/// <summary>A column of a kanban board: a stage of the workflow, with its cards.</summary>
public sealed class KanbanColumn
{
    /// <summary>Initializes a new instance of the <see cref="KanbanColumn"/> class.</summary>
    /// <param name="id">The identifier, which is the title when there is no label.</param>
    /// <param name="label">The title, or <see langword="null"/> to show the identifier.</param>
    public KanbanColumn(string id, DiagramText? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Label = label;
    }

    /// <summary>Gets the identifier.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the title, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets the CSS classes, written to the SVG.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Gets the cards, from top to bottom.</summary>
    public IList<KanbanCard> Cards { get; } = new List<KanbanCard>();

    /// <summary>Adds a card.</summary>
    /// <param name="id">The identifier, which is the text when there is no label.</param>
    /// <param name="label">The text, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The card.</returns>
    public KanbanCard AddCard(string id, DiagramText? label = null)
    {
        var card = new KanbanCard(id, label);
        Cards.Add(card);
        return card;
    }

    /// <summary>Returns the identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
