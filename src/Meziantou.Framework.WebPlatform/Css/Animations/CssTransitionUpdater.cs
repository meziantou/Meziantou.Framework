using System.Globalization;
using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// Starts, reverses and cancels the CSS transitions of an element when its style changes
/// (https://drafts.csswg.org/css-transitions-1/#starting, https://drafts.csswg.org/css-transitions-2/#starting): the
/// before-change style is the style of the element before the change, and the after-change style is the new style without
/// the transitions.
/// </summary>
internal static class CssTransitionUpdater
{
    private sealed record TransitionParameters(double Duration, double Delay, string Easing, bool AllowDiscrete)
    {
        public double CombinedDuration => Math.Max(Duration, 0) + Delay;
    }

    /// <summary>Whether a style may have transitions: the transition properties are declared, or the element has transitions.</summary>
    public static bool MayHaveTransitions(ComputedStyle style)
    {
        if (style.PseudoElement is not (null or "before" or "after" or "marker" or Cascade.StyleEngine.SelectPicker or Cascade.StyleEngine.PickerIcon or Cascade.StyleEngine.Checkmark or "backdrop") && !(Layout.ViewTransitionPseudoTree.IsViewTransitionPseudoElement(style.PseudoElement) && CssAnimationUpdater.IsGenerated(style)))
            return false;

        return style.HasCascadedValue("transition-duration") || style.HasCascadedValue("transition-delay")
            || style.Element.NodeDocument.AnimationsOrNull?.GetTransitions(style.Element, style.PseudoElement) is not null;
    }

    private static bool IsDisplayTransitioningToNone(ComputedStyle style, ComputedStyle? before, Dictionary<string, TransitionParameters> matching, ElementTransitions? transitions)
    {
        for (var parent = style.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.GetComputedValue("display") == "none")
                return false;
        }

        if (transitions is not null && transitions.Running.ContainsKey("display"))
            return true;

