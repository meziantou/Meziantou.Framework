using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The container scroll-state queries (https://drafts.csswg.org/css-conditional-5/#scroll-state-container): the state of the
// scroll-state query containers is snapshotted by the rendering steps ("run snapshot post-layout state steps",
// https://drafts.csswg.org/cssom-view/#post-layout-snapshot, in the resize observer loop of "update the rendering"), and the
// queries are evaluated against the last snapshot (see ContainerQueryContext.EvaluateScrollStateFeature): a change of the scroll
// positions or of the layout is reflected by the styles after the next snapshot, which avoids the layout cycles. A change of a
// snapshot starts a new generation of styles (see StyleEngine), so that the styles of the whole document are computed again, as
// for any other style change (the transitions compare the new styles with the styles before the change).
internal sealed partial class LayoutEngine
{
    /// <summary>The scroll-state query containers of the last layout pass: the first fragments of their principal boxes (see <see cref="IndexFragments"/>).</summary>
    private List<BoxFragment>? _scrollStateContainers;

    /// <summary>The snapshots of the scroll-state query containers whose state is not none (see <see cref="UpdateScrollStateSnapshots"/>).</summary>
    private Dictionary<Element, ScrollStateSnapshot>? _scrollStateSnapshots;

    /// <summary>The dictionary of the previous snapshots, reused by the next update.</summary>
    private Dictionary<Element, ScrollStateSnapshot>? _spareScrollStateSnapshots;

    /// <summary>The scroll-state query containers whose state was snapshotted (by the last snapshot post-layout state steps, or by a layout pass since then).</summary>
    private HashSet<Element>? _snapshottedScrollStateContainers;

    /// <summary>Whether a style evaluated a scroll-state feature: a change of the snapshots then changes the styles.</summary>
    private bool _scrollStateQueried;

    /// <summary>
    /// The number of changes of the snapshots that a style may depend on: the styles are computed again when it changes (see
    /// <see cref="Cascade.StyleEngine"/>).
    /// </summary>
    internal int ScrollStateSnapshotVersion { get; private set; }

    /// <summary>Returns the snapshotted state of a scroll-state query container (none before its first snapshot, or when it is not rendered).</summary>
    public ScrollStateSnapshot GetScrollStateSnapshot(Element element)
        => _scrollStateSnapshots is not null && _scrollStateSnapshots.TryGetValue(element, out var snapshot) ? snapshot : ScrollStateSnapshot.None;

    /// <summary>
    /// Called when a style evaluates a scroll-state feature: the document is laid out by the rendering steps (which take the
    /// snapshots), and the changes of the snapshots change the styles from now on.
    /// </summary>
    internal void OnScrollStateQueried()
    {
        if (_scrollStateQueried)
            return;

        _scrollStateQueried = true;
        RequestRenderingUpdates();
    }

    /// <summary>
    /// Records the direction of a relative scroll (https://drafts.csswg.org/css-scroll-snap-1/#relative-scroll) of a scrolling box
    /// (the difference between the position it scrolls to and its current position): the direction of an axis is kept when the
    /// scroll does not move in it (https://drafts.csswg.org/css-conditional-5/#scrolled).
    /// </summary>
    internal void RecordRelativeScroll(ScrollState state, PhysicalOffset delta)
    {
        var directions = state.RelativeScrollDirections;
        if (delta.X != 0)
        {
            directions = (directions & ~PhysicalEdges.Horizontal) | (delta.X > 0 ? PhysicalEdges.Right : PhysicalEdges.Left);
        }

        if (delta.Y != 0)
        {
            directions = (directions & ~PhysicalEdges.Vertical) | (delta.Y > 0 ? PhysicalEdges.Bottom : PhysicalEdges.Top);
        }

        if (directions == state.RelativeScrollDirections)
            return;

        state.RelativeScrollDirections = directions;
        if (_scrollStateQueried)
        {
            _document.EventLoop?.RequestRenderingUpdate();
        }
    }

    /// <summary>
    /// Updates the current state of every scroll-state query container (https://drafts.csswg.org/css-conditional-5/#updating-scroll-state),
    /// from the layout and the scroll positions: called by the "run snapshot post-layout state steps" of the rendering steps, after
    /// the styles and the layout are updated. Returns whether a snapshot that the styles may depend on changed: the styles and the
    /// layout must then be updated again ("if any snapshotted state changed, then continue").
    /// </summary>
    internal bool UpdateScrollStateSnapshots()
    {
        if (_scrollStateContainers is null && _scrollStateSnapshots is null)
            return false;

        var snapshots = _spareScrollStateSnapshots;
        snapshots?.Clear();
        _snapshottedScrollStateContainers?.Clear();
        if (_scrollStateContainers is not null)
        {
            foreach (var fragment in _scrollStateContainers)
            {
                var element = fragment.Box.Element!;
                (_snapshottedScrollStateContainers ??= new(ReferenceEqualityComparer.Instance)).Add(element);
                var snapshot = TakeScrollStateSnapshot(element, fragment);
                if (!snapshot.IsNone)
                {
                    (snapshots ??= new(ReferenceEqualityComparer.Instance))[element] = snapshot;
                }
            }
        }

        var changed = !AreEqual(_scrollStateSnapshots, snapshots);
        _spareScrollStateSnapshots = _scrollStateSnapshots;
        _scrollStateSnapshots = snapshots is { Count: > 0 } ? snapshots : null;
        if (snapshots is { Count: 0 })
        {
            _spareScrollStateSnapshots ??= snapshots;
        }

        if (!changed || !_scrollStateQueried)
            return false;

        ScrollStateSnapshotVersion++;
        return true;

        static bool AreEqual(Dictionary<Element, ScrollStateSnapshot>? first, Dictionary<Element, ScrollStateSnapshot>? second)
        {
            var count = first?.Count ?? 0;
            if (count != (second?.Count ?? 0))
                return false;

            if (count == 0)
                return true;

            foreach (var (element, snapshot) in first!)
            {
                if (!second!.TryGetValue(element, out var other) || other != snapshot)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Takes the first snapshot of the scroll-state query containers laid out for the first time (e.g. inserted, or whose
    /// <c>container-type</c> changed) at the end of a layout pass, as the browsers do: their queries are evaluated against their
    /// initial state from the next style update (a forced one, e.g. <c>getComputedStyle()</c>, or the one of the rendering steps),
    /// instead of none until the next rendering update. The snapshots of the other containers are only updated by the rendering
    /// steps (see <see cref="UpdateScrollStateSnapshots"/>). Returns whether the styles must be computed again.
    /// </summary>
    private bool TakeInitialScrollStateSnapshots()
    {
        if (_scrollStateContainers is not { Count: > 0 } containers)
            return false;

        var changed = false;
        foreach (var fragment in containers)
        {
            var element = fragment.Box.Element!;
            if (!(_snapshottedScrollStateContainers ??= new(ReferenceEqualityComparer.Instance)).Add(element))
                continue;

            var snapshot = TakeScrollStateSnapshot(element, fragment);
            if (!snapshot.IsNone)
            {
                (_scrollStateSnapshots ??= new(ReferenceEqualityComparer.Instance))[element] = snapshot;
                changed = true;
            }
        }

        if (!changed || !_scrollStateQueried)
            return false;

        ScrollStateSnapshotVersion++;
        return true;
    }

    /// <summary>Returns the current state of a scroll-state query container, from the first fragment of its principal box.</summary>
    private ScrollStateSnapshot TakeScrollStateSnapshot(Element element, BoxFragment fragment)
    {
        // https://drafts.csswg.org/css-conditional-5/#stuck: the sides of the sticky view rectangle whose insets shift the box (a
        // positive offset comes from the top or left inset, a negative one from the bottom or right inset)
        var stuck = PhysicalEdges.None;
        if (fragment.Box.Style.Layout.Position == PositionKind.Sticky)
        {
            var offset = fragment.StickyOffset;
            if (offset.X > 0)
            {
                stuck |= PhysicalEdges.Left;
            }
            else if (offset.X < 0)
            {
                stuck |= PhysicalEdges.Right;
            }

            if (offset.Y > 0)
            {
                stuck |= PhysicalEdges.Top;
            }
            else if (offset.Y < 0)
            {
                stuck |= PhysicalEdges.Bottom;
            }
        }

        // https://drafts.csswg.org/css-conditional-5/#snapped: the snap targets the scroll snap container (the nearest scroll
        // container in the containing block chain, the viewport for the root fragment) is snapped to
        var snapped = PhysicalEdges.None;
        for (var parent = fragment.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not BoxFragment { ScrollState: { } scroller })
                continue;

            if (scroller.Snap is { } snap)
            {
                if (snap.TargetX is { PseudoElement: null } targetX && ReferenceEquals(targetX.Element, element))
                {
                    snapped |= PhysicalEdges.Horizontal;
                }

                if (snap.TargetY is { PseudoElement: null } targetY && ReferenceEquals(targetY.Element, element))
                {
                    snapped |= PhysicalEdges.Vertical;
                }
            }

            break;
        }

        // https://drafts.csswg.org/css-conditional-5/#scrollable and #scrolled: the scrolling box of the container (the viewport
        // for the root element, whose overflow is propagated to the viewport)
        var scrollable = PhysicalEdges.None;
        var scrolled = PhysicalEdges.None;
        var state = ReferenceEquals(element, _document.DocumentElement) ? _root?.ScrollState : fragment.ScrollState;
        if (state is { Fragment: not null })
        {
            // The overflow that is not reachable by the user (overflow: hidden) does not match
            const double Epsilon = 1e-3;
            var min = state.MinOffset;
            var max = state.MaxOffset;
            var position = state.Offset;
            if (state.Overflow.X is OverflowKind.Auto or OverflowKind.Scroll)
            {
                if (position.X > min.X + Epsilon)
                {
                    scrollable |= PhysicalEdges.Left;
                }

                if (position.X < max.X - Epsilon)
                {
                    scrollable |= PhysicalEdges.Right;
                }
            }

            if (state.Overflow.Y is OverflowKind.Auto or OverflowKind.Scroll)
            {
                if (position.Y > min.Y + Epsilon)
                {
                    scrollable |= PhysicalEdges.Top;
                }

                if (position.Y < max.Y - Epsilon)
                {
                    scrollable |= PhysicalEdges.Bottom;
                }
            }

            scrolled = state.RelativeScrollDirections;
        }

        return new ScrollStateSnapshot(stuck, snapped, scrollable, scrolled);
    }
}
