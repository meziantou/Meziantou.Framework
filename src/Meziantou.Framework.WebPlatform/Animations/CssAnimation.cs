using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// An animation created by the <c>animation-*</c> properties (https://drafts.csswg.org/css-animations-2/#the-CSSAnimation-interface).
/// It is updated when the styles of its owning element change, except for the properties changed through the API.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-animations-2/#cssanimation"/>
[IdlInterface("CSSAnimation", "https://drafts.csswg.org/css-animations-2/#cssanimation")]
public sealed class CssAnimation : Animation
{
    private AnimationEffectPhase? _previousPhase;
    private double? _previousIteration;
    private double _previousActiveTime;

    internal CssAnimation(Element owningElement, string? pseudoElement, string animationName, KeyframeEffect effect, AnimationTimeline? timeline)
        : base(effect, timeline)
    {
        OwningElement = owningElement;
        OwningPseudoElement = pseudoElement;
        AnimationName = animationName;
    }

    /// <summary>Gets the name of the animation: the value of <c>animation-name</c> (https://drafts.csswg.org/css-animations-2/#dom-cssanimation-animationname).</summary>
    /// <seealso href="https://drafts.csswg.org/css-animations-2/#dom-cssanimation-animationname"/>
    [IdlMember("animationName", "https://drafts.csswg.org/css-animations-2/#dom-cssanimation-animationname")]
    public string AnimationName { get; }

    /// <summary>The element whose style created the animation (https://drafts.csswg.org/css-animations-2/#owning-element), or null once it is no longer associated with it.</summary>
    internal Element? OwningElement { get; private set; }

    /// <summary>The pseudo-element of the owning element (e.g. "before"), or null.</summary>
    internal string? OwningPseudoElement { get; }

    /// <summary>The position of the animation in the list of <c>animation-name</c>.</summary>
    internal int NameIndex { get; set; }

    /// <summary>The @keyframes rule the keyframes were resolved from, and the inputs of the resolution.</summary>
    internal CssKeyframesRule? KeyframesRule { get; set; }

    internal object? KeyframesCacheKey { get; set; }

    /// <summary>The last value of <c>animation-play-state</c>.</summary>
    internal bool? StylePaused { get; set; }

    // The properties changed through the API are no longer updated by the styles (https://drafts.csswg.org/css-animations-2/#animations)
    internal bool PlayStateOverridden { get; private set; }

    internal bool KeyframesOverridden { get; private set; }

    internal bool TimelineOverridden { get; private set; }

    internal bool RangeStartOverridden { get; private set; }

    internal bool RangeEndOverridden { get; private set; }

    /// <summary>The value of <c>animation-timeline</c> the timeline was resolved from.</summary>
    internal Css.Animations.CssTimelineReference? TimelineReference { get; set; }

    /// <summary>The tree scope of the declaration of <c>animation-timeline</c> (null for the document): the timeline names are tree-scoped.</summary>
    internal Node? TimelineTreeScope { get; set; }

    internal HashSet<string>? OverriddenTimingProperties { get; private set; }

    /// <summary>Whether the timing property was changed through updateTiming().</summary>
    internal bool IsTimingOverridden(string property) => OverriddenTimingProperties?.Contains(property) == true;

    internal override void FlushPendingStyle() => OwningElement?.NodeDocument.FlushAnimationStyles();

    private protected override void OnPlayedByScript() => PlayStateOverridden = true;

    private protected override void OnPausedByScript() => PlayStateOverridden = true;

    // reverse() overrides the play state only when it resumes a paused animation (https://drafts.csswg.org/css-animations-2/#animations)
    private protected override void OnReversedByScript()
    {
        if (PausedBeforeReverse)
        {
            PlayStateOverridden = true;
        }
    }

    private protected override void OnReversingByScript() => PausedBeforeReverse = PlayStateValue == AnimationPlayState.Paused;

    private bool PausedBeforeReverse { get; set; }

    private protected override void OnTimelineSetByScript() => TimelineOverridden = true;

    private protected override void OnRangeSetByScript(bool isEnd)
    {
        if (isEnd)
        {
            RangeEndOverridden = true;
        }
        else
        {
            RangeStartOverridden = true;
        }
    }

    /// <summary>Sets the animation attachment range from <c>animation-range-start</c> and <c>animation-range-end</c>, except the boundaries set through the API.</summary>
    internal void SetRangeByStyle(TimelineRangeBoundary? start, TimelineRangeBoundary? end)
        => SetRange(RangeStartOverridden ? RangeStartValue : start, RangeEndOverridden ? RangeEndValue : end);

    private protected override void OnStartTimeSetByScript() => PlayStateOverridden = true;

    internal override void OnKeyframesSetByScript() => KeyframesOverridden = true;

    internal override void OnEffectTimingUpdated(OptionalEffectTiming timing)
    {
        var properties = OverriddenTimingProperties ??= new(StringComparer.Ordinal);
        if (timing.Delay is not null) properties.Add("delay");
        if (timing.EndDelay is not null) properties.Add("endDelay");
        if (timing.Fill is not null) properties.Add("fill");
        if (timing.IterationStart is not null) properties.Add("iterationStart");
        if (timing.Iterations is not null) properties.Add("iterations");
        if (timing.Duration is not null) properties.Add("duration");
        if (timing.Direction is not null) properties.Add("direction");
        if (timing.Easing is not null) properties.Add("easing");
    }

    internal void PlayByStyle(bool autoRewind) => PlayCore(autoRewind);

    internal void PauseByStyle() => PauseCore();

    internal void SetTimelineByStyle(AnimationTimeline? timeline) => SetTimelineCore(timeline);

