using System.Globalization;
using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// Creates, updates and cancels the CSS animations of an element when its style is computed
/// (https://drafts.csswg.org/css-animations-1/#animations, https://drafts.csswg.org/css-animations-2/#animations).
/// </summary>
internal static class CssAnimationUpdater
{
    /// <summary>Whether a style may have CSS animations: they are declared, or the element already has some.</summary>
    public static bool MayHaveAnimations(ComputedStyle style)
    {
        if (style.PseudoElement is not (null or "before" or "after" or "marker" or Cascade.StyleEngine.SelectPicker or Cascade.StyleEngine.PickerIcon or Cascade.StyleEngine.Checkmark or "backdrop") && !Layout.ViewTransitionPseudoTree.IsViewTransitionPseudoElement(style.PseudoElement))
            return false;

        return style.HasCascadedValue("animation-name") || style.Element.NodeDocument.AnimationsOrNull?.GetCssAnimations(style.Element, style.PseudoElement) is { Count: > 0 };
    }

    /// <summary>
    /// Updates the CSS animations of the element of the style; returns whether they changed. When the layout may compute the
    /// style again (<paramref name="changes"/> is not null), the canceled animations are only pending cancellation, and the
    /// changes are recorded so that they can be undone (see <see cref="AnimationChanges"/>).
    /// </summary>
    public static bool Update(ComputedStyle style, AnimationChanges? changes)
    {
        var element = style.Element;
        var pseudoElement = style.PseudoElement;
        var document = element.NodeDocument;
        var existing = document.AnimationsOrNull?.GetCssAnimations(element, pseudoElement);
        var names = ParseNames(style.GetComputedValue("animation-name"));
        if (names.TrueForAll(name => name is null) && existing is not { Count: > 0 })
            return false;

        var changed = false;
        var unmatched = existing is null ? [] : new List<CssAnimation>(existing);
        var result = new List<CssAnimation>();

        // https://drafts.csswg.org/css-display-3/#valdef-display-none: an element whose display is none (or one of its
        // ancestors) has no animations
        if (!style.IsDisplayNoneForAnimations() && IsGenerated(style))
        {
            var durations = GetList(style, "animation-duration");
            var easings = GetEasingList(style, "animation-timing-function");
            var delays = GetList(style, "animation-delay");
            var iterationCounts = GetList(style, "animation-iteration-count");
            var directions = GetList(style, "animation-direction");
            var fillModes = GetList(style, "animation-fill-mode");
            var playStates = GetList(style, "animation-play-state");
            var compositions = GetList(style, "animation-composition");
            var timelines = GetList(style, "animation-timeline");
            var rangeStarts = GetList(style, "animation-range-start");
            var rangeEnds = GetList(style, "animation-range-end");

            // The lengths of animation-range are zoomed by the element (https://drafts.csswg.org/css-viewport/#zoom-property)
            var zoom = style.Layout.EffectiveZoom;
            var treeScope = style.TryGetCascadedValue("animation-name", out var cascaded) ? cascaded.TreeScope : null;
            var baseUrl = document.DocumentBaseUrl.Serialize();
            var animations = new CssAnimation?[names.Count];

            // The animations are matched from the end of the list (https://drafts.csswg.org/css-animations-2/#animations)
            for (var i = names.Count - 1; i >= 0; i--)
            {
                if (names[i] is not { } name || style.Engine.FindKeyframes(name, treeScope, Layout.ViewTransitionPseudoTree.IsViewTransitionPseudoElement(style.PseudoElement) ? style.Element : null) is not { } rule)
                    continue;

                var parameters = new AnimationParameters(
                    Rule: rule,
                    Duration: ParseTime(Item(durations, i)),
                    Easing: Item(easings, i),
                    Delay: ParseTime(Item(delays, i)) ?? 0,
                    Iterations: Item(iterationCounts, i) == "infinite" ? double.PositiveInfinity : ParseNumber(Item(iterationCounts, i)) ?? 1,
                    Direction: ParseDirection(Item(directions, i)),
                    Fill: ParseFill(Item(fillModes, i)),
                    Paused: Item(playStates, i) == "paused",
                    Composite: ParseComposite(Item(compositions, i)),
                    Timeline: Item(timelines, i) is "auto" ? CssTimelineReference.Auto : CssTimelineReference.Parse(Item(timelines, i)),
                    RangeStart: Item(rangeStarts, i) is "normal" ? null : TimelineRangeBoundary.Parse(Item(rangeStarts, i), isEnd: false)?.WithZoom(zoom),
                    RangeEnd: Item(rangeEnds, i) is "normal" ? null : TimelineRangeBoundary.Parse(Item(rangeEnds, i), isEnd: true)?.WithZoom(zoom),
                    BaseUrl: baseUrl);

                var index = unmatched.FindLastIndex(animation => animation.AnimationName == name);
                CssAnimation animation;
                if (index >= 0)
                {
                    animation = unmatched[index];
                    unmatched.RemoveAt(index);
                    changed |= UpdateAnimation(style, animation, i, parameters);
                }
                else
                {
                    animation = CreateAnimation(style, name, i, parameters);
                    changed = true;
                }

                animations[i] = animation;
            }

            foreach (var animation in animations)
            {
                if (animation is not null)
                {
                    result.Add(animation);
                }
            }
        }

        foreach (var animation in unmatched)
        {
            if (changes is null)
            {
                animation.CancelByStyle();
            }
            else
            {
                animation.IsCancellationPending = true;
                (changes.PendingCancellations ??= []).Add(animation);
            }

            changed = true;
        }

        if (existing is not null || result.Count > 0)
        {
            document.Animations.SetCssAnimations(element, pseudoElement, result);
        }

        return changed;
    }

