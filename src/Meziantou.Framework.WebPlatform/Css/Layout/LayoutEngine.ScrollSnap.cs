using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// Scroll snapping (https://drafts.csswg.org/css-scroll-snap-1/): the snap containers and their snap areas of the last layout, the
// snap positions of the scroll operations, the re-snap after the layout changes, and the snap events
// (https://drafts.csswg.org/css-scroll-snap-2/#snap-events)
internal sealed partial class LayoutEngine
{
    /// <summary>The ratio of the snapport size within which a <c>proximity</c> snap container snaps (the one of Blink).</summary>
    private const double ProximityRatio = 1.0 / 3.0;

    /// <summary>The version of the fragments whose snap containers were computed.</summary>
    private int _snapContainersVersion = -1;

    /// <summary>Whether a scrolling box of the last layout is a scroll snap container, or was one.</summary>
    private bool _hasSnapStates;

    /// <summary>
    /// Returns the position a scroll operation of a scrolling box ends at (https://drafts.csswg.org/css-scroll-snap-1/#choosing):
    /// the selected snap position when the box is a scroll snap container, else the intended position, clamped to the scrolling
    /// area. The snap targets of the box are updated (and a <c>scrollsnapchanging</c> event is queued when they change).
    /// </summary>
    /// <summary>
    /// Returns the snap position of a scroll operation intended to scroll to an element whose nearest scroll container is the
    /// scroll container (https://drafts.csswg.org/css-scroll-snap-1/#choosing: "the user agent must snap to one of that element's
    /// snap positions"): the closest snap position of the element; the axes in which the element has no snap position snap as usual.
    /// </summary>
    public PhysicalOffset GetSnapPosition(ScrollState state, SnapSelectionStrategy strategy, Element element)
    {
        if (GetSnapContainer(state) is not { } container || container.FindSnapPosition(SnapSelectionStrategy.ForElementAreas(strategy, element)) is not { Found: true } result)
            return GetSnapPosition(state, strategy);

        var snapsX = result.TargetX is not null || !container.SnapsOnX;
        var snapsY = result.TargetY is not null || !container.SnapsOnY;
        if (snapsX && snapsY)
        {
            SetSnapTargets(state, result.TargetX, result.TargetY);
            return state.Clamp(result.Position);
        }

        // (the other axis is snapped from the position of the element in the axis where it snaps)
        var intended = strategy.IntendedPosition;
        var other = container.FindSnapPosition(SnapSelectionStrategy.ForEndPosition(new PhysicalOffset(snapsX ? result.Position.X : intended.X, snapsY ? result.Position.Y : intended.Y), scrolledX: !snapsX, scrolledY: !snapsY));
        var position = new PhysicalOffset(snapsX ? result.Position.X : other.Found ? other.Position.X : intended.X, snapsY ? result.Position.Y : other.Found ? other.Position.Y : intended.Y);
        SetSnapTargets(state, snapsX ? result.TargetX : other.TargetX, snapsY ? result.TargetY : other.TargetY);
        return state.Clamp(position);
    }

    public PhysicalOffset GetSnapPosition(ScrollState state, SnapSelectionStrategy strategy)
    {
        var position = strategy.IntendedPosition;
        if (GetSnapContainer(state) is { } container)
        {
            var result = container.FindSnapPosition(strategy);
            SetSnapTargets(state, result.TargetX, result.TargetY);
            if (result.Found)
            {
                position = result.Position;
            }
        }

        return state.Clamp(position);
    }

    /// <summary>Returns the snap container of a scrolling box in the last layout, or null when it does not snap.</summary>
    internal SnapContainer? GetSnapContainer(ScrollState state)
    {
        if (state.Fragment is null)
            return null;

        EnsureSnapContainers();
        if (state.Snap?.Container is not { } container)
            return null;

        container.UpdateAreaStates(_document.FocusedArea, HtmlElementStates.GetTargetElement(_document));
        return container;
    }

