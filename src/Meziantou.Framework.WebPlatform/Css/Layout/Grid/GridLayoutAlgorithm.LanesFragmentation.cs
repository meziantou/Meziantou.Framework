namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The placement of the items of a grid lanes container that is fragmented in its stacking axis (https://drafts.csswg.org/css-grid-3/#fragmentation)
internal sealed partial class GridLayoutAlgorithm
{
    /// <summary>The maximum number of fragmentainers in which the items of a grid lanes container are placed again.</summary>
    private const int MaxLanesFragmentainers = 1000;

    /// <summary>The precision of the comparisons with the edges of the fragmentainers (as in the fragmentation, <see cref="ColumnBreaker"/>).</summary>
    private const double FragmentationEpsilon = 0.01;

    /// <summary>
    /// Returns the fragmentainers of the container when it is a grid lanes container fragmented in its stacking axis (the block
    /// axis) whose items are placed in each fragmentainer (see <see cref="GridLanesFragmentation"/>), else null. Not with dense
    /// packing nor <c>fill-reverse</c>, whose items are placed as in a container that is not fragmented.
    /// </summary>
    private GridLanesBreaks? GetLanesBreaks()
    {
        if (_stackAxis != GridAxis.Rows || !IsPlacedInFragmentainers(_style))
            return null;

        return _context.GridLanesBreaks is { } breaks && breaks.TryGetValue(_box, out var result) ? result : null;
    }

    /// <summary>
    /// Returns whether the items of a grid lanes container fragmented in its stacking axis are placed in each fragmentainer (see
    /// <see cref="GridLanesFragmentation"/>): its lanes are columns, and it has neither dense packing nor <c>fill-reverse</c>.
    /// </summary>
    internal static bool IsPlacedInFragmentainers(LayoutStyle style)
        => GetLaneAxis(style) == GridAxis.Columns && !style.Grid.LanesDirection.FillReverse && !style.Grid.LanesPackDense && !style.Grid.AutoFlow.IsDense;

    /// <summary>
    /// The grid lanes placement in a container fragmented in its stacking axis (https://drafts.csswg.org/css-grid-3/#fragmentation):
    /// "Each grid axis track is fragmented independently in the stacking axis. If a grid item is fragmented, or has a forced break
    /// before/after it, then the running position for the tracks that it spans in the grid axis are set to the size of the
    /// fragmentainer so that no further items will be placed in those tracks. An item that is split into multiple fragments
    /// retains its placement in the grid axis for all its fragments. A grid item that is pushed, however, is placed again by the
    /// next grid container fragment. Placement continues until all items are placed or pushed to a new fragment." The items are
    /// placed in the strip of the fragmentation context: an item that is pushed is placed again after the position where the next
    /// fragmentainer starts, where the running positions start again (after the rest of the items that are fragmented), and the
    /// fragmentation of the strip breaks the items that cross the end of a fragmentainer. As in Gecko: an item is pushed when it does
    /// not fit in what remains of the fragmentainer and cannot be broken (monolithic, <c>break-inside: avoid</c>), unless it is at the
    /// start of the fragmentainer; the rest of an item that is broken is as tall as the part of the item after the end of the
    /// fragmentainer.
    /// </summary>
    private void PlaceFragmentedLanesItems(GridLanesBreaks breaks)
    {
        var tracks = GetTracks(_laneAxis);
        var count = tracks.Length;
        var gap = GetGap(_stackAxis);
        var tolerance = GetFlowTolerance();
        var trackReverse = TrackReverse;
        var running = new double[count];

        // (the tracks closed by a forced break in the previous fragmentainer: the margins after a forced break are preserved)
        var forced = new bool[count];
        var nextForced = new bool[count];
        var cursor = 0;
        var rangeEnd = 0d;
        var pending = new List<GridItem>(_items);
        var pushed = new List<GridItem>();
        var regionStart = 0d;
        for (var region = 0; pending.Count > 0; region++)
        {
            // (the items that remain after many fragmentainers are placed as in a container that is not fragmented)
            var regionEnd = region < MaxLanesFragmentainers ? breaks.GetEnd(region) : double.PositiveInfinity;
            foreach (var item in pending)
            {
                var span = Math.Min(item.GetSpan(_laneAxis), count);
                var start = item.IsLanesAuto ? ChooseLanesPosition(running, tracks, span, tolerance, cursor) : trackReverse ? count - item.GetEnd(_laneAxis) : item.GetStart(_laneAxis);
                var position = GetRunningPosition(running, start, span);
                SetLanePosition(item, trackReverse ? count - start - span : start, trackReverse ? count - start : start + span);
                PrepareLanesSubgrid(item);

                // An item that does not fit in what remains of the fragmentainer (its tracks are full or closed, or it cannot be
                // broken), or that has a forced break before it, is pushed to the next one, unless it is at its start: the break
                // before it closes its tracks (as in Gecko: the next items are not placed in them)
                var (outer, marginStart, isUnbreakable) = MeasureFragmentedLanesItem(item, breaks);
                var fragmentation = item.Box.Style.Layout.Fragmentation;
                var isForcedBefore = breaks.IsForcedBreak(fragmentation.BreakBefore);
                if (position > regionStart + FragmentationEpsilon && !double.IsPositiveInfinity(regionEnd) && (isForcedBefore || position >= regionEnd - FragmentationEpsilon || (isUnbreakable && position + outer > regionEnd + FragmentationEpsilon)))
                {
                    pushed.Add(item);
                    Close(start, span, regionEnd, isForcedBefore);
                    continue;
                }

                // https://drafts.csswg.org/css-break/#break-margins: the block-start margin of an item at the start of a fragmentainer
                // after an unforced break is truncated (its margin box starts before the fragmentainer); it is preserved after a
                // forced break
                if (region > 0 && marginStart > 0 && position <= regionStart + FragmentationEpsilon && !isForcedBefore && !IsAfterForcedBreak(start, span))
                {
                    position -= Math.Min(marginStart, position);
                }

                var advance = Math.Max(0, outer + gap);
                item.LanesOuterSize = Math.Max(0, outer);
                item.LanesPosition = position;

                // (an item that is broken ends where its rest ends once its contents are fragmented, also when a forced break inside
                // it breaks it: the next items of its lanes follow it)
                var end = position + item.LanesOuterSize;
                var isBroken = position + outer > regionEnd + FragmentationEpsilon;
                if (breaks.ItemEnds is { } itemEnds && itemEnds.TryGetValue(item.Box, out var fragmentedEnd) && fragmentedEnd.End > end
                    && Math.Abs(fragmentedEnd.Start - (position + marginStart)) <= FragmentationEpsilon)
                {
                    advance += fragmentedEnd.End - end;
                    end = fragmentedEnd.End;
                    isBroken |= end > regionEnd + FragmentationEpsilon;
                }

                rangeEnd = Math.Max(rangeEnd, end);
                for (var i = start; i < start + span; i++)
                {
                    running[i] = position + advance;
                }

                // An item that is broken (its rest starts the next fragmentainer, see below), or that has a forced break after it,
                // closes its tracks
                var isForcedAfter = breaks.IsForcedBreak(fragmentation.BreakAfter);
                if (isBroken || isForcedAfter)
                {
                    Close(start, span, regionEnd, isForcedAfter && !isBroken);
                }

                if (item.IsLanesAuto)
                {
                    cursor = start + span;
                }
            }

            if (double.IsPositiveInfinity(regionEnd))
                break;

            // The next fragmentainer: the running positions start again at its start, after the rest of the items that are broken
            // (the gaps after the items that end in the fragmentainer disappear at the break)
            for (var i = 0; i < count; i++)
            {
                running[i] = running[i] > regionEnd + Math.Max(0, gap) + FragmentationEpsilon ? running[i] : regionEnd;
            }

            // (the placement starts again in the next fragment of the container: the auto-placement cursor points to the first line)
            regionStart = regionEnd;
            cursor = 0;
            (forced, nextForced) = (nextForced, forced);
            Array.Clear(nextForced);
            (pending, pushed) = (pushed, pending);
            pushed.Clear();
        }

        _stackingRange = rangeEnd;
        _lanesPlaced = true;

        void Close(int start, int span, double end, bool isForced)
        {
            for (var i = start; i < start + span; i++)
            {
                running[i] = Math.Max(running[i], end);
                nextForced[i] |= isForced;
            }
        }

        bool IsAfterForcedBreak(int start, int span)
        {
            for (var i = start; i < start + span; i++)
            {
                if (forced[i])
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Lays out an item of a grid lanes container fragmented in its stacking axis: its outer size in the stacking axis, and whether it
    /// cannot be broken (a monolithic box, <c>break-inside: avoid</c>), and its block-start margin.
    /// </summary>
    private (double Outer, double MarginStart, bool IsUnbreakable) MeasureFragmentedLanesItem(GridItem item, GridLanesBreaks breaks)
    {
        var inlineArea = GetAreaSize(item, GridAxis.Columns);
        var fragment = LayoutItem(item, inlineArea, GetAreaSize(item, GridAxis.Rows));
        var size = _writingDirection.ToLogical(fragment.Size);
        var margins = GetMargins(item, inlineArea ?? 0);
        var breakInside = item.Box.Style.Layout.Fragmentation.BreakInside;
        var isUnbreakable = breaks.IsAvoidInside(breakInside) || breaks.IsMonolithic(fragment, _writingDirection);
        return (size.Block + margins.BlockSum(), margins.BlockStart, isUnbreakable);
    }
}
