using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The effects of the anchor positioning on the layout of an absolutely positioned box in one physical axis.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AnchorAxisParameters
{
    /// <summary>Whether the used value of the <c>auto</c> insets and margins is 0 (<c>position-area</c>, <c>anchor-center</c>).</summary>
    public bool ZeroAutoInsets { get; init; }

    /// <summary>Whether <c>position-area</c> applies (its default alignment replaces <c>normal</c>, https://drafts.csswg.org/css-anchor-position-1/#position-area-alignment).</summary>
    public bool HasPositionArea { get; init; }

    /// <summary>The side the box is aligned to by the default alignment of <c>position-area</c>, or null for <c>center</c> and <c>anchor-center</c>.</summary>
    public PhysicalSide? AreaSide { get; init; }

    /// <summary>Whether the default alignment of <c>position-area</c> is <c>anchor-center</c> (all the tracks are selected).</summary>
    public bool AreaAnchorCenter { get; init; }

    /// <summary>The coordinate of the center of the default anchor box (in the coordinates of the host), or null when there is no default anchor box.</summary>
    public double? AnchorCenter { get; init; }

    /// <summary>The low bound of the overflow limit rect (the original containing block), or null for the default one.</summary>
    public double? LimitLow { get; init; }

    /// <summary>The high bound of the overflow limit rect.</summary>
    public double? LimitHigh { get; init; }
}

/// <summary>The effects of the anchor positioning on the layout of an absolutely positioned box (see <see cref="PositionedLayout.LayoutInContainingBlock"/>).</summary>
internal sealed class AnchorLayoutParameters
{
    public AnchorAxisParameters Horizontal { get; init; }

    public AnchorAxisParameters Vertical { get; init; }
}

/// <summary>The fragment of an absolutely positioned box and its inset-modified containing block (in the coordinates of its host).</summary>
/// <param name="Fragment">The fragment (null when only the inset-modified containing block is measured).</param>
/// <param name="InsetModifiedContainingBlock">The inset-modified containing block.</param>
/// <param name="IsCorrected">Whether the inset-modified containing block had a negative size in an axis, corrected to zero.</param>
internal readonly record struct AbsoluteLayoutResult(BoxFragment Fragment, PhysicalRect InsetModifiedContainingBlock, bool IsCorrected);

/// <summary>
/// CSS Anchor Positioning (https://drafts.csswg.org/css-anchor-position-1/): the layout of an absolutely positioned box relative to
/// its anchor boxes. The anchor functions (<c>anchor()</c>, <c>anchor-size()</c>) of its insets, margins and sizes are resolved to
/// lengths (the style and layout interleaving: the box is laid out with a style whose declarations of the position fallback origin are
/// these lengths, see <see cref="ComputedStyle.WithPositionOption"/>), <c>position-area</c> replaces its containing block by an area
/// of the position-area grid, <c>anchor-center</c> centers it on its default anchor box, and the position options of
/// <c>position-try-fallbacks</c> are tried when it overflows its inset-modified containing block.
/// </summary>
internal static class AnchorPositioning
{
    /// <summary>The maximum number of position options tried ("This limit must be at least five").</summary>
    private const int MaxPositionOptions = 64;

    /// <summary>
    /// Returns whether an absolutely positioned box uses the anchor positioning: an anchor function, <c>position-area</c>, a default
    /// anchor (<c>position-anchor</c>), position options, <c>anchor-center</c> or <c>position-visibility: no-overflow</c> (which
    /// applies to the absolutely positioned boxes without anchor too). Restores the computed style of the box when it was replaced by
    /// the style of a position option by the previous pass. A transition of the result of an anchor function replaces the function
    /// (see <see cref="CssTransitionUpdater.UpdateFromLayout"/>): the after-change style uses it.
    /// </summary>
    public static bool IsAnchorPositioned(ContainerBox box)
    {
        if (box.Style.PositionBaseStyle is { } baseStyle)
        {
            box.Style = baseStyle;
        }

        var style = box.Style;
        var layout = style.Layout;
        var anchor = layout.Anchor;
        return anchor.UsesAnchorFunctions || anchor.PositionArea is not null || anchor.TryFallbacks is not null
            || (CssTransitionUpdater.MayHaveTransitions(style) && style.CreateAfterChangeStyle().Layout.Anchor.UsesAnchorFunctions)
            || anchor.PositionAnchor is PositionAnchorKind.Auto or PositionAnchorKind.Name or PositionAnchorKind.MatchParent
            || (anchor.Visibility & PositionVisibility.NoOverflow) != PositionVisibility.Always
            || layout.Alignment.JustifySelf.Keyword == AlignmentKeyword.AnchorCenter || layout.Alignment.AlignSelf.Keyword == AlignmentKeyword.AnchorCenter;
    }

