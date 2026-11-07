using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A node that shifted in a layout shift, with the visible parts of its boxes in the previous and in the current frame (in the coordinates of the viewport).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LayoutShiftSource(Node? Node, PhysicalRect PreviousRect, PhysicalRect CurrentRect);

/// <summary>The layout shift of a frame (https://wicg.github.io/layout-instability/#layout-shift-value): its value and its sources (at most 5).</summary>
internal sealed record LayoutShiftReport(double Value, LayoutShiftSource[] Sources);

/// <summary>
/// Detects the layout shifts of a document (https://wicg.github.io/layout-instability/): each rendering update records, for each
/// box and text node, its starting point and its visual representation in the layout, and compares them with the previous
/// frame to find the unstable nodes, whose movement gives the layout shift value.
/// </summary>
/// <remarks>
/// <para>
/// The tracked objects follow the browsers: the boxes of the elements and of the pseudo-elements (the principal box: the table
/// wrapper box of a table), except the anonymous boxes, the non-atomic inline boxes, the sticky positioned boxes (their
/// descendants are tracked, the sticky offset being ignored as a transform), the boxes of the SVG elements inside an svg
/// element, and the block containers without child boxes that paint nothing (no background, border, shadow or outline, and not
/// a scroll container); and the text nodes. The starting point is the flow-relative starting corner (in the writing mode of
/// the box) of the first fragment of the box (of the first text fragment of a text node). The visual representation of a box
/// is its visual overflow, as in Chrome (see <see cref="GetVisualOverflow"/>); the one of a text node is the union of its text
/// fragments, each extended to the inline-end edge of the content box of its block container (the area that the text may
/// cover once wrapped, as in Chrome). Both are clipped by the overflow clips and the clip paths of the ancestor fragments (the
/// containing block chain) and by the viewport.
/// </para>
/// <para>
/// A node is unstable when it is visible in both frames (<c>visibility: visible</c>, no zero opacity on its box or an ancestor
/// box), it shifted significantly (3 CSS pixels in either axis, as in Chrome) in the coordinates of the viewport and in the
/// coordinates of the initial containing block (the document), both with and without the transforms (the "transform-indifferent
/// starting point", which ignores the sticky offsets too), and in the scrolling contents of its nearest ancestor scroll
/// container (a scroll does not shift the scrolled contents). As in Chrome, a node whose nearest scroll container or whose
/// fixed positioning (relative to the viewport) changed is not compared (its previous position is in another coordinate space),
/// the previous rectangle and starting point are moved by the change of the scroll offsets (into the current viewport) and
/// both rectangles are clipped by the current clip, a node that moved in the viewport by the scroll anchoring adjustments of
/// its scroll containers (its movement in the document was compensated) is not unstable, and a node that is not visible in one
/// of the frames and only moved in the inline direction (an "inline clip crosser", e.g. a carousel) is not unstable.
/// </para>
/// <para>
/// The layout is only compared when it changed (a layout pass or a scroll since the previous frame). The objects are found by a
/// walk of the fragment tree from its root, which carries the position of each fragment (without the transforms: the
/// fragments under a transform are mapped by <see cref="LayoutEngine.MapToRoot(Fragment, PhysicalOffset)"/>) and the clip of
/// its ancestors.
/// </para>
/// </remarks>
internal sealed class LayoutShiftTracker
{
    /// <summary>https://wicg.github.io/layout-instability/#number-of-pixels-to-significance (3 CSS pixels, as in Chrome).</summary>
    private const double PixelsToSignificance = 3;

    /// <summary>The maximum number of sources of a layout shift (https://wicg.github.io/layout-instability/#report-the-layout-shift-sources).</summary>
    private const int MaxSources = 5;

    /// <summary>The coordinate space of the nodes in a subtree of a fixed positioned box of the viewport (see <see cref="NodeState.ShiftRoot"/>).</summary>
    private static readonly object FixedToViewport = new();

    private readonly LayoutEngine _engine;
    private readonly Stack<(Fragment Fragment, FragmentContext Context)> _stack = new();
    private readonly Dictionary<ContainerBox, bool> _zeroOpacity = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoxFragment, PhysicalRect> _visualOverflows = new(ReferenceEqualityComparer.Instance);
    private readonly List<PhysicalRect> _impactRects = [];
    private Dictionary<NodeKey, NodeState> _previous = [];
    private Dictionary<NodeKey, NodeState> _current = [];
    private int _layoutCount = -1;
    private int _scrollVersion = -1;
    private PhysicalOffset _viewportScrollOffset;
    private PhysicalOffset _viewportAnchoringAdjustments;

