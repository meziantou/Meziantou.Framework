namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A task of a Gantt chart with its computed times and its row.</summary>
internal sealed class GanttScheduledTask(GanttTask task, int groupIndex)
{
    public GanttTask Task { get; } = task;

    /// <summary>Gets the index of the group of the task: the tasks in no section, then the sections.</summary>
    public int GroupIndex { get; } = groupIndex;

    public bool IsResolved { get; set; }

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    /// <summary>Gets or sets the end of the bar, when the excluded days made the task longer, or <see langword="null"/> for <see cref="End"/>.</summary>
    public DateTime? RenderEnd { get; set; }

    public int Row { get; set; }
}
