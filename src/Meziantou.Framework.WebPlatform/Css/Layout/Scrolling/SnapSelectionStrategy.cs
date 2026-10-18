namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A physical axis searched for a snap position.</summary>
internal enum SnapSearchAxis : byte
{
    X,
    Y,
}

/// <summary>
/// The intent of a scroll operation, which selects the snap positions (https://drafts.csswg.org/css-scroll-snap-1/#scroll-types,
/// https://drafts.csswg.org/css-scroll-snap-1/#choosing): the selection algorithm is modeled on the one of Blink
/// (<c>cc/input/snap_selection_strategy.cc</c>), which the tests of the snapping behaviors describe.
/// </summary>
internal abstract class SnapSelectionStrategy
{
    private protected SnapSelectionStrategy(PhysicalOffset currentPosition)
    {
        CurrentPosition = currentPosition;
    }

    /// <summary>The scroll position before the scroll operation.</summary>
    public PhysicalOffset CurrentPosition { get; }

    /// <summary>The position the direction of the scroll operation starts from: <see cref="CurrentPosition"/>, or the start of a gesture of the user.</summary>
    public virtual PhysicalOffset DirectionOrigin => CurrentPosition;

    /// <summary>The position the scroll operation intends to reach without snapping (its natural end-point).</summary>
    public abstract PhysicalOffset IntendedPosition { get; }

    /// <summary>The position from which the closest snap position is searched.</summary>
    public abstract PhysicalOffset BasePosition { get; }

    public abstract bool ShouldSnapOnX { get; }

    public abstract bool ShouldSnapOnY { get; }

    /// <summary>Whether the scroll has an intended direction (a relative scroll).</summary>
    public virtual bool HasIntendedDirection => true;

    /// <summary>Whether the snap positions of <c>scroll-snap-stop: always</c> cannot be passed over (a relative scroll).</summary>
    public virtual bool ShouldRespectSnapStop => false;

    /// <summary>Whether the current snap targets are followed (a re-snap after a layout change).</summary>
    public virtual bool ShouldPrioritizeSnapTargets => false;

    /// <summary>Returns a strategy for an absolute scroll to a position (https://drafts.csswg.org/css-scroll-snap-1/#absolute-scroll), snapping the axes that are scrolled.</summary>
    public static SnapSelectionStrategy ForEndPosition(PhysicalOffset position, bool scrolledX, bool scrolledY) => new EndPositionStrategy(position, scrolledX, scrolledY, prioritizeSnapTargets: false);

    /// <summary>
    /// Returns a strategy for a stationary scroll (https://drafts.csswg.org/css-scroll-snap-1/#stationary-scroll) that keeps the
    /// current snap targets snapped (https://drafts.csswg.org/css-scroll-snap-1/#re-snap).
    /// </summary>
    public static SnapSelectionStrategy ForTargetElement(PhysicalOffset position) => new EndPositionStrategy(position, scrolledX: true, scrolledY: true, prioritizeSnapTargets: true);

    /// <summary>
    /// Returns a strategy for a relative scroll with an intended end position and direction
    /// (https://drafts.csswg.org/css-scroll-snap-1/#relative-scroll): the snap positions ahead in the direction of the scroll.
    /// </summary>
    public static SnapSelectionStrategy ForDisplacement(PhysicalOffset position, PhysicalOffset displacement)
        => new DirectionStrategy(position, position, displacement, preferDistance: true, mustSnapOnly: false, includeOrigin: false);

    /// <summary>
    /// Returns a strategy for the end of a scroll gesture of the user (the wheel, a panning touch, a drag of the thumb of a
    /// scrollbar) that started at a position and ended at another one: the snap position closest to its end among its start
    /// position and the ones beyond it in the direction of the gesture (as Blink's <c>EndAndDirectionStrategy</c>: a short
    /// gesture snaps back to where it started). The axes that do not snap stay at the end position.
    /// </summary>
    public static SnapSelectionStrategy ForEndAndDirection(PhysicalOffset start, PhysicalOffset end)
        => new DirectionStrategy(end, start, end - start, preferDistance: true, mustSnapOnly: false, includeOrigin: true);

    /// <summary>Returns a strategy that only considers the snap areas with <c>scroll-snap-stop: always</c> in the direction of a scroll.</summary>
    public static SnapSelectionStrategy ForMustSnapInDirection(PhysicalOffset position, PhysicalOffset step)
        => new DirectionStrategy(position, position, step, preferDistance: false, mustSnapOnly: true, includeOrigin: false);

