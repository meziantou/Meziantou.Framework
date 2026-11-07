using System.Diagnostics;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#dictdef-eventinit</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-eventinit"/>
[IdlInterface("EventInit", "https://dom.spec.whatwg.org/#dictdef-eventinit")]
public class EventInit
{
    /// <summary>Gets a value indicating whether the event bubbles up through the tree.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventinit-bubbles"/>
    [IdlMember("bubbles", "https://dom.spec.whatwg.org/#dom-eventinit-bubbles")]
    public bool Bubbles { get; init; }

    /// <summary>Gets a value indicating whether the event can be canceled with <c>preventDefault()</c>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventinit-cancelable"/>
    [IdlMember("cancelable", "https://dom.spec.whatwg.org/#dom-eventinit-cancelable")]
    public bool Cancelable { get; init; }

    /// <summary>Gets a value indicating whether the event propagates across the shadow DOM boundary.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventinit-composed"/>
    [IdlMember("composed", "https://dom.spec.whatwg.org/#dom-eventinit-composed")]
    public bool Composed { get; init; }
}

/// <summary>https://dom.spec.whatwg.org/#interface-event</summary>
/// <seealso href="https://dom.spec.whatwg.org/#event"/>
[IdlInterface("Event", "https://dom.spec.whatwg.org/#event")]
public partial class Event
{
    /// <summary>The event is not being dispatched.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-none"/>
    [IdlMember("NONE", "https://dom.spec.whatwg.org/#dom-event-none")]
    public const ushort None = 0;

    /// <summary>The event is propagating through the ancestors of the target, from the root.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-capturing_phase"/>
    [IdlMember("CAPTURING_PHASE", "https://dom.spec.whatwg.org/#dom-event-capturing_phase")]
    public const ushort CapturingPhase = 1;

    /// <summary>The event has reached its target.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-at_target"/>
    [IdlMember("AT_TARGET", "https://dom.spec.whatwg.org/#dom-event-at_target")]
    public const ushort AtTarget = 2;

    /// <summary>The event is propagating back up through the ancestors of the target.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-bubbling_phase"/>
    [IdlMember("BUBBLING_PHASE", "https://dom.spec.whatwg.org/#dom-event-bubbling_phase")]
    public const ushort BubblingPhase = 3;

    private static readonly long TimeOrigin = Stopwatch.GetTimestamp();

    /// <summary>https://dom.spec.whatwg.org/#dom-event-event</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-event"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-event-event")]
    public Event(string type, EventInit? eventInitDict = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Inner event creation steps
        Initialized = true;
        InitializeTimeStamp();
        Type = type;
        if (eventInitDict is not null)
        {
            Bubbles = eventInitDict.Bubbles;
            Cancelable = eventInitDict.Cancelable;
            ComposedFlag = eventInitDict.Composed;
        }
    }

    /// <summary>Creates an event that is not initialized (used by <c>document.createEvent()</c>).</summary>
    private protected Event()
    {
        Type = "";
        InitializeTimeStamp();
    }

    /// <summary>
    /// https://dom.spec.whatwg.org/#inner-event-creation-steps: the time stamp is the relative high resolution coarse time of
    /// the creation, given the relevant global object of the event (the current global object, with the clock of
    /// <c>performance.now()</c>). An event created by .NET code outside of a script has no global object: its time stamp is
    /// measured from the start of the process, until it is dispatched to a target that has a global object.
    /// </summary>
    private void InitializeTimeStamp()
    {
        if (GlobalScope.Current is { } global)
        {
            TimeStamp = global.GetCurrentHighResolutionTime();
            HasGlobalTimeStamp = true;
        }
        else
        {
            TimeStamp = Stopwatch.GetElapsedTime(TimeOrigin).TotalMilliseconds;
        }
    }

    /// <summary>Uses the time origin of <paramref name="global"/> for an event created without a global object.</summary>
    internal void SetTimeStampGlobal(GlobalScope global)
    {
        if (HasGlobalTimeStamp)
            return;

        TimeStamp = global.GetCurrentHighResolutionTime();
        HasGlobalTimeStamp = true;
    }

