namespace Meziantou.Framework.Diagrams;

/// <summary>A card of a kanban board: a task, with its assignee, its ticket and its priority.</summary>
public sealed class KanbanCard
{
    /// <summary>Initializes a new instance of the <see cref="KanbanCard"/> class.</summary>
    /// <param name="id">The identifier, which is the text when there is no label.</param>
    /// <param name="label">The text, or <see langword="null"/> to show the identifier.</param>
    public KanbanCard(string id, DiagramText? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Label = label;
    }

    /// <summary>Gets the identifier.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the text, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the person the task is assigned to, shown on the bottom right, or <see langword="null"/> for none.</summary>
    public string? Assigned { get; set; }

    /// <summary>Gets or sets the ticket of the task, shown on the bottom left, or <see langword="null"/> for none.</summary>
    public string? Ticket { get; set; }

    /// <summary>Gets or sets the priority, shown as a colored stripe on the left, or <see langword="null"/> for none.</summary>
    public KanbanPriority? Priority { get; set; }

    /// <summary>Gets the CSS classes, written to the SVG.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Returns the identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