    /// <summary>
    /// Whether the pseudo-element of a style exists: the pseudo-elements of a view transition only exist in the tree of the active
    /// view transition (https://drafts.csswg.org/css-view-transitions-1/#view-transition-pseudo-elements); the styles computed for
    /// the other ones (e.g. by getComputedStyle()) have no animations.
    /// </summary>
    internal static bool IsGenerated(ComputedStyle style)
        => style.PseudoElement is not { } pseudoElement || !Layout.ViewTransitionPseudoTree.IsViewTransitionPseudoElement(pseudoElement)
            || style.Element.NodeDocument.GetViewTransitionPseudoTree(style.Element)?.Contains(pseudoElement) == true;

    private sealed record AnimationParameters(Om.CssKeyframesRule Rule, double? Duration, string Easing, double Delay, double Iterations, PlaybackDirection Direction, FillMode Fill, bool Paused, CompositeOperation Composite, CssTimelineReference Timeline, TimelineRangeBoundary? RangeStart, TimelineRangeBoundary? RangeEnd, string BaseUrl);

    /// <summary>
    /// Returns the timeline of an item of <c>animation-timeline</c> (https://drafts.csswg.org/css-animations-2/#animation-timeline):
    /// the document timeline for <c>auto</c>, no timeline for <c>none</c>, a new anonymous timeline for <c>scroll()</c> and
    /// <c>view()</c> (the timeline of the animation is kept while its parameters do not change), or the target timeline of a
    /// name (https://drafts.csswg.org/scroll-animations-1/#target-timeline), an inactive timeline when no timeline matches it.
    /// When the scroll-driven animations are disabled, their timelines are no timeline.
    /// </summary>
    private static AnimationTimeline? ResolveTimeline(ComputedStyle style, CssTimelineReference reference, CssAnimation? animation)
    {
        var element = style.Element;
        var document = element.NodeDocument;
        if (reference.Kind == CssTimelineKind.Auto)
            return document.Timeline;

        if (reference.Kind == CssTimelineKind.None || !ScrollTimeline.IsEnabled(document))
            return null;

        if (reference.Kind == CssTimelineKind.Named)
        {
            var timelines = document.Animations.CssTimelines;
            timelines.HasNamedReferences = true;
            var treeScope = style.TryGetCascadedValue("animation-timeline", out var cascaded) ? cascaded.TreeScope : null;
            if (animation is not null)
            {
                animation.TimelineTreeScope = treeScope;
            }

            return timelines.Find(element, reference.Name!, treeScope) ?? timelines.InactiveTimeline;
        }

        // The anonymous timeline of the animation is kept when its parameters do not change
        if (animation is { TimelineReference: { } previous, TimelineValue: ScrollTimeline existing } && previous == reference && ReferenceEquals(existing.Reference, element))
            return existing;

        return reference.Kind == CssTimelineKind.View
            ? ViewTimeline.Create(element, style.PseudoElement, reference.Axis, (reference.InsetStart, reference.InsetEnd))
            : ScrollTimeline.Create(element, reference.Scroller == ScrollTimelineScroller.Self ? null : style.PseudoElement, reference.Scroller, reference.Axis);
    }

