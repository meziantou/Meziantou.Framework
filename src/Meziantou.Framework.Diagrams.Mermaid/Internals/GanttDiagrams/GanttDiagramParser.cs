using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.GanttDiagrams;

/// <summary>
/// Parses the <c>gantt</c> diagrams of Mermaid. Each line is a statement; the keywords ignore the case, as in Mermaid.
/// A task is <c>name : [tags,] [id,] [start,] end</c>, where the start is a date or <c>after id...</c>, and the end
/// a date, a duration (<c>3d</c>) or <c>until id...</c>.
/// </summary>
internal sealed class GanttDiagramParser
{
    private static readonly string[] Tags = ["active", "done", "crit", "milestone", "vert"];

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly GanttDiagram _diagram = new();
    private readonly Dictionary<string, GanttTask> _ids = new(StringComparer.Ordinal);
    private GanttSection? _section;
    private GanttTask? _previous;
    private int _automaticIds;
    private int _taskCount;

    private GanttDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static GanttDiagram Parse(ParseContext context, int position) => new GanttDiagramParser(context, position).Parse();

    private GanttDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("gantt"))
            throw _scanner.Error(keywordStart, "'gantt' is expected.");

        ExpectLineEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "gantt");
        ApplyConfiguration();
        while (true)
        {
            SkipBlankLines();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        return _diagram;
    }

    private void ApplyConfiguration()
    {
        var configuration = _context.Configuration;
        if (string.Equals(configuration.GetString("gantt", "displayMode")?.Trim(), "compact", StringComparison.Ordinal))
        {
            _diagram.Compact = true;
        }

        if (configuration.GetBoolean("gantt", "topAxis") is true)
        {
            _diagram.TopAxis = true;
        }

        if (configuration.GetNumber("gantt", "useWidth") is >= 100 and <= 100_000 and var width)
        {
            _diagram.Width = width;
        }

        if (configuration.GetNumber("gantt", "barHeight") is >= 1 and <= 1000 and var barHeight)
        {
            _diagram.BarHeight = barHeight;
        }

        if (configuration.GetNumber("gantt", "barGap") is >= 0 and <= 1000 and var barGap)
        {
            _diagram.BarGap = barGap;
        }

        if (configuration.GetNumber("gantt", "leftPadding") is >= 0 and <= 10_000 and var leftPadding)
        {
            _diagram.LeftPadding = leftPadding;
        }

        if (configuration.GetString("gantt", "axisFormat") is { Length: > 0 } axisFormat && IsValidValue(axisFormat))
        {
            _diagram.AxisFormat = axisFormat.Trim();
        }

        if (configuration.GetString("gantt", "tickInterval") is { } tickInterval && TryParseTickInterval(tickInterval.Trim(), out var interval))
        {
            _diagram.TickInterval = interval;
        }

        if (configuration.GetString("gantt", "weekday") is { } weekday && TryParseDay(weekday.Trim(), out var day))
        {
            _diagram.WeekStart = day;
        }
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        if (TryKeyword("dateFormat"))
        {
            var value = ReadValue(stopAtHash: true);
            if (value.Contains(',', StringComparison.Ordinal))
                throw _scanner.Error(start, "The date format cannot contain a comma: it separates the parts of a task.");

            _diagram.DateFormat = value;
            return;
        }

        if (TryFlag("inclusiveEndDates"))
        {
            _diagram.InclusiveEndDates = true;
            return;
        }

        if (TryFlag("topAxis"))
        {
            _diagram.TopAxis = true;
            return;
        }

        if (TryKeyword("axisFormat"))
        {
            _diagram.AxisFormat = ReadValue(stopAtHash: true);
            return;
        }

        if (TryKeyword("tickInterval"))
        {
            var value = ReadValue(stopAtHash: true);
            if (TryParseTickInterval(value, out var interval))
            {
                _diagram.TickInterval = interval;
            }
            else
            {
                _context.Warn(start, $"The tick interval '{value}' is not supported: use a number and millisecond, second, minute, hour, day, week or month, such as 1week.");
            }

            return;
        }

        if (TryKeyword("excludes"))
        {
            ReadList(_diagram.Excludes);
            return;
        }

        if (TryKeyword("includes"))
        {
            ReadList(_diagram.Includes);
            return;
        }

        if (TryKeyword("todayMarker"))
        {
            var value = ReadValue(stopAtHash: false);
            if (string.Equals(value, "off", StringComparison.Ordinal))
            {
                _diagram.HideTodayMarker = true;
            }
            else
            {
                _diagram.TodayMarkerStyle = MermaidStyle.Parse(value, _context, start);
            }

            return;
        }

        if (TryDayStatement("weekday", out var weekday))
        {
            _diagram.WeekStart = weekday;
            return;
        }

        if (TryDayStatement("weekend", out var weekend))
        {
            if (weekend is not (DayOfWeek.Friday or DayOfWeek.Saturday))
                throw _scanner.Error(start, "The weekend starts on friday or saturday.");

            _diagram.WeekendStart = weekend;
            return;
        }

        if (TryKeyword("title"))
        {
            var title = _scanner.ReadRestOfLine().Trim();
            _diagram.Title = title.Length is 0 ? null : title;
            ExpectLineEnd();
            return;
        }

        if (TryKeyword("accDescription"))
        {
            _diagram.Description = ReadValue(stopAtHash: true);
            return;
        }

        if (TryKeyword("section"))
        {
            var raw = _scanner.ReadRestOfLine().Trim();
            if (raw.Length is 0)
                throw _scanner.Error(start, "The title of the section is expected.");

            ExpectLineEnd();
            _section = _diagram.AddSection(new DiagramText(MermaidText.DecodePlain(raw)));
            return;
        }

        if (TryKeyword("click"))
        {
            ParseClick(start);
            return;
        }

        ParseTask(start);
    }

    /// <summary>Parses a task: <c>name : [tags,] [id,] [start,] end</c>.</summary>
    private void ParseTask(int start)
    {
        var colon = _scanner.Text.IndexOfOrdinal(':', _scanner.Position);
        var lineEnd = _scanner.Text.IndexOfOrdinal('\n', _scanner.Position);
        if (lineEnd < 0)
        {
            lineEnd = _scanner.Text.Length;
        }

        if (colon < 0 || colon > lineEnd)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a task is a name, ':' and its data.");

        var rawName = _scanner.Text[_scanner.Position..colon].Trim();
        if (rawName.Length is 0)
            throw _scanner.Error(start, "The name of the task is expected.");

        _scanner.Position = colon + 1;
        var dataStart = _scanner.Position;
        var data = ReadValue(stopAtHash: true);
        if (++_taskCount > _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} tasks.");

        var task = new GanttTask(new DiagramText(MermaidText.DecodePlain(rawName)));
        var items = data.Split(',').ToList();
        while (items.Count > 0 && Array.IndexOf(Tags, items[0].Trim()) >= 0)
        {
            switch (items[0].Trim())
            {
                case "active":
                    task.IsActive = true;
                    break;
                case "done":
                    task.IsDone = true;
                    break;
                case "crit":
                    task.IsCritical = true;
                    break;
                case "milestone":
                    task.IsMilestone = true;
                    break;
                default:
                    task.IsVertical = true;
                    break;
            }

            items.RemoveAt(0);
        }

        items = items.ConvertAll(item => item.Trim());
        string? startText = null;
        string endText;
        switch (items.Count)
        {
            case 1:
                endText = items[0];
                break;
            case 2:
                startText = items[0];
                endText = items[1];
                break;
            case 3:
                if (items[0].Length is 0)
                    throw _scanner.Error(dataStart, "The identifier of the task is empty.");

                task.Id = items[0];
                startText = items[1];
                endText = items[2];
                break;
            default:
                throw _scanner.Error(dataStart, items.Count is 0 ? "The end of the task is expected." : "A task has an identifier, a start and an end at most.");
        }

        if (startText is null)
        {
            // The first task needs a start: the others start after the previous one
            if (_previous is null)
                throw _scanner.Error(dataStart, "The first task needs a start date.");
        }
        else if (TryParseReferences(startText, "after", task.After))
        {
        }
        else if (_diagram.TryParseDate(startText, out var startDate))
        {
            task.Start = startDate;
        }
        else
        {
            throw _scanner.Error(dataStart, $"'{startText}' is not a date in the format '{_diagram.DateFormat}' nor 'after' and task identifiers.");
        }

        if (TryParseReferences(endText, "until", task.Until))
        {
        }
        else if (_diagram.TryParseDate(endText, out var endDate))
        {
            task.End = endDate;
        }
        else if (TryParseDuration(endText, out var duration))
        {
            task.Duration = duration;
        }
        else
        {
            _context.Warn(dataStart, $"'{endText}' is neither a date in the format '{_diagram.DateFormat}' nor a duration such as 3d: the task lasts no time.");
            task.Duration = new GanttDuration(0, GanttTimeUnit.Day);
        }

        (_section?.Tasks ?? _diagram.Tasks).Add(task);
        _previous = task;
        var id = task.Id;
        if (id is null)
        {
            _automaticIds++;
            id = "task" + _automaticIds.ToString(CultureInfo.InvariantCulture);
        }

        _ids[id] = task;
    }

    /// <summary>Parses <c>after id1 id2</c> or <c>until id1 id2</c>: the keyword, white space, and identifiers of letters, digits, <c>_</c> and <c>-</c>.</summary>
    private static bool TryParseReferences(string text, string keyword, IList<string> ids)
    {
        if (!text.StartsWith(keyword, StringComparison.Ordinal) || text.Length <= keyword.Length || !char.IsWhiteSpace(text[keyword.Length]))
            return false;

        var position = keyword.Length;
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        var end = position;
        while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '_' or '-' or ' '))
        {
            end++;
        }

        if (end == position)
            return false;

        foreach (var id in text[position..end].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            ids.Add(id);
        }

        return true;
    }

    /// <summary>Parses a duration as Mermaid: a number and <c>ms</c>, <c>s</c>, <c>m</c>, <c>h</c>, <c>d</c>, <c>w</c>, <c>M</c> or <c>y</c>.</summary>
    internal static bool TryParseDuration(string text, out GanttDuration duration)
    {
        duration = default;
        var digits = 0;
        while (digits < text.Length && (char.IsAsciiDigit(text[digits]) || text[digits] == '.'))
        {
            digits++;
        }

        var number = text[..digits];
        if (number.Length is 0 || number[0] == '.' || number[^1] == '.' || number.Count(c => c == '.') > 1)
            return false;

        GanttTimeUnit? unit = text[digits..] switch
        {
            "ms" => GanttTimeUnit.Millisecond,
            "s" => GanttTimeUnit.Second,
            "m" => GanttTimeUnit.Minute,
            "h" => GanttTimeUnit.Hour,
            "d" => GanttTimeUnit.Day,
            "w" => GanttTimeUnit.Week,
            "M" => GanttTimeUnit.Month,
            "y" => GanttTimeUnit.Year,
            _ => null,
        };
        if (unit is null || !double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            return false;

        duration = new GanttDuration(value, unit.Value);
        return true;
    }

    internal static bool TryParseTickInterval(string text, out GanttTickInterval interval)
    {
        interval = default;
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        if (digits is 0 || text[0] == '0' || digits > 9)
            return false;

        GanttTimeUnit? unit = text[digits..] switch
        {
            "millisecond" => GanttTimeUnit.Millisecond,
            "second" => GanttTimeUnit.Second,
            "minute" => GanttTimeUnit.Minute,
            "hour" => GanttTimeUnit.Hour,
            "day" => GanttTimeUnit.Day,
            "week" => GanttTimeUnit.Week,
            "month" => GanttTimeUnit.Month,
            _ => null,
        };
        if (unit is null)
            return false;

        interval = new GanttTickInterval(int.Parse(text.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture), unit.Value);
        return true;
    }

    /// <summary>Parses <c>click ids href "url"</c> and <c>click ids call function(arguments)</c>: the links apply to the tasks already declared, the callbacks are ignored.</summary>
    private void ParseClick(int start)
    {
        var idsStart = _scanner.Position;
        while (!_scanner.IsEnd && !char.IsWhiteSpace(_scanner.Current))
        {
            _scanner.Position++;
        }

        var ids = _scanner.Text[idsStart.._scanner.Position];
        string? link = null;
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.IsAtStatementEnd() || _scanner.Current == ';')
                break;

            if (TryKeyword("href"))
            {
                if (_scanner.Current != '"')
                    throw _scanner.Error("The address is quoted: href \"https://...\".");

                link = _scanner.ReadQuoted();
                continue;
            }

            if (TryKeyword("call"))
            {
                var callStart = _scanner.Position;
                while (!_scanner.IsEnd && _scanner.Current is not ('(' or '\n'))
                {
                    _scanner.Position++;
                }

                if (_scanner.Current == '(')
                {
                    var close = _scanner.Text.IndexOfOrdinal(')', _scanner.Position);
                    var lineEnd = _scanner.Text.IndexOfOrdinal('\n', _scanner.Position);
                    if (close < 0 || (lineEnd >= 0 && lineEnd < close))
                        throw _scanner.Error(callStart, "The arguments of the callback are not closed by ')'.");

                    _scanner.Position = close + 1;
                }

                _context.Warn(callStart, "The callbacks are not supported: they are ignored.");
                continue;
            }

            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': 'href' or 'call' is expected.");
        }

        ExpectLineEnd();
        if (link is null)
            return;

        var url = MermaidCommon.SanitizeUrl(MermaidText.DecodeEntities(link), _context, start);
        foreach (var id in ids.Split(','))
        {
            if (_ids.TryGetValue(id, out var task))
            {
                task.Link = url;
            }
            else
            {
                _context.Warn(start, $"The task '{id}' is not declared before the click statement: its link is ignored.");
            }
        }
    }

    /// <summary>Reads the value of a statement: the rest of the line, up to a semicolon, a <c>#</c> or a <c>%%</c> comment; nothing must follow.</summary>
    private string ReadValue(bool stopAtHash)
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';') && !(stopAtHash && _scanner.Current == '#') && !_scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.Position++;
        }

        var value = _scanner.Text[start.._scanner.Position].Trim();
        if (value.Length is 0)
            throw _scanner.Error(start, "A value is expected.");

        ExpectLineEnd();
        return value;
    }

    /// <summary>Reads the values of <c>excludes</c> and <c>includes</c>: lowercase, separated by white space or commas.</summary>
    private void ReadList(IList<string> target)
    {
        foreach (var token in ReadValue(stopAtHash: true).Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            target.Add(ToLowerAscii(token));
        }
    }

    private static bool IsValidValue(string value) => value.Trim().Length > 0 && value.AsSpan().IndexOfAny("#;\r\n") < 0 && !value.Contains("%%", StringComparison.Ordinal);

    /// <summary>Consumes a keyword, ignoring the case, followed by white space.</summary>
    private bool TryKeyword(string keyword)
    {
        var text = _scanner.Text;
        var position = _scanner.Position;
        if (position + keyword.Length >= text.Length || string.Compare(text, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0 || text[position + keyword.Length] is not (' ' or '\t'))
            return false;

        _scanner.Position += keyword.Length;
        _scanner.SkipSpaces();
        return true;
    }

    /// <summary>Consumes a keyword alone on its line, ignoring the case.</summary>
    private bool TryFlag(string keyword)
    {
        var text = _scanner.Text;
        var position = _scanner.Position;
        if (position + keyword.Length > text.Length || string.Compare(text, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var end = position + keyword.Length;
        if (end < text.Length && !char.IsWhiteSpace(text[end]) && !_scanner.IsCommentAt(end))
            return false;

        _scanner.Position = end;
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
        {
            _scanner.Position = position;
            return false;
        }

        return true;
    }

    private bool TryDayStatement(string keyword, out DayOfWeek day)
    {
        day = default;
        var save = _scanner.Position;
        if (!TryKeyword(keyword))
            return false;

        var start = _scanner.Position;
        while (!_scanner.IsEnd && char.IsAsciiLetter(_scanner.Current))
        {
            _scanner.Position++;
        }

        if (!TryParseDay(_scanner.Text[start.._scanner.Position], out day))
        {
            // As Mermaid, a line that does not name a day is a task
            _scanner.Position = save;
            return false;
        }

        ExpectLineEnd();
        return true;
    }

    internal static bool TryParseDay(string text, out DayOfWeek day)
    {
        var index = Array.FindIndex(GanttDayNames, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
        day = (DayOfWeek)Math.Max(index, 0);
        return index >= 0;
    }

    internal static readonly string[] GanttDayNames = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    internal static string ToLowerAscii(string value) => string.Create(value.Length, value, (span, text) =>
    {
        for (var i = 0; i < text.Length; i++)
        {
            span[i] = char.IsAsciiLetterUpper(text[i]) ? (char)(text[i] + 32) : text[i];
        }
    });

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a statement is alone on its line.");
    }

    private void SkipBlankLines()
    {
        while (!_scanner.IsEnd)
        {
            if (char.IsWhiteSpace(_scanner.Current))
            {
                _scanner.Position++;
            }
            else if (_scanner.IsCommentAt(_scanner.Position))
            {
                _scanner.SkipSpaces();
            }
            else
            {
                break;
            }
        }
    }
}