    /// <summary>
    /// Lays out an anchor-positioned box (https://drafts.csswg.org/css-anchor-position-1/#fallback-apply): with the last successful
    /// position option (or its base style) when its margin box (moved by the default scroll shift) fits in its inset-modified containing
    /// block, else with the first position option that fits, else with the current one.
    /// </summary>
    public static BoxFragment Layout(LayoutContext context, OutOfFlowCandidate candidate, BoxFragment hostFragment, List<BoxFragment>? laidOut,
        PhysicalRect containingBlockRect, WritingDirection containingBlock)
    {
        var box = candidate.Box;
        var baseStyle = box.Style;
        var engine = context.Engine;
        var state = box.Element is { } element ? engine.GetAnchorPositionState(element, box.PseudoElement) : new AnchorPositionState();

        // https://drafts.csswg.org/css-anchor-position-1/#anchor-recalculation-point: "whenever that element begins generating boxes"
        var recalculate = state.LastPass == 0 || state.LastPass < context.Pass - 1;
        if (state.LastPass != context.Pass)
        {
            if (recalculate)
            {
                state.RememberedScrollOffsets.Clear();
                state.LastSuccessfulOption = null;
                state.ReferencedAnchors.Clear();
            }

            state.LastPass = context.Pass;
        }

        var query = new AnchorQuery(context, candidate, hostFragment, laidOut);
        var options = GetPositionOptions(context, box, baseStyle, state);
        var layout = new AnchorLayout(query, baseStyle, containingBlockRect, containingBlock, state, hostFragment)
        {
            ComputesTransitionValues = box.Element is not null && CssTransitionUpdater.MayHaveTransitions(baseStyle),
        };

        AnchorAttempt? chosen = null;
        if (options.Count == 1)
        {
            chosen = layout.Try(options[0], hypothetical: recalculate);
        }
        else
        {
            // https://drafts.csswg.org/css-anchor-position-1/#determine-position-fallback-styles: the current styles are the last
            // successful position option (or the base styles), kept while they do not overflow. Without last successful position
            // option, the base styles are tried at their place in the list sorted by position-try-order ("Each position option is
            // applied to the box, one by one in the order specified by position-try-order, and the first that doesn't cause the
            // box to overflow its containing block is taken as the winner": the option with the largest inset-modified containing
            // block comes first, even when the base styles do not overflow)
            var lastSuccessful = options.Find(option => option.Text == state.LastSuccessfulOption);
            var current = lastSuccessful ?? options[0];
            List<PositionOption>? sorted = null;
            var first = lastSuccessful ?? (sorted = SortOptions(options, layout))[0];
            var firstAttempt = layout.Try(first, hypothetical: recalculate);
            if (firstAttempt.Fits)
            {
                chosen = firstAttempt;
            }
            else
            {
                // As in the browsers, the current option is tried first with the current scroll offsets (a hypothetical anchor
                // recalculation point) when its default scroll shift makes it overflow
                AnchorAttempt? freshAttempt = null;
                if (!recalculate)
                {
                    freshAttempt = layout.Try(first, hypothetical: true);
                    if (freshAttempt.Fits)
                    {
                        chosen = freshAttempt;
                    }
                }

                AnchorAttempt? currentAttempt = ReferenceEquals(first, current) ? freshAttempt ?? firstAttempt : null;
                if (chosen is null)
                {
                    foreach (var option in sorted ?? SortOptions(options, layout))
                    {
                        if (ReferenceEquals(option, first) || option.Text == state.LastSuccessfulOption)
                            continue;

                        var attempt = layout.Try(option, hypothetical: true);
                        if (attempt.Fits)
                        {
                            chosen = attempt;
                            break;
                        }

                        if (ReferenceEquals(option, current))
                        {
                            currentAttempt = attempt;
                        }
                    }
                }

                // "Return current styles" (as in the browsers, with the current scroll offsets)
                chosen ??= currentAttempt ?? firstAttempt;
            }
        }

        // The anchor recalculation point of the chosen option: its anchor references remember the current scroll offsets
        foreach (var (key, offset) in chosen.ScrollOffsets)
        {
            state.RememberedScrollOffsets[key] = offset;
        }

        // The box is laid out again when another option was tried after it (its subtree was laid out with the other style)
        if (!ReferenceEquals(layout.LastAttempt, chosen))
        {
            chosen = layout.Try(chosen.Option, hypothetical: false);
        }

        // https://drafts.csswg.org/css-anchor-position-2/#anchored-container-queries: the anchored queries of the descendants of an
        // anchored query container are evaluated against the option applied by its layout. When the styles of its descendants
        // queried another option (e.g. they were computed before this layout), they are computed again and the box is laid out
        // again with its new contents (the style and layout interleaving, as for the size containers; the option is not chosen
        // again, so there is no loop)
        if (box is { PseudoElement: null, Element: { } container } && (baseStyle.Layout.ContainerType & ContainerType.Anchored) != ContainerType.Normal)
        {
            // (the applied option is recorded by each pass)
            context.PreventLayoutCaching();
            var option = chosen.Option;
            var applied = option.IsBase ? null : new AnchoredFallback(option.Text, option.RuleName, option.Tactics, option.PositionArea, containingBlock, WritingDirection.FromStyle(baseStyle.Layout));
            engine.RecordAnchoredFallback(container, applied, context.Pass);
            var styleEngine = context.Document.GetStyleEngine();
            if (styleEngine.IsQueriedWithAnotherFallback(container, option.Text))
            {
                styleEngine.InvalidateQueryContainerDescendants(container);
                box.Style = baseStyle;
                engine.BoxTree?.RebuildContents(box);
                chosen = layout.Try(option, hypothetical: false);
            }
        }

        // https://github.com/w3c/csswg-drafts/issues/9598: the anchor functions and the declarations of the position option are
        // resolved at computed-value time, so their transitions start when their results change: the box is laid out again with
        // the values of the transitions started by the change
        var transitionValues = chosen.TransitionValues ?? GetTransitionValues(chosen.Style, chosen.Option, state);
        if (layout.ComputesTransitionValues && GetPreviousTransitionValues(context, box, state, recalculate) is { } previousValues
            && CssTransitionUpdater.UpdateFromLayout(baseStyle, previousValues, transitionValues))
        {
            baseStyle = baseStyle.WithCurrentAnimations(includeNewAnimations: true);
            layout.ReplaceBaseStyle(baseStyle);
            chosen = layout.Try(chosen.Option, hypothetical: false);
        }

        state.TransitionValues = transitionValues;
        box.Style = chosen.Style;
        state.UsedOption = chosen.Option.Text;
        // The anchors of the options tried by the previous passes still influence the position of a box with position options
        if (options.Count == 1)
        {
            state.ReferencedAnchors.Clear();
        }

        state.ReferencedAnchors.UnionWith(layout.ReferencedAnchors);

        state.Fragment = chosen.Result.Fragment;
        state.InsetModifiedContainingBlock = chosen.Result.InsetModifiedContainingBlock;
        state.HasFallbacks = options.Count > 1;
        state.DefaultAnchor = chosen.DefaultAnchor;
        state.CompensatesHorizontally = chosen.CompensatesHorizontally;
        state.CompensatesVertically = chosen.CompensatesVertically;
        state.DefaultAnchorRect = chosen.DefaultAnchorRect;
        state.RememberedDefaultAnchorOffset = chosen.DefaultAnchor is { } defaultAnchor && state.RememberedScrollOffsets.TryGetValue(defaultAnchor.Key, out var remembered) ? remembered : PhysicalOffset.Zero;
        state.Overflows = !chosen.FitsWithoutShift;
        state.IsInsetModifiedContainingBlockCorrected = chosen.Result.IsCorrected;
        state.ReferencesDefaultAnchor = chosen.ReferencesDefaultAnchor;
        state.Visibility = chosen.Style.Layout.Anchor.Visibility;
        state.Follower = chosen.Follower;
        chosen.Result.Fragment.AnchorScrollShift = PhysicalOffset.Zero;
        engine.RegisterAnchorPositionedFragment(state);
        return chosen.Result.Fragment;
    }

