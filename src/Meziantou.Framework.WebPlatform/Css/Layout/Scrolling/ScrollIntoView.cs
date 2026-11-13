using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Scrolls a target into view (https://drafts.csswg.org/cssom-view/#scroll-a-target-into-view): the scrolling boxes of the
/// containing block chain of the target are scrolled, from the innermost to the outermost (the viewport, then the scrolling
/// boxes of the same-origin parent documents), to align the target with their scrollport.
/// </summary>
internal static class ScrollIntoView
{
    /// <summary>Scrolls an element into view; the task completes when all the scrolls are completed.</summary>
    /// <param name="engine">The layout of the document of the element.</param>
    /// <param name="target">The element.</param>
    /// <param name="behavior">The scroll behavior.</param>
    /// <param name="block">The alignment in the block flow direction of each scrolling box.</param>
    /// <param name="inline">The alignment in the inline base direction of each scrolling box.</param>
    /// <param name="container">The element whose nearest scrolling box is the last one scrolled (<c>container: "nearest"</c>), or null for all of them.</param>
    /// <param name="lastScrollingBox">The last scrolling box scrolled (the scroll container of a scroll marker group, https://drafts.csswg.org/css-overflow-5/#scroll-marker-activation), or null for all of them.</param>
    /// <param name="targetRect">The rectangle to scroll into view, in the coordinates of the document, instead of the bounding border box of the target (a column of a multi-column container: the scrolling box of the target itself is then scrolled too), or null.</param>
    /// <param name="snapsToTarget">
    /// Whether the scroll is intended to scroll to the target (<c>scrollIntoView()</c>, the navigation to a fragment): its nearest
    /// scroll container, when it is a scroll snap container, snaps to one of its snap positions
    /// (https://drafts.csswg.org/css-scroll-snap-1/#choosing); the other scrolls (the focus...) snap to the closest snap position.
    /// </param>
    /// <param name="ifNeeded">
    /// Whether the target is only aligned as requested with a scrolling box when its border box is not visible in its scrollport
    /// (the focus: the alignment of <c>focus()</c> is <c>center</c>, but the browsers do not scroll to a target that is visible,
    /// see <see cref="GetScrollDelta"/>).
    /// </param>
    public static Task Run(LayoutEngine engine, Element target, ScrollBehavior behavior, ScrollLogicalPosition block, ScrollLogicalPosition inline, Element? container, ScrollState? lastScrollingBox = null, PhysicalRect? targetRect = null, bool snapsToTarget = false, bool ifNeeded = false)
    {
        // https://drafts.csswg.org/css-contain-2/#cv-notes: an element that is not available to the user-agent features (in the
        // skipped contents of content-visibility: hidden) is not scrolled to, "as if it did not have a layout box"; the
        // content-visibility: auto ancestors that skip their contents are activated (they stop skipping them, as in the browsers)
        if (BoxGeneration.Get(target).InSkippedContents)
            return Task.CompletedTask;

        if (ContentRelevancy.ActivateAncestors(target))
        {
            engine.SetNeedsLayout();
        }

        // An element of an SVG document fragment is in the box of its outermost svg element
        var outermost = target is SvgElement svg ? CssomViewGeometry.GetOutermostSvgElement(svg) : null;
        var fragments = engine.GetFragments(outermost ?? target);
        if (fragments.Count == 0)
            return Task.CompletedTask;

        // "Let target bounding border box be the box represented by the return value of invoking Element's getBoundingClientRect()",
        // extended by scroll-margin (https://drafts.csswg.org/css-scroll-snap-1/#scroll-margin), in the coordinates of the document
        // A sticky positioned target is scrolled into view at its unshifted position, as in the browsers (WPT
        // position-sticky-scrollIntoView.html)
        PhysicalRect rect;
        Sides<double> margin = default;
        var includeSelf = targetRect is not null;
        if (targetRect is { } givenRect)
        {
            rect = givenRect;
        }
        else if (outermost is not null)
        {
            if (CssomViewGeometry.GetClientQuads(target) is not [var quad, ..])
                return Task.CompletedTask;

            rect = quad.Translate(engine.FromViewport(PhysicalOffset.Zero)).Bounds;
        }
        else
        {
            rect = GetTargetRect(fragments);
            margin = fragments[0].Box.Style.Layout.Overflow.ScrollMargin;
        }

        // The target bounding border box, without the scroll-margin
        var borderRect = rect.Expand(new Sides<double>(-margin.Top, -margin.Right, -margin.Bottom, -margin.Left));

        List<Task>? promises = null;
        Fragment child = fragments[0];
        if (includeSelf && child is BoxFragment { ScrollState: null } targetFragment && BlockFormattingContext.IsFieldset(targetFragment.Box))
        {
            // The scrolling box of a fieldset is the one of its anonymous fieldset content box
            foreach (var content in targetFragment.ChildrenSpan)
            {
                if (content is BoxFragment { Box.IsFieldsetContent: true, ScrollState: not null })
                {
                    child = content;
                    break;
                }
            }
        }

        var isNearestScrollContainer = snapsToTarget && targetRect is null;
        var document = target.NodeDocument;
        while (true)
        {
            // The scroll position of the viewport before the scrolls (the target rectangle is moved by the scrolls below)
            var viewportOffset = engine.ViewportScrollState.Offset;
            var fixedToViewport = false;
            var fixedFollowsViewport = (Horizontal: false, Vertical: false);
            var isInStuckBox = false;
            for (var fragment = includeSelf ? child : child.Parent; fragment is not null; child = fragment, fragment = fragment.Parent)
            {
                // A sticky positioned ancestor of the target that is stuck (shifted) in its scrolling box does not move when this
                // scrolling box scrolls (the target itself is scrolled to its unshifted position, see GetBounds)
                if (!includeSelf && child != fragments[0] && child is BoxFragment { StickyOffset: var stickyOffset } && stickyOffset != PhysicalOffset.Zero)
                {
                    isInStuckBox = true;
                }

                includeSelf = false;
                if (fragment.Parent is null && child is BoxFragment { Box.Style.Layout.Position: PositionKind.Fixed } fixedBox)
                {
                    // A fixed positioned box does not move when the viewport scrolls, except an anchor-positioned box in the axes
                    // where it compensates for the scroll of a default anchor box that scrolls with the document (its default scroll
                    // shift, https://drafts.csswg.org/css-anchor-position-1/#scroll): "a fixed-positioned box with a default anchor
                    // box that scrolls with the content can be scrolled into view when outside the scrollport"
                    // (https://drafts.csswg.org/css-align-3/#auto-safety-position)
                    fixedToViewport = true;
                    fixedFollowsViewport = engine.GetViewportScrollCompensation(fixedBox);
                }

                if (fragment is not BoxFragment { ScrollState: { } state } scrollContainer)
                    continue;

                // The visual viewport of the top-level document zoomed in by the user: the target is aligned with it, and it scrolls
                // in the layout viewport before the layout viewport scrolls (https://drafts.csswg.org/cssom-view/#perform-a-scroll),
                // also to a fixed positioned target (WPT css/cssom-view/visual-scrollIntoView-001.html)
                var visualViewport = state.IsViewport && document is { BrowsingContext: { Parent: null, Container: null }, VisualViewportOrNull: { PinchScale: > 1 } zoomed } ? zoomed : null;
                if (state.IsViewport && fixedToViewport && !fixedFollowsViewport.Horizontal && !fixedFollowsViewport.Vertical && visualViewport is null)
                    continue;

                var isFixedInViewport = state.IsViewport && fixedToViewport;
                if (isFixedInViewport)
                {
                    // The position of the fixed positioned box in the document, with the current scroll position of the viewport
                    rect = rect.Translate(state.Offset);
                    borderRect = borderRect.Translate(state.Offset);
                }

                // The scrollport, in the coordinates of the document. Its scroll-padding is ignored when the target is in a stuck
                // sticky positioned box: the target stays where it is stuck (as in Gecko: WPT css/cssom-view/scrollIntoView-stuck.tentative.html)
                var scrollport = GetScrollport(document, scrollContainer, state, applyScrollPadding: !isInStuckBox);
                isInStuckBox = false;
                var associatedElement = state.GetAssociatedElement(document);
                var visualDelta = PhysicalOffset.Zero;
                if (visualViewport is not null)
                {
                    var pinchOffset = visualViewport.PinchOffset;
                    scrollport = new PhysicalRect(
                        scrollport.X + pinchOffset.X,
                        scrollport.Y + pinchOffset.Y,
                        Math.Max(0, scrollport.Width - state.Scrollport.Width + visualViewport.Width),
                        Math.Max(0, scrollport.Height - state.Scrollport.Height + visualViewport.Height));
                }

                var delta = GetScrollDelta(rect, scrollport, state.WritingDirection, block, inline, ifNeeded ? borderRect : null);
                if (visualViewport is not null)
                {
                    visualDelta = visualViewport.GetReachableDelta(delta);
                    delta -= visualDelta;
                }

                if (isFixedInViewport)
                {
                    delta = new PhysicalOffset(fixedFollowsViewport.Horizontal ? delta.X : 0, fixedFollowsViewport.Vertical ? delta.Y : 0);
                }

                var current = state.Offset;

                // https://drafts.csswg.org/css-overflow-5/#current-scroll-target: "Scrolling operations in the scroll container
                // with a target Element [...] set the current scroll target to that target"
                engine.SetCurrentScrollTarget(state, target);

                // A scroll snap container is scrolled to the snap position closest to the scroll-into-view position (an absolute
                // scroll, https://drafts.csswg.org/css-scroll-snap-1/#choosing), as in the browsers (WPT nested-scrollIntoView-snaps.html,
                // focus-element-no-snap.html)
                // (the nearest scroll container of the target snaps to one of the snap positions of the target, when it has some)
                var strategy = SnapSelectionStrategy.ForEndPosition(state.Clamp(RoundRoundingErrors(current + delta)), scrolledX: true, scrolledY: true);
                var position = isNearestScrollContainer ? engine.GetSnapPosition(state, strategy, target) : engine.GetSnapPosition(state, strategy);
                isNearestScrollContainer = false;
                if (position != current || state.SmoothScroll is not null)
                {
                    (promises ??= []).Add(engine.PerformScroll(state, position, behavior, associatedElement));
                }
                else
                {
                    engine.UpdateScrollSnapChangeTargets(state);
                }

                if (visualDelta != PhysicalOffset.Zero)
                {
                    visualViewport!.ScrollBy(visualDelta);
                }

                // The target moves with the contents of the scrolling box. For the scrolling boxes of the ancestors, the target is
                // the part of its bounding border box in the scrollport (the edge of the scrollport when it is outside of it), extended
                // by its scroll-margin (WPT scroll-margin-overflow-ancestor.html: a target above the scrollport of an inner scroll
                // container that cannot scroll further gets its scroll-margin from the edge of the scrollport)
                rect = rect.Translate(current - position);
                borderRect = borderRect.Translate(current - position);
                if (!state.IsViewport)
                {
                    var visible = state.Scrollport.Translate(scrollContainer.GetOffsetInRoot());
                    var left = Math.Clamp(borderRect.Left, visible.Left, visible.Right);
                    var top = Math.Clamp(borderRect.Top, visible.Top, visible.Bottom);
                    var clipped = new PhysicalRect(left, top, Math.Clamp(borderRect.Right, visible.Left, visible.Right) - left, Math.Clamp(borderRect.Bottom, visible.Top, visible.Bottom) - top);
                    if (clipped != borderRect)
                    {
                        borderRect = clipped;
                        rect = clipped.Expand(margin);
                    }
                }
                if (container is not null || ReferenceEquals(state, lastScrollingBox))
                    return WhenAll(promises);
            }

            // The scrolling boxes of the parent document, when it is same origin
            if (document.BrowsingContext is not { Container: { } frame } || !frame.IsConnected || !Document.IsSameOrigin(frame.NodeDocument, document))
                return WhenAll(promises);

            var parentEngine = frame.NodeDocument.GetLayoutEngine();
            var frameFragments = parentEngine.GetFragments(frame);
            if (frameFragments.Count == 0)
                return WhenAll(promises);

            // The viewport of the document is the content box of the frame, or the rectangle given by object-fit for an SVG
            // document (the target rectangle is in the coordinates of the document after the scrolls: the scroll position of the
            // viewport before them gives its position in the viewport)
            var contentBox = LayoutEngine.GetNestedViewportRect(frameFragments[0]).Translate(frameFragments[0].GetOffsetInRoot());
            rect = rect.Translate(contentBox.Offset - viewportOffset);
            borderRect = borderRect.Translate(contentBox.Offset - viewportOffset);
            engine = parentEngine;
            document = frame.NodeDocument;
            child = frameFragments[0];
        }

        static Task WhenAll(List<Task>? promises) => promises is null ? Task.CompletedTask : Task.WhenAll(promises);
    }

    /// <summary>
    /// Returns the target bounding border box of the fragments of a box in the coordinates of the document (a sticky box at its
    /// unshifted position), extended by its <c>scroll-margin</c> (https://drafts.csswg.org/css-scroll-snap-1/#scroll-margin).
    /// </summary>
    internal static PhysicalRect GetTargetRect(IReadOnlyList<BoxFragment> fragments)
    {
        var rect = GetBounds(fragments[0]);
        for (var i = 1; i < fragments.Count; i++)
        {
            rect = rect.Union(GetBounds(fragments[i]));
        }

        return rect.Expand(fragments[0].Box.Style.Layout.Overflow.ScrollMargin);
    }

    /// <summary>Returns the scrollport of a scrolling box in the coordinates of the document, reduced by its <c>scroll-padding</c>.</summary>
    internal static PhysicalRect GetScrollport(Document document, BoxFragment scrollContainer, ScrollState state, bool applyScrollPadding = true)
    {
        var origin = scrollContainer.GetOffsetInRoot();
        if (state.IsViewport)
        {
            origin += state.Offset;
        }

        var scrollport = state.Scrollport.Translate(origin);
        if (!applyScrollPadding)
            return scrollport;

        var associatedElement = state.GetAssociatedElement(document);
        if (associatedElement is not null && Cascade.ComputedStyleDeclarations.IsBeingRendered(associatedElement))
        {
            scrollport = ApplyScrollPadding(scrollport, document.GetStyleEngine().GetStyle(associatedElement).Layout.Overflow.ScrollPadding);
        }
        else if (state.PseudoElement is not null && state.ContainerStyle is { } containerStyle)
        {
            scrollport = ApplyScrollPadding(scrollport, containerStyle.Overflow.ScrollPadding);
        }

        return scrollport;
    }

    /// <summary>
    /// Removes the rounding errors of the floating-point numbers from a scroll position (the rectangles mapped through the
    /// scrolling boxes accumulate them, e.g. 980.0000000000001: WPT scroll-margin-overflow-ancestor.html).
    /// </summary>
    internal static PhysicalOffset RoundRoundingErrors(PhysicalOffset position) => new(Math.Round(position.X, 6, MidpointRounding.AwayFromZero), Math.Round(position.Y, 6, MidpointRounding.AwayFromZero));

    /// <summary>Returns the scrollport reduced by <c>scroll-padding</c> (https://drafts.csswg.org/css-scroll-snap-1/#scroll-padding: <c>auto</c> is 0, the percentages refer to the scrollport).</summary>
    internal static PhysicalRect ApplyScrollPadding(PhysicalRect scrollport, Sides<LengthPercentageAuto> padding)
    {
        var top = Resolve(padding.Top, scrollport.Height);
        var right = Resolve(padding.Right, scrollport.Width);
        var bottom = Resolve(padding.Bottom, scrollport.Height);
        var left = Resolve(padding.Left, scrollport.Width);
        return new PhysicalRect(scrollport.X + left, scrollport.Y + top, scrollport.Width - left - right, scrollport.Height - top - bottom);

        static double Resolve(LengthPercentageAuto value, double basis) => value.IsAuto ? 0 : value.Value.Resolve(basis);
    }

    /// <summary>
    /// Returns the change of the scroll position of a scrolling box that aligns the target with it
    /// (https://drafts.csswg.org/cssom-view/#determine-the-scroll-into-view-position).
    /// </summary>
    /// <param name="target">The target rectangle, extended by its scroll-margin.</param>
    /// <param name="scrollport">The scrollport, reduced by its scroll-padding.</param>
    /// <param name="writingDirection">The writing direction of the scrolling box.</param>
    /// <param name="block">The alignment in the block flow direction.</param>
    /// <param name="inline">The alignment in the inline base direction.</param>
    /// <param name="visibleTarget">
    /// The target border box when the target is scrolled into view only when needed (the focus), or null. As in Blink, each axis
    /// is aligned as requested only when the border box is outside of the scrollport in that axis: the scrolling box is not
    /// scrolled in the axes where the target is entirely in the scrollport or is larger than it, and else the target is aligned
    /// with the nearest edge (WPT scroll-margin-focus.html, scroll-margin-visibility-check.html,
    /// focus-large-element-in-overflow-hidden-container.html, css/css-scroll-snap/snap-after-relayout/layout-follows-focused-targeted-block.html).
    /// </param>
    public static PhysicalOffset GetScrollDelta(PhysicalRect target, PhysicalRect scrollport, WritingDirection writingDirection, ScrollLogicalPosition block, ScrollLogicalPosition inline, PhysicalRect? visibleTarget = null)
    {
        var dx = 0d;
        var dy = 0d;
        Align(writingDirection.BlockStart, block);
        Align(writingDirection.InlineStart, inline);
        return new PhysicalOffset(dx, dy);

        void Align(PhysicalSide startSide, ScrollLogicalPosition position)
        {
            var horizontal = startSide is PhysicalSide.Left or PhysicalSide.Right;
            var reversed = startSide is PhysicalSide.Right or PhysicalSide.Bottom;
            var (low, high, boxLow, boxHigh) = horizontal ? (target.Left, target.Right, scrollport.Left, scrollport.Right) : (target.Top, target.Bottom, scrollport.Top, scrollport.Bottom);
            if (visibleTarget is { } visible && (horizontal ? Intersects(visible.Left, visible.Right, boxLow, boxHigh) : Intersects(visible.Top, visible.Bottom, boxLow, boxHigh)))
            {
                if ((low >= boxLow && high <= boxHigh) || high - low >= boxHigh - boxLow)
                    return;

                position = ScrollLogicalPosition.Nearest;
            }

            var delta = GetDelta(low, high, boxLow, boxHigh, position, reversed);
            if (horizontal)
            {
                dx = delta;
            }
            else
            {
                dy = delta;
            }
        }
    }

    /// <summary>Whether a range of a target intersects the range of a scrollport in one axis (an empty target, when it is in the range).</summary>
    private static bool Intersects(double low, double high, double boxLow, double boxHigh) => high > low ? high > boxLow && low < boxHigh : low >= boxLow && low <= boxHigh;

    /// <summary>Returns the scroll delta in one axis (the coordinates grow from the low edge to the high edge; <paramref name="reversed"/> when the beginning edge is the high one).</summary>
    private static double GetDelta(double elementLow, double elementHigh, double boxLow, double boxHigh, ScrollLogicalPosition position, bool reversed)
    {
        if (reversed)
            return -GetDelta(-elementHigh, -elementLow, -boxHigh, -boxLow, position, reversed: false);

        // Edge A is the beginning edge (low), edge B the ending edge (high)
        var elementSize = elementHigh - elementLow;
        var boxSize = boxHigh - boxLow;
        switch (position)
        {
            case ScrollLogicalPosition.Start:
                return elementLow - boxLow;
            case ScrollLogicalPosition.End:
                return elementHigh - boxHigh;
            case ScrollLogicalPosition.Center:
                return ((elementLow + elementHigh) / 2) - ((boxLow + boxHigh) / 2);
        }

        // nearest (an element as large as the scrolling box is aligned like a larger one, as in the browsers: WPT
        // scrollIntoView-horizontal-partially-visible.html)
        var outsideA = elementLow < boxLow;
        var outsideB = elementHigh > boxHigh;
        if (outsideA && outsideB)
            return 0;

        if ((outsideA && elementSize < boxSize) || (outsideB && elementSize >= boxSize))
            return elementLow - boxLow;

        if ((outsideA && elementSize >= boxSize) || (outsideB && elementSize < boxSize))
            return elementHigh - boxHigh;

        return 0;
    }

    /// <summary>The bounding box of the border box of a fragment in the coordinates of the document, with the transforms, at its unshifted sticky position.</summary>
    private static PhysicalRect GetBounds(BoxFragment fragment) => LayoutEngine.MapToRoot(fragment, fragment.BorderBoxRect).Bounds.Translate(-fragment.StickyOffset);
}
