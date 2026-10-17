namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of a <see cref="PerformanceMark"/> (https://w3c.github.io/user-timing/#dom-performancemarkoptions).</summary>
[IdlInterface("PerformanceMarkOptions", "https://w3c.github.io/user-timing/#dom-performancemarkoptions")]
public sealed class PerformanceMarkOptions
{
    /// <summary>Gets or sets the data of the mark, which is structured-cloned (a value of the script engine or a serializable value), or null.</summary>
    [IdlMember("detail", "https://w3c.github.io/user-timing/#dom-performancemarkoptions-detail")]
    public object? Detail { get; set; }

    /// <summary>Gets or sets the time of the mark, in milliseconds relative to the time origin, or null for the current time.</summary>
    [IdlMember("startTime", "https://w3c.github.io/user-timing/#dom-performancemarkoptions-starttime")]
    public double? StartTime { get; set; }
}

/// <summary>The options of <see cref="Performance.Measure(string, object?, string?)"/> (https://w3c.github.io/user-timing/#dom-performancemeasureoptions).</summary>
[IdlInterface("PerformanceMeasureOptions", "https://w3c.github.io/user-timing/#dom-performancemeasureoptions")]
public sealed class PerformanceMeasureOptions
{
    /// <summary>Gets or sets the data of the measure, which is structured-cloned (a value of the script engine or a serializable value), or null.</summary>
    [IdlMember("detail", "https://w3c.github.io/user-timing/#dom-performancemeasureoptions-detail")]
    public object? Detail { get; set; }

    /// <summary>Gets or sets the start of the measure: the name of a mark (a string) or a time in milliseconds (a double), or null.</summary>
    [IdlMember("start", "https://w3c.github.io/user-timing/#dom-performancemeasureoptions-start")]
    public object? Start { get; set; }

    /// <summary>Gets or sets the duration of the measure, in milliseconds, or null.</summary>
    [IdlMember("duration", "https://w3c.github.io/user-timing/#dom-performancemeasureoptions-duration")]
    public double? Duration { get; set; }

    /// <summary>Gets or sets the end of the measure: the name of a mark (a string) or a time in milliseconds (a double), or null.</summary>
    [IdlMember("end", "https://w3c.github.io/user-timing/#dom-performancemeasureoptions-end")]
    public object? End { get; set; }
}

/// <summary>
/// A named time of the performance timeline of a global object (https://w3c.github.io/user-timing/#performancemark), created by
/// <see cref="Performance.Mark"/> (which adds it to the timeline) or by its constructor (which does not).
/// </summary>
[IdlInterface("PerformanceMark", "https://w3c.github.io/user-timing/#dom-performancemark")]
public sealed class PerformanceMark : PerformanceEntry
{
    /// <summary>The entry type of the marks.</summary>
    internal const string MarkEntryType = "mark";

    /// <summary>Creates a mark of the current global object.</summary>
    /// <param name="markName">The name of the mark.</param>
    /// <param name="markOptions">The time and the data of the mark.</param>
    /// <exception cref="TypeErrorException">The start time is negative.</exception>
    /// <exception cref="DomException">A <c>SyntaxError</c> when the name is the name of an attribute of <see cref="PerformanceTiming"/>, in a window; a <c>DataCloneError</c> when the data cannot be structured-cloned.</exception>
    [IdlMember("constructor", "https://w3c.github.io/user-timing/#dom-performancemark-constructor")]
    public PerformanceMark(string markName, PerformanceMarkOptions? markOptions = null)
        : this(GlobalScope.Current ?? throw new InvalidOperationException("A performance mark can only be created by a window or a worker"), markName, markOptions)
    {
    }

    /// <summary>Creates a mark of a global object.</summary>
    /// <param name="global">The global object.</param>
    /// <param name="markName">The name of the mark.</param>
    /// <param name="markOptions">The time and the data of the mark.</param>
    [NotInIdl("The .NET constructor whose global object is explicit")]
    public PerformanceMark(GlobalScope global, string markName, PerformanceMarkOptions? markOptions = null)
        : base(Validate(global, markName), MarkEntryType, markName, GetStartTime(global, markOptions), duration: 0)
    {
        Detail = UserTiming.CloneDetail(global, markOptions?.Detail);
    }

    /// <summary>Gets the data of the mark: the structured clone of the detail of the options, or null.</summary>
    [IdlMember("detail", "https://w3c.github.io/user-timing/#dom-performancemark-detail")]
    public object? Detail { get; }

    private static GlobalScope Validate(GlobalScope global, string markName)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(markName);

        // A mark of a window cannot have the name of a read only attribute of the PerformanceTiming interface
        if (global is Window && Array.IndexOf(PerformanceTiming.AttributeNames, markName) >= 0)
            throw new DomException($"'{markName}' is the name of an attribute of PerformanceTiming, and cannot be the name of a mark", "SyntaxError");

        return global;
    }

    private static double GetStartTime(GlobalScope global, PerformanceMarkOptions? markOptions)
    {
        if (markOptions?.StartTime is not { } startTime)
            return global.GetCurrentHighResolutionTime();

        if (startTime < 0)
            throw new TypeErrorException("The start time of a mark cannot be negative");

        return startTime;
    }
}

/// <summary>
/// The time between two times of the performance timeline of a global object (https://w3c.github.io/user-timing/#performancemeasure),
/// created by <see cref="Performance.Measure(string, object?, string?)"/>.
/// </summary>
[IdlInterface("PerformanceMeasure", "https://w3c.github.io/user-timing/#dom-performancemeasure")]
public sealed class PerformanceMeasure : PerformanceEntry
{
    /// <summary>The entry type of the measures.</summary>
    internal const string MeasureEntryType = "measure";

    internal PerformanceMeasure(GlobalScope global, string name, double startTime, double duration, object? detail)
        : base(global, MeasureEntryType, name, startTime, duration)
    {
        Detail = detail;
    }

    /// <summary>Gets the data of the measure: the structured clone of the detail of the options, or null.</summary>
    [IdlMember("detail", "https://w3c.github.io/user-timing/#dom-performancemeasure-detail")]
    public object? Detail { get; }
}

/// <summary>The algorithms of User Timing (https://w3c.github.io/user-timing/).</summary>
internal static class UserTiming
{
    /// <summary>
    /// The detail of an entry: the structured clone of the value in the realm of the global object (the value is serialized
    /// when the entry is created, and the entry returns the same object each time), or null without value.
    /// </summary>
    public static object? CloneDetail(GlobalScope global, object? detail) => detail is null ? null : global.StructuredClone(detail);
}
