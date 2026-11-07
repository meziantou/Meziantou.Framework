using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An event handler (https://html.spec.whatwg.org/multipage/webappapis.html#event-handlers). Its value is null, a callback,
/// or an internal raw uncompiled handler (the value of an event handler content attribute). The first time the value is
/// set to non-null, an event listener is added; it runs the event handler processing algorithm.
/// </summary>
internal sealed class EventHandlerAttribute(EventTarget target, string type)
{
    private EventHandlerCallback? _value;
    private string? _uncompiledBody;
    private Element? _uncompiledElement;
    private EventListenerEntry? _listener;

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler</summary>
    public EventHandlerCallback? GetCurrentValue()
    {
        if (_uncompiledBody is not null)
        {
            var body = _uncompiledBody;
            var element = _uncompiledElement!;
            _uncompiledBody = null;
            _uncompiledElement = null;
            _value = Compile(element, body);
        }

        return _value;
    }

    /// <summary>
    /// Compiles an internal raw uncompiled handler (https://html.spec.whatwg.org/multipage/webappapis.html#getting-the-current-value-of-the-event-handler, step 1.1).
    /// The value is null when scripting is disabled, or when the body cannot be compiled (the error is reported).
    /// </summary>
    private EventHandlerCallback? Compile(Element element, string body)
    {
        // The element is null when the event target is a Window (the Window-reflecting body element event handlers)
        var scopeElement = target is Window ? null : element;
        var document = element.NodeDocument;
        if (document.RelevantWindow is not { } window || document.BrowsingContext?.Options.ScriptEngine is not { } engine || !document.ScriptingEnabled)
            return null;

        var form = scopeElement is HtmlElement htmlElement ? htmlElement.FormOwner : null;
        var parameters = type == "error" && target is Window ? EventHandlerProcessing.ErrorHandlerParameters : EventHandlerProcessing.EventParameters;
        var source = new EventHandlerSource(window, target, scopeElement, scopeElement is null ? null : document, form, "on" + type, body, document.Url, parameters);
        try
        {
            return engine.CompileEventHandler(source);
        }
#pragma warning disable CA1031 // A syntax error is reported to the global object of the element, and the event handler is null
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ScriptErrors.Report(ex, window);
            return null;
        }
    }

    /// <summary>Sets the value to a callback or null (the setter of the event handler IDL attribute).</summary>
    public void SetValue(EventHandlerCallback? value)
    {
        _uncompiledBody = null;
        _uncompiledElement = null;
        _value = value;
        if (value is null)
        {
            Deactivate();
        }
        else
        {
            Activate();
        }
    }

    /// <summary>Sets the value to an internal raw uncompiled handler (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-attributes:event-handler-content-attributes-3).</summary>
    public void SetUncompiled(Element element, string body)
    {
        _value = null;
        _uncompiledBody = body;
        _uncompiledElement = element;
        Activate();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#activate-an-event-handler</summary>
    private void Activate()
    {
        if (_listener is not null)
            return;

        _listener = new EventListenerEntry { Type = GetEventType(type, target), Callback = new Action<Event>(Invoke) };
        target.AddEventListener(_listener);
    }

    /// <summary>
    /// The event handler event type of the event handler named "on" + <paramref name="name"/>: the name, except for the legacy
    /// prefixed animation and transition event handlers (https://html.spec.whatwg.org/multipage/webappapis.html#globaleventhandlers).
    /// </summary>
    private static string GetEventType(string name, EventTarget target) => name switch
    {
        // https://svgwg.org/specs/animations/#InterfaceSVGAnimationElement: onbegin, onend and onrepeat handle the timing events
        "begin" or "end" or "repeat" when target is SvgAnimationElement => name + "Event",
        "webkitanimationend" => "webkitAnimationEnd",
        "webkitanimationiteration" => "webkitAnimationIteration",
        "webkitanimationstart" => "webkitAnimationStart",
        "webkittransitionend" => "webkitTransitionEnd",
        _ => name,
    };

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#deactivate-an-event-handler</summary>
    private void Deactivate()
    {
        if (_listener is null)
            return;

        target.RemoveEventListener(_listener);
        _listener = null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#the-event-handler-processing-algorithm</summary>
    private void Invoke(Event @event)
    {
        if (GetCurrentValue() is { } callback)
        {
            EventHandlerProcessing.Invoke(callback, @event);
        }
    }
}

/// <summary>An event handler whose value was set from .NET: its return value is not processed.</summary>
internal sealed class DelegateEventHandlerCallback(Action<Event> handler) : EventHandlerCallback
{
    public Action<Event> Handler { get; } = handler;

    protected internal override object? Invoke(EventTarget thisValue, object?[] arguments)
    {
        Handler((Event)arguments[0]!);
        return null;
    }
}

/// <summary>The event handler processing algorithm (https://html.spec.whatwg.org/multipage/webappapis.html#the-event-handler-processing-algorithm).</summary>
internal static class EventHandlerProcessing
{
    public static readonly IReadOnlyList<string> EventParameters = ["event"];

    public static readonly IReadOnlyList<string> ErrorHandlerParameters = ["event", "source", "lineno", "colno", "error"];

    public static void Invoke(EventHandlerCallback callback, Event @event)
    {
        if (callback is DelegateEventHandlerCallback { Handler: var handler })
        {
            handler(@event);
            return;
        }

        var currentTarget = @event.CurrentTarget!;

        // Special error event handling: the onerror handler of a global object (a window or a worker) gets the attributes
        // of the ErrorEvent as arguments
        var specialErrorEventHandling = @event is ErrorEvent && @event.Type == "error" && currentTarget is GlobalScope;
        object?[] arguments = specialErrorEventHandling && @event is ErrorEvent errorEvent
            ? [errorEvent.Message, errorEvent.Filename, errorEvent.Lineno, errorEvent.Colno, errorEvent.Error]
            : [@event];

        var returnValue = callback.Invoke(currentTarget, arguments);

        // Process the return value
        if (specialErrorEventHandling)
        {
            if (returnValue is true)
            {
                @event.PreventDefault();
            }
        }
        else if (@event is BeforeUnloadEvent beforeUnloadEvent && @event.Type == "beforeunload")
        {
            if (returnValue is not null)
            {
                @event.PreventDefault();
                if (beforeUnloadEvent.ReturnValue.Length == 0 && returnValue is string text)
                {
                    beforeUnloadEvent.ReturnValue = text;
                }
            }
        }
        else if (returnValue is false)
        {
            @event.PreventDefault();
        }
    }
}