    /// <summary>Whether a snap area can be snapped to in an axis.</summary>
    public virtual bool IsValidSnapArea(SnapSearchAxis axis, SnapArea area) => (axis == SnapSearchAxis.X ? area.AlignX : area.AlignY) != SnapAlignment.None;

    /// <summary>Whether a snap position is valid for the scroll.</summary>
    public abstract bool IsValidSnapPosition(SnapSearchAxis axis, double position);

    /// <summary>Whether a snap position is preferred over the others.</summary>
    public virtual bool IsPreferredSnapPosition(SnapSearchAxis axis, double position) => true;

    /// <summary>Chooses between the closest aligned snap position and the intended position when it makes a snap area cover the snapport.</summary>
    public abstract SnapSearchResult? PickBestResult(SnapSearchResult? closest, SnapSearchResult? covering);

    /// <summary>Compares two 2D snap positions: negative when the first one is better.</summary>
    public virtual int Compare2dCandidates(PhysicalOffset a, PhysicalOffset b)
    {
        var distanceA = GetLengthSquared(a - BasePosition);
        var distanceB = GetLengthSquared(b - BasePosition);
        return distanceA.CompareTo(distanceB);
    }

    private protected static double GetLengthSquared(PhysicalOffset vector) => (vector.X * vector.X) + (vector.Y * vector.Y);

    private protected static double Get(PhysicalOffset point, SnapSearchAxis axis) => axis == SnapSearchAxis.X ? point.X : point.Y;

    /// <summary>
    /// Returns a strategy that only snaps to the snap areas of an element (not of its pseudo-elements): a scroll operation intended to
    /// scroll to the element (https://drafts.csswg.org/css-scroll-snap-1/#choosing: "the user agent must snap to one of that
    /// element's snap positions").
    /// </summary>
    public static SnapSelectionStrategy ForElementAreas(SnapSelectionStrategy strategy, Dom.Element element) => new ElementAreasStrategy(strategy, element);

    /// <summary>A strategy restricted to the snap areas of an element (see <see cref="ForElementAreas"/>).</summary>
    private sealed class ElementAreasStrategy(SnapSelectionStrategy inner, Dom.Element element) : SnapSelectionStrategy(inner.CurrentPosition)
    {
        public override PhysicalOffset DirectionOrigin => inner.DirectionOrigin;

        public override PhysicalOffset IntendedPosition => inner.IntendedPosition;

        public override PhysicalOffset BasePosition => inner.BasePosition;

        public override bool ShouldSnapOnX => inner.ShouldSnapOnX;

        public override bool ShouldSnapOnY => inner.ShouldSnapOnY;

        public override bool HasIntendedDirection => inner.HasIntendedDirection;

        public override bool ShouldRespectSnapStop => inner.ShouldRespectSnapStop;

        public override bool ShouldPrioritizeSnapTargets => false;

        public override bool IsValidSnapArea(SnapSearchAxis axis, SnapArea area)
            => area.Target.PseudoElement is null && ReferenceEquals(area.Target.Element, element) && inner.IsValidSnapArea(axis, area);

        public override bool IsValidSnapPosition(SnapSearchAxis axis, double position) => inner.IsValidSnapPosition(axis, position);

        public override bool IsPreferredSnapPosition(SnapSearchAxis axis, double position) => inner.IsPreferredSnapPosition(axis, position);

        public override SnapSearchResult? PickBestResult(SnapSearchResult? closest, SnapSearchResult? covering) => inner.PickBestResult(closest, covering);

        public override int Compare2dCandidates(PhysicalOffset a, PhysicalOffset b) => inner.Compare2dCandidates(a, b);
    }

    /// <summary>An absolute or a stationary scroll: the closest snap position to the end position, in any direction.</summary>
    private sealed class EndPositionStrategy(PhysicalOffset position, bool scrolledX, bool scrolledY, bool prioritizeSnapTargets) : SnapSelectionStrategy(position)
    {
        public override PhysicalOffset IntendedPosition => CurrentPosition;

        public override PhysicalOffset BasePosition => CurrentPosition;

        public override bool ShouldSnapOnX => scrolledX;

        public override bool ShouldSnapOnY => scrolledY;

        public override bool HasIntendedDirection => false;

        public override bool ShouldPrioritizeSnapTargets => prioritizeSnapTargets;

        public override bool IsValidSnapPosition(SnapSearchAxis axis, double position) => axis == SnapSearchAxis.X ? scrolledX : scrolledY;

