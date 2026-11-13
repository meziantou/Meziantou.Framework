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
        var placed = new List<GridItem>?[count];
        var pushedInLane = new bool[count];
        HashSet<GridItem>? early = null;
        for (var region = 0; pending.Count > 0; region++)
        {
            // (the items that remain after many fragmentainers are placed as in a container that is not fragmented)
            var regionEnd = region < MaxLanesFragmentainers ? breaks.GetEnd(region) : double.PositiveInfinity;

            // The items of the fragmentainer are placed again when an early break pushes an item that was placed in it (the state at
            // the start of the fragmentainer is restored)
            var regionRunning = (double[])running.Clone();
            var regionForced = (bool[])forced.Clone();
            var regionNextForced = (bool[])nextForced.Clone();
            var regionRangeEnd = rangeEnd;
            early?.Clear();
            while (!PlaceRegion(region, regionEnd))
            {
                Array.Copy(regionRunning, running, count);
                Array.Copy(regionForced, forced, count);
                Array.Copy(regionNextForced, nextForced, count);
                rangeEnd = regionRangeEnd;
                cursor = 0;
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

        // Places the pending items in the fragmentainer that ends at regionEnd; returns false when an early break pushes an item
        // that is placed in it (the fragmentainer is placed again)
        bool PlaceRegion(int region, double regionEnd)
        {
            pushed.Clear();
            foreach (var list in placed)
            {
                list?.Clear();
            }

            Array.Clear(pushedInLane);

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
                var isEarly = early is not null && early.Contains(item);
                if (position > regionStart + FragmentationEpsilon && !double.IsPositiveInfinity(regionEnd) && (isEarly || isForcedBefore || position >= regionEnd - FragmentationEpsilon || (isUnbreakable && position + outer > regionEnd + FragmentationEpsilon)))
                {
                    // https://drafts.csswg.org/css-break-3/#breaking-rules: a break before the item that is avoided moves to an earlier
                    // break opportunity of its lanes
                    if (!isEarly && !isForcedBefore && AddEarlyBreaks(item, start, span, regionEnd))
                        return false;

                    pushed.Add(item);
                    Close(start, span, regionEnd, isForcedBefore);
                    Array.Fill(pushedInLane, true, start, span);
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
                    (placed[i] ??= []).Add(item);
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

            return true;
        }

        // https://drafts.csswg.org/css-break-3/#breaking-rules: the break chosen is the last break opportunity that does not violate
        // the avoid rules (break-before, break-after: avoid), when there is one: an item that is pushed while the break before it is avoided
        // (break-before: avoid on it, break-after: avoid on the previous item of a lane) pushes the last item of that lane before which
        // the break is not avoided, unless it is at the start of the fragmentainer (each lane is fragmented independently). The item
        // that is pushed closes its lanes: the items that follow it in them are pushed too. Returns whether an item is pushed.
        bool AddEarlyBreaks(GridItem item, int start, int span, double regionEnd)
        {
            var added = false;
            for (var i = start; i < start + span; i++)
            {
                // (an item that is broken at the end of the fragmentainer, or an item that is pushed before this one, already breaks the lane)
                if (pushedInLane[i] || placed[i] is not { Count: > 0 } lane || lane[^1].LanesPosition + lane[^1].LanesOuterSize > regionEnd + FragmentationEpsilon || !IsAvoidedBetween(lane[^1], item))
                    continue;

                for (var k = lane.Count - 1; k >= 0; k--)
                {
                    var candidate = lane[k];
                    if (candidate.LanesPosition <= regionStart + FragmentationEpsilon)
                        break;

                    if (!IsAvoidedBetween(k > 0 ? lane[k - 1] : null, candidate))
                    {
                        added |= (early ??= new HashSet<GridItem>(ReferenceEqualityComparer.Instance)).Add(candidate);
                        break;
                    }
                }
            }

            return added;
        }

        bool IsAvoidedBetween(GridItem? previous, GridItem next)
            => IsAvoided(next.Box.Style.Layout.Fragmentation.BreakBefore) || (previous is not null && IsAvoided(previous.Box.Style.Layout.Fragmentation.BreakAfter));

        bool IsAvoided(BreakBetween value) => value == BreakBetween.Avoid || value == (breaks.IsColumns ? BreakBetween.AvoidColumn : BreakBetween.AvoidPage);

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
