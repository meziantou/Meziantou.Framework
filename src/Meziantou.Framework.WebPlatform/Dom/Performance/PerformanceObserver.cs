namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/performance-timeline/#dom-performanceobservercallback</summary>
/// <param name="entries">The entries observed since the previous call.</param>
/// <param name="observer">The observer.</param>
/// <param name="options">The number of entries dropped because a buffer was full, on the first call after <see cref="PerformanceObserver.Observe"/>.</param>
[IdlInterface("PerformanceObserverCallback", "https://w3c.github.io/performance-timeline/#dom-performanceobservercallback")]
public delegate void PerformanceObserverCallback(PerformanceObserverEntryList entries, PerformanceObserver observer, PerformanceObserverCallbackOptions options);

/// <summary>https://w3c.github.io/performance-timeline/#dom-performanceobservercallbackoptions</summary>
[IdlInterface("PerformanceObserverCallbackOptions", "https://w3c.github.io/performance-timeline/#dom-performanceobservercallbackoptions")]
public sealed class PerformanceObserverCallbackOptions
{
    /// <summary>Gets the number of entries dropped because a buffer was full, or null when it is not reported.</summary>
    [IdlMember("droppedEntriesCount", "https://w3c.github.io/performance-timeline/#dom-performanceobservercallbackoptions-droppedentriescount")]
    public ulong? DroppedEntriesCount { get; set; }
}

/// <summary>https://w3c.github.io/performance-timeline/#performanceobserverinit-dictionary</summary>
[IdlInterface("PerformanceObserverInit", "https://w3c.github.io/performance-timeline/#dom-performanceobserverinit")]
public sealed class PerformanceObserverInit
{
    /// <summary>Gets or sets the entry types to observe. No other member can be set with it.</summary>
    [IdlMember("entryTypes", "https://w3c.github.io/performance-timeline/#dom-performanceobserverinit-entrytypes")]
    public IReadOnlyList<string>? EntryTypes { get; set; }

