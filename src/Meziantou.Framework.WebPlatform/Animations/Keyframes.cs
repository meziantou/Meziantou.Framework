using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// A keyframe (https://drafts.csswg.org/web-animations-1/#dictdef-basekeyframe): its offset, easing function, composite
/// operation and property values.
/// </summary>
[IdlInterface("BaseKeyframe", "https://drafts.csswg.org/web-animations-1/#dictdef-basekeyframe")]
public class Keyframe
{
    /// <summary>Gets the offset of the keyframe, in [0, 1], or null to space the keyframes evenly.</summary>
    [IdlMember("offset", "https://drafts.csswg.org/web-animations-1/#dom-basekeyframe-offset")]
    public double? Offset { get; init; }

    /// <summary>
    /// The timeline offset of the keyframe (https://drafts.csswg.org/web-animations-2/#keyframe-offset-type), e.g. "entry 10%":
    /// the keyframe is attached to a point of a named timeline range instead of an offset of the iteration.
    /// </summary>
    internal TimelineRangeBoundary? RangeOffset { get; init; }

    /// <summary>Gets the easing function applied from this keyframe to the next one.</summary>
    [IdlMember("easing", "https://drafts.csswg.org/web-animations-1/#dom-basekeyframe-easing")]
    public string Easing { get; init; } = "linear";

    /// <summary>Gets the composite operation of the values of the keyframe.</summary>
    [IdlMember("composite", "https://drafts.csswg.org/web-animations-1/#dom-basekeyframe-composite")]
    public CompositeOperationOrAuto Composite { get; init; } = CompositeOperationOrAuto.Auto;