    /// <summary>
    /// Returns the before-change values of the transitions of an anchor-positioned box: the values resolved by its last layout, or,
    /// when it was not anchor-positioned, the values of its before-change style (it was laid out with them), or null when it was not
    /// laid out.
    /// </summary>
    private static Dictionary<string, string>? GetPreviousTransitionValues(LayoutContext context, ContainerBox box, AnchorPositionState state, bool recalculate)
    {
        if (!recalculate)
            return state.TransitionValues;

        if (box.Element is not { } element)
            return null;

        var beforeChange = context.Document.GetStyleEngine().GetBeforeChangeStyle(element, box.PseudoElement);
        if (beforeChange is null || beforeChange.IsDisplayNoneForAnimations())
            return null;

        return GetTransitionValues(beforeChange, option: null, state: null);
    }

    /// <summary>
    /// Updates the transitions of an absolutely positioned box that is not anchor-positioned anymore: the values resolved by its last
    /// layout as an anchor-positioned box (e.g. the declarations of a position option) transition to its computed values. Returns
    /// whether the transitions changed: the box is laid out with its new style.
    /// </summary>
    public static bool UpdateTransitionsOfFormerAnchoredBox(LayoutContext context, ContainerBox box)
    {
        if (box.Element is not { } element || context.Engine.TryGetAnchorPositionState(element, box.PseudoElement) is not { TransitionValues: { } previousValues } state)
            return false;

        // (the state is discarded after the pass)
        state.TransitionValues = null;
        var style = box.Style;
        if (!CssTransitionUpdater.MayHaveTransitions(style) || !CssTransitionUpdater.UpdateFromLayout(style, previousValues, GetTransitionValues(style.CreateAfterChangeStyle(), option: null, state: null)))
            return false;

        box.Style = style.WithCurrentAnimations(includeNewAnimations: true);
        return true;
    }

