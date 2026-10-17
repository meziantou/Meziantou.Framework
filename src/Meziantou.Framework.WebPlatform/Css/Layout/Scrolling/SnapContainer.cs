using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A snap target (https://drafts.csswg.org/css-scroll-snap-2/#snap-target): an element or one of its pseudo-elements, which
/// identifies a snap area across the layout passes.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SnapTarget(Element Element, string? PseudoElement);

/// <summary>
/// A scroll snap area (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-area) captured by a snap container: the
/// bounding box of the transformed border boxes of the fragments of a box, extended by <c>scroll-margin</c>, in the coordinates
/// of the scroll container at the scroll position 0, and its alignments resolved to the physical axes.
/// </summary>
internal sealed class SnapArea
{
    public SnapArea(SnapTarget target, PhysicalRect rect, SnapAlignment alignX, SnapAlignment alignY, bool mustSnap)
    {
        Target = target;
        Rect = rect;
        AlignX = alignX;
        AlignY = alignY;
        MustSnap = mustSnap;
    }

    public SnapTarget Target { get; }

    public PhysicalRect Rect { get; }

    /// <summary>The alignment in the horizontal axis: <see cref="SnapAlignment.Start"/> aligns the left edges.</summary>
    public SnapAlignment AlignX { get; internal set; }

    /// <summary>The alignment in the vertical axis: <see cref="SnapAlignment.Start"/> aligns the top edges.</summary>
    public SnapAlignment AlignY { get; internal set; }

    /// <summary>Whether <c>scroll-snap-stop</c> is <c>always</c>.</summary>
    public bool MustSnap { get; }

    /// <summary>Whether the element is focused or has a focused descendant (updated before each selection of a snap position).</summary>
    public bool HasFocusWithin { get; set; }

    /// <summary>Whether the element is the target element of the document (<c>:target</c>) or has it as a descendant (updated before each selection of a snap position).</summary>
    public bool IsTargeted { get; set; }

    /// <summary>The distance in the list of the snap areas of the container to the nearest snap area of an ancestor, or 0.</summary>
    public int ParentOffset { get; init; }

    /// <summary>Whether the area has an alignment in both axes.</summary>
    public bool HasDualAxisAlignment => AlignX != SnapAlignment.None && AlignY != SnapAlignment.None;
}

/// <summary>The result of the selection of a snap position (https://drafts.csswg.org/css-scroll-snap-1/#choosing).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SnapPosition(bool Found, PhysicalOffset Position, SnapTarget? TargetX, SnapTarget? TargetY);

/// <summary>A range of scroll positions, possibly reversed (its bounds are its minimum and its maximum).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SnapRange(double Start, double End)
{
    public double Min => Math.Min(Start, End);

    public double Max => Math.Max(Start, End);

    public bool IsEmpty => Start == End;

    public bool IsReversed => Start > End;

    public bool IsBoundedBy(SnapRange other) => Min >= other.Min && Max <= other.Max;

    /// <summary>Whether the range contains another one (a non-empty range does not contain the empty range at its maximum).</summary>
    public bool Contains(SnapRange other) => other.IsBoundedBy(this) && (other.Max != Max || other.IsEmpty == IsEmpty);

    /// <summary>Whether the ranges intersect (the ranges that only touch do not intersect).</summary>
    public bool Intersects(SnapRange other) => Math.Max(Min, other.Min) < Math.Min(Max, other.Max) || Contains(other) || other.Contains(this);

    public bool Contains(double value) => value >= Min && value <= Max;
}

/// <summary>A candidate snap position in an axis, for a snap area (or for the current position in the cross axis).</summary>
internal sealed class SnapSearchResult
{
    public double SnapOffset { get; set; }

    public SnapSearchAxis Axis { get; set; }

    /// <summary>The range of the snapport in the cross axis, in the coordinates of the scroll container.</summary>
    public SnapRange SnapportVisibleRange { get; set; }

    /// <summary>The range of the scroll positions in the cross axis.</summary>
    public SnapRange CrossAxisScrollRange { get; set; }

    /// <summary>The rectangle of the snap area, in the coordinates of the scroll container at the scroll position 0.</summary>
    public PhysicalRect? Rect { get; set; }

    public SnapArea? Area { get; set; }

    /// <summary>The range of the scroll positions at which the snap area covers the snapport, when it is larger than the snapport.</summary>
    public SnapRange? CoveredRange { get; set; }

    /// <summary>A snap area aligned at the same position that may be better when it is also aligned in the cross axis.</summary>
    public (SnapArea Area, double CrossAxisSnapOffset)? Alternative { get; set; }

    public bool HasFocusWithin => Area is { HasFocusWithin: true };

    /// <summary>The range of the scroll positions in the cross axis at which the snap area intersects the snapport.</summary>
    public SnapRange VisibleRange
    {
        get
        {
            if (Rect is not { } rect)
                return CrossAxisScrollRange;

            var start = Axis == SnapSearchAxis.X ? rect.Top : rect.Left;
            var end = Axis == SnapSearchAxis.X ? rect.Bottom : rect.Right;
            return new SnapRange(
                Math.Clamp(start - SnapportVisibleRange.End, CrossAxisScrollRange.Start, CrossAxisScrollRange.End),
                Math.Clamp(end - SnapportVisibleRange.Start, CrossAxisScrollRange.Start, CrossAxisScrollRange.End));
        }
    }

    public SnapSearchResult Clone() => (SnapSearchResult)MemberwiseClone();

    /// <summary>Unites the rectangles of two snap areas that both cover the snapport at the same position.</summary>
    public void Union(SnapSearchResult other)
    {
        if (Rect is { } rect && other.Rect is { } otherRect)
        {
            Rect = rect.Union(otherRect);
        }
    }
}

/// <summary>
/// A scroll snap container (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-container): its snapport, its scroll range
/// and the snap areas it captures, after a layout pass. The selection of the snap positions
/// (https://drafts.csswg.org/css-scroll-snap-1/#choosing) is modeled on the one of Blink (<c>cc/input/scroll_snap_data.cc</c>):
/// the closest valid snap position in each axis (from the intended position, ahead in the direction of a relative scroll),
/// the snap areas larger than the snapport (https://drafts.csswg.org/css-scroll-snap-1/#snap-overflow), the visibility of the
/// snap areas in the other axis (https://drafts.csswg.org/css-scroll-snap-1/#snap-scope), <c>scroll-snap-stop</c>, and the
/// selection between multiple aligned snap areas (https://drafts.csswg.org/css-scroll-snap-1/#multiple-aligned-snap-areas).
/// </summary>
internal sealed class SnapContainer
{
    // The positions within 0.5px of the edges of a covering snap area are considered covered (rounding of the positions)
    private const double SnapportCoveredTolerance = 0.5;

    // A position within 1px of a snap position is snapped to it
    private const double SnappedToTolerance = 1;

    private readonly List<SnapArea> _areas = [];

    public SnapContainer(ScrollSnapType type, PhysicalRect snapport, PhysicalOffset minPosition, PhysicalOffset maxPosition, PhysicalOffset proximityRange, bool hasHorizontalWritingMode)
    {
        Type = type;
        Snapport = snapport;
        MinPosition = minPosition;
        MaxPosition = maxPosition;
        ProximityRange = proximityRange;
        HasHorizontalWritingMode = hasHorizontalWritingMode;
    }

    /// <summary>The scroll-snap-type, whose axis is physical (<see cref="ScrollSnapAxis.X"/>, <see cref="ScrollSnapAxis.Y"/>, <see cref="ScrollSnapAxis.Both"/> or <see cref="ScrollSnapAxis.Pair"/>).</summary>
    public ScrollSnapType Type { get; }

    /// <summary>The snapport (https://drafts.csswg.org/css-scroll-snap-1/#scroll-padding): the scrollport reduced by scroll-padding, in the coordinates of the scroll container.</summary>
    public PhysicalRect Snapport { get; }

    public PhysicalOffset MinPosition { get; }

    public PhysicalOffset MaxPosition { get; }

    /// <summary>The largest distance to a snap position that is snapped to, in each axis (unlimited for <c>mandatory</c>).</summary>
    public PhysicalOffset ProximityRange { get; }

    public bool HasHorizontalWritingMode { get; }

    public List<SnapArea> Areas => _areas;

    /// <summary>The snap targets the container is snapped to in each axis (only the ones whose snap area still exists).</summary>
    public SnapTarget? TargetX { get; set; }

    public SnapTarget? TargetY { get; set; }

    public bool SnapsOnX => Type.Axis is ScrollSnapAxis.X or ScrollSnapAxis.Both or ScrollSnapAxis.Pair;

    public bool SnapsOnY => Type.Axis is ScrollSnapAxis.Y or ScrollSnapAxis.Both or ScrollSnapAxis.Pair;

    private bool Is2d => Type.Axis is ScrollSnapAxis.Both or ScrollSnapAxis.Pair;

    private SnapRange GetScrollRange(SnapSearchAxis axis) => axis == SnapSearchAxis.X ? new(MinPosition.X, MaxPosition.X) : new(MinPosition.Y, MaxPosition.Y);

    private static double Get(PhysicalOffset point, SnapSearchAxis axis) => axis == SnapSearchAxis.X ? point.X : point.Y;

    private static SnapSearchAxis Cross(SnapSearchAxis axis) => axis == SnapSearchAxis.X ? SnapSearchAxis.Y : SnapSearchAxis.X;

    private static SnapRange GetRange(PhysicalRect rect, SnapSearchAxis axis) => axis == SnapSearchAxis.X ? new(rect.Left, rect.Right) : new(rect.Top, rect.Bottom);

    private bool IsValidSnapArea(SnapSearchAxis axis, SnapSelectionStrategy strategy, SnapArea area)
    {
        if (!strategy.IsValidSnapArea(axis, area))
            return false;

        // Under scroll-snap-type: pair, a snap area must define an alignment on both axes
        return Type.Axis != ScrollSnapAxis.Pair || area.HasDualAxisAlignment;
    }

    /// <summary>Updates the focus and target states of the snap areas, which prefer them (https://drafts.csswg.org/css-scroll-snap-1/#multiple-aligned-snap-areas).</summary>
    public void UpdateAreaStates(Element? focused, Element? targeted)
    {
        foreach (var area in _areas)
        {
            var element = area.Target.Element;
            var isElement = area.Target.PseudoElement is null;
            area.HasFocusWithin = isElement && focused is not null && element.IsShadowIncludingInclusiveAncestorOf(focused);
            area.IsTargeted = isElement && targeted is not null && element.IsShadowIncludingInclusiveAncestorOf(targeted);
        }
    }

    /// <summary>Selects a snap position for a scroll operation (https://drafts.csswg.org/css-scroll-snap-1/#choosing).</summary>
    public SnapPosition FindSnapPosition(SnapSelectionStrategy strategy)
    {
        var basePosition = strategy.BasePosition;
        var shouldSnapOnX = strategy.ShouldSnapOnX && SnapsOnX;
        var shouldSnapOnY = strategy.ShouldSnapOnY && SnapsOnY;
        if (Type.IsNone || (!shouldSnapOnX && !shouldSnapOnY))
            return new SnapPosition(Found: false, strategy.IntendedPosition, TargetX, TargetY);

        var prioritizeX = strategy.ShouldPrioritizeSnapTargets && TargetX is not null;
        var prioritizeY = strategy.ShouldPrioritizeSnapTargets && TargetY is not null;

        SnapSearchResult? selectedX = null;
        SnapSearchResult? selectedY = null;
        if (shouldSnapOnX)
        {
            // The search starts from the current position in the cross axis, clamped to the scroll range
            var initialY = new SnapSearchResult
            {
                SnapOffset = Math.Clamp(basePosition.Y, MinPosition.Y, MaxPosition.Y),
                Axis = SnapSearchAxis.Y,
                SnapportVisibleRange = new(Snapport.Left, Snapport.Right),
                CrossAxisScrollRange = GetScrollRange(SnapSearchAxis.X),
            };
            if (prioritizeX)
            {
                selectedX = GetTargetSnapAreaSearchResult(strategy, SnapSearchAxis.X, initialY);
            }

            selectedX ??= FindClosestValidArea(SnapSearchAxis.X, strategy, initialY);
        }

        if (shouldSnapOnY)
        {
            var initialX = new SnapSearchResult
            {
                SnapOffset = Math.Clamp(basePosition.X, MinPosition.X, MaxPosition.X),
                Axis = SnapSearchAxis.X,
                SnapportVisibleRange = new(Snapport.Top, Snapport.Bottom),
                CrossAxisScrollRange = GetScrollRange(SnapSearchAxis.Y),
            };
            if (prioritizeY)
            {
                selectedY = GetTargetSnapAreaSearchResult(strategy, SnapSearchAxis.Y, initialX);
            }

            selectedY ??= FindClosestValidArea(SnapSearchAxis.Y, strategy, initialX);
        }

        if (selectedX is null && selectedY is null)
        {
            // Searching along each axis separately can miss valid snap positions when both axes snap and the snap areas are
            // outside of the snapport
            if (shouldSnapOnX && shouldSnapOnY && !strategy.ShouldRespectSnapStop && FindSnapPositionForMutualSnap(strategy) is { } mutual)
                return mutual;

            // Not snapped anymore in the axes that were searched
            return new SnapPosition(Found: false, strategy.IntendedPosition, shouldSnapOnX ? null : TargetX, shouldSnapOnY ? null : TargetY);
        }

        if (Type.Axis == ScrollSnapAxis.Pair)
        {
            // When only one axis was scrolled, the other axis follows the snap area selected in the scrolled axis
            selectedX ??= GetSnapSearchResult(SnapSearchAxis.X, selectedY!.Area!);
            selectedY ??= GetSnapSearchResult(SnapSearchAxis.Y, selectedX.Area!);

            // Both axes selected different snap areas: a single one is chosen
            if (!ReferenceEquals(selectedX.Area, selectedY.Area))
            {
                var crossOfX = GetSnapSearchResult(SnapSearchAxis.Y, selectedX.Area!);
                var crossOfY = GetSnapSearchResult(SnapSearchAxis.X, selectedY.Area!);
                var fromX = new PhysicalOffset(selectedX.SnapOffset, crossOfX.SnapOffset);
                var fromY = new PhysicalOffset(crossOfY.SnapOffset, selectedY.SnapOffset);
                var comparison = strategy.Compare2dCandidates(fromX, fromY);

                // When all else is equal, the candidate in the block direction is preferred
                if (comparison > 0 || (comparison == 0 && HasHorizontalWritingMode))
                {
                    selectedX = crossOfY;
                }
                else
                {
                    selectedY = crossOfX;
                }
            }
        }

        // When both axes snap, the snap areas must be mutually visible: when one axis found nothing (its snap areas are outside
        // of the snapport in the cross axis), or when they are not mutually visible, the search is done again in one axis with
        // the result of the other axis as the cross axis constraint
        if (shouldSnapOnX && shouldSnapOnY && (selectedX is null || selectedY is null || !IsMutualVisible(selectedX, selectedY)))
        {
            var followX = true;
            if (selectedX is not null && selectedY is not null)
            {
                followX = SelectAxisToFollowForMutualVisibility(strategy, selectedX, selectedY) == SnapSearchAxis.X;
            }
            else if (selectedY is not null)
            {
                followX = false;
            }

            if (followX)
            {
                selectedY = FindClosestValidArea(SnapSearchAxis.Y, strategy, selectedX!);
            }
            else
            {
                selectedX = FindClosestValidArea(SnapSearchAxis.X, strategy, selectedY!);
            }
        }

        // For each axis, the alternative is a better selection when it is also aligned in the cross axis
        if (selectedY is { Alternative: not null })
        {
            SelectAlternative(selectedY, selectedX, strategy.CurrentPosition.X, MinPosition.X, MaxPosition.X);
        }

        if (selectedX is { Alternative: not null })
        {
            SelectAlternative(selectedX, selectedY, strategy.CurrentPosition.Y, MinPosition.Y, MaxPosition.Y);
        }

        var position = strategy.CurrentPosition;
        var targetX = TargetX;
        var targetY = TargetY;
        if (selectedX is not null)
        {
            position = new PhysicalOffset(selectedX.SnapOffset, position.Y);
            targetX = selectedX.Area?.Target;
        }

        if (selectedY is not null)
        {
            position = new PhysicalOffset(position.X, selectedY.SnapOffset);
            targetY = selectedY.Area?.Target;
        }

        return new SnapPosition(Found: true, position, targetX, targetY);
    }

    /// <summary>
    /// Finds a snap area aligned in both axes, closest to the corridor of the snapport (block axis first): called only when the
    /// search in each axis found nothing.
    /// </summary>
    private SnapPosition? FindSnapPositionForMutualSnap(SnapSelectionStrategy strategy)
    {
        SnapPosition? result = null;
        var smallestX = double.MaxValue;
        var smallestY = double.MaxValue;
        foreach (var area in _areas)
        {
            if (!strategy.IsValidSnapArea(SnapSearchAxis.X, area) || !strategy.IsValidSnapArea(SnapSearchAxis.Y, area))
                continue;

            var x = GetSnapSearchResult(SnapSearchAxis.X, area);
            var dx = x.SnapOffset - strategy.CurrentPosition.X;
            if (Math.Abs(dx) > ProximityRange.X)
                continue;

            var y = GetSnapSearchResult(SnapSearchAxis.Y, area);
            var dy = y.SnapOffset - strategy.CurrentPosition.Y;
            if (Math.Abs(dy) > ProximityRange.Y)
                continue;

            // The distance from the corridor of the snapport: the block scrolling distance first, then the inline one
            var distanceX = dx < 0 ? -dx : dx > Snapport.Width ? dx - Snapport.Width : 0;
            var distanceY = dy < 0 ? -dy : dy > Snapport.Height ? dy - Snapport.Height : 0;
            if (distanceY < smallestY || (distanceY == smallestY && distanceX < smallestX))
            {
                smallestX = distanceX;
                smallestY = distanceY;
                result = new SnapPosition(Found: true, new PhysicalOffset(x.SnapOffset, y.SnapOffset), area.Target, area.Target);
            }
        }

        return result;
    }

    /// <summary>Returns the snap position of the current snap target in an axis (following it after a layout change), or null.</summary>
    private SnapSearchResult? GetTargetSnapAreaSearchResult(SnapSelectionStrategy strategy, SnapSearchAxis axis, SnapSearchResult crossAxisResult)
    {
        var target = axis == SnapSearchAxis.X ? TargetX : TargetY;
        if (target is null)
            return null;

        foreach (var area in _areas)
        {
            if (area.Target != target.Value || !IsValidSnapArea(axis, strategy, area))
                continue;

            var aligned = GetSnapSearchResult(axis, area);
            if (CanCoverSnapportOnAxis(axis, area.Rect))
            {
                // A snap area larger than the snapport: the snap areas nested in it (themselves possibly larger than the snapport)
                // are considered
                return FindClosestValidAreaInternal(axis, strategy, crossAxisResult, considerCovering: true, GetRange(area.Rect, axis)) ?? aligned;
            }

            return aligned;
        }

        return null;
    }

    private SnapSearchResult? FindClosestValidArea(SnapSearchAxis axis, SnapSelectionStrategy strategy, SnapSearchResult crossAxisResult)
    {
        var result = FindClosestValidAreaInternal(axis, strategy, crossAxisResult, considerCovering: true, activeElementRange: null);

        // A relative scroll does not pass over a snap area with scroll-snap-stop: always (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-stop):
        // the first one between the current position and the result is chosen
        if (result is not null && strategy.ShouldRespectSnapStop)
        {
            var mustOnly = SnapSelectionStrategy.ForMustSnapInDirection(strategy.DirectionOrigin, strategy.IntendedPosition - strategy.DirectionOrigin);
            var mustResult = FindClosestValidAreaInternal(axis, mustOnly, crossAxisResult, considerCovering: false, activeElementRange: null);
            if (mustResult is not null)
            {
                var reference = Get(strategy.DirectionOrigin, axis);
                result = Math.Abs(result.SnapOffset - reference) < Math.Abs(mustResult.SnapOffset - reference) ? result : mustResult;
            }
        }

        // A relative scroll that finds no snap position ahead of it snaps to the closest one in any direction, so that a mandatory
        // snap container is snapped
        if (result is not null || Type.Strictness == ScrollSnapStrictness.Proximity || !strategy.HasIntendedDirection)
            return result;

        var relaxed = SnapSelectionStrategy.ForEndPosition(strategy.CurrentPosition, strategy.ShouldSnapOnX, strategy.ShouldSnapOnY);
        return FindClosestValidAreaInternal(axis, relaxed, crossAxisResult, considerCovering: true, activeElementRange: null);
    }

    private SnapSearchResult? FindClosestValidAreaInternal(SnapSearchAxis axis, SnapSelectionStrategy strategy, SnapSearchResult crossAxisResult, bool considerCovering, SnapRange? activeElementRange)
    {
        // The search result of the snap area closest to the base position
        SnapSearchResult? closest = null;

        // The search result at the intended position when it makes a snap area cover the snapport
        SnapSearchResult? coveringIntended = null;

        var intendedPosition = Get(strategy.IntendedPosition, axis);
        var basePosition = Get(strategy.BasePosition, axis);
        var preferredCandidate = false;
        var proximityDistance = Get(ProximityRange, axis);
        var smallestDistance = proximityDistance;

        for (var index = 0; index < _areas.Count; index++)
        {
            var area = _areas[index];
            if (!IsValidSnapArea(axis, strategy, area))
                continue;

            if (activeElementRange is { } range && !range.Intersects(GetRange(area.Rect, axis)))
                continue;

            var candidate = GetSnapSearchResult(axis, area);
            Evaluate(candidate, area, index);
            if (considerCovering && CanCoverSnapportOnAxis(axis, area.Rect) && FindCoveringCandidate(area, axis, candidate, intendedPosition) is { } covering)
            {
                covering.Rect = area.Rect;
                if (covering.SnapOffset == intendedPosition)
                {
                    if (coveringIntended is null)
                    {
                        coveringIntended = covering;
                    }
                    else
                    {
                        coveringIntended.Union(covering);
                        if (covering.HasFocusWithin)
                        {
                            coveringIntended.Area = covering.Area;
                        }
                    }
                }
                else
                {
                    // A covering candidate displaced from the intended position competes on distance with the aligned positions
                    Evaluate(covering, area, index);
                }
            }
        }

        return strategy.PickBestResult(closest, coveringIntended);

        void Evaluate(SnapSearchResult candidate, SnapArea area, int index)
        {
            if (!IsMutualVisible(candidate, crossAxisResult) || !strategy.IsValidSnapPosition(axis, candidate.SnapOffset))
                return;

            var distance = Math.Abs(candidate.SnapOffset - basePosition);
            if (distance > proximityDistance)
                return;

            var isPreferred = strategy.IsPreferredSnapPosition(axis, candidate.SnapOffset);
            if (preferredCandidate && !isPreferred)
                return;

            if (distance > smallestDistance && (preferredCandidate || !isPreferred))
                return;

            // The aligned snap areas that have the focus are preferred
            if (distance < smallestDistance || (isPreferred && (!preferredCandidate || candidate.HasFocusWithin)))
            {
                smallestDistance = distance;
                closest = candidate;
                preferredCandidate = isPreferred;
            }
            else if (closest is not null && !closest.HasFocusWithin)
            {
                // Then the targeted snap areas (https://drafts.csswg.org/css-scroll-snap-1/#multiple-aligned-snap-areas)
                if (closest.Area is { IsTargeted: true })
                    return;

                if (area.IsTargeted)
                {
                    closest = candidate;
                    preferredCandidate = isPreferred;
                    return;
                }

                // The first aligned snap area in tree order wins, except that an ancestor is removed when its descendant is aligned at
                // the same position
                if (candidate.SnapOffset == closest.SnapOffset && IsDescendantSnapArea(index, closest.Area!))
                {
                    smallestDistance = distance;
                    closest = candidate;
                    preferredCandidate = isPreferred;
                }
                else if (Is2d && area.HasDualAxisAlignment && isPreferred == preferredCandidate)
                {
                    // An equally aligned snap area that can be snapped to in both axes is a potential alternative
                    UpdateSearchAlternative(closest, area, index, strategy);
                }
            }
        }
    }

    private bool IsDescendantSnapArea(int descendantIndex, SnapArea ancestor)
    {
        var parentOffset = _areas[descendantIndex].ParentOffset;
        while (parentOffset > 0)
        {
            descendantIndex -= parentOffset;
            var parent = _areas[descendantIndex];
            if (ReferenceEquals(parent, ancestor))
                return true;

            parentOffset = parent.ParentOffset;
        }

        return false;
    }

    /// <summary>Returns the snap position of a snap area in an axis (https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-align), clamped to the scroll range (https://drafts.csswg.org/css-scroll-snap-1/#unreachable).</summary>
    private SnapSearchResult GetSnapSearchResult(SnapSearchAxis axis, SnapArea area)
    {
        double offset;
        SnapRange visibleRange;
        var rect = area.Rect;
        if (axis == SnapSearchAxis.X)
        {
            offset = area.AlignX switch
            {
                SnapAlignment.Start => rect.Left - Snapport.Left,
                SnapAlignment.Center => ((rect.Left + rect.Right) / 2) - ((Snapport.Left + Snapport.Right) / 2),
                _ => rect.Right - Snapport.Right,
            };
            offset = Math.Clamp(offset, MinPosition.X, MaxPosition.X);
            visibleRange = new SnapRange(Snapport.Top, Snapport.Bottom);
        }
        else
        {
            offset = area.AlignY switch
            {
                SnapAlignment.Start => rect.Top - Snapport.Top,
                SnapAlignment.Center => ((rect.Top + rect.Bottom) / 2) - ((Snapport.Top + Snapport.Bottom) / 2),
                _ => rect.Bottom - Snapport.Bottom,
            };
            offset = Math.Clamp(offset, MinPosition.Y, MaxPosition.Y);
            visibleRange = new SnapRange(Snapport.Left, Snapport.Right);
        }

        return new SnapSearchResult
        {
            SnapOffset = offset,
            Axis = axis,
            SnapportVisibleRange = visibleRange,
            CrossAxisScrollRange = GetScrollRange(Cross(axis)),
            Rect = rect,
            Area = area,
        };
    }

    private bool CanCoverSnapportOnAxis(SnapSearchAxis axis, PhysicalRect areaRect)
        => axis == SnapSearchAxis.X ? areaRect.Width >= Snapport.Width : areaRect.Height >= Snapport.Height;

    /// <summary>
    /// Returns the snap position closest to the intended position at which a snap area larger than the snapport covers it
    /// (https://drafts.csswg.org/css-scroll-snap-1/#snap-overflow), avoiding the other snap areas that intersect it (the
    /// distance between the previous and the next snap positions must be larger than the snapport), or null.
    /// </summary>
    private SnapSearchResult? FindCoveringCandidate(SnapArea area, SnapSearchAxis axis, SnapSearchResult alignedCandidate, double intendedPosition)
    {
        var horizontal = axis == SnapSearchAxis.X;
        var scrollPadding = horizontal ? Snapport.Left : Snapport.Top;
        var snapportSize = horizontal ? Snapport.Width : Snapport.Height;
        var alignment = horizontal ? area.AlignX : area.AlignY;
        var areaRange = GetRange(area.Rect, axis);
        var preferredSnapport = new SnapRange(intendedPosition + scrollPadding, intendedPosition + scrollPadding + snapportSize);

        var backward = areaRange;
        var middle = areaRange;
        var forward = areaRange;
        foreach (var intruder in _areas)
        {
            var intruderRange = GetRange(intruder.Rect, axis);

            // An area outside of the area, or a superset of it, does not intrude
            if (intruderRange.Start > areaRange.End || intruderRange.End < areaRange.Start)
                continue;

            if (intruderRange.Start <= areaRange.Start && intruderRange.End >= areaRange.End)
                continue;

            // The range before the intruders that intersect the snapport
            if (intruderRange.End < preferredSnapport.Start)
            {
                backward = backward with { Start = Math.Max(backward.Start, intruderRange.End) };
            }
            else
            {
                backward = backward with { End = Math.Min(backward.End, intruderRange.Start) };
            }

            // The range after the intruders that intersect the snapport
            if (intruderRange.Start > preferredSnapport.End)
            {
                forward = forward with { End = Math.Min(forward.End, intruderRange.Start) };
            }
            else
            {
                forward = forward with { Start = Math.Max(forward.Start, intruderRange.End) };
            }

            // The range inside the snapport, between the intruders before and after it
            if (intruderRange.Contains(preferredSnapport) || preferredSnapport.Contains(intruderRange))
            {
                middle = default;
            }
            else if (intruderRange.Start <= preferredSnapport.Start)
            {
                middle = middle with { Start = Math.Max(middle.Start, intruderRange.End) };
            }
            else
            {
                middle = middle with { End = Math.Min(middle.End, intruderRange.Start) };
            }
        }

        if (GetResultForDodgingRange(areaRange, middle, alignedCandidate, intendedPosition, scrollPadding, snapportSize, alignment) is { } middleCandidate)
            return middleCandidate;

        var backwardCandidate = GetResultForDodgingRange(areaRange, backward, alignedCandidate, intendedPosition, scrollPadding, snapportSize, alignment);
        var forwardCandidate = GetResultForDodgingRange(areaRange, forward, alignedCandidate, intendedPosition, scrollPadding, snapportSize, alignment);
        if (backwardCandidate is null)
            return forwardCandidate;

        if (forwardCandidate is null)
            return backwardCandidate;

        return Math.Abs(backwardCandidate.SnapOffset - intendedPosition) < Math.Abs(forwardCandidate.SnapOffset - intendedPosition) ? backwardCandidate : forwardCandidate;
    }

    private static SnapSearchResult? GetResultForDodgingRange(SnapRange areaRange, SnapRange dodgingRange, SnapSearchResult alignedCandidate, double preferredOffset, double scrollPadding, double snapportSize, SnapAlignment alignment)
    {
        if (dodgingRange.IsEmpty || dodgingRange.IsReversed)
            return null;

        var result = alignedCandidate.Clone();
        var minOffset = dodgingRange.Start - scrollPadding;
        var maxOffset = dodgingRange.End - scrollPadding - snapportSize;
        if (maxOffset > minOffset)
        {
            result.SnapOffset = Math.Clamp(preferredOffset, minOffset, maxOffset);
            result.CoveredRange = new SnapRange(minOffset, maxOffset);
            return result;
        }

        // The snapport does not fit in the dodging range: the alignment of the snap area chooses a position, so that the contents
        // in the dodging range are reachable
        var offset = alignment switch
        {
            SnapAlignment.Start => minOffset,
            SnapAlignment.Center => (minOffset + maxOffset) / 2,
            _ => maxOffset,
        };
        minOffset = areaRange.Start - scrollPadding;
        maxOffset = areaRange.End - scrollPadding - snapportSize;
        if (maxOffset < minOffset)
            return null;

        result.SnapOffset = Math.Clamp(offset, minOffset, maxOffset);
        return result;
    }

    /// <summary>Whether a scroll position makes a snap area cover the snapport in an axis.</summary>
    private bool IsSnapportCoveredOnAxis(SnapSearchAxis axis, double position, PhysicalRect areaRect)
    {
        if (axis == SnapSearchAxis.X)
        {
            if (areaRect.Width < Snapport.Width)
                return false;

            return position >= areaRect.Left - Snapport.Left - SnapportCoveredTolerance && position <= areaRect.Right - Snapport.Right + SnapportCoveredTolerance;
        }

        if (areaRect.Height < Snapport.Height)
            return false;

        return position >= areaRect.Top - Snapport.Top - SnapportCoveredTolerance && position <= areaRect.Bottom - Snapport.Bottom + SnapportCoveredTolerance;
    }

    /// <summary>Whether the container is snapped to a snap area at a scroll position, in either axis (both for <c>pair</c>).</summary>
    public bool IsSnappedToArea(SnapArea area, PhysicalOffset position, out bool snappedX, out bool snappedY)
    {
        var coveredX = IsSnapportCoveredOnAxis(SnapSearchAxis.X, position.X, area.Rect);
        var coveredY = IsSnapportCoveredOnAxis(SnapSearchAxis.Y, position.Y, area.Rect);
        snappedX = false;
        snappedY = false;
        if (SnapsOnX && area.AlignX != SnapAlignment.None)
        {
            var result = GetSnapSearchResult(SnapSearchAxis.X, area);
            snappedX = (Math.Abs(result.SnapOffset - position.X) <= SnappedToTolerance || coveredX) && result.VisibleRange.Contains(position.Y);
        }

        if (SnapsOnY && area.AlignY != SnapAlignment.None)
        {
            var result = GetSnapSearchResult(SnapSearchAxis.Y, area);
            snappedY = (Math.Abs(result.SnapOffset - position.Y) <= SnappedToTolerance || coveredY) && result.VisibleRange.Contains(position.X);
        }

        return Type.Axis == ScrollSnapAxis.Pair ? snappedX && snappedY : snappedX || snappedY;
    }

    private static bool IsMutualVisible(SnapSearchResult a, SnapSearchResult b)
        => new SnapRange(b.SnapOffset, b.SnapOffset).IsBoundedBy(a.VisibleRange) && new SnapRange(a.SnapOffset, a.SnapOffset).IsBoundedBy(b.VisibleRange);

    private void UpdateSearchAlternative(SnapSearchResult current, SnapArea candidateArea, int candidateIndex, SnapSelectionStrategy strategy)
    {
        var currentArea = current.Area!;
        var crossAxis = Cross(current.Axis);

        // The current result is an alternative when it snaps in both axes (a later candidate may be a better alternative when it is
        // closer in the cross axis)
        if (current.Alternative is null && currentArea.HasDualAxisAlignment)
        {
            current.Alternative = (currentArea, GetSnapSearchResult(crossAxis, currentArea).SnapOffset);
        }

        var candidateCrossOffset = GetSnapSearchResult(crossAxis, candidateArea).SnapOffset;
        if (current.Alternative is { } alternative)
        {
            var crossBase = Get(strategy.BasePosition, crossAxis);
            var candidateDistance = Math.Abs(crossBase - candidateCrossOffset);
            var alternativeDistance = Math.Abs(crossBase - alternative.CrossAxisSnapOffset);
            if (candidateDistance > alternativeDistance)
                return;

            // A candidate as close as the alternative wins only when it is its descendant
            if (candidateDistance == alternativeDistance && !IsDescendantSnapArea(candidateIndex, alternative.Area))
                return;
        }

        current.Alternative = (candidateArea, candidateCrossOffset);
    }

    private static void SelectAlternative(SnapSearchResult selection, SnapSearchResult? crossSelection, double crossCurrentPosition, double crossMin, double crossMax)
    {
        var alternative = selection.Alternative!.Value;
        var crossOffset = crossSelection?.SnapOffset ?? Math.Clamp(crossCurrentPosition, crossMin, crossMax);
        if (Math.Abs(crossOffset - alternative.CrossAxisSnapOffset) <= SnappedToTolerance)
        {
            selection.Area = alternative.Area;
        }
    }

    /// <summary>
    /// Chooses the axis whose snap area is kept when the snap areas selected in both axes are not mutually visible
    /// (https://drafts.csswg.org/css-scroll-snap-1/#snap-scope): after a layout change, the axis of the focused snap area, then
    /// of the targeted one, then the block axis (https://drafts.csswg.org/css-scroll-snap-1/#re-snap); else the closest one.
    /// </summary>
    private SnapSearchAxis SelectAxisToFollowForMutualVisibility(SnapSelectionStrategy strategy, SnapSearchResult selectedX, SnapSearchResult selectedY)
    {
        if (strategy.ShouldPrioritizeSnapTargets)
        {
            // Snapped in one axis only before the change: that axis is followed
            if (TargetX is null)
                return SnapSearchAxis.Y;

            if (TargetY is null)
                return SnapSearchAxis.X;

            if (selectedX.HasFocusWithin)
                return SnapSearchAxis.X;

            if (selectedY.HasFocusWithin)
                return SnapSearchAxis.Y;

            if (selectedX.Area is { IsTargeted: true })
                return SnapSearchAxis.X;

            if (selectedY.Area is { IsTargeted: true })
                return SnapSearchAxis.Y;

            return HasHorizontalWritingMode ? SnapSearchAxis.Y : SnapSearchAxis.X;
        }

        return Math.Abs(selectedX.SnapOffset - strategy.BasePosition.X) <= Math.Abs(selectedY.SnapOffset - strategy.BasePosition.Y) ? SnapSearchAxis.X : SnapSearchAxis.Y;
    }
}