        // The intended position, when it makes a snap area cover the snapport, is the best position
        public override SnapSearchResult? PickBestResult(SnapSearchResult? closest, SnapSearchResult? covering) => covering ?? closest;
    }

    /// <summary>A relative scroll: the snap positions ahead in the direction of the scroll.</summary>
    private sealed class DirectionStrategy : SnapSelectionStrategy
    {
        private readonly PhysicalOffset _step;
        private readonly bool _preferDistance;
        private readonly bool _mustSnapOnly;
        private readonly bool _includeOrigin;
        private readonly PhysicalOffset _origin;

        /// <param name="position">The current position.</param>
        /// <param name="origin">The position the direction starts from (the current position, or the start of a gesture).</param>
        /// <param name="step">The displacement from <paramref name="origin"/>.</param>
        /// <param name="preferDistance">Whether the scroll has an intended end position (else only a direction).</param>
        /// <param name="mustSnapOnly">Whether only the snap areas with <c>scroll-snap-stop: always</c> are considered.</param>
        /// <param name="includeOrigin">Whether the snap positions at the origin are valid (the end of a gesture can snap back to its start).</param>
        public DirectionStrategy(PhysicalOffset position, PhysicalOffset origin, PhysicalOffset step, bool preferDistance, bool mustSnapOnly, bool includeOrigin)
            : base(position)
        {
            _origin = origin;
            _step = step;
            _preferDistance = preferDistance;
            _mustSnapOnly = mustSnapOnly;
            _includeOrigin = includeOrigin;
        }

        public override PhysicalOffset DirectionOrigin => _origin;

        public override PhysicalOffset IntendedPosition => _origin + _step;

        // A scroll with an intended end position searches from it, a scroll with only a direction searches from the current position
        public override PhysicalOffset BasePosition => _preferDistance ? _origin + _step : CurrentPosition;

        // The end of a gesture snaps both axes: an axis in which it ended where it started snaps back to its start
        public override bool ShouldSnapOnX => _step.X != 0 || _includeOrigin;

        public override bool ShouldSnapOnY => _step.Y != 0 || _includeOrigin;

        public override bool ShouldRespectSnapStop => true;

        public override bool IsValidSnapArea(SnapSearchAxis axis, SnapArea area) => base.IsValidSnapArea(axis, area) && (!_mustSnapOnly || area.MustSnap);

        // A scroll with an intended end position prefers any distance, a scroll with only a direction has no preferred distance
        public override bool IsPreferredSnapPosition(SnapSearchAxis axis, double position) => _preferDistance || position == Get(CurrentPosition, axis);

        public override bool IsValidSnapPosition(SnapSearchAxis axis, double position)
        {
            // The current snap position is not ahead (the end of a gesture can snap back to its start)
            var delta = position - Get(_origin, axis);
            var step = Get(_step, axis);
            if (_includeOrigin && delta == 0)
                return true;

            return (step > 0 && delta > 0) || (step < 0 && delta < 0);
        }

        public override SnapSearchResult? PickBestResult(SnapSearchResult? closest, SnapSearchResult? covering)
        {
            // The closest aligned position is chosen only when the intended position does not make a snap area cover the snapport,
            // or when it is before it in the direction of a scroll with only a direction
            if (closest is null)
                return covering;

            if (covering is null)
                return closest;

            // The same snap area: covering best preserves the intended position
            if (ReferenceEquals(covering.Area, closest.Area))
                return covering;

            if (!_preferDistance)
            {
                if ((_step.X > 0 || _step.Y > 0) && closest.SnapOffset < covering.SnapOffset)
                    return closest;

                if ((_step.X < 0 || _step.Y < 0) && closest.SnapOffset > covering.SnapOffset)
                    return closest;
            }

            return covering;
        }

        public override int Compare2dCandidates(PhysicalOffset a, PhysicalOffset b)
        {
            var vectorA = a - BasePosition;
            var vectorB = b - BasePosition;
            var lengthA = GetLengthSquared(vectorA);
            var lengthB = GetLengthSquared(vectorB);

            // The candidate closer to the direction of the scroll (higher cosine similarity) is better
            if (lengthA > 0 && lengthB > 0)
            {
                var cosA = ((_step.X * vectorA.X) + (_step.Y * vectorA.Y)) / Math.Sqrt(lengthA);
                var cosB = ((_step.X * vectorB.X) + (_step.Y * vectorB.Y)) / Math.Sqrt(lengthB);
                if (cosA != cosB)
                    return cosA > cosB ? -1 : 1;
            }

            return lengthA.CompareTo(lengthB);
        }
    }
}
