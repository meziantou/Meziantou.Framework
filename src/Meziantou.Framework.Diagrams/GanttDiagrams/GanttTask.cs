namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A task of a Gantt chart. It starts at <see cref="Start"/>, or after the tasks of <see cref="After"/>, or else after
/// the previous task; it ends at <see cref="End"/>, or at the start of the first task of <see cref="Until"/>, or after
/// <see cref="Duration"/>, or else at its start.
/// </summary>
public sealed class GanttTask
{
    /// <summary>Initializes a new instance of the <see cref="GanttTask"/> class.</summary>
    /// <param name="name">The name.</param>
    public GanttTask(DiagramText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets or sets the name, shown in the bar or next to it.</summary>
    public DiagramText Name { get; set; }

    /// <summary>Gets or sets the identifier that the other tasks use in <see cref="After"/> and <see cref="Until"/>, or <see langword="null"/> for none.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the start date, or <see langword="null"/> to start after other tasks.</summary>
    public DateTime? Start { get; set; }

    /// <summary>Gets the identifiers of the tasks this task starts after, when <see cref="Start"/> is not set: it starts at the latest of their ends.</summary>
    public IList<string> After { get; } = new List<string>();

    /// <summary>Gets or sets the end date, or <see langword="null"/> to end at <see cref="Until"/> or after <see cref="Duration"/>.</summary>
    public DateTime? End { get; set; }

    /// <summary>Gets the identifiers of the tasks this task lasts until, when <see cref="End"/> is not set: it ends at the earliest of their starts.</summary>
    public IList<string> Until { get; } = new List<string>();

    /// <summary>Gets or sets the duration, when neither <see cref="End"/> nor <see cref="Until"/> are set.</summary>
    public GanttDuration? Duration { get; set; }

    /// <summary>Gets or sets a value that indicates whether the task is done, drawn in gray (Mermaid <c>done</c>).</summary>
    public bool IsDone { get; set; }

    /// <summary>Gets or sets a value that indicates whether the task is in progress (Mermaid <c>active</c>).</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets a value that indicates whether the task is critical, drawn in red (Mermaid <c>crit</c>).</summary>
    public bool IsCritical { get; set; }

    /// <summary>Gets or sets a value that indicates whether the task is a milestone, drawn as a diamond in the middle of its time (Mermaid <c>milestone</c>).</summary>
    public bool IsMilestone { get; set; }

    /// <summary>Gets or sets a value that indicates whether the task is a vertical marker across the chart at its start, with its name below (Mermaid <c>vert</c>).</summary>
    public bool IsVertical { get; set; }

    /// <summary>Gets or sets the address the task links to in the SVG, or <see langword="null"/> for none.</summary>
    public string? Link { get; set; }

    /// <summary>Returns the name of the task.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name.Text;
}