    /// <summary>
    /// Sets the time stamp of an event created by the user agent for a target of <paramref name="global"/> (e.g. an event of
    /// the user input): its relevant global object is the one of its target, whatever the current global object was.
    /// </summary>
    internal void InitializeTimeStamp(GlobalScope global)
    {
        TimeStamp = global.GetCurrentHighResolutionTime();
        HasGlobalTimeStamp = true;
    }

    /// <summary>Whether <see cref="TimeStamp"/> is relative to the time origin of a global object.</summary>
    private bool HasGlobalTimeStamp { get; set; }

    /// <summary>Creates an event whose initialized flag is unset (https://dom.spec.whatwg.org/#dom-document-createevent).</summary>
    internal static Event CreateUninitialized() => new();

    /// <summary>Gets the type of the event, such as <c>click</c>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-type"/>
    [IdlMember("type", "https://dom.spec.whatwg.org/#dom-event-type")]
    public string Type { get; internal set; }

    /// <summary>Gets the object the event was dispatched to.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-target"/>
    [IdlMember("target", "https://dom.spec.whatwg.org/#dom-event-target")]
    public EventTarget? Target => TargetInternal;

    /// <summary>Gets the object the event was dispatched to (legacy alias of <c>target</c>).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-srcelement"/>
    [IdlMember("srcElement", "https://dom.spec.whatwg.org/#dom-event-srcelement")]
    public EventTarget? SrcElement => TargetInternal;

    /// <summary>Gets the object whose listener is currently being invoked.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-currenttarget"/>
    [IdlMember("currentTarget", "https://dom.spec.whatwg.org/#dom-event-currenttarget")]
    public EventTarget? CurrentTarget { get; internal set; }

    /// <summary>
    /// Gets the current phase of the event dispatch (<c>NONE</c>, <c>CAPTURING_PHASE</c>, <c>AT_TARGET</c> or
    /// <c>BUBBLING_PHASE</c>).
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-eventphase"/>
    [IdlMember("eventPhase", "https://dom.spec.whatwg.org/#dom-event-eventphase")]
    public ushort EventPhase { get; internal set; }

    /// <summary>Gets a value indicating whether the event bubbles up through the tree.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-bubbles"/>
    [IdlMember("bubbles", "https://dom.spec.whatwg.org/#dom-event-bubbles")]
    public bool Bubbles { get; private set; }

    /// <summary>Gets a value indicating whether the event can be canceled with <c>preventDefault()</c>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-cancelable"/>
    [IdlMember("cancelable", "https://dom.spec.whatwg.org/#dom-event-cancelable")]
    public bool Cancelable { get; private set; }

    /// <summary>Gets a value indicating whether the event propagates across the shadow DOM boundary.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-composed"/>
    [IdlMember("composed", "https://dom.spec.whatwg.org/#dom-event-composed")]
    public bool Composed => ComposedFlag;

    /// <summary>
    /// Gets a value indicating whether the event was dispatched by the user agent rather than created by a script.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-istrusted"/>
    [IdlMember("isTrusted", "https://dom.spec.whatwg.org/#dom-event-istrusted")]
    public bool IsTrusted { get; internal set; }

    /// <summary>The time the event was created, in milliseconds relative to the time origin.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-timestamp"/>
    [IdlMember("timeStamp", "https://dom.spec.whatwg.org/#dom-event-timestamp")]
    public double TimeStamp { get; private set; }

    /// <summary>Gets a value indicating whether the event has been canceled.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-defaultprevented"/>
    [IdlMember("defaultPrevented", "https://dom.spec.whatwg.org/#dom-event-defaultprevented")]
    public bool DefaultPrevented => CanceledFlag;

    /// <summary>
    /// Gets or sets the legacy stop propagation flag; setting it to true stops the propagation of the event.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-cancelbubble"/>
    [IdlMember("cancelBubble", "https://dom.spec.whatwg.org/#dom-event-cancelbubble")]
    public bool CancelBubble
    {
        get => StopPropagationFlag;
        set
        {
            if (value)
            {
                StopPropagationFlag = true;
            }
        }
    }

