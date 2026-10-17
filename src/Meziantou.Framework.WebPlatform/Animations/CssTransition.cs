using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// An animation created by a change of the value of a property with a transition (https://drafts.csswg.org/css-transitions-2/#the-CSSTransition-interface).
/// </summary>
[IdlInterface("CSSTransition", "https://drafts.csswg.org/css-transitions-2/#csstransition")]
public sealed class CssTransition : Animation
{
    private static long s_nextGeneration;

    private TransitionPhase? _previousPhase;
    private double _previousActiveTime;

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

    internal override void FlushPendingStyle() => OwningElement?.NodeDocument.FlushAnimationStyles();

    internal void PlayByStyle() => PlayCore(autoRewind: true);

    /// <summary>Cancels the transition when the style changes (https://drafts.csswg.org/css-transitions-1/#starting): the <c>transitioncancel</c> event is queued.</summary>
    internal void CancelByStyle()
    {
        CancelCore();
        QueueDeclarativeEvents();
        OwningElement = null;
        GlobalIndex = NextGlobalIndex();
    }

    // A transition canceled through the API is no longer a running transition of its owning element
    private protected override void OnCanceledByScript()
    {
        if (OwningElement is { } element)
        {
            element.NodeDocument.AnimationsOrNull?.RemoveRunningTransition(element, OwningPseudoElement, this);
        }

        QueueDeclarativeEvents();
        OwningElement = null;
        GlobalIndex = NextGlobalIndex();
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

    /// <summary>https://drafts.csswg.org/css-transitions-2/#event-dispatch</summary>
    internal override void QueueDeclarativeEvents()
    {
        if (EffectValue is not KeyframeEffect effect || OwningElement is null)
            return;

        var localTime = effect.LocalTime;
        TransitionPhase phase;
        double? activeTime = null;
        if (PlayStateValue == AnimationPlayState.Idle)
        {
            phase = TransitionPhase.Idle;
        }
        else if (HasPendingTask || StartTimeValue is null)
        {
            phase = TransitionPhase.Pending;
        }
        else
        {
            var effectPhase = effect.GetPhase(localTime);
            activeTime = effect.GetActiveTime(localTime, effectPhase);
            phase = effectPhase switch
            {
                AnimationEffectPhase.Before => TransitionPhase.Before,
                AnimationEffectPhase.Active => TransitionPhase.Active,
                AnimationEffectPhase.After => TransitionPhase.After,
                _ => TransitionPhase.Idle,
            };
        }

        var previousPhase = _previousPhase ?? TransitionPhase.Idle;
        var previousActiveTime = _previousActiveTime;
        _previousPhase = phase;
        if (activeTime is { } time)
        {
            _previousActiveTime = time;
        }

        var activeDuration = effect.ActiveDuration;
        var intervalStart = Math.Max(Math.Min(-effect.NormalizedDelay, activeDuration), 0);
        var intervalEnd = Math.Max(Math.Min(EffectEnd - effect.NormalizedDelay, activeDuration), 0);
        var wasPendingOrBefore = previousPhase is TransitionPhase.Pending or TransitionPhase.Before;
        switch (phase)
        {
            case TransitionPhase.Pending or TransitionPhase.Before when previousPhase == TransitionPhase.Idle:
                Queue("transitionrun", intervalStart, effect);
                break;
            case TransitionPhase.Active when previousPhase == TransitionPhase.Idle:
                Queue("transitionrun", intervalStart, effect);
                Queue("transitionstart", intervalStart, effect);
                break;
            case TransitionPhase.After when previousPhase == TransitionPhase.Idle:
                Queue("transitionrun", intervalStart, effect);
                Queue("transitionstart", intervalStart, effect);
                Queue("transitionend", intervalEnd, effect);
                break;
            case TransitionPhase.Active when wasPendingOrBefore:
                Queue("transitionstart", intervalStart, effect);
                break;
            case TransitionPhase.After when wasPendingOrBefore:
                Queue("transitionstart", intervalStart, effect);
                Queue("transitionend", intervalEnd, effect);
                break;
            case TransitionPhase.After when previousPhase == TransitionPhase.Active:
                Queue("transitionend", intervalEnd, effect);
                break;
            case TransitionPhase.Before when previousPhase == TransitionPhase.Active:
                Queue("transitionend", intervalStart, effect);
                break;
            case TransitionPhase.Active when previousPhase == TransitionPhase.After:
                Queue("transitionstart", intervalEnd, effect);
                break;
            case TransitionPhase.Before when previousPhase == TransitionPhase.After:
                Queue("transitionstart", intervalEnd, effect);
                Queue("transitionend", intervalStart, effect);
                break;
            case TransitionPhase.Idle when previousPhase is not TransitionPhase.Idle and not TransitionPhase.After:
                Queue("transitioncancel", Math.Max(previousActiveTime, 0), effect, cancel: true);
                break;
        }
    }

    private void Queue(string type, double elapsedTime, KeyframeEffect effect, bool cancel = false)
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
            scheduledTime = Timeline?.ToOriginRelativeTime(startTime + ((effect.NormalizedDelay + elapsedTime) / PlaybackRate));
        }

        document.Animations.QueueEvent(transitionEvent, target, this, scheduledTime);
    }
}