    /// <summary>Gets or sets a single entry type to observe.</summary>
    [IdlMember("type", "https://w3c.github.io/performance-timeline/#dom-performanceobserverinit-type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets whether the entries of the performance entry buffer of <see cref="Type"/> are observed.</summary>
    [IdlMember("buffered", "https://w3c.github.io/performance-timeline/#dom-performanceobserverinit-buffered")]
    public bool? Buffered { get; set; }

    /// <summary>
    /// Gets or sets the minimum duration of the event timing entries (<see cref="PerformanceEventTiming"/>) that are observed
    /// with <see cref="Type"/>, in milliseconds (https://w3c.github.io/event-timing/#sec-modifications-perf-timeline): 104 by
    /// default, and at least 16.
    /// </summary>
    [IdlMember("durationThreshold", "https://w3c.github.io/event-timing/#dom-performanceobserverinit-durationthreshold")]
    public double? DurationThreshold { get; set; }
}

/// <summary>A registered performance observer (https://w3c.github.io/performance-timeline/#dfn-registered-performance-observer).</summary>
internal sealed class RegisteredPerformanceObserver(PerformanceObserver observer, PerformanceObserverInit options)
{
    public PerformanceObserver Observer { get; } = observer;

    /// <summary>The options list.</summary>
    public List<PerformanceObserverInit> Options { get; } = [options];
}

/// <summary>Observes the entries added to the performance timeline of a global object (https://w3c.github.io/performance-timeline/#the-performanceobserver-interface).</summary>
[IdlInterface("PerformanceObserver", "https://w3c.github.io/performance-timeline/#dom-performanceobserver")]
public sealed class PerformanceObserver
{
    private readonly PerformanceObserverCallback _callback;
    private readonly GlobalScope _global;
    private ObserverType _observerType;

    /// <summary>Creates an observer of the performance timeline of the current global object.</summary>
    /// <param name="callback">The callback invoked with the observed entries.</param>
    [IdlMember("constructor", "https://w3c.github.io/performance-timeline/#dom-performanceobserver-constructor")]
    public PerformanceObserver(PerformanceObserverCallback callback)
        : this(GlobalScope.Current ?? throw new InvalidOperationException("A performance observer can only be created by a window or a worker"), callback)
    {
    }

    /// <summary>Creates an observer of the performance timeline of a global object.</summary>
    /// <param name="global">The global object whose performance timeline is observed.</param>
    /// <param name="callback">The callback invoked with the observed entries.</param>
    [NotInIdl("The .NET constructor whose global object is explicit")]
    public PerformanceObserver(GlobalScope global, PerformanceObserverCallback callback)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(callback);
        _global = global;
        _callback = callback;
    }

    private enum ObserverType
    {
        Undefined,
        Single,
        Multiple,
    }

    /// <summary>The observer buffer (https://w3c.github.io/performance-timeline/#dfn-observer-buffer).</summary>
    internal List<PerformanceEntry> Buffer { get; } = [];

    /// <summary>Whether the next call of the callback reports the dropped entries count (https://w3c.github.io/performance-timeline/#dfn-requires-dropped-entries).</summary>
    internal bool RequiresDroppedEntries { get; set; }

    /// <summary>Gets the entry types supported by the user agent, in alphabetical order (https://w3c.github.io/performance-timeline/#supportedentrytypes-attribute).</summary>
    [IdlMember("supportedEntryTypes", "https://w3c.github.io/performance-timeline/#dom-performanceobserver-supportedentrytypes")]
    public static IReadOnlyList<string> SupportedEntryTypes => GlobalScope.Current?.Performance.SupportedEntryTypes ?? [PerformanceResourceTiming.ResourceEntryType];

    /// <summary>Starts to observe the entries of one or several types (https://w3c.github.io/performance-timeline/#observe-method).</summary>
    /// <param name="options">The entry types to observe.</param>
    [IdlMember("observe", "https://w3c.github.io/performance-timeline/#dom-performanceobserver-observe")]
    public void Observe(PerformanceObserverInit? options = null)
    {
        options ??= new PerformanceObserverInit();
        var performance = _global.Performance;
        if (options.EntryTypes is null && options.Type is null)
            throw new TypeErrorException("The options must include entryTypes or type");

        if (options.EntryTypes is not null && (options.Type is not null || options.Buffered is not null))
            throw new TypeErrorException("entryTypes cannot be used with another option");

        if (_observerType == ObserverType.Undefined)
        {
            _observerType = options.EntryTypes is not null ? ObserverType.Multiple : ObserverType.Single;
        }

        if (_observerType == ObserverType.Single && options.EntryTypes is not null)
            throw new DomException("The observer observes a single type", "InvalidModificationError");

        if (_observerType == ObserverType.Multiple && options.Type is not null)
            throw new DomException("The observer observes a list of types", "InvalidModificationError");

        RequiresDroppedEntries = true;
        var registeredObserver = performance.RegisteredObservers.Find(registered => ReferenceEquals(registered.Observer, this));
        if (_observerType == ObserverType.Multiple)
        {
            var entryTypes = options.EntryTypes!.Where(performance.SupportedEntryTypes.Contains).ToList();
            if (entryTypes.Count == 0)
                return;

            foreach (var entryType in entryTypes)
            {
                performance.OnObserving(entryType);
            }

            var copy = new PerformanceObserverInit { EntryTypes = entryTypes };
            if (registeredObserver is not null)
            {
                registeredObserver.Options.Clear();
                registeredObserver.Options.Add(copy);
            }
            else
            {
                performance.RegisteredObservers.Add(new RegisteredPerformanceObserver(this, copy));
            }

            return;
        }

        var type = options.Type!;
        if (!performance.SupportedEntryTypes.Contains(type))
            return;

        performance.OnObserving(type);
        var typeOptions = new PerformanceObserverInit { Type = type, Buffered = options.Buffered, DurationThreshold = options.DurationThreshold };
        if (registeredObserver is not null)
        {
            var index = registeredObserver.Options.FindIndex(current => current.Type == type);
            if (index >= 0)
            {
                registeredObserver.Options[index] = typeOptions;
            }
            else
            {
                registeredObserver.Options.Add(typeOptions);
            }
        }
        else
        {
            performance.RegisteredObservers.Add(new RegisteredPerformanceObserver(this, typeOptions));
        }

        if (options.Buffered == true)
        {
            Buffer.AddRange(performance.GetBuffer(type).Where(entry => Performance.ShouldAddEntry(entry, typeOptions)));
            performance.QueueObserverTask();
        }
    }

    /// <summary>Stops observing the entries and discards the entries of the observer buffer (https://w3c.github.io/performance-timeline/#disconnect-method).</summary>
    [IdlMember("disconnect", "https://w3c.github.io/performance-timeline/#dom-performanceobserver-disconnect")]
    public void Disconnect()
    {
        _global.Performance.RegisteredObservers.RemoveAll(registered => ReferenceEquals(registered.Observer, this));
        Buffer.Clear();
    }

    /// <summary>Returns the entries of the observer buffer and empties it (https://w3c.github.io/performance-timeline/#takerecords-method).</summary>
    [IdlMember("takeRecords", "https://w3c.github.io/performance-timeline/#dom-performanceobserver-takerecords")]
    public IReadOnlyList<PerformanceEntry> TakeRecords()
    {
        var records = Buffer.ToArray();
        Buffer.Clear();
        return records;
    }

    /// <summary>Invokes the callback, reporting its exception (https://webidl.spec.whatwg.org/#invoke-a-callback-function).</summary>
    internal void Invoke(PerformanceObserverEntryList entries, PerformanceObserverCallbackOptions options, GlobalScope global)
    {
        try
        {
            _callback(entries, this, options);
        }
#pragma warning disable CA1031 // "report the exception": an exception of a callback does not stop the notification of the other observers
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Scripting.ScriptErrors.Report(ex, global);
        }
    }
}

