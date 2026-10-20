using Meziantou.Framework.WebPlatform.Animations;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of <see cref="Element.Animate(IEnumerable{Keyframe}, KeyframeAnimationOptions)"/> (https://drafts.csswg.org/web-animations-1/#dictdef-keyframeanimationoptions).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#dictdef-keyframeanimationoptions"/>
[IdlInterface("KeyframeAnimationOptions", "https://drafts.csswg.org/web-animations-1/#dictdef-keyframeanimationoptions")]
public sealed class KeyframeAnimationOptions : KeyframeEffectOptions
{
    /// <summary>Gets the identifier of the animation.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeanimationoptions-id"/>
    [IdlMember("id", "https://drafts.csswg.org/web-animations-1/#dom-keyframeanimationoptions-id")]
    public string Id { get; init; } = "";

    /// <summary>Gets the timeline of the animation (null for no timeline); the default timeline of the document when not set.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-keyframeanimationoptions-timeline"/>
    [IdlMember("timeline", "https://drafts.csswg.org/web-animations-1/#dom-keyframeanimationoptions-timeline")]
    public AnimationTimeline? Timeline
    {
        get;
        init
        {
            field = value;
            HasTimeline = true;
        }
    }

    /// <summary>Whether <see cref="Timeline"/> is set.</summary>
    internal bool HasTimeline { get; private init; }

    /// <summary>
    /// Gets the start of the animation attachment range (https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangestart):
    /// "normal", an <c>animation-range-start</c> value, a <see cref="TimelineRangeOffset"/> or an offset.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangestart"/>
    [IdlMember("rangeStart", "https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangestart")]
    public TimelineRangeValue RangeStart { get; init; } = "normal";

    /// <summary>
    /// Gets the end of the animation attachment range (https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangeend):
    /// "normal", an <c>animation-range-end</c> value, a <see cref="TimelineRangeOffset"/> or an offset.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangeend"/>
    [IdlMember("rangeEnd", "https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangeend")]
    public TimelineRangeValue RangeEnd { get; init; } = "normal";
}

/// <summary>The options of <see cref="Element.GetAnimations"/> (https://drafts.csswg.org/web-animations-1/#dictdef-getanimationsoptions).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#dictdef-getanimationsoptions"/>
[IdlInterface("GetAnimationsOptions", "https://drafts.csswg.org/web-animations-1/#dictdef-getanimationsoptions")]
public sealed class GetAnimationsOptions
{
    /// <summary>Gets whether the animations of the descendants (and their pseudo-elements) are included.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-getanimationsoptions-subtree"/>
    [IdlMember("subtree", "https://drafts.csswg.org/web-animations-1/#dom-getanimationsoptions-subtree")]
    public bool Subtree { get; init; }

    /// <summary>Gets the pseudo-element whose animations are returned (e.g. <c>::before</c>), or null for the element.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-getanimationsoptions-pseudoelement"/>
    [IdlMember("pseudoElement", "https://drafts.csswg.org/web-animations-1/#dom-getanimationsoptions-pseudoelement")]
    public string? PseudoElement { get; init; }
}

// https://drafts.csswg.org/web-animations-1/#the-animatable-interface-mixin
public partial class Element
{
    /// <summary>
    /// Creates an animation of the element with the keyframes, whose duration is <paramref name="duration"/> milliseconds, and
    /// plays it (https://drafts.csswg.org/web-animations-1/#dom-animatable-animate).
    /// </summary>
    /// <exception cref="TypeErrorException">The keyframes or the duration are invalid.</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animatable-animate"/>
    [IdlMember("animate", "https://drafts.csswg.org/web-animations-1/#dom-animatable-animate")]
    public Animation Animate(IEnumerable<Keyframe>? keyframes, double duration)
    {
        var effect = new KeyframeEffect(this, keyframes, duration);
        var animation = new Animation(effect, NodeDocument.Timeline) { RealmWindow = NodeDocument.RelevantWindow };
        animation.Play();
        return animation;
    }

    /// <summary>Creates an animation of the element with the keyframes and plays it (https://drafts.csswg.org/web-animations-1/#dom-animatable-animate).</summary>
    /// <exception cref="TypeErrorException">The keyframes or the timing properties are invalid.</exception>
    /// <exception cref="DomException">The pseudo-element is invalid (SyntaxError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animatable-animate"/>
    [IdlMember("animate", "https://drafts.csswg.org/web-animations-1/#dom-animatable-animate")]
    public Animation Animate(IEnumerable<Keyframe>? keyframes, KeyframeAnimationOptions? options = null)
    {
        var rangeStart = options is null ? null : TimelineRangeBoundary.FromScriptValue(options.RangeStart, isEnd: false);
        var rangeEnd = options is null ? null : TimelineRangeBoundary.FromScriptValue(options.RangeEnd, isEnd: true);
        var effect = new KeyframeEffect(this, keyframes, options);
        var timeline = options is { HasTimeline: true } ? options.Timeline : NodeDocument.Timeline;
        var animation = new Animation(effect, timeline) { Id = options?.Id ?? "", RealmWindow = NodeDocument.RelevantWindow };
        animation.SetRangeFromOptions(rangeStart, rangeEnd);
        animation.Play();
        return animation;
    }

    /// <summary>
    /// Returns the relevant animations of the element (or of one of its pseudo-elements, or of its subtree), sorted by their
    /// composite order (https://drafts.csswg.org/web-animations-1/#dom-animatable-getanimations).
    /// </summary>
    /// <exception cref="DomException">The pseudo-element is invalid (SyntaxError).</exception>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-animatable-getanimations"/>
    [IdlMember("getAnimations", "https://drafts.csswg.org/web-animations-1/#dom-animatable-getanimations")]
    public IReadOnlyList<Animation> GetAnimations(GetAnimationsOptions? options = null)
    {
        var pseudoElement = KeyframeEffect.ParsePseudoElement(options?.PseudoElement);
        var document = NodeDocument;
        document.FlushAnimationStyles();
        return document.AnimationsOrNull?.GetAnimations(this, shadowIncluding: true, subtreeOf: this, pseudoElement, includeSubtree: options?.Subtree ?? false) ?? [];
    }
}
