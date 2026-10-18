using System.Collections;
using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The timing of a trusted event of the user input (https://w3c.github.io/event-timing/#sec-performance-event-timing): the time
/// the event was created, the time its dispatch started and ended, and the time until the next rendering update of its window.
/// It is reported to the <c>event</c> performance observers of the window when its duration is at least their duration
/// threshold, and the entry of the first interaction is also reported as the <c>first-input</c> entry.
/// </summary>
[IdlInterface("PerformanceEventTiming", "https://w3c.github.io/event-timing/#performanceeventtiming")]
public sealed class PerformanceEventTiming : PerformanceEntry
{
    /// <summary>The entry type of the event timing entries.</summary>
    internal const string EventEntryType = "event";

    /// <summary>The entry type of the entry of the first input of a window.</summary>
    internal const string FirstInputEntryType = "first-input";

    /// <summary>The maximum size of the performance entry buffer of the <c>event</c> type (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int EventMaxBufferSize = 150;

    /// <summary>The maximum size of the performance entry buffer of the <c>first-input</c> type.</summary>
    internal const int FirstInputMaxBufferSize = 1;

    /// <summary>The duration threshold of the performance entry buffer and of the observers that have no <c>durationThreshold</c>, in milliseconds.</summary>
    internal const double DefaultDurationThreshold = 104;

    /// <summary>The minimum duration of the entries that are queued, and the minimum duration threshold of an observer, in milliseconds.</summary>
    internal const double MinimumDurationThreshold = 16;

    /// <summary>The granularity of the duration, in milliseconds.</summary>
    private const double DurationGranularity = 8;

    private double _duration;

    internal PerformanceEventTiming(GlobalScope global, string entryType, string name, double startTime, double processingStart, bool cancelable, ulong interactionId)
        : base(global, entryType, name, startTime, duration: 0)
    {
        ProcessingStart = processingStart;
        ProcessingEnd = processingStart;
        Cancelable = cancelable;
        InteractionId = interactionId;
        TargetSelector = "";
    }

    /// <summary>The last target of the event (https://w3c.github.io/event-timing/#performanceeventtiming-eventtarget), or null when it is not a node.</summary>
    internal Node? EventTarget { get; set; }

    /// <summary>The order of the dispatch of the event among the events timed in its window.</summary>
    internal long Sequence { get; init; }

    /// <summary>Whether the duration was computed by a rendering update (a duration of 0 is the one of an event followed by a rendering update in less than 4 ms).</summary>
    internal bool HasDuration { get; private set; }

    /// <summary>Gets the time the dispatch of the event started (the event listeners are about to run), in milliseconds relative to the time origin of the window.</summary>
    [IdlMember("processingStart", "https://w3c.github.io/event-timing/#dom-performanceeventtiming-processingstart")]
    public double ProcessingStart { get; }

    /// <summary>Gets the time the dispatch of the event ended (the event listeners and the default actions ran), in milliseconds relative to the time origin of the window.</summary>
    [IdlMember("processingEnd", "https://w3c.github.io/event-timing/#dom-performanceeventtiming-processingend")]
    public double ProcessingEnd { get; internal set; }

    /// <summary>Gets whether the event was cancelable.</summary>
    [IdlMember("cancelable", "https://w3c.github.io/event-timing/#dom-performanceeventtiming-cancelable")]
    public bool Cancelable { get; }

    /// <summary>
    /// Gets the last target of the event (the target retargeted out of the shadow trees it propagated out of), or null when it
    /// is no longer exposed (https://w3c.github.io/paint-timing/#exposed-for-paint-timing): it is not connected, it is in a
    /// shadow tree, or its document is not fully active.
    /// </summary>
    [IdlMember("target", "https://w3c.github.io/event-timing/#dom-performanceeventtiming-target")]
    public Node? Target => PaintTimingHelpers.IsExposedForPaintTiming(EventTarget, document: null) ? EventTarget : null;

    /// <summary>
    /// Gets a selector that identifies the last target of the event when the event was dispatched
    /// (https://w3c.github.io/event-timing/#sec-target-selectors): the name of the node, followed by its id (<c>DIV#id</c>) or
    /// else by its <c>src</c> attribute (<c>IMG[src=...]</c>).
    /// </summary>
    [IdlMember("targetSelector", "https://w3c.github.io/event-timing/#dom-performanceeventtiming-targetselector")]
    public string TargetSelector { get; internal set; }

    /// <summary>
    /// Gets the identifier of the interaction of the user the event belongs to (a press and a release of a pointer with their
    /// click, or a press and a release of a key), or 0 when it belongs to none.
    /// </summary>
    [IdlMember("interactionId", "https://w3c.github.io/event-timing/#dom-performanceeventtiming-interactionid")]
    public ulong InteractionId { get; internal set; }

    private protected override double GetDuration() => _duration;

    /// <summary>
    /// Sets the duration to the time between the creation of the event and a rendering update, rounded to the nearest 8 ms
    /// (https://w3c.github.io/event-timing/#set-event-timing-entry-duration).
    /// </summary>
    internal void SetDuration(double renderingTimestamp)
    {
        _duration = Math.Max(0, Math.Round((renderingTimestamp - StartTime) / DurationGranularity, MidpointRounding.AwayFromZero) * DurationGranularity);
        HasDuration = true;
    }

    /// <summary>Creates the <c>first-input</c> entry: a copy of the entry of the first event that belongs to an interaction.</summary>
    internal PerformanceEventTiming CreateFirstInputEntry() => new(Global, FirstInputEntryType, Name, StartTime, ProcessingStart, Cancelable, InteractionId)
    {
        ProcessingEnd = ProcessingEnd,
        EventTarget = EventTarget,
        TargetSelector = TargetSelector,
        _duration = _duration,
        HasDuration = true,
    };

    /// <summary>
    /// https://w3c.github.io/event-timing/#sec-should-add-performanceeventtiming: the <c>first-input</c> entry is always added;
    /// an <c>event</c> entry is added to the buffer of an observer when its duration is at least the duration threshold of the
    /// observer (at least 16 ms), and to the performance entry buffer (no options) when it is at least 104 ms.
    /// </summary>
    internal bool ShouldAdd(PerformanceObserverInit? options)
    {
        if (EntryType == FirstInputEntryType)
            return true;

        var minDuration = options?.DurationThreshold is { } threshold ? Math.Max(MinimumDurationThreshold, threshold) : DefaultDurationThreshold;
        return _duration >= minDuration;
    }
}

/// <summary>
/// The number of trusted events of each type considered for Event Timing that were dispatched in a window
/// (https://w3c.github.io/event-timing/#sec-event-counts), whatever their duration. An event is counted by the rendering
/// update that follows its dispatch.
/// </summary>
[IdlInterface("EventCounts", "https://w3c.github.io/event-timing/#eventcounts")]
public sealed class EventCounts : IReadOnlyCollection<KeyValuePair<string, ulong>>
{
    /// <summary>The types of the events considered for Event Timing (https://w3c.github.io/event-timing/#sec-events-exposed).</summary>
    private static readonly string[] EventTypes =
    [
        "auxclick", "click", "contextmenu", "dblclick", "mousedown", "mouseenter", "mouseleave", "mouseout", "mouseover", "mouseup",
        "pointerover", "pointerenter", "pointerdown", "pointerup", "pointercancel", "pointerout", "pointerleave", "gotpointercapture", "lostpointercapture",
        "touchstart", "touchend", "touchcancel",
        "keydown", "keypress", "keyup",
        "beforeinput", "input",
        "compositionstart", "compositionupdate", "compositionend",
        "dragstart", "dragend", "dragenter", "dragleave", "dragover", "drop",
    ];

    private static readonly FrozenDictionary<string, int> Indexes = EventTypes.Select(static (type, index) => KeyValuePair.Create(type, index)).ToFrozenDictionary(StringComparer.Ordinal);

    private readonly ulong[] _counts = new ulong[EventTypes.Length];

    internal EventCounts()
    {
    }

    /// <summary>Whether the events of a type are considered for Event Timing.</summary>
    internal static bool IsConsidered(string type) => Indexes.ContainsKey(type);

    /// <summary>Gets the number of event types: all the considered types are in the map, with a count of 0 until an event is dispatched.</summary>
    [NotInIdl("The size member of maplike<DOMString, unsigned long long>")]
    public int Count => EventTypes.Length;

    /// <summary>Returns the number of events of a type that were dispatched, or null when the type is not considered for Event Timing.</summary>
    /// <param name="type">The type of the events, such as <c>click</c>.</param>
    [NotInIdl("The get() member of maplike<DOMString, unsigned long long>")]
    public ulong? Get(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Indexes.TryGetValue(type, out var index) ? _counts[index] : null;
    }

    /// <summary>Returns whether the events of a type are counted.</summary>
    /// <param name="type">The type of the events, such as <c>click</c>.</param>
    [NotInIdl("The has() member of maplike<DOMString, unsigned long long>")]
    public bool Has(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Indexes.ContainsKey(type);
    }

    internal void Increment(string type)
    {
        if (Indexes.TryGetValue(type, out var index))
        {
            _counts[index]++;
        }
    }

    /// <summary>Enumerates the event types and their counts, in the order of the specification.</summary>
    [NotInIdl("The entries of maplike<DOMString, unsigned long long>")]
    public IEnumerator<KeyValuePair<string, ulong>> GetEnumerator()
    {
        for (var i = 0; i < EventTypes.Length; i++)
        {
            yield return KeyValuePair.Create(EventTypes[i], _counts[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The Event Timing state of a window (https://w3c.github.io/event-timing/#sec-modifications-HTML) and its processing model:
/// the dispatch of a trusted event of a considered type creates an entry (<see cref="Initialize"/>, <see cref="Complete"/>),
/// whose duration is set by the next rendering update of the event loop (<see cref="DispatchPendingEntries"/>), which queues
/// it to the performance timeline.
/// </summary>
internal sealed class EventTimingState
{
    private readonly Window _window;

    /// <summary>The entries to be queued by the next rendering update.</summary>
    private readonly List<PerformanceEventTiming> _entriesToBeQueued = [];

    /// <summary>The pending pointer downs: the entry of the last <c>pointerdown</c> of each pointer, until its release or its cancelation tells whether it is an interaction.</summary>
    private Dictionary<int, PerformanceEventTiming>? _pendingPointerDowns;

    /// <summary>The pointer interaction value map: the interaction of the last release of each pointer, used by its <c>click</c>.</summary>
    private Dictionary<int, ulong>? _pointerInteractionValues;

    /// <summary>The interaction of the pressed keys, by key code.</summary>
    private Dictionary<uint, ulong>? _pressedKeyInteractionValues;

    /// <summary>The interaction of the last key press, until a pointer is pressed: the one of the <c>keypress</c> event and of the click the key simulates. 0 when there is none.</summary>
    private ulong _lastKeyInteractionValue;

    /// <summary>The user interaction value: it starts at a random value and increases by a small number, so that it is not a counter of the interactions.</summary>
    private ulong _userInteractionValue = (ulong)System.Security.Cryptography.RandomNumberGenerator.GetInt32(100, 10_001);

    private bool _hasDispatchedInputEvent;
    private bool _isContextMenuTriggered;
    private long _nextSequence;

    public EventTimingState(Window window)
    {
        _window = window;
    }

    /// <summary>The number of events of each considered type whose duration was set.</summary>
    public EventCounts EventCounts { get; } = new();

    /// <summary>The number of interactions of the user: the interaction identifiers that were computed.</summary>
    public ulong InteractionCount { get; private set; }

    /// <summary>
    /// The steps added at the start of the dispatch of an event (https://w3c.github.io/event-timing/#sec-modifications-DOM):
    /// computes the interaction of the event and initializes its entry (https://w3c.github.io/event-timing/#initialize-event-timing).
    /// The caller checked that the event is trusted and that its type is considered (<see cref="EventCounts.IsConsidered"/>).
    /// </summary>
    public PerformanceEventTiming Initialize(Event @event)
    {
        var interactionId = ComputeInteractionId(@event);
        return new PerformanceEventTiming(_window, PerformanceEventTiming.EventEntryType, @event.Type, @event.TimeStamp, _window.GetCurrentHighResolutionTime(), @event.Cancelable, interactionId) { Sequence = _nextSequence++ };
    }

    /// <summary>https://w3c.github.io/event-timing/#increasing-interaction-count</summary>
    private ulong IncreaseInteractionCount()
    {
        _userInteractionValue += 7;
        InteractionCount++;
        return _userInteractionValue;
    }

    /// <summary>
    /// https://w3c.github.io/event-timing/#computing-interactionid for the pointer and composition events. The keyboard events
    /// follow the browsers (and the tests) instead of the pending key downs of the specification: the interaction is computed by
    /// <c>keydown</c>, so that a key that is not released is an interaction; <c>keypress</c> and the <c>keyup</c> of the same
    /// key code belong to it, and so does the click that the key simulates (its pointer id is -1).
    /// </summary>
    private ulong ComputeInteractionId(Event @event)
    {
        switch (@event.Type)
        {
            case "keydown" when @event is KeyboardEvent keyboardEvent:
                if (keyboardEvent.IsComposing)
                    return 0;

                _lastKeyInteractionValue = IncreaseInteractionCount();
                (_pressedKeyInteractionValues ??= [])[keyboardEvent.KeyCode] = _lastKeyInteractionValue;
                return _lastKeyInteractionValue;

            case "keypress" when @event is KeyboardEvent keyboardEvent:
                return keyboardEvent.IsComposing ? 0 : _lastKeyInteractionValue;

            case "keyup" when @event is KeyboardEvent keyboardEvent:
                if (keyboardEvent.IsComposing || _pressedKeyInteractionValues is null || !_pressedKeyInteractionValues.Remove(keyboardEvent.KeyCode, out var keyInteractionValue))
                    return 0;

                return keyInteractionValue;

            case "compositionstart":
                // The pressed keys are not matched with the key releases of the composition
                _pressedKeyInteractionValues?.Clear();
                _lastKeyInteractionValue = 0;
                return 0;

            case "input":
                // An input event of a composition is an interaction (the other ones belong to the interaction of their key)
                return @event is InputEvent { IsComposing: true } ? IncreaseInteractionCount() : 0;

            case "click" when @event is PointerEvent pointerEvent:
                if (pointerEvent.PointerId < 0)
                    return _lastKeyInteractionValue;

                return _pointerInteractionValues is not null && _pointerInteractionValues.Remove(pointerEvent.PointerId, out var clickInteractionValue) ? clickInteractionValue : 0;

            case "pointerup" or "pointercancel" or "contextmenu" when @event is PointerEvent pointerEvent:
                var type = @event.Type;
                var pointerId = pointerEvent.PointerId;
                if (_pendingPointerDowns is null || !_pendingPointerDowns.Remove(pointerId, out var pointerDownEntry))
                {
                    if (type == "contextmenu")
                        return _userInteractionValue;

                    if (type == "pointerup" && _isContextMenuTriggered)
                    {
                        _isContextMenuTriggered = false;
                        return _userInteractionValue;
                    }

                    return 0;
                }

                if (type is "pointerup" or "contextmenu")
                {
                    var pointerInteractionValue = IncreaseInteractionCount();
                    (_pointerInteractionValues ??= [])[pointerId] = pointerInteractionValue;
                    pointerDownEntry.InteractionId = pointerInteractionValue;
                }

                _entriesToBeQueued.Add(pointerDownEntry);
                if (type == "contextmenu")
                {
                    _isContextMenuTriggered = true;
                }

                return type == "pointercancel" ? 0 : pointerDownEntry.InteractionId;

            default:
                return 0;
        }
    }

    /// <summary>
    /// The steps added at the end of the dispatch of an event (https://w3c.github.io/event-timing/#finalize-event-timing): the
    /// entry is completed with the end of the dispatch and with the last target of the event, and waits for the next rendering
    /// update of the event loop.
    /// </summary>
    public void Complete(PerformanceEventTiming timingEntry, Event @event, EventTarget target)
    {
        timingEntry.ProcessingEnd = _window.GetCurrentHighResolutionTime();
        timingEntry.EventTarget = target as Node;
        timingEntry.TargetSelector = GenerateCssSelector(target);
        if (@event.Type == "pointerdown" && @event is PointerEvent pointerEvent)
        {
            // The interaction of a pointerdown is known when the pointer is released or canceled
            _pendingPointerDowns ??= [];
            if (_pendingPointerDowns.TryGetValue(pointerEvent.PointerId, out var previousPointerDownEntry))
            {
                _entriesToBeQueued.Add(previousPointerDownEntry);
            }

            _pendingPointerDowns[pointerEvent.PointerId] = timingEntry;
            _isContextMenuTriggered = false;

            // The next click that has no pointer is not the click of the last key press
            _lastKeyInteractionValue = 0;
        }
        else
        {
            _entriesToBeQueued.Add(timingEntry);
        }

        _window.EventLoop.RequestEventTimingDispatch(_window);
    }

    /// <summary>https://w3c.github.io/event-timing/#generate-a-css-selector</summary>
    private static string GenerateCssSelector(EventTarget target)
    {
        if (target is not Node node)
            return "";

        if (node is Element element)
        {
            if (element.GetAttribute("id") is { } id)
                return node.NodeName + "#" + id;

            if (element.GetAttribute("src") is { } src)
                return node.NodeName + "[src=" + src + "]";
        }

        return node.NodeName;
    }

    /// <summary>
    /// https://w3c.github.io/event-timing/#dispatch-pending-event-timing-entries, run by the rendering update of the event loop
    /// after the paint timing: sets the duration of the entries, counts their events, and queues the ones that last at least
    /// 16 ms and the first input.
    /// </summary>
    public void DispatchPendingEntries()
    {
        var renderingTimestamp = _window.GetCurrentHighResolutionTime();
        var performance = _window.Performance;
        if (_entriesToBeQueued.Count > 0)
        {
            // The entries are queued in the order of their events: the entry of an event is completed after the entries of the
            // events dispatched by its listeners and by its default action (e.g. the click of a key press), and a pointerdown
            // waits for its pointerup
            var entries = _entriesToBeQueued.OrderBy(static entry => entry.Sequence).ToArray();
            _entriesToBeQueued.Clear();
            foreach (var timingEntry in entries)
            {
                SetDuration(timingEntry, renderingTimestamp);

                // The first input is the first entry that belongs to an interaction. The specification only considers the
                // entries whose duration is being set, which skips a pointerdown that waited for its pointerup during a
                // rendering update: as in the browsers, such a pointerdown is the first input.
                if (!_hasDispatchedInputEvent && timingEntry.InteractionId != 0)
                {
                    _hasDispatchedInputEvent = true;
                    performance.QueueEventTimingEntry(timingEntry.CreateFirstInputEntry());
                }

                if (timingEntry.Duration >= PerformanceEventTiming.MinimumDurationThreshold)
                {
                    performance.QueueEventTimingEntry(timingEntry);
                }
            }
        }

        // The duration of a pointerdown does not include the time the pointer stays pressed
        if (_pendingPointerDowns is { Count: > 0 })
        {
            foreach (var pendingPointerDownEntry in _pendingPointerDowns.Values)
            {
                SetDuration(pendingPointerDownEntry, renderingTimestamp);
            }
        }
    }

    /// <summary>
    /// https://w3c.github.io/event-timing/#set-event-timing-entry-duration. The specification sets the duration again while it is
    /// zero, which would count twice an event followed by a rendering update in less than 4 ms: the duration is set once.
    /// </summary>
    private void SetDuration(PerformanceEventTiming timingEntry, double renderingTimestamp)
    {
        if (timingEntry.HasDuration)
            return;

        timingEntry.SetDuration(renderingTimestamp);
        EventCounts.Increment(timingEntry.Name);
    }
}