        return matching.TryGetValue("display", out var parameters) && parameters is { AllowDiscrete: true, CombinedDuration: > 0 }
            && before is not null && !before.IsDisplayNoneForAnimations();
    }

    /// <summary>
    /// Updates the transitions of the element of the style after a style change; returns whether they changed. When the layout
    /// may compute the style again (<paramref name="changes"/> is not null), the canceled transitions are only pending
    /// cancellation, and the changes are recorded so that they can be undone (see <see cref="TransitionChanges"/>).
    /// </summary>
    /// <param name="style">The new style of the element.</param>
    /// <param name="before">The before-change style, or null.</param>
    /// <param name="changes">The changes recorded for the layout, or null.</param>
    /// <param name="afterChange">The after-change style, or null for the after-change style of <paramref name="style"/>.</param>
    public static bool Update(ComputedStyle style, ComputedStyle? before, TransitionChanges? changes, ComputedStyle? afterChange = null)
    {
        var element = style.Element;
        var pseudoElement = style.PseudoElement;
        var document = element.NodeDocument;
        var transitions = document.AnimationsOrNull?.GetTransitions(element, pseudoElement);
        if (before is null && transitions is null)
            return false;

        // Nothing changed: no transition starts, and the running transitions keep their end value (the computation of the
        // values of all the properties is skipped)
        if (before is not null && style.HasSameComputedValuesAs(before))
            return false;

        // https://drafts.csswg.org/css-transitions-2/#transition-property: an element that is not rendered has no transitions
        // https://drafts.csswg.org/css-transitions-2/#transition-property: an element that is not rendered has no transitions,
        // except when its display transitions to none (transition-behavior: allow-discrete), which keeps it rendered
        var matching = GetMatchingTransitions(style);
        if (style.IsDisplayNoneForAnimations() && !IsDisplayTransitioningToNone(style, before, matching, transitions))
        {
            matching = [];
        }

        if (matching.Count == 0 && transitions is null)
            return false;

        var after = afterChange ?? style.CreateAfterChangeStyle();
        var context = new CssInterpolationContext(after);
        var candidates = new SortedSet<string>(matching.Keys, StringComparer.Ordinal);
        if (transitions is not null)
        {
            candidates.UnionWith(transitions.Running.Keys);
            candidates.UnionWith(transitions.Completed.Keys);
        }

        var changed = false;
        foreach (var property in candidates)
        {
            var afterValue = GetValue(after, property);
            var parameters = matching.GetValueOrDefault(property);
            CssTransition? running = null;
            string? completed = null;
            if (transitions is not null)
            {
                // A running transition that reached its end is completed
                if (transitions.Running.TryGetValue(property, out running) && running.PlayStateValue == AnimationPlayState.Finished)
                {
                    transitions.Running.Remove(property);
                    transitions.Completed[property] = running.EndValue;
                    running = null;
                }

                completed = transitions.Completed.GetValueOrDefault(property);
            }

            // Step 1: start a transition
            if (running is null && before is not null && parameters is { CombinedDuration: > 0 } && !style.IsAnimatedByAnimations(property) && !IsInheritedFromTransition(style, before, property))
            {
                var beforeValue = GetValue(before, property);
                if ((!string.Equals(beforeValue, afterValue, StringComparison.Ordinal) || HasTreeScopedNameChanged(before, after, property, afterValue)) && (completed is null || !string.Equals(completed, afterValue, StringComparison.Ordinal))
                    && IsTransitionable(context, property, beforeValue, afterValue, parameters.AllowDiscrete))
                {
                    transitions ??= document.Animations.GetOrCreateTransitions(element, pseudoElement);
                    RemoveCompleted(transitions, property, changes);
                    transitions.Running[property] = StartTransition(style, property, beforeValue, afterValue, parameters, beforeValue, reversingShorteningFactor: 1, IsTainted(before, property), IsTainted(after, property));
                    changed = true;
                    continue;
                }
            }

            // Step 2: a completed transition whose end value is not the after-change value is removed
            if (completed is not null && !string.Equals(completed, afterValue, StringComparison.Ordinal))
            {
                RemoveCompleted(transitions!, property, changes);
            }

            // Step 3: the transitions of a property that no longer has a matching transition-property are canceled
            if (parameters is null)
            {
                if (running is not null)
                {
                    Cancel(transitions!, running, changes);
                    changed = true;
                }

                if (transitions is not null)
                {
                    RemoveCompleted(transitions, property, changes);
                }

                continue;
            }

            // Step 4: a running transition whose end value changed is canceled, reversed or replaced (the end value of a
            // transition started by the layout is a value that the layout resolved: the layout compares it, see UpdateFromLayout)
            if (running is not null && !running.IsStartedByLayout && !string.Equals(running.EndValue, afterValue, StringComparison.Ordinal))
            {
                ReplaceRunningTransition(style, context, transitions!, running, afterValue, parameters, changes, IsTainted(after, property), startedByLayout: false);
                changed = true;
            }
        }

        if (transitions is not null)
        {
            document.Animations.RemoveTransitionsIfEmpty(element, pseudoElement);
        }

        return changed;
    }

    /// <summary>
    /// Updates the transitions of an anchor-positioned box after its layout (https://github.com/w3c/csswg-drafts/issues/9598): the
    /// anchor functions and the declarations of the chosen position option are resolved at computed-value time, with the style
    /// and layout interleaving, so the transitions start when their results change, even when the declared values do not (the
    /// anchor box moved, the anchor name designates another element, another position option applies...). The layout resolves
    /// them: <paramref name="before"/> are the values resolved by the previous layout, <paramref name="after"/> the values of the
    /// after-change style resolved by this layout. A running transition started by a style change is kept (its end value is the
    /// declared value). Returns whether the transitions changed.
    /// </summary>
    public static bool UpdateFromLayout(ComputedStyle style, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        if (style.IsDisplayNoneForAnimations())
            return false;

        var matching = GetMatchingTransitions(style);
        if (matching.Count == 0)
            return false;

        var element = style.Element;
        var pseudoElement = style.PseudoElement;
        var document = element.NodeDocument;
        var transitions = document.AnimationsOrNull?.GetTransitions(element, pseudoElement);
        var context = new CssInterpolationContext(style.CreateAfterChangeStyle());
        ComputedStyle? currentStyle = null;
        var changed = false;
        foreach (var (property, afterValue) in after)
        {
            if (!before.TryGetValue(property, out var beforeValue) || !matching.TryGetValue(property, out var parameters) || parameters.CombinedDuration <= 0 || style.IsAnimatedByAnimations(property))
                continue;

            if (transitions is not null && transitions.Running.TryGetValue(property, out var running))
            {
                if (running.PlayStateValue == AnimationPlayState.Finished)
                {
                    transitions.Running.Remove(property);
                    transitions.Completed[property] = running.EndValue;
                }
                else
                {
                    if (running.IsStartedByLayout && !string.Equals(running.EndValue, afterValue, StringComparison.Ordinal))
                    {
                        ReplaceRunningTransition(currentStyle ??= style.WithCurrentAnimations(includeNewAnimations: true), context, transitions, running, afterValue, parameters, changes: null, endTainted: false, startedByLayout: true);
                        changed = true;
                    }

                    continue;
                }
            }

            if (string.Equals(beforeValue, afterValue, StringComparison.Ordinal) || IsInheritedFromTransition(style, style, property)
                || (transitions?.Completed.GetValueOrDefault(property) is { } completed && string.Equals(completed, afterValue, StringComparison.Ordinal))
                || !IsTransitionable(context, property, beforeValue, afterValue, parameters.AllowDiscrete))
            {
                continue;
            }

            transitions ??= document.Animations.GetOrCreateTransitions(element, pseudoElement);
            RemoveCompleted(transitions, property, changes: null);
            transitions.Running[property] = StartTransition(style, property, beforeValue, afterValue, parameters, beforeValue, reversingShorteningFactor: 1, startedByLayout: true);
            changed = true;
        }

        if (transitions is not null)
        {
            document.Animations.RemoveTransitionsIfEmpty(element, pseudoElement);
        }

        return changed;
    }

    /// <summary>Cancels a running transition whose end value changed, and starts a transition from its current value to the new end value (reversed when it goes back to its start value).</summary>
    private static void ReplaceRunningTransition(ComputedStyle style, CssInterpolationContext context, ElementTransitions transitions, CssTransition running, string afterValue, TransitionParameters parameters, TransitionChanges? changes, bool endTainted, bool startedByLayout)
    {
        var property = running.TransitionProperty;
        var currentValue = GetValue(style, property);
        var output = GetTimingFunctionOutput(running);
        Cancel(transitions, running, changes);
        if (string.Equals(currentValue, afterValue, StringComparison.Ordinal) || parameters.CombinedDuration <= 0 || !IsTransitionable(context, property, currentValue, afterValue, parameters.AllowDiscrete))
            return;

        if (string.Equals(running.ReversingAdjustedStartValue, afterValue, StringComparison.Ordinal))
        {
            // https://drafts.csswg.org/css-transitions-1/#reversing: the duration of the reversed transition is shortened
            var factor = Math.Clamp(Math.Abs((output * running.ReversingShorteningFactor) + 1 - running.ReversingShorteningFactor), 0, 1);
            var shortened = parameters with
            {
                Duration = parameters.Duration * factor,
                Delay = parameters.Delay < 0 ? parameters.Delay * factor : parameters.Delay,
            };

            transitions.Running[property] = StartTransition(style, property, currentValue, afterValue, shortened, running.EndValue, factor, IsTainted(style, property), endTainted, startedByLayout);
        }
        else
        {
            transitions.Running[property] = StartTransition(style, property, currentValue, afterValue, parameters, currentValue, reversingShorteningFactor: 1, IsTainted(style, property), endTainted, startedByLayout);
        }
    }

    /// <summary>Cancels a running transition, or makes its cancellation pending when the changes are recorded.</summary>
    private static void Cancel(ElementTransitions transitions, CssTransition running, TransitionChanges? changes)
    {
        transitions.Running.Remove(running.TransitionProperty);
        if (changes is null)
        {
            running.CancelByStyle();
        }
        else
        {
            running.IsCancellationPending = true;
            (changes.PendingCancellations ??= []).Add(running);
        }
    }

    private static void RemoveCompleted(ElementTransitions transitions, string property, TransitionChanges? changes)
    {
        if (transitions.Completed.Remove(property, out var value) && changes is not null)
        {
            (changes.RemovedCompleted ??= new(StringComparer.Ordinal)).TryAdd(property, value);
        }
    }

    /// <summary>
    /// Returns the matching transition parameters of the properties (https://drafts.csswg.org/css-transitions-1/#matching-transition-property-value):
    /// the last item of transition-property that names a property (all, a shorthand of the property, or the property) wins.
    /// </summary>
    private static Dictionary<string, TransitionParameters> GetMatchingTransitions(ComputedStyle style)
    {
        var result = new Dictionary<string, TransitionParameters>(StringComparer.Ordinal);
        var properties = style.GetComputedValue("transition-property");
        if (properties == "none")
            return result;

        var names = SplitList(properties);
        var durations = SplitList(style.GetComputedValue("transition-duration"));
        var delays = SplitList(style.GetComputedValue("transition-delay"));
        var easings = CssAnimationUpdater.GetEasingList(style, "transition-timing-function");
        var behaviors = SplitList(style.GetComputedValue("transition-behavior"));
        for (var i = 0; i < names.Count; i++)
        {
            var parameters = new TransitionParameters(
                Duration: ParseTime(durations[i % durations.Count]),
                Delay: ParseTime(delays[i % delays.Count]),
                Easing: easings[i % easings.Count],
                AllowDiscrete: behaviors[i % behaviors.Count] == "allow-discrete");

            var name = names[i];
            if (name == "all")
            {
                foreach (var longhand in CssInterpolation.AnimatableLonghands)
                {
                    result[longhand] = parameters;
                }
            }
            else if (CssPropertyRegistry.IsCustomProperty(name))
            {
                result[name] = parameters;
            }
            else if (CssPropertyRegistry.Get(InfraAscii.ToAsciiLowercase(name)) is { } definition)
            {
                if (definition.IsShorthand)
                {
                    foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(definition))
                    {
                        if (CssInterpolation.IsAnimatable(longhand))
                        {
                            result[longhand] = parameters;
                        }
                    }
                }
                else if (CssInterpolation.IsAnimatable(definition.Name))
                {
                    result[definition.Name] = parameters;
                }
            }
        }

        // The logical properties are transitioned as the physical properties they map to
        foreach (var name in result.Keys.ToArray())
        {
            if (LogicalProperties.GetPhysicalProperty(name, style) is { } physical)
            {
                if (!result.ContainsKey(physical))
                {
                    result[physical] = result[name];
                }

                result.Remove(name);
            }
        }

        return result;
    }

    private static CssTransition StartTransition(ComputedStyle style, string property, string startValue, string endValue, TransitionParameters parameters, string reversingAdjustedStartValue, double reversingShorteningFactor, bool startTainted = false, bool endTainted = false, bool startedByLayout = false)
    {
        var element = style.Element;
        var document = element.NodeDocument;
        // The timing function is the easing of the effect (https://drafts.csswg.org/css-transitions-2/#csstransition)
        var effect = new KeyframeEffect(element, style.PseudoElement)
        {
            Duration = Math.Max(parameters.Duration, 0),
            Delay = parameters.Delay,
            Fill = FillMode.Backwards,
            EasingFunction = CssEasing.Parse(parameters.Easing) ?? CssEasing.Linear,
            Easing = CssPropertyRules.SerializeEasingFunction(parameters.Easing) ?? parameters.Easing,
        };

        effect.SetProcessedKeyframes(
        [
            new ProcessedKeyframe
            {
                Offset = 0,
                ComputedOffset = 0,
                IsAttrTainted = startTainted,
                Properties = { new(property, startValue) },
                Longhands = { [property] = new CssLonghandValue(property, startValue) },
            },
            new ProcessedKeyframe
            {
                Offset = 1,
                ComputedOffset = 1,
                IsAttrTainted = endTainted,
                Properties = { new(property, endValue) },
                Longhands = { [property] = new CssLonghandValue(property, endValue) },
            },
        ]);

        var transition = new CssTransition(element, style.PseudoElement, property, effect, document.Timeline)
        {
            StartValue = startValue,
            EndValue = endValue,
            ReversingAdjustedStartValue = reversingAdjustedStartValue,
            ReversingShorteningFactor = reversingShorteningFactor,
            IsStartedByLayout = startedByLayout,
        };

        transition.PlayByStyle();
        return transition;
    }

    /// <summary>
    /// Returns whether the value of a property is inherited from an ancestor that transitions the property: a transition does
    /// not start when the value changes because it is inherited from a transitioning element (https://drafts.csswg.org/css-transitions-1/#starting).
    /// When the element did not inherit the value before the change (e.g. its declaration changed from a color to inherit), the
    /// transition starts, from its before-change value to the inherited after-change value (WPT css/css-transitions/after-change-style-inherited.html).
    /// </summary>
    private static bool IsInheritedFromTransition(ComputedStyle style, ComputedStyle before, string property)
    {
        if (!Inherits(style, property) || !Inherits(before, property))
            return false;

        for (var parent = style.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Element.NodeDocument.AnimationsOrNull?.GetTransitions(parent.Element, parent.PseudoElement)?.Running.ContainsKey(property) == true)
                return true;

            if (parent.TryGetCascadedValue(property, out var parentCascaded) && parentCascaded.Value.Value != "inherit")
                return false;
        }

        return false;

        // The value is inherited: explicitly, or by default for an inherited property
        static bool Inherits(ComputedStyle style, string property)
        {
            if (style.TryGetCascadedValue(property, out var cascaded))
                return cascaded.Value.Value == "inherit";

            return CssPropertyRegistry.IsCustomProperty(property) || CssPropertyRegistry.Get(property) is { Inherited: true };
        }
    }

    /// <summary>Returns the output of the timing function of a transition at its current time (https://drafts.csswg.org/css-transitions-1/#reversing).</summary>
    private static double GetTimingFunctionOutput(CssTransition transition)
    {
        return transition.EffectValue is KeyframeEffect effect && effect.GetIterationProgress() is { } progress ? progress : 0;
    }

    private static bool IsTransitionable(CssInterpolationContext context, string property, string from, string to, bool allowDiscrete)
    {
        if (!CssInterpolation.IsAnimatable(property, context.Style))
            return false;

        return allowDiscrete || !CssInterpolation.Prepare(context, property, from, to).IsDiscrete;
    }

    /// <summary>Whether the value of a custom property is attr()-tainted (https://drafts.csswg.org/css-values-5/#attr-security): so are the values of its transition.</summary>
    private static bool IsTainted(ComputedStyle style, string property) => CssPropertyRegistry.IsCustomProperty(property) && style.IsCustomPropertyTainted(property);

    /// <summary>
    /// Whether the counter style name of <c>list-style-type</c> is declared in another tree: the computed value of an
    /// author-defined name is a tree-scoped reference (https://drafts.csswg.org/css-scoping/#shadow-names), the same name in
    /// another tree is another value. The names of the predefined counter styles are keywords.
    /// </summary>
    private static bool HasTreeScopedNameChanged(ComputedStyle before, ComputedStyle after, string property, string value)
    {
        if (property != "list-style-type" || value is "none" or "" || value[0] is '"' or '\'' || CounterStyle.Predefined.ContainsKey(CounterStyle.NormalizeName(value)))
            return false;

        return !ReferenceEquals(GetTreeScope(before, property), GetTreeScope(after, property));

        // (an inherited value keeps the tree of its declaration)
        static Dom.Node? GetTreeScope(ComputedStyle style, string property)
        {
            for (var current = style; current is not null; current = current.Parent)
            {
                if (current.TryGetCascadedValue(property, out var cascaded) && cascaded.Value.Value.Trim() is not ("inherit" or "unset"))
                    return cascaded.TreeScope;
            }

            return null;
        }
    }

    private static string GetValue(ComputedStyle style, string property) => CssPropertyRegistry.IsCustomProperty(property) ? style.GetCustomPropertyValue(property) ?? "" : style.GetComputedValue(property);

    private static List<string> SplitList(string value)
    {
        if (!value.Contains(',', StringComparison.Ordinal))
            return [value];

        var result = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(CssGenericInterpolation.Parse(value)))
        {
            result.Add(CssSerializer.SerializeValue(item, property: null));
        }

        return result;
    }

    private static double ParseTime(string value)
    {
        if (value.EndsWith("ms", StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
            return milliseconds;

        if (value.EndsWith('s', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return seconds * 1000;

        return 0;
    }
}

/// <summary>
/// The changes of the transitions of an element by the computations of its style in a generation of styles that the layout may
/// compute again with the size of its query container (see <see cref="StyleEngine.InvalidateQueryContainerDescendants"/>): the
/// layout may lay out the container more than once (e.g. to measure it), and a discarded style must not have changed the
/// transitions. The transitions it started are canceled, and the ones it canceled run again: they are only canceled once the
/// styles are settled (see <see cref="StyleEngine.CommitPendingCancellations"/>).
/// </summary>
/// <param name="generation">The last transition generation before the first computation: the transitions started by the computations have a greater generation.</param>
internal sealed class TransitionChanges(long generation)
{
    public long Generation { get; } = generation;

    /// <summary>The running transitions canceled by the computations, whose cancellation is pending (see <see cref="Animation.IsCancellationPending"/>).</summary>
    public List<CssTransition>? PendingCancellations { get; set; }

    /// <summary>The end values of the completed transitions removed by the computations, by property.</summary>
    public Dictionary<string, string>? RemovedCompleted { get; set; }
}