    private static CssAnimation CreateAnimation(ComputedStyle style, string name, int index, AnimationParameters parameters)
    {
        var effect = new KeyframeEffect(style.Element, style.PseudoElement);
        ApplyTiming(effect, null, parameters);
        effect.CompositeValue = parameters.Composite;
        effect.SetProcessedKeyframes(CssKeyframesResolver.Resolve(parameters.Rule, parameters.Easing, CompositeOperationOrAuto.Auto, parameters.BaseUrl));
        var animation = new CssAnimation(style.Element, style.PseudoElement, name, effect, ResolveTimeline(style, parameters.Timeline, animation: null))
        {
            NameIndex = index,
            KeyframesRule = parameters.Rule,
            KeyframesCacheKey = (CssStyleVersion.Current, parameters.Easing),
            StylePaused = parameters.Paused,
            TimelineReference = parameters.Timeline,
            TimelineTreeScope = style.TryGetCascadedValue("animation-timeline", out var timelineCascaded) ? timelineCascaded.TreeScope : null,
        };

        animation.SetRangeByStyle(parameters.RangeStart, parameters.RangeEnd);

        animation.PlayByStyle(autoRewind: true);
        if (parameters.Paused)
        {
            animation.PauseByStyle();
        }

        return animation;
    }

    private static bool UpdateAnimation(ComputedStyle style, CssAnimation animation, int index, AnimationParameters parameters)
    {
        var changed = false;
        animation.NameIndex = index;
        if (animation.EffectValue is KeyframeEffect effect)
        {
            if (!animation.KeyframesOverridden)
            {
                var cacheKey = (CssStyleVersion.Current, parameters.Easing);
                if (!ReferenceEquals(animation.KeyframesRule, parameters.Rule) || !Equals(animation.KeyframesCacheKey, cacheKey))
                {
                    animation.KeyframesRule = parameters.Rule;
                    animation.KeyframesCacheKey = cacheKey;
                    effect.SetProcessedKeyframes(CssKeyframesResolver.Resolve(parameters.Rule, parameters.Easing, CompositeOperationOrAuto.Auto, parameters.BaseUrl));
                    changed = true;
                }

                if (effect.CompositeValue != parameters.Composite)
                {
                    effect.CompositeValue = parameters.Composite;
                    animation.OnEffectChanged();
                    changed = true;
                }
            }

            if (ApplyTiming(effect, animation, parameters))
            {
                animation.OnEffectChanged();
                changed = true;
            }
        }

        if (!animation.TimelineOverridden)
        {
            var timeline = ResolveTimeline(style, parameters.Timeline, animation);
            animation.TimelineReference = parameters.Timeline;
            if (!ReferenceEquals(animation.TimelineValue, timeline))
            {
                animation.SetTimelineByStyle(timeline);
                changed = true;
            }
        }

        if (!Equals(animation.RangeStartValue, animation.RangeStartOverridden ? animation.RangeStartValue : parameters.RangeStart)
            || !Equals(animation.RangeEndValue, animation.RangeEndOverridden ? animation.RangeEndValue : parameters.RangeEnd))
        {
            animation.SetRangeByStyle(parameters.RangeStart, parameters.RangeEnd);
            changed = true;
        }

        // https://drafts.csswg.org/css-animations-1/#animation-play-state: a change of the play state pauses or resumes the animation
        if (!animation.PlayStateOverridden && animation.StylePaused != parameters.Paused)
        {
            if (parameters.Paused)
            {
                animation.PauseByStyle();
            }
            else if (animation.PlayStateValue == AnimationPlayState.Paused)
            {
                animation.PlayByStyle(autoRewind: false);
            }

            changed = true;
        }

        animation.StylePaused = parameters.Paused;
        return changed;
    }

    /// <summary>Applies the timing of the animation to its effect, except the properties changed through updateTiming(); returns whether it changed.</summary>
    private static bool ApplyTiming(KeyframeEffect effect, CssAnimation? animation, AnimationParameters parameters)
    {
        var changed = false;
        void Set<T>(string property, T current, T value, Action<T> setter)
        {
            if (animation?.IsTimingOverridden(property) == true || EqualityComparer<T>.Default.Equals(current, value))
                return;

            setter(value);
            changed = true;
        }

        Set("duration", effect.Duration, parameters.Duration, value => effect.Duration = value);
        Set("delay", effect.Delay, parameters.Delay, value => effect.Delay = value);
        Set("iterations", effect.Iterations, parameters.Iterations, value => effect.Iterations = value);
        Set("direction", effect.Direction, parameters.Direction, value => effect.Direction = value);
        Set("fill", effect.Fill, parameters.Fill, value => effect.Fill = value);
        return changed;
    }

