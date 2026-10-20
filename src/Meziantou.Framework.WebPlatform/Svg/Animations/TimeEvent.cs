namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The event fired at the animation elements when their interval begins (<c>beginEvent</c>), ends (<c>endEvent</c>) or
/// repeats (<c>repeatEvent</c>) (https://svgwg.org/specs/animations/#InterfaceTimeEvent).
/// </summary>
/// <seealso href="https://svgwg.org/specs/animations/#InterfaceTimeEvent"/>
[IdlInterface("TimeEvent", "https://svgwg.org/specs/animations/#InterfaceTimeEvent")]
public sealed class TimeEvent : Event
{
    internal TimeEvent(string type, EventTarget? view, int detail)
        : base(type, eventInitDict: null)
    {
        View = view;
        Detail = detail;
    }

    /// <summary>Gets the window of the document of the element, or null.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__TimeEvent__view"/>
    [IdlMember("view", "https://svgwg.org/specs/animations/#__svg__TimeEvent__view")]
    public EventTarget? View { get; private set; }

    /// <summary>Gets the iteration of the element for a <c>repeatEvent</c> event, 0 for the other events.</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__TimeEvent__detail"/>
    [IdlMember("detail", "https://svgwg.org/specs/animations/#__svg__TimeEvent__detail")]
    public int Detail { get; private set; }

    /// <summary>https://svgwg.org/specs/animations/#__svg__TimeEvent__initTimeEvent</summary>
    /// <seealso href="https://svgwg.org/specs/animations/#__svg__TimeEvent__initTimeEvent"/>
    [IdlMember("initTimeEvent", "https://svgwg.org/specs/animations/#__svg__TimeEvent__initTimeEvent")]
    public void InitTimeEvent(string typeArg, EventTarget? viewArg = null, int detailArg = 0)
    {
        ArgumentNullException.ThrowIfNull(typeArg);
        if (DispatchFlag)
            return;

        Initialize(typeArg, bubbles: false, cancelable: false);
        View = viewArg;
        Detail = detailArg;
    }
}