    public LayoutShiftTracker(LayoutEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Records the starting points and the visual representations of the current layout and returns the layout shift since the
    /// previous call (https://wicg.github.io/layout-instability/#report-the-layout-shift), or null when its value is 0 (the first
    /// frame, nothing shifted, or the layout did not change).
    /// </summary>
    public LayoutShiftReport? Update()
    {
        if (_engine.LaidOutRoot is not { } root)
        {
            _current.Clear();
            _layoutCount = -1;
            return null;
        }

        if (_engine.LayoutCount == _layoutCount && _engine.ScrollVersion == _scrollVersion)
            return null;

        _layoutCount = _engine.LayoutCount;
        _scrollVersion = _engine.ScrollVersion;
        (_previous, _current) = (_current, _previous);
        _current.Clear();
        _viewportScrollOffset = _engine.ViewportScrollState.Offset;
        _viewportAnchoringAdjustments = _engine.ViewportScrollState.AnchoringAdjustments;
        try
        {
            Collect(root);
        }
        finally
        {
            _stack.Clear();
            _zeroOpacity.Clear();
            _visualOverflows.Clear();
        }

        return _previous.Count == 0 ? null : Compare(root);
    }

    /// <summary>Walks the fragment tree and records the state of the tracked objects in <see cref="_current"/>.</summary>
    private void Collect(BoxFragment root)
    {
        // The viewport (without the scrollbars) clips the visual representations
        var viewport = root.PaddingBoxRect;
        var rootClip = ClipBox.FromRect(viewport);
        PushChildren(root, new FragmentContext { Clip = rootClip }, rootClip);
        while (_stack.TryPop(out var item))
        {
            var (fragment, context) = item;
            switch (fragment)
            {
                case BoxFragment box:
                    var childrenClip = context.Clip;
                    if (box.Box.Kind != ContainerBoxKind.Viewport)
                    {
                        if (IsTracked(box))
                        {
                            RecordBox(box, context);
                        }

                        childrenClip = GetChildrenClip(box, context);
                    }

                    // The contents skipped by content-visibility are not rendered (they may be laid out for a geometry API)
                    if (!box.Box.SkipsContents)
                    {
                        if (!box.Box.IsInlineBox)
                        {
                            context = context with { Block = box, BlockOrigin = context.Origin };
                        }

                        PushChildren(box, context, childrenClip);
                    }

                    break;

                case TextFragment text:
                    if (text.Box is TextRun { Kind: TextRunKind.Text, Node: { } node } run)
                    {
                        RecordText(text, run, node, context);
                    }

                    break;

                default:
                    // A line box
                    PushChildren(fragment, context, context.Clip);
                    break;
            }
        }
    }

    /// <summary>Pushes the children of a fragment with their contexts, so that they are visited in the order of the fragment tree.</summary>
    private void PushChildren(Fragment parent, FragmentContext parentContext, ClipBox childrenClip)
    {
        var children = parent.ChildrenSpan;
        for (var i = children.Length - 1; i >= 0; i--)
        {
            var child = children[i];
            _stack.Push((child, GetChildContext(parent, parentContext, childrenClip, child)));
        }
    }

    private static FragmentContext GetChildContext(Fragment parent, FragmentContext parentContext, ClipBox childrenClip, Fragment child)
    {
        var offset = LayoutEngine.GetScrolledOffset(child);
        var childBox = child as BoxFragment;
        var stickyOffset = childBox?.StickyOffset ?? PhysicalOffset.Zero;
        var unscrolledOffset = child.Offset - stickyOffset;
        var context = parentContext with
        {
            Origin = parentContext.Origin + offset,
            StableOrigin = parentContext.StableOrigin + offset - stickyOffset,
            Transformed = parentContext.Transformed || (childBox is not null && childBox.Box.Style.Layout.HasTransform),
            Clip = childrenClip,
        };

        if (parent is BoxFragment { ScrollState: { IsViewport: false } scrollState, Box.Element: var scroller })
        {
            // The scrolling contents of a scroll container
            context = context with { Scroller = scroller, OriginInScroller = unscrolledOffset, ScrollOffset = parentContext.ScrollOffset + scrollState.Offset, AnchoringAdjustments = parentContext.AnchoringAdjustments + scrollState.AnchoringAdjustments };
        }
        else if (parent.Parent is null && childBox is { Box.Style.Layout.Position: PositionKind.Fixed })
        {
            // A fixed positioned box of the viewport does not move when the viewport scrolls
            context = context with { Scroller = null, InFixed = true, OriginInScroller = unscrolledOffset, ScrollOffset = PhysicalOffset.Zero, AnchoringAdjustments = PhysicalOffset.Zero };
        }
        else
        {
            context = context with { OriginInScroller = parentContext.OriginInScroller + unscrolledOffset };
        }

        return context;
    }

    /// <summary>
    /// Returns the clip of the descendants of a box fragment, in the coordinates of the viewport: the clip of the fragment
    /// intersected with its clip path and its overflow clip edge (in the axes it clips), as <see cref="IntersectionGeometry"/>.
    /// </summary>
    private ClipBox GetChildrenClip(BoxFragment box, FragmentContext context)
    {
        var clip = context.Clip;
        if (ClipPaths.GetBounds(box) is { } clipPath)
        {
            clip = clip.Intersect(ClipBox.FromRect(MapRect(box, context, clipPath)));
        }

        if (box.ClipsOverflow)
        {
            var overflowClip = MapRect(box, context, box.OverflowClipRect);
            clip = clip.Intersect(new ClipBox(
                box.ClipsOverflowX ? overflowClip.Left : double.NegativeInfinity,
                box.ClipsOverflowY ? overflowClip.Top : double.NegativeInfinity,
                box.ClipsOverflowX ? overflowClip.Right : double.PositiveInfinity,
                box.ClipsOverflowY ? overflowClip.Bottom : double.PositiveInfinity));
        }

        return clip;
    }

    /// <summary>
    /// Whether a box is tracked: the box of an element or of a pseudo-element, as in the browsers (see the remarks of
    /// <see cref="LayoutShiftTracker"/>).
    /// </summary>
    private static bool IsTracked(BoxFragment fragment)
    {
        var box = fragment.Box;
        if (box.Element is not { } element || box.Kind is ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid or ContainerBoxKind.Anonymous || box.IsInlineBox)
            return false;

        // The sticky positioned boxes move when their scroll container scrolls; the SVG elements do not participate in the CSS
        // layout (except the outermost svg element)
        if (box.Style.Layout.Position == PositionKind.Sticky || (element is SvgElement && box.PseudoElement is null && box.Kind != ContainerBoxKind.Replaced))
            return false;

        if (box.Kind is ContainerBoxKind.Replaced or ContainerBoxKind.TableWrapper || box.FirstChild is not null || fragment.IsScrollContainer)
            return true;

        // A box without contents that paints nothing (e.g. an empty div that only takes space) does not shift visually
        var style = box.Style;
        return fragment.Border.Top > 0 || fragment.Border.Right > 0 || fragment.Border.Bottom > 0 || fragment.Border.Left > 0
            || style.GetComputedValue("background-color") is not ("transparent" or "rgba(0, 0, 0, 0)")
            || style.GetComputedValue("background-image") != "none"
            || style.GetComputedValue("box-shadow") != "none"
            || style.GetComputedValue("outline-style") != "none";
    }

    private void RecordBox(BoxFragment fragment, FragmentContext context)
    {
        var box = fragment.Box;
        var key = new NodeKey(box.Element!, box.PseudoElement);
        var state = GetOrAddState(key, box.PseudoElement is null ? box.Element : null);
        state.Visible &= box.Style.Layout.Visibility == Visibility.Visible && !HasZeroOpacity(box);
        AddVisualRect(state, MapRect(fragment, context, GetVisualOverflow(fragment)), context.Clip);
        if (ReferenceEquals(_engine.GetLaidOutFirstFragment(box), fragment))
        {
            SetStartingPoint(state, fragment, context, GetStartingCorner(WritingDirection.FromStyle(box.Style.Layout), fragment.Size));
        }
    }

    private void RecordText(TextFragment fragment, TextRun run, Text node, FragmentContext context)
    {
        if (fragment.Size.Width <= 0 && fragment.Size.Height <= 0)
            return;

        var key = new NodeKey(node, PseudoElement: null);
        var state = GetOrAddState(key, node);
        state.Visible &= run.Style.Layout.Visibility == Visibility.Visible && (run.Parent is null || !HasZeroOpacity(run.Parent));

        // The text fragment extended to the inline-end edge of the content box of its block container
        var writingDirection = WritingDirection.FromStyle(run.Style.Layout);
        var rect = fragment.LocalRect;
        if (context.Block is { } block)
        {
            var content = block.ContentBoxRect;
            if (block.ScrollState is { IsViewport: false } scrollState)
            {
                content = content.Translate(-scrollState.Offset);
            }

            // The position of the text fragment in the coordinates of the block container
            var position = context.Origin - context.BlockOrigin;
            rect = writingDirection.InlineEnd switch
            {
                PhysicalSide.Right => new PhysicalRect(0, 0, Math.Max(rect.Width, content.Right - position.X), rect.Height),
                PhysicalSide.Left => ExtendStart(rect, Math.Min(0, content.Left - position.X), horizontal: true),
                PhysicalSide.Bottom => new PhysicalRect(0, 0, rect.Width, Math.Max(rect.Height, content.Bottom - position.Y)),
                _ => ExtendStart(rect, Math.Min(0, content.Top - position.Y), horizontal: false),
            };
        }

        AddVisualRect(state, MapRect(fragment, context, rect), context.Clip);
        if (!state.HasStartingPoint && ReferenceEquals(GetFirstTextFragment(node), fragment))
        {
            SetStartingPoint(state, fragment, context, GetStartingCorner(writingDirection, fragment.Size));
        }

        static PhysicalRect ExtendStart(PhysicalRect rect, double start, bool horizontal) => horizontal
            ? new PhysicalRect(start, 0, rect.Width - start, rect.Height)
            : new PhysicalRect(0, start, rect.Width, rect.Height - start);
    }

    /// <summary>
    /// Returns the visual overflow of a box fragment in its coordinates, as in Chrome: its border box united with the visual
    /// overflow of its descendants that are not painted by their own layer (the positioned boxes, the transformed boxes and
    /// the stacking contexts are tracked separately) and with the text fragments, the contents being clipped by the overflow
    /// clip edge of the box. The ink overflow (shadows, outlines) is not included (#375/#376).
    /// </summary>
    private PhysicalRect GetVisualOverflow(BoxFragment fragment)
    {
        if (_visualOverflows.TryGetValue(fragment, out var result))
            return result;

        var contents = PhysicalRect.Empty;
        if (!fragment.Box.SkipsContents)
        {
            AddContentsVisualOverflow(fragment, PhysicalOffset.Zero, ref contents);
        }

        if (fragment.ClipsOverflow && !contents.IsEmpty)
        {
            var clip = fragment.OverflowClipRect;
            contents = new ClipBox(
                fragment.ClipsOverflowX ? clip.Left : double.NegativeInfinity,
                fragment.ClipsOverflowY ? clip.Top : double.NegativeInfinity,
                fragment.ClipsOverflowX ? clip.Right : double.PositiveInfinity,
                fragment.ClipsOverflowY ? clip.Bottom : double.PositiveInfinity).Apply(contents);
        }

        result = fragment.BorderBoxRect.Union(contents);
        _visualOverflows[fragment] = result;
        return result;
    }

    private void AddContentsVisualOverflow(Fragment parent, PhysicalOffset parentOffset, ref PhysicalRect contents)
    {
        foreach (var child in parent.ChildrenSpan)
        {
            var offset = parentOffset + LayoutEngine.GetScrolledOffset(child);
            switch (child)
            {
                case BoxFragment box:
                    var layout = box.Box.Style.Layout;
                    if (!layout.IsPositioned && !layout.HasTransform && !box.EstablishesStackingContext)
                    {
                        contents = contents.Union(GetVisualOverflow(box).Translate(offset));
                    }

                    break;

                case TextFragment text:
                    contents = contents.Union(text.LocalRect.Translate(offset));
                    break;

                default:
                    AddContentsVisualOverflow(child, offset, ref contents);
                    break;
            }
        }
    }

    /// <summary>Returns the first text fragment of a text node: the first fragment of its first text run that has fragments.</summary>
    private Fragment? GetFirstTextFragment(Text node)
    {
        for (var run = _engine.BoxTree?.GetFirstTextRun(node); run is not null; run = run.NextRunOfNode)
        {
            if (_engine.GetLaidOutFirstFragment(run) is { } fragment)
                return fragment;
        }

        return null;
    }

    private NodeState GetOrAddState(NodeKey key, Node? attributionNode)
    {
        if (!_current.TryGetValue(key, out var state))
        {
            state = new NodeState(attributionNode);
            _current.Add(key, state);
        }

        return state;
    }

    private static void AddVisualRect(NodeState state, PhysicalRect rect, ClipBox clip)
    {
        state.VisualRect = state.VisualRect.Union(rect);
        if (!state.HasClip)
        {
            state.HasClip = true;
            state.Clip = clip;
        }
    }

    /// <summary>Records the starting point of a node (https://wicg.github.io/layout-instability/#starting-point) in the coordinate spaces where it is compared.</summary>
    private void SetStartingPoint(NodeState state, Fragment fragment, FragmentContext context, PhysicalOffset corner)
    {
        // The position in the document (the coordinates of the root of the fragment tree), with the transforms, and without them
        var document = context.Transformed ? LayoutEngine.MapToRoot(fragment, corner) : context.Origin + corner;
        var stableDocument = context.StableOrigin + corner;
        state.HasStartingPoint = true;
        state.Document = document;
        state.StableDocument = stableDocument;
        state.Viewport = document - _viewportScrollOffset;
        state.StableViewport = stableDocument - _viewportScrollOffset;
        state.InScroller = context.OriginInScroller + corner;
        state.ShiftRoot = context.Scroller ?? (context.InFixed ? FixedToViewport : null);
        state.ScrollOffset = context.InFixed ? context.ScrollOffset : context.ScrollOffset + _viewportScrollOffset;
        state.AnchoringAdjustments = context.InFixed ? context.AnchoringAdjustments : context.AnchoringAdjustments + _viewportAnchoringAdjustments;
        state.IsHorizontal = fragment is TextFragment text ? text.Run.Style.Layout.WritingMode == WritingMode.HorizontalTb : ((BoxFragment)fragment).Box.Style.Layout.WritingMode == WritingMode.HorizontalTb;
    }

    /// <summary>The physical position of the flow-relative starting corner (inline-start, block-start) of a fragment.</summary>
    private static PhysicalOffset GetStartingCorner(WritingDirection writingDirection, PhysicalSize size)
    {
        var inlineStart = writingDirection.InlineStart;
        var blockStart = writingDirection.BlockStart;
        var x = inlineStart == PhysicalSide.Right || blockStart == PhysicalSide.Right ? size.Width : 0;
        var y = inlineStart == PhysicalSide.Bottom ? size.Height : 0;
        return new PhysicalOffset(x, y);
    }

    /// <summary>Maps a rectangle in the coordinates of a fragment to the bounds of its quad in the coordinates of the viewport.</summary>
    private PhysicalRect MapRect(Fragment fragment, FragmentContext context, PhysicalRect rect)
    {
        var mapped = context.Transformed ? LayoutEngine.MapToRoot(fragment, rect).Bounds : rect.Translate(context.Origin);
        return mapped.Translate(-_viewportScrollOffset);
    }

    /// <summary>Whether a box or one of its ancestors in the box tree (the DOM ancestors) has an opacity of 0.</summary>
    private bool HasZeroOpacity(ContainerBox box)
    {
        if (_zeroOpacity.TryGetValue(box, out var result))
            return result;

        result = box.Style.GetComputedValue("opacity") == "0" || (box.Parent is { } parent && HasZeroOpacity(parent));
        _zeroOpacity[box] = result;
        return result;
    }

    /// <summary>Finds the unstable nodes (https://wicg.github.io/layout-instability/#unstable-node-set) and computes the layout shift value.</summary>
    private LayoutShiftReport? Compare(BoxFragment root)
    {
        _impactRects.Clear();
        var maximumMoveDistance = 0d;
        var sources = new List<LayoutShiftSource>(MaxSources);
        foreach (var (key, current) in _current)
        {
            if (!_previous.TryGetValue(key, out var previous) || !IsUnstableCandidate(previous, current))
                continue;

            // As in Chrome, the previous visual representation and starting point are moved by the change of the scroll offsets of
            // the scroll containers of the node (into the coordinates of the current viewport), and both visual representations are
            // clipped by the current clips and viewport
            var scrollDelta = previous.ScrollOffset - current.ScrollOffset;

            // A node that moved by the scroll anchoring adjustments of its scroll containers did not shift: its movement is
            // compensated (as Chrome's scroll_anchor_adjustment)
            var anchoringDelta = current.AnchoringAdjustments - previous.AnchoringAdjustments;
            if (anchoringDelta != PhysicalOffset.Zero && !DiffersSignificantly(previous.Viewport + scrollDelta + anchoringDelta, current.Viewport, blockAxisOnly: false, current.IsHorizontal))
                continue;

            var previousRect = current.Clip.Apply(previous.VisualRect.Translate(scrollDelta));
            var currentRect = current.Clip.Apply(current.VisualRect);
            if (previousRect.IsEmpty && currentRect.IsEmpty)
                continue;

            // An inline clip crosser (https://wicg.github.io/layout-instability/#inline-clip-crosser): a node that enters or leaves the
            // viewport (or its clip) and that would not be an unstable-candidate if only the movements in the block axis counted
            if ((previousRect.IsEmpty || currentRect.IsEmpty) && !IsUnstableCandidate(previous, current, blockAxisOnly: true))
                continue;

            // https://wicg.github.io/layout-instability/#move-distance (in the coordinates of the viewport)
            var previousStartingPoint = previous.Viewport + scrollDelta;
            var moveX = Math.Abs(current.Viewport.X - previousStartingPoint.X);
            var moveY = Math.Abs(current.Viewport.Y - previousStartingPoint.Y);
            maximumMoveDistance = Math.Max(maximumMoveDistance, Math.Max(moveX, moveY));
            if (!previousRect.IsEmpty)
            {
                _impactRects.Add(previousRect);
            }

            if (!currentRect.IsEmpty)
            {
                _impactRects.Add(currentRect);
            }

            RecordAttribution(sources, new LayoutShiftSource(current.AttributionNode, previousRect, currentRect));
        }

        if (_impactRects.Count == 0)
            return null;

        // https://wicg.github.io/layout-instability/#impact-fraction and https://wicg.github.io/layout-instability/#distance-fraction
        var viewport = root.PaddingBoxRect.Size;
        var viewportArea = viewport.Width * viewport.Height;
        var impactFraction = viewportArea == 0 ? 0 : RectangleUnion.GetArea(_impactRects) / viewportArea;
        var viewportBaseDistance = Math.Max(viewport.Width, viewport.Height);
        var distanceFraction = viewportBaseDistance == 0 ? 0 : Math.Min(maximumMoveDistance / viewportBaseDistance, 1);
        var value = impactFraction * distanceFraction;
        if (value <= 0)
            return null;

        // "Sort C in descending order by area of node impact region" (the sort is stable)
        var sorted = sources.OrderByDescending(GetImpactArea).ToArray();
        return new LayoutShiftReport(value, sorted);
    }

    /// <summary>
    /// https://wicg.github.io/layout-instability/#unstable-candidate (the significance threshold and the exclusions of the browsers:
    /// see the remarks): a node visible in both frames, in the same coordinate space, that shifted.
    /// </summary>
    private static bool IsUnstableCandidate(NodeState previous, NodeState current)
    {
        if (!previous.HasStartingPoint || !current.HasStartingPoint || !previous.Visible || !current.Visible || !ReferenceEquals(previous.ShiftRoot, current.ShiftRoot))
            return false;

        return IsUnstableCandidate(previous, current, blockAxisOnly: false);
    }

    /// <summary>https://wicg.github.io/layout-instability/#unstable-candidate (the conditions on the starting points).</summary>
    private static bool IsUnstableCandidate(NodeState previous, NodeState current, bool blockAxisOnly)
    {
        var horizontal = current.IsHorizontal;

        // "has shifted" in the coordinate space of the viewport and in the one of the initial containing block: the starting point
        // and the transform-indifferent starting point both differ significantly
        if (!DiffersSignificantly(previous.Viewport, current.Viewport, blockAxisOnly, horizontal) || !DiffersSignificantly(previous.StableViewport, current.StableViewport, blockAxisOnly, horizontal))
            return false;

        if (!DiffersSignificantly(previous.Document, current.Document, blockAxisOnly, horizontal) || !DiffersSignificantly(previous.StableDocument, current.StableDocument, blockAxisOnly, horizontal))
            return false;

        // A node that did not shift in the scrolling contents of its scroll container only moved because it was scrolled
        return current.ShiftRoot is not Element || DiffersSignificantly(previous.InScroller, current.InScroller, blockAxisOnly, horizontal);
    }

    /// <summary>
    /// https://wicg.github.io/layout-instability/#differ-significantly: in either axis, or only in the block axis (vertical in a
    /// horizontal writing mode) for the inline clip crossers.
    /// </summary>
    private static bool DiffersSignificantly(PhysicalOffset a, PhysicalOffset b, bool blockAxisOnly, bool horizontal)
    {
        var differsX = Math.Abs(a.X - b.X) >= PixelsToSignificance;
        var differsY = Math.Abs(a.Y - b.Y) >= PixelsToSignificance;
        if (blockAxisOnly)
            return horizontal ? differsY : differsX;

        return differsX || differsY;
    }

    /// <summary>
    /// The step 2 of https://wicg.github.io/layout-instability/#report-the-layout-shift-sources for a node: its node impact region
    /// (the union of its previous and current rectangles, as in Chrome) is redundant when it is enclosed by the one of a source,
    /// replaces the sources it encloses, else is added, or replaces the source with the smallest region when there are 5.
    /// </summary>
    private static void RecordAttribution(List<LayoutShiftSource> sources, LayoutShiftSource source)
    {
        var smallest = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (Encloses(source, sources[i]))
            {
                sources[i] = source;

                // The other sources enclosed by the node are redundant
                for (var j = sources.Count - 1; j > i; j--)
                {
                    if (Encloses(source, sources[j]))
                    {
                        sources.RemoveAt(j);
                    }
                }

                return;
            }

            if (Encloses(sources[i], source))
                return;

            if (smallest < 0 || GetImpactArea(sources[i]) < GetImpactArea(sources[smallest]))
            {
                smallest = i;
            }
        }

        if (sources.Count < MaxSources)
        {
            sources.Add(source);
        }
        else if (GetImpactArea(source) > GetImpactArea(sources[smallest]))
        {
            sources[smallest] = source;
        }
    }

