namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The geometry of the gaps of a grid container, for the painting of the gap decorations (https://drafts.csswg.org/css-gaps-1/)
internal sealed partial class GridLayoutAlgorithm
{
    /// <summary>
    /// Returns the gaps of the grid (https://drafts.csswg.org/css-gaps-1/#gap-grid: "the gutters between grid rows and grid
    /// columns"), or null when the container paints no gap decoration. The gutters of the collapsed tracks coincide: "collapsed
    /// gutters are treated as a single gap for decoration purposes" (https://drafts.csswg.org/css-gaps-1/#assigning). Each gap
    /// runs from the start to the end of the grid and intersects every gap of the other axis; the state of each of its
    /// portions (an item spans across it, the cells on its sides are empty) comes from the areas of the items.
    /// </summary>
    private GapGeometry? BuildGapGeometry()
    {
        if (!_style.HasGapRules)
            return null;

        var columns = GetVisibleTracks(_columns);
        var rows = GetVisibleTracks(_rows);
        if (columns.Length < 2 && rows.Length < 2)
            return null;

        var rowStates = ComputeGapStates(GridAxis.Rows, rows, columns);
        var columnStates = ComputeGapStates(GridAxis.Columns, columns, rows);
        var geometry = new GapGeometry
        {
            Kind = GapContainerKind.Grid,
            ColumnGaps = CreateGaps(GridAxis.Columns, columns, rows, columnStates, rowStates, _content.Offset.Inline, _content.Offset.Block),
            RowGaps = CreateGaps(GridAxis.Rows, rows, columns, rowStates, columnStates, _content.Offset.Block, _content.Offset.Inline),
        };
        return geometry.IsEmpty ? null : geometry;
    }

    /// <summary>
    /// Returns the difference between the gutters of a subgridded axis of a subgrid and the ones of its parent grid on each side
    /// of a gutter (the items get it as extra margins, https://drafts.csswg.org/css-grid-2/#subgrid-margins), else 0.
    /// </summary>
    private double GetSubgridGutterShift(GridAxis axis)
        => (axis == GridAxis.Columns ? _columnsFromParent : _rowsFromParent) && _input?.Get(axis) is { } input ? (GetOwnGap(axis, input) - input.ParentGap) / 2 : 0;

    /// <summary>Returns the indices of the tracks that are not collapsed.</summary>
    private static int[] GetVisibleTracks(GridTrack[] tracks)
    {
        var result = new List<int>(tracks.Length);
        for (var i = 0; i < tracks.Length; i++)
        {
            if (!tracks[i].IsCollapsed)
            {
                result.Add(i);
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Returns the states of the portions of the gaps of an axis: <c>[gap][cell]</c> is the state of the gap between the tracks
    /// <c>tracks[gap]</c> and <c>tracks[gap + 1]</c> along the track <c>crossTracks[cell]</c> of the other axis.
    /// </summary>
    private GapSegmentState[][] ComputeGapStates(GridAxis axis, int[] tracks, int[] crossTracks)
    {
        var gapCount = Math.Max(0, tracks.Length - 1);
        var states = new GapSegmentState[gapCount][];
        for (var gap = 0; gap < gapCount; gap++)
        {
            states[gap] = new GapSegmentState[crossTracks.Length];
        }

        if (gapCount == 0 || crossTracks.Length == 0)
            return states;

        // The end line (in the axis) of the item that extends the furthest among the items that cover each cell: 0 for an empty cell
        var crossAxis = axis == GridAxis.Columns ? GridAxis.Rows : GridAxis.Columns;
        var trackCount = GetTracks(axis).Length;
        var crossTrackCount = GetTracks(crossAxis).Length;
        var endLines = new int[trackCount][];
        for (var track = 0; track < trackCount; track++)
        {
            endLines[track] = new int[crossTrackCount];
        }

        foreach (var item in _items)
        {
            var end = Math.Min(item.GetEnd(axis), trackCount);
            var crossEnd = Math.Min(item.GetEnd(crossAxis), crossTrackCount);
            for (var track = Math.Max(0, item.GetStart(axis)); track < end; track++)
            {
                for (var cross = Math.Max(0, item.GetStart(crossAxis)); cross < crossEnd; cross++)
                {
                    endLines[track][cross] = Math.Max(endLines[track][cross], end);
                }
            }
        }

        for (var gap = 0; gap < gapCount; gap++)
        {
            var before = tracks[gap];
            var after = tracks[gap + 1];
            for (var cell = 0; cell < crossTracks.Length; cell++)
            {
                var cross = crossTracks[cell];
                var beforeEnd = endLines[before][cross];
                if (beforeEnd > before + 1)
                {
                    states[gap][cell] = GapSegmentState.Blocked;
                    continue;
                }

                var state = GapSegmentState.None;
                if (beforeEnd == 0)
                {
                    state |= GapSegmentState.EmptyBefore;
                }

                if (endLines[after][cross] == 0)
                {
                    state |= GapSegmentState.EmptyAfter;
                }

                states[gap][cell] = state;
            }
        }

        return states;
    }

    /// <summary>Creates the gaps between the tracks of an axis, which intersect the gaps between the tracks of the other axis.</summary>
    /// <param name="axis">The axis of the tracks the gaps separate.</param>
    /// <param name="tracks">The tracks of the axis that are not collapsed.</param>
    /// <param name="crossTracks">The tracks of the other axis that are not collapsed.</param>
    /// <param name="states">The states of the gaps of the axis (see <see cref="ComputeGapStates"/>).</param>
    /// <param name="crossStates">The states of the gaps of the other axis.</param>
    /// <param name="offset">The offset of the content box in the axis.</param>
    /// <param name="crossOffset">The offset of the content box in the other axis.</param>
    private GapLine[] CreateGaps(GridAxis axis, int[] tracks, int[] crossTracks, GapSegmentState[][] states, GapSegmentState[][] crossStates, double offset, double crossOffset)
    {
        if (tracks.Length < 2)
            return [];

        var gridTracks = GetTracks(axis);
        var crossAxis = axis == GridAxis.Columns ? GridAxis.Rows : GridAxis.Columns;
        var gridCrossTracks = GetTracks(crossAxis);

        // (the gutters of a subgrid are its own ones, centered in the gutters of the tracks of its parent)
        var shift = GetSubgridGutterShift(axis);
        var crossShift = GetSubgridGutterShift(crossAxis);
        var gaps = new GapLine[tracks.Length - 1];
        for (var gap = 0; gap < gaps.Length; gap++)
        {
            var before = gridTracks[tracks[gap]];
            var after = gridTracks[tracks[gap + 1]];
            GapIntersection[] intersections;
            if (crossTracks.Length == 0)
            {
                // No track in the other axis (a grid without row): the gap has no extent
                intersections = [GapIntersection.Edge(crossOffset), GapIntersection.Edge(crossOffset)];
            }
            else
            {
                intersections = new GapIntersection[crossTracks.Length + 1];
                intersections[0] = GapIntersection.Edge(crossOffset + gridCrossTracks[crossTracks[0]].Position, states[gap][0]);
                for (var cell = 1; cell < crossTracks.Length; cell++)
                {
                    var crossBefore = gridCrossTracks[crossTracks[cell - 1]];
                    var crossAfter = gridCrossTracks[crossTracks[cell]];
                    var intersection = GapIntersection.Junction(crossOffset + ((crossBefore.End + crossAfter.Position) / 2), Math.Max(0, crossAfter.Position - crossBefore.End + (2 * crossShift)), cell - 1, states[gap][cell]);
                    intersection.HasCrossStates = true;
                    intersection.CrossBefore = crossStates[cell - 1][gap];
                    intersection.CrossAfter = crossStates[cell - 1][gap + 1];
                    intersections[cell] = intersection;
                }

                intersections[^1] = GapIntersection.Edge(crossOffset + gridCrossTracks[crossTracks[^1]].End);
            }

            gaps[gap] = new GapLine
            {
                Center = offset + ((before.End + after.Position) / 2),
                Size = Math.Max(0, after.Position - before.End + (2 * shift)),
                Intersections = intersections,
                ValueIndex = gap,
                ValueCount = gaps.Length,
                IsMain = axis == GridAxis.Rows,
            };
        }

        return gaps;
    }

    /// <summary>
    /// Returns the gaps of a grid lanes container (https://drafts.csswg.org/css-gaps-1/#gap-grid-lanes), or null when it paints no
    /// gap decoration: "When applied to the grid axis [...] refers to the gutter between adjacent grid tracks. When applied to the
    /// stacking axis [...] refers to the gutter between items in each grid track". A gutter of the stacking axis is confined to
    /// its track, just before its item (https://drafts.csswg.org/css-grid-3/#gutters: the empty space left by the placement
    /// before a spanning item is not part of it; the gutter before a spanning item has one segment in each track it spans). The
    /// gutters of the grid axis run across the stacking axis and are abutted by the gutters of their two tracks; a portion of
    /// them is blocked when the same item is on its two sides.
    /// </summary>
    private GapGeometry? BuildLanesGapGeometry()
    {
        if (!_style.HasGapRules)
            return null;

        var tracks = GetTracks(_laneAxis);
        var lanes = GetVisibleTracks(tracks);
        if (lanes.Length == 0)
            return null;

        var lanesAreColumns = _laneAxis == GridAxis.Columns;
        var laneOffset = lanesAreColumns ? _content.Offset.Inline : _content.Offset.Block;
        var stackOffset = lanesAreColumns ? _content.Offset.Block : _content.Offset.Inline;
        var stackGap = GetGap(_stackAxis);
        var fillReverse = FillReverse;

        // The extent of the gutters of the grid axis: the content box (or the stacking range when it overflows); with an explicit
        // content alignment, the aligned stacking range (as in Blink)
        var stackTrack = GetTracks(_stackAxis)[0];
        var contentAlignment = lanesAreColumns ? _style.Alignment.AlignContent : _style.Alignment.JustifyContent;
        var (stackStart, stackEnd) = contentAlignment.Keyword == AlignmentKeyword.Normal
            ? (Math.Min(0, stackTrack.Position), Math.Max(_stackingContainerSize, stackTrack.End))
            : (stackTrack.Position, stackTrack.End);
        stackStart += stackOffset;
        stackEnd += stackOffset;

        // The items of each track from the start of the stacking axis, and the centers of the gutters before them (after them
        // with fill-reverse: the gutter is on the side of the item where the stacking comes from)
        var occupants = new List<GridItem>[lanes.Length];
        var gutters = new double[lanes.Length][];
        for (var lane = 0; lane < lanes.Length; lane++)
        {
            var track = lanes[lane];
            var items = new List<GridItem>();
            foreach (var item in _items)
            {
                if (item.GetStart(_laneAxis) <= track && track < item.GetEnd(_laneAxis))
                {
                    items.Add(item);
                }
            }

            // The order of the placement in the track (a stable sort: the items at the same position stay in their order)
            var ordered = items.Select((item, index) => (item, index)).OrderBy(e => e.item.LanesPosition).ThenBy(e => e.index).Select(e => e.item).ToList();
            var centers = new double[Math.Max(0, ordered.Count - 1)];
            for (var i = 1; i < ordered.Count; i++)
            {
                var center = ordered[i].LanesAreaStart - (stackGap / 2);
                centers[i - 1] = stackOffset + (fillReverse ? _stackingContainerSize - center : center);
            }

            if (fillReverse)
            {
                ordered.Reverse();
                Array.Reverse(centers);
            }

            occupants[lane] = ordered;
            gutters[lane] = centers;
        }

        // The index of the first gutter of each track in the gutters of the stacking axis
        var gutterIndexes = new int[lanes.Length];
        for (var lane = 1; lane < lanes.Length; lane++)
        {
            gutterIndexes[lane] = gutterIndexes[lane - 1] + gutters[lane - 1].Length;
        }

        // The gutters between the tracks
        var laneGaps = new GapLine[lanes.Length - 1];
        var laneGapSizes = new double[laneGaps.Length];
        for (var gap = 0; gap < laneGaps.Length; gap++)
        {
            var before = tracks[lanes[gap]];
            var after = tracks[lanes[gap + 1]];
            laneGapSizes[gap] = Math.Max(0, after.Position - before.End);
            laneGaps[gap] = new GapLine
            {
                Center = laneOffset + ((before.End + after.Position) / 2),
                Size = laneGapSizes[gap],
                Intersections = CreateLaneGapIntersections(occupants[gap], gutters[gap], gutterIndexes[gap], occupants[gap + 1], gutters[gap + 1], gutterIndexes[gap + 1], stackStart, stackEnd, stackGap),
                ValueIndex = TrackReverse ? laneGaps.Length - 1 - gap : gap,
                ValueCount = laneGaps.Length,
                IsMain = true,
            };
        }

        // The gutters of the stacking axis: the values are assigned independently in each track, in the order of the placement
        // (https://drafts.csswg.org/css-gaps-1/#assigning: "values are assigned independently in each grid track, restarting the
        // value list and expanding repeat() for that track")
        var stackGaps = new List<GapLine>();
        for (var lane = 0; lane < lanes.Length; lane++)
        {
            var centers = gutters[lane];
            for (var i = 0; i < centers.Length; i++)
            {
                var start = lane == 0
                    ? GapIntersection.Edge(laneOffset + tracks[lanes[0]].Position)
                    : GapIntersection.Junction(laneGaps[lane - 1].Center, laneGapSizes[lane - 1], lane - 1);
                var end = lane == lanes.Length - 1
                    ? GapIntersection.Edge(laneOffset + tracks[lanes[^1]].End)
                    : GapIntersection.Junction(laneGaps[lane].Center, laneGapSizes[lane], lane);
                stackGaps.Add(new GapLine
                {
                    Center = centers[i],
                    Size = stackGap,
                    Intersections = [start, end],
                    ValueIndex = fillReverse ? centers.Length - 1 - i : i,
                    ValueCount = centers.Length,
                });
            }
        }

        if (laneGaps.Length == 0 && stackGaps.Count == 0)
            return null;

        return new GapGeometry
        {
            Kind = GapContainerKind.GridLanes,
            ColumnGaps = lanesAreColumns ? laneGaps : [.. stackGaps],
            RowGaps = lanesAreColumns ? [.. stackGaps] : laneGaps,
        };
    }

    /// <summary>
    /// Returns the intersections of the gutter between two tracks of a grid lanes container: the bounds of the stacking axis, and
    /// the gutters of the stacking axis of the two tracks (the gutters at the same position are one intersection). The gutters of
    /// the two tracks that overlap one another are one gap junction (see <see cref="GapOverlapWindow"/>): its opening has the state
    /// of the portions inside the junction.
    /// </summary>
    private static GapIntersection[] CreateLaneGapIntersections(List<GridItem> before, double[] beforeGutters, int firstBeforeGutter, List<GridItem> after, double[] afterGutters, int firstAfterGutter, double start, double end, double stackGap)
    {
        var empty = GapSegmentState.None;
        if (before.Count == 0)
        {
            empty |= GapSegmentState.EmptyBefore;
        }

        if (after.Count == 0)
        {
            empty |= GapSegmentState.EmptyAfter;
        }

        // The portions of the gutter: their end, the state of the portion, whether the gutter at their end overlaps the previous
        // one, and the index of a gutter of the stacking axis at their end
        var segments = new List<(double End, GapSegmentState State, bool ContinuesWindow, int Gutter)>();
        var beforeIndex = 0;
        var afterIndex = 0;
        while (beforeIndex < beforeGutters.Length && beforeGutters[beforeIndex] <= start)
        {
            beforeIndex++;
        }

        while (afterIndex < afterGutters.Length && afterGutters[afterIndex] <= start)
        {
            afterIndex++;
        }

        var overlaps = false;
        while (true)
        {
            // The same item on the two sides: a spanning item
            var state = empty;
            if (empty == GapSegmentState.None && ReferenceEquals(before[beforeIndex], after[afterIndex]))
            {
                state = GapSegmentState.Blocked;
            }

            var offset = NextOffset();
            if (offset >= end)
            {
                segments.Add((end, state, false, -1));
                break;
            }

            var gutter = beforeIndex < beforeGutters.Length && beforeGutters[beforeIndex] == offset ? firstBeforeGutter + beforeIndex : firstAfterGutter + afterIndex;

            while (beforeIndex < beforeGutters.Length && beforeGutters[beforeIndex] == offset)
            {
                beforeIndex++;
            }

            while (afterIndex < afterGutters.Length && afterGutters[afterIndex] == offset)
            {
                afterIndex++;
            }

            segments.Add((offset, state, overlaps, gutter));

            // The gutters that only touch do not overlap
            var next = NextOffset();
            overlaps = next < end && next - offset < stackGap;
        }

        var result = new List<GapIntersection>(segments.Count + 1) { GapIntersection.Edge(start, segments[0].State) };
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var outgoing = i + 1 < segments.Count ? segments[i + 1].State : GapSegmentState.None;
            if (i == segments.Count - 1)
            {
                result.Add(GapIntersection.Edge(segment.End));
            }
            else if (!segment.ContinuesWindow)
            {
                result.Add(GapIntersection.Junction(segment.End, stackGap, segment.Gutter, outgoing));
            }
            else if (result[^1].Window == GapOverlapWindow.Close)
            {
                // The window continues: the portion after its previous closing is inside it
                var opening = result[^2];
                var closing = result[^1];
                opening.State |= closing.State;
                closing.Offset = segment.End;
                closing.State = outgoing;
                closing.CrossGapIndex = segment.Gutter;
                result[^2] = opening;
                result[^1] = closing;
            }
            else
            {
                var opening = result[^1];
                opening.Window = GapOverlapWindow.Open;
                result[^1] = opening;
                var closing = GapIntersection.Junction(segment.End, stackGap, segment.Gutter, outgoing);
                closing.Window = GapOverlapWindow.Close;
                result.Add(closing);
            }
        }

        // The percentages of the insets resolve against the size of the junction
        for (var i = 1; i + 1 < result.Count; i++)
        {
            if (result[i].Window == GapOverlapWindow.Open)
            {
                var opening = result[i];
                var closing = result[i + 1];
                var size = closing.Offset - opening.Offset + stackGap;
                opening.InsetBasis = size;
                closing.InsetBasis = size;
                result[i] = opening;
                result[i + 1] = closing;
            }
        }

        return [.. result];

        double NextOffset()
        {
            var offset = end;
            if (beforeIndex < beforeGutters.Length)
            {
                offset = Math.Min(offset, beforeGutters[beforeIndex]);
            }

            if (afterIndex < afterGutters.Length)
            {
                offset = Math.Min(offset, afterGutters[afterIndex]);
            }

            return offset;
        }
    }
}
