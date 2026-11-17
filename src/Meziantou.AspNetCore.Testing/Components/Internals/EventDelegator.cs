using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components.RenderTree;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// Dispatches the DOM events to the event handlers of the components, as <c>EventDelegator.ts</c> of Blazor does: one
/// listener per event type on the document, which walks from the target to the root and invokes the handlers of the
/// elements, applying <c>@on{event}:preventDefault</c> and <c>@on{event}:stopPropagation</c>.
/// </summary>
internal sealed class EventDelegator(Document document, EventDelegator.DispatchCallback dispatch)
{
    public delegate void DispatchCallback(ulong eventHandlerId, EventFieldInfo? fieldInfo, Event domEvent, Element element);

    private static readonly HashSet<string> NonBubblingEvents = new(StringComparer.Ordinal)
    {
        "abort", "blur", "cancel", "canplay", "canplaythrough", "change", "close", "cuechange", "durationchange", "emptied",
        "ended", "error", "focus", "load", "loadeddata", "loadedmetadata", "loadend", "loadstart", "mouseenter",
        "mouseleave", "pointerenter", "pointerleave", "pause", "play", "playing", "progress", "ratechange", "reset",
        "scroll", "seeked", "seeking", "stalled", "submit", "suspend", "timeupdate", "toggle", "unload", "volumechange",
        "waiting",
    };

    // Having a handler implicitly prevents the default action of these events (a form submission navigates)
    private static readonly HashSet<string> AlwaysPreventDefaultEvents = new(StringComparer.Ordinal)
    {
        "submit",
    };

    private static readonly HashSet<string> DisableableEvents = new(StringComparer.Ordinal)
    {
        "click", "dblclick", "mousedown", "mousemove", "mouseup",
    };

    private readonly ConditionalWeakTable<Element, ElementEventInfo> _elementInfos = [];
    private readonly Dictionary<ulong, (Element Element, string EventName)> _handlers = [];
    private readonly HashSet<string> _registeredEventNames = new(StringComparer.Ordinal);

    public void SetListener(Element element, string eventName, ulong eventHandlerId, int componentId)
    {
        var info = _elementInfos.GetOrCreateValue(element);
        if (info.Handlers.TryGetValue(eventName, out var existing))
        {
            _handlers.Remove(existing.EventHandlerId);
        }

        info.Handlers[eventName] = (eventHandlerId, componentId);
        _handlers[eventHandlerId] = (element, eventName);
        EnsureGlobalListener(eventName);
    }

    public void RemoveListener(ulong eventHandlerId)
    {
        if (!_handlers.Remove(eventHandlerId, out var entry))
            return;

        if (_elementInfos.TryGetValue(entry.Element, out var info) && info.Handlers.TryGetValue(entry.EventName, out var handler) && handler.EventHandlerId == eventHandlerId)
        {
            info.Handlers.Remove(entry.EventName);
        }
    }

    public void SetPreventDefault(Element element, string eventName, bool value)
    {
        var info = _elementInfos.GetOrCreateValue(element);
        if (value)
        {
            info.PreventDefault.Add(eventName);
            EnsureGlobalListener(eventName);
        }
        else
        {
            info.PreventDefault.Remove(eventName);
        }
    }

    public void SetStopPropagation(Element element, string eventName, bool value)
    {
        var info = _elementInfos.GetOrCreateValue(element);
        if (value)
        {
            info.StopPropagation.Add(eventName);
            EnsureGlobalListener(eventName);
        }
        else
        {
            info.StopPropagation.Remove(eventName);
        }
    }

    /// <summary>Gets the event handlers registered on an element, by event name (without the <c>on</c> prefix).</summary>
    public IReadOnlyCollection<string> GetEventNames(Element element)
    {
        if (_elementInfos.TryGetValue(element, out var info))
            return info.Handlers.Keys;

        return [];
    }

    private void EnsureGlobalListener(string eventName)
    {
        if (!_registeredEventNames.Add(eventName))
            return;

        document.AddEventListener(eventName, OnGlobalEvent, useCapture: NonBubblingEvents.Contains(eventName));
    }

    private void OnGlobalEvent(Event domEvent)
    {
        var eventName = domEvent.Type;
        var candidate = domEvent.Target switch
        {
            Element element => element,
            Node node => node.ParentElement,
            _ => null,
        };

        var isNonBubbling = NonBubblingEvents.Contains(eventName);
        var stopPropagationWasRequested = false;
        while (candidate is not null)
        {
            if (_elementInfos.TryGetValue(candidate, out var info))
            {
                if (info.Handlers.TryGetValue(eventName, out var handler) && !(DisableableEvents.Contains(eventName) && IsDisabled(candidate)))
                {
                    if (AlwaysPreventDefaultEvents.Contains(eventName))
                    {
                        domEvent.PreventDefault();
                    }

                    var fieldInfo = GetEventFieldInfo(domEvent, handler.ComponentId);
                    dispatch(handler.EventHandlerId, fieldInfo, domEvent, candidate);
                }

                if (info.StopPropagation.Contains(eventName))
                {
                    stopPropagationWasRequested = true;
                }

                if (info.PreventDefault.Contains(eventName))
                {
                    domEvent.PreventDefault();
                }
            }

            if (isNonBubbling || stopPropagationWasRequested)
                break;

            candidate = candidate.ParentElement;
        }
    }

    private static bool IsDisabled(Element element) => element is HtmlButtonElement or HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement && element.HasAttribute("disabled");

    private static EventFieldInfo? GetEventFieldInfo(Event domEvent, int componentId)
    {
        // The value of a form field is sent with the event, so the renderer updates the attribute frame of the field
        // without rendering it again (https://github.com/dotnet/aspnetcore/blob/main/src/Components/Web.JS/src/Rendering/Events/EventFieldInfo.ts)
        object? value = domEvent.Target switch
        {
            HtmlInputElement { Type: "checkbox" } checkbox => checkbox.Checked,
            HtmlInputElement input => input.Value,
            HtmlSelectElement { Multiple: false } select => select.Value,
            HtmlTextAreaElement textArea => textArea.Value,
            _ => null,
        };

        return value is null ? null : new EventFieldInfo { ComponentId = componentId, FieldValue = value };
    }

    private sealed class ElementEventInfo
    {
        public Dictionary<string, (ulong EventHandlerId, int ComponentId)> Handlers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> PreventDefault { get; } = new(StringComparer.Ordinal);

        public HashSet<string> StopPropagation { get; } = new(StringComparer.Ordinal);
    }
}
