namespace Meziantou.Framework.WebPlatform.Css.Layout;

internal sealed partial class GridLayoutAlgorithm
{
    /// <summary>
    /// The track sizing algorithm (https://drafts.csswg.org/css-grid-2/#algo-track-sizing) of one axis: initializes the base sizes
    /// and growth limits of the tracks, resolves the intrinsic track sizes with the contributions of the items, maximizes the
    /// tracks, expands the flexible tracks and stretches the <c>auto</c> tracks. The base sizes are the used sizes of the tracks.
    /// </summary>
    /// <param name="axis">The axis.</param>
    /// <param name="mode">The sizing constraint.</param>
    /// <param name="definiteSize">
    /// The size of the content box in the axis when it is not definite but known (the size of a grid container whose block size
    /// depends on its rows: the rows are sized again with it, to resolve their percentages), or null.
    /// </param>
    /// <param name="resolveIntrinsicSizesOnly">Whether the algorithm stops after the resolution of the intrinsic track sizes (the hypothetical sizes of the intrinsic repetitions of a grid lanes container).</param>
    private void SizeTracks(GridAxis axis, GridSizingMode mode, double? definiteSize = null, bool resolveIntrinsicSizesOnly = false)
    {
        var tracks = GetTracks(axis);
        if (tracks.Length == 0)
            return;

        // The available grid space (https://drafts.csswg.org/css-grid-2/#available-grid-space): the content box when its size is
        // definite; indefinite under a min-content or max-content constraint (the percentages of the tracks then behave as auto)
        double? available = mode == GridSizingMode.Layout ? definiteSize ?? (axis == GridAxis.Columns ? _inlineSize : _blockSize) : null;
        double? percentageBasis = mode == GridSizingMode.Layout ? definiteSize ?? (axis == GridAxis.Columns ? _inlineSize : _percentageBlockSize) : null;
        UpdateGutters(tracks);

        // https://drafts.csswg.org/css-grid-2/#algo-init: "Initialize each track's base size and growth limit"
        foreach (var track in tracks)
        {
            track.ResolveFunctions(percentageBasis);
            track.BaseSize = track.Min == GridMinFunction.Fixed ? track.MinValue : 0;
            track.GrowthLimit = track.Max == GridMaxFunction.Fixed ? track.MaxValue : double.PositiveInfinity;
            if (track.GrowthLimit < track.BaseSize)
            {
                track.GrowthLimit = track.BaseSize;
            }

            track.InfinitelyGrowable = false;
        }

        ComputeBaselineShims(axis);
        CollectSizingItems(axis);
        ResolveIntrinsicTrackSizes(axis, tracks, mode);
        if (resolveIntrinsicSizesOnly)
            return;

        MaximizeTracks(axis, tracks, mode, available);
        ExpandFlexibleTracks(axis, tracks, mode, available);
        if (mode == GridSizingMode.Layout)
        {
            StretchAutoTracks(axis, tracks, available);
        }
    }

    /// <summary>The items that participate in the sizing of the axis being sized (see <see cref="CollectSizingItems(GridAxis)"/>).</summary>
    private readonly List<GridSizingItem> _sizingItems = [];

    /// <summary>
    /// Collects the items that participate in the sizing of the tracks of an axis: the grid items, except the subgrids that are
    /// subgridded in the axis, which are "treated as empty and [their] grid items (the grandchildren) are treated as direct children
    /// of the grid container" (https://drafts.csswg.org/css-grid-2/#algo-grid-sizing), recursively.
    /// </summary>
    private void CollectSizingItems(GridAxis axis) => CollectSizingItems(axis, _sizingItems);