    /// <summary>
    /// Updates the scrollsnapchange targets of a scrolling box (https://drafts.csswg.org/css-scroll-snap-2/#update-scrollsnapchange-targets)
    /// when a scroll operation is completed: a <c>scrollsnapchange</c> event is queued when they changed.
    /// </summary>
    internal void UpdateScrollSnapChangeTargets(ScrollState state)
    {
        if (state.Snap is not { EventTargetsInitialized: true } snap || (snap.ChangeBlock == snap.ChangingBlock && snap.ChangeInline == snap.ChangingInline))
            return;

        snap.ChangeBlock = snap.ChangingBlock;
        snap.ChangeInline = snap.ChangingInline;
        QueueSnapEvent(state, "scrollsnapchange", snap.ChangeBlock, snap.ChangeInline);
    }

    /// <summary>
    /// Sets the snap targets of a scrolling box, and updates its scrollsnapchanging targets
    /// (https://drafts.csswg.org/css-scroll-snap-2/#update-scrollsnapchanging-targets): a <c>scrollsnapchanging</c> event is
    /// queued when they change. The first targets of a box are set without snap events: the layout is computed lazily (when the
    /// geometry is read, or for the observers of the scroll and snap events), so the first snap may happen long after the load
    /// of the document, unlike in the browsers, which fire a scrollsnapchange event for the first snap.
    /// </summary>
    private void SetSnapTargets(ScrollState state, SnapTarget? targetX, SnapTarget? targetY)
    {
        var snap = state.Snap ??= new ScrollSnapState();
        snap.TargetX = targetX;
        snap.TargetY = targetY;
        if (snap.Container is { } container)
        {
            container.TargetX = targetX;
            container.TargetY = targetY;
        }

        // The block axis is the vertical axis in the horizontal writing modes
        var horizontal = state.WritingDirection.IsHorizontal;
        var block = horizontal ? targetY : targetX;
        var inline = horizontal ? targetX : targetY;
        if (!snap.EventTargetsInitialized)
        {
            snap.EventTargetsInitialized = true;
            snap.ChangingBlock = snap.ChangeBlock = block;
            snap.ChangingInline = snap.ChangeInline = inline;
            return;
        }

        if (block == snap.ChangingBlock && inline == snap.ChangingInline)
            return;

        snap.ChangingBlock = block;
        snap.ChangingInline = inline;
        QueueSnapEvent(state, "scrollsnapchanging", block, inline);
    }

    private void QueueSnapEvent(ScrollState state, string type, SnapTarget? block, SnapTarget? inline)
        => QueuePendingScrollEvent(state.Element ?? (EventTarget)_document, type, block?.Element, inline?.Element);

    /// <summary>
    /// Called when a listener of a scroll event or of a snap event is added: these events make the scroll positions observable
    /// (e.g. a fragment navigation, the snapping after a layout change), so the layout of the document is computed by the next
    /// rendering updates (a document whose geometry is never read is not laid out).
    /// </summary>
    internal static void OnScrollEventListenerAdded(EventTarget target)
    {
        var document = target switch
        {
            Node node => node.NodeDocument,
            Window window => window.Document,
            _ => null,
        };
        if (document?.BrowsingContext is not null)
        {
            document.GetLayoutEngine().RequestRenderingUpdates();
        }
    }

