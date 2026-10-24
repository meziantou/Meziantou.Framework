using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The positioned layout (https://drafts.csswg.org/css-position-3/): the containing blocks of the positioned boxes, the relative
/// offsets, and the absolute positioning layout model (https://drafts.csswg.org/css-position-3/#abspos-layout).
/// </summary>
/// <remarks>
/// <para>
/// An absolutely positioned box is not laid out by the formatting context of its parent: the parent records its static position
/// in its <see cref="FragmentBuilder"/> (an <see cref="OutOfFlowCandidate"/>), and the candidate goes up the fragments (in
/// <see cref="BoxFragment.PendingOutOfFlow"/>, its static position translated in their coordinates) until the fragment of its
/// containing block, which lays it out once its own size is known (<see cref="LayoutOutOfFlowDescendants"/>, called at the end
/// of <see cref="FormattingContext.Layout"/>). Its fragment is a child of the fragment of its containing block, after the
/// in-flow children: the fragment of a fixed positioned box is a child of the root fragment (the viewport), unless an ancestor
/// establishes a containing block for it. The fragment of a box whose containing block is an inline box is a child of the
/// fragment of the block container that contains all the fragments of the inline box (<see cref="GetHost"/>).
/// </para>
/// <para>
/// The relative offsets are applied by the parent when it places the fragments (<see cref="BoxFragment.RelativeOffset"/>, added
/// by <see cref="FragmentBuilder.Build"/>); the sticky offsets are applied after the layout, once the positions of the scroll
/// containers are known (<see cref="StickyPosition"/>).
/// </para>
/// </remarks>
internal static class PositionedLayout
{
    /// <summary>
    /// Returns whether a box is absolutely positioned (https://drafts.csswg.org/css-position-3/#absolute-positioning-scheme):
    /// <c>position: absolute | fixed</c>, or a box rendered in the top layer, whose position "computes to absolute" when it is not
    /// <c>fixed</c> (https://drafts.csswg.org/css-position-4/#top-layer). The table grid box of a positioned table is in the flow
    /// of its table wrapper box.
    /// </summary>
    public static bool IsAbsolutelyPositioned(ContainerBox box)
        => box.IsInTopLayer || (box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Replaced or ContainerBoxKind.TableWrapper && box.Style.Layout.IsAbsolutelyPositioned);

    /// <summary>
    /// Returns the parent of a box for the positioning: the inline box it was split out of (<see cref="ContainerBox.SplitInlineParent"/>),
    /// else its parent box.
    /// </summary>
    public static ContainerBox? GetPositioningParent(ContainerBox box) => box.SplitInlineParent ?? box.Parent;

    /// <summary>
    /// Returns the box that establishes the containing block of an absolutely positioned box
    /// (https://drafts.csswg.org/css-position-3/#def-cb): for <c>position: absolute</c>, the nearest positioned ancestor, or the
    /// nearest ancestor that establishes a containing block for the fixed positioned boxes, or the viewport box (the initial
    /// containing block); for <c>position: fixed</c>, the nearest ancestor that establishes a containing block for the fixed
    /// positioned boxes (a transform, a filter, layout or paint containment, <c>will-change</c>...), or the viewport box (the
    /// viewport). An inline box is represented by its first part.
    /// </summary>
    public static ContainerBox FindContainingBlock(ContainerBox box)
    {
        // https://drafts.csswg.org/css-position-4/#top-layer: "If its position property computes to fixed, its containing block
        // is the viewport; otherwise, it's the initial containing block"
        var isFixed = box.Style.Layout.Position == PositionKind.Fixed;
        var last = box;
        var ancestor = GetPositioningParent(box);
        while (ancestor is not null)
        {
            last = ancestor;
            if (ancestor.Kind == ContainerBoxKind.Viewport)
                return ancestor;

            // (the anonymous fieldset content box is a containing block when its fieldset is one)
            // (the contents box of a slider-like control is positioned: it is the containing block of its thumb)
            if ((!ancestor.IsAnonymous || ancestor.IsFieldsetContent || ancestor.IsFormControlContents) && EstablishesContainingBlock(ancestor, isFixed))
                return ancestor.IsInlineBox ? GetFirstPart(ancestor) : ancestor;

            // https://drafts.csswg.org/css-multicol-1/#column-span: "The containing block of a spanner is the multi-column
            // container": the containing block chain skips the ancestors between them
            ancestor = MultiColumnFlow.GetColumnSpannerContainer(ancestor) ?? GetPositioningParent(ancestor);
        }

        return last;
    }

    /// <summary>Returns whether a box establishes the containing block of its absolutely positioned (or fixed positioned) descendants.</summary>
    private static bool EstablishesContainingBlock(ContainerBox box, bool isFixed)
    {
        // https://drafts.csswg.org/css-tables-3/#abspos-boxes-in-table-root: the containing block of a positioned table is generated
        // by its table wrapper box (its table grid box has the same style)
        if (box.Kind == ContainerBoxKind.TableGrid)
            return false;

        // https://svgwg.org/svg2-draft/embedded.html#ForeignObjectElement: as in the browsers, a foreignObject element is the
        // containing block of its positioned descendants (it is transformed by the SVG layout)
        if (SvgForeignObjectLayout.IsForeignObject(box))
            return true;

        var layout = box.Style.Layout;

        // The transforms, the containment... do not apply to the non-atomic inline boxes; the filters do
        // (https://drafts.fxtf.org/filter-effects/#FilterProperty)
        if (box.IsInlineBox)
        {
            return (!isFixed && layout.IsPositioned) || box.Style.GetComputedValue("filter") is not ("none" or "") || box.Style.GetComputedValue("backdrop-filter") is not ("none" or "")
                || Array.Exists(layout.WillChangeProperties, static property => property is "filter" or "backdrop-filter");
        }

        var establishesFixed = UsedContainment.EstablishesFixedPositionContainingBlock(box);
        if (isFixed)
            return establishesFixed;

        // https://drafts.csswg.org/css-position-3/#absolute-cb: "If no ancestor establishes one, the absolute positioning
        // containing block is the initial containing block": the root element establishes one only when it is positioned
        if (box.IsInTopLayer || layout.IsPositioned || establishesFixed)
            return true;

        // (will-change: position, https://drafts.csswg.org/css-will-change/#will-change)
        return !box.Style.IsRoot && Array.Exists(layout.WillChangeProperties, static property => property == "position");
    }

    private static ContainerBox GetFirstPart(ContainerBox box)
    {
        while (box.PreviousContinuation is { } previous)
        {
            box = previous;
        }

        return box;
    }

    /// <summary>
    /// Returns the box whose layout lays out the absolutely positioned boxes of a containing block: the containing block itself,
    /// or, for an inline box, its block container (the parent of the anonymous blocks that contain its parts when it is split
    /// around block-level boxes), whose fragment contains all the fragments of the inline box.
    /// </summary>
    public static ContainerBox GetHost(ContainerBox containingBlock)
    {
        if (!containingBlock.IsInlineBox)
            return containingBlock;

        var block = containingBlock.Parent;
        while (block is { IsInlineBox: true })
        {
            block = block.Parent;
        }

        if (block is null)
            return containingBlock;

        if (containingBlock.Continuation is not null && block.IsAnonymous && block.Parent is { } parent)
            return parent;

        return block;
    }

    /// <summary>
    /// Returns whether the original display of an absolutely positioned box (before its blockification,
    /// https://drafts.csswg.org/css-display/#blockify) is inline-level: the static position of a box in an inline formatting
    /// context is its position on the line when it is inline-level, and the start of the next line otherwise (as in the browsers).
    /// </summary>
    public static bool IsOriginalDisplayInlineLevel(ComputedStyle style)
    {
        // The initial value of display is inline
        if (!style.TryGetCascadedValue("display", out var cascaded))
            return true;

        var value = cascaded.Value.Value;
        if (value is "initial" or "unset")
            return true;

        return CssDisplay.Canonicalize(value).StartsWith("inline", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns how an absolutely positioned box is aligned in its static-position rectangle in each axis of its static-position
    /// containing block (https://drafts.csswg.org/css-position-3/#resolving-inset-auto): its <c>justify-self</c> and
    /// <c>align-self</c>, whose <c>auto</c> behaves as the <c>justify-items</c>/<c>align-items</c> of its parent box "when determining
    /// the static position" (https://drafts.csswg.org/css-align-3/#valdef-justify-self-auto); "ignore overflow alignment, and treat
    /// normal as start and any baseline or stretch alignment value as its fallback alignment".
    /// </summary>
    /// <remarks>
    /// This is the static position in a block container or in an inline box (the flex and grid layouts compute the static
    /// positions of their children). As in the browsers, the <c>*-items</c> properties of the parent are only used when they
    /// would apply to the box in flow: the <c>justify-items</c> of a block container (https://drafts.csswg.org/css-align-3/#justify-block),
    /// whose <c>align-items</c> does not apply to its block-level children; none of them in an inline box
    /// (WPT css/css-align/abspos/align-items-static-position-001.tentative.html, -002).
    /// </remarks>
    /// <param name="box">The absolutely positioned box.</param>
    /// <param name="parent">The parent box of the box (its <c>justify-items</c> applies when it is a block container).</param>
    /// <param name="staticPositionContainingBlock">The writing mode and the direction of the static-position containing block.</param>
    public static (StaticPositionEdge Inline, StaticPositionEdge Block) GetStaticPositionAlignment(ContainerBox box, ContainerBox parent, WritingDirection staticPositionContainingBlock)
    {
        var layout = box.Style.Layout;
        var parentLayout = parent.Kind == ContainerBoxKind.Viewport || parent.IsInlineBox ? null : parent.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);
        return (
            ToEdge(BoxAlignment.GetJustifySelf(layout, parentLayout), AlignmentAxis.Inline(staticPositionContainingBlock)),
            ToEdge(BoxAlignment.GetAlignSelf(layout, parent: null), AlignmentAxis.Block(staticPositionContainingBlock)));

        StaticPositionEdge ToEdge(Alignment value, in AlignmentAxis axis) => BoxAlignment.ResolveSelfAlignment(value.Keyword, axis, writingDirection, AxisAlignment.Start) switch
        {
            AxisAlignment.End or AxisAlignment.LastBaseline => StaticPositionEdge.End,
            AxisAlignment.Center => StaticPositionEdge.Center,
            _ => StaticPositionEdge.Start,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Relative positioning
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets the relative offset of the fragment of a box laid out in the given constraint space (its containing block): the
    /// offset of a relatively positioned box (https://drafts.csswg.org/css-position-3/#relpos-insets), plus the offsets of the
    /// relatively positioned inline boxes a block-level box was split out of (https://drafts.csswg.org/css2/#anonymous-block-level:
    /// "relative positioning [...] of the inline box affects the block-level box").
    /// </summary>
    public static void SetRelativeOffset(ContainerBox box, BoxFragment fragment, in ConstraintSpace space)
    {
        // The table wrapper box is offset (its table grid box has the same style)
        if (box.Kind == ContainerBoxKind.TableGrid || IsAbsolutelyPositioned(box))
            return;

        var containingBlock = space.WritingDirection;
        var width = containingBlock.IsHorizontal ? space.PercentageInlineSize : space.PercentageBlockSize;
        var height = containingBlock.IsHorizontal ? space.PercentageBlockSize : space.PercentageInlineSize;
        var offset = PhysicalOffset.Zero;
        var layout = box.Style.Layout;
        if (layout.Position == PositionKind.Relative && !box.IsAnonymous)
        {
            offset = GetRelativeOffset(layout, containingBlock, width, height, out var insets);
            fragment.ResolvedInsets = insets;
        }

        for (var inline = box.SplitInlineParent; inline is { IsInlineBox: true }; inline = inline.SplitInlineParent ?? inline.Parent)
        {
            if (inline.Style.Layout.Position == PositionKind.Relative)
            {
                offset += GetRelativeOffset(inline.Style.Layout, containingBlock, width, height, out _);
            }
        }

        fragment.RelativeOffset = offset;
    }

    /// <summary>
    /// Returns the offset of a relatively positioned box (https://drafts.csswg.org/css-position-3/#relpos-insets): "If opposing
    /// inset properties in an axis both compute to auto, their used values are zero. If only one is auto, its used value becomes
    /// the negation of the other, and the box is shifted by the specified amount. If neither is auto, the position is
    /// over-constrained; [...] the computed end side value is ignored, and its used value becomes the negation of the start
    /// side" (in the writing mode of the containing block). The percentages refer to the size of the containing block; a
    /// percentage of an indefinite size behaves as <c>auto</c> (https://drafts.csswg.org/css2/#position-props).
    /// </summary>
    /// <param name="layout">The style of the box.</param>
    /// <param name="containingBlock">The writing mode and the direction of the containing block.</param>
    /// <param name="width">The width of the containing block, or null when it is indefinite.</param>
    /// <param name="height">The height of the containing block, or null when it is indefinite.</param>
    /// <param name="resolvedInsets">The resolved values of the insets (https://drafts.csswg.org/cssom/#resolved-values): the used values, and the specified values when the box is over-constrained.</param>
    public static PhysicalOffset GetRelativeOffset(LayoutStyle layout, WritingDirection containingBlock, double? width, double? height, out Sides<double?> resolvedInsets)
    {
        var inset = layout.Insets.Inset;
        var left = Resolve(inset.Left, width);
        var right = Resolve(inset.Right, width);
        var top = Resolve(inset.Top, height);
        var bottom = Resolve(inset.Bottom, height);
        var horizontalStart = containingBlock.IsHorizontal ? containingBlock.InlineStart : containingBlock.BlockStart;
        var verticalStart = containingBlock.IsHorizontal ? containingBlock.BlockStart : containingBlock.InlineStart;
        resolvedInsets = new Sides<double?>(top ?? -(bottom ?? 0), right ?? -(left ?? 0), bottom ?? -(top ?? 0), left ?? -(right ?? 0));
        return new PhysicalOffset(GetAxisOffset(left, right, horizontalStart == PhysicalSide.Left), GetAxisOffset(top, bottom, verticalStart == PhysicalSide.Top));

        static double? Resolve(LengthPercentageAuto value, double? basis) => value.IsAuto ? null : value.Value.TryResolve(basis);

        static double GetAxisOffset(double? lowSide, double? highSide, bool lowSideIsStart) => (lowSide, highSide) switch
        {
            (null, null) => 0,
            (null, { } high) => -high,
            ({ } low, null) => low,
            ({ } low, { } high) => lowSideIsStart ? low : -high,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Absolute positioning
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lays out the absolutely positioned descendants of a box whose containing block is the box (or an inline box whose
    /// fragments are in the fragment of the box, see <see cref="GetHost"/>), once its size is known: the candidates of its
    /// formatting context (<see cref="FragmentBuilder.AddOutOfFlowCandidate(ContainerBox, LogicalOffset, PhysicalOffset)"/>) and the ones of the fragments of its children,
    /// in tree order. Their fragments are appended to the children of the fragment; the other candidates are left in
    /// <see cref="BoxFragment.PendingOutOfFlow"/> for an ancestor. The viewport box lays out all the remaining candidates.
    /// </summary>
    public static void LayoutOutOfFlowDescendants(LayoutContext context, ContainerBox box, BoxFragment fragment, FragmentBuilder builder)
    {
        var own = builder.TakeOutOfFlowCandidates();
        if (own is null && !fragment.HasPendingOutOfFlowDescendants)
            return;

        // The candidates in tree order: the ones of the formatting context precede the child they were added before
        var candidates = new List<OutOfFlowCandidate>();
        var ownIndex = 0;
        var children = fragment.ChildrenSpan;
        for (var i = 0; i < children.Length; i++)
        {
            while (own is not null && ownIndex < own.Count && own[ownIndex].ChildIndex <= i)
            {
                candidates.Add(own[ownIndex++].Candidate);
            }

            CollectCandidates(children[i], children[i].Offset, candidates);
        }

        while (own is not null && ownIndex < own.Count)
        {
            candidates.Add(own[ownIndex++].Candidate);
        }

        var isViewport = box.Kind == ContainerBoxKind.Viewport;
        List<BoxFragment>? laidOut = null;
        List<OutOfFlowCandidate>? pending = null;

        // The boxes whose containing block is an inline box of the host are laid out first: their containing block is laid out
        // before the boxes whose containing block is the host (an anchor-positioned box finds the boxes laid out before it,
        // https://drafts.csswg.org/css-anchor-position-1/#acceptable-anchor-element); the fragments are appended in tree order
        var fragments = new List<BoxFragment?>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            fragments.Add(null);
        }

        // The deepest inline containing blocks first (pass 0 is the last one: the candidates whose containing block is the host)
        var maxDepth = 0;
        foreach (var candidate in candidates)
        {
            if (!ReferenceEquals(candidate.ContainingBlock, box) && candidate.ContainingBlock.IsInlineBox && ReferenceEquals(candidate.Host, box))
            {
                maxDepth = Math.Max(maxDepth, GetInlineDepth(candidate.ContainingBlock, box));
            }
        }

        var hasInlineContainingBlocks = false;
        for (var pass = maxDepth; pass >= 0; pass--)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (fragments[i] is not null)
                    continue;

                if (!isViewport && !ReferenceEquals(candidate.Host, box))
                {
                    if (pending is null || !pending.Contains(candidate))
                    {
                        candidate.StaticPositionContainer ??= fragment;
                        (pending ??= []).Add(candidate);
                    }

                    continue;
                }

                var inlineContainingBlock = !ReferenceEquals(candidate.ContainingBlock, box) && candidate.ContainingBlock.IsInlineBox;
                if (pass != (inlineContainingBlock ? GetInlineDepth(candidate.ContainingBlock, box) : 0))
                    continue;

                hasInlineContainingBlocks |= inlineContainingBlock;
                var result = LayoutAbsolutelyPositionedBox(context, candidate, box, fragment, laidOut);

                // https://drafts.csswg.org/css-overflow-4/#line-clamp: a box whose containing block is an inline box that starts after the
                // clamp point of a line-clamp container is hidden (its containing block is after the clamp point)
                if (inlineContainingBlock && IsInlineStartHidden(fragment, candidate.ContainingBlock, hidden: false) == true)
                {
                    result.IsHiddenForPaint = true;
                }

                (laidOut ??= []).Add(result);
                fragments[i] = result;

                // The descendants of the box whose containing block is an ancestor (e.g. a fixed positioned box in an absolutely
                // positioned box) continue from its fragment, in tree order (they follow the box, before its next siblings)
                if (result.PendingOutOfFlow is { } nested)
                {
                    var index = i + 1;
                    foreach (var nestedCandidate in nested)
                    {
                        nestedCandidate.StaticPositionContainer ??= result;
                        nestedCandidate.StaticPosition += result.Offset;
                        candidates.Insert(index, nestedCandidate);
                        fragments.Insert(index, null);
                        index++;
                    }

                    result.PendingOutOfFlow = null;
                }
            }
        }

        if (laidOut is not null)
        {
            if (hasInlineContainingBlocks)
            {
                laidOut.Clear();
                foreach (var laidOutFragment in fragments)
                {
                    if (laidOutFragment is not null)
                    {
                        laidOut.Add(laidOutFragment);
                    }
                }
            }

            fragment.AppendOutOfFlowChildren(laidOut);
        }

        fragment.PendingOutOfFlow = pending;
    }

    /// <summary>
    /// Lays out the absolutely positioned boxes of the strip of a multi-column container (see <see cref="MultiColumnFormattingContext"/>)
    /// whose containing block is an inline box whose fragments are children of the strip (the multi-column container is their
    /// block container): the containing block is formed by the fragments of the inline box before they are fragmented ("the
    /// first box fragment" and "the end-most box fragment(s)" in the stitched coordinates of the fragmentation context,
    /// https://drafts.csswg.org/css-position-3/#absolute-cb), and the fragments of the boxes are fragmented with the strip.
    /// The candidates laid out are removed from <paramref name="candidates"/>; returns the children of the strip followed by
    /// their fragments.
    /// </summary>
    internal static Fragment[] LayoutInlineContainingBlockCandidatesOfStrip(LayoutContext context, ContainerBox box, Fragment[] children, PhysicalSize stripSize, List<(OutOfFlowCandidate Candidate, PhysicalOffset StaticPosition)> candidates)
    {
        if (!candidates.Exists(entry => IsInStrip(entry.Candidate, box)))
            return children;

        // (the fragment of the strip: the host of the inline boxes; the fragments of the boxes are not appended to it)
        var strip = new BoxFragment(box, stripSize, default, default, default, children);
        var laidOut = new List<BoxFragment>();
        var maxDepth = 0;
        foreach (var (candidate, _) in candidates)
        {
            if (IsInStrip(candidate, box))
            {
                maxDepth = Math.Max(maxDepth, GetInlineDepth(candidate.ContainingBlock, box));
            }
        }

        // The deepest inline containing blocks first, as in LayoutOutOfFlowDescendants
        var fragments = new List<BoxFragment?>();
        for (var pass = maxDepth; pass >= 1; pass--)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var (candidate, staticPosition) = candidates[i];
                if (i < fragments.Count && fragments[i] is not null || !IsInStrip(candidate, box) || GetInlineDepth(candidate.ContainingBlock, box) != pass)
                    continue;

                candidate.StaticPosition = staticPosition;
                var result = LayoutAbsolutelyPositionedBox(context, candidate, box, strip, laidOut);
                laidOut.Add(result);
                while (fragments.Count <= i)
                {
                    fragments.Add(null);
                }

                fragments[i] = result;

                // The descendants of the box whose containing block is an ancestor continue from its fragment
                if (result.PendingOutOfFlow is { } nested)
                {
                    var index = i + 1;
                    foreach (var nestedCandidate in nested)
                    {
                        nestedCandidate.StaticPositionContainer ??= result;
                        nestedCandidate.StaticPosition += result.Offset;
                        candidates.Insert(index, (nestedCandidate, nestedCandidate.StaticPosition));
                        fragments.Insert(index, null);
                        maxDepth = Math.Max(maxDepth, IsInStrip(nestedCandidate, box) ? GetInlineDepth(nestedCandidate.ContainingBlock, box) : 0);
                        index++;
                    }

                    result.PendingOutOfFlow = null;
                }
            }
        }

        var all = new Fragment[children.Length + laidOut.Count];
        children.CopyTo(all, 0);
        var count = children.Length;
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (i < fragments.Count && fragments[i] is not null)
            {
                candidates.RemoveAt(i);
            }
        }

        foreach (var fragment in fragments)
        {
            if (fragment is not null)
            {
                all[count++] = fragment;
            }
        }

        return all;

        static bool IsInStrip(OutOfFlowCandidate candidate, ContainerBox box)
            => candidate.ContainingBlock.IsInlineBox && ReferenceEquals(candidate.Host, box) && !ReferenceEquals(candidate.ContainingBlock, box);
    }

    /// <summary>Returns the number of inline boxes from an inline containing block to its host (1 for an inline box child of the host).</summary>
    private static int GetInlineDepth(ContainerBox inline, ContainerBox host)
    {
        var depth = 0;
        for (var current = inline; current is not null && !ReferenceEquals(current, host); current = current.Parent)
        {
            if (current.IsInlineBox)
            {
                depth++;
            }
        }

        return Math.Max(1, depth);
    }

    /// <summary>Moves the candidates of a fragment (and of the atomic inlines of a line box or an inline box) to a list, translating their static positions by <paramref name="offset"/>.</summary>
    internal static void CollectCandidates(Fragment fragment, PhysicalOffset offset, List<OutOfFlowCandidate> candidates)
    {
        if (fragment is BoxFragment box)
        {
            if (box.PendingOutOfFlow is { } pending)
            {
                foreach (var candidate in pending)
                {
                    candidate.StaticPosition += offset;
                    candidates.Add(candidate);
                }

                box.PendingOutOfFlow = null;
            }

            if (!box.Box.IsInlineBox || !box.HasPendingOutOfFlowDescendants)
                return;
        }
        else if (fragment is not LineBoxFragment || !fragment.HasPendingOutOfFlowDescendants)
        {
            return;
        }

        foreach (var child in fragment.ChildrenSpan)
        {
            CollectCandidates(child, offset + child.Offset, candidates);
        }
    }

    /// <summary>
    /// Lays out an absolutely positioned box in its containing block (https://drafts.csswg.org/css-position-3/#abspos-layout)
    /// and returns its fragment, positioned in the coordinates of the fragment of <paramref name="host"/>.
    /// </summary>
    /// <remarks>
    /// The algorithm of CSS Position 3, which gives the results of CSS 2.1 §10.3.7, §10.3.8, §10.6.4 and §10.6.5 in every
    /// writing mode: in each axis of the containing block, the insets give the inset-modified containing block (the static
    /// position replaces two <c>auto</c> insets), the automatic size is the stretch-fit size when both insets are set (a
    /// non-replaced box that is not a table), else the fit-content size, then the <c>auto</c> margins share the remaining space
    /// and the box is aligned in the inset-modified containing block with its self-alignment properties
    /// (https://drafts.csswg.org/css-position-3/#abspos-alignment, https://drafts.csswg.org/css-align-3/#justify-abspos).
    /// </remarks>
    private static BoxFragment LayoutAbsolutelyPositionedBox(LayoutContext context, OutOfFlowCandidate candidate, ContainerBox host, BoxFragment hostFragment, List<BoxFragment>? laidOut)
    {
        var (containingBlockRect, containingBlock) = GetContainingBlockRect(context, candidate, host, hostFragment, out var relativeOffset);
        if (relativeOffset != PhysicalOffset.Zero)
        {
            // The containing block is a relatively positioned inline box: the box moves with it (the fragmentation places the
            // box from its position before the relative positioning, as it does for the other boxes)
            var result = LayoutAbsolutelyPositionedBoxCore(context, candidate, hostFragment, laidOut, containingBlockRect, containingBlock);
            result.RelativeOffset += relativeOffset;
            return result;
        }

        return LayoutAbsolutelyPositionedBoxCore(context, candidate, hostFragment, laidOut, containingBlockRect, containingBlock);
    }

    private static BoxFragment LayoutAbsolutelyPositionedBoxCore(LayoutContext context, OutOfFlowCandidate candidate, BoxFragment hostFragment, List<BoxFragment>? laidOut, PhysicalRect containingBlockRect, WritingDirection containingBlock)
    {
        if (candidate.StaticPositionIsContainingBlock)
        {
            // The static-position rectangle is the containing block (a grid area, https://drafts.csswg.org/css-align-3/#staticpos-rect)
            var staticX = candidate.CentersHorizontally ? containingBlockRect.X + (containingBlockRect.Width / 2) : candidate.StaticHorizontalEdge == PhysicalSide.Right ? containingBlockRect.Right : containingBlockRect.Left;
            var staticY = candidate.CentersVertically ? containingBlockRect.Y + (containingBlockRect.Height / 2) : candidate.StaticVerticalEdge == PhysicalSide.Bottom ? containingBlockRect.Bottom : containingBlockRect.Top;
            candidate.StaticPosition = new PhysicalOffset(staticX, staticY);
        }

        // https://drafts.csswg.org/css-anchor-position-1/: the box is positioned and sized relative to its anchor boxes
        if (AnchorPositioning.IsAnchorPositioned(candidate.Box))
            return AnchorPositioning.Layout(context, candidate, hostFragment, laidOut, containingBlockRect, containingBlock);

        AnchorPositioning.UpdateTransitionsOfFormerAnchoredBox(context, candidate.Box);
        AnchorPositioning.UpdateInheritedStyles(candidate.Box);
        return LayoutInContainingBlock(context, candidate, containingBlockRect, containingBlock, anchor: null).Fragment;
    }

    /// <summary>
    /// Lays out an absolutely positioned box in a containing block (in the coordinates of the fragment of its host) and returns its
    /// fragment and its inset-modified containing block. <paramref name="anchor"/> gives the effects of the anchor positioning
    /// (https://drafts.csswg.org/css-anchor-position-1/): <c>position-area</c> (its containing block is the area), <c>anchor-center</c>.
    /// </summary>
    internal static AbsoluteLayoutResult LayoutInContainingBlock(LayoutContext context, OutOfFlowCandidate candidate, PhysicalRect containingBlockRect, WritingDirection containingBlock, AnchorLayoutParameters? anchor, bool measureOnly = false)
    {
        var box = candidate.Box;
        var layout = box.Style.Layout;
        var boxModel = layout.Box;
        var containingBlockSize = containingBlock.ToLogical(containingBlockRect.Size);
        var insets = layout.Insets.Inset;
        var inlineAnchor = anchor is null ? default : containingBlock.IsHorizontal ? anchor.Horizontal : anchor.Vertical;
        var blockAnchor = anchor is null ? default : containingBlock.IsHorizontal ? anchor.Vertical : anchor.Horizontal;

        // https://drafts.csswg.org/css-anchor-position-1/#position-area: "The used value of any auto inset properties and auto
        // margin properties resolves to 0" (also for anchor-center, https://drafts.csswg.org/css-anchor-position-1/#anchor-center)
        var inlineSingleAutoInset = GetSingleAutoInset(insets, containingBlock.InlineStart, containingBlock.InlineEnd);
        var blockSingleAutoInset = GetSingleAutoInset(insets, containingBlock.BlockStart, containingBlock.BlockEnd);
        insets = ZeroAutoInsets(insets, containingBlock.InlineStart, containingBlock.InlineEnd, inlineAnchor.ZeroAutoInsets);
        insets = ZeroAutoInsets(insets, containingBlock.BlockStart, containingBlock.BlockEnd, blockAnchor.ZeroAutoInsets);

        // https://drafts.csswg.org/css-position-3/#resolving-insets: the inset-modified containing block, in each axis of the containing block
        var inlineAxis = ResolveAxis(containingBlock.InlineStart, containingBlock.InlineEnd, containingBlockSize.Inline, insets, candidate, containingBlockRect);
        var blockAxis = ResolveAxis(containingBlock.BlockStart, containingBlock.BlockEnd, containingBlockSize.Block, insets, candidate, containingBlockRect);
        inlineAxis = inlineAxis with { LimitStart = ToAxisCoordinate(inlineAnchor.LimitLow, inlineAnchor.LimitHigh, containingBlock.InlineStart, containingBlockRect, isStart: true), LimitEnd = ToAxisCoordinate(inlineAnchor.LimitLow, inlineAnchor.LimitHigh, containingBlock.InlineStart, containingBlockRect, isStart: false) };
        blockAxis = blockAxis with { LimitStart = ToAxisCoordinate(blockAnchor.LimitLow, blockAnchor.LimitHigh, containingBlock.BlockStart, containingBlockRect, isStart: true), LimitEnd = ToAxisCoordinate(blockAnchor.LimitLow, blockAnchor.LimitHigh, containingBlock.BlockStart, containingBlockRect, isStart: false) };
        var imcbRect = GetInsetModifiedContainingBlock(inlineAxis, blockAxis, containingBlock, containingBlockRect);
        if (measureOnly)
            return new AbsoluteLayoutResult(null!, imcbRect, inlineAxis.IsCorrected || blockAxis.IsCorrected);

        // https://drafts.csswg.org/css-align-3/#justify-abspos: the self-alignment in the axes of the containing block; "auto
        // behaves as normal [...] when determining the actual position of an absolutely positioned box"
        var writingDirection = WritingDirection.FromStyle(layout);
        var inlineAlignment = AbsoluteAlignment.Resolve(layout.Alignment.JustifySelf, AlignmentAxis.Inline(containingBlock), writingDirection);
        var blockAlignment = AbsoluteAlignment.Resolve(layout.Alignment.AlignSelf, AlignmentAxis.Block(containingBlock), writingDirection);
        if (anchor is not null)
        {
            inlineAlignment = AnchorPositioning.ResolveAlignment(inlineAlignment, layout.Alignment.JustifySelf, inlineAnchor, AlignmentAxis.Inline(containingBlock), inlineSingleAutoInset, containingBlockRect);
            blockAlignment = AnchorPositioning.ResolveAlignment(blockAlignment, layout.Alignment.AlignSelf, blockAnchor, AlignmentAxis.Block(containingBlock), blockSingleAutoInset, containingBlockRect);
        }

        // https://drafts.csswg.org/css-position-3/#abspos-auto-size: "If its self-alignment property in the relevant axis is
        // stretch and neither of its inset properties in that axis are auto, or if it is normal and the box is non-replaced, not
        // a table wrapper box, and has no auto inset in the relevant axis, its automatic size is its stretch-fit size. Otherwise,
        // its automatic size is its fit-content size."
        var margin = FormattingContext.ResolveMargins(boxModel, containingBlockSize.Inline);

        // (the form controls are not replaced elements: as in the browsers, they stretch like the other boxes)
        var canStretch = box.Kind == ContainerBoxKind.Normal || (box.Kind == ContainerBoxKind.Replaced && ReplacedContent.Get(context, box).Kind == ReplacedContentKind.FormControl);
        double? stretchInline = inlineAxis.BothInsets && inlineAlignment.Stretches(canStretch) ? inlineAxis.Size - margin.Get(containingBlock.InlineStart) - margin.Get(containingBlock.InlineEnd) : null;
        double? stretchBlock = blockAxis.BothInsets && blockAlignment.Stretches(canStretch) ? blockAxis.Size - margin.Get(containingBlock.BlockStart) - margin.Get(containingBlock.BlockEnd) : null;

        // "If the box has an aspect-ratio, then an automatic size in the ratio-dependent axis is instead resolved as a max-content
        // size. When both axes have an automatic size, if only one axis has an auto inset then that axis is the ratio-dependent
        // axis" (the block axis otherwise). When only one axis has an automatic size, it is the ratio-dependent axis
        // (https://drafts.csswg.org/css-sizing-4/#ratio-dependent-axis): its size is transferred from the size of the other axis.
        // As in the browsers, an explicit stretch alignment keeps the stretch-fit size.
        if (boxModel.AspectRatio.HasRatio && (boxModel.Width.IsAuto || boxModel.Height.IsAuto) && (stretchInline is not null || stretchBlock is not null))
        {
            var isOrthogonal = writingDirection.IsOrthogonalTo(containingBlock);
            var ownInlineAxis = isOrthogonal ? blockAxis : inlineAxis;
            var ownBlockAxis = isOrthogonal ? inlineAxis : blockAxis;
            var inlineIsRatioDependent = boxModel.Width.IsAuto && boxModel.Height.IsAuto
                ? !ownInlineAxis.BothInsets && ownBlockAxis.BothInsets
                : boxModel.GetSize(writingDirection.IsHorizontal).IsAuto;
            if (inlineIsRatioDependent == isOrthogonal)
            {
                if (blockAlignment.IsNormal)
                {
                    stretchBlock = null;
                }
            }
            else if (inlineAlignment.IsNormal)
            {
                stretchInline = null;
            }
        }

        // As in Blink and WebKit, the fit-content size of a table is not computed in an inset-modified containing block that is
        // larger than the containing block (negative insets, a static position before the containing block): the available
        // inline size is at most the inline size of the containing block (WPT css/css-tables/absolute-tables-008.tentative.html)
        var availableInlineSize = box.Kind == ContainerBoxKind.TableWrapper ? Math.Min(inlineAxis.Size, containingBlockSize.Inline) : inlineAxis.Size;
        var space = new ConstraintSpace
        {
            WritingDirection = containingBlock,
            AvailableInlineSize = availableInlineSize,
            AvailableBlockSize = blockAxis.Size,
            PercentageInlineSize = containingBlockSize.Inline,
            PercentageBlockSize = containingBlockSize.Block,
            StretchInlineSize = stretchInline,
            StretchBlockSize = stretchBlock,
        };

        var fragment = context.LayoutBox(box, space);
        var size = containingBlock.ToLogical(fragment.Size);

        // https://drafts.csswg.org/css-position-3/#abspos-margins: the auto margins, then the position in the inset-modified containing block
        var usedMargin = fragment.Margin;
        var specifiedMargin = boxModel.Margin;

        // The overflow limit rect is only used by the default overflow alignment of the aligned boxes
        var (inlineLimitStart, inlineLimitEnd) = inlineAlignment.IsNormal && inlineAlignment.AnchorCenter is null ? default : GetOverflowLimits(candidate, containingBlock, containingBlock.InlineStart);
        var (blockLimitStart, blockLimitEnd) = blockAlignment.IsNormal && blockAlignment.AnchorCenter is null ? default : GetOverflowLimits(candidate, containingBlock, containingBlock.BlockStart);
        var (inlinePosition, inlineStartMargin, inlineEndMargin) = inlineAxis.Place(size.Inline, usedMargin.Get(containingBlock.InlineStart), usedMargin.Get(containingBlock.InlineEnd),
            specifiedMargin.Get(containingBlock.InlineStart).IsAuto && !inlineAnchor.ZeroAutoInsets, specifiedMargin.Get(containingBlock.InlineEnd).IsAuto && !inlineAnchor.ZeroAutoInsets, isInlineAxis: true, inlineAlignment, inlineLimitStart, inlineLimitEnd);
        var (blockPosition, blockStartMargin, blockEndMargin) = blockAxis.Place(size.Block, usedMargin.Get(containingBlock.BlockStart), usedMargin.Get(containingBlock.BlockEnd),
            specifiedMargin.Get(containingBlock.BlockStart).IsAuto && !blockAnchor.ZeroAutoInsets, specifiedMargin.Get(containingBlock.BlockEnd).IsAuto && !blockAnchor.ZeroAutoInsets, isInlineAxis: false, blockAlignment, blockLimitStart, blockLimitEnd);

        usedMargin = usedMargin.With(containingBlock.InlineStart, inlineStartMargin).With(containingBlock.InlineEnd, inlineEndMargin)
            .With(containingBlock.BlockStart, blockStartMargin).With(containingBlock.BlockEnd, blockEndMargin);
        fragment.Margin = usedMargin;

        var x = 0d;
        var y = 0d;
        SetCoordinate(containingBlock.InlineStart, inlinePosition, size.Inline, ref x, ref y);
        SetCoordinate(containingBlock.BlockStart, blockPosition, size.Block, ref x, ref y);
        fragment.Offset = new PhysicalOffset(x, y);

        // https://drafts.csswg.org/cssom/#resolved-values: the used values of the insets, the computed values when over-constrained
        var resolved = default(Sides<double?>)
            .With(containingBlock.InlineStart, inlineAxis.InsetStart ?? inlinePosition - inlineStartMargin)
            .With(containingBlock.InlineEnd, inlineAxis.InsetEnd ?? inlineAxis.ContainingBlockSize - inlinePosition - size.Inline - inlineEndMargin)
            .With(containingBlock.BlockStart, blockAxis.InsetStart ?? blockPosition - blockStartMargin)
            .With(containingBlock.BlockEnd, blockAxis.InsetEnd ?? blockAxis.ContainingBlockSize - blockPosition - size.Block - blockEndMargin);
        fragment.ResolvedInsets = resolved;
        return new AbsoluteLayoutResult(fragment, imcbRect, inlineAxis.IsCorrected || blockAxis.IsCorrected);

        void SetCoordinate(PhysicalSide start, double position, double length, ref double x, ref double y)
        {
            switch (start)
            {
                case PhysicalSide.Left:
                    x = containingBlockRect.X + position;
                    break;
                case PhysicalSide.Right:
                    x = containingBlockRect.Right - position - length;
                    break;
                case PhysicalSide.Top:
                    y = containingBlockRect.Y + position;
                    break;
                default:
                    y = containingBlockRect.Bottom - position - length;
                    break;
            }
        }
    }

    /// <summary>
    /// Returns how far the overflow limit rect of the default overflow alignment of an absolutely positioned box extends beyond its
    /// containing block in an axis (https://drafts.csswg.org/css-align-3/#auto-safety-position): "if the original containing block
    /// is generated by a scroll container (and is not its fixed containing block), the overflow limit rect is extended to infinity
    /// in the scrollable axises in any direction that does not extend into the unreachable scrollable overflow region". The initial
    /// containing block is generated by the viewport, a scroll container; the viewport is the fixed containing block of the fixed
    /// positioned boxes (their limit is extended by <see cref="AnchorPositioning"/> when their default anchor box scrolls with the
    /// contents). Returns whether the limit is infinite on the start side and on the end side of the axis (<paramref name="start"/>
    /// is its start side).
    /// </summary>
    private static (bool Start, bool End) GetOverflowLimits(OutOfFlowCandidate candidate, WritingDirection containingBlockDirection, PhysicalSide start)
    {
        var containingBlock = candidate.ContainingBlock;
        var horizontal = start is PhysicalSide.Left or PhysicalSide.Right;
        if (containingBlock.Kind == ContainerBoxKind.Viewport)
        {
            if (candidate.Box.Style.Layout.Position == PositionKind.Fixed)
                return default;

            var viewportOverflow = ScrollContainers.GetViewportOverflow(ScrollContainers.GetViewportStyles(containingBlock).Overflow);
            if (!ScrollContainers.IsScrollable(horizontal ? viewportOverflow.X : viewportOverflow.Y))
                return default;

            // The writing mode and the direction of the viewport are the ones of the root element
            var viewportOrigin = ScrollContainers.GetScrollOrigin(containingBlock, containingBlockDirection);
            var viewportOriginSide = (viewportOrigin.First is PhysicalSide.Left or PhysicalSide.Right) == horizontal ? viewportOrigin.First : viewportOrigin.Second;
            return viewportOriginSide == start ? (false, true) : (true, false);
        }

        if (containingBlock.IsInlineBox || !ScrollContainers.IsScrollContainer(containingBlock))
            return default;

        var (overflowX, overflowY) = ScrollContainers.GetUsedOverflow(containingBlock);
        if (!ScrollContainers.IsScrollable(horizontal ? overflowX : overflowY))
            return default;

        // The scrollable overflow beyond the sides of the scroll origin is unreachable
        var origin = ScrollContainers.GetScrollOrigin(containingBlock, WritingDirection.FromStyle(containingBlock.Style.Layout));
        var originSide = (origin.First is PhysicalSide.Left or PhysicalSide.Right) == horizontal ? origin.First : origin.Second;
        return originSide == start ? (false, true) : (true, false);
    }

    /// <summary>
    /// Returns the containing block of a candidate (its padding box, https://drafts.csswg.org/css-position-3/#absolute-cb) in the
    /// coordinates of the fragment of the host, and its writing mode and direction.
    /// </summary>
    /// <param name="relativeOffset">Receives the relative offset of the containing block in the fragment of the host when it is an inline box (the rectangle includes it).</param>
    private static (PhysicalRect Rect, WritingDirection WritingDirection) GetContainingBlockRect(LayoutContext context, OutOfFlowCandidate candidate, ContainerBox host, BoxFragment hostFragment, out PhysicalOffset relativeOffset)
    {
        relativeOffset = PhysicalOffset.Zero;
        var containingBlock = candidate.ContainingBlock;
        if (containingBlock.IsInlineBox && !ReferenceEquals(containingBlock, host))
            return (GetInlineContainingBlockRect(containingBlock, hostFragment, out relativeOffset), WritingDirection.FromStyle(containingBlock.Style.Layout));

        // https://drafts.csswg.org/css-grid-2/#abspos-items: "If an absolutely positioned element's containing block is generated
        // by a grid container, the containing block corresponds to the grid area determined by its grid-placement properties"
        if (ReferenceEquals(containingBlock, host) && host.LayoutState is GridContainerState { } grid && grid.Pass == context.Pass && FormattingContext.GetKind(host) == FormattingContextKind.Grid)
        {
            var writingDirection = hostFragment.WritingDirection;
            var paddingBox = writingDirection.ToLogical(hostFragment.PaddingBoxRect, hostFragment.Size);
            return (writingDirection.ToPhysical(grid.GetAbsoluteArea(candidate.Box, paddingBox), hostFragment.Size), writingDirection);
        }

        // https://drafts.csswg.org/css-view-transitions-1/#snapshot-containing-block: the containing block of ::view-transition is
        // the snapshot containing block, the viewport with its scrollbar gutters (the fragment of the viewport box is the
        // initial containing block, placed after the gutters of the start edges)
        if (candidate.Box.IsViewTransitionRoot)
            return (new PhysicalRect(-hostFragment.Offset.X, -hostFragment.Offset.Y, context.ViewportSize.Width, context.ViewportSize.Height), WritingDirection.HorizontalLtr);

        // The viewport box is the initial containing block (and the viewport, the scroll position is not subtracted yet: #347)
        return (hostFragment.PaddingBoxRect, hostFragment.WritingDirection);
    }

    /// <summary>
    /// Returns the containing block formed by an inline box (https://drafts.csswg.org/css-position-3/#absolute-cb): "using the
    /// writing mode of that box, [...] a rectangle from the start-most [padding] edges (in both axes) of the first box fragment of
    /// the ancestor, and the end-most [padding] edges of the end-most box fragment(s) of the ancestor in each axis" (the padding
    /// edges of CSS 2.1 §10.1 and of the browsers), in the coordinates of the fragment that contains its fragments.
    /// </summary>
    /// <param name="relativeOffset">Receives the sum of the relative offsets of the first fragment of the inline box and of the fragments that contain it in the host.</param>
    private static PhysicalRect GetInlineContainingBlockRect(ContainerBox inline, BoxFragment hostFragment, out PhysicalOffset relativeOffset)
    {
        // The fragments on the empty lines (e.g. the parts of an inline box split around block-level boxes that only contain
        // out-of-flow boxes) are ignored, as in the browsers, unless all the fragments are on empty lines
        var fragments = new InlineFragments();
        FindInlineFragments(hostFragment, PhysicalOffset.Zero, PhysicalOffset.Zero, inline, includeEmptyLines: false, line: null, ref fragments);
        if (fragments.FirstLine is null)
        {
            FindInlineFragments(hostFragment, PhysicalOffset.Zero, PhysicalOffset.Zero, inline, includeEmptyLines: true, line: null, ref fragments);
        }

        relativeOffset = fragments.FirstRelativeOffset;
        if (fragments.FirstLine is null)
            return new PhysicalRect(hostFragment.ContentBoxRect.Offset, PhysicalSize.Zero);

        var firstRect = fragments.First;
        var last = fragments.Last;
        var writingDirection = WritingDirection.FromStyle(inline.Style.Layout);
        var left = GetEdge(PhysicalSide.Left);
        var top = GetEdge(PhysicalSide.Top);
        var right = GetEdge(PhysicalSide.Right);
        var bottom = GetEdge(PhysicalSide.Bottom);
        return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

        double GetEdge(PhysicalSide side)
        {
            var rect = side == writingDirection.InlineStart || side == writingDirection.BlockStart ? firstRect : last;
            return side switch
            {
                PhysicalSide.Left => rect.Left,
                PhysicalSide.Top => rect.Top,
                PhysicalSide.Right => rect.Right,
                _ => rect.Bottom,
            };
        }

        static void FindInlineFragments(Fragment fragment, PhysicalOffset offset, PhysicalOffset relative, ContainerBox inline, bool includeEmptyLines, LineBoxFragment? line, ref InlineFragments fragments)
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                var childOffset = offset + child.Offset;
                if (child is BoxFragment box)
                {
                    var childBox = box.Box;
                    var childRelative = relative + box.RelativeOffset;
                    if (line is not null && childBox.IsInlineBox && ReferenceEquals(childBox.Element, inline.Element) && childBox.PseudoElement == inline.PseudoElement && (includeEmptyLines || !line.Size.IsEmpty))
                    {
                        fragments.Add(line, box.PaddingBoxRect.Translate(childOffset), childRelative);
                    }

                    // The parts of a split inline box are in anonymous blocks; the inline boxes contain the nested ones
                    if (childBox.IsInlineBox || childBox.IsAnonymous && childBox.IsBlockContainer)
                    {
                        FindInlineFragments(child, childOffset, childRelative, inline, includeEmptyLines, line, ref fragments);
                    }
                }
                else if (child is LineBoxFragment childLine)
                {
                    FindInlineFragments(child, childOffset, relative, inline, includeEmptyLines, childLine, ref fragments);
                }
            }
        }
    }

    /// <summary>
    /// The fragments of an inline box that form its containing block: the union of its fragments on the first line box that has
    /// some and the union of its fragments on the last one (the bidirectional reordering splits an inline box in several
    /// fragments on a line; as in Blink, the edges are the ones of all the fragments of the line).
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct InlineFragments
    {
        public LineBoxFragment? FirstLine;
        public LineBoxFragment? LastLine;
        public PhysicalRect First;
        public PhysicalRect Last;

        /// <summary>The sum of the relative offsets of the first fragment and of the fragments that contain it.</summary>
        public PhysicalOffset FirstRelativeOffset;

        public void Add(LineBoxFragment line, PhysicalRect rect, PhysicalOffset relativeOffset)
        {
            if (FirstLine is null)
            {
                FirstLine = line;
                First = rect;
                FirstRelativeOffset = relativeOffset;
            }
            else if (ReferenceEquals(FirstLine, line))
            {
                First = Union(First, rect);
            }

            Last = ReferenceEquals(LastLine, line) ? Union(Last, rect) : rect;
            LastLine = line;
        }

        private static PhysicalRect Union(PhysicalRect a, PhysicalRect b)
        {
            var left = Math.Min(a.Left, b.Left);
            var top = Math.Min(a.Top, b.Top);
            return new PhysicalRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
        }
    }

    /// <summary>
    /// Returns whether the first fragment of an inline box in a fragment is hidden after the clamp point of a line-clamp container
    /// (<see cref="Fragment.IsHiddenForPaint"/>: it is in a hidden line box or block), or null when the inline box has no fragment.
    /// </summary>
    private static bool? IsInlineStartHidden(Fragment fragment, ContainerBox inline, bool hidden)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            var childHidden = hidden || child.IsHiddenForPaint;
            if (child is BoxFragment { Box: var childBox })
            {
                if (childBox.IsInlineBox && ReferenceEquals(childBox.Element, inline.Element) && childBox.PseudoElement == inline.PseudoElement)
                    return childHidden;

                if ((childBox.IsInlineBox || childBox.IsAnonymous && childBox.IsBlockContainer) && IsInlineStartHidden(child, inline, childHidden) is { } result)
                    return result;
            }
            else if (child is LineBoxFragment && IsInlineStartHidden(child, inline, childHidden) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Returns the side whose inset is not <c>auto</c> when exactly one inset of an axis is <c>auto</c>, else null.</summary>
    private static PhysicalSide? GetSingleAutoInset(Sides<LengthPercentageAuto> insets, PhysicalSide start, PhysicalSide end)
    {
        var startIsAuto = insets.Get(start).IsAuto;
        var endIsAuto = insets.Get(end).IsAuto;
        return startIsAuto == endIsAuto ? null : startIsAuto ? end : start;
    }

    /// <summary>Replaces the <c>auto</c> insets of an axis by 0 when <paramref name="zero"/> is true.</summary>
    private static Sides<LengthPercentageAuto> ZeroAutoInsets(Sides<LengthPercentageAuto> insets, PhysicalSide start, PhysicalSide end, bool zero)
    {
        if (!zero)
            return insets;

        if (insets.Get(start).IsAuto)
        {
            insets = insets.With(start, LengthPercentage.Zero);
        }

        if (insets.Get(end).IsAuto)
        {
            insets = insets.With(end, LengthPercentage.Zero);
        }

        return insets;
    }

    /// <summary>Converts the physical bounds of an overflow limit (in the coordinates of the host) to a distance from the start side of an axis of the containing block.</summary>
    private static double? ToAxisCoordinate(double? low, double? high, PhysicalSide start, PhysicalRect containingBlockRect, bool isStart)
    {
        if (low is not { } lowValue || high is not { } highValue)
            return null;

        return start switch
        {
            PhysicalSide.Left => (isStart ? lowValue : highValue) - containingBlockRect.Left,
            PhysicalSide.Right => containingBlockRect.Right - (isStart ? highValue : lowValue),
            PhysicalSide.Top => (isStart ? lowValue : highValue) - containingBlockRect.Top,
            _ => containingBlockRect.Bottom - (isStart ? highValue : lowValue),
        };
    }

    /// <summary>Returns the inset-modified containing block in the coordinates of the host.</summary>
    private static PhysicalRect GetInsetModifiedContainingBlock(in AbsoluteAxis inlineAxis, in AbsoluteAxis blockAxis, WritingDirection containingBlock, PhysicalRect containingBlockRect)
    {
        double left = 0, top = 0, right = 0, bottom = 0;
        Set(containingBlock.InlineStart, inlineAxis.ImcbStart, inlineAxis.ImcbStart + inlineAxis.Size);
        Set(containingBlock.BlockStart, blockAxis.ImcbStart, blockAxis.ImcbStart + blockAxis.Size);
        return new PhysicalRect(left, top, right - left, bottom - top);

        void Set(PhysicalSide start, double from, double to)
        {
            switch (start)
            {
                case PhysicalSide.Left:
                    (left, right) = (containingBlockRect.Left + from, containingBlockRect.Left + to);
                    break;
                case PhysicalSide.Right:
                    (left, right) = (containingBlockRect.Right - to, containingBlockRect.Right - from);
                    break;
                case PhysicalSide.Top:
                    (top, bottom) = (containingBlockRect.Top + from, containingBlockRect.Top + to);
                    break;
                default:
                    (top, bottom) = (containingBlockRect.Bottom - to, containingBlockRect.Bottom - from);
                    break;
            }
        }
    }

    /// <summary>Resolves the insets of an axis of the containing block (https://drafts.csswg.org/css-position-3/#resolving-insets).</summary>
    private static AbsoluteAxis ResolveAxis(PhysicalSide start, PhysicalSide end, double containingBlockSize, Sides<LengthPercentageAuto> insets, OutOfFlowCandidate candidate, PhysicalRect containingBlockRect)
    {
        var startInset = insets.Get(start);
        var endInset = insets.Get(end);
        double? insetStart = startInset.IsAuto ? null : startInset.Value.Resolve(containingBlockSize);
        double? insetEnd = endInset.IsAuto ? null : endInset.Value.Resolve(containingBlockSize);
        double imcbStart;
        double imcbEnd;
        var alignEnd = false;
        var alignCenter = false;
        var isSafe = false;
        var safeAlignsEnd = false;
        if (insetStart is null && insetEnd is null)
        {
            // "If both inset properties in a given axis are auto, then [...] for self-start alignment or its equivalent, set its
            // start-edge inset property to the static position, and its end-edge inset property to zero" (the static position
            // is on the side of the start edge of the static-position containing block)
            var horizontal = start is PhysicalSide.Left or PhysicalSide.Right;
            var staticEdge = horizontal ? candidate.StaticHorizontalEdge : candidate.StaticVerticalEdge;
            var staticPosition = candidate.StaticPosition;
            var fromStart = start switch
            {
                PhysicalSide.Left => staticPosition.X - containingBlockRect.X,
                PhysicalSide.Right => containingBlockRect.Right - staticPosition.X,
                PhysicalSide.Top => staticPosition.Y - containingBlockRect.Y,
                _ => containingBlockRect.Bottom - staticPosition.Y,
            };

            // A safe alignment at the static position (see OutOfFlowCandidate.IsSafeHorizontally)
            var isCentered = horizontal ? candidate.CentersHorizontally : candidate.CentersVertically;
            if (horizontal ? candidate.IsSafeHorizontally : candidate.IsSafeVertically)
            {
                isSafe = true;
                safeAlignsEnd = (horizontal ? candidate.SafeHorizontalEdge : candidate.SafeVerticalEdge) != start;
            }

            if (isCentered)
            {
                // "for center alignment, let start-distance be the distance from the center of its static-position rectangle to the
                // start edge of its containing block, and end-distance be the distance from the center of its static-position
                // rectangle to the end edge of its containing block. If start-distance is less than or equal to end-distance, then
                // set the start-edge inset property to zero, and set the end-edge inset property to (containing block size - 2 ×
                // |start-distance|); otherwise, set the end-edge inset property to zero and the start-edge inset property to
                // (containing block size - 2 × |end-distance|)."
                // The absolute values mirror a static position that is outside of the containing block (e.g. a flex container
                // below the initial containing block): as in the browsers, the box stays centered on its static position, in an
                // inset-modified containing block of zero size
                var startDistance = fromStart;
                var endDistance = containingBlockSize - fromStart;
                var halfSize = Math.Max(0, Math.Min(startDistance, endDistance));
                imcbStart = fromStart - halfSize;
                imcbEnd = containingBlockSize - fromStart - halfSize;
                alignCenter = true;
            }
            else if (staticEdge == start)
            {
                imcbStart = fromStart;
                imcbEnd = 0;
            }
            else
            {
                imcbStart = 0;
                imcbEnd = containingBlockSize - fromStart;
                alignEnd = true;
            }
        }
        else if (insetStart is null)
        {
            // "If only one inset property in a given axis is auto, it is set to zero", and the box is aligned to the edge of
            // the stronger inset
            imcbStart = 0;
            imcbEnd = insetEnd!.Value;
            alignEnd = true;
        }
        else
        {
            imcbStart = insetStart.Value;
            imcbEnd = insetEnd ?? 0;
        }

        // "If the inset-modified containing block size in any axis is less than zero, then the weaker inset in the affected axis
        // is reduced (possibly becoming negative) to bring that size up to zero"
        var corrected = containingBlockSize - imcbStart - imcbEnd < 0;
        if (corrected)
        {
            if (alignEnd)
            {
                imcbStart = containingBlockSize - imcbEnd;
            }
            else
            {
                imcbEnd = containingBlockSize - imcbStart;
            }
        }

        return new AbsoluteAxis(containingBlockSize, insetStart, insetEnd, imcbStart, imcbEnd, alignEnd)
        {
            AlignCenter = alignCenter,
            IsCorrected = corrected,
            IsSafeAtStaticPosition = isSafe,
            SafeAlignsEnd = safeAlignsEnd,
        };
    }

    /// <summary>An axis of the containing block of an absolutely positioned box, with its inset-modified containing block.</summary>
    /// <param name="ContainingBlockSize">The size of the containing block in the axis.</param>
    /// <param name="InsetStart">The start inset in px, or null when it is <c>auto</c>.</param>
    /// <param name="InsetEnd">The end inset in px, or null when it is <c>auto</c>.</param>
    /// <param name="ImcbStart">The distance of the start edge of the inset-modified containing block from the start edge of the containing block.</param>
    /// <param name="ImcbEnd">The distance of the end edge of the inset-modified containing block from the end edge of the containing block.</param>
    /// <param name="AlignEnd">Whether the box is aligned with the end edge of the inset-modified containing block (the stronger inset, or the static position).</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct AbsoluteAxis(double ContainingBlockSize, double? InsetStart, double? InsetEnd, double ImcbStart, double ImcbEnd, bool AlignEnd)
    {
        /// <summary>Whether the inset-modified containing block had a negative size, corrected to zero (https://drafts.csswg.org/css-position-3/#resolving-insets).</summary>
        public bool IsCorrected { get; init; }

        /// <summary>
        /// The start of the overflow limit rect of the default overflow alignment (https://drafts.csswg.org/css-align-3/#auto-safety-position)
        /// from the start of the containing block, when it is not the bounding box of the containing block and of the inset-modified
        /// containing block: the original (or scrollable) containing block of a box with <c>position-area</c> or <c>anchor-center</c>.
        /// </summary>
        public double? LimitStart { get; init; }

        /// <summary>The end of the overflow limit rect (see <see cref="LimitStart"/>).</summary>
        public double? LimitEnd { get; init; }

        /// <summary>Whether the box is centered in the inset-modified containing block (its static position is the center of its margin box).</summary>
        public bool AlignCenter { get; init; }

        /// <summary>
        /// Whether the alignment of the box at its static position is <c>safe</c> (https://drafts.csswg.org/css-align-3/#overflow-values):
        /// a margin box that is larger than the inset-modified containing block is aligned with the start edge of that block,
        /// in the writing mode of the static-position containing block (see <see cref="OutOfFlowCandidate.IsSafeHorizontally"/>).
        /// </summary>
        public bool IsSafeAtStaticPosition { get; init; }

        /// <summary>Whether the start edge of the fallback alignment of <see cref="IsSafeAtStaticPosition"/> is on the end side of the axis.</summary>
        public bool SafeAlignsEnd { get; init; }

        /// <summary>Whether neither inset is <c>auto</c>.</summary>
        public bool BothInsets => InsetStart is not null && InsetEnd is not null;

        /// <summary>The size of the inset-modified containing block.</summary>
        public double Size => Math.Max(0, ContainingBlockSize - ImcbStart - ImcbEnd);

        /// <summary>
        /// Resolves the <c>auto</c> margins (https://drafts.csswg.org/css-position-3/#abspos-margins), aligns the margin box in the
        /// inset-modified containing block (https://drafts.csswg.org/css-position-3/#abspos-alignment), and returns the position of
        /// the border box from the start edge of the containing block, and the used margins.
        /// </summary>
        public (double Position, double StartMargin, double EndMargin) Place(double size, double startMargin, double endMargin, bool startIsAuto, bool endIsAuto, bool isInlineAxis,
            AbsoluteAlignment alignment, bool infiniteLimitStart, bool infiniteLimitEnd)
        {
            if (!BothInsets)
            {
                // "If either inset property in the relevant axis is auto, then any auto margins resolve to zero"; "If the computed
                // value of either inset property in an axis is auto, then its margin box is aligned to the edge of the inset-modified
                // containing block corresponding to its stronger inset (even if this would overflow its containing block)"
                if (alignment.AnchorCenter is null)
                {
                    // A safe alignment at the static position: the margin box that overflows the inset-modified containing block
                    // is aligned with its start edge
                    if (IsSafeAtStaticPosition && size + startMargin + endMargin > ContainingBlockSize - ImcbStart - ImcbEnd + 1e-7)
                        return (SafeAlignsEnd ? ContainingBlockSize - ImcbEnd - endMargin - size : ImcbStart + startMargin, startMargin, endMargin);

                    if (AlignCenter)
                        return (ImcbStart + (ContainingBlockSize - ImcbStart - ImcbEnd - size - startMargin - endMargin) / 2 + startMargin, startMargin, endMargin);

                    return (AlignEnd ? ContainingBlockSize - ImcbEnd - endMargin - size : ImcbStart + startMargin, startMargin, endMargin);
                }
            }

            // "the remaining space is calculated as the size of its inset-modified containing block in the relevant axis minus the
            // box's used size in the relevant axis, and this remaining space is divided among any auto margins. If, in the inline
            // axis, the remaining space is negative and both margins are auto, the start margin resolves to zero and the end
            // margin receives the remaining space."
            var remaining = Size - size - startMargin - endMargin;
            var outerSize = size + startMargin + endMargin;

            // https://drafts.csswg.org/css-anchor-position-1/#anchor-center: the margin box is centered on the center of the default
            // anchor box, as far as it stays in the inset-modified containing block (as in the browsers), and the default overflow
            // alignment keeps it in the overflow limit rect when it does not fit
            if (alignment.AnchorCenter is { } anchorCenter)
            {
                var centered = anchorCenter - (outerSize / 2);
                if (alignment.Overflow == OverflowAlignment.Unsafe)
                    return (centered + startMargin, startMargin, endMargin);

                if (remaining >= 0)
                    return (Math.Clamp(centered, ImcbStart, ImcbStart + remaining) + startMargin, startMargin, endMargin);

                if (alignment.Overflow == OverflowAlignment.Safe)
                    return (ImcbStart + startMargin, startMargin, endMargin);

                return (GetDefaultOverflowPosition(centered, outerSize, infiniteLimitStart, infiniteLimitEnd) + startMargin, startMargin, endMargin);
            }

            if (startIsAuto || endIsAuto)
            {
                if (startIsAuto && endIsAuto)
                {
                    if (remaining < 0 && isInlineAxis)
                    {
                        endMargin = remaining;
                    }
                    else
                    {
                        startMargin = remaining / 2;
                        endMargin = remaining / 2;
                    }
                }
                else if (startIsAuto)
                {
                    startMargin = remaining;
                }
                else
                {
                    endMargin = remaining;
                }

                return (ImcbStart + startMargin, startMargin, endMargin);
            }

            // "Otherwise, the box is aligned as specified by its self-alignment property in the relevant axis [...], using its
            // margin box as the alignment subject and the inset-modified containing block as the alignment container" (normal:
            // start, the over-constrained box keeps its start inset)
            if (alignment.IsNormal)
                return (ImcbStart + startMargin, startMargin, endMargin);

            var marginBoxStart = ImcbStart + BoxAlignment.GetOffset(alignment.Alignment, remaining, safe: alignment.Overflow == OverflowAlignment.Safe);
            if (remaining < 0 && alignment.Overflow == OverflowAlignment.Default)
            {
                marginBoxStart = GetDefaultOverflowPosition(marginBoxStart, outerSize, infiniteLimitStart, infiniteLimitEnd);
            }

            return (marginBoxStart + startMargin, startMargin, endMargin);
        }

        /// <summary>
        /// https://drafts.csswg.org/css-align-3/#auto-safety-position: "if the alignment subject fits within the overflow limit rect,
        /// align the alignment subject such that it fully covers the inset-modified containing block and is otherwise aligned as
        /// specified to the extent possible without overflowing the overflow limit rect. Otherwise, start-align the alignment subject
        /// within the overflow limit rect." (the bounding rectangle of the inset-modified containing block and of the containing
        /// block, or <see cref="LimitStart"/>/<see cref="LimitEnd"/>)
        /// </summary>
        private double GetDefaultOverflowPosition(double marginBoxStart, double outerSize, bool infiniteLimitStart, bool infiniteLimitEnd)
        {
            var imcbEnd = ContainingBlockSize - ImcbEnd;
            var limitStart = infiniteLimitStart ? double.NegativeInfinity : Math.Min(LimitStart ?? 0, ImcbStart);
            var limitEnd = infiniteLimitEnd ? double.PositiveInfinity : Math.Max(LimitEnd ?? ContainingBlockSize, imcbEnd);
            if (outerSize <= limitEnd - limitStart)
            {
                var min = Math.Max(limitStart, Math.Min(imcbEnd, limitEnd) - outerSize);
                var max = Math.Min(limitEnd - outerSize, Math.Max(ImcbStart, limitStart));
                return Math.Clamp(marginBoxStart, min, Math.Max(min, max));
            }

            return limitStart;
        }
    }

    /// <summary>
    /// The self-alignment of an absolutely positioned box in an axis of its containing block (https://drafts.csswg.org/css-align-3/#justify-abspos):
    /// <c>normal</c> sizes the box as <c>stretch</c> or as <c>start</c> depending on the type of box, and start-aligns it; the
    /// baseline alignments use their fallback alignment.
    /// </summary>
    /// <param name="Alignment">The alignment from the start edge of the axis in the writing mode of the containing block.</param>
    /// <param name="Overflow">The overflow alignment (<c>safe</c>, <c>unsafe</c> or the default one).</param>
    /// <param name="IsNormal">Whether the value is <c>normal</c> (or <c>auto</c>).</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct AbsoluteAlignment(AxisAlignment Alignment, OverflowAlignment Overflow, bool IsNormal)
    {
        /// <summary>
        /// The center of the default anchor box from the start of the containing block, when the box is aligned with <c>anchor-center</c>
        /// (https://drafts.csswg.org/css-anchor-position-1/#anchor-center), else null.
        /// </summary>
        public double? AnchorCenter { get; init; }

        /// <summary>Resolves a computed <c>justify-self</c> or <c>align-self</c> of an absolutely positioned box in an axis of its containing block.</summary>
        public static AbsoluteAlignment Resolve(Alignment value, in AlignmentAxis axis, WritingDirection box)
        {
            var isNormal = value.Keyword is AlignmentKeyword.Auto or AlignmentKeyword.Normal or AlignmentKeyword.Legacy;
            var alignment = BoxAlignment.GetBaselineFallback(BoxAlignment.ResolveSelfAlignment(value.Keyword, axis, box, AxisAlignment.Start), axis);
            return new AbsoluteAlignment(alignment, value.Overflow, isNormal);
        }

        /// <summary>Returns whether the automatic size is the stretch-fit size (when neither inset is <c>auto</c>): <c>stretch</c>, or <c>normal</c> for a box that <paramref name="normalStretches"/>.</summary>
        public bool Stretches(bool normalStretches) => AnchorCenter is null && (Alignment == AxisAlignment.Stretch || (IsNormal && normalStretches));
    }
}