    private static bool Encloses(LayoutShiftSource a, LayoutShiftSource b) => Contains(a.PreviousRect, b.PreviousRect) && Contains(a.CurrentRect, b.CurrentRect);

    private static bool Contains(PhysicalRect a, PhysicalRect b) => b.IsEmpty || (!a.IsEmpty && b.Left >= a.Left && b.Top >= a.Top && b.Right <= a.Right && b.Bottom <= a.Bottom);

    /// <summary>The area of the node impact region of a source: the union of its previous and current rectangles.</summary>
    private static double GetImpactArea(LayoutShiftSource source)
    {
        var a = source.PreviousRect;
        var b = source.CurrentRect;
        var area = (a.IsEmpty ? 0 : a.Width * a.Height) + (b.IsEmpty ? 0 : b.Width * b.Height);
        if (!a.IsEmpty && !b.IsEmpty)
        {
            var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            if (width > 0 && height > 0)
            {
                area -= width * height;
            }
        }

        return area;
    }

    /// <summary>A tracked object: the box of an element or of a pseudo-element, or a text node.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct NodeKey(Node Node, string? PseudoElement);

    /// <summary>The state of a tracked object in a frame.</summary>
    private sealed class NodeState(Node? attributionNode)
    {
        /// <summary>The node reported by the attribution (null for a pseudo-element).</summary>
        public Node? AttributionNode { get; } = attributionNode;