    /// <summary>
    /// Re-snaps the scroll snap containers after a layout pass (https://drafts.csswg.org/css-scroll-snap-1/#re-snap): a stationary
    /// scroll that follows the snap targets the box was snapped to (or snaps to the closest snap position, e.g. after the first
    /// layout). The scroll to the same targets is instant, the scroll to other targets uses scroll-behavior. A box that stops
    /// snapping is not snapped to any target anymore (https://drafts.csswg.org/css-scroll-snap-2/#scrollsnapchange).
    /// </summary>
    private void ResnapAfterLayout()
    {
        if (!_hasSnapStates && !HasSnapContainerStyle())
            return;

        EnsureSnapContainers();
        if (!_hasSnapStates)
            return;

        foreach (var state in EnumerateScrollStates())
        {
            if (state.Snap is not { } snap || state.Fragment is null)
                continue;

            if (snap.Container is not { } container)
            {
                if (snap.TargetX is not null || snap.TargetY is not null)
                {
                    SetSnapTargets(state, null, null);
                }

                UpdateScrollSnapChangeTargets(state);
                continue;
            }

            // A scroll gesture of the user snaps at its end: the snap targets it would snap to are updated (scrollsnapchanging)
            if (state.UserGestureStart is { } gestureStart)
            {
                UpdateUserGestureSnapTargets(state, gestureStart, state.Offset);
                continue;
            }

            // A smooth scroll in progress snaps at its end
            if (state.SmoothScroll is not null)
                continue;

            var previousX = snap.TargetX;
            var previousY = snap.TargetY;
            container.UpdateAreaStates(_document.FocusedArea, HtmlElementStates.GetTargetElement(_document));
            var result = container.FindSnapPosition(SnapSelectionStrategy.ForTargetElement(state.Offset));
            SetSnapTargets(state, result.TargetX, result.TargetY);
            var position = state.Clamp(result.Found ? result.Position : state.Offset);
            if (position == state.Offset)
            {
                UpdateScrollSnapChangeTargets(state);
                continue;
            }

            var sameTargets = previousX == result.TargetX && previousY == result.TargetY;
            _ = PerformScrollCore(state, position, sameTargets ? ScrollBehavior.Instant : ScrollBehavior.Auto, state.GetAssociatedElement(_document));
        }
    }

    /// <summary>Whether a scrolling box of the last layout has a scroll-snap-type (a cheap check before the snap areas are collected).</summary>
    private bool HasSnapContainerStyle()
    {
        if (_viewportScrollState is { } viewport && HasSnapType(viewport))
            return true;

        if (_scrollStates is not null)
        {
            foreach (var state in _scrollStates.Values)
            {
                if (HasSnapType(state))
                    return true;
            }
        }

        if (_pseudoElementScrollStates is not null)
        {
            foreach (var state in _pseudoElementScrollStates.Values)
            {
                if (HasSnapType(state))
                    return true;
            }
        }

        return false;

        static bool HasSnapType(ScrollState state) => state.Fragment is not null && state.ContainerStyle is { } style && !style.Overflow.ScrollSnapType.IsNone;
    }

    /// <summary>
    /// Computes the snap containers of the last layout and the snap areas they capture
    /// (https://drafts.csswg.org/css-scroll-snap-1/#snap-container): "In each axis, a snap position is captured by its nearest
    /// ancestor scroll container on its associated element's containing block chain that is scrollable in that axis".
    /// </summary>
    private void EnsureSnapContainers()
    {
        if (_snapContainersVersion == _fragmentIndexVersion)
            return;

        _snapContainersVersion = _fragmentIndexVersion;
        _hasSnapStates = false;
        var hasContainers = false;
        foreach (var state in EnumerateScrollStates())
        {
            if (state.Fragment is null)
                continue;

            var container = CreateSnapContainer(state);
            if (container is null && state.Snap is null)
                continue;

            var snap = state.Snap ??= new ScrollSnapState();
            snap.Container = container;
            _hasSnapStates = true;
            hasContainers |= container is not null;
        }

        if (!hasContainers || _boxTree?.Viewport is not { } viewport)
            return;

        CollectSnapAreas(viewport);

        // The snap targets are kept only when their snap areas still exist
        foreach (var state in EnumerateScrollStates())
        {
            if (state.Snap is not { Container: { } container } snap)
                continue;

            snap.TargetX = container.TargetX = FindArea(container, snap.TargetX);
            snap.TargetY = container.TargetY = FindArea(container, snap.TargetY);
        }

        static SnapTarget? FindArea(SnapContainer container, SnapTarget? target)
        {
            if (target is { } value)
            {
                foreach (var area in container.Areas)
                {
                    if (area.Target == value)
                        return value;
                }
            }

            return null;
        }
    }