    private void CollectSizingItems(GridAxis axis, List<GridSizingItem> output)
    {
        output.Clear();
        foreach (var item in _items)
        {
            if (_isLanes && !_lanesPlaced && axis == _laneAxis && item.IsLanesAuto)
            {
                AddLanesSizingItems(item, axis, output);
                continue;
            }

            if (item.Subgrid is { } subgrid && subgrid.IsSubgriddedIn(axis, out var subgridAxis, out var reversed))
            {
                subgrid.CollectHoistedItems(output, subgridAxis, axis, item.GetStart(axis), item.GetEnd(axis), reversed, item);
                continue;
            }

            output.Add(new GridSizingItem(item, this, axis, axis, item.GetStart(axis), item.GetEnd(axis))
            {
                ExtraStart = item.GetExtraStart(axis),
                ExtraEnd = item.GetExtraEnd(axis),
                Shim = item.GetShim(axis),
            });
        }
    }

    /// <summary>Returns the sum of the gutters between the tracks <paramref name="start"/> and <paramref name="end"/> (exclusive): the gutters an item spanning them crosses.</summary>
    private double GetGuttersWithin(GridAxis axis, int start, int end)
    {
        var tracks = GetTracks(axis);
        var gap = GetGap(axis);
        var size = 0d;
        for (var i = start + 1; i < end; i++)
        {
            if (tracks[i].HasGutterBefore)
            {
                size += gap;
            }
        }

        return size;
    }

    /// <summary>Returns the sum of the base sizes of all the tracks and of the gutters.</summary>
    private double GetUsedSpace(GridAxis axis, GridTrack[] tracks)
    {
        var size = GetGuttersWithin(axis, 0, tracks.Length);
        foreach (var track in tracks)
        {
            size += track.BaseSize;
        }

        return size;
    }