    /// <summary>Gets the property values of the keyframe, by CSS property name (e.g. <c>margin-left</c> or <c>--custom</c>).</summary>
    [NotInIdl("The property values are the other members of the keyframe object")]
    public IDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>A keyframe returned by <see cref="KeyframeEffect.GetKeyframes"/> (https://drafts.csswg.org/web-animations-1/#dictdef-basecomputedkeyframe).</summary>
[IdlInterface("BaseComputedKeyframe", "https://drafts.csswg.org/web-animations-1/#dictdef-basecomputedkeyframe")]
public sealed class ComputedKeyframe : Keyframe
{
    /// <summary>
    /// Gets the offset of the keyframe, computed when it is not specified. It is NaN (null in the script API) for a keyframe with a
    /// timeline offset that cannot be resolved (e.g. the timeline is not a view timeline or is inactive).
    /// </summary>
    [IdlMember("computedOffset", "https://drafts.csswg.org/web-animations-1/#dom-basecomputedkeyframe-computedoffset")]
    public double ComputedOffset { get; init; }

    /// <summary>Whether the computed offset is unresolved (null in the script API): the timeline offset of the keyframe cannot be resolved.</summary>
    internal bool IsOffsetUnresolved { get; init; }
}

/// <summary>A processed keyframe of a keyframe effect: its property values are valid and their longhands are expanded.</summary>
internal sealed class ProcessedKeyframe
{
    public double? Offset { get; init; }

    /// <summary>
    /// The timeline offset of the keyframe (e.g. "entry 10%"), or null: its <see cref="ComputedOffset"/> is resolved from the
    /// timeline and the attachment range of the animation (see <see cref="KeyframeEffect.ResolveTimelineOffsets"/>), NaN when it
    /// cannot be resolved (https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes).
    /// </summary>
    public TimelineRangeBoundary? RangeOffset { get; init; }

    /// <summary>Whether the timeline offset of the keyframe cannot be resolved (its <see cref="ComputedOffset"/> is NaN, reported as null).</summary>
    public bool IsOffsetUnresolved { get; set; }

    public double ComputedOffset { get; set; }

    public string Easing { get; init; } = "linear";

    public CssEasing EasingFunction { get; init; } = CssEasing.Linear;

    public CompositeOperationOrAuto Composite { get; init; } = CompositeOperationOrAuto.Auto;

    /// <summary>The property values as specified (CSS property names, in insertion order).</summary>
    public List<KeyValuePair<string, string>> Properties { get; init; } = [];

    /// <summary>The longhands (and custom properties) of the keyframe, after the expansion of the shorthands.</summary>
    public Dictionary<string, CssLonghandValue> Longhands { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The base URL of the values (the style sheet of a @keyframes rule), or null.</summary>
    public Css.Cascade.CascadedValueSource? Source { get; init; }

    /// <summary>Whether the keyframe is an implicit keyframe of a CSS animation, whose values are the underlying values.</summary>
    public bool IsImplicit { get; init; }

    private bool? _hasFlowRelativeLonghands;

    /// <summary>Whether a longhand of the keyframe is flow-relative (e.g. margin-inline-start), computed once the keyframe is complete.</summary>
    public bool HasFlowRelativeLonghands
    {
        get
        {
            if (_hasFlowRelativeLonghands is { } value)
                return value;

            var result = false;
            foreach (var name in Longhands.Keys)
            {
                if (Css.Cascade.LogicalProperties.IsFlowRelative(name))
                {
                    result = true;
                    break;
                }
            }

            _hasFlowRelativeLonghands = result;
            return result;
        }
    }
}

/// <summary>The processing of the keyframes (https://drafts.csswg.org/web-animations-1/#processing-a-keyframes-argument).</summary>
internal static class KeyframeProcessing
{
    /// <summary>
    /// Processes a list of keyframes: the offsets must be in [0, 1] and loosely sorted, and the easing functions valid (a
    /// TypeError is thrown otherwise); the invalid property values are discarded.
    /// </summary>
    public static List<ProcessedKeyframe> Process(IEnumerable<Keyframe>? keyframes)
    {
        var result = new List<ProcessedKeyframe>();
        if (keyframes is null)
            return result;

        double? previousOffset = null;
        foreach (var keyframe in keyframes)
        {
            if (keyframe.RangeOffset is null && keyframe.Offset is { } offset)
            {
                if (offset is < 0 or > 1 || double.IsNaN(offset))
                    throw new TypeErrorException("The keyframe offsets must be in the range [0, 1].");

                if (previousOffset is { } previous && offset < previous)
                    throw new TypeErrorException("The keyframe offsets must be sorted.");

                previousOffset = offset;
            }
        }

        foreach (var keyframe in keyframes)
        {
            var (easing, easingText) = AnimationEffect.ParseEasing(keyframe.Easing);
            var processed = new ProcessedKeyframe
            {
                Offset = keyframe.RangeOffset is null ? keyframe.Offset : null,
                RangeOffset = keyframe.RangeOffset,
                ComputedOffset = keyframe.RangeOffset is null ? 0 : double.NaN,
                Easing = easingText,
                EasingFunction = easing,
                Composite = keyframe.Composite,
            };

            AddProperties(processed, keyframe.Properties);
            result.Add(processed);
        }

        ComputeMissingOffsets(result);
        return result;
    }

    /// <summary>
    /// Adds the valid property values to a keyframe and expands the shorthands: the longhands win over the shorthands, and
    /// the shorthands with fewer longhands win over the other shorthands (https://drafts.csswg.org/web-animations-1/#calculating-computed-keyframes).
    /// </summary>
    private static void AddProperties(ProcessedKeyframe keyframe, IEnumerable<KeyValuePair<string, string>> properties)
    {
        var shorthands = new List<(string Name, List<CssLonghandValue> Longhands)>();
        foreach (var (name, value) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var propertyName = CssPropertyRegistry.NormalizeName(name);
            if (!IsAnimatableProperty(propertyName))
                continue;

            var longhands = CssStyleDeclaration.ParseValue(CssStyleDeclaration.KeyframeContext, propertyName, CssParser.ParseListOfComponentValues(value), value);
            if (longhands is null || longhands.Count == 0)
                continue;

            keyframe.Properties.Add(new(propertyName, value));
            if (CssPropertyRegistry.Get(propertyName) is { IsShorthand: true })
            {
                shorthands.Add((propertyName, longhands));
            }
            else
            {
                foreach (var longhand in longhands)
                {
                    keyframe.Longhands[longhand.Name] = longhand;
                }
            }
        }

        foreach (var (_, longhands) in shorthands.OrderBy(s => s.Longhands.Count).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            foreach (var longhand in longhands)
            {
                // The non-animatable longhands of a shorthand are ignored (e.g. the animation properties of all)
                if (CssAnimationTypes.Get(longhand.Name) != CssInterpolationKind.NotAnimatable)
                {
                    keyframe.Longhands.TryAdd(longhand.Name, longhand);
                }
            }
        }
    }

    /// <summary>Returns whether a property can be animated by a keyframe effect: an animatable longhand, a shorthand of animatable longhands, or a custom property.</summary>
    public static bool IsAnimatableProperty(string property)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return true;

        if (CssPropertyRegistry.Get(property) is not { } definition)
            return false;

        if (!definition.IsShorthand)
            return CssAnimationTypes.Get(definition.Name) != CssInterpolationKind.NotAnimatable;

        // A shorthand of the animation and transition properties (e.g. animation) is not animatable
        return definition.Name is not ("animation" or "transition") && CssPropertyRegistry.GetLonghandsRecursive(definition).Exists(longhand => CssAnimationTypes.Get(longhand) != CssInterpolationKind.NotAnimatable);
    }

    /// <summary>
    /// https://drafts.csswg.org/web-animations-1/#compute-missing-keyframe-offsets: the keyframes with a timeline offset are not
    /// part of the computation (their computed offset is resolved from the timeline).
    /// </summary>
    public static void ComputeMissingOffsets(List<ProcessedKeyframe> keyframes)
    {
        if (keyframes.Exists(k => k.RangeOffset is not null))
        {
            ComputeMissingOffsets(keyframes.FindAll(k => k.RangeOffset is null));
            return;
        }

        if (keyframes.Count == 0)
            return;

        var offsets = new double?[keyframes.Count];
        for (var i = 0; i < keyframes.Count; i++)
        {
            offsets[i] = keyframes[i].Offset;
        }

        if (keyframes.Count > 1 && offsets[0] is null)
        {
            offsets[0] = 0;
        }

        if (offsets[^1] is null)
        {
            offsets[^1] = 1;
        }

        var previousIndex = 0;
        for (var i = 1; i < offsets.Length; i++)
        {
            if (offsets[i] is not { } next)
                continue;

            var start = offsets[previousIndex] ?? 0;
            for (var j = previousIndex + 1; j < i; j++)
            {
                offsets[j] = start + ((next - start) * (j - previousIndex) / (i - previousIndex));
            }

            previousIndex = i;
        }

        for (var i = 0; i < keyframes.Count; i++)
        {
            keyframes[i].ComputedOffset = offsets[i] ?? 0;
        }
    }
}
