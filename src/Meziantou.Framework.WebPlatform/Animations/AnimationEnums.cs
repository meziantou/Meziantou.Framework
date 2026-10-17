namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The play state of an animation (https://drafts.csswg.org/web-animations-1/#play-states).</summary>
[IdlInterface("AnimationPlayState", "https://drafts.csswg.org/web-animations-1/#enumdef-animationplaystate")]
public enum AnimationPlayState
{
    /// <summary>The current time of the animation is unresolved and there are no pending tasks.</summary>
    [IdlMember("idle", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-idle")]
    Idle,

    /// <summary>The animation is running.</summary>
    [IdlMember("running", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-running")]
    Running,

    /// <summary>The animation is paused.</summary>
    [IdlMember("paused", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-paused")]
    Paused,

    /// <summary>The animation reached the end of its effect (or its start, when it plays backwards).</summary>
    [IdlMember("finished", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-finished")]
    Finished,
}

/// <summary>The replace state of an animation (https://drafts.csswg.org/web-animations-1/#animation-replace-state).</summary>
[IdlInterface("AnimationReplaceState", "https://drafts.csswg.org/web-animations-1/#enumdef-animationreplacestate")]
public enum AnimationReplaceState
{
    /// <summary>The animation has not been removed.</summary>
    [IdlMember("active", "https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-active")]
    Active,

    /// <summary>The animation was removed because other animations replace its effect.</summary>
    [IdlMember("removed", "https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-removed")]
    Removed,

    /// <summary>The animation is never removed (see <see cref="Animation.Persist"/>).</summary>
    [IdlMember("persisted", "https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-persisted")]
    Persisted,
}

/// <summary>The fill mode of an animation effect (https://drafts.csswg.org/web-animations-1/#fill-behavior).</summary>
[IdlInterface("FillMode", "https://drafts.csswg.org/web-animations-1/#enumdef-fillmode")]
public enum FillMode
{
    /// <summary>The effect has no effect outside of its active interval.</summary>
    [IdlMember("none", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-none")]
    None,

    /// <summary>The effect fills after its active interval.</summary>
    [IdlMember("forwards", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-forwards")]
    Forwards,

    /// <summary>The effect fills before its active interval.</summary>
    [IdlMember("backwards", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-backwards")]
    Backwards,

    /// <summary>The effect fills before and after its active interval.</summary>
    [IdlMember("both", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-both")]
    Both,

    /// <summary>The fill mode of the effect: none for the keyframe effects.</summary>
    [IdlMember("auto", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-auto")]
    Auto,
}

/// <summary>The playback direction of an animation effect (https://drafts.csswg.org/web-animations-1/#direction-control).</summary>
[IdlInterface("PlaybackDirection", "https://drafts.csswg.org/web-animations-1/#enumdef-playbackdirection")]
public enum PlaybackDirection
{
    /// <summary>All the iterations play forwards.</summary>
    [IdlMember("normal", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-normal")]
    Normal,

    /// <summary>All the iterations play backwards.</summary>
    [IdlMember("reverse", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-reverse")]
    Reverse,

    /// <summary>The even iterations play forwards and the odd iterations backwards.</summary>
    [IdlMember("alternate", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-alternate")]
    Alternate,

    /// <summary>The even iterations play backwards and the odd iterations forwards.</summary>
    [IdlMember("alternate-reverse", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-alternate-reverse")]
    AlternateReverse,
}

/// <summary>How the values of a keyframe effect are combined with the underlying value (https://drafts.csswg.org/web-animations-1/#effect-composition).</summary>
[IdlInterface("CompositeOperation", "https://drafts.csswg.org/web-animations-1/#enumdef-compositeoperation")]
public enum CompositeOperation
{
    /// <summary>The value replaces the underlying value.</summary>
    [IdlMember("replace", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-replace")]
    Replace,

    /// <summary>The value is added to the underlying value.</summary>
    [IdlMember("add", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-add")]
    Add,

    /// <summary>The value is accumulated onto the underlying value.</summary>
    [IdlMember("accumulate", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-accumulate")]
    Accumulate,
}

/// <summary>The composite operation of a keyframe, or auto to use the composite operation of the effect.</summary>
[IdlInterface("CompositeOperationOrAuto", "https://drafts.csswg.org/web-animations-1/#enumdef-compositeoperationorauto")]
public enum CompositeOperationOrAuto
{
    /// <summary>The value replaces the underlying value.</summary>
    [IdlMember("replace", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-replace")]
    Replace,

    /// <summary>The value is added to the underlying value.</summary>
    [IdlMember("add", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-add")]
    Add,

    /// <summary>The value is accumulated onto the underlying value.</summary>
    [IdlMember("accumulate", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-accumulate")]
    Accumulate,

    /// <summary>The composite operation of the keyframe effect.</summary>
    [IdlMember("auto", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-auto")]
    Auto,
}

/// <summary>How the iterations of a keyframe effect build on each other (https://drafts.csswg.org/web-animations-2/#effect-accumulation-section).</summary>
[IdlInterface("IterationCompositeOperation", "https://drafts.csswg.org/web-animations-2/#enumdef-iterationcompositeoperation")]
public enum IterationCompositeOperation
{
    /// <summary>Each iteration is independent.</summary>
    [IdlMember("replace", "https://drafts.csswg.org/web-animations-2/#dom-iterationcompositeoperation-replace")]
    Replace,

    /// <summary>The final value of each iteration is accumulated onto the next iterations.</summary>
    [IdlMember("accumulate", "https://drafts.csswg.org/web-animations-2/#dom-iterationcompositeoperation-accumulate")]
    Accumulate,
}
