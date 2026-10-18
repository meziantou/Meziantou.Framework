using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The phase of an animation effect (https://drafts.csswg.org/web-animations-1/#animation-effect-phases-and-states).</summary>
internal enum AnimationEffectPhase
{
    Before,
    Active,
    After,
    Idle,
}

/// <summary>
/// An animation effect: its timing properties and the timing model that calculates its progress from the current time of its
/// animation (https://drafts.csswg.org/web-animations-1/#animation-effects).
/// </summary>
[IdlInterface("AnimationEffect", "https://drafts.csswg.org/web-animations-1/#animationeffect")]
public abstract class AnimationEffect : IRealmBoundPlatformObject
{
    private protected AnimationEffect()
    {
    }

    /// <summary>The animation associated with the effect (https://drafts.csswg.org/web-animations-1/#associated-animation-of-an-animation-effect).</summary>
    internal Animation? Animation { get; set; }

    /// <summary>The start delay, in milliseconds.</summary>
    internal double Delay { get; set; }

    /// <summary>The end delay, in milliseconds.</summary>
    internal double EndDelay { get; set; }

    internal FillMode Fill { get; set; } = FillMode.Auto;

    internal double IterationStart { get; set; }

    internal double Iterations { get; set; } = 1;

    /// <summary>The iteration duration in milliseconds, or null for auto.</summary>
    internal double? Duration { get; set; }

    internal PlaybackDirection Direction { get; set; }

    /// <summary>The serialization of the easing function.</summary>
    internal string Easing { get; set; } = "linear";

    internal CssEasing EasingFunction { get; set; } = CssEasing.Linear;

    Window? IRealmBoundPlatformObject.RealmWindow => GetRealmWindow();

    private protected virtual Window? GetRealmWindow() => Animation?.Timeline?.Document?.RelevantWindow;

    /// <summary>Returns the specified timing properties of the effect (https://drafts.csswg.org/web-animations-1/#dom-animationeffect-gettiming).</summary>
    [IdlMember("getTiming", "https://drafts.csswg.org/web-animations-1/#dom-animationeffect-gettiming")]
    public EffectTiming GetTiming()
    {
        Animation?.FlushPendingStyle();
        return new EffectTiming
        {
            Delay = Delay,
            EndDelay = EndDelay,
            Fill = Fill,
            IterationStart = IterationStart,
            Iterations = Iterations,
            Duration = Duration is { } duration ? duration : "auto",
            Direction = Direction,
            Easing = Easing,
        };
    }

    /// <summary>Returns the calculated timing properties of the effect (https://drafts.csswg.org/web-animations-1/#dom-animationeffect-getcomputedtiming).</summary>
    [IdlMember("getComputedTiming", "https://drafts.csswg.org/web-animations-1/#dom-animationeffect-getcomputedtiming")]
    public ComputedEffectTiming GetComputedTiming()
    {
        Animation?.FlushPendingStyle();
        return GetComputedTiming(LocalTime);
    }

    /// <summary>Returns the calculated timing properties of the effect at a local time (the local time of the animator effect of a worklet animation).</summary>
    internal ComputedEffectTiming GetComputedTiming(double? localTime)
    {
        var phase = GetPhase(localTime);
        var activeTime = GetActiveTime(localTime, phase);
        double? progress = GetTransformedProgress(phase, activeTime);
        double? currentIteration = GetCurrentIteration(phase, activeTime);

        // https://drafts.csswg.org/web-animations-2/#the-computedeffecttiming-dictionary: the times are percentages for a
        // progress-based timeline (the delays are the specified ones)
        var animation = Animation;
        return new ComputedEffectTiming
        {
            Delay = Delay,
            EndDelay = EndDelay,
            Fill = ResolvedFill,
            IterationStart = IterationStart,
            Iterations = Iterations,
            Duration = animation?.TimelineValue is { IsProgressBased: true } ? new CssUnitValue(IterationDuration, "percent") : IterationDuration,
            Direction = Direction,
            Easing = Easing,
            StartTime = animation?.ToCssNumberish(0) ?? 0,
            EndTime = animation?.ToCssNumberish(EndTime) ?? EndTime,
            ActiveDuration = animation?.ToCssNumberish(ActiveDuration) ?? ActiveDuration,
            LocalTime = localTime is { } time ? animation?.ToCssNumberish(time) ?? time : (CssNumberish?)null,
            Progress = progress,
            CurrentIteration = currentIteration,
        };
    }

    /// <summary>
    /// Updates the timing properties of the effect (https://drafts.csswg.org/web-animations-1/#dom-animationeffect-updatetiming):
    /// the members of <paramref name="timing"/> that are set replace the current values.
    /// </summary>
    /// <exception cref="TypeErrorException">A value is invalid (e.g. a negative duration or an invalid easing function).</exception>
    [IdlMember("updateTiming", "https://drafts.csswg.org/web-animations-1/#dom-animationeffect-updatetiming")]
    public void UpdateTiming(OptionalEffectTiming? timing = null)
    {
        Animation?.FlushPendingStyle();
        if (timing is null)
            return;

        UpdateTimingCore(timing);
        Animation?.OnEffectTimingUpdated(timing);
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#update-the-timing-properties-of-an-animation-effect</summary>
    internal void UpdateTimingCore(OptionalEffectTiming timing)
    {
        // The values are validated before any of them is applied
        if (timing.IterationStart is { } iterationStart && (iterationStart < 0 || !double.IsFinite(iterationStart)))
            throw new TypeErrorException("The iteration start must be a finite number greater than or equal to 0.");

        if (timing.Iterations is { } iterations && (iterations < 0 || double.IsNaN(iterations)))
            throw new TypeErrorException("The number of iterations must be a number greater than or equal to 0.");

        // The iterations of an effect of a progress-based timeline must fit in its finite attachment range
        if (timing.Iterations is { } infiniteIterations && double.IsPositiveInfinity(infiniteIterations) && Animation?.TimelineValue is { IsProgressBased: true })
            throw new TypeErrorException("The number of iterations of an animation of a progress-based timeline must be finite.");

        double? duration = null;
        var durationIsAuto = false;
        if (timing.Duration is { } durationValue)
        {
            duration = ParseDuration(durationValue);
            durationIsAuto = duration is null;
        }

        CssEasing? easing = null;
        string? easingText = null;
        if (timing.Easing is { } easingValue)
        {
            (easing, easingText) = ParseEasing(easingValue);
        }

        if (timing.Delay is { } delay)
        {
            Delay = delay;
        }

        if (timing.EndDelay is { } endDelay)
        {
            EndDelay = endDelay;
        }

        if (timing.Fill is { } fill)
        {
            Fill = fill;
        }

        if (timing.IterationStart is { } start)
        {
            IterationStart = start;
        }

        if (timing.Iterations is { } count)
        {
            Iterations = count;
        }

        if (duration is not null || durationIsAuto)
        {
            Duration = duration;
        }

        if (timing.Direction is { } direction)
        {
            Direction = direction;
        }

        if (easing is not null)
        {
            EasingFunction = easing;
            Easing = easingText!;
        }

        Animation?.OnEffectChanged();
    }

    /// <summary>Returns the iteration duration of a duration value (null for auto); throws a TypeError when it is invalid.</summary>
    internal static double? ParseDuration(EffectDuration duration)
    {
        switch (duration.Value)
        {
            case double number:
                if (number < 0 || double.IsNaN(number))
                    throw new TypeErrorException("The duration must be a number greater than or equal to 0.");

                return number;

            case string text:
                if (text != "auto")
                    throw new TypeErrorException("The duration must be a number or \"auto\".");

                return null;

            case CssUnitValue { Unit: "ms" or "s" } unitValue:
                var milliseconds = unitValue.Unit == "s" ? unitValue.Value * 1000 : unitValue.Value;
                if (milliseconds < 0 || double.IsNaN(milliseconds))
                    throw new TypeErrorException("The duration must be greater than or equal to 0.");

                return milliseconds;

            default:
                throw new TypeErrorException("The duration must be a number or \"auto\".");
        }
    }

    /// <summary>Parses an easing function (https://drafts.csswg.org/web-animations-1/#dom-effecttiming-easing); throws a TypeError when it is invalid.</summary>
    internal static (CssEasing Easing, string Serialization) ParseEasing(string value)
    {
        if (CssEasing.Parse(value) is not { } easing || CssPropertyRules.SerializeEasingFunction(value) is not { } serialization)
            throw new TypeErrorException("'" + value + "' is not a valid easing function.");

        return (easing, serialization);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Timing model (https://drafts.csswg.org/web-animations-1/#timing-model)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The fill mode of the effect, where auto is none (https://drafts.csswg.org/web-animations-1/#fill-modes).</summary>
    internal FillMode ResolvedFill => Fill == FillMode.Auto ? FillMode.None : Fill;

    /// <summary>
    /// The iteration duration (https://drafts.csswg.org/web-animations-1/#iteration-duration): auto is the intrinsic iteration
    /// duration, 0 for a monotonic timeline. The timing of an effect of a progress-based timeline is normalized (percentages).
    /// </summary>
    internal double IterationDuration => GetNormalizedTiming().IterationDuration;

    /// <summary>The start delay (https://drafts.csswg.org/web-animations-1/#start-delay), normalized for a progress-based timeline.</summary>
    internal double NormalizedDelay => GetNormalizedTiming().StartDelay;

    /// <summary>The end delay (https://drafts.csswg.org/web-animations-1/#end-delay), normalized for a progress-based timeline.</summary>
    internal double NormalizedEndDelay => GetNormalizedTiming().EndDelay;

    /// <summary>
    /// https://drafts.csswg.org/web-animations-2/#normalize-specified-timing: the timing of an effect of a progress-based
    /// timeline is converted to proportions of the duration of its attachment range (a percentage of the timeline): the
    /// specified times keep their proportions, an auto duration fills the range (the delays are then 0).
    /// </summary>
    private (double StartDelay, double EndDelay, double IterationDuration) GetNormalizedTiming()
    {
        if (Animation?.TimelineDuration is not { } timelineDuration)
            return (Delay, EndDelay, Duration ?? 0);

        if (Duration is { } duration)
        {
            // https://drafts.csswg.org/web-animations-2/#time-based-animation-to-a-proportional-animation
            var activeDuration = duration == 0 || Iterations == 0 ? 0 : duration * Iterations;
            var totalTime = Math.Max(Delay + activeDuration + EndDelay, 0);
            if (double.IsPositiveInfinity(duration))
                return (0, 0, Iterations == 0 || double.IsInfinity(Iterations) ? 0 : timelineDuration / Iterations);

            if (totalTime == 0 || !double.IsFinite(totalTime))
                return (0, 0, 0);

            var scale = timelineDuration / totalTime;
            return (Delay * scale, EndDelay * scale, duration * scale);
        }

        // https://drafts.csswg.org/web-animations-2/#intrinsic-iteration-duration
        return (0, 0, Iterations == 0 || double.IsInfinity(Iterations) ? 0 : timelineDuration / Iterations);
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#active-duration</summary>
    internal double ActiveDuration
    {
        get
        {
            var duration = IterationDuration;
            if (duration == 0 || Iterations == 0)
                return 0;

            return duration * Iterations;
        }
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#end-time</summary>
    internal double EndTime
    {
        get
        {
            var (startDelay, endDelay, _) = GetNormalizedTiming();
            return Math.Max(startDelay + ActiveDuration + endDelay, 0);
        }
    }

    /// <summary>The local time: the current time of the associated animation (https://drafts.csswg.org/web-animations-1/#local-time).</summary>
    internal double? LocalTime => Animation is WorkletAnimation workletAnimation
        ? (workletAnimation.PlayStateValue == AnimationPlayState.Idle ? null : WorkletLocalTime)
        : Animation?.CurrentTimeValue;

    /// <summary>
    /// The local time of an effect of a worklet animation (https://drafts.css-houdini.org/css-animation-worklet-1/#worklet-animation-desc:
    /// "the associated animator instance controls the animation effect's local time directly"): the last local time the
    /// animator set, or null.
    /// </summary>
    internal double? WorkletLocalTime { get; set; }

    /// <summary>Whether the associated animation plays backwards (https://drafts.csswg.org/web-animations-1/#animation-direction).</summary>
    private bool IsBackwards => Animation is { } animation && animation.PlaybackRate < 0;

    /// <summary>https://drafts.csswg.org/web-animations-1/#animation-effect-phases-and-states</summary>
    /// <summary>
    /// Whether the phases are determined with the endpoint-inclusive active interval flag (used to commit the computed styles,
    /// https://drafts.csswg.org/web-animations-1/#commit-computed-styles).
    /// </summary>
    [ThreadStatic]
    private static bool s_endpointInclusive;

    /// <summary>Returns a scope in which the phases are determined with the endpoint-inclusive active interval flag.</summary>
    internal static EndpointInclusiveScope UseEndpointInclusiveActiveInterval() => new(enable: true);

    internal readonly struct EndpointInclusiveScope : IDisposable
    {
        private readonly bool _previous;

        public EndpointInclusiveScope(bool enable)
        {
            _previous = s_endpointInclusive;
            s_endpointInclusive = enable;
        }

        public void Dispose() => s_endpointInclusive = _previous;
    }

    internal AnimationEffectPhase GetPhase(double? localTime)
    {
        if (localTime is not { } time)
            return AnimationEffectPhase.Idle;

        var endTime = EndTime;
        var delay = NormalizedDelay;
        var beforeActiveBoundary = Math.Max(Math.Min(delay, endTime), 0);
        var activeAfterBoundary = Math.Max(Math.Min(delay + ActiveDuration, endTime), 0);
        var backwards = IsBackwards;

        // https://drafts.csswg.org/web-animations-2/#at-timeline-boundary: the boundaries are inclusive at the boundaries of a progress-based timeline
        var endpointInclusive = s_endpointInclusive || Animation is { IsAtTimelineBoundary: true };
        if (time < beforeActiveBoundary || (backwards && !endpointInclusive && time == beforeActiveBoundary))
            return AnimationEffectPhase.Before;

        if (time > activeAfterBoundary || (!backwards && !endpointInclusive && time == activeAfterBoundary))
            return AnimationEffectPhase.After;

        return AnimationEffectPhase.Active;
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#active-time</summary>
    internal double? GetActiveTime(double? localTime, AnimationEffectPhase phase)
    {
        var fill = ResolvedFill;
        var delay = NormalizedDelay;
        return phase switch
        {
            AnimationEffectPhase.Before when fill is FillMode.Backwards or FillMode.Both => Math.Max(localTime!.Value - delay, 0),
            AnimationEffectPhase.Active => localTime!.Value - delay,
            AnimationEffectPhase.After when fill is FillMode.Forwards or FillMode.Both => Math.Max(Math.Min(localTime!.Value - delay, ActiveDuration), 0),
            _ => null,
        };
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#overall-progress</summary>
    private double? GetOverallProgress(AnimationEffectPhase phase, double? activeTime)
    {
        if (activeTime is not { } time)
            return null;

        var duration = IterationDuration;
        if (duration == 0)
            return phase == AnimationEffectPhase.Before ? IterationStart : IterationStart + Iterations;

        return (time / duration) + IterationStart;
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#simple-iteration-progress</summary>
    private double? GetSimpleIterationProgress(AnimationEffectPhase phase, double? activeTime, double? overallProgress)
    {
        if (overallProgress is not { } overall)
            return null;

        var simple = double.IsInfinity(overall) ? IterationStart % 1 : overall % 1;
        if (simple == 0 && phase is AnimationEffectPhase.Active or AnimationEffectPhase.After && activeTime == ActiveDuration && Iterations != 0)
        {
            simple = 1;
        }

        return simple;
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#current-iteration</summary>
    internal double? GetCurrentIteration(AnimationEffectPhase phase, double? activeTime)
    {
        if (activeTime is null)
            return null;

        if (phase == AnimationEffectPhase.After && double.IsPositiveInfinity(Iterations))
            return double.PositiveInfinity;

        var overall = GetOverallProgress(phase, activeTime)!.Value;
        var simple = GetSimpleIterationProgress(phase, activeTime, overall);
        return simple == 1 ? Math.Floor(overall) - 1 : Math.Floor(overall);
    }

    /// <summary>
    /// Returns the transformed progress of the effect (https://drafts.csswg.org/web-animations-1/#calculating-the-transformed-progress):
    /// the iteration progress, or null when the effect is not in effect.
    /// </summary>
    internal double? GetTransformedProgress(AnimationEffectPhase phase, double? activeTime)
    {
        var overall = GetOverallProgress(phase, activeTime);
        if (GetSimpleIterationProgress(phase, activeTime, overall) is not { } simple)
            return null;

        // https://drafts.csswg.org/web-animations-1/#calculating-the-directed-progress
        var currentIteration = GetCurrentIteration(phase, activeTime)!.Value;
        bool forwards;
        if (Direction is PlaybackDirection.Normal)
        {
            forwards = true;
        }
        else if (Direction is PlaybackDirection.Reverse)
        {
            forwards = false;
        }
        else
        {
            var d = currentIteration;
            if (Direction is PlaybackDirection.AlternateReverse)
            {
                d++;
            }

            forwards = double.IsInfinity(d) || d % 2 == 0;
        }

        var directed = forwards ? simple : 1 - simple;
        var beforeFlag = (phase == AnimationEffectPhase.Before && forwards) || (phase == AnimationEffectPhase.After && !forwards);
        return EasingFunction.Evaluate(directed, beforeFlag);
    }

    /// <summary>Returns the iteration progress at the current time of the associated animation, or null when the effect is not in effect.</summary>
    internal double? GetIterationProgress()
    {
        var localTime = LocalTime;
        var phase = GetPhase(localTime);
        return GetTransformedProgress(phase, GetActiveTime(localTime, phase));
    }

    /// <summary>
    /// Returns the before flag of the easing functions at the current time (https://drafts.csswg.org/web-animations-1/#before-flag):
    /// the effect is in its before phase going forwards, or in its after phase going backwards.
    /// </summary>
    internal bool GetBeforeFlag()
    {
        var localTime = LocalTime;
        var phase = GetPhase(localTime);
        if (phase is not (AnimationEffectPhase.Before or AnimationEffectPhase.After))
            return false;

        var currentIteration = GetCurrentIteration(phase, GetActiveTime(localTime, phase)) ?? 0;
        var forwards = Direction switch
        {
            PlaybackDirection.Normal => true,
            PlaybackDirection.Reverse => false,
            _ => double.IsInfinity(currentIteration + (Direction == PlaybackDirection.AlternateReverse ? 1 : 0)) || (currentIteration + (Direction == PlaybackDirection.AlternateReverse ? 1 : 0)) % 2 == 0,
        };

        return (phase == AnimationEffectPhase.Before && forwards) || (phase == AnimationEffectPhase.After && !forwards);
    }

    /// <summary>Returns the current iteration at the current time of the associated animation, or 0 when the effect is not in effect.</summary>
    internal double GetComputedIteration()
    {
        var localTime = LocalTime;
        var phase = GetPhase(localTime);
        return GetCurrentIteration(phase, GetActiveTime(localTime, phase)) ?? 0;
    }

    /// <summary>
    /// Whether the effect is in play: in its active phase, and its animation is not finished
    /// (https://drafts.csswg.org/web-animations-1/#in-play).
    /// </summary>
    internal bool IsInPlay => GetPhase(LocalTime) == AnimationEffectPhase.Active && Animation is { } animation && animation.PlayState != AnimationPlayState.Finished;

    /// <summary>https://drafts.csswg.org/web-animations-1/#current</summary>
    internal bool IsCurrent
    {
        get
        {
            if (Animation is not { } animation)
                return false;

            var phase = GetPhase(LocalTime);
            if (phase == AnimationEffectPhase.Active && animation.PlayState != AnimationPlayState.Finished)
                return true;

            if ((animation.PlaybackRate > 0 && phase == AnimationEffectPhase.Before) || (animation.PlaybackRate < 0 && phase == AnimationEffectPhase.After))
                return true;

            // An animation of a timeline that is not monotonically increasing (e.g. a scroll timeline) is current while it is not idle
            return animation.TimelineValue is { IsMonotonicallyIncreasing: false } && animation.PlayStateValue != AnimationPlayState.Idle;
        }
    }

    /// <summary>Whether the effect is in effect: its active time is resolved (https://drafts.csswg.org/web-animations-1/#in-effect).</summary>
    internal bool IsInEffect
    {
        get
        {
            var localTime = LocalTime;
            return GetActiveTime(localTime, GetPhase(localTime)) is not null;
        }
    }

    /// <summary>Called when the associated animation of the effect changes.</summary>
    internal virtual void OnAnimationChanged(Animation? previous)
    {
    }
}
