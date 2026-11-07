using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// An animation created by a change of the value of a property with a transition (https://drafts.csswg.org/css-transitions-2/#the-CSSTransition-interface).
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-transitions-2/#csstransition"/>
[IdlInterface("CSSTransition", "https://drafts.csswg.org/css-transitions-2/#csstransition")]
public sealed class CssTransition : Animation
{
    private static long s_nextGeneration;

    private TransitionPhase? _previousPhase;
    private double _previousActiveTime;
    private double? _cancelActiveTime;
    private double? _lastScheduledTime;

    private enum TransitionPhase
    {
        Idle,
        Pending,
        Before,
        Active,
        After,
    }

    internal CssTransition(Element owningElement, string? pseudoElement, string property, KeyframeEffect effect, AnimationTimeline? timeline)
        : base(effect, timeline)
    {
        OwningElement = owningElement;
        OwningPseudoElement = pseudoElement;
        TransitionProperty = property;
        TransitionGeneration = Interlocked.Increment(ref s_nextGeneration);
    }

    /// <summary>Gets the name of the transitioned property (https://drafts.csswg.org/css-transitions-2/#dom-csstransition-transitionproperty).</summary>
    /// <seealso href="https://drafts.csswg.org/css-transitions-2/#dom-csstransition-transitionproperty"/>
    [IdlMember("transitionProperty", "https://drafts.csswg.org/css-transitions-2/#dom-csstransition-transitionproperty")]
    public string TransitionProperty { get; }

    /// <summary>The element whose style change created the transition (https://drafts.csswg.org/css-transitions-2/#owning-element), or null.</summary>
    internal Element? OwningElement { get; private set; }

    internal string? OwningPseudoElement { get; }

    /// <summary>https://drafts.csswg.org/css-transitions-2/#transition-generation</summary>
    internal long TransitionGeneration { get; }

    /// <summary>The generation of the last transition created: the transitions created after it have a greater generation.</summary>
    internal static long LastGeneration => Interlocked.Read(ref s_nextGeneration);

    /// <summary>The start value of the transition (a computed value).</summary>
    internal string StartValue { get; init; } = "";

    /// <summary>The end value of the transition (a computed value).</summary>
    internal string EndValue { get; init; } = "";

    /// <summary>https://drafts.csswg.org/css-transitions-1/#reversing-adjusted-start-value</summary>
    internal string ReversingAdjustedStartValue { get; init; } = "";

    /// <summary>https://drafts.csswg.org/css-transitions-1/#reversing-shortening-factor</summary>
    internal double ReversingShorteningFactor { get; init; } = 1;

    /// <summary>
    /// Whether the layout started the transition: its values are the results of anchor functions or the declarations of a position
    /// option of an anchor-positioned box (see <see cref="Css.Animations.CssTransitionUpdater.UpdateFromLayout"/>).
    /// </summary>
    internal bool IsStartedByLayout { get; init; }

    internal override void FlushPendingStyle() => OwningElement?.NodeDocument.FlushAnimationStyles();

    internal void PlayByStyle() => PlayCore(autoRewind: true);

    /// <summary>Cancels the transition when the style changes (https://drafts.csswg.org/css-transitions-1/#starting): the <c>transitioncancel</c> event is queued.</summary>
    internal void CancelByStyle() => CancelByStyle(discarded: false);

    private void CancelByStyle(bool discarded)
    {
        // A transition canceled by the style change that follows the one that started it (before an animation frame) has run:
        // its transitionrun event is queued before its transitioncancel event
        if (!discarded && _previousPhase is null)
        {
            QueueDeclarativeEvents();
        }

        CancelCore();
        QueueDeclarativeEvents();
        OwningElement = null;
        GlobalIndex = NextGlobalIndex();
        IsAppendedToGlobalListWhenNotIdle = PlayStateValue == AnimationPlayState.Idle;
    }

    /// <summary>
    /// Cancels a transition started by a style that the layout discarded (see <see cref="Css.Cascade.StyleEngine.InvalidateQueryContainerDescendants"/>):
    /// it was not sampled yet, and its effect is removed from the effect stack of its target (the layout may start and discard
    /// a transition each time it lays out a query container).
    /// </summary>
    internal void Discard()
    {
        CancelByStyle(discarded: true);
        SetEffectCore(null);
    }

    // A transition canceled through the API is no longer a running transition of its owning element, but it is not disassociated
    // from it (https://drafts.csswg.org/css-transitions-2/#csstransition): a play() that follows dispatches its events again
    private protected override void OnCanceledByScript()
    {
        if (OwningElement is { } element)
        {
            element.NodeDocument.AnimationsOrNull?.RemoveRunningTransition(element, OwningPseudoElement, this);
        }

        QueueDeclarativeEvents();
    }

    // The composite order of the transitions: the tree order of their owning elements, their generation, then their property
    internal override int CompositeOrderClass => OwningElement is null ? 2 : 0;

    internal override int CompareCompositeOrderWithinClass(Animation other)
    {
        if (other is not CssTransition { OwningElement: { } otherElement } otherTransition || OwningElement is null)
            return base.CompareCompositeOrderWithinClass(other);

        var result = DeclarativeAnimations.CompareTreeOrder(OwningElement, OwningPseudoElement, otherElement, otherTransition.OwningPseudoElement);
        if (result != 0)
            return result;

        result = TransitionGeneration.CompareTo(otherTransition.TransitionGeneration);
        return result != 0 ? result : string.CompareOrdinal(TransitionProperty, otherTransition.TransitionProperty);
    }

