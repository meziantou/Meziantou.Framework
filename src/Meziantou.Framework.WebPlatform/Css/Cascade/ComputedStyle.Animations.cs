using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The animation origin of the cascade (https://drafts.csswg.org/css-cascade-5/#cascade-origin-animation): the values of the
// effect stack of the element (https://drafts.csswg.org/web-animations-1/#the-effect-stack), which win over the normal
// declarations and lose to the important declarations
internal sealed partial class ComputedStyle
{
    /// <summary>The keyframe effects that target the element (or the pseudo-element), or null.</summary>
    private readonly List<KeyframeEffect>? _effects;

    /// <summary>
    /// Whether the values of the running transitions apply (https://drafts.csswg.org/css-cascade-5/#cascade-origin-transition);
    /// false for the after-change style (https://drafts.csswg.org/css-transitions-1/#after-change-style).
    /// </summary>
    public bool IncludeTransitions { get; init; } = true;

    /// <summary>
    /// Returns the animated value of a longhand: the value of the running transition (which wins over all the declarations),
    /// or the value of the effect stack of the animations (unless the declaration is important). Returns false when no effect
    /// in effect animates it.
    /// </summary>
    private bool TryGetAnimatedValue(CssPropertyDefinition property, out string value)
    {
        value = "";
        var name = property.Name;
        if (StyleEngine.GetPseudoElementPropertyFilter(PseudoElement) is { } applies && !applies(name))
            return false;

        if (ResolvedAnchorProperties is { } resolved && resolved.Contains(name))
            return false;

        if (!MayBeAnimated(name))
            return false;

        var transition = IncludeTransitions ? GetTransitionEffect(name) : null;
        var important = (TryGetCascadedValue(name, out var cascaded) && cascaded.IsImportant) || HasUserAgentImportantOverlay(name);
        var effects = important ? null : GetEffectStack(name);
        if (transition is null && effects is null)
            return false;

        var context = new CssInterpolationContext(this);
        string? result = ComputeUnanimatedProperty(property);
        foreach (var effect in (IEnumerable<KeyframeEffect>?)effects ?? [])
        {
            var underlying = result;
            result = ComputeEffectValue(effect, name, underlying, context, (keyframe, declared) => ComputeKeyframeValue(property, keyframe, declared, underlying));
        }

        if (transition is not null)
        {
            var underlying = result;
            result = ComputeEffectValue(transition, name, underlying, context, (keyframe, declared) => ComputeKeyframeValue(property, keyframe, declared, underlying));
        }

        value = result ?? "";
        return true;
    }

    /// <summary>Returns the animated value of a custom property, or false when no effect in effect animates it.</summary>
    private bool TryGetAnimatedCustomPropertyValue(string name, out string? value)
    {
        value = null;
        if (!MayBeAnimated(name))
            return false;

        var transition = IncludeTransitions ? GetTransitionEffect(name) : null;
        var important = _cascaded.TryGetValue(name, out var cascaded) && cascaded.IsImportant;
        var effects = important ? null : GetEffectStack(name);
        if (transition is null && effects is null)
            return false;

        var registration = RegisteredProperties.GetValueOrDefault(name);
        var context = new CssInterpolationContext(this);
        var result = ComputeUnanimatedCustomProperty(name, registration);
        foreach (var effect in (IEnumerable<KeyframeEffect>?)effects ?? [])
        {
            result = ComputeCustomPropertyEffectValue(effect, name, result, registration, context);
        }

        if (transition is not null)
        {
            result = ComputeCustomPropertyEffectValue(transition, name, result, registration, context);
        }

        value = result;
        return true;
    }

    private string? ComputeCustomPropertyEffectValue(KeyframeEffect effect, string name, string? underlying, CustomPropertyRegistration? registration, CssInterpolationContext context) =>
        ComputeEffectValue(effect, name, underlying, context, (keyframe, declared) =>
        {
            if (IsRevertKeyword(declared.Value))
                return underlying;

            return ComputeCustomProperty(name, new CascadedValue(declared, 0, keyframe.Source), registration, rollbacks: 0);
        });

    /// <summary>Returns the after-change style of the element: the same cascade, without the transitions (https://drafts.csswg.org/css-transitions-1/#after-change-style).</summary>
    internal ComputedStyle CreateAfterChangeStyle()
    {
        if (!IncludeTransitions)
            return this;

        if (_afterChangeStyle is { } afterChangeStyle)
            return afterChangeStyle;

        // The inherited values are the ones of the after-change style of the parent
        _cascadedShared = true;
        return _afterChangeStyle = WithVisitedCascadeOf(new ComputedStyle(Engine, Element, PseudoElement, Parent?.CreateAfterChangeStyle(), _cascaded, cascadedShared: true) { HighlightParent = HighlightParent, IncludeTransitions = false }, sameCascadedValues: true);
    }

    private ComputedStyle? _afterChangeStyle;

    /// <summary>
    /// Returns the style with the same cascade and the animations of the element at the current time: the before-change style
    /// of a transition is "the computed values of all properties on the element as of the previous style change event, except
    /// with any styles derived from declarative animations such as CSS Transitions, CSS Animations, and SMIL Animations updated
    /// to the current time" (https://drafts.csswg.org/css-transitions-1/#before-change-style). The animated values of the style
    /// were computed when it was (e.g. by the last rendering update, before a transition ended).
    /// </summary>
    internal ComputedStyle WithCurrentAnimations()
    {
        if (_effects is null)
            return this;

        _cascadedShared = true;
        var style = new ComputedStyle(Engine, Element, PseudoElement, Parent, _cascaded, cascadedShared: true) { HighlightParent = HighlightParent, IncludeTransitions = IncludeTransitions };
        style._hasOverlayAutoRule = _hasOverlayAutoRule;
        return WithVisitedCascadeOf(style, sameCascadedValues: true);
    }

    /// <summary>Returns the computed value of a longhand without the animations and the transitions.</summary>
    internal string GetUnanimatedComputedValue(string property)
    {
        if (_effects is not { Count: > 0 } || CssPropertyRegistry.Get(property) is not { IsShorthand: false } definition)
            return GetComputedValue(property);

        return ComputeUnanimatedProperty(definition);
    }

    /// <summary>
    /// Returns whether the element is not rendered: its display is none (without the animations of the element, which do not
    /// cancel themselves), or the display of an ancestor is none.
    /// </summary>
    internal bool IsDisplayNoneForAnimations()
    {
        if (GetUnanimatedComputedValue("display") == "none")
            return true;

        for (var style = Parent; style is not null; style = style.Parent)
        {
            if (style.GetComputedValue("display") == "none")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the properties targeted by the effects of the element that are current or in effect and whose animation is not
    /// removed, or null: "the user agent must act as if the will-change property on the target element includes the property"
    /// (https://drafts.csswg.org/web-animations-1/#side-effects-section).
    /// </summary>
    internal HashSet<string>? GetRelevantAnimatedProperties()
    {
        if (_effects is not { Count: > 0 } effects)
            return null;

        HashSet<string>? result = null;
        foreach (var effect in effects)
        {
            if (effect.Animation is not { ReplaceState: not AnimationReplaceState.Removed } || !effect.IsRelevant)
                continue;

            foreach (var keyframe in effect.Keyframes)
            {
                foreach (var property in keyframe.Longhands.Keys)
                {
                    if (!CssPropertyRegistry.IsCustomProperty(property))
                    {
                        (result ??= new HashSet<string>(StringComparer.Ordinal)).Add(property);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Returns whether a property is animated by an effect in effect that is not a CSS transition.</summary>
    internal bool IsAnimatedByAnimations(string property) => _effects is { Count: > 0 } && !HasUserAgentImportantOverlay(property) && GetEffectStack(property) is not null;

    /// <summary>
    /// Returns the value of a property with the effects of the animations up to <paramref name="animation"/> in the composite
    /// order, including its effect when it was removed (https://drafts.csswg.org/web-animations-1/#dom-animation-commitstyles).
    /// </summary>
    internal string? ComputeCommittedValue(string property, Animation animation)
    {
        var effects = new List<KeyframeEffect>();
        foreach (var effect in _effects ?? [])
        {
            if (effect.Animation is not { } effectAnimation || !Animates(effect, property) || effect.GetIterationProgress() is null)
                continue;

            if (effectAnimation.ReplaceState == AnimationReplaceState.Removed && !ReferenceEquals(effectAnimation, animation))
                continue;

            if (effectAnimation.CompareCompositeOrder(animation) <= 0)
            {
                effects.Add(effect);
            }
        }

        effects.Sort(static (a, b) => a.Animation!.CompareCompositeOrder(b.Animation!));
        var context = new CssInterpolationContext(this);
        if (CssPropertyRegistry.IsCustomProperty(property))
        {
            var registration = RegisteredProperties.GetValueOrDefault(property);
            var customResult = ComputeUnanimatedCustomProperty(property, registration);
            foreach (var effect in effects)
            {
                var underlying = customResult;
                customResult = ComputeEffectValue(effect, property, underlying, context, (keyframe, declared) => IsRevertKeyword(declared.Value) ? underlying : ComputeCustomProperty(property, new CascadedValue(declared, 0, keyframe.Source), registration, rollbacks: 0));
            }

            return customResult;
        }

        if (CssPropertyRegistry.Get(property) is not { IsShorthand: false } definition)
            return null;

        string? result = ComputeUnanimatedProperty(definition);
        foreach (var effect in effects)
        {
            var underlying = result;
            result = ComputeEffectValue(effect, property, underlying, context, (keyframe, declared) => ComputeKeyframeValue(definition, keyframe, declared, underlying));
        }

        return result;
    }

    /// <summary>Adds the names of the custom properties animated by the effects in effect.</summary>
    private void AddAnimatedCustomPropertyNames(SortedSet<string> names)
    {
        if (_effects is not { Count: > 0 } effects)
            return;

        foreach (var effect in effects)
        {
            if (!effect.IsInEffect)
                continue;

            foreach (var keyframe in effect.Keyframes)
            {
                foreach (var property in keyframe.Longhands.Keys)
                {
                    if (CssPropertyRegistry.IsCustomProperty(property))
                    {
                        names.Add(property);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns the effects in effect that animate a property (a physical longhand or a custom property), sorted by the
    /// composite order of their animations, or null when there is none.
    /// </summary>
    private List<KeyframeEffect>? GetEffectStack(string property)
    {
        List<KeyframeEffect>? result = null;
        foreach (var effect in _effects!)
        {
            if (effect.Animation is not { ReplaceState: not AnimationReplaceState.Removed } animation || animation is CssTransition || !Animates(effect, property) || effect.GetIterationProgress() is null)
                continue;

            (result ??= []).Add(effect);
        }

        if (result is { Count: > 1 })
        {
            result.Sort(static (a, b) => a.Animation!.CompareCompositeOrder(b.Animation!));
        }

        return result;
    }

    /// <summary>Returns the effect of the CSS transition in effect that animates a property (the last one in the composite order), or null.</summary>
    /// <summary>
    /// Returns whether the style has the same after-change computed values as another style of the same element (e.g. its
    /// before-change style): the elements of the inheritance chain are the same, with the same cascaded values and no
    /// animation other than the CSS transitions.
    /// </summary>
    internal bool HasSameComputedValuesAs(ComputedStyle other)
    {
        if (!Engine.BeforeChangeComparableByCascade)
            return false;

        for (ComputedStyle? a = this, b = other; ; a = a.Parent, b = b.Parent)
        {
            if (a is null || b is null)
                return a is null && b is null;

            // (the overlay of an element depends on whether it is in the top layer, not on its cascaded values)
            if (!ReferenceEquals(a.Element, b.Element) || a.PseudoElement != b.PseudoElement || a.HasEffectsInEffect() || b.HasEffectsInEffect() || !HaveSameCascadedValues(a._cascaded, b._cascaded)
                || a._hasOverlayAutoRule != b._hasOverlayAutoRule)
                return false;
        }
    }

    /// <summary>Whether an effect other than a CSS transition is in effect (the transitions are not part of the after-change style).</summary>
    private bool HasEffectsInEffect()
    {
        if (_effects is not { Count: > 0 } effects)
            return false;

        foreach (var effect in effects)
        {
            if (effect.Animation is not CssTransition && effect.IsInEffect)
                return true;
        }

        return false;
    }

    private static bool HaveSameCascadedValues(Dictionary<string, CascadedValue> a, Dictionary<string, CascadedValue> b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a.Count != b.Count)
            return false;

        foreach (var (name, value) in a)
        {
            if (!b.TryGetValue(name, out var other) || !value.Value.Equals(other.Value) || !Equals(value.Source, other.Source) || value.IsImportant != other.IsImportant)
                return false;

            // The values of attr() and of the tree-counting functions depend on the element, not only on the cascaded value
            if (value.Value.Value.Contains("attr(", StringComparison.OrdinalIgnoreCase) || value.Value.Value.Contains("sibling-", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private KeyframeEffect? GetTransitionEffect(string property)
    {
        if (_effects is not { Count: > 0 } effects)
            return null;

        KeyframeEffect? result = null;
        foreach (var effect in effects)
        {
            if (effect.Animation is not CssTransition transition || !Animates(effect, property) || effect.GetIterationProgress() is null)
                continue;

            if (result is null || transition.CompareCompositeOrder(result.Animation!) > 0)
            {
                result = effect;
            }
        }

        return result;
    }

    /// <summary>The properties of the keyframes of the effects (see <see cref="MayBeAnimated"/>), or null when they are not collected yet.</summary>
    private HashSet<string>? _keyframeProperties;

    /// <summary>The version of the animations of the document and the number of effects when <see cref="_keyframeProperties"/> was collected.</summary>
    private (int Version, int Count) _keyframePropertiesVersion;

    /// <summary>Whether a keyframe of the effects has a flow-relative longhand, whose physical property depends on the writing mode.</summary>
    private bool _keyframesHaveFlowRelativeLonghands;

    /// <summary>
    /// Returns whether a keyframe of the effects of the style may have a value of a property (a physical longhand or a custom
    /// property): the properties of all the keyframes are collected once, so the properties that no effect animates are not
    /// looked up in each effect.
    /// </summary>
    private bool MayBeAnimated(string property)
    {
        var effects = _effects!;
        var version = (Engine.Document.AnimationsOrNull?.Version ?? 0, effects.Count);
        if (_keyframeProperties is null || version != _keyframePropertiesVersion)
        {
            var properties = new HashSet<string>(StringComparer.Ordinal);
            var flowRelative = false;
            foreach (var effect in effects)
            {
                foreach (var keyframe in effect.Keyframes)
                {
                    properties.UnionWith(keyframe.Longhands.Keys);
                    flowRelative |= keyframe.HasFlowRelativeLonghands;
                }
            }

            _keyframeProperties = properties;
            _keyframesHaveFlowRelativeLonghands = flowRelative;
            _keyframePropertiesVersion = version;
        }

        return _keyframesHaveFlowRelativeLonghands || _keyframeProperties.Contains(property);
    }

    private bool Animates(KeyframeEffect effect, string property)
    {
        foreach (var keyframe in effect.Keyframes)
        {
            if (GetKeyframeValue(keyframe, property) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the value of a property in a keyframe: its value, or the value of the logical property that maps to it (a
    /// physical property wins over a logical property, https://drafts.csswg.org/web-animations-1/#calculating-computed-keyframes).
    /// </summary>
    private CssLonghandValue? GetKeyframeValue(ProcessedKeyframe keyframe, string property)
    {
        if (keyframe.Longhands.TryGetValue(property, out var value))
            return value;

        if (!keyframe.HasFlowRelativeLonghands || CssPropertyRegistry.IsCustomProperty(property))
            return null;

        foreach (var (name, longhand) in keyframe.Longhands)
        {
            if (LogicalProperties.IsFlowRelative(name) && LogicalProperties.GetPhysicalProperty(name, this) == property)
                return longhand;
        }

        return null;
    }

    /// <summary>Computes the value of a property in a keyframe: revert and revert-layer are the underlying value.</summary>
    private string? ComputeKeyframeValue(CssPropertyDefinition property, ProcessedKeyframe keyframe, CssLonghandValue value, string? underlying)
    {
        if (IsRevertKeyword(value.Value))
            return underlying;

        // A longhand of a pending-substitution shorthand keeps its name (it is looked up in the substituted shorthand)
        var declared = value.ShorthandName is null ? value with { Name = property.Name } : value;
        return ComputeCascadedValue(property, new CascadedValue(declared, 0, keyframe.Source), property.Inherited, rollbacks: 0);
    }

    private static bool IsRevertKeyword(string value) => InfraAscii.ToAsciiLowercase(value.Trim()) is "revert" or "revert-layer";

    /// <summary>
    /// Computes the effect value of a keyframe effect for a property (https://drafts.csswg.org/web-animations-1/#the-effect-value-of-a-keyframe-animation-effect):
    /// the property-specific keyframes, with neutral keyframes at the offsets 0 and 1 when they are missing, are interpolated at the
    /// iteration progress, after the composite operations with the underlying value.
    /// </summary>
    private string? ComputeEffectValue(KeyframeEffect effect, string property, string? underlying, CssInterpolationContext context, Func<ProcessedKeyframe, CssLonghandValue, string?> computeValue)
    {
        if (effect.GetIterationProgress() is not { } progress)
            return underlying;

        // The property-specific keyframes, with their computed values. The keyframes with a timeline offset are positioned by
        // their computed offset, possibly outside [0, 1], and ignored when it is not resolved
        // (https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes)
        var hasTimelineOffsets = effect.HasTimelineOffsets;
        if (hasTimelineOffsets)
        {
            effect.ResolveTimelineOffsets();
        }

        var keyframes = new List<(double Offset, CssEasing Easing, CompositeOperationOrAuto Composite, string? Value, bool Neutral)>();
        foreach (var keyframe in effect.Keyframes)
        {
            if (!double.IsNaN(keyframe.ComputedOffset) && GetKeyframeValue(keyframe, property) is { } declared)
            {
                keyframes.Add((keyframe.ComputedOffset, keyframe.EasingFunction, keyframe.Composite, computeValue(keyframe, declared), false));
            }
        }

        if (keyframes.Count == 0)
            return underlying;

        if (hasTimelineOffsets)
        {
            keyframes = [.. keyframes.OrderBy(k => k.Offset)];
        }

        // The neutral keyframes are only added when no keyframe is at or before 0 (at or after 1)
        if (!keyframes.Exists(k => k.Offset <= 0))
        {
            keyframes.Insert(0, (0, CssEasing.Linear, CompositeOperationOrAuto.Add, underlying, true));
        }

        if (!keyframes.Exists(k => k.Offset >= 1))
        {
            keyframes.Add((1, CssEasing.Linear, CompositeOperationOrAuto.Add, underlying, true));
        }

        // The interval endpoints
        int startIndex;
        int? endIndex;
        if (progress < 0 && keyframes.Count(k => k.Offset == 0) > 1)
        {
            startIndex = 0;
            endIndex = null;
        }
        else if (progress >= 1 && keyframes.Count(k => k.Offset == 1) > 1)
        {
            startIndex = keyframes.Count - 1;
            endIndex = null;
        }
        else
        {
            startIndex = keyframes.FindLastIndex(k => k.Offset <= progress && k.Offset < 1);
            if (startIndex < 0)
            {
                startIndex = Math.Max(0, keyframes.FindLastIndex(k => k.Offset == 0));
            }

            endIndex = startIndex + 1 < keyframes.Count ? startIndex + 1 : null;
        }

        var currentIteration = effect.GetComputedIteration();
        var finalValue = keyframes[^1].Value;
        var composited = false;
        var startValue = ApplyComposite(effect, property, keyframes[startIndex], underlying, finalValue, currentIteration, context, ref composited);
        if (endIndex is not { } end)
            return composited && startValue is not null ? Canonicalize(property, startValue) : startValue;

        var endValue = ApplyComposite(effect, property, keyframes[end], underlying, finalValue, currentIteration, context, ref composited);
        var startOffset = keyframes[startIndex].Offset;
        var endOffset = keyframes[end].Offset;
        var intervalDistance = (progress - startOffset) / (endOffset - startOffset);
        var transformedDistance = keyframes[startIndex].Easing.Evaluate(intervalDistance, effect.GetBeforeFlag());
        return Interpolate(property, startValue, endValue, transformedDistance, context, composited);
    }

    /// <summary>Applies the iteration composite operation and the composite operation of a keyframe to its value.</summary>
    private string? ApplyComposite(KeyframeEffect effect, string property, (double Offset, CssEasing Easing, CompositeOperationOrAuto Composite, string? Value, bool Neutral) keyframe, string? underlying, string? finalValue, double currentIteration, CssInterpolationContext context, ref bool composited)
    {
        if (keyframe.Neutral)
            return underlying;

        var value = keyframe.Value;
        if (value is null)
            return null;

        if (effect.IterationCompositeValue == IterationCompositeOperation.Accumulate && currentIteration > 0 && finalValue is not null)
        {
            // The accumulated value is not clamped to the range of the property until it is interpolated (e.g. an opacity of 1.2)
            value = CssInterpolation.AccumulateIterations(context, property, finalValue, currentIteration, value);
            composited = true;
        }

        var composite = keyframe.Composite switch
        {
            CompositeOperationOrAuto.Replace => CompositeOperation.Replace,
            CompositeOperationOrAuto.Add => CompositeOperation.Add,
            CompositeOperationOrAuto.Accumulate => CompositeOperation.Accumulate,
            _ => effect.Composite,
        };

        if (composite == CompositeOperation.Replace || underlying is null)
            return value;

        // The values out of the range of the property (e.g. the channels of the colors) are kept until they are interpolated
        composited = true;
        return composite == CompositeOperation.Add
            ? CssInterpolation.Add(context, property, underlying, value)
            : CssInterpolation.Accumulate(context, property, underlying, value);
    }

    private string? Interpolate(string property, string? from, string? to, double progress, CssInterpolationContext context, bool composited)
    {
        if (from is null || to is null)
        {
            var value = progress < 0.5 ? from : to;
            return composited && value is not null ? Canonicalize(property, value) : value;
        }

        var result = CssInterpolation.Prepare(context, property, from, to).Interpolate(progress);
        return !composited && (ReferenceEquals(result, from) || ReferenceEquals(result, to)) ? result : Canonicalize(property, result);
    }

    /// <summary>Computes a combined value again: e.g. a calc() is simplified (the values are already computed, so this is idempotent).</summary>
    private string Canonicalize(string property, string value)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return RegisteredProperties.GetValueOrDefault(property) is { Syntax.IsUniversal: false } registration ? registration.Syntax.Compute(this, value, baseUrl: null) ?? value : value;

        return CssPropertyRegistry.Get(property) is { } definition ? CssValueComputer.Compute(this, definition, value, baseUrl: null) : value;
    }
}
