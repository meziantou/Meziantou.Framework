using System.Globalization;
using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// Resolves the keyframes of a CSS animation from a @keyframes rule (https://drafts.csswg.org/css-animations-2/#keyframes):
/// the keyframe blocks with the same offset, easing function and composite operation are merged, and the properties missing
/// from the keyframes at 0% and 100% take the underlying value.
/// </summary>
internal static class CssKeyframesResolver
{
    /// <summary>The value of the properties of the implicit keyframes: the underlying value (see <see cref="ComputedStyle"/>).</summary>
    public const string NeutralValue = "revert-layer";

    public static List<ProcessedKeyframe> Resolve(CssKeyframesRule rule, string defaultEasing, CompositeOperationOrAuto defaultComposite, string? baseUrl)
    {
        var (defaultEasingFunction, defaultEasingText) = ParseEasing(defaultEasing) ?? (CssEasing.Linear, "linear");
        var source = CascadedValueSource.Create(rule.ParentStyleSheet?.BaseUrl ?? baseUrl, treeScope: null, mixin: null);
        var keyframes = new List<ProcessedKeyframe>();
        foreach (var child in rule.ChildRules)
        {
            if (child is not CssKeyframeRule keyframeRule)
                continue;

            // The animation-timing-function and animation-composition of the block are its easing function and composite operation
            var easingFunction = defaultEasingFunction;
            var easingText = defaultEasingText;
            var composite = defaultComposite;
            var declarations = new List<CssLonghandValue>();
            foreach (var declaration in keyframeRule.Style.Declarations)
            {
                if (declaration.Important)
                    continue;

                switch (declaration.Name)
                {
                    case "animation-timing-function":
                        if (ParseEasing(declaration.Value.ComputationValue ?? declaration.Value.Value) is { } easing)
                        {
                            (easingFunction, easingText) = easing;
                        }

                        continue;
                    case "animation-composition":
                        composite = ParseComposite(declaration.Value.Value) ?? composite;
                        continue;
                }

                if (!KeyframeProcessing.IsAnimatableProperty(declaration.Name))
                    continue;

                declarations.Add(declaration.Value);
            }

            foreach (var key in keyframeRule.Keys)
            {
                // https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes: a timeline range name and a percentage (e.g.
                // entry 10%) attach the keyframe to a point of the timeline
                ProcessedKeyframe? keyframe;
                if (ParseOffset(key) is { } offset)
                {
                    keyframe = keyframes.Find(k => k.RangeOffset is null && k.ComputedOffset == offset && k.Easing == easingText && k.Composite == composite);
                    if (keyframe is null)
                    {
                        keyframe = new ProcessedKeyframe
                        {
                            Offset = offset,
                            ComputedOffset = offset,
                            Easing = easingText,
                            EasingFunction = easingFunction,
                            Composite = composite,
                            Source = source,
                        };
                        keyframes.Add(keyframe);
                    }
                }
                else if (TimelineRangeBoundary.ParseKeyframeSelector(ValueMatcher.RemoveWhitespace(Syntax.CssParser.ParseListOfComponentValues(key))) is { } rangeOffset)
                {
                    // A keyframe merged with a later block moves to the position of the later block, as in the browsers (WPT
                    // merge-timeline-offset-keyframes.html)
                    keyframe = keyframes.Find(k => Equals(k.RangeOffset, rangeOffset) && k.Easing == easingText && k.Composite == composite);
                    if (keyframe is not null)
                    {
                        keyframes.Remove(keyframe);
                        keyframes.Add(keyframe);
                    }
                    else
                    {
                        keyframe = new ProcessedKeyframe
                        {
                            RangeOffset = rangeOffset,
                            ComputedOffset = double.NaN,
                            Easing = easingText,
                            EasingFunction = easingFunction,
                            Composite = composite,
                            Source = source,
                        };
                        keyframes.Add(keyframe);
                    }
                }
                else
                {
                    continue;
                }

                foreach (var declaration in declarations)
                {
                    keyframe.Longhands[declaration.Name] = declaration;
                    keyframe.Properties.RemoveAll(p => p.Key == declaration.Name);
                    keyframe.Properties.Add(new(declaration.Name, declaration.Value));
                }
            }
        }

        // A stable sort by offset (the keyframes with a timeline offset are after the other ones, in their order)
        keyframes = [.. keyframes.OrderBy(k => k.RangeOffset is null ? k.ComputedOffset : double.PositiveInfinity)];

        // The implicit keyframes at 0% and 100% for the properties that are not specified at these offsets
        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyframe in keyframes)
        {
            properties.UnionWith(keyframe.Longhands.Keys);
        }