    /// <summary>Records the active time of the cancel event: the active time at the moment the transition is canceled, with a fill mode of both.</summary>
    private protected override void OnBecomingIdle() => _cancelActiveTime = GetActiveTimeForCancel();

    private double GetActiveTimeForCancel()
    {
        if (EffectValue is not { } effect)
            return 0;

        var localTime = effect.LocalTime;
        return effect.GetActiveTime(localTime, effect.GetPhase(localTime), FillMode.Both) ?? 0;
    }

    /// <summary>https://drafts.csswg.org/css-transitions-2/#event-dispatch</summary>
    internal override void QueueDeclarativeEvents()
    {
        if (OwningElement is null)
            return;

        var effect = EffectValue;
        _lastScheduledTime = null;
        var previousPhase = _previousPhase ?? TransitionPhase.Idle;
        TransitionPhase phase;
        if (PlayStateValue == AnimationPlayState.Idle)
        {
            phase = TransitionPhase.Idle;
        }
        else if (effect is null)
        {
            phase = CurrentTimeValue switch
            {
                null => TransitionPhase.Idle,
                < 0 => TransitionPhase.Before,
                _ => TransitionPhase.After,
            };
        }
        else if (HasPendingTask && previousPhase is TransitionPhase.Idle or TransitionPhase.Pending)
        {
            phase = TransitionPhase.Pending;
        }
        else
        {
            phase = effect.GetPhase(effect.LocalTime) switch
            {
                AnimationEffectPhase.Before => TransitionPhase.Before,
                AnimationEffectPhase.Active => TransitionPhase.Active,
                AnimationEffectPhase.After => TransitionPhase.After,
                _ => TransitionPhase.Idle,
            };
        }

        var cancelActiveTime = _cancelActiveTime ?? _previousActiveTime;
        _previousPhase = phase;
        _cancelActiveTime = null;
        if (phase != TransitionPhase.Idle)
        {
            _previousActiveTime = GetActiveTimeForCancel();
        }

        var activeDuration = effect?.ActiveDuration ?? 0;
        var delay = effect?.NormalizedDelay ?? 0;
        var intervalStart = Math.Max(Math.Min(-delay, activeDuration), 0);
        var intervalEnd = Math.Max(Math.Min(EffectEnd - delay, activeDuration), 0);
        var wasPendingOrBefore = previousPhase is TransitionPhase.Pending or TransitionPhase.Before;
        switch (phase)
        {
            case TransitionPhase.Pending or TransitionPhase.Before when previousPhase == TransitionPhase.Idle:
                Queue("transitionrun", intervalStart, delay);
                break;
            case TransitionPhase.Active when previousPhase == TransitionPhase.Idle:
                Queue("transitionrun", intervalStart, delay);
                Queue("transitionstart", intervalStart, delay);
                break;
            case TransitionPhase.After when previousPhase == TransitionPhase.Idle:
                Queue("transitionrun", intervalStart, delay);
                Queue("transitionstart", intervalStart, delay);
                Queue("transitionend", intervalEnd, delay);
                break;
            case TransitionPhase.Active when wasPendingOrBefore:
                Queue("transitionstart", intervalStart, delay);
                break;
            case TransitionPhase.After when wasPendingOrBefore:
                Queue("transitionstart", intervalStart, delay);
                Queue("transitionend", intervalEnd, delay);
                break;
            case TransitionPhase.After when previousPhase == TransitionPhase.Active:
                Queue("transitionend", intervalEnd, delay);
                break;
            case TransitionPhase.Before when previousPhase == TransitionPhase.Active:
                Queue("transitionend", intervalStart, delay);
                break;
            case TransitionPhase.Active when previousPhase == TransitionPhase.After:
                Queue("transitionstart", intervalEnd, delay);
                break;
            case TransitionPhase.Before when previousPhase == TransitionPhase.After:
                Queue("transitionstart", intervalEnd, delay);
                Queue("transitionend", intervalStart, delay);
                break;
            case TransitionPhase.Idle when previousPhase is not TransitionPhase.Idle and not TransitionPhase.After:
                Queue("transitioncancel", Math.Max(cancelActiveTime, 0), delay, cancel: true);
                break;
        }
    }

    private void Queue(string type, double elapsedTime, double delay, bool cancel = false)
    {
        if (OwningElement is not { } target)
            return;

        var document = target.NodeDocument;
        var transitionEvent = new TransitionEvent(type, new TransitionEventInit
        {
            // https://drafts.csswg.org/css-transitions-1/#transition-events: the events bubble
            Bubbles = true,
            PropertyName = TransitionProperty,
            ElapsedTime = elapsedTime / 1000,
            PseudoElement = OwningPseudoElement is { } pseudo ? "::" + pseudo : "",
            Animation = this,
        })
        { RealmWindow = document.RelevantWindow };

        double? scheduledTime = null;
        if (cancel)
        {
            scheduledTime = Timeline?.ToOriginRelativeTime(Timeline.CurrentTimeValue);
        }
        else if (StartTimeValue is { } startTime && PlaybackRate != 0)
        {
            scheduledTime = Timeline?.ToOriginRelativeTime(startTime + ((delay + elapsedTime) / PlaybackRate));
        }

        // The events of a phase change are dispatched in the order of the table, even when an event would occur earlier than the
        // previous one (e.g. the end event at the interval start after the start event at the interval end when seeking backwards)
        if (scheduledTime is { } time && _lastScheduledTime is { } last && time < last)
        {
            scheduledTime = last;
        }

        _lastScheduledTime = scheduledTime;
        document.Animations.QueueEvent(transitionEvent, target, this, scheduledTime);
    }
}