    /// <summary>
    /// Gets or sets the legacy inverse of <c>defaultPrevented</c>; setting it to false cancels the event.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-returnvalue"/>
    [IdlMember("returnValue", "https://dom.spec.whatwg.org/#dom-event-returnvalue")]
    public bool ReturnValue
    {
        get => !CanceledFlag;
        set
        {
            if (!value)
            {
                SetCanceledFlag();
            }
        }
    }

    internal EventTarget? TargetInternal { get; set; }

    internal EventTarget? RelatedTarget { get; set; }

    internal IReadOnlyList<EventTarget?> TouchTargetList { get; set; } = [];

    internal List<EventPathItem> Path { get; } = [];

    internal bool StopPropagationFlag { get; set; }

    internal bool StopImmediatePropagationFlag { get; set; }

    internal bool CanceledFlag { get; set; }

    /// <summary>Whether a listener threw an exception during the dispatch (the legacyOutputDidListenersThrowFlag of https://dom.spec.whatwg.org/#concept-event-dispatch).</summary>
    internal bool LegacyOutputDidListenersThrowFlag { get; set; }

    internal bool InPassiveListenerFlag { get; set; }

    internal bool ComposedFlag { get; set; }

    internal bool Initialized { get; set; }

    internal bool DispatchFlag { get; set; }

    /// <summary>Prevents the event from reaching the listeners of the other objects of its path.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-stoppropagation"/>
    [IdlMember("stopPropagation", "https://dom.spec.whatwg.org/#dom-event-stoppropagation")]
    public void StopPropagation() => StopPropagationFlag = true;

    /// <summary>
    /// Prevents the event from reaching the other listeners, including the remaining listeners of the current target.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-stopimmediatepropagation"/>
    [IdlMember("stopImmediatePropagation", "https://dom.spec.whatwg.org/#dom-event-stopimmediatepropagation")]
    public void StopImmediatePropagation()
    {
        StopPropagationFlag = true;
        StopImmediatePropagationFlag = true;
    }

    /// <summary>Cancels the event when it is cancelable.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-preventdefault"/>
    [IdlMember("preventDefault", "https://dom.spec.whatwg.org/#dom-event-preventdefault")]
    public void PreventDefault() => SetCanceledFlag();

