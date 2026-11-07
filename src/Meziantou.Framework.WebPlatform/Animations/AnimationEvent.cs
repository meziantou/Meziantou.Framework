using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The initialization dictionary of <see cref="AnimationEvent"/> (https://drafts.csswg.org/css-animations-1/#dictdef-animationeventinit).</summary>
/// <seealso href="https://drafts.csswg.org/css-animations-1/#dictdef-animationeventinit"/>
[IdlInterface("AnimationEventInit", "https://drafts.csswg.org/css-animations-1/#dictdef-animationeventinit")]
public sealed class AnimationEventInit : EventInit
{
    /// <summary>Gets the name of the animation.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-animationeventinit-animationname"/>
    [IdlMember("animationName", "https://drafts.csswg.org/css-animations-1/#dom-animationeventinit-animationname")]
    public string AnimationName { get; init; } = "";

    /// <summary>Gets the elapsed time of the animation, in seconds.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-animationeventinit-elapsedtime"/>
    [IdlMember("elapsedTime", "https://drafts.csswg.org/css-animations-1/#dom-animationeventinit-elapsedtime")]
    public double ElapsedTime { get; init; }

    /// <summary>Gets the pseudo-element the animation runs on (e.g. <c>::before</c>), or the empty string.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-animationeventinit-pseudoelement"/>
    [IdlMember("pseudoElement", "https://drafts.csswg.org/css-animations-1/#dom-animationeventinit-pseudoelement")]
    public string PseudoElement { get; init; } = "";

    /// <summary>Gets the animation that generated the event.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-2/#dom-animationeventinit-animation"/>
    [IdlMember("animation", "https://drafts.csswg.org/css-animations-2/#dom-animationeventinit-animation")]
    public CssAnimation? Animation { get; init; }
}

/// <summary>
/// The event fired by the CSS animations: <c>animationstart</c>, <c>animationiteration</c>, <c>animationend</c> and
/// <c>animationcancel</c> (https://drafts.csswg.org/css-animations-1/#interface-animationevent).
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-animations-1/#animationevent"/>
[IdlInterface("AnimationEvent", "https://drafts.csswg.org/css-animations-1/#animationevent")]
public sealed class AnimationEvent : Event, IRealmBoundPlatformObject
{
    /// <summary>Creates an event of the given type.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-animationevent-animationevent"/>
    [IdlMember("constructor", "https://drafts.csswg.org/css-animations-1/#dom-animationevent-animationevent")]
    public AnimationEvent(string type, AnimationEventInit? animationEventInitDict = null)
        : base(type, animationEventInitDict)
    {
        AnimationName = animationEventInitDict?.AnimationName ?? "";
        ElapsedTime = animationEventInitDict?.ElapsedTime ?? 0;
        PseudoElement = animationEventInitDict?.PseudoElement ?? "";
        Animation = animationEventInitDict?.Animation;
    }

    internal Window? RealmWindow { get; init; }

    Window? IRealmBoundPlatformObject.RealmWindow => RealmWindow;

    /// <summary>Gets the name of the animation (the value of <c>animation-name</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-animationevent-animationname"/>
    [IdlMember("animationName", "https://drafts.csswg.org/css-animations-1/#dom-animationevent-animationname")]
    public string AnimationName { get; }

    /// <summary>Gets the time the animation has been running, in seconds, excluding the time it was paused.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-animationevent-elapsedtime"/>
    [IdlMember("elapsedTime", "https://drafts.csswg.org/css-animations-1/#dom-animationevent-elapsedtime")]
    public double ElapsedTime { get; }

    /// <summary>Gets the pseudo-element the animation runs on (e.g. <c>::before</c>), or the empty string.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-1/#dom-animationevent-pseudoelement"/>
    [IdlMember("pseudoElement", "https://drafts.csswg.org/css-animations-1/#dom-animationevent-pseudoelement")]
    public string PseudoElement { get; }

    /// <summary>Gets the animation that generated the event.</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-2/#dom-animationevent-animation"/>
    [IdlMember("animation", "https://drafts.csswg.org/css-animations-2/#dom-animationevent-animation")]
    public CssAnimation? Animation { get; }
}
