using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The initialization dictionary of <see cref="AnimationPlaybackEvent"/> (https://drafts.csswg.org/web-animations-2/#dictdef-animationplaybackeventinit).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-2/#dictdef-animationplaybackeventinit"/>
[IdlInterface("AnimationPlaybackEventInit", "https://drafts.csswg.org/web-animations-2/#dictdef-animationplaybackeventinit")]
public sealed class AnimationPlaybackEventInit : EventInit
{
    /// <summary>Gets the current time of the animation.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animationplaybackeventinit-currenttime"/>
    [IdlMember("currentTime", "https://drafts.csswg.org/web-animations-2/#dom-animationplaybackeventinit-currenttime")]
    public CssNumberish? CurrentTime { get; init; }

    /// <summary>Gets the current time of the timeline of the animation.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animationplaybackeventinit-timelinetime"/>
    [IdlMember("timelineTime", "https://drafts.csswg.org/web-animations-2/#dom-animationplaybackeventinit-timelinetime")]
    public CssNumberish? TimelineTime { get; init; }
}

/// <summary>
/// The event fired when the playback state of an animation changes: <c>finish</c>, <c>cancel</c> and <c>remove</c>
/// (https://drafts.csswg.org/web-animations-1/#animationplaybackevent).
/// </summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#animationplaybackevent"/>
[IdlInterface("AnimationPlaybackEvent", "https://drafts.csswg.org/web-animations-1/#animationplaybackevent")]
public sealed class AnimationPlaybackEvent : Event, IRealmBoundPlatformObject
{
    /// <summary>Creates an event of the given type.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationplaybackevent-animationplaybackevent"/>
    [IdlMember("constructor", "https://drafts.csswg.org/web-animations-1/#dom-animationplaybackevent-animationplaybackevent")]
    public AnimationPlaybackEvent(string type, AnimationPlaybackEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        CurrentTime = eventInitDict?.CurrentTime;
        TimelineTime = eventInitDict?.TimelineTime;
    }

    /// <summary>The window in whose realm the event is created (the window of the animation), or null for the current realm.</summary>
    internal Window? RealmWindow { get; init; }

    Window? IRealmBoundPlatformObject.RealmWindow => RealmWindow;

    /// <summary>Gets the current time of the animation that generated the event.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animationplaybackevent-currenttime"/>
    [IdlMember("currentTime", "https://drafts.csswg.org/web-animations-2/#dom-animationplaybackevent-currenttime")]
    public CssNumberish? CurrentTime { get; }

    /// <summary>Gets the current time of the timeline of the animation that generated the event.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animationplaybackevent-timelinetime"/>
    [IdlMember("timelineTime", "https://drafts.csswg.org/web-animations-2/#dom-animationplaybackevent-timelinetime")]
    public CssNumberish? TimelineTime { get; }
}
