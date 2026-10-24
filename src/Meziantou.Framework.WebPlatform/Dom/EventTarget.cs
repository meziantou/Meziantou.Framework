using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#callbackdef-eventlistener</summary>
/// <seealso href="https://dom.spec.whatwg.org/#callbackdef-eventlistener"/>
[IdlInterface("EventListener", "https://dom.spec.whatwg.org/#callbackdef-eventlistener")]
public interface IEventListener
{
    /// <summary>Handles the event dispatched to the listener.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventlistener-handleevent"/>
    [IdlMember("handleEvent", "https://dom.spec.whatwg.org/#dom-eventlistener-handleevent")]
    void HandleEvent(Event @event);
}

/// <summary>https://dom.spec.whatwg.org/#dictdef-eventlisteneroptions</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-eventlisteneroptions"/>
[IdlInterface("EventListenerOptions", "https://dom.spec.whatwg.org/#dictdef-eventlisteneroptions")]
public class EventListenerOptions
{
    /// <summary>Gets a value indicating whether the listener is registered for the capture phase.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventlisteneroptions-capture"/>
    [IdlMember("capture", "https://dom.spec.whatwg.org/#dom-eventlisteneroptions-capture")]
    public bool Capture { get; init; }
}

/// <summary>https://dom.spec.whatwg.org/#dictdef-addeventlisteneroptions</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-addeventlisteneroptions"/>
[IdlInterface("AddEventListenerOptions", "https://dom.spec.whatwg.org/#dictdef-addeventlisteneroptions")]
public sealed class AddEventListenerOptions : EventListenerOptions
{
    /// <summary>
    /// Gets a value indicating whether the listener never calls <c>preventDefault()</c>, or null to use the default.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-addeventlisteneroptions-passive"/>
    [IdlMember("passive", "https://dom.spec.whatwg.org/#dom-addeventlisteneroptions-passive")]
    public bool? Passive { get; init; }

    /// <summary>Gets a value indicating whether the listener is removed after it is invoked once.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-addeventlisteneroptions-once"/>
    [IdlMember("once", "https://dom.spec.whatwg.org/#dom-addeventlisteneroptions-once")]
    public bool Once { get; init; }

