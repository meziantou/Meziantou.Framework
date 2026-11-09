namespace Meziantou.Framework.Diagrams;

/// <summary>A section of a Gantt chart: tasks drawn on rows of the same color, with the title on the left.</summary>
public sealed class GanttSection
{
    /// <summary>Initializes a new instance of the <see cref="GanttSection"/> class.</summary>
    /// <param name="title">The title.</param>
    public GanttSection(DiagramText title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }

    /// <summary>Gets or sets the title.</summary>
    public DiagramText Title { get; set; }

    /// <summary>Gets the tasks, in order.</summary>
    public IList<GanttTask> Tasks { get; } = new List<GanttTask>();

    /// <summary>Adds a task.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The task.</returns>
    public GanttTask AddTask(DiagramText name)
    {
        var task = new GanttTask(name);
        Tasks.Add(task);
        return task;
    }
}
