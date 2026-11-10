namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Tells whether a day is excluded from the tasks of a Gantt chart, as the <c>isInvalidDate</c> of Mermaid.</summary>
internal sealed class GanttExclusions
{
    private readonly GanttDiagram _diagram;
    private readonly HashSet<string> _excludes;
    private readonly HashSet<string> _includes;

    public GanttExclusions(GanttDiagram diagram)
    {
        _diagram = diagram;
        _excludes = new HashSet<string>(diagram.Excludes.Select(ToLowerAscii), StringComparer.Ordinal);
        _includes = new HashSet<string>(diagram.Includes.Select(ToLowerAscii), StringComparer.Ordinal);
    }

    public bool HasExclusions => _excludes.Count > 0;

    public bool HasRules => _excludes.Count > 0 || _includes.Count > 0;

    public bool IsExcluded(DateTime date)
    {
        var formatted = _diagram.FormatDateRaw(date);
        var isoDay = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (_includes.Contains(formatted) || _includes.Contains(isoDay))
            return false;

        if (_excludes.Contains("weekends"))
        {
            var weekendStart = _diagram.WeekendStart;
            if (date.DayOfWeek == weekendStart || date.DayOfWeek == (DayOfWeek)(((int)weekendStart + 1) % 7))
                return true;
        }

        if (_excludes.Contains(ToLowerAscii(GanttDateFormat.DayNames[(int)date.DayOfWeek])))
            return true;

        return _excludes.Contains(formatted) || _excludes.Contains(isoDay);
    }

    internal static string ToLowerAscii(string value) => string.Create(value.Length, value, (span, text) =>
    {
        for (var i = 0; i < text.Length; i++)
        {
            span[i] = char.IsAsciiLetterUpper(text[i]) ? (char)(text[i] + 32) : text[i];
        }
    });
}
