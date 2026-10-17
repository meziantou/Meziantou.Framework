namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://w3c.github.io/hr-time/#the-performance-interface, with the extensions of Performance Timeline
/// (https://w3c.github.io/performance-timeline/#extensions-to-the-performance-interface), Resource Timing
/// (https://w3c.github.io/resource-timing/#sec-extensions-performance-interface), User Timing
/// (https://w3c.github.io/user-timing/#extensions-performance-interface) and Navigation Timing
/// (https://w3c.github.io/navigation-timing/#extensions-to-the-performance-interface). It holds the performance timeline of its
/// global object: the performance entry buffer and the registered performance observers.
/// </summary>
[IdlInterface("Performance", "https://w3c.github.io/hr-time/#sec-performance")]
public sealed class Performance : EventTarget
{
    /// <summary>The initial size limit of the resource timing buffer (https://w3c.github.io/resource-timing/#dfn-resource-timing-buffer-size-limit).</summary>
    private const int DefaultResourceTimingBufferSize = 250;

    private readonly GlobalScope _global;
    private readonly List<RegisteredPerformanceObserver> _registeredObservers = [];
    private readonly List<PerformanceEntry> _resourceTimingBuffer = [];
    private readonly List<PerformanceEntry> _resourceTimingSecondaryBuffer = [];
    private readonly List<PerformanceEntry> _markBuffer = [];
    private readonly List<PerformanceEntry> _measureBuffer = [];
    private PerformanceTiming? _timing;
    private PerformanceNavigation? _navigation;
    private List<PerformanceEntry>? _layoutShiftBuffer;
    private List<PerformanceEntry>? _paintBuffer;
    private List<PerformanceEntry>? _visibilityStateBuffer;
    private List<PerformanceEntry>? _largestContentfulPaintBuffer;
    private List<PerformanceEntry>? _elementTimingBuffer;
    private List<PerformanceEntry>? _eventTimingBuffer;
    private List<PerformanceEntry>? _firstInputBuffer;
    private EventCounts? _eventCounts;
    private ulong _paintDroppedEntriesCount;
    private ulong _largestContentfulPaintDroppedEntriesCount;
    private ulong _elementTimingDroppedEntriesCount;
    private ulong _eventTimingDroppedEntriesCount;
    private ulong _firstInputDroppedEntriesCount;
    private IReadOnlyList<string>? _supportedEntryTypes;
    private ulong _lastEntryId = (ulong)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 10_000);
    private int _resourceTimingBufferSizeLimit = DefaultResourceTimingBufferSize;
    private bool _resourceTimingBufferFullEventPending;
    private bool _observerTaskQueued;

    internal Performance(GlobalScope global)
    {
        _global = global;
    }

    internal override GlobalScope? RelevantGlobal => _global;

    /// <summary>
    /// The frozen array of supported entry types of the global object (https://w3c.github.io/performance-timeline/#dfn-frozen-array-of-supported-entry-types),
    /// in alphabetical order: for a window, <c>element</c> (see <see cref="WebPlatformFeatures.ElementTimingEnabled"/>),
    /// <c>event</c> and <c>first-input</c> (see <see cref="WebPlatformFeatures.EventTimingEnabled"/>),
    /// <c>largest-contentful-paint</c> (see <see cref="WebPlatformFeatures.LargestContentfulPaintEnabled"/>), <c>layout-shift</c>
    /// (see <see cref="WebPlatformFeatures.LayoutInstabilityEnabled"/>), <c>navigation</c> (see <see cref="WebPlatformFeatures.NavigationTimingEnabled"/>)
    /// <c>paint</c> (see <see cref="WebPlatformFeatures.PaintTimingEnabled"/>) and <c>visibility-state</c> (see
    /// <see cref="WebPlatformFeatures.VisibilityStateEntryEnabled"/>); <c>mark</c> and <c>measure</c> (see
    /// <see cref="WebPlatformFeatures.UserTimingEnabled"/>); and <c>resource</c>.
    /// </summary>
    internal IReadOnlyList<string> SupportedEntryTypes => _supportedEntryTypes ??= CreateSupportedEntryTypes();

    private string[] CreateSupportedEntryTypes()
    {
        var types = new List<string>();
        var features = _global.Options.EffectiveFeatures;
        var isWindow = _global is Window;
        if (isWindow && features.ElementTimingEnabled)
        {
            types.Add(PerformanceElementTiming.ElementEntryType);
        }

        if (isWindow && features.EventTimingEnabled)
        {
            types.Add(PerformanceEventTiming.EventEntryType);
            types.Add(PerformanceEventTiming.FirstInputEntryType);
        }

        if (isWindow && features.LargestContentfulPaintEnabled)
        {
            types.Add(LargestContentfulPaint.LargestContentfulPaintEntryType);
        }

        if (isWindow && features.LayoutInstabilityEnabled)
        {
            types.Add(LayoutShift.LayoutShiftEntryType);
        }

        if (features.UserTimingEnabled)
        {
            types.Add(PerformanceMark.MarkEntryType);
            types.Add(PerformanceMeasure.MeasureEntryType);
        }

        if (isWindow && features.NavigationTimingEnabled)
        {
            types.Add(PerformanceNavigationTiming.NavigationEntryType);
        }

        if (isWindow && features.PaintTimingEnabled)
        {
            types.Add(PerformancePaintTiming.PaintEntryType);
        }

        types.Add(PerformanceResourceTiming.ResourceEntryType);
        if (isWindow && features.VisibilityStateEntryEnabled)
        {
            types.Add(VisibilityStateEntry.VisibilityStateEntryType);
        }

        return [.. types];
    }

    /// <summary>
    /// The navigation timing entry of the document of the window (https://w3c.github.io/navigation-timing/#dfn-create-the-navigation-timing-entry),
    /// or null for a worker: the only entry of the <c>navigation</c> type of the performance entry buffer.
    /// </summary>
    internal PerformanceNavigationTiming? NavigationTimingEntry { get; private set; }

    /// <summary>The dropped entries count of the <c>layout-shift</c> entry type: the entries that were not added to the full buffer.</summary>
    private ulong _layoutShiftDroppedEntriesCount;

    /// <summary>The list of registered performance observer objects of the global object (https://w3c.github.io/performance-timeline/#dfn-list-of-registered-performance-observer-objects).</summary>
    internal List<RegisteredPerformanceObserver> RegisteredObservers => _registeredObservers;

    /// <summary>
    /// The dropped entries count of the "resource" entry type (https://w3c.github.io/performance-timeline/#dfn-dropped-entries-count):
    /// the resource timing entries queued while the resource timing buffer was full.
    /// </summary>
    internal ulong ResourceDroppedEntriesCount { get; private set; }

    /// <summary>
    /// https://w3c.github.io/hr-time/#now-method: the time elapsed since the time origin of the global object, in milliseconds,
    /// measured with <see cref="DomEventLoop.TimeProvider"/>.
    /// </summary>
    [IdlMember("now", "https://w3c.github.io/hr-time/#dom-performance-now")]
    public double Now() => _global.GetCurrentHighResolutionTime();

    /// <summary>https://w3c.github.io/hr-time/#timeorigin-attribute: the time origin of the global object, in milliseconds since the Unix epoch.</summary>
    [IdlMember("timeOrigin", "https://w3c.github.io/hr-time/#dom-performance-timeorigin")]
    public double TimeOrigin => _global.TimeOrigin;

    /// <summary>https://w3c.github.io/performance-timeline/#getentries-method: the entries of the performance entry buffer, in chronological order.</summary>
    [IdlMember("getEntries", "https://w3c.github.io/performance-timeline/#dom-performance-getentries")]
    public IReadOnlyList<PerformanceEntry> GetEntries()
    {
        OnReadingTimeline(type: null);
        return FilterBufferMap(name: null, type: null);
    }

    /// <summary>https://w3c.github.io/performance-timeline/#getentriesbytype-method</summary>
    [IdlMember("getEntriesByType", "https://w3c.github.io/performance-timeline/#dom-performance-getentriesbytype")]
    public IReadOnlyList<PerformanceEntry> GetEntriesByType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        OnReadingTimeline(type);
        return FilterBufferMap(name: null, type);
    }

    /// <summary>https://w3c.github.io/performance-timeline/#getentriesbyname-method</summary>
    [IdlMember("getEntriesByName", "https://w3c.github.io/performance-timeline/#dom-performance-getentriesbyname")]
    public IReadOnlyList<PerformanceEntry> GetEntriesByName(string name, string? type = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        OnReadingTimeline(type);
        return FilterBufferMap(name, type);
    }

    /// <summary>
    /// https://w3c.github.io/user-timing/#mark-method: creates a mark (see <see cref="PerformanceMark"/>), queues it to the
    /// performance observers and adds it to the performance timeline.
    /// </summary>
    /// <param name="markName">The name of the mark.</param>
    /// <param name="markOptions">The time and the data of the mark.</param>
    /// <returns>The mark.</returns>
    [IdlMember("mark", "https://w3c.github.io/user-timing/#dom-performance-mark")]
    public PerformanceMark Mark(string markName, PerformanceMarkOptions? markOptions = null)
    {
        var entry = new PerformanceMark(_global, markName, markOptions);
        QueueEntry(entry);
        _markBuffer.Add(entry);
        return entry;
    }

    /// <summary>https://w3c.github.io/user-timing/#clearmarks-method: removes the marks of a name, or all the marks, from the performance timeline.</summary>
    /// <param name="markName">The name of the marks to remove, or null for all the marks.</param>
    [IdlMember("clearMarks", "https://w3c.github.io/user-timing/#dom-performance-clearmarks")]
    public void ClearMarks(string? markName = null) => ClearBuffer(_markBuffer, markName);

    /// <summary>
    /// https://w3c.github.io/user-timing/#measure-method: creates a measure between two times, queues it to the performance
    /// observers and adds it to the performance timeline. A time is the name of a mark (the time of its most recent
    /// occurrence), the name of an attribute of <see cref="PerformanceTiming"/> in a window, or a number of milliseconds.
    /// </summary>
    /// <param name="measureName">The name of the measure.</param>
    /// <param name="startOrMeasureOptions">The start of the measure (a string), a <see cref="PerformanceMeasureOptions"/>, or null: the measure starts at the time origin.</param>
    /// <param name="endMark">The end of the measure, or null for the current time. It cannot be used with options.</param>
    /// <returns>The measure.</returns>
    /// <exception cref="TypeErrorException">The options are not valid, or a time is negative.</exception>
    /// <exception cref="DomException">A <c>SyntaxError</c> when a mark does not exist; an <c>InvalidAccessError</c> when an attribute of <see cref="PerformanceTiming"/> is not set yet.</exception>
    [IdlMember("measure", "https://w3c.github.io/user-timing/#dom-performance-measure")]
    public PerformanceMeasure Measure(string measureName, object? startOrMeasureOptions = null, string? endMark = null)
    {
        ArgumentNullException.ThrowIfNull(measureName);
        var options = startOrMeasureOptions as PerformanceMeasureOptions;
        if (options is { Start: null, End: null, Duration: null, Detail: null })
        {
            // A dictionary without member is not a measure options object
            options = null;
        }

        if (options is not null)
        {
            if (endMark is not null)
                throw new TypeErrorException("The end mark cannot be used with the measure options");

            if (options.Start is null && options.End is null)
                throw new TypeErrorException("The measure options must have a start or an end");

            if (options.Start is not null && options.Duration is not null && options.End is not null)
                throw new TypeErrorException("The measure options cannot have a start, a duration and an end");
        }

        double endTime;
        if (endMark is not null)
        {
            endTime = ConvertMarkToTimestamp(endMark);
        }
        else if (options?.End is { } end)
        {
            endTime = ConvertMarkToTimestamp(end);
        }
        else if (options is { Start: { } startWithDuration, Duration: { } durationFromStart })
        {
            endTime = ConvertMarkToTimestamp(startWithDuration) + ConvertMarkToTimestamp(durationFromStart);
        }
        else
        {
            endTime = Now();
        }

        double startTime;
        if (options?.Start is { } start)
        {
            startTime = ConvertMarkToTimestamp(start);
        }
        else if (options is { Duration: { } durationToEnd, End: { } endWithDuration })
        {
            startTime = ConvertMarkToTimestamp(endWithDuration) - ConvertMarkToTimestamp(durationToEnd);
        }
        else if (startOrMeasureOptions is string startMark)
        {
            startTime = ConvertMarkToTimestamp(startMark);
        }
        else
        {
            startTime = 0;
        }

        var entry = new PerformanceMeasure(_global, measureName, startTime, endTime - startTime, UserTiming.CloneDetail(_global, options?.Detail));
        QueueEntry(entry);
        _measureBuffer.Add(entry);
        return entry;
    }

    /// <summary>https://w3c.github.io/user-timing/#clearmeasures-method: removes the measures of a name, or all the measures, from the performance timeline.</summary>
    /// <param name="measureName">The name of the measures to remove, or null for all the measures.</param>
    [IdlMember("clearMeasures", "https://w3c.github.io/user-timing/#dom-performance-clearmeasures")]
    public void ClearMeasures(string? measureName = null) => ClearBuffer(_measureBuffer, measureName);

    private static void ClearBuffer(List<PerformanceEntry> buffer, string? name)
    {
        if (name is null)
        {
            buffer.Clear();
        }
        else
        {
            buffer.RemoveAll(entry => entry.Name == name);
        }
    }

    /// <summary>https://w3c.github.io/user-timing/#dfn-convert-a-mark-to-a-timestamp</summary>
    private double ConvertMarkToTimestamp(object mark)
    {
        if (mark is string name)
        {
            if (Array.IndexOf(PerformanceTiming.AttributeNames, name) >= 0)
                return ConvertNameToTimestamp(name);

            for (var i = _markBuffer.Count - 1; i >= 0; i--)
            {
                if (_markBuffer[i].Name == name)
                    return _markBuffer[i].StartTime;
            }

            throw new DomException($"The mark '{name}' does not exist", "SyntaxError");
        }

        var timestamp = Convert.ToDouble(mark, System.Globalization.CultureInfo.InvariantCulture);
        if (timestamp < 0)
            throw new TypeErrorException("A time of a measure cannot be negative");

        return timestamp;
    }

    /// <summary>https://w3c.github.io/user-timing/#dfn-convert-a-name-to-a-timestamp: the value of an attribute of <see cref="PerformanceTiming"/>, relative to the start of the navigation.</summary>
    private double ConvertNameToTimestamp(string name)
    {
        if (_global is not Window || Timing is not { } timing)
            throw new TypeErrorException($"'{name}' is the name of an attribute of PerformanceTiming, which is only available in a window");

        if (name == "navigationStart")
            return 0;

        var startTime = timing.GetValue(name) ?? 0;
        if (startTime == 0)
            throw new DomException($"The attribute '{name}' of PerformanceTiming is not available yet", "InvalidAccessError");

        return (double)startTime - timing.NavigationStart;
    }

    /// <summary>
    /// Gets the legacy timing of the navigation of the document of the window (https://w3c.github.io/navigation-timing/#dom-performance-timing),
    /// or null for a worker.
    /// </summary>
    [IdlMember("timing", "https://w3c.github.io/navigation-timing/#dom-performance-timing")]
    public PerformanceTiming? Timing => _timing ??= _global is Window window ? new PerformanceTiming(window) : null;

    /// <summary>
    /// Gets the legacy description of the navigation of the document of the window (https://w3c.github.io/navigation-timing/#dom-performance-navigation),
    /// or null for a worker.
    /// </summary>
    [IdlMember("navigation", "https://w3c.github.io/navigation-timing/#dom-performance-navigation")]
    public PerformanceNavigation? Navigation => _navigation ??= _global is Window window ? new PerformanceNavigation(window) : null;

    /// <summary>
    /// Sets the navigation timing entry of the document of the window (https://w3c.github.io/navigation-timing/#dfn-create-the-navigation-timing-entry):
    /// it replaces the entry of the initial <c>about:blank</c> document when the window is reused.
    /// </summary>
    internal void SetNavigationTimingEntry(PerformanceNavigationTiming entry)
    {
        entry.Id = GenerateEntryId();
        entry.NavigationId = 1;
        NavigationTimingEntry = entry;
    }

    /// <summary>Queues the navigation timing entry to the performance observers, once the document is completely loaded (https://w3c.github.io/navigation-timing/#dfn-queue-the-navigation-timing-entry).</summary>
    internal void QueueNavigationTimingEntry()
    {
        if (NavigationTimingEntry is { } entry && _global.Options.EffectiveFeatures.NavigationTimingEnabled)
        {
            QueueEntry(entry);
        }
    }

    /// <summary>https://w3c.github.io/resource-timing/#dom-performance-clearresourcetimings: removes the entries of the resource timing buffer.</summary>
    [IdlMember("clearResourceTimings", "https://w3c.github.io/resource-timing/#dom-performance-clearresourcetimings")]
    public void ClearResourceTimings() => _resourceTimingBuffer.Clear();

    /// <summary>
    /// https://w3c.github.io/resource-timing/#dom-performance-setresourcetimingbuffersize: sets the size limit of the resource
    /// timing buffer. The entries above the limit are kept.
    /// </summary>
    [IdlMember("setResourceTimingBufferSize", "https://w3c.github.io/resource-timing/#dom-performance-setresourcetimingbuffersize")]
    public void SetResourceTimingBufferSize(uint maxSize) => _resourceTimingBufferSizeLimit = (int)Math.Min(maxSize, int.MaxValue);

    /// <summary>The event handler of the <c>resourcetimingbufferfull</c> event, fired when the resource timing buffer is full.</summary>
    [IdlMember("onresourcetimingbufferfull", "https://w3c.github.io/resource-timing/#dom-performance-onresourcetimingbufferfull")]
    public Action<Event>? OnResourceTimingBufferFull
    {
        get => GetEventHandler("resourcetimingbufferfull");
        set => SetEventHandler("resourcetimingbufferfull", value);
    }

    /// <summary>
    /// Gets the number of trusted events of each type considered for Event Timing that were dispatched in the window
    /// (https://w3c.github.io/event-timing/#dom-performance-eventcounts), or null for a worker. The counts stay at 0 when Event
    /// Timing is disabled (<see cref="WebPlatformFeatures.EventTimingEnabled"/>).
    /// </summary>
    [IdlMember("eventCounts", "https://w3c.github.io/event-timing/#dom-performance-eventcounts")]
    public EventCounts? EventCounts => _global is Window window ? window.Options.EffectiveFeatures.EventTimingEnabled ? window.EventTiming.EventCounts : _eventCounts ??= new EventCounts() : null;

    /// <summary>
    /// Gets the number of interactions of the user with the window (https://w3c.github.io/event-timing/#dom-performance-interactioncount):
    /// the presses of a pointer followed by its release, and the presses of a key. It is 0 for a worker.
    /// </summary>
    [IdlMember("interactionCount", "https://w3c.github.io/event-timing/#dom-performance-interactioncount")]
    public ulong InteractionCount => _global is Window window && window.Options.EffectiveFeatures.EventTimingEnabled ? window.EventTiming.InteractionCount : 0;

    /// <summary>
    /// https://w3c.github.io/performance-timeline/#dfn-generate-a-performance-entry-id: the last performance entry id is
    /// increased by a small random number, so that it is not a counter of the entries.
    /// </summary>
    internal ulong GenerateEntryId()
    {
        _lastEntryId += (ulong)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 8);
        return _lastEntryId;
    }

    /// <summary>Returns the entries of the buffer of an entry type that are available from the timeline, or an empty list.</summary>
    internal List<PerformanceEntry> GetBuffer(string type) => type switch
    {
        PerformanceResourceTiming.ResourceEntryType => _resourceTimingBuffer,
        PerformanceMark.MarkEntryType => _markBuffer,
        PerformanceMeasure.MeasureEntryType => _measureBuffer,
        PerformanceNavigationTiming.NavigationEntryType => NavigationBuffer,
        LayoutShift.LayoutShiftEntryType => _layoutShiftBuffer ?? [],
        PerformancePaintTiming.PaintEntryType => _paintBuffer ?? [],
        VisibilityStateEntry.VisibilityStateEntryType => _visibilityStateBuffer ?? [],
        LargestContentfulPaint.LargestContentfulPaintEntryType => _largestContentfulPaintBuffer ?? [],
        PerformanceElementTiming.ElementEntryType => _elementTimingBuffer ?? [],
        PerformanceEventTiming.EventEntryType => _eventTimingBuffer ?? [],
        PerformanceEventTiming.FirstInputEntryType => _firstInputBuffer ?? [],
        _ => [],
    };

    /// <summary>Returns the dropped entries count of an entry type (https://w3c.github.io/performance-timeline/#dfn-dropped-entries-count).</summary>
    internal ulong GetDroppedEntriesCount(string type) => type switch
    {
        PerformanceResourceTiming.ResourceEntryType => ResourceDroppedEntriesCount,
        LayoutShift.LayoutShiftEntryType => _layoutShiftDroppedEntriesCount,
        PerformancePaintTiming.PaintEntryType => _paintDroppedEntriesCount,
        LargestContentfulPaint.LargestContentfulPaintEntryType => _largestContentfulPaintDroppedEntriesCount,
        PerformanceElementTiming.ElementEntryType => _elementTimingDroppedEntriesCount,
        PerformanceEventTiming.EventEntryType => _eventTimingDroppedEntriesCount,
        PerformanceEventTiming.FirstInputEntryType => _firstInputDroppedEntriesCount,
        _ => 0,
    };

    /// <summary>
    /// Called when a performance observer of the global object starts to observe an entry type: the layout shifts of the
    /// associated document are computed from then on by the rendering updates (a document whose layout shifts are not observed
    /// is not laid out for them).
    /// </summary>
    internal void OnObserving(string type)
    {
        if (type == LayoutShift.LayoutShiftEntryType && _global.AssociatedDocument is { } document)
        {
            document.StartLayoutShiftTracking();
        }
        else if (type is PerformancePaintTiming.PaintEntryType or LargestContentfulPaint.LargestContentfulPaintEntryType or PerformanceElementTiming.ElementEntryType)
        {
            _global.AssociatedDocument?.StartPaintTiming();
        }
    }

    /// <summary>
    /// Called when a script reads the entries of the performance timeline of a type (or of all the types): the paint timing entries
    /// are available from the timeline, so the paints of the associated document are computed by the rendering updates from then on.
    /// </summary>
    private void OnReadingTimeline(string? type)
    {
        if ((type is null || type == PerformancePaintTiming.PaintEntryType) && _global is Window { BrowsingContext.Options.EffectiveFeatures.PaintTimingEnabled: true })
        {
            _global.AssociatedDocument?.StartPaintTiming();
        }
    }

    /// <summary>The buffer of the <c>navigation</c> type: the navigation timing entry of the document of the window, when Navigation Timing is enabled.</summary>
    private List<PerformanceEntry> NavigationBuffer => NavigationTimingEntry is { } entry && _global.Options.EffectiveFeatures.NavigationTimingEnabled ? [entry] : [];

    /// <summary>https://w3c.github.io/performance-timeline/#dfn-filter-buffer-map-by-name-and-type</summary>
    private List<PerformanceEntry> FilterBufferMap(string? name, string? type)
    {
        // The buffers of the performance entry buffer map whose entries are available from the timeline: the navigation timing,
        // the resource timing, the marks, the measures, the paint timing, the first input and the visibility states (the layout shifts, the largest
        // contentful paints, the element timings and the event timings are not: https://w3c.github.io/timing-entrytypes-registry/#registry)
        var result = new List<PerformanceEntry>();
        AddBuffer(NavigationBuffer, PerformanceNavigationTiming.NavigationEntryType);
        AddBuffer(_resourceTimingBuffer, PerformanceResourceTiming.ResourceEntryType);
        AddBuffer(_markBuffer, PerformanceMark.MarkEntryType);
        AddBuffer(_measureBuffer, PerformanceMeasure.MeasureEntryType);
        if (_paintBuffer is not null)
        {
            AddBuffer(_paintBuffer, PerformancePaintTiming.PaintEntryType);
        }

        if (_firstInputBuffer is not null)
        {
            AddBuffer(_firstInputBuffer, PerformanceEventTiming.FirstInputEntryType);
        }

        if (_visibilityStateBuffer is not null)
        {
            AddBuffer(_visibilityStateBuffer, VisibilityStateEntry.VisibilityStateEntryType);
        }

        SortByStartTime(result);
        return result;

        void AddBuffer(List<PerformanceEntry> buffer, string bufferType)
        {
            if (buffer.Count > 0 && (type is null || type == bufferType))
            {
                result.AddRange(FilterBuffer(buffer, name, type));
            }
        }
    }

    /// <summary>https://w3c.github.io/performance-timeline/#dfn-filter-buffer-by-name-and-type</summary>
    internal static List<PerformanceEntry> FilterBuffer(IEnumerable<PerformanceEntry> buffer, string? name, string? type)
    {
        var result = new List<PerformanceEntry>();
        foreach (var entry in buffer)
        {
            if (type is not null && type != entry.EntryType)
                continue;

            if (name is not null && name != entry.Name)
                continue;

            result.Add(entry);
        }

        return result;
    }

    /// <summary>Sorts entries in chronological order with respect to their start time, keeping the order of the entries that start at the same time.</summary>
    internal static void SortByStartTime(List<PerformanceEntry> entries)
    {
        var sorted = entries.OrderBy(entry => entry.StartTime).ToList();
        entries.Clear();
        entries.AddRange(sorted);
    }

    /// <summary>https://w3c.github.io/performance-timeline/#queue-a-performanceentry</summary>
    internal void QueueEntry(PerformanceEntry entry)
    {
        if (entry.Id == 0)
        {
            entry.Id = GenerateEntryId();
        }

        // The navigation id is the id of the most recent navigation of the associated document (the document is not
        // navigated again: its first navigation)
        entry.NavigationId = _global.AssociatedDocument is null ? 0UL : 1UL;

        var interestedObservers = new List<PerformanceObserver>();
        foreach (var registeredObserver in _registeredObservers)
        {
            if (registeredObserver.Options.Exists(options => (options.Type == entry.EntryType || (options.EntryTypes?.Contains(entry.EntryType) ?? false)) && ShouldAddEntry(entry, options)))
            {
                interestedObservers.Add(registeredObserver.Observer);
            }
        }

        foreach (var observer in interestedObservers)
        {
            observer.Buffer.Add(entry);
        }

        // The resource timing entries are added to the resource timing buffer by "mark resource timing" (AddResourceTimingEntry).
        // The task only notifies the observers whose buffer is not empty: it is not queued when no observer is interested.
        if (interestedObservers.Count > 0)
        {
            QueueObserverTask();
        }
    }

    /// <summary>
    /// https://w3c.github.io/performance-timeline/#queue-a-performanceentry for a layout shift: queued to the observers, and
    /// added to the performance entry buffer of the <c>layout-shift</c> type (used by the <c>buffered</c> observers) unless it is full.
    /// </summary>
    internal void QueueLayoutShiftEntry(LayoutShift entry)
    {
        QueueEntry(entry);
        _layoutShiftBuffer ??= [];
        if (_layoutShiftBuffer.Count < LayoutShift.MaxBufferSize)
        {
            _layoutShiftBuffer.Add(entry);
        }
        else
        {
            _layoutShiftDroppedEntriesCount++;
        }
    }

    /// <summary>A paint timing entry (https://w3c.github.io/paint-timing/#report-paint-timing): queued to the observers and added to the buffer of the <c>paint</c> type.</summary>
    internal void QueuePaintTimingEntry(PerformancePaintTiming entry) => QueueBufferedEntry(entry, ref _paintBuffer, PerformancePaintTiming.MaxBufferSize, ref _paintDroppedEntriesCount);

    /// <summary>
    /// A visibility state entry (https://html.spec.whatwg.org/multipage/interaction.html#update-the-visibility-state): queued to
    /// the observers and added to the buffer of the <c>visibility-state</c> type.
    /// </summary>
    /// <param name="visible">The visibility state.</param>
    /// <param name="timestamp">The time of the change, or null for the initial visibility state of the document of the window (the entry starts at 0, and replaces the entries of the initial <c>about:blank</c> document when the window is reused).</param>
    internal void QueueVisibilityStateEntry(bool visible, double? timestamp)
    {
        if (!_global.Options.EffectiveFeatures.VisibilityStateEntryEnabled)
            return;

        if (timestamp is null)
        {
            _visibilityStateBuffer?.Clear();
        }

        var droppedEntriesCount = 0UL;
        QueueBufferedEntry(new VisibilityStateEntry(_global, visible, timestamp ?? 0), ref _visibilityStateBuffer, VisibilityStateEntry.MaxBufferSize, ref droppedEntriesCount);
    }

    /// <summary>A largest contentful paint entry: queued to the observers and added to the buffer of the <c>largest-contentful-paint</c> type.</summary>
    internal void QueueLargestContentfulPaintEntry(LargestContentfulPaint entry) => QueueBufferedEntry(entry, ref _largestContentfulPaintBuffer, LargestContentfulPaint.MaxBufferSize, ref _largestContentfulPaintDroppedEntriesCount);

    /// <summary>An element timing entry: queued to the observers and added to the buffer of the <c>element</c> type.</summary>
    internal void QueueElementTimingEntry(PerformanceElementTiming entry) => QueueBufferedEntry(entry, ref _elementTimingBuffer, PerformanceElementTiming.MaxBufferSize, ref _elementTimingDroppedEntriesCount);

    /// <summary>
    /// An event timing entry (https://w3c.github.io/event-timing/#dispatch-pending-event-timing-entries): queued to the observers
    /// whose duration threshold it reaches, and added to the buffer of its type: the <c>event</c> entries that last at least
    /// 104 ms, and the <c>first-input</c> entry.
    /// </summary>
    internal void QueueEventTimingEntry(PerformanceEventTiming entry)
    {
        if (entry.EntryType == PerformanceEventTiming.FirstInputEntryType)
        {
            QueueBufferedEntry(entry, ref _firstInputBuffer, PerformanceEventTiming.FirstInputMaxBufferSize, ref _firstInputDroppedEntriesCount);
        }
        else
        {
            QueueBufferedEntry(entry, ref _eventTimingBuffer, PerformanceEventTiming.EventMaxBufferSize, ref _eventTimingDroppedEntriesCount);
        }
    }

    /// <summary>
    /// https://w3c.github.io/performance-timeline/#dfn-should-add-entry: whether an entry is added to the buffer of an observer
    /// that has these options, or to the performance entry buffer of its type when <paramref name="options"/> is null. Only
    /// the event timing entries are filtered (by their duration).
    /// </summary>
    internal static bool ShouldAddEntry(PerformanceEntry entry, PerformanceObserverInit? options) => entry is not PerformanceEventTiming eventTiming || eventTiming.ShouldAdd(options);

    /// <summary>https://w3c.github.io/performance-timeline/#queue-a-performanceentry, with the buffer of the type of the entry (used by the <c>buffered</c> observers) unless it is full.</summary>
    private void QueueBufferedEntry(PerformanceEntry entry, ref List<PerformanceEntry>? buffer, int maxBufferSize, ref ulong droppedEntriesCount)
    {
        QueueEntry(entry);
        buffer ??= [];
        if (buffer.Count >= maxBufferSize)
        {
            droppedEntriesCount++;
        }
        else if (ShouldAddEntry(entry, options: null))
        {
            buffer.Add(entry);
        }
    }

    /// <summary>https://w3c.github.io/performance-timeline/#dfn-queue-the-performanceobserver-task</summary>
    internal void QueueObserverTask()
    {
        if (_observerTaskQueued)
            return;

        _observerTaskQueued = true;
        _global.QueueGlobalTask(() =>
        {
            _observerTaskQueued = false;
            foreach (var registeredObserver in _registeredObservers.ToArray())
            {
                var observer = registeredObserver.Observer;
                if (observer.Buffer.Count == 0)
                    continue;

                var entries = observer.Buffer.ToArray();
                observer.Buffer.Clear();
                var entryList = new PerformanceObserverEntryList(entries);
                ulong? droppedEntriesCount = null;
                if (observer.RequiresDroppedEntries)
                {
                    droppedEntriesCount = 0;
                    foreach (var options in registeredObserver.Options)
                    {
                        if (options.Type is { } type)
                        {
                            droppedEntriesCount += GetDroppedEntriesCount(type);
                        }

                        foreach (var entryType in options.EntryTypes ?? [])
                        {
                            droppedEntriesCount += GetDroppedEntriesCount(entryType);
                        }
                    }

                    observer.RequiresDroppedEntries = false;
                }

                observer.Invoke(entryList, new PerformanceObserverCallbackOptions { DroppedEntriesCount = droppedEntriesCount }, _global);
            }
        });
    }

    /// <summary>https://w3c.github.io/resource-timing/#dfn-add-a-performanceresourcetiming-entry</summary>
    internal void AddResourceTimingEntry(PerformanceEntry entry)
    {
        // https://w3c.github.io/performance-timeline/#dfn-determine-if-a-performance-entry-buffer-is-full: the entry is counted
        // as dropped when the buffer is full while it is queued, even if the resourcetimingbufferfull event makes room for it
        if (!CanAddResourceTimingEntry())
        {
            ResourceDroppedEntriesCount++;
        }

        if (CanAddResourceTimingEntry() && !_resourceTimingBufferFullEventPending)
        {
            _resourceTimingBuffer.Add(entry);
            return;
        }

        if (!_resourceTimingBufferFullEventPending)
        {
            _resourceTimingBufferFullEventPending = true;
            _global.QueueGlobalTask(FireBufferFullEvent);
        }

        _resourceTimingSecondaryBuffer.Add(entry);
    }

    /// <summary>https://w3c.github.io/resource-timing/#dfn-can-add-resource-timing-entry</summary>
    private bool CanAddResourceTimingEntry() => _resourceTimingBuffer.Count < _resourceTimingBufferSizeLimit;

    /// <summary>https://w3c.github.io/resource-timing/#dfn-fire-a-buffer-full-event</summary>
    private void FireBufferFullEvent()
    {
        while (_resourceTimingSecondaryBuffer.Count > 0)
        {
            var excessEntriesBefore = _resourceTimingSecondaryBuffer.Count;
            if (!CanAddResourceTimingEntry())
            {
                FireEvent(new Event("resourcetimingbufferfull"));
            }

            CopySecondaryBuffer();
            var excessEntriesAfter = _resourceTimingSecondaryBuffer.Count;
            if (excessEntriesBefore <= excessEntriesAfter)
            {
                // The entries that do not fit in the buffer are dropped
                _resourceTimingSecondaryBuffer.Clear();
                break;
            }
        }

        _resourceTimingBufferFullEventPending = false;
    }

    /// <summary>https://w3c.github.io/resource-timing/#dfn-copy-secondary-buffer</summary>
    private void CopySecondaryBuffer()
    {
        while (_resourceTimingSecondaryBuffer.Count > 0 && CanAddResourceTimingEntry())
        {
            _resourceTimingBuffer.Add(_resourceTimingSecondaryBuffer[0]);
            _resourceTimingSecondaryBuffer.RemoveAt(0);
        }
    }
}