    /// <summary>
    /// Returns the values of the properties whose results the layout of an anchor-positioned box computes: the properties that accept
    /// the anchor functions, the declarations of its position option, and the properties of the previous layout.
    /// </summary>
    private static Dictionary<string, string> GetTransitionValues(ComputedStyle style, PositionOption? option, AnchorPositionState? state)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in AnchorPositioningStyle.AnchorFunctionProperties)
        {
            result[property] = style.GetUnanimatedComputedValue(property);
        }

        if (option?.Rule is { } rule)
        {
            foreach (var declaration in rule.Style.Declarations)
            {
                var property = LogicalProperties.GetPhysicalProperty(declaration.Value.Name, style) ?? declaration.Value.Name;
                result.TryAdd(property, style.GetUnanimatedComputedValue(property));
            }
        }

        if (option?.PositionArea is not null)
        {
            result.TryAdd("position-area", style.GetUnanimatedComputedValue("position-area"));
        }

        if (state?.TransitionValues is { } previous)
        {
            foreach (var property in previous.Keys)
            {
                result.TryAdd(property, style.GetUnanimatedComputedValue(property));
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves the self-alignment of an anchor-positioned box in an axis of its containing block: <c>anchor-center</c> centers it on its
    /// default anchor box, and <c>normal</c> is the default alignment of <c>position-area</c>
    /// (https://drafts.csswg.org/css-anchor-position-1/#position-area-alignment): <c>center</c> when only the center track is selected,
    /// <c>anchor-center</c> when the three tracks are selected, else toward the anchor; "if only one inset property in the relevant axis
    /// is auto, the default alignment is instead towards the edge with the non-auto inset; and this is an unsafe alignment".
    /// </summary>
    internal static PositionedLayout.AbsoluteAlignment ResolveAlignment(PositionedLayout.AbsoluteAlignment resolved, Alignment value, in AnchorAxisParameters parameters, in AlignmentAxis axis,
        PhysicalSide? singleNonAutoInset, PhysicalRect containingBlockRect)
    {
        if (value.Keyword == AlignmentKeyword.AnchorCenter)
            return parameters.AnchorCenter is { } center ? resolved with { Alignment = AxisAlignment.Center, AnchorCenter = ToAxisCoordinate(center, axis.Start, containingBlockRect) } : resolved;

        if (!resolved.IsNormal || !parameters.HasPositionArea)
            return resolved;

        if (singleNonAutoInset is { } side)
            return new PositionedLayout.AbsoluteAlignment(axis.ToAlignment(side), OverflowAlignment.Unsafe, IsNormal: false);

        if (parameters.AreaAnchorCenter && parameters.AnchorCenter is { } anchorCenter)
            return new PositionedLayout.AbsoluteAlignment(AxisAlignment.Center, value.Overflow, IsNormal: false) { AnchorCenter = ToAxisCoordinate(anchorCenter, axis.Start, containingBlockRect) };

        if (parameters.AreaSide is { } areaSide)
            return new PositionedLayout.AbsoluteAlignment(axis.ToAlignment(areaSide), value.Overflow, IsNormal: false);

        return new PositionedLayout.AbsoluteAlignment(AxisAlignment.Center, value.Overflow, IsNormal: false);

        static double ToAxisCoordinate(double coordinate, PhysicalSide start, PhysicalRect rect) => start switch
        {
            PhysicalSide.Left => coordinate - rect.Left,
            PhysicalSide.Right => rect.Right - coordinate,
            PhysicalSide.Top => coordinate - rect.Top,
            _ => rect.Bottom - coordinate,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Position options
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the position options list of a box (https://drafts.csswg.org/css-anchor-position-1/#position-options-list): its base
    /// styles, then the options of <c>position-try-fallbacks</c> whose <c>@position-try</c> rule exists. Clears the last successful
    /// position option after a fallback-sensitive change (https://drafts.csswg.org/css-anchor-position-1/#fallback-sensitive-changes).
    /// </summary>
    private static List<PositionOption> GetPositionOptions(LayoutContext context, ContainerBox box, ComputedStyle baseStyle, AnchorPositionState state)
    {
        var options = new List<PositionOption> { new(PositionOption.BaseText, rule: null, [], positionArea: null) };
        var anchor = baseStyle.Layout.Anchor;
        var signature = new StringBuilder();
        if (anchor.TryFallbacks is { } fallbacks)
        {
            var styleEngine = context.Document.GetStyleEngine();
            foreach (var fallback in fallbacks)
            {
                if (options.Count >= MaxPositionOptions)
                    break;

                CssPositionTryRule? rule = null;
                if (fallback.RuleName is { } ruleName)
                {
                    // "If there is a @position-try rule with the given name [...] Otherwise, this value has no effect"
                    rule = styleEngine.FindPositionTry(ruleName, anchor.TryFallbacksScope);
                    if (rule is null)
                        continue;

                    signature.Append(ruleName).Append('{').Append(rule.Style.CssText).Append('}');
                }

                options.Add(new PositionOption(fallback.Text, rule, fallback.Tactics, fallback.PositionArea) { RuleName = fallback.RuleName });
            }

            signature.Append(baseStyle.GetComputedValue("position-try-fallbacks")).Append('|').Append(baseStyle.GetComputedValue("position-try-order"));
        }

        // https://drafts.csswg.org/css-anchor-position-1/#fallback-sensitive-changes: the position, the position-try longhands, the
        // accepted @position-try properties and the @position-try rules
        if (options.Count > 1)
        {
            signature.Append('|').Append(baseStyle.GetComputedValue("position")).Append('|').Append(box.Kind);
            foreach (var property in AcceptedProperties)
            {
                signature.Append('|').Append(baseStyle.GetComputedValue(property));
            }
        }

        var text = signature.ToString();
        if (!string.Equals(state.Signature, text, StringComparison.Ordinal))
        {
            state.Signature = text;
            state.LastSuccessfulOption = null;
            state.ReferencedAnchors.Clear();
        }

        return options;
    }

    /// <summary>The physical longhands of the accepted @position-try properties (https://drafts.csswg.org/css-anchor-position-1/#accepted-position-try-properties).</summary>
    internal static readonly string[] AcceptedProperties =
    [
        "top", "right", "bottom", "left", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "width", "height", "min-width", "min-height", "max-width", "max-height",
        "justify-self", "align-self", "position-anchor", "position-area",
    ];

    /// <summary>
    /// Sorts the position options by the size of their inset-modified containing block (https://drafts.csswg.org/css-anchor-position-1/#position-try-order-property):
    /// "Stably sort the position options list according to this size, with the largest coming first" (the auto insets are zero).
    /// </summary>
    private static List<PositionOption> SortOptions(List<PositionOption> options, AnchorLayout layout)
    {
        var order = layout.BaseStyle.Layout.Anchor.TryOrder;
        if (order == PositionTryOrder.Normal)
            return options;

        var sizes = new double[options.Count];
        for (var i = 0; i < options.Count; i++)
        {
            var imcb = layout.MeasureInsetModifiedContainingBlock(options[i]);
            var horizontal = order switch
            {
                PositionTryOrder.MostWidth => true,
                PositionTryOrder.MostHeight => false,
                PositionTryOrder.MostInlineSize => layout.ContainingBlock.IsHorizontal,
                _ => !layout.ContainingBlock.IsHorizontal,
            };

            sizes[i] = horizontal ? imcb.Width : imcb.Height;
        }

        var indexes = Enumerable.Range(0, options.Count).ToArray();
        Array.Sort(indexes, (a, b) => sizes[a] != sizes[b] ? sizes[b].CompareTo(sizes[a]) : a.CompareTo(b));
        return [.. indexes.Select(index => options[index])];
    }

    /// <summary>A position option (https://drafts.csswg.org/css-anchor-position-1/#position-options): the base styles, or an entry of <c>position-try-fallbacks</c>.</summary>
    internal sealed class PositionOption(string text, CssPositionTryRule? rule, IReadOnlyList<TryTactic> tactics, string? positionArea)
    {
        /// <summary>The text of the base styles option.</summary>
        public const string BaseText = "";

        /// <summary>The serialized entry of <c>position-try-fallbacks</c> (identifies the last successful position option).</summary>
        public string Text { get; } = text;

        public CssPositionTryRule? Rule { get; } = rule;

        public IReadOnlyList<TryTactic> Tactics { get; } = tactics;

        public string? PositionArea { get; } = positionArea;

        /// <summary>The name of the <c>@position-try</c> rule of the option, or null.</summary>
        public string? RuleName { get; init; }

        public bool IsBase => Text.Length == 0;
    }

    /// <summary>The layout of a box with a position option.</summary>
    internal sealed class AnchorAttempt
    {
        public required PositionOption Option { get; init; }

        public required ComputedStyle Style { get; init; }

        public required AbsoluteLayoutResult Result { get; init; }

        /// <summary>Whether the margin box, moved by the default scroll shift, is contained in the inset-modified containing block.</summary>
        public bool Fits { get; init; }

        /// <summary>Whether the margin box is contained in the inset-modified containing block (without the default scroll shift).</summary>
        public bool FitsWithoutShift { get; init; }

        /// <summary>The scroll offsets of the anchor references (the remembered scroll offsets of a recalculation point).</summary>
        public required Dictionary<(Element Element, string? PseudoElement), PhysicalOffset> ScrollOffsets { get; init; }

        public AnchorBox? DefaultAnchor { get; init; }

        /// <summary>The rectangle of the default anchor box the box was laid out with (in the coordinates of its containing block fragment).</summary>
        public PhysicalRect DefaultAnchorRect { get; init; }

        public bool CompensatesHorizontally { get; init; }

        public bool CompensatesVertically { get; init; }

        /// <summary>Whether the box references its default anchor box (<c>position-area</c>, <c>anchor-center</c>, an anchor function without name).</summary>
        public bool ReferencesDefaultAnchor { get; init; }

        public required InsetModifiedContainingBlockFollower Follower { get; init; }

        /// <summary>The values of the after-change style used by the transitions (see <see cref="GetTransitionValues"/>), or null when the box has no transitions.</summary>
        public Dictionary<string, string>? TransitionValues { get; init; }
    }

    /// <summary>Lays out an anchor-positioned box with its position options.</summary>
    private sealed class AnchorLayout(AnchorQuery query, ComputedStyle baseStyle, PhysicalRect containingBlockRect, WritingDirection containingBlock, AnchorPositionState state, BoxFragment hostFragment)
    {
        private ComputedStyle _baseStyle = baseStyle;

        public ComputedStyle BaseStyle => _baseStyle;

        /// <summary>Whether the attempts compute the values of the after-change style used by the transitions (the box may have transitions).</summary>
        public bool ComputesTransitionValues { get; init; }

        /// <summary>Replaces the computed style of the box (with the transitions started by the layout).</summary>
        public void ReplaceBaseStyle(ComputedStyle style) => _baseStyle = style;

        public WritingDirection ContainingBlock => containingBlock;

        /// <summary>The last attempt (the subtree of the box is laid out with its style).</summary>
        public AnchorAttempt? LastAttempt { get; private set; }

        /// <summary>The anchor elements referenced by the position options tried (they stay relevant to the user, https://drafts.csswg.org/css-anchor-position-1/#anchor-relevance).</summary>
        public HashSet<(Element Element, string? PseudoElement)> ReferencedAnchors { get; } = [];

        /// <summary>Returns the inset-modified containing block of a position option, without laying out the box (the auto insets are zero).</summary>
        public PhysicalRect MeasureInsetModifiedContainingBlock(PositionOption option)
        {
            var resolution = Resolve(option, hypothetical: true, zeroAutoInsets: true);
            var box = query.Box;
            var previous = box.Style;
            box.Style = resolution.Style;
            try
            {
                return PositionedLayout.LayoutInContainingBlock(query.Context, query.Candidate, resolution.ContainingBlockRect, containingBlock, resolution.Parameters, measureOnly: true).InsetModifiedContainingBlock;
            }
            finally
            {
                box.Style = previous;
            }
        }

        public AnchorAttempt Try(PositionOption option, bool hypothetical)
        {
            var resolution = Resolve(option, hypothetical, zeroAutoInsets: false);
            var box = query.Box;
            box.Style = resolution.Style;
            var result = PositionedLayout.LayoutInContainingBlock(query.Context, query.Candidate, resolution.ContainingBlockRect, containingBlock, resolution.Parameters);
            var fragment = result.Fragment;
            var marginBox = fragment.MarginBoxRect.Translate(fragment.Offset);
            var imcb = result.InsetModifiedContainingBlock;
            var fits = !result.IsCorrected && Contains(imcb, marginBox);

            // https://drafts.csswg.org/css-anchor-position-1/#fallback-apply: "When a positioned box (after applying any default scroll
            // shift) overflows its inset-modified containing block"
            var shift = PhysicalOffset.Zero;
            if (!hypothetical && resolution.DefaultAnchor is { } defaultAnchor && state.RememberedScrollOffsets.TryGetValue(defaultAnchor.Key, out var remembered))
            {
                var current = defaultAnchor.GetScrollOffset(query.Context.Engine);
                shift = new PhysicalOffset(resolution.CompensatesHorizontally ? remembered.X - current.X : 0, resolution.CompensatesVertically ? remembered.Y - current.Y : 0);
            }

            var attempt = new AnchorAttempt
            {
                Option = option,
                Style = resolution.Style,
                Result = result,
                Fits = !result.IsCorrected && Contains(resolution.Follower.GetInsetModifiedContainingBlock(imcb, shift), marginBox.Translate(shift)),
                Follower = resolution.Follower,
                FitsWithoutShift = fits,
                ScrollOffsets = resolution.ScrollOffsets,
                DefaultAnchor = resolution.DefaultAnchor,
                DefaultAnchorRect = resolution.DefaultAnchorRect,
                CompensatesHorizontally = resolution.CompensatesHorizontally,
                CompensatesVertically = resolution.CompensatesVertically,
                ReferencesDefaultAnchor = resolution.ReferencesDefaultAnchor,
                TransitionValues = resolution.TransitionValues,
            };

            LastAttempt = attempt;
            return attempt;
        }

        /// <summary>Returns whether a rectangle contains another one (within the rounding errors).</summary>
        private static bool Contains(PhysicalRect outer, PhysicalRect inner)
        {
            const double Epsilon = 0.01;
            return inner.Left >= outer.Left - Epsilon && inner.Top >= outer.Top - Epsilon && inner.Right <= outer.Right + Epsilon && inner.Bottom <= outer.Bottom + Epsilon;
        }

        /// <summary>
        /// Applies a position option to the box (https://drafts.csswg.org/css-anchor-position-1/#apply-a-position-option): its
        /// declarations in the position fallback origin, then the anchor functions resolved to lengths, the default anchor box, and
        /// the containing block (<c>position-area</c>).
        /// </summary>
        private OptionResolution Resolve(PositionOption option, bool hypothetical, bool zeroAutoInsets)
        {
            var optionStyle = ApplyOption(option);
            var anchorStyle = optionStyle.Layout.Anchor;
            var defaultAnchorBox = query.GetDefaultAnchor(optionStyle);
            var scrollOffsets = new Dictionary<(Element Element, string? PseudoElement), PhysicalOffset>();
            var defaultAnchor = defaultAnchorBox is null ? null : query.GetAnchorBox(defaultAnchorBox);
            var engine = query.Context.Engine;

            // The anchor references are calculated as if all the scroll containers were at their initial scroll position, then
            // their remembered scroll offset is added (https://drafts.csswg.org/css-anchor-position-1/#remembered-scroll-offset)
            PhysicalRect GetRect(AnchorBox anchorBox)
            {
                if (!scrollOffsets.TryGetValue(anchorBox.Key, out var offset))
                {
                    offset = !hypothetical && state.RememberedScrollOffsets.TryGetValue(anchorBox.Key, out var remembered) ? remembered : anchorBox.GetScrollOffset(engine);
                    scrollOffsets[anchorBox.Key] = offset;
                }

                return anchorBox.UnscrolledRect.Translate(-offset);
            }

            // "If abspos has a default anchor element, it always calculates a remembered scroll offset for it"
            PhysicalRect? defaultRect = defaultAnchor is null ? null : GetRect(defaultAnchor);

            // https://drafts.csswg.org/css-anchor-position-1/#scroll-container: the scrollable containing block replaces the local
            // containing block of a scroll container when the box has a default anchor element
            var cbRect = containingBlockRect;
            var originalCb = cbRect;
            var containingBlockBox = query.Candidate.ContainingBlock;
            if (defaultAnchor is not null && containingBlockBox.Kind != ContainerBoxKind.Viewport && hostFragment.IsScrollContainer && ReferenceEquals(hostFragment.Box, containingBlockBox))
            {
                cbRect = GetScrollableContainingBlock(hostFragment);
                originalCb = cbRect;
            }

            // https://drafts.csswg.org/css-align-3/#auto-safety-position: "for fixed-positioned boxes with default overflow alignment
            // (only) because a fixed-positioned box with a default anchor box that scrolls with the content can be scrolled into view
            // when outside the scrollport, in such cases the overflow limit rect is extended to the scroll container's entire
            // scrollable overflow area" (the scrolling area of the viewport, moved by the scroll offset the anchor is measured with)
            var limitRect = originalCb;
            if (defaultAnchor is { IncludesViewportScroll: true } && hostFragment.Box.Kind == ContainerBoxKind.Viewport && scrollOffsets.TryGetValue(defaultAnchor.Key, out var usedOffset))
            {
                var viewportOffset = engine.ViewportScrollState.Offset - (defaultAnchor.GetScrollOffset(engine) - usedOffset);
                limitRect = limitRect.Union(hostFragment.GetScrollingArea().Translate(-viewportOffset));
            }

            // https://drafts.csswg.org/css-anchor-position-1/#position-area: the containing block is the selected area of the grid
            var horizontal = new AnchorAxisParameters { ZeroAutoInsets = zeroAutoInsets };
            var vertical = new AnchorAxisParameters { ZeroAutoInsets = zeroAutoInsets };
            if (anchorStyle.PositionArea is { } area && defaultRect is { } anchorRect)
            {
                var (horizontalTracks, verticalTracks) = area.Resolve(containingBlock, WritingDirection.FromStyle(optionStyle.Layout));
                var (left, right) = GetArea(horizontalTracks, cbRect.Left, cbRect.Right, anchorRect.Left, anchorRect.Right);
                var (top, bottom) = GetArea(verticalTracks, cbRect.Top, cbRect.Bottom, anchorRect.Top, anchorRect.Bottom);
                cbRect = new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
                horizontal = GetAreaParameters(horizontalTracks, PhysicalSide.Left, PhysicalSide.Right, anchorRect.Left, anchorRect.Right, limitRect.Left, limitRect.Right);
                vertical = GetAreaParameters(verticalTracks, PhysicalSide.Top, PhysicalSide.Bottom, anchorRect.Top, anchorRect.Bottom, limitRect.Top, limitRect.Bottom);
            }

            // https://drafts.csswg.org/css-anchor-position-1/#anchor-center: centered on the default anchor box
            var layout = optionStyle.Layout;
            var horizontalAlignment = containingBlock.IsHorizontal ? layout.Alignment.JustifySelf : layout.Alignment.AlignSelf;
            var verticalAlignment = containingBlock.IsHorizontal ? layout.Alignment.AlignSelf : layout.Alignment.JustifySelf;
            if (defaultRect is { } centerRect)
            {
                horizontal = horizontal with { AnchorCenter = centerRect.Left + (centerRect.Width / 2) };
                vertical = vertical with { AnchorCenter = centerRect.Top + (centerRect.Height / 2) };
                if (horizontalAlignment.Keyword == AlignmentKeyword.AnchorCenter)
                {
                    horizontal = horizontal with { ZeroAutoInsets = true, LimitLow = horizontal.LimitLow ?? limitRect.Left, LimitHigh = horizontal.LimitHigh ?? limitRect.Right };
                }

                if (verticalAlignment.Keyword == AlignmentKeyword.AnchorCenter)
                {
                    vertical = vertical with { ZeroAutoInsets = true, LimitLow = vertical.LimitLow ?? limitRect.Top, LimitHigh = vertical.LimitHigh ?? limitRect.Bottom };
                }
            }

            // https://drafts.csswg.org/css-anchor-position-1/#anchor-resolution: the anchor functions resolve to lengths
            var referencesDefault = anchorStyle.PositionArea is not null || horizontalAlignment.Keyword == AlignmentKeyword.AnchorCenter || verticalAlignment.Keyword == AlignmentKeyword.AnchorCenter;
            var compensatesHorizontally = defaultAnchor is not null && (anchorStyle.PositionArea is not null || horizontalAlignment.Keyword == AlignmentKeyword.AnchorCenter);
            var compensatesVertically = defaultAnchor is not null && (anchorStyle.PositionArea is not null || verticalAlignment.Keyword == AlignmentKeyword.AnchorCenter);
            var style = optionStyle;
            var evaluatorSides = default(Sides<bool>);
            if (anchorStyle.UsesAnchorFunctions)
            {
                var evaluator = new AnchorFunctionEvaluator(query, optionStyle, cbRect, containingBlock, defaultAnchorBox, GetRect);
                style = evaluator.Resolve(_baseStyle);
                referencesDefault |= evaluator.ReferencesDefaultAnchor;
                if (defaultAnchor is not null)
                {
                    evaluatorSides = evaluator.GetSidesFollowing(defaultAnchor);
                }


                // https://drafts.csswg.org/css-anchor-position-1/#compensate-for-scroll: an anchor() function of the axis refers to an
                // element with the same nearest scroll container as the default anchor box
                if (defaultAnchor is not null)
                {
                    compensatesHorizontally |= evaluator.ReferencesScrollContextOf(defaultAnchor, horizontalAxis: true);
                    compensatesVertically |= evaluator.ReferencesScrollContextOf(defaultAnchor, horizontalAxis: false);
                }
            }

            ReferencedAnchors.UnionWith(scrollOffsets.Keys);
            var parameters = new AnchorLayoutParameters { Horizontal = horizontal, Vertical = vertical };
            var follower = new InsetModifiedContainingBlockFollower
            {
                Area = anchorStyle.PositionArea is { } positionArea && defaultRect is { } areaAnchor
                    ? (positionArea.Resolve(containingBlock, WritingDirection.FromStyle(optionStyle.Layout)), originalCb, areaAnchor, cbRect)
                    : null,
                FollowingSides = defaultAnchor is not null && anchorStyle.UsesAnchorFunctions ? evaluatorSides : default,
            };

            // The values of the after-change style (without the transitions), whose anchor functions are resolved
            Dictionary<string, string>? transitionValues = null;
            if (ComputesTransitionValues)
            {
                var afterChange = new AnchorFunctionEvaluator(query, optionStyle.CreateAfterChangeStyle(), cbRect, containingBlock, defaultAnchorBox, GetRect).Resolve(_baseStyle);
                transitionValues = GetTransitionValues(afterChange, option, state);
            }

            return new OptionResolution(style, cbRect, parameters, scrollOffsets, defaultAnchor, compensatesHorizontally, compensatesVertically, referencesDefault)
            {
                Follower = follower,
                DefaultAnchorRect = defaultRect ?? default,
                TransitionValues = transitionValues,
            };
        }

        /// <summary>
        /// Returns the scrollable containing block of a scroll container (https://drafts.csswg.org/css-anchor-position-1/#scrollable-containing-block):
        /// its padding box extended to its in-flow contents and their paddings, as its scrolling area, without the relative offsets and the
        /// transforms of the contents (https://github.com/w3c/csswg-drafts/issues/12552), in the coordinates of its fragment.
        /// </summary>
        private static PhysicalRect GetScrollableContainingBlock(BoxFragment scrollContainer)
        {
            var padding = scrollContainer.PaddingBoxRect;
            if (scrollContainer.InFlowBounds is not { } inFlow)
                return padding;

            var area = padding.Union(inFlow.Expand(scrollContainer.Padding));
            return ScrollContainers.ClampToScrollOrigin(area, padding, ScrollContainers.GetScrollOrigin(scrollContainer.Box, scrollContainer.WritingDirection));
        }

        /// <summary>Returns the bounds of the selected tracks of the position-area grid in an axis (https://drafts.csswg.org/css-anchor-position-1/#position-area-grid-resolution).</summary>
        internal static (double Low, double High) GetArea(PositionAreaTracks tracks, double containingBlockLow, double containingBlockHigh, double anchorLow, double anchorHigh)
        {
            Span<double> lines = [Math.Min(containingBlockLow, anchorLow), anchorLow, anchorHigh, Math.Max(containingBlockHigh, anchorHigh)];
            return (lines[tracks.First], lines[tracks.Last + 1]);
        }

        private static AnchorAxisParameters GetAreaParameters(PositionAreaTracks tracks, PhysicalSide low, PhysicalSide high, double anchorLow, double anchorHigh, double limitLow, double limitHigh) => new()
        {
            ZeroAutoInsets = true,
            HasPositionArea = true,
            AreaAnchorCenter = tracks.IsAll,
            AreaSide = tracks.IsCenter || tracks.IsAll ? null : tracks.First == 0 ? high : low,
            LimitLow = Math.Min(limitLow, anchorLow),
            LimitHigh = Math.Max(limitHigh, anchorHigh),
        };

        /// <summary>Returns the style of the box with the declarations of a position option (https://drafts.csswg.org/css-anchor-position-1/#apply-a-position-option).</summary>
        private ComputedStyle ApplyOption(PositionOption option)
        {
            if (option.IsBase)
                return _baseStyle;

            if (option.PositionArea is { } positionArea)
                return _baseStyle.WithPositionOption([new CssLonghandValue("position-area", positionArea)], _baseStyle);

            var style = _baseStyle;
            if (option.Rule is { } rule)
            {
                var declarations = new List<CssLonghandValue>(rule.Style.Declarations.Count);
                foreach (var declaration in rule.Style.Declarations)
                {
                    declarations.Add(declaration.Value);
                }

                style = _baseStyle.WithPositionOption(declarations, _baseStyle);
            }

            if (option.Tactics.Count > 0)
            {
                style = _baseStyle.WithPositionOption(TryTactics.Apply(style, option.Tactics, containingBlock), _baseStyle);
            }

            return style;
        }
    }

    /// <summary>The style, the containing block and the anchor data of a position option.</summary>
    private sealed record OptionResolution(ComputedStyle Style, PhysicalRect ContainingBlockRect, AnchorLayoutParameters Parameters,
        Dictionary<(Element Element, string? PseudoElement), PhysicalOffset> ScrollOffsets, AnchorBox? DefaultAnchor, bool CompensatesHorizontally, bool CompensatesVertically, bool ReferencesDefaultAnchor)
    {
        public InsetModifiedContainingBlockFollower Follower { get; init; } = new();

        public PhysicalRect DefaultAnchorRect { get; init; }

        public Dictionary<string, string>? TransitionValues { get; init; }
    }

    /// <summary>
    /// Moves the inset-modified containing block of an anchor-positioned box with the scrolling of its default anchor box: the box is
    /// laid out with the remembered scroll offsets, and its margin box is moved by the default scroll shift; the overflow is checked
    /// against the inset-modified containing block the current scroll offsets would give (https://drafts.csswg.org/css-anchor-position-1/#fallback-apply):
    /// the lines of the position-area grid at the edges of the default anchor box move, the ones at the edges of the containing block
    /// do not, nor do the edges given by the insets that do not refer to the default anchor box.
    /// </summary>
    internal sealed class InsetModifiedContainingBlockFollower
    {
        /// <summary>The tracks of <c>position-area</c>, the containing block, the default anchor box and the area at the layout, or null.</summary>
        public ((PositionAreaTracks Horizontal, PositionAreaTracks Vertical) Tracks, PhysicalRect ContainingBlock, PhysicalRect Anchor, PhysicalRect Area)? Area { get; init; }

        /// <summary>The sides whose insets refer to the scrolling context of the default anchor box.</summary>
        public Sides<bool> FollowingSides { get; init; }

        /// <summary>Returns the inset-modified containing block when the default anchor box is moved by <paramref name="shift"/>.</summary>
        public PhysicalRect GetInsetModifiedContainingBlock(PhysicalRect imcb, PhysicalOffset shift)
        {
            if (shift == PhysicalOffset.Zero)
                return imcb;

            if (Area is { } area)
            {
                // The insets are kept relative to the area
                var anchor = area.Anchor.Translate(shift);
                var (left, right) = AnchorLayout.GetArea(area.Tracks.Horizontal, area.ContainingBlock.Left, area.ContainingBlock.Right, anchor.Left, anchor.Right);
                var (top, bottom) = AnchorLayout.GetArea(area.Tracks.Vertical, area.ContainingBlock.Top, area.ContainingBlock.Bottom, anchor.Top, anchor.Bottom);
                return new PhysicalRect(
                    left + (imcb.Left - area.Area.Left),
                    top + (imcb.Top - area.Area.Top),
                    Math.Max(0, (right - left) - (area.Area.Width - imcb.Width)),
                    Math.Max(0, (bottom - top) - (area.Area.Height - imcb.Height)));
            }

            var sides = FollowingSides;
            var x1 = imcb.Left + (sides.Left ? shift.X : 0);
            var x2 = imcb.Right + (sides.Right ? shift.X : 0);
            var y1 = imcb.Top + (sides.Top ? shift.Y : 0);
            var y2 = imcb.Bottom + (sides.Bottom ? shift.Y : 0);
            return new PhysicalRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }
    }
}
