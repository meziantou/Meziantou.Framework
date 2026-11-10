using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the Gantt charts as Mermaid: a row per task (or per lane of a section in compact mode), the rows of a section
/// in its color, the titles of the sections on the left, the time axis at the bottom, and the bars of the tasks.
/// </summary>
internal static class GanttDiagramRenderer
{
    private const double TopPadding = 50;
    private const double RightPadding = 75;
    private const double GridLineStartPadding = 35;
    private const double TodayMargin = 25;
    private const int SectionStyles = 4;

    public static SceneBounds Render(GanttDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var colors = GanttColors.For(theme);
        var fontSize = theme.FontSize * 11 / 16;
        var tasks = GanttSchedule.Compute(diagram);
        var rowTasks = tasks.Where(task => !task.Task.IsVertical).ToList();

        // The groups that have rows, in order: they set the colors
        var groups = rowTasks.Select(task => task.GroupIndex).Distinct().ToList();
        var groupRows = AssignRows(diagram, rowTasks, groups);
        var rowCount = groupRows.Values.Sum();
        var barHeight = diagram.BarHeight;
        var gap = barHeight + diagram.BarGap;
        var leftPadding = diagram.LeftPadding;
        var width = diagram.Width;
        var height = (2 * TopPadding) + (rowCount * gap);
        var range = Math.Max(1, width - leftPadding - RightPadding);
        var bounds = new BoundsBuilder();
        bounds.Add(new SceneBounds(0, 0, width, height));

        DateTime minimum = default, maximum = default;
        if (tasks.Count > 0)
        {
            minimum = tasks.Min(task => task.Start);
            maximum = tasks.Max(task => task.End);
        }

        double X(DateTime date)
        {
            var span = (maximum - minimum).TotalMilliseconds;
            return span <= 0 ? range / 2 : Math.Round(range * (date - minimum).TotalMilliseconds / span, MidpointRounding.AwayFromZero);
        }

        DrawExcludedDays();
        if (tasks.Count > 0)
        {
            DrawAxis(top: false);
            if (diagram.TopAxis)
            {
                DrawAxis(top: true);
            }
        }

        DrawRows();
        DrawTasks();
        DrawSectionTitles();
        DrawToday();
        return bounds.ToBounds();

        int ColorIndex(int groupIndex) => groups.IndexOf(groupIndex) % SectionStyles;

        void DrawExcludedDays()
        {
            var exclusions = new GanttExclusions(diagram);
            if (!exclusions.HasRules || tasks.Count is 0 || (maximum - minimum).TotalDays > 5 * 366)
                return;

            var layer = group.Add(new SceneGroup { CssClass = "excludes" });
            DateTime? rangeStart = null;
            DateTime rangeEnd = default;
            for (var day = minimum; day <= maximum; day = day.AddDays(1))
            {
                if (exclusions.IsExcluded(day))
                {
                    rangeStart ??= day;
                    rangeEnd = day;
                }
                else if (rangeStart is { } startDay)
                {
                    AddRange(startDay, rangeEnd);
                    rangeStart = null;
                }

                if (day >= DateTime.MaxValue.AddDays(-1))
                    break;
            }

            void AddRange(DateTime start, DateTime end)
            {
                var x1 = X(start.Date) + leftPadding;
                var x2 = X(end.Date.AddDays(1).AddMilliseconds(-1)) + leftPadding;
                layer.Add(new SceneRectangle(x1, GridLineStartPadding, Math.Max(0, x2 - x1), Math.Max(0, height - TopPadding - GridLineStartPadding)) { Fill = colors.ExcludeBackground, CssClass = "exclude-range" });
            }
        }

        void DrawAxis(bool top)
        {
            var axisFormat = diagram.AxisFormat ?? (diagram.DateFormat is "D" ? "%d" : "%Y-%m-%d");
            var ticks = GanttTimeAxis.GetTicks(minimum, maximum, diagram.TickInterval, diagram.WeekStart);
            var axis = group.Add(new SceneGroup { CssClass = "grid" });
            var axisY = top ? TopPadding : height - 50;
            var lineEnd = top ? axisY + (height - TopPadding - GridLineStartPadding) : GridLineStartPadding;
            foreach (var tick in ticks)
            {
                var x = X(tick) + leftPadding;
                axis.Add(new SceneLine(x, axisY, x, lineEnd) { Stroke = colors.Grid, StrokeWidth = 1, Opacity = 0.8 });
                var label = context.Text(GanttTimeFormat.Format(axisFormat, tick), fontSize * 10 / 11);
                var labelY = top ? axisY - 3 - (label.Height / 2) : axisY + 3 + (label.Height / 2);
                label.Draw(axis, x, labelY, theme.TextColor);
                bounds.Add(new SceneBounds(x - (label.Width / 2), labelY - (label.Height / 2), label.Width, label.Height));
            }
        }

        void DrawRows()
        {
            var layer = group.Add(new SceneGroup { CssClass = "sections" });
            var rowTop = 0;
            foreach (var groupIndex in groups)
            {
                var color = ColorIndex(groupIndex) switch
                {
                    0 => colors.SectionBackground,
                    2 => colors.SectionBackground2,
                    _ => colors.AlternateSectionBackground,
                };
                for (var row = 0; row < groupRows[groupIndex]; row++)
                {
                    layer.Add(new SceneRectangle(0, ((rowTop + row) * gap) + TopPadding - 2, width - (RightPadding / 2), gap) { Fill = color, Opacity = 0.2, CssClass = "section section" + ColorIndex(groupIndex).ToString(System.Globalization.CultureInfo.InvariantCulture) });
                }

                rowTop += groupRows[groupIndex];
            }
        }

        void DrawTasks()
        {
            var layer = group.Add(new SceneGroup { CssClass = "tasks" });
            var texts = group.Add(new SceneGroup { CssClass = "task-texts" });
            foreach (var scheduled in tasks.OrderBy(task => task.Start).ThenBy(task => task.Task.IsVertical ? 1 : 0))
            {
                var task = scheduled.Task;
                var startX = X(scheduled.Start);
                var endX = X(scheduled.End);
                var (fill, stroke) = (task.IsActive, task.IsDone, task.IsCritical) switch
                {
                    (true, _, true) => (colors.ActiveTaskBackground, colors.CriticalBorder),
                    (true, _, false) => (colors.ActiveTaskBackground, colors.ActiveTaskBorder),
                    (false, true, true) => (colors.DoneTaskBackground, colors.CriticalBorder),
                    (false, true, false) => (colors.DoneTaskBackground, colors.DoneTaskBorder),
                    (false, false, true) => (colors.CriticalBackground, colors.CriticalBorder),
                    _ => (colors.TaskBackground, colors.TaskBorder),
                };
                var taskGroup = layer.Add(new SceneGroup { CssClass = GetCssClass(task), Hyperlink = task.Link });
                if (task.Id is { } id)
                {
                    taskGroup.SetDataAttribute("id", id);
                }

                var label = context.Text(task.Name, fontSize);
                var top = (scheduled.Row * gap) + TopPadding;
                if (task.IsVertical)
                {
                    var markerX = startX + leftPadding;
                    var markerHeight = (rowCount * gap) + (barHeight * 2);
                    taskGroup.Add(new SceneRectangle(markerX, GridLineStartPadding, 0.08 * barHeight, markerHeight) { Fill = stroke, Stroke = stroke, StrokeWidth = theme.StrokeWidth });
                    var labelY = GridLineStartPadding + (rowCount * gap) + 60;
                    label.Draw(taskGroup, markerX, labelY - (label.Ascent / 2), theme.TextColor);
                    bounds.Add(new SceneBounds(markerX - (label.Width / 2), labelY - label.Height, label.Width, label.Height));
                    continue;
                }

                double barLeft;
                double barRight;
                if (task.IsMilestone)
                {
                    var center = startX + leftPadding + (0.5 * (endX - startX));
                    var half = barHeight * 0.8 / Math.Sqrt(2);
                    var middle = top + (barHeight / 2);
                    taskGroup.Add(new ScenePath(Geometry.Polygon((center, middle - half), (center + half, middle), (center, middle + half), (center - half, middle))) { Fill = fill, Stroke = stroke, StrokeWidth = 2 });
                    barLeft = center - (barHeight / 2) - leftPadding;
                    barRight = barLeft + barHeight;
                }
                else
                {
                    var renderEndX = X(scheduled.RenderEnd ?? scheduled.End);
                    taskGroup.Add(new SceneRectangle(startX + leftPadding, top, Math.Max(0, renderEndX - startX), barHeight) { Fill = fill, Stroke = stroke, StrokeWidth = 2, CornerRadius = 3 });
                    barLeft = startX;
                    barRight = renderEndX;
                }

                // The name is in the bar when it fits, otherwise on its right, or on its left at the right edge
                var textY = top + (barHeight / 2);
                SceneColor textColor;
                double textX;
                if (label.Width > barRight - barLeft)
                {
                    textColor = colors.TaskTextOutside;
                    if (barRight + label.Width + (1.5 * leftPadding) > width)
                    {
                        textX = barLeft + leftPadding - 5 - (label.Width / 2);
                    }
                    else
                    {
                        textX = barRight + leftPadding + 5 + (label.Width / 2);
                    }
                }
                else
                {
                    textColor = task.IsActive || task.IsDone || task.IsCritical ? colors.TaskTextDark : colors.TaskText;
                    textX = ((barLeft + barRight) / 2) + leftPadding;
                }

                var textGroup = texts.Add(new SceneGroup { CssClass = "task-text", Hyperlink = task.Link });
                label.Draw(textGroup, textX, textY, textColor);
                bounds.Add(new SceneBounds(textX - (label.Width / 2), textY - (label.Height / 2), label.Width, label.Height));
            }
        }

        void DrawSectionTitles()
        {
            var layer = group.Add(new SceneGroup { CssClass = "section-titles" });
            var rowTop = 0;
            foreach (var groupIndex in groups)
            {
                var title = GetGroupTitle(diagram, groupIndex);
                var rows = groupRows[groupIndex];
                if (title is { IsEmpty: false })
                {
                    var text = context.Text(title, fontSize);
                    var y = (rows * gap / 2) + (rowTop * gap) + TopPadding;
                    text.Draw(layer, 10 + (text.Width / 2), y, theme.TitleColor, SceneTextAnchor.Start);
                    bounds.Add(new SceneBounds(10, y - (text.Height / 2), text.Width, text.Height));
                }

                rowTop += rows;
            }
        }

        void DrawToday()
        {
            if (diagram.Today is not { } today || diagram.HideTodayMarker || tasks.Count is 0)
                return;

            var x = X(today) + leftPadding;
            var style = diagram.TodayMarkerStyle;
            group.Add(new SceneLine(x, TodayMargin, x, height - TodayMargin)
            {
                Stroke = style?.Stroke ?? colors.TodayLine,
                StrokeWidth = style?.StrokeWidth ?? 2,
                StrokeDashArray = style?.StrokeDashArray,
                Opacity = style?.Opacity ?? 1,
                CssClass = "today",
            });
            bounds.Add(new LayoutPoint(x, TodayMargin));
        }
    }

