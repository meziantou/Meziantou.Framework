using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The geometry of the scroll-driven timelines (https://drafts.csswg.org/scroll-animations-1/): the scroll container of a
/// timeline, its scroll offsets along the axis of the timeline, and the position of the subject of a view progress timeline
/// in its scrollport. The positions are measured from the scroll origin (https://drafts.csswg.org/css-overflow-3/#scroll-origin),
/// so they increase while scrolling towards the end of the scrollable overflow in any writing mode.
/// </summary>
internal static class ScrollTimelineGeometry
{
    /// <summary>
    /// Returns the nearest ancestor scroll container of an element that is scrollable in the axis (the <c>nearest</c> value of
    /// <c>scroll()</c> and the source of a view progress timeline, https://drafts.csswg.org/scroll-animations-1/#scroll-notation):
    /// its ancestors in the containing block chain, then the root element (the document viewport). The logical axis is resolved
    /// with the writing mode of the containing block of the element, as in the browsers (WPT
    /// scroll-timeline-source-single-axis-*); for the viewport, with the writing mode of the viewport (<see cref="ScrollTimelineSource.Horizontal"/>
    /// is then null). The source is null when the element does not generate a box. The layout is computed when it is out of date.
    /// </summary>
    public static ScrollTimelineSource FindNearestScrollContainer(Element element, string? pseudoElement, ScrollAxis axis)
    {
        var document = element.NodeDocument;
        if (!element.IsConnected || !document.IsFullyActive || document.BrowsingContext is null)
            return default;

        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        var box = pseudoElement is null ? engine.BoxTree?.GetPrincipalBox(element) : engine.BoxTree?.GetPseudoElementBox(element, pseudoElement);
        ContainerBox? containingBlock;
        if (box is not null)
        {
            containingBlock = GetContainingBlockChainParent(box);
        }
        else if (pseudoElement is null && GetSvgSubjectRoot(engine, element) is { } svgRoot)
        {
            // An element of an SVG document fragment without CSS box (a rendered SVG graphics element): its ancestors are the
            // ones of the box of its outermost svg element
            containingBlock = svgRoot.Box;
        }
        else
        {
            return default;
        }

        var horizontal = containingBlock is null or { Kind: ContainerBoxKind.Viewport } ? false : IsHorizontal(axis, WritingDirection.FromStyle(containingBlock.Style.Layout));
        for (var ancestor = containingBlock; ancestor is not null; ancestor = GetContainingBlockChainParent(ancestor))
        {
            if (ancestor.Kind == ContainerBoxKind.Viewport)
                break;

            if (ancestor.IsAnonymous || ancestor.Element is not { } ancestorElement || ancestor.PseudoElement is not null)
                continue;

            if (ReferenceEquals(ancestorElement, document.DocumentElement))
                break;

            if (ScrollContainers.IsScrollContainer(ancestor) && ScrollContainers.GetUsedOverflow(ancestor) is var overflow && ScrollContainers.IsScrollable(horizontal ? overflow.X : overflow.Y))
                return new ScrollTimelineSource(ancestorElement, horizontal);
        }

        return new ScrollTimelineSource(GetViewportSource(document), Horizontal: null);
    }

    /// <summary>
    /// Returns the first fragment of the outermost svg element of an element of an SVG document fragment (a rendered SVG element
    /// without CSS box, or a foreignObject element, whose box is placed by the SVG layout), or null for the other elements.
    /// </summary>
    private static BoxFragment? GetSvgSubjectRoot(LayoutEngine engine, Element element)
    {
        if (element is not SvgElement svg || CssomViewGeometry.GetOutermostSvgElement(svg) is not { } outermost)
            return null;

        return engine.GetFragments(outermost) is [var fragment, ..] ? fragment : null;
    }

    /// <summary>The element that represents the document viewport as a source: the scrolling element (the body element in quirks mode), or the root element.</summary>
    public static Element? GetViewportSource(Document document) => document.ScrollingElement ?? document.DocumentElement;

    /// <summary>The next box of the containing block chain: the containing block of an absolutely positioned box, the parent box otherwise.</summary>
    private static ContainerBox? GetContainingBlockChainParent(ContainerBox box)
    {
        if (box.IsOutOfFlow && box.Style.Layout.Position is PositionKind.Absolute or PositionKind.Fixed)
            return PositionedLayout.FindContainingBlock(box);

        return box.Parent;
    }

    /// <summary>Whether the axis of a timeline is the horizontal axis for a scroll container in the writing mode.</summary>
    private static bool IsHorizontal(ScrollAxis axis, WritingDirection writingDirection) => axis switch
    {
        ScrollAxis.X => true,
        ScrollAxis.Y => false,
        ScrollAxis.Inline => writingDirection.IsHorizontal,
        _ => !writingDirection.IsHorizontal,
    };


    /// <summary>Returns the scrolling box of a source: the viewport for the root element and the scrolling element of the document.</summary>
    private static ScrollState? GetScrollState(LayoutEngine engine, Element source)
    {
        var document = source.NodeDocument;
        if (ReferenceEquals(source, document.DocumentElement) || ReferenceEquals(source, document.ScrollingElement))
            return engine.ViewportScrollState is { Fragment: not null } viewport ? viewport : null;

        return engine.GetScrollState(source);
    }

    private static double GetZoom(LayoutEngine engine, ScrollState state)
    {
        var element = state.Element ?? engine.Document.DocumentElement;
        if (element is null)
            return 1;

        var zoom = engine.Document.GetStyleEngine().GetStyle(element).Layout.EffectiveZoom;
        return zoom > 0 ? zoom : 1;
    }

    /// <summary>The scroll offsets of a scrolling box along an axis: the current one and the largest one, from the scroll origin.</summary>
    private static (double Offset, double Max, bool Reversed) GetScrollOffsets(ScrollState state, bool horizontal)
    {
        var min = horizontal ? state.MinOffset.X : state.MinOffset.Y;
        var max = horizontal ? state.MaxOffset.X : state.MaxOffset.Y;
        var offset = horizontal ? state.Offset.X : state.Offset.Y;

        // The scrollable overflow extends leftwards or upwards (e.g. direction: rtl): the scroll offsets are negative
        var reversed = min < 0;
        return (reversed ? max - offset : offset - min, max - min, reversed);
    }

    /// <summary>
    /// Computes the state of a scroll progress timeline (https://drafts.csswg.org/scroll-animations-1/#scroll-timeline-progress):
    /// it is inactive when its source does not generate a scroll container that is scrollable in its axis, or when it has no
    /// scrollable overflow.
    /// </summary>
    public static ScrollTimelineSnapshot ComputeScrollTimeline(Element? source, ScrollAxis axis, bool? horizontalAxis = null)
    {
        if (source is null)
            return ScrollTimelineSnapshot.Inactive;

        var document = source.NodeDocument;
        if (!source.IsConnected || !document.IsFullyActive || document.BrowsingContext is null)
            return new ScrollTimelineSnapshot { Source = source };

        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        if (GetScrollState(engine, source) is not { } state)
            return new ScrollTimelineSnapshot { Source = source };

        var horizontal = horizontalAxis ?? IsHorizontal(axis, state.WritingDirection);
        if (!ScrollContainers.IsScrollable(horizontal ? state.Overflow.X : state.Overflow.Y))
            return new ScrollTimelineSnapshot { Source = source };

        var (offset, max, _) = GetScrollOffsets(state, horizontal);
        var zoom = GetZoom(engine, state);
        return new ScrollTimelineSnapshot
        {
            IsActive = max > 0,
            Source = source,
            ScrollOffset = offset / zoom,
            MaxScrollOffset = max / zoom,
            SourceZoom = zoom,
        };
    }

    /// <summary>
    /// Computes the state of a view progress timeline (https://drafts.csswg.org/scroll-animations-1/#view-timeline-progress):
    /// the position of the border box of the subject in the scrollport of its nearest ancestor scroll container, when its
    /// scroll offset is 0. The transforms are ignored, the relative and absolute positioning are accounted for. The subject box
    /// of an element of an SVG document fragment is its bounding box, with the transforms of the SVG document fragment. An inset is
    /// null for <c>auto</c> (the <c>scroll-padding</c> of the scroll container); its lengths are CSS px, zoomed by the subject.
    /// </summary>
    public static ScrollTimelineSnapshot ComputeViewTimeline(Element subject, string? pseudoElement, ScrollAxis axis, LengthPercentage? startInset, LengthPercentage? endInset)
    {
        if (FindNearestScrollContainer(subject, pseudoElement, axis) is not { Element: { } source } nearest)
            return ScrollTimelineSnapshot.Inactive;

        var engine = subject.NodeDocument.GetLayoutEngine();
        if (GetScrollState(engine, source) is not { } state)
            return new ScrollTimelineSnapshot { Source = source };

        var horizontal = nearest.Horizontal ?? IsHorizontal(axis, state.WritingDirection);
        if (!ScrollContainers.IsScrollable(horizontal ? state.Overflow.X : state.Overflow.Y))
            return new ScrollTimelineSnapshot { Source = source };

        PhysicalRect rect;
        Fragment? subjectFragment = null;
        if (pseudoElement is null && GetSvgSubjectRoot(engine, subject) is { } svgRoot)
        {
            // An element of an SVG document fragment (including a foreignObject): its bounding box, with the transforms of the SVG
            // document fragment (as getBoundingClientRect(), https://drafts.csswg.org/cssom-view/#dom-element-getclientrects),
            // placed in the content box of its outermost svg element
            if (subject is not SvgElement svg || SvgBoundingBox.GetBoundingQuad(svg, SvgUserSpace.GetOutermostViewport(svgRoot)) is not { } quad)
                return new ScrollTimelineSnapshot { Source = source };

            rect = quad.Bounds.Translate(svgRoot.GetOffsetInRoot() + svgRoot.ContentBoxRect.Offset);
        }
        else
        {
            var fragments = engine.GetFragments(subject, pseudoElement);
            if (fragments.Count == 0)
                return new ScrollTimelineSnapshot { Source = source };

            subjectFragment = fragments[0];
            rect = fragments[0].GetRectInRoot();
            for (var i = 1; i < fragments.Count; i++)
            {
                rect = rect.Union(fragments[i].GetRectInRoot());
            }
        }

        // The position of the subject in the coordinates of the fragment of the scroll container, at the scroll position 0 (the
        // positions in the root do not subtract the scroll position of the viewport)
        if (!state.IsViewport && state.Fragment is { } scrollContainer)
        {
            var origin = scrollContainer.GetOffsetInRoot();
            rect = rect.Translate(new PhysicalOffset(state.Offset.X - origin.X, state.Offset.Y - origin.Y));
        }

        var (offset, max, reversed) = GetScrollOffsets(state, horizontal);
        var sticky = subjectFragment is null ? null : GetStickyConstraints(subjectFragment, state, horizontal);
        if (sticky is not null)
        {
            // The subject at its static position
            foreach (var box in sticky)
            {
                rect = rect.Translate(PhysicalOffset.Zero - box.StickyOffset);
            }
        }

        var scrollport = state.Scrollport;
        double subjectStart;
        double subjectSize;
        double viewportSize;
        if (horizontal)
        {
            subjectStart = reversed ? scrollport.Right - rect.Right : rect.Left - scrollport.Left;
            subjectSize = rect.Width;
            viewportSize = scrollport.Width;
        }
        else
        {
            subjectStart = reversed ? scrollport.Bottom - rect.Bottom : rect.Top - scrollport.Top;
            subjectSize = rect.Height;
            viewportSize = scrollport.Height;
        }

        // https://drafts.csswg.org/scroll-animations-1/#view-timeline-inset: auto is the scroll-padding of the scroll container
        var zoom = GetZoom(engine, state);
        var scrollPadding = (state.Element ?? subject.NodeDocument.DocumentElement) is { } paddingElement ? subject.NodeDocument.GetStyleEngine().GetStyle(paddingElement).Layout.Overflow.ScrollPadding : default;
        var (startPadding, endPadding) = horizontal
            ? (reversed ? (scrollPadding.Right, scrollPadding.Left) : (scrollPadding.Left, scrollPadding.Right))
            : (reversed ? (scrollPadding.Bottom, scrollPadding.Top) : (scrollPadding.Top, scrollPadding.Bottom));

        // The lengths of the insets (view-timeline-inset, view()) are zoomed by the subject, whose style declares them
        var subjectZoom = subject.NodeDocument.GetStyleEngine().GetStyle(subject, pseudoElement).Layout.EffectiveZoom;
        if (!(subjectZoom > 0))
        {
            subjectZoom = zoom;
        }

        var start = startInset is { } startValue ? startValue.Resolve(viewportSize / subjectZoom) * subjectZoom : startPadding.IsAuto ? 0 : startPadding.Value.Resolve(viewportSize);
        var end = endInset is { } endValue ? endValue.Resolve(viewportSize / subjectZoom) * subjectZoom : endPadding.IsAuto ? 0 : endPadding.Value.Resolve(viewportSize);

        var snapshot = new ScrollTimelineSnapshot
        {
            Source = source,
            IsView = true,
            ScrollOffset = offset / zoom,
            MaxScrollOffset = max / zoom,
            SubjectStart = subjectStart / zoom,
            SubjectSize = subjectSize / zoom,
            EdgeOffsets = sticky is null ? null : ComputeStickyEdgeOffsets(sticky, state, engine.ViewportScrollState.Offset, horizontal, reversed, subjectStart, subjectSize, start, viewportSize - end, zoom),
            ViewStart = start / zoom,
            ViewEnd = (viewportSize - end) / zoom,
            SourceZoom = zoom,
        };

        // Like the browsers, a view progress timeline is active even when its scroll container has no scrollable overflow (WPT
        // view-timeline-subject-in-shadow-root-with-timeline-scope.html): its 0% and 100% positions must differ
        return snapshot with { IsActive = snapshot.EndOffset != snapshot.StartOffset };
    }

    /// <summary>
    /// Returns the fragments of the subject of a view progress timeline and of its ancestors that are sticky positioned in the
    /// scrollport of the timeline along its axis (the nearest scroll container that scrolls in that axis is its scroll
    /// container), or null when there is none: the position of the subject then depends on the scroll position.
    /// </summary>
    private static List<BoxFragment>? GetStickyConstraints(Fragment subject, ScrollState state, bool horizontal)
    {
        List<BoxFragment>? result = null;
        for (var fragment = subject; fragment is not null && !ReferenceEquals(fragment, state.Fragment); fragment = fragment.Parent)
        {
            if (fragment is BoxFragment { StickyConstraints: { } constraints } box
                && (horizontal ? constraints.HorizontalScrollContainer : constraints.VerticalScrollContainer) is var container
                && (state.IsViewport ? container is null : ReferenceEquals(container, state.Fragment)))
            {
                (result ??= []).Add(box);
            }
        }

        return result;
    }

    /// <summary>
    /// Computes the scroll offsets at which the edges of a subject that moves with the scroll position (sticky positioned, or in
    /// a sticky positioned box) reach the edges of the visibility range: as in the browsers (the tests of
    /// scroll-animations/view-timelines/sticky), the ranges of the timeline are the scroll offsets at which the subject, at its
    /// sticky position for each scroll offset, enters and leaves the range. The position of the subject relative to the
    /// scrollport does not increase with the scroll offset (it is constant while the box sticks), so each offset is found by
    /// bisection.
    /// </summary>
    /// <param name="sticky">The sticky positioned fragments (see <see cref="GetStickyConstraints"/>).</param>
    /// <param name="state">The scrolling box of the timeline.</param>
    /// <param name="viewportScrollPosition">The scroll position of the viewport.</param>
    /// <param name="horizontal">Whether the axis of the timeline is horizontal.</param>
    /// <param name="reversed">Whether the scroll offsets of the axis are negative (the timeline goes from right to left or from bottom to top).</param>
    /// <param name="subjectStart">The start edge of the subject at its static position, relative to the scrollport at the scroll offset 0.</param>
    /// <param name="subjectSize">The size of the subject along the axis.</param>
    /// <param name="viewStart">The start edge of the visibility range in the scrollport.</param>
    /// <param name="viewEnd">The end edge of the visibility range in the scrollport.</param>
    /// <param name="zoom">The effective zoom of the scroll container (the offsets are divided by it).</param>
    private static ViewTimelineEdgeOffsets ComputeStickyEdgeOffsets(List<BoxFragment> sticky, ScrollState state, PhysicalOffset viewportScrollPosition, bool horizontal, bool reversed, double subjectStart, double subjectSize, double viewStart, double viewEnd, double zoom)
    {
        var min = horizontal ? state.MinOffset.X : state.MinOffset.Y;
        var max = horizontal ? state.MaxOffset.X : state.MaxOffset.Y;

        // The position of the start edge of the subject relative to the start edge of the scrollport at a scroll offset
        double GetRelativeStart(double offset)
        {
            var physical = reversed ? max - offset : offset + min;
            var shift = 0d;
            foreach (var box in sticky)
            {
                var constraints = box.StickyConstraints!;
                var delta = constraints.ComputeOffset(horizontal ? new PhysicalOffset(physical, GetCrossScrollPosition(constraints.VerticalScrollContainer)) : new PhysicalOffset(GetCrossScrollPosition(constraints.HorizontalScrollContainer), physical));
                shift += horizontal ? delta.X : delta.Y;
            }

            return subjectStart + (reversed ? -shift : shift) - offset;
        }

        // The cross axis is scrolled by the scroll position of its scroll container (the viewport when it is null)
        double GetCrossScrollPosition(BoxFragment? container)
        {
            var offset = container is null ? viewportScrollPosition : container.ScrollState?.Offset ?? PhysicalOffset.Zero;
            return horizontal ? offset.Y : offset.X;
        }

        // The smallest scroll offset at which the edge (the start edge plus a size) reaches a position of the scrollport, or passes
        // it (an edge that sticks at the start edge of the range has not left it yet)
        double Solve(double size, double position, bool passes)
        {
            var low = -1e7;
            var high = 1e7;
            for (var i = 0; i < 80; i++)
            {
                var middle = (low + high) / 2;
                var edge = GetRelativeStart(middle) + size;
                if (passes ? edge < position : edge <= position)
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            return Math.Round(high, 6, MidpointRounding.AwayFromZero) / zoom;
        }

        return new ViewTimelineEdgeOffsets(
            StartAtViewEnd: Solve(0, viewEnd, passes: false),
            EndAtViewEnd: Solve(subjectSize, viewEnd, passes: false),
            StartAtViewStart: Solve(0, viewStart, passes: true),
            EndAtViewStart: Solve(subjectSize, viewStart, passes: true));
    }
}

/// <summary>The scroll container of a timeline (null when there is none), and the physical axis of the timeline when the lookup resolved it (null: resolved with the writing mode of the scroll container).</summary>
internal readonly record struct ScrollTimelineSource(Element? Element, bool? Horizontal);