        public bool Visible { get; set; } = true;

        public bool HasStartingPoint { get; set; }

        /// <summary>The starting point in the coordinates of the viewport.</summary>
        public PhysicalOffset Viewport { get; set; }

        /// <summary>The transform-indifferent starting point in the coordinates of the viewport.</summary>
        public PhysicalOffset StableViewport { get; set; }

        /// <summary>The starting point in the coordinates of the initial containing block (the document).</summary>
        public PhysicalOffset Document { get; set; }

        /// <summary>The transform-indifferent starting point in the coordinates of the initial containing block.</summary>
        public PhysicalOffset StableDocument { get; set; }

        /// <summary>The transform-indifferent starting point in the scrolling contents of the nearest scroll container.</summary>
        public PhysicalOffset InScroller { get; set; }

        /// <summary>The element of the nearest scroll container (not the viewport), <see cref="FixedToViewport"/> in a fixed positioned box of the viewport, or null.</summary>
        public object? ShiftRoot { get; set; }

        public bool IsHorizontal { get; set; }

        /// <summary>The scroll offsets of the scroll containers of the node (the viewport included, unless it is fixed positioned), whose change is not a shift.</summary>
        public PhysicalOffset ScrollOffset { get; set; }

        /// <summary>The sums of the scroll anchoring adjustments of the scroll containers of the node (see <see cref="ScrollState.AnchoringAdjustments"/>).</summary>
        public PhysicalOffset AnchoringAdjustments { get; set; }