    /// <summary>Returns the snap container of a scrolling box (its snapport, scroll range and snapping axes), or null when its scroll-snap-type is none.</summary>
    private static SnapContainer? CreateSnapContainer(ScrollState state)
    {
        // The scroll-snap-type and scroll-padding of the root element apply to the viewport (they are not propagated from the body)
        if (state.ContainerStyle is not { } style)
            return null;

        var overflow = style.Overflow;
        var type = overflow.ScrollSnapType;
        if (type.IsNone)
            return null;

        // The logical axes are resolved with the writing mode of the scroll container
        var horizontal = state.WritingDirection.IsHorizontal;
        type = type.Axis switch
        {
            ScrollSnapAxis.Block => type with { Axis = horizontal ? ScrollSnapAxis.Y : ScrollSnapAxis.X },
            ScrollSnapAxis.Inline => type with { Axis = horizontal ? ScrollSnapAxis.X : ScrollSnapAxis.Y },
            _ => type,
        };

        var snapport = ScrollIntoView.ApplyScrollPadding(state.Scrollport, overflow.ScrollPadding);
        var proximity = type.Strictness == ScrollSnapStrictness.Mandatory
            ? new PhysicalOffset(double.MaxValue, double.MaxValue)
            : new PhysicalOffset(snapport.Width * ProximityRatio, snapport.Height * ProximityRatio);
        return new SnapContainer(type, snapport, state.MinOffset, state.MaxOffset, proximity, horizontal);
    }

    /// <summary>Collects the snap areas of the boxes, in tree order, into the snap containers that capture them.</summary>
    private void CollectSnapAreas(ContainerBox viewport)
    {
        var boxTree = _boxTree!;
        Dictionary<(SnapContainer Container, LayoutBox Box), int>? indexes = null;
        var stack = new Stack<LayoutBox>();
        stack.Push(viewport);
        while (stack.TryPop(out var box))
        {
            if (box is not ContainerBox container)
                continue;

            for (var child = container.LastChild; child is not null; child = child.PreviousSibling)
            {
                stack.Push(child);
            }

            // https://drafts.csswg.org/css-multicol-2/#column-pseudo: the column boxes of a multi-column container whose ::column
            // pseudo-elements have a scroll-snap-align are snap areas
            if (container is { PseudoElement: null, Element: { } columnsElement } && MultiColumnFormattingContext.IsMultiColumnContainer(container))
            {
                AddColumnSnapAreas(container, columnsElement);
            }

            if (container.Element is not { } element || container.Style.Layout.Overflow.ScrollSnapAlign.IsNone)
                continue;

            // The principal box of the element (or the box of its pseudo-element) with its continuations
            var principal = container.PseudoElement is { } pseudo ? boxTree.GetPseudoElementBox(element, pseudo) : boxTree.GetPrincipalBox(element);
            if (!ReferenceEquals(principal, container) || GetFirstFragment(container) is not BoxFragment first)
                continue;

            var nearest = FindCapturingScrollContainers(first, out var containerX, out var containerY);
            var snapX = containerX?.ScrollState?.Snap?.Container;
            var snapY = containerY?.ScrollState?.Snap?.Container;
            if (snapX is null && snapY is null)
                continue;

            var target = new SnapTarget(element, container.PseudoElement);
            if (snapX is not null)
            {
                AddArea(snapX, containerX!, captureX: true, captureY: ReferenceEquals(containerX, containerY));
            }

            if (snapY is not null && !ReferenceEquals(containerX, containerY))
            {
                AddArea(snapY, containerY!, captureX: false, captureY: true);
            }

            void AddArea(SnapContainer snapContainer, BoxFragment scrollContainer, bool captureX, bool captureY)
            {
                var rect = GetSnapAreaRect(container, scrollContainer);
                var (alignX, alignY) = GetPhysicalAlignment(container.Style.Layout, rect, nearest!, scrollContainer);
                var areas = snapContainer.Areas;

                // The nearest snap area of an ancestor in the same container (an ancestor is not snapped to when its descendant is aligned at the same position)
                var parentOffset = 0;
                if (indexes is not null)
                {
                    for (var ancestor = (LayoutBox?)container.Parent; ancestor is not null; ancestor = ancestor.Parent)
                    {
                        if (indexes.TryGetValue((snapContainer, ancestor), out var parentIndex))
                        {
                            parentOffset = areas.Count - parentIndex;
                            break;
                        }
                    }
                }

                (indexes ??= [])[(snapContainer, container)] = areas.Count;
                areas.Add(new SnapArea(target, rect, captureX ? alignX : SnapAlignment.None, captureY ? alignY : SnapAlignment.None, container.Style.Layout.Overflow.ScrollSnapStopAlways)
                {
                    ParentOffset = parentOffset,
                });
            }
        }
    }