    /// <summary>https://dom.spec.whatwg.org/#set-the-canceled-flag</summary>
    private void SetCanceledFlag()
    {
        if (Cancelable && !InPassiveListenerFlag)
        {
            CanceledFlag = true;
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-event-initevent</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-initevent"/>
    [IdlMember("initEvent", "https://dom.spec.whatwg.org/#dom-event-initevent")]
    public void InitEvent(string type, bool bubbles = false, bool cancelable = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (DispatchFlag)
            return;

        Initialize(type, bubbles, cancelable);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-event-initialize</summary>
    private protected void Initialize(string type, bool bubbles, bool cancelable)
    {
        Initialized = true;
        StopPropagationFlag = false;
        StopImmediatePropagationFlag = false;
        CanceledFlag = false;
        IsTrusted = false;
        TargetInternal = null;
        Type = type;
        Bubbles = bubbles;
        Cancelable = cancelable;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-event-composedpath</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-event-composedpath"/>
    [IdlMember("composedPath", "https://dom.spec.whatwg.org/#dom-event-composedpath")]
    public IReadOnlyList<EventTarget> ComposedPath()
    {
        var composedPath = new List<EventTarget>();
        var path = Path;
        if (path.Count == 0)
            return composedPath;

        var currentTarget = CurrentTarget!;
        composedPath.Add(currentTarget);
        var currentTargetIndex = 0;
        var currentTargetHiddenSubtreeLevel = 0;
        var index = path.Count - 1;
        while (index >= 0)
        {
            if (path[index].RootOfClosedTree)
            {
                currentTargetHiddenSubtreeLevel++;
            }

            if (ReferenceEquals(path[index].InvocationTarget, currentTarget))
            {
                currentTargetIndex = index;
                break;
            }

            if (path[index].SlotInClosedTree)
            {
                currentTargetHiddenSubtreeLevel--;
            }

            index--;
        }

        var currentHiddenLevel = currentTargetHiddenSubtreeLevel;
        var maxHiddenLevel = currentTargetHiddenSubtreeLevel;
        index = currentTargetIndex - 1;
        while (index >= 0)
        {
            if (path[index].RootOfClosedTree)
            {
                currentHiddenLevel++;
            }

            if (currentHiddenLevel <= maxHiddenLevel)
            {
                composedPath.Insert(0, path[index].InvocationTarget);
            }

            if (path[index].SlotInClosedTree)
            {
                currentHiddenLevel--;
                if (currentHiddenLevel < maxHiddenLevel)
                {
                    maxHiddenLevel = currentHiddenLevel;
                }
            }

            index--;
        }

        currentHiddenLevel = currentTargetHiddenSubtreeLevel;
        maxHiddenLevel = currentTargetHiddenSubtreeLevel;
        index = currentTargetIndex + 1;
        while (index < path.Count)
        {
            if (path[index].SlotInClosedTree)
            {
                currentHiddenLevel++;
            }

            if (currentHiddenLevel <= maxHiddenLevel)
            {
                composedPath.Add(path[index].InvocationTarget);
            }

            if (path[index].RootOfClosedTree)
            {
                currentHiddenLevel--;
                if (currentHiddenLevel < maxHiddenLevel)
                {
                    maxHiddenLevel = currentHiddenLevel;
                }
            }

            index++;
        }

        return composedPath;
    }
}

/// <summary>An event path item (https://dom.spec.whatwg.org/#concept-event-path).</summary>
internal sealed class EventPathItem
{
    public required EventTarget InvocationTarget { get; init; }

    public bool InvocationTargetInShadowTree { get; init; }

    public EventTarget? ShadowAdjustedTarget { get; init; }

    public EventTarget? RelatedTarget { get; init; }

    public required IReadOnlyList<EventTarget?> TouchTargetList { get; init; }

    public bool RootOfClosedTree { get; init; }

    public bool SlotInClosedTree { get; init; }
}

/// <summary>Represents the initialization dictionary of a <see cref="CustomEvent"/>.</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-customeventinit"/>
[IdlInterface("CustomEventInit", "https://dom.spec.whatwg.org/#dictdef-customeventinit")]
public sealed class CustomEventInit : EventInit
{
    /// <summary>Gets the custom data associated with the event.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-customeventinit-detail"/>
    [IdlMember("detail", "https://dom.spec.whatwg.org/#dom-customeventinit-detail")]
    public object? Detail { get; init; }
}

/// <summary>https://dom.spec.whatwg.org/#interface-customevent</summary>
/// <seealso href="https://dom.spec.whatwg.org/#customevent"/>
[IdlInterface("CustomEvent", "https://dom.spec.whatwg.org/#customevent")]
public sealed class CustomEvent : Event
{
    /// <summary>Creates a custom event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-customevent-customevent"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-customevent-customevent")]
    public CustomEvent(string type, CustomEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Detail = eventInitDict?.Detail;
    }

    private CustomEvent()
    {
    }

    internal static CustomEvent CreateUninitializedCustomEvent() => new();

    /// <summary>Gets the custom data associated with the event.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-customevent-detail"/>
    [IdlMember("detail", "https://dom.spec.whatwg.org/#dom-customevent-detail")]
    public object? Detail { get; private set; }

    /// <summary>https://dom.spec.whatwg.org/#dom-customevent-initcustomevent</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-customevent-initcustomevent"/>
    [IdlMember("initCustomEvent", "https://dom.spec.whatwg.org/#dom-customevent-initcustomevent")]
    public void InitCustomEvent(string type, bool bubbles = false, bool cancelable = false, object? detail = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (DispatchFlag)
            return;

        Initialize(type, bubbles, cancelable);
        Detail = detail;
    }
}