        /// <summary>The bounds of the visual representation before the clips, in the coordinates of the viewport.</summary>
        public PhysicalRect VisualRect { get; set; }

        public bool HasClip { get; set; }

        /// <summary>The clip of the ancestors of the node and of the viewport, in the coordinates of the viewport.</summary>
        public ClipBox Clip { get; set; }
    }

    /// <summary>The position and the clip of a fragment during the walk of the fragment tree.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FragmentContext
    {
        /// <summary>The position of the fragment in the coordinates of the root of the fragment tree, without the transforms.</summary>
        public PhysicalOffset Origin { get; init; }

        /// <summary>The position of the fragment without the transforms and the sticky offsets.</summary>
        public PhysicalOffset StableOrigin { get; init; }

        /// <summary>Whether a transform applies to the fragment or to an ancestor: <see cref="Origin"/> is then not its position.</summary>
        public bool Transformed { get; init; }

        /// <summary>The clip of the ancestors of the fragment and of the viewport, in the coordinates of the viewport.</summary>
        public ClipBox Clip { get; init; }

        public Element? Scroller { get; init; }

        public PhysicalOffset OriginInScroller { get; init; }

        /// <summary>The sum of the scroll offsets of the scroll containers (not the viewport) the fragment is in.</summary>
        public PhysicalOffset ScrollOffset { get; init; }

        /// <summary>The sum of the scroll anchoring adjustments of the scroll containers (not the viewport) the fragment is in.</summary>
        public PhysicalOffset AnchoringAdjustments { get; init; }

        public bool InFixed { get; init; }

        /// <summary>The nearest block container fragment (not an inline box) that is an ancestor of the fragment, and its <see cref="Origin"/>.</summary>
        public BoxFragment? Block { get; init; }

        public PhysicalOffset BlockOrigin { get; init; }
    }