    /// <summary>
    /// Adds the snap areas of the column boxes of a multi-column container (https://drafts.csswg.org/css-multicol-2/#column-pseudo):
    /// the <c>scroll-snap-align</c> of its <c>::column</c> pseudo-elements, the column boxes of its last layout extended by their
    /// <c>scroll-margin</c>, captured by the container itself when it is a scroll container (the columns are its contents).
    /// </summary>
    private void AddColumnSnapAreas(ContainerBox box, Element element)
    {
        var styleEngine = _document.GetStyleEngine();
        if (!styleEngine.MayHaveAuthorPseudoElementRules(element, ScrollControls.Column) || GetFirstFragment(box) is not BoxFragment { ColumnRects: { } columns } fragment)
            return;

        var style = styleEngine.GetStyle(element, ScrollControls.Column).Layout;
        if (style.Overflow.ScrollSnapAlign.IsNone)
            return;

        var nearest = FindCapturingScrollContainers(fragment, out var containerX, out var containerY);
        if (fragment.ScrollState is { } ownState)
        {
            nearest = fragment;
            containerX = ScrollContainers.IsScrollable(ownState.Overflow.X) ? fragment : containerX;
            containerY = ScrollContainers.IsScrollable(ownState.Overflow.Y) ? fragment : containerY;
        }

        var snapX = containerX?.ScrollState?.Snap?.Container;
        var snapY = containerY?.ScrollState?.Snap?.Container;
        if (snapX is null && snapY is null)
            return;

        for (var i = 0; i < columns.Length; i++)
        {
            var target = new SnapTarget(element, ScrollControls.GetColumn(i));
            var columnRect = fragment.WritingDirection.ToPhysical(columns[i], fragment.Size);
            if (snapX is not null)
            {
                AddArea(snapX, containerX!, captureX: true, captureY: ReferenceEquals(containerX, containerY));
            }

            if (snapY is not null && !ReferenceEquals(containerX, containerY))
            {
                AddArea(snapY, containerY!, captureX: false, captureY: true);
            }

            void AddArea(SnapContainer snapContainer, BoxFragment scrollContainer, bool captureX, bool captureY)
            {
                var rect = (ReferenceEquals(scrollContainer, fragment) ? columnRect : MapToAncestor(fragment, columnRect, scrollContainer)).Expand(style.Overflow.ScrollMargin);
                var (alignX, alignY) = GetPhysicalAlignment(style, rect, nearest!, scrollContainer);
                snapContainer.Areas.Add(new SnapArea(target, rect, captureX ? alignX : SnapAlignment.None, captureY ? alignY : SnapAlignment.None, style.Overflow.ScrollSnapStopAlways));
            }
        }
    }

    /// <summary>
    /// Finds the nearest scroll containers on the containing block chain of a fragment that are scrollable in each axis, and
    /// returns the nearest scroll container (null when there is none).
    /// </summary>
    private static BoxFragment? FindCapturingScrollContainers(Fragment fragment, out BoxFragment? containerX, out BoxFragment? containerY)
    {
        containerX = null;
        containerY = null;
        BoxFragment? nearest = null;
        for (var parent = fragment.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not BoxFragment { ScrollState: { } state } scrollContainer)
                continue;

            nearest ??= scrollContainer;

            if (containerX is null && ScrollContainers.IsScrollable(state.Overflow.X))
            {
                containerX = scrollContainer;
            }

            if (containerY is null && ScrollContainers.IsScrollable(state.Overflow.Y))
            {
                containerY = scrollContainer;
            }

            if (containerX is not null && containerY is not null)
                return nearest;
        }

