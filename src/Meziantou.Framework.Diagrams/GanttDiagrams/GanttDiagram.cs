using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A Gantt chart: tasks on a time axis, grouped in sections, which start at a date or after other tasks and last a
/// duration or until another task (Mermaid <c>gantt</c>). The dates have no time zone; nothing depends on the current
/// date, unless <see cref="Today"/> is set.
/// </summary>
public sealed class GanttDiagram : Diagram
{
    private string _dateFormat = "YYYY-MM-DD";
    private GanttDateFormat _format = new("YYYY-MM-DD");
    private DayOfWeek _weekendStart = DayOfWeek.Saturday;
    private double _width = 1200;
    private double _barHeight = 20;
    private double _barGap = 4;
    private double _leftPadding = 75;

    /// <summary>
    /// Gets or sets the format of the dates in Mermaid, as dayjs: <c>YYYY-MM-DD</c> (the default), <c>HH:mm</c>,
    /// <c>X</c> (Unix seconds)... See <see cref="FormatDate(DateTime)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The format is empty, or contains a comma, a semicolon, <c>#</c> or a line break.</exception>
    public string DateFormat
    {
        get => _dateFormat;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (value.AsSpan().IndexOfAny(",;#\r\n") >= 0 || value.Trim().Length != value.Length)
                throw new ArgumentException("The date format cannot contain a comma, a semicolon, '#', a line break, or start or end with spaces.", nameof(value));

            _dateFormat = value;
            _format = new GanttDateFormat(value);
        }
    }

    /// <summary>
    /// Gets or sets the format of the dates of the axis, as the <c>strftime</c> of d3: <c>%Y-%m-%d</c>, <c>%H:%M</c>,
    /// <c>%b %d</c>..., or <see langword="null"/> for <c>%Y-%m-%d</c>.
    /// </summary>
    public string? AxisFormat { get; set; }

    /// <summary>Gets or sets the interval between the ticks of the axis, or <see langword="null"/> to choose about ten ticks.</summary>
    public GanttTickInterval? TickInterval { get; set; }

    /// <summary>Gets or sets the first day of a week, where the ticks of a weekly <see cref="TickInterval"/> are. The default is <see cref="DayOfWeek.Sunday"/>.</summary>
    public DayOfWeek WeekStart { get; set; }

    /// <summary>Gets or sets the first day of the weekend that <c>weekends</c> excludes: <see cref="DayOfWeek.Saturday"/> (the default) or <see cref="DayOfWeek.Friday"/>.</summary>
    public DayOfWeek WeekendStart
    {
        get => _weekendStart;
        set
        {
            if (value is not (DayOfWeek.Friday or DayOfWeek.Saturday))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The weekend starts on Friday or Saturday.");

            _weekendStart = value;
        }
    }

    /// <summary>
    /// Gets the days the tasks do not count, in lower case: <c>weekends</c>, the name of a day (<c>friday</c>), or a date
    /// in <see cref="DateFormat"/> or <c>YYYY-MM-DD</c>. A task that spans them is longer, unless it ends at a <c>YYYY-MM-DD</c> date.
    /// </summary>
    public IList<string> Excludes { get; } = new List<string>();

    /// <summary>Gets the dates that the tasks count even when <see cref="Excludes"/> excludes them.</summary>
    public IList<string> Includes { get; } = new List<string>();

    /// <summary>Gets or sets a value that indicates whether the end dates of the tasks are included: a task that ends on a date lasts until the end of that day.</summary>
    public bool InclusiveEndDates { get; set; }

    /// <summary>Gets or sets a value that indicates whether the axis is drawn above the tasks too.</summary>
    public bool TopAxis { get; set; }

    /// <summary>Gets or sets a value that indicates whether the tasks of a section that do not overlap share a row (Mermaid <c>displayMode: compact</c>).</summary>
    public bool Compact { get; set; }

    /// <summary>Gets or sets the date the marker of today is drawn at, or <see langword="null"/> for no marker. Mermaid draws it at the current date, which a Mermaid text does not set.</summary>
    public DateTime? Today { get; set; }

    /// <summary>Gets or sets a value that indicates whether the marker of today is hidden (Mermaid <c>todayMarker off</c>).</summary>
    public bool HideTodayMarker { get; set; }

    /// <summary>Gets or sets the style of the line of the marker of today, or <see langword="null"/> for the theme.</summary>
    public DiagramStyle? TodayMarkerStyle { get; set; }

    /// <summary>Gets or sets the width of the chart, in pixels (Mermaid <c>useWidth</c>). The default is 1200.</summary>
    public double Width
    {
        get => _width;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 100);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100_000);
            _width = value;
        }
    }

    /// <summary>Gets or sets the height of the bars of the tasks, in pixels. The default is 20.</summary>
    public double BarHeight
    {
        get => _barHeight;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _barHeight = value;
        }
    }

    /// <summary>Gets or sets the space between two rows of tasks, in pixels. The default is 4.</summary>
    public double BarGap
    {
        get => _barGap;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _barGap = value;
        }
    }

    /// <summary>Gets or sets the width of the column of the titles of the sections, on the left of the axis, in pixels. The default is 75.</summary>
    public double LeftPadding
    {
        get => _leftPadding;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 10_000);
            _leftPadding = value;
        }
    }

    /// <summary>Gets the tasks that are in no section, drawn first.</summary>
    public IList<GanttTask> Tasks { get; } = new List<GanttTask>();

    /// <summary>Gets the sections.</summary>
    public IList<GanttSection> Sections { get; } = new List<GanttSection>();

    /// <summary>Adds a section.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The section.</returns>
    public GanttSection AddSection(DiagramText title)
    {
        var section = new GanttSection(title);
        Sections.Add(section);
        return section;
    }

    /// <summary>Adds a task that is in no section.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The task.</returns>
    public GanttTask AddTask(DiagramText name)
    {
        var task = new GanttTask(name);
        Tasks.Add(task);
        return task;
    }

    /// <summary>Enumerates the tasks, in order: those in no section, then those of the sections.</summary>
    /// <returns>The tasks.</returns>
    public IEnumerable<GanttTask> EnumerateTasks() => Tasks.Concat(Sections.SelectMany(section => section.Tasks));

    /// <summary>Finds a task by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The last task with this identifier, as Mermaid, or <see langword="null"/> when there is none.</returns>
    public GanttTask? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return EnumerateTasks().LastOrDefault(task => string.Equals(task.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Formats a date with <see cref="DateFormat"/>; when the text does not parse back to the same date
    /// (a format without the time, with a day of the week only, a literal <c>after</c>...), the date is formatted as ISO, <c>2014-01-06T10:30:00.000</c>.
    /// </summary>
    /// <param name="date">The date.</param>
    /// <returns>The text.</returns>
    public string FormatDate(DateTime date)
    {
        var text = _format.Format(date);
        if (text.Length > 0 && text.Trim().Length == text.Length && text.AsSpan().IndexOfAny(",;#\r\n") < 0
            && !text.StartsWith("after", StringComparison.Ordinal) && !text.StartsWith("until", StringComparison.Ordinal) && !text.Contains("%%", StringComparison.Ordinal)
            && _format.TryParse(text, out var parsed) && parsed == date)
            return text;

        return GanttDateFormat.FormatIso(date);
    }

    /// <summary>Parses a date with <see cref="DateFormat"/>, or as an ISO date (<c>2014-01-06</c>, <c>2014-01-06T10:30</c>...).</summary>
    /// <param name="text">The text.</param>
    /// <param name="date">The date, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is a date.</returns>
    public bool TryParseDate(string text, out DateTime date)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _format.TryParse(text, out date) || GanttDateFormat.TryParseIso(text, out date);
    }

    internal bool TryParseDateStrict(string text, out DateTime date) => _format.TryParse(text, out date);

    internal string FormatDateRaw(DateTime date) => _format.Format(date);

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => GanttDiagramRenderer.Render(this, context, group);
}
