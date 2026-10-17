using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The timing properties of an animation effect (https://drafts.csswg.org/web-animations-1/#dictdef-effecttiming).</summary>
[IdlInterface("EffectTiming", "https://drafts.csswg.org/web-animations-1/#dictdef-effecttiming")]
public class EffectTiming
{
    /// <summary>Gets the start delay, in milliseconds.</summary>
    [IdlMember("delay", "https://drafts.csswg.org/web-animations-2/#dom-effecttiming-delay")]
    public double Delay { get; init; }

    /// <summary>Gets the end delay, in milliseconds.</summary>
    [IdlMember("endDelay", "https://drafts.csswg.org/web-animations-2/#dom-effecttiming-enddelay")]
    public double EndDelay { get; init; }

    /// <summary>Gets the fill mode.</summary>
    [IdlMember("fill", "https://drafts.csswg.org/web-animations-1/#dom-effecttiming-fill")]
    public FillMode Fill { get; init; } = FillMode.Auto;

    /// <summary>Gets the iteration start: the offset, in iterations, at which the effect begins.</summary>
    [IdlMember("iterationStart", "https://drafts.csswg.org/web-animations-1/#dom-effecttiming-iterationstart")]
    public double IterationStart { get; init; }

    /// <summary>Gets the number of iterations (may be infinity).</summary>
    [IdlMember("iterations", "https://drafts.csswg.org/web-animations-1/#dom-effecttiming-iterations")]
    public double Iterations { get; init; } = 1;

    /// <summary>Gets the iteration duration, in milliseconds, or "auto".</summary>
    [IdlMember("duration", "https://drafts.csswg.org/web-animations-2/#dom-effecttiming-duration")]
    public EffectDuration Duration { get; init; } = "auto";

    /// <summary>Gets the playback direction.</summary>
    [IdlMember("direction", "https://drafts.csswg.org/web-animations-1/#dom-effecttiming-direction")]
    public PlaybackDirection Direction { get; init; }

    /// <summary>Gets the easing function applied to the iterations.</summary>
    [IdlMember("easing", "https://drafts.csswg.org/web-animations-1/#dom-effecttiming-easing")]
    public string Easing { get; init; } = "linear";
}

/// <summary>The timing properties to update (https://drafts.csswg.org/web-animations-1/#dictdef-optionaleffecttiming): null members are not changed.</summary>
[IdlInterface("OptionalEffectTiming", "https://drafts.csswg.org/web-animations-1/#dictdef-optionaleffecttiming")]
public sealed class OptionalEffectTiming
{
    /// <summary>Gets the start delay, in milliseconds.</summary>
    [IdlMember("delay", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-delay")]
    public double? Delay { get; init; }

    /// <summary>Gets the end delay, in milliseconds.</summary>
    [IdlMember("endDelay", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-enddelay")]
    public double? EndDelay { get; init; }

    /// <summary>Gets the fill mode.</summary>
    [IdlMember("fill", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-fill")]
    public FillMode? Fill { get; init; }

    /// <summary>Gets the iteration start.</summary>
    [IdlMember("iterationStart", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-iterationstart")]
    public double? IterationStart { get; init; }

    /// <summary>Gets the number of iterations.</summary>
    [IdlMember("iterations", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-iterations")]
    public double? Iterations { get; init; }

    /// <summary>Gets the iteration duration, in milliseconds, or "auto".</summary>
    [IdlMember("duration", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-duration")]
    public EffectDuration? Duration { get; init; }

    /// <summary>Gets the playback direction.</summary>
    [IdlMember("direction", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-direction")]
    public PlaybackDirection? Direction { get; init; }

    /// <summary>Gets the easing function.</summary>
    [IdlMember("easing", "https://drafts.csswg.org/web-animations-1/#dom-optionaleffecttiming-easing")]
    public string? Easing { get; init; }
}

/// <summary>The timing properties of an animation effect and its calculated timing (https://drafts.csswg.org/web-animations-1/#dictdef-computedeffecttiming).</summary>
[IdlInterface("ComputedEffectTiming", "https://drafts.csswg.org/web-animations-1/#dictdef-computedeffecttiming")]
public sealed class ComputedEffectTiming : EffectTiming
{
    /// <summary>Gets the iteration progress, or null when the effect is not in effect.</summary>
    [IdlMember("progress", "https://drafts.csswg.org/web-animations-1/#dom-computedeffecttiming-progress")]
    public double? Progress { get; init; }

    /// <summary>Gets the current iteration, or null when the effect is not in effect.</summary>
    [IdlMember("currentIteration", "https://drafts.csswg.org/web-animations-1/#dom-computedeffecttiming-currentiteration")]
    public double? CurrentIteration { get; init; }

    /// <summary>Gets the start time of the effect (always 0).</summary>
    [IdlMember("startTime", "https://drafts.csswg.org/web-animations-2/#dom-computedeffecttiming-starttime")]
    public CssNumberish StartTime { get; init; }

    /// <summary>Gets the end time of the effect.</summary>
    [IdlMember("endTime", "https://drafts.csswg.org/web-animations-2/#dom-computedeffecttiming-endtime")]
    public CssNumberish EndTime { get; init; }

    /// <summary>Gets the active duration of the effect.</summary>
    [IdlMember("activeDuration", "https://drafts.csswg.org/web-animations-2/#dom-computedeffecttiming-activeduration")]
    public CssNumberish ActiveDuration { get; init; }

    /// <summary>Gets the local time of the effect, or null when it is not associated with an animation or the current time of the animation is unresolved.</summary>
    [IdlMember("localTime", "https://drafts.csswg.org/web-animations-2/#dom-computedeffecttiming-localtime")]
    public CssNumberish? LocalTime { get; init; }
}

/// <summary>The (unrestricted double or CSSNumericValue or DOMString) union of the iteration duration: a number of milliseconds or "auto".</summary>
public readonly struct EffectDuration : IUnionValue, IEquatable<EffectDuration>
{
    private EffectDuration(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="double"/>, a <see cref="CssNumericValue"/> or a <see cref="string"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a number of milliseconds to an <c>EffectDuration</c>.</summary>
    [NotInIdl]
    public static implicit operator EffectDuration(double value) => new(value);

    /// <summary>Converts a string (e.g. "auto") to an <c>EffectDuration</c>.</summary>
    [NotInIdl]
    public static implicit operator EffectDuration(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssNumericValue</c> to an <c>EffectDuration</c>.</summary>
    [NotInIdl]
    public static implicit operator EffectDuration(CssNumericValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates an <c>EffectDuration</c> from a number of milliseconds.</summary>
    [NotInIdl]
    public static EffectDuration FromDouble(double value) => value;

    /// <summary>Creates an <c>EffectDuration</c> from a string.</summary>
    [NotInIdl]
    public static EffectDuration FromString(string value) => value;

    /// <summary>Creates an <c>EffectDuration</c> from a <c>CssNumericValue</c>.</summary>
    [NotInIdl]
    public static EffectDuration FromCssNumericValue(CssNumericValue value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(EffectDuration other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EffectDuration other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>EffectDuration</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(EffectDuration left, EffectDuration right) => left.Equals(right);

    /// <summary>Determines whether two <c>EffectDuration</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(EffectDuration left, EffectDuration right) => !left.Equals(right);
}