    /// <summary>
    /// Cancels the animation when its name is removed from <c>animation-name</c> or its owning element is no longer rendered: the
    /// <c>animationcancel</c> event is queued, then the animation is no longer associated with its owning element.
    /// </summary>
    internal void CancelByStyle()
    {
        CancelCore();
        QueueDeclarativeEvents();
        Disassociate();
    }

    /// <summary>
    /// Cancels an animation created by a style that the layout discarded (see <see cref="Css.Cascade.StyleEngine.InvalidateQueryContainerDescendants"/>):
    /// it was not sampled yet, and its effect is removed from the effect stack of its target (the layout may create and discard
    /// an animation each time it lays out a query container).
    /// </summary>
    internal void Discard()
    {
        CancelByStyle();
        SetEffectCore(null);
    }

    /// <summary>Removes the association with the owning element: the animation becomes an animation of the global animation list.</summary>
    internal void Disassociate()
    {
        if (OwningElement is null)
            return;

        OwningElement = null;
        GlobalIndex = NextGlobalIndex();
    }

    // The composite order of the CSS animations: the tree order of their owning elements, then their position in animation-name
    internal override int CompositeOrderClass => OwningElement is null ? 2 : 1;

    internal override int CompareCompositeOrderWithinClass(Animation other)
    {
        if (other is not CssAnimation { OwningElement: { } otherElement } otherAnimation || OwningElement is null)
            return base.CompareCompositeOrderWithinClass(other);

        var result = DeclarativeAnimations.CompareTreeOrder(OwningElement, OwningPseudoElement, otherElement, otherAnimation.OwningPseudoElement);
        return result != 0 ? result : NameIndex.CompareTo(otherAnimation.NameIndex);
    }

    /// <summary>https://drafts.csswg.org/css-animations-2/#event-dispatch: the events are determined by the phase change of the effect.</summary>
    internal override void QueueDeclarativeEvents()
    {
        if (HasPendingTask || EffectValue is not KeyframeEffect effect || OwningElement is null)
            return;

        var localTime = effect.LocalTime;
        var phase = PlayStateValue == AnimationPlayState.Idle ? AnimationEffectPhase.Idle : effect.GetPhase(localTime);
        var activeTime = effect.GetActiveTime(localTime, phase);
        var iteration = effect.GetCurrentIteration(phase, activeTime);
        var previousPhase = _previousPhase ?? AnimationEffectPhase.Idle;
        var previousIteration = _previousIteration;
        var previousActiveTime = _previousActiveTime;
        _previousPhase = phase;
        _previousIteration = iteration;
        if (activeTime is { } time)
        {
            _previousActiveTime = time;
        }

        var activeDuration = effect.ActiveDuration;
        var intervalStart = Math.Max(Math.Min(-effect.NormalizedDelay, activeDuration), 0);
        var intervalEnd = Math.Max(Math.Min(EffectEnd - effect.NormalizedDelay, activeDuration), 0);
        switch (previousPhase, phase)
        {
            case (AnimationEffectPhase.Idle or AnimationEffectPhase.Before, AnimationEffectPhase.Active):
                Queue("animationstart", intervalStart, effect);
                break;
            case (AnimationEffectPhase.Idle or AnimationEffectPhase.Before, AnimationEffectPhase.After):
                Queue("animationstart", intervalStart, effect);
                Queue("animationend", intervalEnd, effect);
                break;
            case (AnimationEffectPhase.Active, AnimationEffectPhase.Before):
                Queue("animationend", intervalStart, effect);
                break;
            case (AnimationEffectPhase.Active, AnimationEffectPhase.Active) when previousIteration != iteration && previousIteration is { } previous && iteration is { } current:
            {
                var boundary = Math.Max(previous, current);
                Queue("animationiteration", (boundary - effect.IterationStart) * effect.IterationDuration, effect);
                break;
            }

            case (AnimationEffectPhase.Active, AnimationEffectPhase.After):
                Queue("animationend", intervalEnd, effect);
                break;
            case (AnimationEffectPhase.After, AnimationEffectPhase.Active):
                Queue("animationstart", intervalEnd, effect);
                break;
            case (AnimationEffectPhase.After, AnimationEffectPhase.Before):
                Queue("animationstart", intervalEnd, effect);
                Queue("animationend", intervalStart, effect);
                break;
            case (not AnimationEffectPhase.Idle and not AnimationEffectPhase.After, AnimationEffectPhase.Idle):
                Queue("animationcancel", Math.Max(previousActiveTime, 0), effect, cancel: true);
                break;
        }
    }

    private void Queue(string type, double elapsedTime, KeyframeEffect effect, bool cancel = false)
    {
        if (OwningElement is not { } target || target.NodeDocument is not { } document)
            return;

        var animationEvent = new AnimationEvent(type, new AnimationEventInit
        {
            // https://drafts.csswg.org/css-animations-1/#events: the events bubble
            Bubbles = true,
            AnimationName = AnimationName,
            ElapsedTime = elapsedTime / 1000,
            PseudoElement = OwningPseudoElement is { } pseudo ? "::" + pseudo : "",
            Animation = this,
        })
        { RealmWindow = document.RelevantWindow };

        // The scheduled event time: the time the event would occur, converted to an origin-relative time
        double? scheduledTime = null;
        if (cancel)
        {
            scheduledTime = Timeline?.ToOriginRelativeTime(Timeline.CurrentTimeValue);
        }
        else if (StartTimeValue is { } startTime && PlaybackRate != 0)
        {
            scheduledTime = Timeline?.ToOriginRelativeTime(startTime + ((effect.NormalizedDelay + elapsedTime) / PlaybackRate));
        }

        document.Animations.QueueEvent(animationEvent, target, this, scheduledTime);
    }
}
