using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The state of an anchor-positioned element kept from one layout pass to the next (https://drafts.csswg.org/css-anchor-position-1/):
/// its remembered scroll offsets, its last successful position option, and the data of its default scroll shift.
/// </summary>
internal sealed class AnchorPositionState
{
    /// <summary>The last layout pass that laid out the box (an anchor recalculation point occurs when it starts generating boxes again).</summary>
    public int LastPass;

    /// <summary>The pass the fragment was registered for the default scroll shift.</summary>
    public int RegisteredPass;

    /// <summary>The remembered scroll offsets of the anchor references (https://drafts.csswg.org/css-anchor-position-1/#remembered-scroll-offset).</summary>
    public Dictionary<(Element Element, string? PseudoElement), PhysicalOffset> RememberedScrollOffsets { get; } = [];

    /// <summary>The last successful position option (https://drafts.csswg.org/css-anchor-position-1/#last-successful-position-option), or null.</summary>
    public string? LastSuccessfulOption;

    /// <summary>
    /// The target anchor elements of the position options tried since the anchor recalculation point or the last fallback-sensitive
    /// change (https://drafts.csswg.org/css-anchor-position-1/#anchor-relevance: they influence the position of the box).
    /// </summary>
    public HashSet<(Element Element, string? PseudoElement)> ReferencedAnchors { get; } = [];

    /// <summary>The position option used by the last layout.</summary>
    public string? UsedOption;

    /// <summary>The values whose changes are fallback-sensitive changes (https://drafts.csswg.org/css-anchor-position-1/#fallback-sensitive-changes).</summary>
    public string? Signature;

    /// <summary>The fragment of the last layout.</summary>
    public BoxFragment? Fragment;

    /// <summary>The inset-modified containing block of the last layout, in the coordinates of the parent of the fragment.</summary>
    public PhysicalRect InsetModifiedContainingBlock;

    /// <summary>Whether the box has more than one position option.</summary>
    public bool HasFallbacks;

    /// <summary>Whether the margin box overflowed the inset-modified containing block (after the position fallback), without the default scroll shift.</summary>
    public bool Overflows;

    /// <summary>Whether the inset-modified containing block of the last layout had a negative size in an axis, corrected to zero.</summary>
    public bool IsInsetModifiedContainingBlockCorrected;

    /// <summary>The default anchor box, or null.</summary>
    public AnchorBox? DefaultAnchor;

    /// <summary>The rectangle of the default anchor box the box was laid out with, in the coordinates of the fragment of its containing block.</summary>
    public PhysicalRect DefaultAnchorRect;

    /// <summary>The remembered scroll offset of the default anchor box.</summary>
    public PhysicalOffset RememberedDefaultAnchorOffset;

    /// <summary>Whether the box compensates for the scroll of its default anchor box in the horizontal axis (https://drafts.csswg.org/css-anchor-position-1/#compensate-for-scroll).</summary>
    public bool CompensatesHorizontally;

    public bool CompensatesVertically;

    /// <summary>Whether the box references its default anchor box (position-visibility: anchor-valid).</summary>
    public bool ReferencesDefaultAnchor;

    public PositionVisibility Visibility;

    /// <summary>Moves the inset-modified containing block with the scrolling of the default anchor box (for the overflow checks).</summary>
    public AnchorPositioning.InsetModifiedContainingBlockFollower? Follower;

    /// <summary>
    /// The values resolved by the last layout of the properties whose results the layout computes (the anchor functions and the
    /// declarations of the position option): the before-change values of their transitions (see <see cref="Animations.CssTransitionUpdater.UpdateFromLayout"/>).
    /// </summary>
    public Dictionary<string, string>? TransitionValues;

    /// <summary>The declarations of the position option applied by the last layout, when the box may have transitions, or null.</summary>
    public List<CssLonghandValue>? OptionDeclarations;

    /// <summary>The computed value of <c>position-try-fallbacks</c> of the last layout.</summary>
    public string? OptionFallbacks;
}

internal sealed partial class LayoutEngine
{
    private Dictionary<(Element Element, string? PseudoElement), AnchorPositionState>? _anchorPositionStates;
    private List<AnchorPositionState>? _anchorPositionedBoxes;
    private bool _checkingAnchorRelevance;

    /// <summary>Returns the state of an anchor-positioned element (or pseudo-element), created on first use.</summary>
    internal AnchorPositionState GetAnchorPositionState(Element element, string? pseudoElement)
    {
        _anchorPositionStates ??= [];
        if (!_anchorPositionStates.TryGetValue((element, pseudoElement), out var state))
        {
            state = new AnchorPositionState();
            _anchorPositionStates.Add((element, pseudoElement), state);
        }

        return state;
    }

    /// <summary>
    /// Returns the declarations of the position option applied by the last layout of an anchor-positioned element that may have
    /// transitions, or null (see <see cref="Cascade.StyleEngine"/>): the computed values of the element include them
    /// (https://drafts.csswg.org/css-anchor-position-1/#fallback-apply), so its transitions compare the styles with them.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="pseudoElement">The pseudo-element, or null.</param>
    /// <param name="fallbacks">The computed value of <c>position-try-fallbacks</c> of the style: the option applies while it is the same, or null to get the option whatever its value.</param>
    internal List<CssLonghandValue>? GetAppliedPositionOption(Element element, string? pseudoElement, string? fallbacks)
    {
        if (TryGetAnchorPositionState(element, pseudoElement) is not { OptionDeclarations: { } declarations } state)
            return null;

        return fallbacks is null || string.Equals(fallbacks, state.OptionFallbacks, StringComparison.Ordinal) ? declarations : null;
    }

    /// <summary>Returns the state of an element (or pseudo-element) that was anchor-positioned by the last layout pass, or null.</summary>
    internal AnchorPositionState? TryGetAnchorPositionState(Element element, string? pseudoElement)
        => _anchorPositionStates is not null && _anchorPositionStates.TryGetValue((element, pseudoElement), out var state) ? state : null;

    /// <summary>
    /// Returns whether an element is relevant to the user because of the anchor positioning (https://drafts.csswg.org/css-anchor-position-1/#anchor-relevance):
    /// "if a descendant of el is a target anchor element for a positioned box (which itself is not skipped and whose containing block
    /// is not el or a descendant of el), then el must be considered relevant to the user".
    /// </summary>
    internal bool IsAnchorRelevant(Element element)
    {
        // The positioned boxes and their anchors cannot keep each other relevant cyclically
        if (_anchorPositionStates is not { Count: > 0 } states || _checkingAnchorRelevance)
            return false;

        _checkingAnchorRelevance = true;
        try
        {
            foreach (var ((positioned, _), state) in states)
            {
                if (state.ReferencedAnchors.Count == 0 || element.IsShadowIncludingInclusiveAncestorOf(positioned))
                    continue;

                foreach (var (anchor, _) in state.ReferencedAnchors)
                {
                    if (!ReferenceEquals(anchor, element) && element.IsShadowIncludingInclusiveAncestorOf(anchor) && !ContentRelevancy.IsInSkippedContents(positioned))
                        return true;
                }
            }

            return false;
        }
        finally
        {
            _checkingAnchorRelevance = false;
        }
    }

    /// <summary>Records that an anchor-positioned box was laid out by the current pass (its default scroll shift and its visibility are updated after the pass).</summary>
    internal void RegisterAnchorPositionedFragment(AnchorPositionState state)
    {
        if (state.RegisteredPass == LayoutCount + 1)
            return;

        state.RegisteredPass = LayoutCount + 1;
        (_anchorPositionedBoxes ??= []).Add(state);

        // The anchor queries find the fragments of the anchors laid out by the pass: the layout result cache is not used anymore
        // (see UpdateLayout)
        UsesAnchorPositioning = true;
    }

    /// <summary>Forgets the anchor-positioned boxes of the previous pass (before a layout pass).</summary>
    private void ClearAnchorPositionedBoxes() => _anchorPositionedBoxes?.Clear();

    /// <summary>
    /// After a layout pass: forgets the state of the elements that stopped being anchor-positioned, and applies the default scroll
    /// shifts and the visibility of the anchor-positioned boxes.
    /// </summary>
    private void UpdateAnchorPositionedBoxes()
    {
        if (_anchorPositionStates is { Count: > 0 } states)
        {
            List<(Element, string?)>? stale = null;
            foreach (var (key, state) in states)
            {
                if (state.LastPass != LayoutCount)
                {
                    (stale ??= []).Add(key);
                }
            }

            if (stale is not null)
            {
                foreach (var key in stale)
                {
                    states.Remove(key);
                }
            }
        }

        if (_anchorPositionedBoxes is { Count: > 0 })
        {
            ApplyAnchorScrollShifts();
        }
    }

    /// <summary>
    /// Moves the anchor-positioned boxes by their default scroll shift (https://drafts.csswg.org/css-anchor-position-1/#default-scroll-shift):
    /// "the difference between the remembered scroll offset of the default anchor element and what its current remembered scroll offset
    /// would be if it were recalculated", in the axes where the box compensates for scroll. The shift is applied after the layout and
    /// after each scroll "as if affected by a transform", like the sticky offsets (it is part of the offset of the fragment).
    /// </summary>
    private void ApplyAnchorScrollShifts()
    {
        if (_anchorPositionedBoxes is null)
            return;

        foreach (var state in _anchorPositionedBoxes)
        {
            if (state.Fragment is not { } fragment)
                continue;

            var shift = PhysicalOffset.Zero;
            if (state.DefaultAnchor is { } anchor && (state.CompensatesHorizontally || state.CompensatesVertically))
            {
                var delta = GetDefaultAnchorMovement(state, anchor, fragment);
                shift = new PhysicalOffset(state.CompensatesHorizontally ? delta.X : 0, state.CompensatesVertically ? delta.Y : 0);
            }

            if (shift != fragment.AnchorScrollShift)
            {
                fragment.Offset += shift - fragment.AnchorScrollShift;
                fragment.AnchorScrollShift = shift;
            }
        }

        UpdateForceHiddenBoxes(_anchorPositionedBoxes);
    }

    /// <summary>
    /// Updates the boxes hidden by <c>position-visibility</c>. A box hidden by it hides its contents, and an anchor box in them is
    /// invisible (<c>anchor-visible</c>): the boxes are evaluated until the result is stable (an anchor box is laid out before the
    /// boxes positioned relative to it, so that chains of anchors converge in one pass, or in one more pass per unordered link).
    /// </summary>
    private void UpdateForceHiddenBoxes(List<AnchorPositionState> boxes)
    {
        for (var pass = 0; pass <= boxes.Count; pass++)
        {
            var changed = false;
            foreach (var state in boxes)
            {
                if (state.Fragment is not { } fragment)
                    continue;

                var hidden = IsForceHidden(state, fragment);
                if (hidden != fragment.IsForceHidden)
                {
                    SetForceHidden(fragment, hidden);
                    changed = true;
                }
            }

            if (!changed)
                break;
        }
    }

    /// <summary>
    /// Returns the axes in which a fixed positioned box moves with the document when the viewport scrolls: the axes where it
    /// compensates for scroll (https://drafts.csswg.org/css-anchor-position-1/#compensate-for-scroll), when its default anchor box
    /// scrolls with the viewport (its default scroll shift follows the scroll position of the viewport).
    /// </summary>
    internal (bool Horizontal, bool Vertical) GetViewportScrollCompensation(BoxFragment fragment)
    {
        if (fragment.Box.Element is { } element && _anchorPositionStates is { } states && states.TryGetValue((element, fragment.Box.PseudoElement), out var state)
            && ReferenceEquals(state.Fragment, fragment) && state.DefaultAnchor is { IncludesViewportScroll: true })
        {
            return (state.CompensatesHorizontally, state.CompensatesVertically);
        }

        return default;
    }

    /// <summary>Sets whether <c>position-visibility</c> hides a box: the painting order (the hit testing and the display list) changes.</summary>
    private void SetForceHidden(BoxFragment fragment, bool hidden)
    {
        if (fragment.IsForceHidden == hidden)
            return;

        fragment.IsForceHidden = hidden;
        _paintOrder = null;
        InvalidatePaint();
    }

    /// <summary>
    /// Returns how far the default anchor box moved since the layout of the box, in the coordinates of its containing block: its
    /// current position (with the current scroll offsets, the sticky offsets and the default scroll shifts of its ancestors, through
    /// the transforms) minus the position the box was laid out with. Without its fragment, the difference of the remembered scroll
    /// offset and of the current scroll offset.
    /// </summary>
    private PhysicalOffset GetDefaultAnchorMovement(AnchorPositionState state, AnchorBox anchor, BoxFragment fragment)
    {
        if (anchor.Fragment is { } anchorFragment && IsInFragmentTree(anchorFragment) && IsInFragmentTree(fragment))
        {
            // The origin of the coordinates of the containing block fragment, in the coordinates of the root (as the box is placed)
            var rect = anchor.RectInFragment;
            if (anchor.InScrolledContents && anchorFragment.Box.Element is { } scroller && TryGetScrollState(scroller) is { } scrollState)
            {
                rect = rect.Translate(-scrollState.Offset);
            }

            var current = MapToRoot(anchorFragment, rect).Bounds;
            var origin = fragment.GetOffsetInRoot() - fragment.Offset;
            return current.Offset - origin - state.DefaultAnchorRect.Offset;
        }

        var scrollOffset = anchor.GetScrollOffset(this);
        return state.RememberedDefaultAnchorOffset - scrollOffset;
    }

    /// <summary>
    /// Returns whether a fragment is in the fragment tree of the last layout, outside of the fragmentation contexts (whose fragments
    /// are moved to their fragmentainers after the layout, e.g. the columns of a multi-column container).
    /// </summary>
    private bool IsInFragmentTree(Fragment fragment)
    {
        var root = fragment;
        while (root.Parent is { } parent)
        {
            if (parent is BoxFragment { Box.Style.Layout.IsMultiColumnContainer: true })
                return false;

            root = parent;
        }

        return ReferenceEquals(root, _root);
    }

    /// <summary>
    /// Returns whether <c>position-visibility</c> hides an anchor-positioned box (https://drafts.csswg.org/css-anchor-position-1/#position-visibility):
    /// <c>anchor-valid</c> when it references its default anchor box and has none, <c>anchor-visible</c> when its default anchor box is
    /// invisible (hidden by <c>visibility</c>, or by the <c>position-visibility</c> of the box or of one of its ancestors) or clipped by
    /// intervening boxes, <c>no-overflow</c> when it overflows its inset-modified containing block.
    /// </summary>
    private static bool IsForceHidden(AnchorPositionState state, BoxFragment fragment)
    {
        var visibility = state.Visibility;
        if ((visibility & PositionVisibility.AnchorValid) != PositionVisibility.Always && state.ReferencesDefaultAnchor && state.DefaultAnchor is null)
            return true;

        if ((visibility & PositionVisibility.NoOverflow) != PositionVisibility.Always && OverflowsInsetModifiedContainingBlock(state, fragment))
            return true;

        if ((visibility & PositionVisibility.AnchorVisible) != PositionVisibility.Always && state.DefaultAnchor is { } anchor)
        {
            if (anchor.Box.Style.Layout.Visibility != Css.Layout.Visibility.Visible)
                return true;

            // visibility: force-hidden "can't be overridden by descendants": an anchor box that is hidden by position-visibility, or
            // in the contents of a box hidden by it, is invisible
            for (Fragment? ancestor = anchor.Box.LastLayoutFragment; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor is BoxFragment { IsForceHidden: true })
                    return true;
            }

            if (IsClippedByInterveningBoxes(anchor.Box, fragment))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the margin box of an anchor-positioned box, moved by its default scroll shift, overflows its inset-modified
    /// containing block (the one the current scroll offsets give, see <see cref="AnchorPositioning.InsetModifiedContainingBlockFollower"/>).
    /// </summary>
    private static bool OverflowsInsetModifiedContainingBlock(AnchorPositionState state, BoxFragment fragment)
    {
        var shift = fragment.AnchorScrollShift;
        if (shift == PhysicalOffset.Zero)
            return state.Overflows;

        if (state.IsInsetModifiedContainingBlockCorrected)
            return true;

        // The offset of the fragment includes the default scroll shift
        var marginBox = fragment.MarginBoxRect.Translate(fragment.Offset);
        var imcb = state.Follower?.GetInsetModifiedContainingBlock(state.InsetModifiedContainingBlock, shift) ?? state.InsetModifiedContainingBlock;
        const double Epsilon = 0.01;
        return marginBox.Left < imcb.Left - Epsilon || marginBox.Top < imcb.Top - Epsilon || marginBox.Right > imcb.Right + Epsilon || marginBox.Bottom > imcb.Bottom + Epsilon;
    }

    /// <summary>
    /// Returns whether an anchor box is clipped by intervening boxes (https://drafts.csswg.org/css-anchor-position-1/#clipped-by-intervening-boxes):
    /// its rectangle is fully clipped by the overflow clip of an ancestor that is a descendant of the containing block of the box.
    /// </summary>
    private static bool IsClippedByInterveningBoxes(ContainerBox anchor, BoxFragment positioned)
    {
        if (anchor.LastLayoutFragment is not { } anchorFragment || positioned.Parent is not { } containingBlock)
            return false;

        var rect = anchorFragment.GetRectInRoot();
        for (var ancestor = anchorFragment.Parent; ancestor is not null && !ReferenceEquals(ancestor, containingBlock); ancestor = ancestor.Parent)
        {
            if (ancestor is not BoxFragment { ClipsOverflow: true } clipper || ancestor.Parent is null)
                continue;

            var clip = clipper.OverflowClipRect.Translate(clipper.GetOffsetInRoot());
            var left = Math.Max(rect.Left, clip.Left);
            var top = Math.Max(rect.Top, clip.Top);
            var right = Math.Min(rect.Right, clip.Right);
            var bottom = Math.Min(rect.Bottom, clip.Bottom);
            var hasArea = rect.Width > 0 && rect.Height > 0;
            if (hasArea ? right <= left || bottom <= top : right < left || bottom < top)
                return true;
        }

        return false;
    }

    /// <summary>
    /// "At the time that ResizeObserver events are determined and delivered, the box must record the last successful position option"
    /// (https://drafts.csswg.org/css-anchor-position-1/#record-the-last-successful-position-option): the option used by the layout.
    /// </summary>
    private void RecordLastSuccessfulPositionOptions()
    {
        if (_anchorPositionedBoxes is null)
            return;

        foreach (var state in _anchorPositionedBoxes)
        {
            if (state.HasFallbacks)
            {
                state.LastSuccessfulOption = state.UsedOption;
            }
        }
    }

    /// <summary>
    /// At the start of a rendering update (after the scroll steps, before the animation frame callbacks, as the browsers take the
    /// snapshot of the scroll offsets "each frame, before style recalc"): the position fallback of the anchor-positioned boxes is determined again when their
    /// default scroll shift makes them overflow their inset-modified containing block (https://drafts.csswg.org/css-anchor-position-1/#fallback-apply:
    /// "When a positioned box (after applying any default scroll shift) overflows its inset-modified containing block"), and the
    /// visibility of the boxes is updated (their anchors may have been scrolled out of view).
    /// </summary>
    internal void UpdateAnchorPositionedBoxesForRendering()
    {
        if (_anchorPositionedBoxes is not { Count: > 0 } boxes || NeedsLayout)
            return;

        UpdateForceHiddenBoxes(boxes);
        foreach (var state in boxes)
        {
            if (state.Fragment is not { } fragment)
                continue;

            if (!state.HasFallbacks || fragment.AnchorScrollShift == PhysicalOffset.Zero)
                continue;

            var marginBox = fragment.MarginBoxRect.Translate(fragment.Offset);
            var imcb = state.Follower?.GetInsetModifiedContainingBlock(state.InsetModifiedContainingBlock, fragment.AnchorScrollShift) ?? state.InsetModifiedContainingBlock;
            const double Epsilon = 0.01;
            if (marginBox.Left < imcb.Left - Epsilon || marginBox.Top < imcb.Top - Epsilon || marginBox.Right > imcb.Right + Epsilon || marginBox.Bottom > imcb.Bottom + Epsilon)
            {
                SetNeedsLayout();
                return;
            }
        }
    }
}
