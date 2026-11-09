using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.GanttDiagrams;

/// <summary>Writes a <see cref="GanttDiagram"/> as Mermaid: the settings, the tasks outside the sections, the sections, then the links.</summary>
internal static class GanttDiagramWriter
{
    private static readonly string[] Tags = ["active", "done", "crit", "milestone", "vert"];

    private static readonly string[] Keywords =
    [
        "gantt", "dateformat", "inclusiveenddates", "topaxis", "axisformat", "tickinterval", "includes", "excludes", "todaymarker",
        "weekday", "weekend", "title", "accdescription", "accdescr", "acctitle", "section", "click",
    ];

    public static void Write(StringBuilder builder, GanttDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.Compact)
        {
            configuration.Add("displayMode: compact");
        }

        if (diagram.Width != 1200)
        {
            configuration.Add("useWidth: " + MermaidStyle.Number(diagram.Width));
        }

        if (diagram.BarHeight != 20)
        {
            configuration.Add("barHeight: " + MermaidStyle.Number(diagram.BarHeight));
        }

        if (diagram.BarGap != 4)
        {
            configuration.Add("barGap: " + MermaidStyle.Number(diagram.BarGap));
        }

        if (diagram.LeftPadding != 75)
        {
            configuration.Add("leftPadding: " + MermaidStyle.Number(diagram.LeftPadding));
        }

