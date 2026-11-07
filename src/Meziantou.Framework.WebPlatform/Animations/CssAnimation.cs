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
    private double? _cancelActiveTime;
    private double? _lastScheduledTime;

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

    // Setting the start time overrides animation-play-state only when it pauses or resumes the animation (https://drafts.csswg.org/css-animations-2/#animations)
    private protected override void OnStartTimeSetByScript(AnimationPlayState previousPlayState)
    {
        if ((previousPlayState == AnimationPlayState.Paused) != (PlayStateValue == AnimationPlayState.Paused))
        {
            PlayStateOverridden = true;
        }
    }

    // A canceled animation is no longer updated by animation-play-state, and its animationcancel event is queued at once (a
    // play() that follows in the same task restarts it: the next frame sees it starting again)
    private protected override void OnCanceledByScript()
    {
        PlayStateOverridden = true;
        QueueDeclarativeEvents();
    }

    internal override void OnKeyframesSetByScript() => KeyframesOverridden = true;

    // An effect set through the API is no longer updated by the animation-* properties of the effect (https://drafts.csswg.org/css-animations-2/#animations)
    private protected override void OnEffectSetByScript()
    {
        KeyframesOverridden = true;
        OverriddenTimingProperties ??= new(StringComparer.Ordinal);
        OverriddenTimingProperties.UnionWith(["delay", "endDelay", "fill", "iterationStart", "iterations", "duration", "direction", "easing"]);
    }

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
        IsAppendedToGlobalListWhenNotIdle = PlayStateValue == AnimationPlayState.Idle;
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

    /// <summary>Records the active time of the cancel event: the active time at the moment the animation is canceled, with a fill mode of both.</summary>
    private protected override void OnBecomingIdle() => _cancelActiveTime = GetActiveTimeForCancel();

    private double GetActiveTimeForCancel()
    {
        if (EffectValue is not { } effect)
            return 0;

        var localTime = effect.LocalTime;
        return effect.GetActiveTime(localTime, effect.GetPhase(localTime), FillMode.Both) ?? 0;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-animations-2/#event-dispatch: the events are determined by the phase change of the effect (or,
    /// without an effect, by the current time of the animation).
    /// </summary>
    internal override void QueueDeclarativeEvents()
    {
        if (HasPendingTask || OwningElement is null)
            return;

        var effect = EffectValue;
        _lastScheduledTime = null;
        AnimationEffectPhase phase;
        double? iteration = null;
        if (PlayStateValue == AnimationPlayState.Idle)
        {
            phase = AnimationEffectPhase.Idle;
        }
        else if (effect is null)
        {
            phase = CurrentTimeValue switch
            {
                null => AnimationEffectPhase.Idle,
                < 0 => AnimationEffectPhase.Before,
                _ => AnimationEffectPhase.After,
            };
        }
        else
        {
            var localTime = effect.LocalTime;
            phase = effect.GetPhase(localTime);
            iteration = effect.GetCurrentIteration(phase, effect.GetActiveTime(localTime, phase));
        }

        var previousPhase = _previousPhase ?? AnimationEffectPhase.Idle;
        var previousIteration = _previousIteration;
        var cancelActiveTime = _cancelActiveTime ?? _previousActiveTime;
        _previousPhase = phase;
        _previousIteration = iteration;
        _cancelActiveTime = null;
        if (phase != AnimationEffectPhase.Idle)
        {
            _previousActiveTime = GetActiveTimeForCancel();
        }

        var activeDuration = effect?.ActiveDuration ?? 0;
        var delay = effect?.NormalizedDelay ?? 0;
        var intervalStart = Math.Max(Math.Min(-delay, activeDuration), 0);
        var intervalEnd = Math.Max(Math.Min(EffectEnd - delay, activeDuration), 0);
        switch (previousPhase, phase)
        {
            case (AnimationEffectPhase.Idle or AnimationEffectPhase.Before, AnimationEffectPhase.Active):
                Queue("animationstart", intervalStart, delay);
                break;
            case (AnimationEffectPhase.Idle or AnimationEffectPhase.Before, AnimationEffectPhase.After):
                Queue("animationstart", intervalStart, delay);
                Queue("animationend", intervalEnd, delay);
                break;
            case (AnimationEffectPhase.Active, AnimationEffectPhase.Before):
                Queue("animationend", intervalStart, delay);
                break;
            case (AnimationEffectPhase.Active, AnimationEffectPhase.Active) when previousIteration != iteration && previousIteration is { } previous && iteration is { } current:
            {
                var boundary = Math.Max(previous, current);
                Queue("animationiteration", (boundary - effect!.IterationStart) * effect.IterationDuration, delay);
                break;
            }

            case (AnimationEffectPhase.Active, AnimationEffectPhase.After):
                Queue("animationend", intervalEnd, delay);
                break;
            case (AnimationEffectPhase.After, AnimationEffectPhase.Active):
                Queue("animationstart", intervalEnd, delay);
                break;
            case (AnimationEffectPhase.After, AnimationEffectPhase.Before):
                Queue("animationstart", intervalEnd, delay);
                Queue("animationend", intervalStart, delay);
                break;
            case (not AnimationEffectPhase.Idle and not AnimationEffectPhase.After, AnimationEffectPhase.Idle):
                Queue("animationcancel", Math.Max(cancelActiveTime, 0), delay, cancel: true);
                break;
        }
    }

    private void Queue(string type, double elapsedTime, double delay, bool cancel = false)
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
            scheduledTime = Timeline?.ToOriginRelativeTime(startTime + ((delay + elapsedTime) / PlaybackRate));
        }
        else if (StartTimeValue is null)
        {
            // An animation without start time (e.g. paused, with a hold time) reaches the phase now: as in the browsers, its
            // events are not dispatched before the events that occurred earlier (e.g. the animationcancel event of the animation
            // it replaced, when its element is re-parented)
            scheduledTime = Timeline?.ToOriginRelativeTime(Timeline.CurrentTimeValue);
        }

        // The events of a phase change are dispatched in the order of the table, even when an event would occur earlier than the
        // previous one (e.g. the end event at the interval start after the start event at the interval end when seeking backwards)
        if (scheduledTime is { } time && _lastScheduledTime is { } last && time < last)
        {
            scheduledTime = last;
        }

        _lastScheduledTime = scheduledTime;
        document.Animations.QueueEvent(animationEvent, target, this, scheduledTime);
    }
}