        return nearest;
    }

    /// <summary>
    /// Returns the snap area of a box (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-area): the bounding box of the
    /// transformed border boxes of its fragments, in the coordinates of the scroll container at the scroll position 0, extended
    /// by scroll-margin.
    /// </summary>
    private PhysicalRect GetSnapAreaRect(ContainerBox box, BoxFragment scrollContainer)
    {
        var rect = default(PhysicalRect);
        var first = true;
        for (var part = box; part is not null; part = part.Continuation)
        {
            for (var fragment = GetFirstFragment(part); fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                if (fragment is not BoxFragment boxFragment)
                    continue;

                var bounds = MapToAncestor(boxFragment, boxFragment.BorderBoxRect, scrollContainer);
                rect = first ? bounds : rect.Union(bounds);
                first = false;
            }
        }

        return rect.Expand(box.Style.Layout.Overflow.ScrollMargin);
    }

    /// <summary>Maps a rectangle of a fragment to the coordinates of an ancestor at its scroll position 0 (the scroll positions of the scroll containers in between are applied), and returns its bounding box.</summary>
    private static PhysicalRect MapToAncestor(Fragment fragment, PhysicalRect rect, Fragment ancestor)
    {
        var quad = new PhysicalQuad(rect);
        for (var current = fragment; current is not null && !ReferenceEquals(current, ancestor); current = current.Parent)
        {
            if (GetTransform(current) is { } matrix)
            {
                quad = new PhysicalQuad(Transform(matrix, quad.P1), Transform(matrix, quad.P2), Transform(matrix, quad.P3), Transform(matrix, quad.P4));
            }

            quad = quad.Translate(ReferenceEquals(current.Parent, ancestor) ? current.Offset : GetScrolledOffset(current));
        }

        return quad.Bounds;

        static PhysicalOffset Transform(double[] matrix, PhysicalOffset point) => LayoutTransforms.TryTransformPoint(matrix, point, out var result) ? result : point;
    }

    /// <summary>
    /// Resolves scroll-snap-align to the physical axes (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-align): the block
    /// and inline axes of the writing mode of the nearest scroll container (even when another one captures the snap area in an
    /// axis), start and end resolved with respect to the writing mode of the snap container, unless the snap area is larger than
    /// the snap container, in which case they are resolved with respect to the writing mode of the box itself.
    /// </summary>
    private static (SnapAlignment X, SnapAlignment Y) GetPhysicalAlignment(LayoutStyle style, PhysicalRect areaRect, BoxFragment nearestScrollContainer, BoxFragment snapContainer)
    {
        var align = style.Overflow.ScrollSnapAlign;
        var resolvingDirection = nearestScrollContainer.ScrollState!.WritingDirection;
        var containerDirection = snapContainer.ScrollState!.WritingDirection;
        var areaDirection = WritingDirection.FromStyle(style);
        var flipX = IsFlippedX(areaRect.Width > snapContainer.Size.Width ? areaDirection : containerDirection);
        var flipY = IsFlippedY(areaRect.Height > snapContainer.Size.Height ? areaDirection : containerDirection);
        return resolvingDirection.IsHorizontal
            ? (Flip(align.Inline, flipX), Flip(align.Block, flipY))
            : (Flip(align.Block, flipX), Flip(align.Inline, flipY));

        static bool IsFlippedX(WritingDirection direction) => (direction.IsHorizontal ? direction.InlineStart : direction.BlockStart) == PhysicalSide.Right;

        static bool IsFlippedY(WritingDirection direction) => (direction.IsHorizontal ? direction.BlockStart : direction.InlineStart) == PhysicalSide.Bottom;

        static SnapAlignment Flip(SnapAlignment alignment, bool flip) => !flip ? alignment : alignment switch
        {
            SnapAlignment.Start => SnapAlignment.End,
            SnapAlignment.End => SnapAlignment.Start,
            _ => alignment,
        };
    }
}