        // The title is a statement of the diagram, which holds one line
        var titleStatement = diagram.Title is { Length: > 0 } title && title.AsSpan().IndexOfAny("\r\n\u2028\u2029\u0085") < 0 && title.Trim().Length == title.Length && !title.Contains("%%", StringComparison.Ordinal);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "gantt", configuration, includeTitle: !titleStatement);
        builder.Append("gantt\n");
        if (titleStatement)
        {
            builder.Append("    title ").Append(diagram.Title).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        if (!string.Equals(diagram.DateFormat, "YYYY-MM-DD", StringComparison.Ordinal))
        {
            builder.Append("    dateFormat ").Append(CheckValue(diagram.DateFormat, nameof(diagram.DateFormat))).Append('\n');
        }

        if (diagram.AxisFormat is { } axisFormat)
        {
            builder.Append("    axisFormat ").Append(CheckValue(axisFormat, nameof(diagram.AxisFormat))).Append('\n');
        }

        if (diagram.TickInterval is { } interval)
        {
            builder.Append("    tickInterval ").Append(interval.Count.ToString(CultureInfo.InvariantCulture)).Append(FormatTickUnit(interval.Unit)).Append('\n');
        }

        if (diagram.WeekStart is not DayOfWeek.Sunday)
        {
            builder.Append("    weekday ").Append(GanttDiagramParser.GanttDayNames[(int)diagram.WeekStart]).Append('\n');
        }

        if (diagram.WeekendStart is not DayOfWeek.Saturday)
        {
            builder.Append("    weekend ").Append(GanttDiagramParser.GanttDayNames[(int)diagram.WeekendStart]).Append('\n');
        }

        WriteList(builder, "excludes", diagram.Excludes);
        WriteList(builder, "includes", diagram.Includes);
        if (diagram.InclusiveEndDates)
        {
            builder.Append("    inclusiveEndDates\n");
        }

        if (diagram.TopAxis)
        {
            builder.Append("    topAxis\n");
        }

        if (diagram.HideTodayMarker)
        {
            builder.Append("    todayMarker off\n");
        }
        else if (!MermaidStyle.IsEmpty(diagram.TodayMarkerStyle))
        {
            builder.Append("    todayMarker ").Append(MermaidStyle.Format(diagram.TodayMarkerStyle!)).Append('\n');
        }

        GanttTask? previous = null;
        var automaticIds = 0;
        foreach (var task in diagram.Tasks)
        {
            WriteTask(builder, diagram, task, previous, ref automaticIds);
            previous = task;
        }

        foreach (var section in diagram.Sections)
        {
            builder.Append("    section ").Append(EncodeName(section.Title, "section")).Append('\n');
            foreach (var task in section.Tasks)
            {
                WriteTask(builder, diagram, task, previous, ref automaticIds);
                previous = task;
            }
        }
    }

    private static void WriteTask(StringBuilder builder, GanttDiagram diagram, GanttTask task, GanttTask? previous, ref int automaticIds)
    {
        builder.Append("    ").Append(EncodeName(task.Name, "task")).Append(" :");
        var items = new List<string>();
        if (task.IsActive)
        {
            items.Add("active");
        }

        if (task.IsDone)
        {
            items.Add("done");
        }

        if (task.IsCritical)
        {
            items.Add("crit");
        }

        if (task.IsMilestone)
        {
            items.Add("milestone");
        }

        if (task.IsVertical)
        {
            items.Add("vert");
        }

        string? start = null;
        if (task.After.Count > 0)
        {
            start = "after " + string.Join(' ', task.After.Select(CheckReference));
        }
        else if (task.Start is { } startDate)
        {
            start = diagram.FormatDate(startDate);
        }
        else if (task.Id is not null)
        {
            // Mermaid reads an identifier only with a start: the task starts after the previous one
            if (previous?.Id is not { } previousId)
                throw new ArgumentException($"The task '{task.Name.Text}' cannot be written as Mermaid: a task with an identifier needs a start, or a previous task with an identifier.", nameof(task));

            start = "after " + CheckReference(previousId);
        }
        else if (previous is null)
        {
            throw new ArgumentException($"The task '{task.Name.Text}' cannot be written as Mermaid: the first task needs a start.", nameof(task));
        }

        if (task.Id is { } id)
        {
            if (id.Length is 0 || id.Trim().Length != id.Length || id.AsSpan().IndexOfAny(",#;\r\n") >= 0 || id.Contains("%%", StringComparison.Ordinal) || Array.IndexOf(Tags, id) >= 0)
                throw new ArgumentException($"The identifier '{id}' cannot be written as Mermaid: it is empty, starts or ends with white space, contains a comma, '#', a semicolon or a line break, or is a tag such as 'done'.", nameof(task));

            items.Add(id);
        }

        if (start is not null)
        {
            items.Add(start);
        }

        string end;
        if (task.Until.Count > 0)
        {
            end = "until " + string.Join(' ', task.Until.Select(CheckReference));
        }
        else if (task.End is { } endDate)
        {
            end = diagram.FormatDate(endDate);
        }
        else if (task.Duration is { } duration)
        {
            end = FormatDuration(duration);
        }
        else
        {
            end = "0d";
        }

        items.Add(end);
        builder.Append(' ').Append(string.Join(", ", items)).Append('\n');

        // The tasks without identifier are task1, task2... The click statement names the last task declared with an identifier
        var clickId = task.Id;
        if (clickId is null)
        {
            automaticIds++;
            clickId = "task" + automaticIds.ToString(CultureInfo.InvariantCulture);
        }

        if (task.Link is { } link)
        {
            if (clickId.Contains(',', StringComparison.Ordinal) || clickId.Any(char.IsWhiteSpace))
                throw new ArgumentException($"The link of the task '{task.Name.Text}' cannot be written as Mermaid: its identifier '{clickId}' contains white space or a comma.", nameof(task));

            builder.Append("    click ").Append(clickId).Append(" href ").Append(MermaidText.QuoteAttribute(link.Trim())).Append('\n');
        }
    }

    internal static string FormatDuration(GanttDuration duration)
    {
        var number = duration.Value is 0 ? "0" : duration.Value.ToString("R", CultureInfo.InvariantCulture);
        if (number.Contains('E', StringComparison.Ordinal))
        {
            number = duration.Value.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        return number + duration.Unit switch
        {
            GanttTimeUnit.Millisecond => "ms",
            GanttTimeUnit.Second => "s",
            GanttTimeUnit.Minute => "m",
            GanttTimeUnit.Hour => "h",
            GanttTimeUnit.Day => "d",
            GanttTimeUnit.Week => "w",
            GanttTimeUnit.Month => "M",
            _ => "y",
        };
    }

    private static string FormatTickUnit(GanttTimeUnit unit) => unit switch
    {
        GanttTimeUnit.Millisecond => "millisecond",
        GanttTimeUnit.Second => "second",
        GanttTimeUnit.Minute => "minute",
        GanttTimeUnit.Hour => "hour",
        GanttTimeUnit.Day => "day",
        GanttTimeUnit.Week => "week",
        _ => "month",
    };

    private static void WriteList(StringBuilder builder, string keyword, IList<string> values)
    {
        if (values.Count is 0)
            return;

        foreach (var value in values)
        {
            if (value.Length is 0 || value.AsSpan().IndexOfAny(" \t\r\n,;#") >= 0 || value.Contains("%%", StringComparison.Ordinal))
                throw new ArgumentException($"The value '{value}' of '{keyword}' cannot be written as Mermaid: it is empty or contains a space, a comma, a semicolon or '#'.", nameof(values));
        }

        // The parser trims the list
        var list = string.Join(' ', values.Select(GanttDiagramParser.ToLowerAscii));
        if (list.Trim().Length != list.Length)
            throw new ArgumentException($"The values of '{keyword}' cannot be written as Mermaid: the list starts or ends with white space.", nameof(values));

        builder.Append("    ").Append(keyword).Append(' ').Append(list).Append('\n');
    }

    /// <summary>Checks that a reference of <c>after</c> or <c>until</c> is made of letters, digits, <c>_</c> and <c>-</c>: the characters that Mermaid reads.</summary>
    private static string CheckReference(string id)
    {
        if (id.Length is 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new ArgumentException($"The task identifier '{id}' cannot be referenced in Mermaid: use letters, digits, '_' and '-'.", nameof(id));

        return id;
    }

    private static string CheckValue(string value, string name)
    {
        if (value.Trim().Length is 0 || value.Trim().Length != value.Length || value.AsSpan().IndexOfAny("#;\r\n") >= 0 || value.Contains("%%", StringComparison.Ordinal))
            throw new ArgumentException($"The {name} '{value}' cannot be written as Mermaid: it is empty, starts or ends with white space, or contains '#', a semicolon or a line break.", name);

        return value;
    }

    /// <summary>
    /// Encodes the name of a task or the title of a section: the colons, the white space at the ends, and the first
    /// letter of a name that starts with a keyword become entity codes.
    /// </summary>
    private static string EncodeName(DiagramText text, string kind)
    {
        var value = text.Text;
        if (value.Length is 0)
            throw new ArgumentException($"The {kind} cannot be written as Mermaid: its text is empty.", nameof(text));

        var start = 0;
        var end = value.Length;
        var prefix = string.Empty;
        var suffix = string.Empty;
        if (char.IsWhiteSpace(value[0]) || (char.IsAsciiLetter(value[0]) && Keywords.Any(keyword => value.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))))
        {
            prefix = Entity(value[0]);
            start = 1;
        }

        if (end > start && char.IsWhiteSpace(value[^1]))
        {
            suffix = Entity(value[^1]);
            end--;
        }

        return prefix + MermaidText.EncodePlain(value[start..end]).Replace(":", "#58;", StringComparison.Ordinal) + suffix;
    }

    private static string Entity(char c) => "#" + ((int)c).ToString(CultureInfo.InvariantCulture) + ";";
}