/// <summary>The entries given to the callback of a <see cref="PerformanceObserver"/> (https://w3c.github.io/performance-timeline/#performanceobserverentrylist-interface).</summary>
[IdlInterface("PerformanceObserverEntryList", "https://w3c.github.io/performance-timeline/#dom-performanceobserverentrylist")]
public sealed class PerformanceObserverEntryList
{
    private readonly PerformanceEntry[] _entries;

    internal PerformanceObserverEntryList(PerformanceEntry[] entries)
    {
        _entries = entries;
    }

    /// <summary>Returns the entries, in chronological order.</summary>
    [IdlMember("getEntries", "https://w3c.github.io/performance-timeline/#dom-performanceobserverentrylist-getentries")]
    public IReadOnlyList<PerformanceEntry> GetEntries() => Filter(name: null, type: null);

    /// <summary>Returns the entries of a type, in chronological order.</summary>
    [IdlMember("getEntriesByType", "https://w3c.github.io/performance-timeline/#dom-performanceobserverentrylist-getentriesbytype")]
    public IReadOnlyList<PerformanceEntry> GetEntriesByType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Filter(name: null, type);
    }

    /// <summary>Returns the entries of a name, and optionally of a type, in chronological order.</summary>
    [IdlMember("getEntriesByName", "https://w3c.github.io/performance-timeline/#dom-performanceobserverentrylist-getentriesbyname")]
    public IReadOnlyList<PerformanceEntry> GetEntriesByName(string name, string? type = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Filter(name, type);
    }

    /// <summary>https://w3c.github.io/performance-timeline/#dfn-filter-buffer-by-name-and-type, sorted by start time.</summary>
    private List<PerformanceEntry> Filter(string? name, string? type)
    {
        var result = Performance.FilterBuffer(_entries, name, type);
        Performance.SortByStartTime(result);
        return result;
    }
}
