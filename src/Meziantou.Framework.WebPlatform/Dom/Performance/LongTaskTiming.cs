namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A long task (https://w3c.github.io/longtasks/#sec-PerformanceLongTaskTiming): a task of the event loop, with its
/// microtask checkpoint, that ran scripts for 50 milliseconds or more. It is reported to the <c>longtask</c> performance
/// observers of the windows of the top-level browsing contexts of the scripts that ran in the task, and of their descendant
/// browsing contexts. Its name tells where the scripts ran, relative to the window that observes it (<c>self</c>,
/// <c>same-origin-ancestor</c>, <c>cross-origin-descendant</c>, <c>multiple-contexts</c>...).
/// </summary>
/// <remarks>The Long Tasks API is a discontinued draft that the browsers based on Chromium implement.</remarks>
/// <seealso href="https://w3c.github.io/longtasks/#performancelongtasktiming"/>
[IdlInterface("PerformanceLongTaskTiming", "https://w3c.github.io/longtasks/#performancelongtasktiming")]
public sealed class PerformanceLongTaskTiming : PerformanceEntry
{
    /// <summary>The entry type of the long task entries.</summary>
    internal const string LongTaskEntryType = "longtask";

    /// <summary>The maximum size of the performance entry buffer of the long tasks (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int MaxBufferSize = 200;

    /// <summary>The minimum duration of a long task, in milliseconds (https://w3c.github.io/longtasks/#report-long-tasks).</summary>
    internal const double Threshold = 50;

    private PerformanceLongTaskTiming(GlobalScope global, string name, double startTime, double duration, TaskAttributionTiming attribution)
        : base(global, LongTaskEntryType, name, startTime, duration)
    {
        Attribution = [attribution];
    }

    /// <summary>Gets where the scripts of the task ran, relative to the window of the entry (https://w3c.github.io/longtasks/#sec-PerformanceLongTaskTiming).</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-performancelongtasktiming-name"/>
    [IdlMember("name", "https://w3c.github.io/longtasks/#dom-performancelongtasktiming-name")]
    public new string Name => base.Name;

    /// <summary>Gets <c>longtask</c>.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-performancelongtasktiming-entrytype"/>
    [IdlMember("entryType", "https://w3c.github.io/longtasks/#dom-performancelongtasktiming-entrytype")]
    public new string EntryType => base.EntryType;

    /// <summary>Gets the time of the start of the task, in milliseconds relative to the time origin of the window of the entry.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-performancelongtasktiming-starttime"/>
    [IdlMember("startTime", "https://w3c.github.io/longtasks/#dom-performancelongtasktiming-starttime")]
    public new double StartTime => base.StartTime;

    /// <summary>Gets the duration of the task, in whole milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-performancelongtasktiming-duration"/>
    [IdlMember("duration", "https://w3c.github.io/longtasks/#dom-performancelongtasktiming-duration")]
    public new double Duration => base.Duration;

    /// <summary>Gets the attribution of the task: a single <see cref="TaskAttributionTiming"/>, which identifies the container of the culprit browsing context.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-performancelongtasktiming-attribution"/>
    [IdlMember("attribution", "https://w3c.github.io/longtasks/#dom-performancelongtasktiming-attribution")]
    public IReadOnlyList<TaskAttributionTiming> Attribution { get; }

    /// <summary>
    /// https://w3c.github.io/longtasks/#report-long-tasks: reports a task of the event loop to the windows of the top-level
    /// browsing contexts of the windows whose scripts ran in the task (the script evaluation environment settings object
    /// set of the task), and of their descendant browsing contexts.
    /// </summary>
    /// <param name="timeProvider">The clock of the event loop (<see cref="DomEventLoop.TimeProvider"/>).</param>
    /// <param name="startTimestamp">The timestamp of the start of the task.</param>
    /// <param name="endTimestamp">The timestamp of the end of the task.</param>
    /// <param name="scriptEvaluationWindows">The windows whose scripts ran in the task.</param>
    internal static void Report(TimeProvider timeProvider, long startTimestamp, long endTimestamp, IReadOnlyList<Window> scriptEvaluationWindows)
    {
        if ((endTimestamp - startTimestamp) * 1000d / timeProvider.TimestampFrequency < Threshold)
            return;

        var destinations = new List<Window>();
        foreach (var window in scriptEvaluationWindows)
        {
            if (window.BrowsingContext?.Top is not { } top)
                continue;

            foreach (var navigable in top.GetInclusiveDescendantNavigables())
            {
                if (navigable.Document is { IsFullyActive: true, RelevantWindow: { } destination } && destination.Options.EffectiveFeatures.LongTasksEnabled && !destinations.Contains(destination))
                {
                    destinations.Add(destination);
                }
            }
        }

        foreach (var destination in destinations)
        {
            var (name, culprit) = GetMinimalCulpritAttribution(destination, scriptEvaluationWindows);
            var attribution = TaskAttributionTiming.Create(destination, culprit);
            var startTime = destination.ToRelativeHighResolutionTime(startTimestamp);
            var duration = Math.Truncate(destination.ToRelativeHighResolutionTime(endTimestamp) - startTime);
            var entry = new PerformanceLongTaskTiming(destination, name, startTime, duration, attribution);
            destination.Performance.QueueLongTaskEntry(entry);

            // The attribution is not queued: it gets the identifiers of the entries of the timeline when it is created
            attribution.Id = destination.Performance.GenerateEntryId();
            attribution.NavigationId = entry.NavigationId;
        }
    }

    /// <summary>
    /// The name of the entry (the minimal culprit attribution) and the window whose container is reported by the attribution
    /// (step "process task's script evaluation environment settings object set" of https://w3c.github.io/longtasks/#report-long-tasks).
    /// As in the table of https://w3c.github.io/longtasks/#sec-PointingToCulprit and in the browsers, the container is only
    /// reported for a same-origin culprit that is not the window itself: the algorithm only hides the cross-origin ancestors,
    /// which contradicts the table and the same-origin policy that the section requires.
    /// </summary>
    private static (string Name, Window? Culprit) GetMinimalCulpritAttribution(Window destination, IReadOnlyList<Window> scriptEvaluationWindows)
    {
        if (scriptEvaluationWindows.Count == 0)
            return ("unknown", null);

        if (scriptEvaluationWindows.Count > 1)
            return ("multiple-contexts", null);

        var culprit = scriptEvaluationWindows[0];
        if (ReferenceEquals(culprit, destination))
            return ("self", null);

        var destinationContext = destination.BrowsingContext;
        var culpritContext = culprit.BrowsingContext;
        if (OriginRecord.IsSameOrigin(culprit.Document.Origin, destination.Document.Origin))
        {
            if (destinationContext is null || culpritContext is null)
                return ("same-origin", culprit);

            if (IsAncestor(culpritContext, destinationContext))
                return ("same-origin-ancestor", culprit);

            if (IsAncestor(destinationContext, culpritContext))
                return ("same-origin-descendant", culprit);

            return ("same-origin", culprit);
        }

        if (destinationContext is null || culpritContext is null)
            return ("cross-origin-unreachable", null);

        if (IsAncestor(culpritContext, destinationContext))
            return ("cross-origin-ancestor", null);

        if (IsAncestor(destinationContext, culpritContext))
            return ("cross-origin-descendant", null);

        return ("cross-origin-unreachable", null);
    }

    private static bool IsAncestor(BrowsingContext ancestor, BrowsingContext context)
    {
        for (var current = context.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }
}

/// <summary>
/// The attribution of a long task (https://w3c.github.io/longtasks/#sec-TaskAttributionTiming): the container (<c>iframe</c>,
/// <c>frame</c>, <c>object</c> or <c>embed</c> element) of the browsing context whose scripts ran in the task, or
/// <c>window</c> when it is not reported (the scripts ran in the window of the entry, in several windows, or in a
/// cross-origin ancestor).
/// </summary>
/// <seealso href="https://w3c.github.io/longtasks/#taskattributiontiming"/>
[IdlInterface("TaskAttributionTiming", "https://w3c.github.io/longtasks/#taskattributiontiming")]
public sealed class TaskAttributionTiming : PerformanceEntry
{
    /// <summary>The entry type of the attributions.</summary>
    internal const string TaskAttributionEntryType = "taskattribution";

    private TaskAttributionTiming(GlobalScope global, string containerType, string containerSrc, string containerId, string containerName)
        : base(global, TaskAttributionEntryType, "unknown", startTime: 0, duration: 0)
    {
        ContainerType = containerType;
        ContainerSrc = containerSrc;
        ContainerId = containerId;
        ContainerName = containerName;
    }

    /// <summary>Gets <c>unknown</c>.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-name"/>
    [IdlMember("name", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-name")]
    public new string Name => base.Name;

    /// <summary>Gets <c>taskattribution</c>.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-entrytype"/>
    [IdlMember("entryType", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-entrytype")]
    public new string EntryType => base.EntryType;

    /// <summary>Gets 0.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-starttime"/>
    [IdlMember("startTime", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-starttime")]
    public new double StartTime => base.StartTime;

    /// <summary>Gets 0.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-duration"/>
    [IdlMember("duration", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-duration")]
    public new double Duration => base.Duration;

    /// <summary>Gets the type of the container of the culprit browsing context: <c>iframe</c>, <c>frame</c>, <c>object</c>, <c>embed</c>, or <c>window</c> when it is not reported.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-containertype"/>
    [IdlMember("containerType", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-containertype")]
    public string ContainerType { get; }

    /// <summary>Gets the <c>src</c> attribute (<c>data</c> for an <c>object</c> element) of the container, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-containersrc"/>
    [IdlMember("containerSrc", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-containersrc")]
    public string ContainerSrc { get; }

    /// <summary>Gets the ID of the container, or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-containerid"/>
    [IdlMember("containerId", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-containerid")]
    public string ContainerId { get; }

    /// <summary>Gets the <c>name</c> attribute of the container (except an <c>embed</c> element), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/longtasks/#dom-taskattributiontiming-containername"/>
    [IdlMember("containerName", "https://w3c.github.io/longtasks/#dom-taskattributiontiming-containername")]
    public string ContainerName { get; }

    /// <summary>
    /// Creates the attribution of a long task for a window (step "let attribution be a new TaskAttributionTiming" of
    /// https://w3c.github.io/longtasks/#report-long-tasks): the container of the browsing context of the culprit, when it
    /// has one.
    /// </summary>
    internal static TaskAttributionTiming Create(Window destination, Window? culprit)
    {
        // No culprit is reported, or it is a top-level window
        if (culprit?.BrowsingContext?.Container is not { } container)
            return new TaskAttributionTiming(destination, "window", "", "", "");

        var id = container.Id;
        return container switch
        {
            HtmlIFrameElement => new TaskAttributionTiming(destination, "iframe", container.GetAttributeValue("src") ?? "", id, container.GetAttributeValue("name") ?? ""),
            HtmlFrameElement => new TaskAttributionTiming(destination, "frame", container.GetAttributeValue("src") ?? "", id, container.GetAttributeValue("name") ?? ""),
            HtmlObjectElement => new TaskAttributionTiming(destination, "object", container.GetAttributeValue("data") ?? "", id, container.GetAttributeValue("name") ?? ""),
            HtmlEmbedElement => new TaskAttributionTiming(destination, "embed", container.GetAttributeValue("src") ?? "", id, ""),
            _ => new TaskAttributionTiming(destination, "window", "", id, ""),
        };
    }
}
