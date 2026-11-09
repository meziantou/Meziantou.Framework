namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Computes the times of the tasks of a Gantt chart as Mermaid: a task starts at its date, after the tasks it names
/// (the latest of their ends), or after the previous task; it ends at its date, at the earliest start of the tasks it
/// names, or after its duration; the excluded days make it longer. A reference to a task that does not exist, or a
/// cycle, starts at the earliest date of the chart, where Mermaid uses the current date.
/// </summary>
internal static class GanttSchedule
{
    private const int MaxExclusionSteps = 20_000;
    private static readonly DateTime DefaultOrigin = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static List<GanttScheduledTask> Compute(GanttDiagram diagram)
    {
        var tasks = new List<GanttScheduledTask>();
        var groupIndex = 0;
        if (diagram.Tasks.Count > 0)
        {
            foreach (var task in diagram.Tasks)
            {
                tasks.Add(new GanttScheduledTask(task, groupIndex));
            }

            groupIndex++;
        }

        foreach (var section in diagram.Sections)
        {
            foreach (var task in section.Tasks)
            {
                tasks.Add(new GanttScheduledTask(task, groupIndex));
            }

            groupIndex++;
        }

        // The identifiers, explicit or automatic (task1, task2... for the tasks without one); the last one wins
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var automatic = 0;
        for (var i = 0; i < tasks.Count; i++)
        {
            var id = tasks[i].Task.Id;
            if (id is null)
            {
                automatic++;
                id = "task" + automatic.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            ids[id] = i;
        }

        var origin = DateTime.MaxValue;
        foreach (var scheduled in tasks)
        {
            if (scheduled.Task.Start is { } start && start < origin)
            {
                origin = start;
            }

            if (scheduled.Task.End is { } end && end < origin)
            {
                origin = end;
            }
        }

        if (origin == DateTime.MaxValue)
        {
            origin = DefaultOrigin;
        }

        var exclusions = new GanttExclusions(diagram);
        var remaining = tasks.Count;
        for (var pass = 0; pass <= tasks.Count && remaining > 0; pass++)
        {
            var progress = false;
            for (var i = 0; i < tasks.Count; i++)
            {
                if (!tasks[i].IsResolved && TryResolve(i))
                {
                    remaining--;
                    progress = true;
                }
            }

            if (!progress)
                break;
        }

        // The tasks left are in a cycle: they start at the origin
        for (var i = 0; i < tasks.Count; i++)
        {
            if (!tasks[i].IsResolved)
            {
                Finish(tasks[i], origin);
            }
        }

        return tasks;

        bool TryResolve(int index)
        {
            var scheduled = tasks[index];
            var task = scheduled.Task;
            DateTime start;
            if (task.Start is { } date)
            {
                start = date;
            }
            else if (task.After.Count > 0)
            {
                DateTime? latest = null;
                foreach (var id in task.After)
                {
                    if (!ids.TryGetValue(id, out var reference))
                        continue;

                    if (!tasks[reference].IsResolved)
                        return false;

                    if (latest is null || tasks[reference].End > latest)
                    {
                        latest = tasks[reference].End;
                    }
                }

                start = latest ?? origin;
            }
            else if (index > 0)
            {
                if (!tasks[index - 1].IsResolved)
                    return false;

                start = tasks[index - 1].End;
            }
            else
            {
                start = origin;
            }

            if (task.End is null && task.Until.Count > 0)
            {
                foreach (var id in task.Until)
                {
                    if (ids.TryGetValue(id, out var reference) && !tasks[reference].IsResolved && reference != index)
                        return false;
                }
            }

            Finish(scheduled, start);
            return true;
        }

        void Finish(GanttScheduledTask scheduled, DateTime start)
        {
            var task = scheduled.Task;
            DateTime end;
            var manual = false;
            if (task.End is { } date)
            {
                end = date;
                if (diagram.InclusiveEndDates)
                {
                    end = end.AddDays(end <= DateTime.MaxValue.AddDays(-1) ? 1 : 0);
                }

                // Mermaid does not move the end dates written YYYY-MM-DD
                manual = GanttDiagramRenderer.IsIsoDay(diagram.FormatDate(date));
            }
            else if (task.Until.Count > 0)
            {
                DateTime? earliest = null;
                foreach (var id in task.Until)
                {
                    if (ids.TryGetValue(id, out var reference) && tasks[reference].IsResolved && (earliest is null || tasks[reference].Start < earliest))
                    {
                        earliest = tasks[reference].Start;
                    }
                }

                end = earliest ?? origin;
            }
            else if (task.Duration is { } duration)
            {
                end = duration.AddTo(start);
            }
            else
            {
                end = start;
            }

            scheduled.Start = start;
            scheduled.End = end;
            if (exclusions.HasExclusions && !manual)
            {
                ExtendOverExcludedDays(scheduled);
            }

            scheduled.IsResolved = true;
        }

        void ExtendOverExcludedDays(GanttScheduledTask scheduled)
        {
            if (scheduled.Start >= DateTime.MaxValue.AddDays(-1))
                return;

            var day = scheduled.Start.AddDays(1);
            var end = scheduled.End;
            DateTime? renderEnd = null;
            var invalid = false;
            for (var step = 0; day <= end && step < MaxExclusionSteps; step++)
            {
                if (!invalid)
                {
                    renderEnd = end;
                }

                invalid = exclusions.IsExcluded(day);
                if (invalid)
                {
                    if (end >= DateTime.MaxValue.AddDays(-1))
                        break;

                    end = end.AddDays(1);
                }

                if (day >= DateTime.MaxValue.AddDays(-1))
                    break;

                day = day.AddDays(1);
            }

            scheduled.End = end;
            scheduled.RenderEnd = renderEnd;
        }
    }
}