    /// <summary>Parses the computed value of animation-name: the names, null for none.</summary>
    private static List<string?> ParseNames(string value)
    {
        var result = new List<string?>();
        if (value == "none")
        {
            result.Add(null);
            return result;
        }

        foreach (var item in ValueMatcher.SplitAtCommas(CssGenericInterpolation.Parse(value)))
        {
            result.Add(item switch
            {
                [CssPreservedToken { Type: CssTokenType.Ident } ident] when !InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "none") => ident.Token.Value,
                [CssPreservedToken { Type: CssTokenType.String } text] => text.Token.Value,
                _ => null,
            });
        }

        return result;
    }

    private static List<string> GetList(ComputedStyle style, string property)
    {
        var value = style.GetComputedValue(property);
        if (!value.Contains(',', StringComparison.Ordinal))
            return [value];

        var result = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(CssGenericInterpolation.Parse(value)))
        {
            result.Add(CssSerializer.SerializeValue(item, property: null));
        }

        return result;
    }

    /// <summary>
    /// Returns the easing functions of an animation or transition property with the full precision of their numbers (the
    /// computed value rounds them): the cascaded value when it is the specified value.
    /// </summary>
    internal static List<string> GetEasingList(ComputedStyle style, string property)
    {
        if (style.TryGetCascadedValue(property, out var cascaded) && cascaded.Value is { ShorthandName: null, ComputationValue: { } precise })
            return SplitList(precise);

        return GetList(style, property);
    }

    private static List<string> SplitList(string value)
    {
        var result = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(CssGenericInterpolation.Parse(value)))
        {
            result.Add(CssSerializer.WithPreservedNumbers(() => CssSerializer.SerializeValue(item, property: null)));
        }

        return result;
    }

    private static string Item(List<string> list, int index) => list[index % list.Count];

    /// <summary>Parses a computed time (e.g. 1.5s) to milliseconds; auto (the duration of a document timeline animation) is null.</summary>
    private static double? ParseTime(string value)
    {
        if (value.EndsWith("ms", StringComparison.Ordinal) && ParseNumber(value[..^2]) is { } milliseconds)
            return milliseconds;

        if (value.EndsWith('s', StringComparison.Ordinal) && ParseNumber(value[..^1]) is { } seconds)
            return seconds * 1000;

        return null;
    }

    private static double? ParseNumber(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static PlaybackDirection ParseDirection(string value) => value switch
    {
        "reverse" => PlaybackDirection.Reverse,
        "alternate" => PlaybackDirection.Alternate,
        "alternate-reverse" => PlaybackDirection.AlternateReverse,
        _ => PlaybackDirection.Normal,
    };

    private static FillMode ParseFill(string value) => value switch
    {
        "forwards" => FillMode.Forwards,
        "backwards" => FillMode.Backwards,
        "both" => FillMode.Both,
        _ => FillMode.None,
    };

    private static CompositeOperation ParseComposite(string value) => value switch
    {
        "add" => CompositeOperation.Add,
        "accumulate" => CompositeOperation.Accumulate,
        _ => CompositeOperation.Replace,
    };
}

/// <summary>
/// The changes of the CSS animations of an element by the computations of its style in a generation of styles that the layout
/// may compute again with the size of its query container (see <see cref="StyleEngine.InvalidateQueryContainerDescendants"/>):
/// the layout may lay out the container more than once (e.g. to measure it), and a discarded style must not have changed the
/// animations. The animations it created are canceled, and the ones it canceled run again: they are only canceled once the
/// styles are settled (see <see cref="StyleEngine.CommitPendingCancellations"/>).
/// </summary>
/// <param name="animations">The CSS animations of the element before the first computation.</param>
internal sealed class AnimationChanges(List<CssAnimation>? animations)
{
    public List<CssAnimation>? Animations { get; } = animations;

    /// <summary>The CSS animations canceled by the computations, whose cancellation is pending (see <see cref="Animation.IsCancellationPending"/>).</summary>
    public List<CssAnimation>? PendingCancellations { get; set; }
}
