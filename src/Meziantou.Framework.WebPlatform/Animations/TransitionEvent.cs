using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The initialization dictionary of <see cref="TransitionEvent"/> (https://drafts.csswg.org/css-transitions-1/#dictdef-transitioneventinit).</summary>
[IdlInterface("TransitionEventInit", "https://drafts.csswg.org/css-transitions-1/#dictdef-transitioneventinit")]
public sealed class TransitionEventInit : EventInit
{
    /// <summary>Gets the name of the transitioned property.</summary>
    [IdlMember("propertyName", "https://drafts.csswg.org/css-transitions-1/#dom-transitioneventinit-propertyname")]
    public string PropertyName { get; init; } = "";

    /// <summary>Gets the elapsed time of the transition, in seconds.</summary>
    [IdlMember("elapsedTime", "https://drafts.csswg.org/css-transitions-1/#dom-transitioneventinit-elapsedtime")]
    public double ElapsedTime { get; init; }

    /// <summary>Gets the pseudo-element the transition runs on (e.g. <c>::before</c>), or the empty string.</summary>
    [IdlMember("pseudoElement", "https://drafts.csswg.org/css-transitions-1/#dom-transitioneventinit-pseudoelement")]
    public string PseudoElement { get; init; } = "";

    /// <summary>Gets the transition that generated the event.</summary>
    [IdlMember("animation", "https://drafts.csswg.org/css-transitions-2/#dom-transitioneventinit-animation")]
    public CssTransition? Animation { get; init; }
}

/// <summary>
/// The event fired by the CSS transitions: <c>transitionrun</c>, <c>transitionstart</c>, <c>transitionend</c> and
/// <c>transitioncancel</c> (https://drafts.csswg.org/css-transitions-1/#interface-transitionevent).
/// </summary>
[IdlInterface("TransitionEvent", "https://drafts.csswg.org/css-transitions-1/#transitionevent")]
public sealed class TransitionEvent : Event, IRealmBoundPlatformObject
{
    /// <summary>Creates an event of the given type.</summary>
    [IdlMember("constructor", "https://drafts.csswg.org/css-transitions-1/#dom-transitionevent-transitionevent")]
    public TransitionEvent(string type, TransitionEventInit? transitionEventInitDict = null)
        : base(type, transitionEventInitDict)
    {
        PropertyName = transitionEventInitDict?.PropertyName ?? "";
        ElapsedTime = transitionEventInitDict?.ElapsedTime ?? 0;
        PseudoElement = transitionEventInitDict?.PseudoElement ?? "";
        Animation = transitionEventInitDict?.Animation;
    }

    internal Window? RealmWindow { get; init; }

    Window? IRealmBoundPlatformObject.RealmWindow => RealmWindow;

    /// <summary>Gets the name of the transitioned property.</summary>
    [IdlMember("propertyName", "https://drafts.csswg.org/css-transitions-1/#dom-transitionevent-propertyname")]
    public string PropertyName { get; }

    /// <summary>Gets the time the transition has been running, in seconds.</summary>
    [IdlMember("elapsedTime", "https://drafts.csswg.org/css-transitions-1/#dom-transitionevent-elapsedtime")]
    public double ElapsedTime { get; }

    /// <summary>Gets the pseudo-element the transition runs on (e.g. <c>::before</c>), or the empty string.</summary>
    [IdlMember("pseudoElement", "https://drafts.csswg.org/css-transitions-1/#dom-transitionevent-pseudoelement")]
    public string PseudoElement { get; }

    /// <summary>Gets the transition that generated the event.</summary>
    [IdlMember("animation", "https://drafts.csswg.org/css-transitions-2/#dom-transitionevent-animation")]
    public CssTransition? Animation { get; }
}
