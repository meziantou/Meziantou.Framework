namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The play state of an animation (https://drafts.csswg.org/web-animations-1/#play-states).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#enumdef-animationplaystate"/>
[IdlInterface("AnimationPlayState", "https://drafts.csswg.org/web-animations-1/#enumdef-animationplaystate")]
public enum AnimationPlayState
{
    /// <summary>The current time of the animation is unresolved and there are no pending tasks.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-idle"/>
    [IdlMember("idle", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-idle")]
    Idle,

    /// <summary>The animation is running.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-running"/>
    [IdlMember("running", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-running")]
    Running,

    /// <summary>The animation is paused.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-paused"/>
    [IdlMember("paused", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-paused")]
    Paused,

    /// <summary>The animation reached the end of its effect (or its start, when it plays backwards).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-finished"/>
    [IdlMember("finished", "https://drafts.csswg.org/web-animations-1/#dom-animationplaystate-finished")]
    Finished,
}

/// <summary>The replace state of an animation (https://drafts.csswg.org/web-animations-1/#animation-replace-state).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#enumdef-animationreplacestate"/>
[IdlInterface("AnimationReplaceState", "https://drafts.csswg.org/web-animations-1/#enumdef-animationreplacestate")]
public enum AnimationReplaceState
{
    /// <summary>The animation has not been removed.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-active"/>
    [IdlMember("active", "https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-active")]
    Active,

    /// <summary>The animation was removed because other animations replace its effect.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-removed"/>
    [IdlMember("removed", "https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-removed")]
    Removed,

    /// <summary>The animation is never removed (see <see cref="Animation.Persist"/>).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-persisted"/>
    [IdlMember("persisted", "https://drafts.csswg.org/web-animations-1/#dom-animationreplacestate-persisted")]
    Persisted,
}

/// <summary>The fill mode of an animation effect (https://drafts.csswg.org/web-animations-1/#fill-behavior).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#enumdef-fillmode"/>
[IdlInterface("FillMode", "https://drafts.csswg.org/web-animations-1/#enumdef-fillmode")]
public enum FillMode
{
    /// <summary>The effect has no effect outside of its active interval.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-fillmode-none"/>
    [IdlMember("none", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-none")]
    None,

    /// <summary>The effect fills after its active interval.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-fillmode-forwards"/>
    [IdlMember("forwards", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-forwards")]
    Forwards,

    /// <summary>The effect fills before its active interval.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-fillmode-backwards"/>
    [IdlMember("backwards", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-backwards")]
    Backwards,

    /// <summary>The effect fills before and after its active interval.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-fillmode-both"/>
    [IdlMember("both", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-both")]
    Both,

    /// <summary>The fill mode of the effect: none for the keyframe effects.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-fillmode-auto"/>
    [IdlMember("auto", "https://drafts.csswg.org/web-animations-1/#dom-fillmode-auto")]
    Auto,
}

/// <summary>The playback direction of an animation effect (https://drafts.csswg.org/web-animations-1/#direction-control).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#enumdef-playbackdirection"/>
[IdlInterface("PlaybackDirection", "https://drafts.csswg.org/web-animations-1/#enumdef-playbackdirection")]
public enum PlaybackDirection
{
    /// <summary>All the iterations play forwards.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-normal"/>
    [IdlMember("normal", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-normal")]
    Normal,

    /// <summary>All the iterations play backwards.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-reverse"/>
    [IdlMember("reverse", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-reverse")]
    Reverse,

    /// <summary>The even iterations play forwards and the odd iterations backwards.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-alternate"/>
    [IdlMember("alternate", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-alternate")]
    Alternate,

    /// <summary>The even iterations play backwards and the odd iterations forwards.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-alternate-reverse"/>
    [IdlMember("alternate-reverse", "https://drafts.csswg.org/web-animations-1/#dom-playbackdirection-alternate-reverse")]
    AlternateReverse,
}

/// <summary>How the values of a keyframe effect are combined with the underlying value (https://drafts.csswg.org/web-animations-1/#effect-composition).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#enumdef-compositeoperation"/>
[IdlInterface("CompositeOperation", "https://drafts.csswg.org/web-animations-1/#enumdef-compositeoperation")]
public enum CompositeOperation
{
    /// <summary>The value replaces the underlying value.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-replace"/>
    [IdlMember("replace", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-replace")]
    Replace,

    /// <summary>The value is added to the underlying value.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-add"/>
    [IdlMember("add", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-add")]
    Add,

    /// <summary>The value is accumulated onto the underlying value.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-accumulate"/>
    [IdlMember("accumulate", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperation-accumulate")]
    Accumulate,
}

/// <summary>The composite operation of a keyframe, or auto to use the composite operation of the effect.</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#enumdef-compositeoperationorauto"/>
[IdlInterface("CompositeOperationOrAuto", "https://drafts.csswg.org/web-animations-1/#enumdef-compositeoperationorauto")]
public enum CompositeOperationOrAuto
{
    /// <summary>The value replaces the underlying value.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-replace"/>
    [IdlMember("replace", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-replace")]
    Replace,

    /// <summary>The value is added to the underlying value.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-add"/>
    [IdlMember("add", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-add")]
    Add,

    /// <summary>The value is accumulated onto the underlying value.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-accumulate"/>
    [IdlMember("accumulate", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-accumulate")]
    Accumulate,

    /// <summary>The composite operation of the keyframe effect.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-auto"/>
    [IdlMember("auto", "https://drafts.csswg.org/web-animations-1/#dom-compositeoperationorauto-auto")]
    Auto,
}

/// <summary>How the iterations of a keyframe effect build on each other (https://drafts.csswg.org/web-animations-2/#effect-accumulation-section).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-2/#enumdef-iterationcompositeoperation"/>
[IdlInterface("IterationCompositeOperation", "https://drafts.csswg.org/web-animations-2/#enumdef-iterationcompositeoperation")]
public enum IterationCompositeOperation
{
    /// <summary>Each iteration is independent.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-iterationcompositeoperation-replace"/>
    [IdlMember("replace", "https://drafts.csswg.org/web-animations-2/#dom-iterationcompositeoperation-replace")]
    Replace,

    /// <summary>The final value of each iteration is accumulated onto the next iterations.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-iterationcompositeoperation-accumulate"/>
    [IdlMember("accumulate", "https://drafts.csswg.org/web-animations-2/#dom-iterationcompositeoperation-accumulate")]
    Accumulate,
}
