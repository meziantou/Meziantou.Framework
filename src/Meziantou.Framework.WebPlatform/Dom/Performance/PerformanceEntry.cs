namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A performance metric of the performance timeline of a global object (https://w3c.github.io/performance-timeline/#the-performanceentry-interface).</summary>
[IdlInterface("PerformanceEntry", "https://w3c.github.io/performance-timeline/#dom-performanceentry")]
public abstract class PerformanceEntry
{
    private readonly double _duration;

    private protected PerformanceEntry(GlobalScope global, string entryType, string name, double startTime, double duration)
    {
        Global = global;
        EntryType = entryType;
        Name = name;
        StartTime = startTime;
        _duration = duration;
    }

    /// <summary>The relevant global object of the entry.</summary>
    internal GlobalScope Global { get; }

    /// <summary>Gets the identifier of the entry (https://w3c.github.io/performance-timeline/#dom-performanceentry-id), unique in the performance timeline of its global object.</summary>
    [IdlMember("id", "https://w3c.github.io/performance-timeline/#dom-performanceentry-id")]
    public ulong Id { get; internal set; }

    /// <summary>Gets the name of the entry, e.g. the requested URL of a <see cref="PerformanceResourceTiming"/>.</summary>
    [IdlMember("name", "https://w3c.github.io/performance-timeline/#dom-performanceentry-name")]
    public string Name { get; }

    /// <summary>Gets the type of the entry, e.g. <c>resource</c>.</summary>
    [IdlMember("entryType", "https://w3c.github.io/performance-timeline/#dom-performanceentry-entrytype")]
    public string EntryType { get; }

    /// <summary>Gets the time of the start of the entry, in milliseconds relative to the time origin of its global object.</summary>
    [IdlMember("startTime", "https://w3c.github.io/performance-timeline/#dom-performanceentry-starttime")]
    public double StartTime { get; }

    /// <summary>Gets the duration of the entry, in milliseconds.</summary>
    [IdlMember("duration", "https://w3c.github.io/performance-timeline/#dom-performanceentry-duration")]
    public double Duration => GetDuration();

    /// <summary>The duration of the entry: the one of the navigation timing entry of a document changes until the load event completes.</summary>
    private protected virtual double GetDuration() => _duration;

    /// <summary>Gets the identifier of the navigation of the document of the global object when the entry was queued, or 0 for a worker.</summary>
    [IdlMember("navigationId", "https://w3c.github.io/performance-timeline/#dom-performanceentry-navigationid")]
    public ulong NavigationId { get; internal set; }
}