    /// <summary>Returns whether an item spans a flexible track.</summary>
    private static bool SpansFlexibleTrack(GridSizingItem item, GridTrack[] tracks)
    {
        for (var i = item.Start; i < item.End; i++)
        {
            if (tracks[i].IsFlexible)
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Resolve intrinsic track sizes
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#algo-content: resolves the intrinsic track sizing functions with the items that span a
    /// single track, then the items that span several tracks (by increasing span) that do not span a flexible track, then the
    /// items that span a flexible track (together), distributing the extra space to the tracks.
    /// </summary>
    private void ResolveIntrinsicTrackSizes(GridAxis axis, GridTrack[] tracks, GridSizingMode mode)
    {
        // 2. "Size tracks to fit non-spanning items": the contributions of the items with a span of 1 in a track that has an
        // intrinsic sizing function and not a flexible sizing function
        var growthLimits = new double[tracks.Length];
        Array.Fill(growthLimits, double.NegativeInfinity);
        var maxSpan = 1;
        var hasFlexibleItems = false;
        foreach (var item in _sizingItems)
        {
            var span = item.Span;
            if (span != 1)
            {
                maxSpan = Math.Max(maxSpan, span);
                hasFlexibleItems |= SpansFlexibleTrack(item, tracks);
                continue;
            }

            var index = item.Start;
            var track = tracks[index];
            if (track.IsFlexible)
            {
                hasFlexibleItems = true;
                continue;
            }

            if (!track.HasIntrinsicMin && !track.HasIntrinsicMax)
                continue;

            switch (track.Min)
            {
                case GridMinFunction.MinContent:
                    track.BaseSize = Math.Max(track.BaseSize, GetContribution(item, GridContributionKind.MinContent));
                    break;
                case GridMinFunction.MaxContent:
                    track.BaseSize = Math.Max(track.BaseSize, GetContribution(item, GridContributionKind.MaxContent));
                    break;
                case GridMinFunction.Auto:
                    // "If the track has an auto min track sizing function and the grid container is being sized under a min-/max-content
                    // constraint, set the track's base size to the maximum of its items' limited min-content contributions [...]
                    // Otherwise, set the track's base size to the maximum of its items' minimum contributions"
                    var kind = mode == GridSizingMode.Layout ? GridContributionKind.Minimum : GridContributionKind.LimitedMinContent;
                    track.BaseSize = Math.Max(track.BaseSize, GetContribution(item, kind));
                    break;
            }

            var growth = track.Max switch
            {
                GridMaxFunction.MinContent => GetContribution(item, GridContributionKind.MinContent),
                GridMaxFunction.MaxContent or GridMaxFunction.Auto => GetContribution(item, GridContributionKind.MaxContent),

                // "For fit-content() maximums, furthermore clamp this growth limit by the fit-content() argument"
                GridMaxFunction.FitContent => Math.Min(GetContribution(item, GridContributionKind.MaxContent), track.FitContentLimit),
                _ => double.NegativeInfinity,
            };

            growthLimits[index] = Math.Max(growthLimits[index], growth);
        }

        for (var i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i];
            if (!double.IsNegativeInfinity(growthLimits[i]))
            {
                track.GrowthLimit = growthLimits[i];
            }

            // "In all cases, if a track's growth limit is now less than its base size, increase the growth limit to match the base size"
            if (track.GrowthLimit < track.BaseSize)
            {
                track.GrowthLimit = track.BaseSize;
            }
        }

        // 3. "Increase sizes to accommodate spanning items crossing content-sized tracks", by increasing span
        if (maxSpan > 1)
        {
            var group = new List<GridSizingItem>();
            for (var span = 2; span <= maxSpan; span++)
            {
                group.Clear();
                foreach (var item in _sizingItems)
                {
                    if (item.Span == span && !SpansFlexibleTrack(item, tracks))
                    {
                        group.Add(item);
                    }
                }

                if (group.Count > 0)
                {
                    ResolveSpanningItems(axis, tracks, group, mode, flexible: false);
                }
            }
        }

        // 4. "Increase sizes to accommodate spanning items crossing flexible tracks": the items that span a flexible track,
        // together, distributing the space only to the flexible tracks
        if (hasFlexibleItems)
        {
            var group = new List<GridSizingItem>();
            foreach (var item in _sizingItems)
            {
                if (SpansFlexibleTrack(item, tracks))
                {
                    group.Add(item);
                }
            }

            ResolveSpanningItems(axis, tracks, group, mode, flexible: true);
        }

        // 5. "If any track still has an infinite growth limit (because, for example, it had no items placed in it or it is a
        // flexible track), set its growth limit to its base size."
        foreach (var track in tracks)
        {
            if (double.IsPositiveInfinity(track.GrowthLimit))
            {
                track.GrowthLimit = track.BaseSize;
            }
        }
    }

    /// <summary>The steps of https://drafts.csswg.org/css-grid-2/#algo-spanning-items for a group of items.</summary>
    /// <param name="flexible">Whether the items span flexible tracks: the space is then only distributed to the flexible tracks, according to their flex factors (https://drafts.csswg.org/css-grid-2/#algo-spanning-flex-items).</param>
    private void ResolveSpanningItems(GridAxis axis, GridTrack[] tracks, List<GridSizingItem> items, GridSizingMode mode, bool flexible)
    {
        // 1. "For intrinsic minimums: First distribute extra space to base sizes of tracks with an intrinsic min track sizing
        // function, to accommodate these items' minimum contributions" (the limited min-content contributions under a min- or
        // max-content constraint)
        // (as in the browsers, the items that span flexible tracks keep their minimum contributions under these constraints: the
        // flexible tracks of a container sized to its min-content size do not grow to their contents, WPT css/css-grid/grid-flex-spanning-items-001.html)
        DistributeExtraSpace(axis, tracks, items, growthLimits: false, track => (!flexible || track.IsFlexible) && track.HasIntrinsicMin,
            mode == GridSizingMode.Layout || (flexible && mode == GridSizingMode.MinContent) ? GridContributionKind.Minimum : GridContributionKind.LimitedMinContent, flexible);

        // 2. "For content-based minimums: Next continue to distribute extra space to the base sizes of tracks with a min track sizing
        // function of min-content or max-content, to accommodate these items' min-content contributions."
        DistributeExtraSpace(axis, tracks, items, growthLimits: false, track => (!flexible || track.IsFlexible) && track.Min is GridMinFunction.MinContent or GridMinFunction.MaxContent,
            GridContributionKind.MinContent, flexible);

        // 3. "For max-content minimums: Next, if the grid container is being sized under a max-content constraint, continue to
        // distribute extra space to the base sizes of tracks with a min track sizing function of auto or max-content, to
        // accommodate these items' limited max-content contributions. In all cases, continue to distribute extra space to the base
        // sizes of tracks with a min track sizing function of max-content, to accommodate these items' max-content contributions."
        if (mode == GridSizingMode.MaxContent)
        {
            DistributeExtraSpace(axis, tracks, items, growthLimits: false, track => (!flexible || track.IsFlexible) && track.Min is GridMinFunction.Auto or GridMinFunction.MaxContent,
                GridContributionKind.LimitedMaxContent, flexible);
        }

        DistributeExtraSpace(axis, tracks, items, growthLimits: false, track => (!flexible || track.IsFlexible) && track.Min == GridMinFunction.MaxContent,
            GridContributionKind.MaxContent, flexible);

        // 4. "If at this point any track's growth limit is now less than its base size, increase its growth limit to match its base size."
        foreach (var track in tracks)
        {
            if (track.GrowthLimit < track.BaseSize)
            {
                track.GrowthLimit = track.BaseSize;
            }
        }

        if (flexible)
            return;

        // 5. "For intrinsic maximums: Next distribute extra space to the growth limits of tracks with intrinsic max track sizing
        // function, to accommodate these items' min-content contributions. Mark any tracks whose growth limit changed from infinite
        // to finite in this step as infinitely growable for the next step."
        foreach (var track in tracks)
        {
            track.InfinitelyGrowable = false;
        }

        DistributeExtraSpace(axis, tracks, items, growthLimits: true, track => track.HasIntrinsicMax, GridContributionKind.MinContent, flexible: false);

        // 6. "For max-content maximums: Lastly continue to distribute extra space to the growth limits of tracks with a max track
        // sizing function of max-content, to accommodate these items' max-content contributions."
        DistributeExtraSpace(axis, tracks, items, growthLimits: true, track => track.HasMaxContentMax, GridContributionKind.MaxContent, flexible: false);

        foreach (var track in tracks)
        {
            track.InfinitelyGrowable = false;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#extra-space: distributes the extra space required by the contributions of items to the
    /// base sizes or the growth limits of the affected tracks they span: the space is distributed equally up to the limits of the
    /// tracks, then beyond the limits; each track keeps the largest increase of the items (its planned increase), which is added
    /// at the end.
    /// </summary>
    private void DistributeExtraSpace(GridAxis axis, GridTrack[] tracks, List<GridSizingItem> items, bool growthLimits, Func<GridTrack, bool> isAffected, GridContributionKind kind, bool flexible)
    {
        foreach (var track in tracks)
        {
            track.PlannedIncrease = double.NaN;
        }

        var affected = new List<GridTrack>();
        var any = false;
        foreach (var item in items)
        {
            affected.Clear();
            var start = item.Start;
            var end = item.End;

            // "Find the space to distribute: Subtract the affected size of every spanned track (not just the affected tracks) from
            // the item's size contribution, flooring it at zero. (For infinite growth limits, substitute the track's base size.)"
            var spanned = GetGuttersWithin(axis, start, end);
            for (var i = start; i < end; i++)
            {
                var track = tracks[i];
                spanned += GetAffectedSize(track, growthLimits);
                if (isAffected(track))
                {
                    affected.Add(track);
                }
            }

            if (affected.Count == 0)
                continue;

            any = true;
            foreach (var track in affected)
            {
                track.ItemIncurredIncrease = 0;
                if (double.IsNaN(track.PlannedIncrease))
                {
                    track.PlannedIncrease = 0;
                }
            }

            var space = Math.Max(0, GetContribution(item, kind) - spanned);
            if (space <= Epsilon)
                continue;

            if (flexible)
            {
                // "if the sum of the flexible sizing functions of all flexible tracks spanned by the item is greater than or equal to
                // one, distributing space to such tracks according to the ratios of their flexible sizing functions rather than
                // distributing space equally; and if the sum is less than one, distributing that proportion of space according to
                // the ratios of their flexible sizing functions and the rest equally"
                var flexSum = 0d;
                foreach (var track in affected)
                {
                    flexSum += track.FlexFactor;
                }

                foreach (var track in affected)
                {
                    track.ItemIncurredIncrease = flexSum >= 1
                        ? space * track.FlexFactor / flexSum
                        : (space * track.FlexFactor) + (space * (1 - flexSum) / affected.Count);
                }
            }
            else
            {
                // "Distribute space up to limits"
                space = DistributeEqually(affected, space, track => GetAffectedSize(track, growthLimits), track => GetLimit(track, growthLimits));

                // "Distribute space to non-affected tracks": not applied, as in the browsers (the space would be absorbed by the
                // tracks that are not affected, whose growth limits are still infinite when the base sizes are distributed: WPT
                // css/css-grid/parsing/grid-content-sized-columns-resolution.html)
                // "Distribute space beyond limits"
                if (space > Epsilon)
                {
                    DistributeBeyondLimits(affected, space, growthLimits, kind);
                }
            }

            // "For each affected track, if the track's item-incurred increase is larger than the track's planned increase set the
            // track's planned increase to that value."
            foreach (var track in affected)
            {
                track.PlannedIncrease = Math.Max(track.PlannedIncrease, track.ItemIncurredIncrease);
            }
        }

        if (!any)
            return;

        // "Update the tracks' affected sizes by adding in the planned increase [...] (If the affected size is an infinite growth limit,
        // set it to the track's base size plus the planned increase.)"
        foreach (var track in tracks)
        {
            if (double.IsNaN(track.PlannedIncrease))
                continue;

            if (growthLimits)
            {
                if (double.IsPositiveInfinity(track.GrowthLimit))
                {
                    track.GrowthLimit = track.BaseSize + track.PlannedIncrease;
                    track.InfinitelyGrowable = true;
                }
                else
                {
                    track.GrowthLimit += track.PlannedIncrease;
                }
            }
            else
            {
                track.BaseSize += track.PlannedIncrease;
            }

            track.PlannedIncrease = double.NaN;
        }
    }

    /// <summary>The affected size of a track: its base size, or its growth limit (its base size when it is infinite).</summary>
    private static double GetAffectedSize(GridTrack track, bool growthLimits)
        => growthLimits && !double.IsPositiveInfinity(track.GrowthLimit) ? track.GrowthLimit : track.BaseSize;

    /// <summary>
    /// The limit of the distribution of space to a track: "For base sizes, the limit is its growth limit, capped by its
    /// fit-content() argument if any. For growth limits, the limit is the growth limit if the growth limit is finite and the track
    /// is not infinitely growable, otherwise its fit-content() argument if it has a fit-content() track sizing function, and
    /// infinity otherwise."
    /// </summary>
    private static double GetLimit(GridTrack track, bool growthLimits)
    {
        if (!growthLimits)
            return Math.Min(track.GrowthLimit, track.FitContentLimit);

        if (!double.IsPositiveInfinity(track.GrowthLimit) && !track.InfinitelyGrowable)
            return track.GrowthLimit;

        return track.FitContentLimit;
    }

    /// <summary>
    /// "Distribute space beyond limits: If extra space remains at this point, unfreeze and continue to distribute space to the
    /// item-incurred increase of" the affected tracks whose max track sizing function matches the contributions (a fit-content()
    /// track is treated as max-content until it reaches its argument).
    /// </summary>
    private static void DistributeBeyondLimits(List<GridTrack> affected, double space, bool growthLimits, GridContributionKind kind)
    {
        var targets = new List<GridTrack>();
        var maxContent = kind is GridContributionKind.MaxContent or GridContributionKind.LimitedMaxContent;
        foreach (var track in affected)
        {
            var matches = growthLimits || !maxContent ? track.HasIntrinsicMax : track.HasMaxContentMax;
            if (matches && (track.Max != GridMaxFunction.FitContent || GetAffectedSize(track, growthLimits) + track.ItemIncurredIncrease < track.FitContentLimit - Epsilon))
            {
                targets.Add(track);
            }
        }

        // "if there are no such tracks, then all affected tracks" (for the base sizes)
        if (targets.Count == 0)
        {
            if (growthLimits)
                return;

            targets.AddRange(affected);
            DistributeEqually(targets, space, track => GetAffectedSize(track, growthLimits), _ => double.PositiveInfinity);
            return;
        }

        var remaining = DistributeEqually(targets, space, track => GetAffectedSize(track, growthLimits), track => track.Max == GridMaxFunction.FitContent ? track.FitContentLimit : double.PositiveInfinity);

        // A fit-content() track that reaches its argument "is treated as having a fixed sizing function of that argument": the rest of
        // the space goes to the other tracks that match, or to all the affected tracks (WPT
        // css/css-grid/grid-lanes/track-sizing/grid-lanes-percentage-rows-indefinite-height-003.html)
        if (remaining > Epsilon && !growthLimits)
        {
            DistributeBeyondLimits(affected, remaining, growthLimits, kind);
        }
    }

    /// <summary>
    /// Distributes space equally to the item-incurred increases of tracks, "freezing a track's item-incurred increase as its
    /// affected size + item-incurred increase reaches its limit (and continuing to grow the unfrozen tracks as needed)". Returns the
    /// space that could not be distributed.
    /// </summary>
    private static double DistributeEqually(List<GridTrack> tracks, double space, Func<GridTrack, double> size, Func<GridTrack, double> limit)
    {
        while (space > Epsilon)
        {
            var unfrozen = 0;
            foreach (var track in tracks)
            {
                if (size(track) + track.ItemIncurredIncrease < limit(track) - Epsilon)
                {
                    unfrozen++;
                }
            }

            if (unfrozen == 0)
                break;

            var share = space / unfrozen;
            var distributed = 0d;
            foreach (var track in tracks)
            {
                var room = limit(track) - (size(track) + track.ItemIncurredIncrease);
                if (room <= Epsilon)
                    continue;

                var increase = Math.Min(share, room);
                track.ItemIncurredIncrease += increase;
                distributed += increase;
            }

            space -= distributed;
            if (distributed <= Epsilon)
                break;
        }

        return Math.Max(0, space);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Maximize, flexible tracks, stretch
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the definite min and max sizes of the content box of the container in an axis (the max-width/height and
    /// min-width/height of the algorithm), or null; the minimum wins over a smaller maximum.
    /// </summary>
    private (double? Min, double? Max) GetMinMaxSizes(GridAxis axis)
    {
        var (min, max) = axis == GridAxis.Columns ? (_minInlineSize, _maxInlineSize) : (_minBlockSize, _maxBlockSize);
        return (min, max is { } maximum && min is { } minimum ? Math.Max(maximum, minimum) : max);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#algo-grow-tracks: "If the free space is positive, distribute it equally to the base
    /// sizes of all tracks, freezing tracks as they reach their growth limits [...] if sizing the grid container under a
    /// max-content constraint, the free space is infinite; if sizing under a min-content constraint, the free space is zero. If
    /// this would cause the grid to be larger than the grid container's inner size as limited by its max-width/height, then redo
    /// this step, treating the available grid space as equal to the grid container's inner size when it's sized to its
    /// max-width/height." An indefinite available space (the rows of a container whose block size is indefinite) is a
    /// max-content constraint.
    /// </summary>
    private void MaximizeTracks(GridAxis axis, GridTrack[] tracks, GridSizingMode mode, double? available)
    {
        if (mode == GridSizingMode.MinContent)
            return;

        var used = GetUsedSpace(axis, tracks);
        double freeSpace;
        if (available is { } size)
        {
            freeSpace = size - used;
        }
        else
        {
            var growth = GetGuttersWithin(axis, 0, tracks.Length);
            foreach (var track in tracks)
            {
                growth += track.GrowthLimit;
            }

            // (as in the browsers, the maximum block size of the container does not limit its rows: they keep the sizes of their
            // contents, which overflow the container, WPT css/css-grid/layout-algorithm/grid-track-ignores-max-size-001.html)
            if (axis == GridAxis.Columns && GetMinMaxSizes(axis).Max is { } max && growth > max)
            {
                freeSpace = max - used;
            }
            else
            {
                foreach (var track in tracks)
                {
                    track.BaseSize = track.GrowthLimit;
                }

                return;
            }
        }

        if (freeSpace <= Epsilon)
            return;

        var list = new List<GridTrack>(tracks);
        foreach (var track in list)
        {
            track.ItemIncurredIncrease = 0;
        }

        DistributeEqually(list, freeSpace, track => track.BaseSize, track => track.GrowthLimit);
        foreach (var track in list)
        {
            track.BaseSize += track.ItemIncurredIncrease;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#algo-flex-tracks: sizes the flexible tracks with "the largest value it can assign to an
    /// fr without exceeding the available space": the size of an fr in the free space when it is definite, else the largest of the
    /// base sizes of the flexible tracks per flex factor and of the fr sizes required by the max-content contributions of the
    /// items that cross them (then the grid is kept between the min and max sizes of the container).
    /// </summary>
    private void ExpandFlexibleTracks(GridAxis axis, GridTrack[] tracks, GridSizingMode mode, double? available)
    {
        var hasFlexible = false;
        foreach (var track in tracks)
        {
            hasFlexible |= track.IsFlexible;
        }

        if (!hasFlexible)
            return;

        double fraction;
        var gutters = GetGuttersWithin(axis, 0, tracks.Length);
        if (mode == GridSizingMode.MinContent)
        {
            fraction = 0;
        }
        else if (available is { } size)
        {
            // "If the free space is zero [...] the used flex fraction is zero. Otherwise, if the free space is a definite length: the
            // result of finding the size of an fr using all of the grid tracks and a space to fill of the available grid space."
            fraction = size - GetUsedSpace(axis, tracks) <= 0 ? 0 : FindFrSize(tracks, 0, tracks.Length, size - gutters);
        }
        else
        {
            fraction = 0;
            foreach (var track in tracks)
            {
                if (track.IsFlexible)
                {
                    fraction = Math.Max(fraction, track.FlexFactor > 1 ? track.BaseSize / track.FlexFactor : track.BaseSize);
                }
            }

            foreach (var item in _sizingItems)
            {
                if (!SpansFlexibleTrack(item, tracks))
                    continue;

                var start = item.Start;
                var end = item.End;
                var contribution = GetContribution(item, GridContributionKind.MaxContent);
                fraction = Math.Max(fraction, FindFrSize(tracks, start, end, contribution - GetGuttersWithin(axis, start, end)));
            }

            // "If using this flex fraction would cause the grid to be smaller than the grid container's min-width/height (or larger
            // than the grid container's max-width/height), then redo this step, treating the free space as definite and the
            // available grid space as equal to the grid container's inner size when it's sized to its min-width/height (max-width/height)."
            var gridSize = gutters;
            foreach (var track in tracks)
            {
                gridSize += track.IsFlexible ? Math.Max(track.BaseSize, fraction * track.FlexFactor) : track.BaseSize;
            }

            var (min, max) = GetMinMaxSizes(axis);
            if (min is { } minimum && gridSize < minimum - Epsilon)
            {
                fraction = FindFrSize(tracks, 0, tracks.Length, minimum - gutters);
            }
            else if (max is { } maximum && gridSize > maximum + Epsilon)
            {
                fraction = FindFrSize(tracks, 0, tracks.Length, maximum - gutters);
            }
        }

        // "For each flexible track, if the product of the used flex fraction and the track's flex factor is greater than the track's
        // base size, set its base size to that product."
        foreach (var track in tracks)
        {
            if (track.IsFlexible && fraction * track.FlexFactor > track.BaseSize)
            {
                track.BaseSize = fraction * track.FlexFactor;
            }
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#algo-find-fr-size: "This algorithm finds the largest size that an fr unit can be
    /// without exceeding the target size": the leftover space (the space to fill minus the base sizes of the non-flexible tracks)
    /// divided by the sum of the flex factors (at least 1), the flexible tracks whose base size is larger than their share being
    /// treated as inflexible.
    /// </summary>
    private static double FindFrSize(GridTrack[] tracks, int start, int end, double spaceToFill)
    {
        for (var i = start; i < end; i++)
        {
            tracks[i].IsTreatedAsInflexible = false;
        }

        while (true)
        {
            var leftover = spaceToFill;
            var flexSum = 0d;
            for (var i = start; i < end; i++)
            {
                var track = tracks[i];
                if (track.IsFlexible && !track.IsTreatedAsInflexible)
                {
                    flexSum += track.FlexFactor;
                }
                else
                {
                    leftover -= track.BaseSize;
                }
            }

            var size = Math.Max(0, leftover) / Math.Max(1, flexSum);
            var restart = false;
            for (var i = start; i < end; i++)
            {
                var track = tracks[i];
                if (track.IsFlexible && !track.IsTreatedAsInflexible && size * track.FlexFactor < track.BaseSize)
                {
                    track.IsTreatedAsInflexible = true;
                    restart = true;
                }
            }

            if (!restart)
                return size;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#algo-stretch: "When the content-distribution property of the grid container is normal or
    /// stretch in this axis, this step expands tracks that have an auto max track sizing function by dividing any remaining
    /// positive, definite free space equally amongst them. If the free space is indefinite, but the grid container has a definite
    /// min-width/height, use that size to calculate the free space for this step instead."
    /// </summary>
    private void StretchAutoTracks(GridAxis axis, GridTrack[] tracks, double? available)
    {
        var alignment = axis == GridAxis.Columns ? _style.Alignment.JustifyContent : _style.Alignment.AlignContent;
        if (alignment.Keyword is not (AlignmentKeyword.Normal or AlignmentKeyword.Stretch))
            return;

        var size = available ?? GetMinMaxSizes(axis).Min;
        if (size is null)
            return;

        var freeSpace = size.Value - GetUsedSpace(axis, tracks);
        if (freeSpace <= Epsilon)
            return;

        var count = 0;
        foreach (var track in tracks)
        {
            if (track.Max == GridMaxFunction.Auto)
            {
                count++;
            }
        }

        if (count == 0)
            return;

        var share = freeSpace / count;
        foreach (var track in tracks)
        {
            if (track.Max == GridMaxFunction.Auto)
            {
                track.BaseSize += share;
            }
        }
    }
}

/// <summary>The intrinsic size contributions of the track sizing algorithm (https://drafts.csswg.org/css-grid-2/#algo-content).</summary>
internal enum GridContributionKind : byte
{
    /// <summary>https://drafts.csswg.org/css-grid-2/#minimum-contribution</summary>
    Minimum,

    /// <summary>https://drafts.csswg.org/css-sizing-3/#min-content-contribution</summary>
    MinContent,

    /// <summary>https://drafts.csswg.org/css-sizing-3/#max-content-contribution</summary>
    MaxContent,

    /// <summary>https://drafts.csswg.org/css-grid-2/#limited-contribution: the min-content contribution limited by the fixed max track sizing functions, floored by the minimum contribution.</summary>
    LimitedMinContent,

    /// <summary>The max-content contribution limited the same way.</summary>
    LimitedMaxContent,
}