    /// <summary>Gets the signal that removes the listener when it is aborted.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-addeventlisteneroptions-signal"/>
    [IdlMember("signal", "https://dom.spec.whatwg.org/#dom-addeventlisteneroptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>A callback implemented by a script: the global object of its associated realm.</summary>
internal interface IScriptCallback
{
    /// <summary>The global object of the associated realm of the callback (https://webidl.spec.whatwg.org/#dfn-associated-realm).</summary>
    GlobalScope CallbackGlobal { get; }
}

/// <summary>An event listener (https://dom.spec.whatwg.org/#concept-event-listener).</summary>
internal sealed class EventListenerEntry
{
    public required string Type { get; init; }

    /// <summary>An <see cref="IEventListener"/> or a <see cref="Delegate"/> (<c>Action&lt;Event&gt;</c>).</summary>
    public required object? Callback { get; set; }

    public bool Capture { get; init; }

    public bool? Passive { get; set; }

    public bool Once { get; init; }

    public AbortSignal? Signal { get; init; }

    public bool Removed { get; set; }
}

/// <summary>https://dom.spec.whatwg.org/#interface-eventtarget</summary>
/// <seealso href="https://dom.spec.whatwg.org/#eventtarget"/>
[IdlInterface("EventTarget", "https://dom.spec.whatwg.org/#eventtarget")]
public partial class EventTarget
{
    /// <summary>
    /// The event listener list of an event target that is not a node, or the rare data of a node (see <see cref="Node.RareData"/>),
    /// which holds the event listener list of the node: most nodes have no listeners, and the field is shared to keep them small.
    /// </summary>
    private object? _listenersOrRareData;

    /// <summary>The storage of the rare data of a node (see <see cref="Node.RareData"/>).</summary>
    private protected object? ListenersOrRareData
    {
        get => _listenersOrRareData;
        set => _listenersOrRareData = value;
    }

    // Copy-on-write: dispatching an event uses the array as the clone of the event listener list
    // (https://dom.spec.whatwg.org/#concept-event-listener-invoke) without copying it
    private EventListenerEntry[]? EventListeners
    {
        get => _listenersOrRareData is NodeRareData rareData ? rareData.EventListeners : (EventListenerEntry[]?)_listenersOrRareData;
        set
        {
            if (this is not Node node)
            {
                _listenersOrRareData = value;
            }
            else if (value is not null || node.RareDataOrNull is not null)
            {
                node.RareData.EventListeners = value;
            }
        }
    }

    /// <summary>Creates an event target.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventtarget-eventtarget"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-eventtarget-eventtarget")]
    public EventTarget()
    {
        // The relevant global object of the event targets that are not nodes is the current global object, if any
        if (this is not Node && GlobalScope.Current is { } global)
        {
            RelevantGlobals.AddOrUpdate(this, global);
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EventTarget, GlobalScope> RelevantGlobals = [];

    /// <summary>
    /// The relevant global object (https://html.spec.whatwg.org/multipage/webappapis.html#concept-relevant-global): the window
    /// of the node document for nodes, or the current global object when the target was created (a window or the global
    /// object of a worker), if any.
    /// </summary>
    internal virtual GlobalScope? RelevantGlobal => RelevantGlobals.TryGetValue(this, out var global) ? global : null;

    /// <summary>Sets the relevant global object of an event target created for another global object (e.g. a transferred message port).</summary>
    internal void SetRelevantGlobalCore(GlobalScope global) => RelevantGlobals.AddOrUpdate(this, global);

    /// <summary>The event listener list (https://dom.spec.whatwg.org/#eventtarget-event-listener-list). The array is never modified: adding or removing a listener replaces it.</summary>
    internal EventListenerEntry[] EventListenerList => EventListeners ?? [];

    /// <summary>Registers the listener for events of the given type with the given options.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventtarget-addeventlistener"/>
    [IdlMember("addEventListener", "https://dom.spec.whatwg.org/#dom-eventtarget-addeventlistener")]
    public void AddEventListener(string type, IEventListener? callback, AddEventListenerOptions? options = null) => AddEventListenerCore(type, callback, options);

    /// <summary>
    /// Registers the listener for events of the given type, in the capture phase when <c>useCapture</c> is true.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventtarget-addeventlistener"/>
    [IdlMember("addEventListener", "https://dom.spec.whatwg.org/#dom-eventtarget-addeventlistener")]
    public void AddEventListener(string type, IEventListener? callback, bool useCapture) => AddEventListenerCore(type, callback, new AddEventListenerOptions { Capture = useCapture });

    /// <summary>Registers the delegate as a listener for events of the given type with the given options.</summary>
    [NotInIdl("Convenience overload for delegates; the delegate is the identity of the listener")]
    public void AddEventListener(string type, Action<Event>? callback, AddEventListenerOptions? options = null) => AddEventListenerCore(type, callback, options);

    /// <summary>
    /// Registers the delegate as a listener for events of the given type, in the capture phase when <c>useCapture</c>
    /// is true.
    /// </summary>
    [NotInIdl("Convenience overload for delegates; the delegate is the identity of the listener")]
    public void AddEventListener(string type, Action<Event>? callback, bool useCapture) => AddEventListenerCore(type, callback, new AddEventListenerOptions { Capture = useCapture });

    /// <summary>https://dom.spec.whatwg.org/#dom-eventtarget-addeventlistener</summary>
    internal void AddEventListenerCore(string type, object? callback, AddEventListenerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Flatten more options
        AddEventListener(new EventListenerEntry
        {
            Type = type,
            Callback = callback,
            Capture = options?.Capture ?? false,
            Passive = options?.Passive,
            Once = options?.Once ?? false,
            Signal = options?.Signal,
        });
    }

    /// <summary>https://dom.spec.whatwg.org/#add-an-event-listener</summary>
    internal void AddEventListener(EventListenerEntry listener)
    {
        if (listener.Signal is { Aborted: true })
            return;

        if (listener.Callback is null)
            return;

        listener.Passive ??= GetDefaultPassiveValue(listener.Type);

        var list = EventListenerList;
        foreach (var existing in list)
        {
            if (existing.Type == listener.Type && Equals(existing.Callback, listener.Callback) && existing.Capture == listener.Capture)
                return;
        }

        EventListeners = [.. list, listener];
        listener.Signal?.AddAbortAlgorithm(() => RemoveEventListener(listener));
        if (listener.Type is "scroll" or "scrollend" or "scrollsnapchange" or "scrollsnapchanging")
        {
            Css.Layout.LayoutEngine.OnScrollEventListenerAdded(this);
        }

        OnEventListenerAdded(listener);
    }

    /// <summary>Called when an event listener is added to the event listener list (e.g. to keep the target alive while it has listeners).</summary>
    private protected virtual void OnEventListenerAdded(EventListenerEntry listener)
    {
    }

    /// <summary>Called when an event listener is removed from the event listener list.</summary>
    private protected virtual void OnEventListenerRemoved(EventListenerEntry listener)
    {
    }

    /// <summary>Gets whether the event listener list contains a listener for the events of type <paramref name="type"/>.</summary>
    internal bool HasEventListener(string type)
    {
        foreach (var listener in EventListenerList)
        {
            if (listener.Type == type)
                return true;
        }

        return false;
    }

    /// <summary>Called when an event handler IDL attribute is set (e.g. onmessage enables the port message queue of a MessagePort).</summary>
    private protected virtual void OnEventHandlerSet(string type)
    {
    }

    // The event handler maps of the event targets that are not nodes (nodes use their rare data)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EventTarget, Dictionary<string, EventHandlerAttribute>> EventHandlerMaps = [];

    /// <summary>Returns the event handler map (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-map).</summary>
    internal virtual Dictionary<string, EventHandlerAttribute>? GetEventHandlerMap(bool create)
    {
        if (create)
            return EventHandlerMaps.GetValue(this, _ => new Dictionary<string, EventHandlerAttribute>(StringComparer.Ordinal));

        return EventHandlerMaps.TryGetValue(this, out var map) ? map : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#determining-the-target-of-an-event-handler</summary>
    internal virtual EventTarget? GetEventHandlerTarget(string type) => this;

    /// <summary>The getter of an event handler IDL attribute (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-idl-attributes).</summary>
    internal Action<Event>? GetEventHandler(string type) => Scripting.EventHandlerCallback.ToDelegate(GetEventHandlerCallback(type));

    /// <summary>The setter of an event handler IDL attribute (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-idl-attributes).</summary>
    internal void SetEventHandler(string type, Action<Event>? value) => SetEventHandlerCallback(type, Scripting.EventHandlerCallback.FromDelegate(value));

    /// <summary>The getter of an event handler IDL attribute, used by the script bindings (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-idl-attributes).</summary>
    internal Scripting.EventHandlerCallback? GetEventHandlerCallback(string type)
    {
        var target = GetEventHandlerTarget(type);
        if (target?.GetEventHandlerMap(create: false) is { } map && map.TryGetValue(type, out var handler))
            return handler.GetCurrentValue();

        return null;
    }

    /// <summary>The setter of an event handler IDL attribute, used by the script bindings (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-idl-attributes).</summary>
    internal void SetEventHandlerCallback(string type, Scripting.EventHandlerCallback? value)
    {
        OnEventHandlerSet(type);
        var target = GetEventHandlerTarget(type);
        if (target is null)
            return;

        var map = target.GetEventHandlerMap(create: value is not null);
        if (map is null)
            return;

        if (!map.TryGetValue(type, out var handler))
        {
            if (value is null)
                return;

            handler = new EventHandlerAttribute(target, type);
            map[type] = handler;
        }

        handler.SetValue(value);
    }

    /// <summary>Gets whether this object is the global object of a browsing context (a Window), which only exists when a host emulates one.</summary>
    internal virtual bool IsWindow => false;

    /// <summary>https://dom.spec.whatwg.org/#default-passive-value</summary>
    private bool GetDefaultPassiveValue(string type)
    {
        if (type is not ("touchstart" or "touchmove" or "wheel" or "mousewheel"))
            return false;

        if (IsWindow)
            return true;

        if (this is not Node node)
            return false;

        var document = node.NodeDocument;
        return ReferenceEquals(node, document) || ReferenceEquals(node, document.DocumentElement) || ReferenceEquals(node, document.Body);
    }

    /// <summary>Removes the listener registered for events of the given type and capture setting.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventtarget-removeeventlistener"/>
    [IdlMember("removeEventListener", "https://dom.spec.whatwg.org/#dom-eventtarget-removeeventlistener")]
    public void RemoveEventListener(string type, IEventListener? callback, EventListenerOptions? options = null) => RemoveEventListenerCore(type, callback, options?.Capture ?? false);

    /// <summary>Removes the listener registered for events of the given type and capture setting.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventtarget-removeeventlistener"/>
    [IdlMember("removeEventListener", "https://dom.spec.whatwg.org/#dom-eventtarget-removeeventlistener")]
    public void RemoveEventListener(string type, IEventListener? callback, bool useCapture) => RemoveEventListenerCore(type, callback, useCapture);

    /// <summary>
    /// Removes the delegate registered as a listener for events of the given type and capture setting.
    /// </summary>
    [NotInIdl("Convenience overload for delegates")]
    public void RemoveEventListener(string type, Action<Event>? callback, EventListenerOptions? options = null) => RemoveEventListenerCore(type, callback, options?.Capture ?? false);

    /// <summary>
    /// Removes the delegate registered as a listener for events of the given type and capture setting.
    /// </summary>
    [NotInIdl("Convenience overload for delegates")]
    public void RemoveEventListener(string type, Action<Event>? callback, bool useCapture) => RemoveEventListenerCore(type, callback, useCapture);

    internal void RemoveEventListenerCore(string type, object? callback, bool capture)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (EventListeners is null)
            return;

        foreach (var listener in EventListeners)
        {
            if (listener.Type == type && Equals(listener.Callback, callback) && listener.Capture == capture)
            {
                RemoveEventListener(listener);
                return;
            }
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#remove-an-event-listener</summary>
    internal void RemoveEventListener(EventListenerEntry listener)
    {
        listener.Removed = true;
        var list = EventListenerList;
        var index = Array.IndexOf(list, listener);
        if (index >= 0)
        {
            EventListeners = list.Length == 1 ? null : [.. list.AsSpan(0, index), .. list.AsSpan(index + 1)];
            OnEventListenerRemoved(listener);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#erase-all-event-listeners-and-handlers</summary>
    internal void EraseAllEventListenersAndHandlers()
    {
        if (GetEventHandlerMap(create: false) is { } map)
        {
            foreach (var handler in map.Values)
            {
                handler.SetValue(null);
            }
        }

        // https://dom.spec.whatwg.org/#remove-all-event-listeners
        if (EventListeners is { Length: > 0 } removedListeners)
        {
            EventListeners = null;
            foreach (var listener in removedListeners)
            {
                listener.Removed = true;
                OnEventListenerRemoved(listener);
            }
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-eventtarget-dispatchevent</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-eventtarget-dispatchevent"/>
    [IdlMember("dispatchEvent", "https://dom.spec.whatwg.org/#dom-eventtarget-dispatchevent")]
    public bool DispatchEvent(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.DispatchFlag || !@event.Initialized)
            throw DomException.InvalidState("The event is already being dispatched or is not initialized");

        @event.IsTrusted = false;
        return EventDispatcher.Dispatch(@event, this);
    }

    /// <summary>Fires an event created by the user agent (https://dom.spec.whatwg.org/#concept-event-fire): the event is trusted.</summary>
    internal bool FireEvent(Event @event)
    {
        @event.IsTrusted = true;
        return EventDispatcher.Dispatch(@event, this);
    }

    /// <summary>
    /// The get the parent algorithm (https://dom.spec.whatwg.org/#get-the-parent). The default returns null.
    /// </summary>
    internal virtual EventTarget? GetTheParent(Event @event) => null;

    /// <summary>Whether this target has activation behavior (https://dom.spec.whatwg.org/#eventtarget-activation-behavior).</summary>
    internal virtual bool HasActivationBehavior => false;

    internal virtual void ActivationBehavior(Event @event)
    {
    }

    internal virtual bool HasLegacyPreActivationBehavior => false;

    internal virtual void LegacyPreActivationBehavior()
    {
    }

    internal virtual void LegacyCanceledActivationBehavior()
    {
    }
}

/// <summary>The dispatch algorithm (https://dom.spec.whatwg.org/#concept-event-dispatch).</summary>
internal static class EventDispatcher
{
    /// <summary>The number of exceptions reported on the thread (<see cref="Scripting.ScriptErrors.Report(Exception, GlobalScope?)"/>), to know whether a listener threw an exception.</summary>
    internal static int ReportedExceptionCount
    {
        get => s_reportedExceptionCount;
        set => s_reportedExceptionCount = value;
    }

    [ThreadStatic]
    private static int s_reportedExceptionCount;

    /// <summary>https://dom.spec.whatwg.org/#concept-retarget (a null <paramref name="b"/> retargets <paramref name="a"/> out of all the shadow trees).</summary>
    public static EventTarget? Retarget(EventTarget? a, EventTarget? b)
    {
        while (true)
        {
            if (a is not Node node || node.GetRoot() is not ShadowRoot root)
                return a;

            if (b is Node nodeB && root.IsShadowIncludingInclusiveAncestorOf(nodeB))
                return a;

            a = root.Host;
        }
    }

    public static bool Dispatch(Event @event, EventTarget target, bool legacyTargetOverride = false)
    {
        // https://html.spec.whatwg.org/multipage/interaction.html#user-activation-processing-model: the activation
        // notification runs before an activation triggering input event is dispatched
        if (target is Node userActivationTarget && Window.IsActivationTriggeringInputEvent(@event))
        {
            Window.NotifyActivation(userActivationTarget.NodeDocument);
        }

        // https://svgwg.org/specs/animations/#TimingAttributes: the event-base conditions of the SMIL animations listen to the
        // elements they refer to, which are resolved again after the changes of the document
        if (target is Node { NodeDocument.SmilOrNull: { } smil })
        {
            smil.ResolveReferencesBeforeEvent();
        }

        if (@event.IsTrusted && @event is MouseEvent or KeyboardEvent && target is Node userInputTarget)
        {
            UserAgentDefaultActions.BeforeDispatch(@event, userInputTarget);
        }

        PerformanceEventTiming? timingEntry = null;
        if (target.RelevantGlobal is { } targetGlobal)
        {
            @event.SetTimeStampGlobal(targetGlobal);

            // https://w3c.github.io/largest-contentful-paint/#sec-modifications-DOM: a trusted scroll or input event stops the
            // reporting of the largest contentful paint
            (targetGlobal as Window)?.NotifyEventDispatched(@event);

            // https://w3c.github.io/event-timing/#sec-modifications-DOM: the interaction of the event is computed and its
            // event timing entry is initialized
            if (@event.IsTrusted && targetGlobal is Window timingWindow)
            {
                timingEntry = timingWindow.InitializeEventTiming(@event);
            }
        }

        @event.DispatchFlag = true;
        // The legacy target override flag is only used for events fired at a Window: the target is its associated Document
        var targetOverride = legacyTargetOverride && target is Window window ? window.Document : target;

        EventTarget? activationTarget = null;
        ScrollControlReference? activatedScrollControl = null;
        var originalTarget = target;
        var relatedTarget = Retarget(@event.RelatedTarget, target);
        var clearTargets = false;

        // Not in the DOM standard, but interoperable (https://github.com/whatwg/html/issues/5886): the path of the trusted
        // mousedown, mouseup, click and dblclick events stops at a disabled form control, so they are not dispatched to a
        // disabled form control and do not propagate from its descendants to it and its ancestors
        var stopsAtDisabledFormControls = @event.IsTrusted && @event is MouseEvent && @event.Type is "mousedown" or "mouseup" or "click" or "dblclick";
        if ((!ReferenceEquals(target, relatedTarget) || ReferenceEquals(target, @event.RelatedTarget)) && !(stopsAtDisabledFormControls && IsDisabledFormControl(target)))
        {
            var touchTargets = RetargetTouchTargets(@event, target);
            AppendToEventPath(@event, target, targetOverride, relatedTarget, touchTargets, slotInClosedTree: false);
            var isActivationEvent = @event.Type == "click" && @event is MouseEvent;
            if (isActivationEvent && target.HasActivationBehavior)
            {
                activationTarget = target;
            }

            var slottable = target is Node targetNode && Slots.IsSlottable(targetNode) && targetNode.Slottable?.AssignedSlot is not null ? target : null;
            var slotInClosedTree = false;
            var parent = target.GetTheParent(@event);
            while (parent is not null)
            {
                if (stopsAtDisabledFormControls && IsDisabledFormControl(parent))
                    break;

                if (slottable is not null)
                {
                    slottable = null;
                    if (((Node)parent).GetRoot() is ShadowRoot { Mode: ShadowRootMode.Closed })
                    {
                        slotInClosedTree = true;
                    }
                }

                if (parent is Node parentNode && Slots.IsSlottable(parentNode) && parentNode.Slottable?.AssignedSlot is not null)
                {
                    slottable = parent;
                }

                relatedTarget = Retarget(@event.RelatedTarget, parent);
                touchTargets = RetargetTouchTargets(@event, parent);

                // Without shadow roots in the document, the parents of a node are in its tree
                if (parent is Node node && target is Node currentTargetNode && (!HasAnyShadowRoots(node.NodeDocument) || currentTargetNode.GetRoot().IsShadowIncludingInclusiveAncestorOf(node)))
                {
                    if (isActivationEvent && @event.Bubbles && activationTarget is null && parent.HasActivationBehavior)
                    {
                        activationTarget = parent;
                    }

                    AppendToEventPath(@event, parent, null, relatedTarget, touchTargets, slotInClosedTree);
                }
                else if (parent is not Node)
                {
                    // parent is a Window-like global
                    if (isActivationEvent && @event.Bubbles && activationTarget is null && parent.HasActivationBehavior)
                    {
                        activationTarget = parent;
                    }

                    AppendToEventPath(@event, parent, null, relatedTarget, touchTargets, slotInClosedTree);
                }
                else if (ReferenceEquals(parent, relatedTarget))
                {
                    parent = null;
                }
                else
                {
                    target = parent;
                    if (isActivationEvent && activationTarget is null && target.HasActivationBehavior)
                    {
                        activationTarget = target;
                    }

                    AppendToEventPath(@event, parent, target, relatedTarget, touchTargets, slotInClosedTree);
                }

                if (parent is not null)
                {
                    parent = parent.GetTheParent(@event);
                }

                slotInClosedTree = false;
            }

            EventPathItem? clearTargetsItem = null;
            for (var i = @event.Path.Count - 1; i >= 0; i--)
            {
                if (@event.Path[i].ShadowAdjustedTarget is not null)
                {
                    clearTargetsItem = @event.Path[i];
                    break;
                }
            }

            if (clearTargetsItem is not null &&
                (IsNodeInShadowTree(clearTargetsItem.ShadowAdjustedTarget) || IsNodeInShadowTree(clearTargetsItem.RelatedTarget) || clearTargetsItem.TouchTargetList.Any(IsNodeInShadowTree)))
            {
                clearTargets = true;
            }

            // https://drafts.csswg.org/css-overflow-5/#scroll-marker-activation: a scroll navigation control at the point of a
            // trusted click is the innermost activation target (the pseudo-elements are not in the event path)
            if (isActivationEvent && target is Node clickTarget)
            {
                activatedScrollControl = ScrollControlActions.GetActivationTarget((MouseEvent)@event, clickTarget);
                if (activatedScrollControl is not null)
                {
                    activationTarget = null;
                }
            }

            if (activationTarget is not null && activationTarget.HasLegacyPreActivationBehavior)
            {
                activationTarget.LegacyPreActivationBehavior();
            }

            for (var i = @event.Path.Count - 1; i >= 0; i--)
            {
                var item = @event.Path[i];
                @event.EventPhase = item.ShadowAdjustedTarget is not null ? Event.AtTarget : Event.CapturingPhase;
                Invoke(item, i, @event, capturing: true);
            }

            for (var i = 0; i < @event.Path.Count; i++)
            {
                var item = @event.Path[i];
                if (item.ShadowAdjustedTarget is not null)
                {
                    @event.EventPhase = Event.AtTarget;
                }
                else
                {
                    if (!@event.Bubbles)
                        continue;

                    @event.EventPhase = Event.BubblingPhase;
                }

                Invoke(item, i, @event, capturing: false);
            }
        }

        @event.EventPhase = Event.None;
        @event.CurrentTarget = null;
        @event.Path.Clear();
        @event.DispatchFlag = false;
        @event.StopPropagationFlag = false;
        @event.StopImmediatePropagationFlag = false;
        if (clearTargets)
        {
            @event.TargetInternal = null;
            @event.RelatedTarget = null;
            @event.TouchTargetList = [];
        }

        if (activationTarget is not null)
        {
            if (!@event.CanceledFlag)
            {
                // Not in the DOM standard, but as in the browsers: the activation behavior sees the target retargeted against
                // the activation target, not the target of the last invoked listener (retargeted out of the shadow trees) or
                // null when the targets were cleared (e.g. a click in a popover nested in its popovertarget button, in a
                // shadow tree, must not toggle the popover)
                var targetAfterDispatch = @event.TargetInternal;
                @event.TargetInternal = Retarget(originalTarget, activationTarget);
                try
                {
                    activationTarget.ActivationBehavior(@event);
                }
                finally
                {
                    @event.TargetInternal = targetAfterDispatch;
                }
            }
            else
            {
                activationTarget.LegacyCanceledActivationBehavior();
            }
        }

        if (activatedScrollControl is not null && !@event.CanceledFlag)
        {
            ScrollControlActions.Activate(activatedScrollControl, byKeyboard: false);
        }

        // The default actions of the user agent for the trusted events of the user interactions (focus, sequential navigation),
        // for the target of the event in its own tree (not retargeted out of its shadow trees: e.g. a press on a button in a
        // shadow tree focuses the button)
        if (@event.IsTrusted && @event is MouseEvent or KeyboardEvent && originalTarget is Node defaultActionTarget)
        {
            UserAgentDefaultActions.Run(@event, defaultActionTarget, @event.CanceledFlag);
        }

        // https://w3c.github.io/event-timing/#finalize-event-timing, with the last target of the event (the entry belongs to
        // the window that computed its interaction, which is the relevant global object of the target unless a listener
        // moved the target to another document)
        if (timingEntry is { Global: Window timingEntryWindow } && target.RelevantGlobal is Window)
        {
            timingEntryWindow.EventTiming.Complete(timingEntry, @event, target);
        }

        target.RelevantGlobal?.EventLoop.OnOperationCompleted();
        return !@event.CanceledFlag;
    }

    private static bool IsNodeInShadowTree(EventTarget? target) => target is Node node && HasAnyShadowRoots(node.NodeDocument) && node.GetRoot() is ShadowRoot;

    private static bool HasAnyShadowRoots(Document document) => document.HasShadowRoots || document.HasUserAgentShadowRoots;

    /// <summary>The touch target list of the event, each touch target retargeted against the target (empty for most events).</summary>
    private static EventTarget?[] RetargetTouchTargets(Event @event, EventTarget target)
    {
        var touchTargetList = @event.TouchTargetList;
        if (touchTargetList.Count == 0)
            return [];

        var result = new EventTarget?[touchTargetList.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Retarget(touchTargetList[i], target);
        }

        return result;
    }

    /// <summary>Whether the target is a disabled form control: a button, input, select, textarea or form-associated custom element that is disabled (not a fieldset).</summary>
    private static bool IsDisabledFormControl(EventTarget target) =>
        target is HtmlElement element &&
        (element.TagId is TagId.Button or TagId.Input or TagId.Select or TagId.Textarea || element.IsCustomizedFormAssociatedCustomElement) &&
        Css.Selectors.HtmlElementStates.IsActuallyDisabled(element);

    /// <summary>https://dom.spec.whatwg.org/#concept-event-path-append</summary>
    private static void AppendToEventPath(Event @event, EventTarget invocationTarget, EventTarget? shadowAdjustedTarget, EventTarget? relatedTarget, IReadOnlyList<EventTarget?> touchTargets, bool slotInClosedTree)
    {
        @event.Path.Add(new EventPathItem
        {
            InvocationTarget = invocationTarget,
            InvocationTargetInShadowTree = IsNodeInShadowTree(invocationTarget),
            ShadowAdjustedTarget = shadowAdjustedTarget,
            RelatedTarget = relatedTarget,
            TouchTargetList = touchTargets,
            RootOfClosedTree = invocationTarget is ShadowRoot { Mode: ShadowRootMode.Closed },
            SlotInClosedTree = slotInClosedTree,
        });
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-event-listener-invoke</summary>
    private static void Invoke(EventPathItem pathItem, int pathIndex, Event @event, bool capturing)
    {
        var targetIndex = pathIndex;
        while (@event.Path[targetIndex].ShadowAdjustedTarget is null)
        {
            targetIndex--;
        }

        @event.TargetInternal = @event.Path[targetIndex].ShadowAdjustedTarget;
        @event.RelatedTarget = pathItem.RelatedTarget;
        @event.TouchTargetList = pathItem.TouchTargetList;
        if (@event.StopPropagationFlag)
            return;

        // Not in the DOM standard, but interoperable (Blink, Gecko and WebKit): the submit and reset events of a nested form
        // do not reach the listeners of the ancestor forms, and their propagation stops there
        if (!capturing && pathItem.InvocationTarget is HtmlFormElement && @event.Type is "submit" or "reset" && !ReferenceEquals(@event.TargetInternal, pathItem.InvocationTarget))
        {
            @event.StopPropagationFlag = true;
            return;
        }

        @event.CurrentTarget = pathItem.InvocationTarget;
        var listeners = pathItem.InvocationTarget.EventListenerList;
        var found = InnerInvoke(@event, listeners, capturing, pathItem.InvocationTargetInShadowTree);
        if (!found && @event.IsTrusted)
        {
            var originalEventType = @event.Type;
            var legacyType = @event.Type switch
            {
                "animationend" => "webkitAnimationEnd",
                "animationiteration" => "webkitAnimationIteration",
                "animationstart" => "webkitAnimationStart",
                "transitionend" => "webkitTransitionEnd",

                // Not in the DOM Standard: the legacy mousewheel event of Blink and WebKit is the wheel event, dispatched with this
                // type to the listeners of the targets that have no wheel listener (Gecko does not fire it)
                "wheel" => "mousewheel",
                _ => null,
            };

            if (legacyType is null)
                return;

            @event.Type = legacyType;
            InnerInvoke(@event, listeners, capturing, pathItem.InvocationTargetInShadowTree);
            @event.Type = originalEventType;
        }
    }

    /// <summary>
    /// The global object of the listener's callback's associated realm (https://dom.spec.whatwg.org/#concept-event-listener-inner-invoke):
    /// the realm of the script callback, or of the function of an event handler.
    /// </summary>
    private static GlobalScope? GetListenerGlobal(object? callback, Event @event) => callback switch
    {
        IScriptCallback scriptCallback => scriptCallback.CallbackGlobal,
        Action<Event> { Target: EventHandlerAttribute eventHandler } when eventHandler.GetCurrentValue() is IScriptCallback scriptCallback => scriptCallback.CallbackGlobal,
        _ => @event.CurrentTarget?.RelevantGlobal,
    };

    /// <summary>https://dom.spec.whatwg.org/#concept-event-listener-inner-invoke</summary>
    private static bool InnerInvoke(Event @event, EventListenerEntry[] listeners, bool capturing, bool invocationTargetInShadowTree)
    {
        var found = false;
        foreach (var listener in listeners)
        {
            if (listener.Removed)
                continue;

            if (@event.Type != listener.Type)
                continue;

            found = true;
            if (capturing && !listener.Capture)
                continue;

            // The listeners of a target whose global object is a window of a document that is not fully active are not invoked
            if (@event.CurrentTarget?.RelevantGlobal is Window { } targetGlobal && !targetGlobal.Document.IsFullyActive)
                continue;

            if (!capturing && listener.Capture)
                continue;

            if (listener.Once)
            {
                @event.CurrentTarget!.RemoveEventListener(listener);
            }

            if (listener.Passive == true)
            {
                @event.InPassiveListenerFlag = true;
            }

            // The current event (window.event) of the global object of the listener's callback. The global of a .NET
            // listener is the relevant global object of the current target.
            // window.event: the workers have no current event
            var listenerGlobal = GetListenerGlobal(listener.Callback, @event) as Window;
            var currentEvent = listenerGlobal?.CurrentEvent;
            if (listenerGlobal is not null && !invocationTargetInShadowTree)
            {
                listenerGlobal.CurrentEvent = @event;
            }

            var reportedExceptions = ReportedExceptionCount;
            try
            {
                switch (listener.Callback)
                {
                    case IEventListener eventListener:
                        eventListener.HandleEvent(@event);
                        break;
                    case Action<Event> action:
                        action(@event);
                        break;
                }

                // The exceptions of the script callbacks are reported by the callbacks
                if (ReportedExceptionCount != reportedExceptions)
                {
                    @event.LegacyOutputDidListenersThrowFlag = true;
                }
            }
#pragma warning disable CA1031 // Exceptions thrown by listeners are reported and do not stop the dispatch
            catch (Exception ex)
#pragma warning restore CA1031
            {
                @event.LegacyOutputDidListenersThrowFlag = true;
                Scripting.ScriptErrors.Report(ex, @event.CurrentTarget?.RelevantGlobal);
            }

            if (listenerGlobal is not null)
            {
                listenerGlobal.CurrentEvent = currentEvent;
            }

            @event.InPassiveListenerFlag = false;
            if (@event.StopImmediatePropagationFlag)
                break;
        }

        return found;
    }
}