    /// <summary>A clip rectangle whose edges may be infinite (a clip in one axis).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ClipBox(double Left, double Top, double Right, double Bottom)
    {
        public static ClipBox FromRect(PhysicalRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

        public ClipBox Intersect(ClipBox other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

        /// <summary>Returns the part of a rectangle in the clip, or an empty rectangle.</summary>
        public PhysicalRect Apply(PhysicalRect rect)
        {
            var left = Math.Max(rect.Left, Left);
            var top = Math.Max(rect.Top, Top);
            var right = Math.Min(rect.Right, Right);
            var bottom = Math.Min(rect.Bottom, Bottom);
            return right > left && bottom > top ? new PhysicalRect(left, top, right - left, bottom - top) : PhysicalRect.Empty;
        }
    }
}

/// <summary>The area of the union of rectangles (https://wicg.github.io/layout-instability/#impact-region), computed by a sweep over the x axis with a segment tree of the covered y intervals.</summary>
internal static class RectangleUnion
{
    public static double GetArea(List<PhysicalRect> rects)
    {
        if (rects.Count == 0)
            return 0;

        if (rects.Count == 1)
            return rects[0].Width * rects[0].Height;

        // The distinct y coordinates of the edges
        var ys = new double[rects.Count * 2];
        for (var i = 0; i < rects.Count; i++)
        {
            ys[i * 2] = rects[i].Top;
            ys[(i * 2) + 1] = rects[i].Bottom;
        }

        Array.Sort(ys);
        var count = 0;
        for (var i = 0; i < ys.Length; i++)
        {
            if (count == 0 || ys[i] != ys[count - 1])
            {
                ys[count++] = ys[i];
            }
        }

        // The vertical edges of the rectangles: +1 at the left edge, -1 at the right edge
        var events = new (double X, int Delta, int Top, int Bottom)[rects.Count * 2];
        for (var i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            var top = Array.BinarySearch(ys, 0, count, rect.Top);
            var bottom = Array.BinarySearch(ys, 0, count, rect.Bottom);
            events[i * 2] = (rect.Left, 1, top, bottom);
            events[(i * 2) + 1] = (rect.Right, -1, top, bottom);
        }

        Array.Sort(events, static (a, b) => a.X.CompareTo(b.X));

        // Segment tree over the intervals [ys[i], ys[i + 1]]: the cover count and the covered length of each node
        var intervals = count - 1;
        var cover = new int[intervals * 4];
        var length = new double[intervals * 4];
        var area = 0d;
        for (var i = 0; i < events.Length; i++)
        {
            if (i > 0)
            {
                area += length[1] * (events[i].X - events[i - 1].X);
            }

            if (events[i].Top < events[i].Bottom)
            {
                Update(1, 0, intervals, events[i].Top, events[i].Bottom, events[i].Delta);
            }
        }

        return area;

        void Update(int node, int start, int end, int from, int to, int delta)
        {
            if (to <= start || end <= from)
                return;

            if (from <= start && end <= to)
            {
                cover[node] += delta;
            }
            else
            {
                var middle = (start + end) / 2;
                Update(node * 2, start, middle, from, to, delta);
                Update((node * 2) + 1, middle, end, from, to, delta);
            }

            if (cover[node] > 0)
            {
                length[node] = ys[end] - ys[start];
            }
            else if (end - start == 1)
            {
                length[node] = 0;
            }
            else
            {
                length[node] = length[node * 2] + length[(node * 2) + 1];
            }
        }
    }
}