    private static DiagramText? GetGroupTitle(GanttDiagram diagram, int groupIndex)
    {
        var sectionIndex = diagram.Tasks.Count > 0 ? groupIndex - 1 : groupIndex;
        return sectionIndex >= 0 && sectionIndex < diagram.Sections.Count ? diagram.Sections[sectionIndex].Title : null;
    }

    /// <summary>Gives each task its row: in order, or, in compact mode, the first lane of its section where it does not overlap another task.</summary>
    private static Dictionary<int, int> AssignRows(GanttDiagram diagram, List<GanttScheduledTask> rowTasks, List<int> groups)
    {
        var result = new Dictionary<int, int>();
        var offset = 0;
        foreach (var groupIndex in groups)
        {
            var groupTasks = rowTasks.Where(task => task.GroupIndex == groupIndex).ToList();
            int rows;
            if (diagram.Compact)
            {
                var laneEnds = new List<DateTime>();
                foreach (var task in groupTasks.Select((task, index) => (Task: task, Index: index)).OrderBy(item => item.Task.Start).ThenBy(item => item.Index).Select(item => item.Task))
                {
                    var lane = laneEnds.FindIndex(end => task.Start >= end);
                    if (lane < 0)
                    {
                        lane = laneEnds.Count;
                        laneEnds.Add(task.End);
                    }
                    else
                    {
                        laneEnds[lane] = task.End;
                    }

                    task.Row = offset + lane;
                }

                rows = Math.Max(1, laneEnds.Count);
            }
            else
            {
                for (var i = 0; i < groupTasks.Count; i++)
                {
                    groupTasks[i].Row = offset + i;
                }

                rows = groupTasks.Count;
            }

            result[groupIndex] = rows;
            offset += rows;
        }

        return result;
    }

    private static string GetCssClass(GanttTask task)
    {
        var result = "task";
        if (task.IsActive)
        {
            result += " active";
        }

        if (task.IsDone)
        {
            result += " done";
        }

        if (task.IsCritical)
        {
            result += " crit";
        }

        if (task.IsMilestone)
        {
            result += " milestone";
        }

        if (task.IsVertical)
        {
            result += " vert";
        }

        return result;
    }

    /// <summary>Indicates whether a text is a date written <c>YYYY-MM-DD</c>, which Mermaid does not move over the excluded days.</summary>
    internal static bool IsIsoDay(string text) => text.Length is 10 && GanttDateFormat.TryParseIso(text, out _);
}