        // An empty @keyframes rule has no keyframes
        if (properties.Count == 0)
            return [];

        // The properties of the keyframes with a timeline offset get their implicit keyframes when the effect value is computed:
        // only when no keyframe is at or before 0% (at or after 100%) once the timeline offsets are resolved
        // (https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes)
        foreach (var keyframe in keyframes)
        {
            if (keyframe.RangeOffset is not null)
            {
                properties.ExceptWith(keyframe.Longhands.Keys);
            }
        }

        if (properties.Count == 0)
            return keyframes;

        AddImplicitKeyframe(keyframes, properties, 0, defaultEasingFunction, defaultEasingText, defaultComposite, source);
        AddImplicitKeyframe(keyframes, properties, 1, defaultEasingFunction, defaultEasingText, defaultComposite, source);
        return keyframes;
    }

    private static void AddImplicitKeyframe(List<ProcessedKeyframe> keyframes, HashSet<string> properties, double offset, CssEasing easing, string easingText, CompositeOperationOrAuto composite, CascadedValueSource? source)
    {
        var missing = new HashSet<string>(properties, StringComparer.Ordinal);
        foreach (var keyframe in keyframes)
        {
            if (keyframe.RangeOffset is null && keyframe.ComputedOffset == offset)
            {
                missing.ExceptWith(keyframe.Longhands.Keys);
            }
        }

        if (missing.Count == 0 && keyframes.Exists(k => k.RangeOffset is null && k.ComputedOffset == offset))
            return;

        // The keyframe of the offset with the default easing and composite operation, created when needed
        var implicitKeyframe = keyframes.Find(k => k.RangeOffset is null && k.ComputedOffset == offset && k.Easing == easingText && k.Composite == composite);
        if (implicitKeyframe is null)
        {
            // https://drafts.csswg.org/css-animations-2/#keyframes: the composite operation of a new implicit keyframe is replace
            implicitKeyframe = new ProcessedKeyframe
            {
                Offset = offset,
                ComputedOffset = offset,
                Easing = easingText,
                EasingFunction = easing,
                Composite = CompositeOperationOrAuto.Replace,
                Source = source,
                IsImplicit = true,
            };

            if (offset == 0)
            {
                keyframes.Insert(0, implicitKeyframe);
            }
            else if (keyframes.FindIndex(k => k.RangeOffset is not null) is var rangeIndex and >= 0)
            {
                keyframes.Insert(rangeIndex, implicitKeyframe);
            }
            else
            {
                keyframes.Add(implicitKeyframe);
            }
        }

        foreach (var property in missing.Order(StringComparer.Ordinal))
        {
            implicitKeyframe.Longhands[property] = new CssLonghandValue(property, NeutralValue);
        }
    }

    /// <summary>Returns the offset of a keyframe selector (e.g. 50%), or null for the timeline range keyframes (e.g. entry 10%).</summary>
    private static double? ParseOffset(string key)
    {
        if (key == "from")
            return 0;

        if (key == "to")
            return 1;

        if (key.EndsWith('%', StringComparison.Ordinal) && !key.Contains(' ', StringComparison.Ordinal) && double.TryParse(key.AsSpan(0, key.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            return percentage / 100;

        return null;
    }

    /// <summary>Parses an easing function with the full precision of its numbers (they are rounded by the serialization).</summary>
    private static (CssEasing Easing, string Text)? ParseEasing(string value) => CssEasing.Parse(value) is { } easing ? (easing, CssPropertyRules.SerializeEasingFunction(value) ?? value) : null;

    public static CompositeOperationOrAuto? ParseComposite(string value) => value switch
    {
        "replace" => CompositeOperationOrAuto.Replace,
        "add" => CompositeOperationOrAuto.Add,
        "accumulate" => CompositeOperationOrAuto.Accumulate,
        _ => null,
    };
}
