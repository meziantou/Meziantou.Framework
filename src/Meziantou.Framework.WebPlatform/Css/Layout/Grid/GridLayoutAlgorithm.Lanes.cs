namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The grid lanes layout (https://drafts.csswg.org/css-grid-3/): the items of a grid lanes container are placed in the tracks of
/// one axis (the grid axis, sized by the track sizing algorithm of the grid layout) and stacked in the other one (the stacking
/// axis).
/// </summary>
/// <remarks>
/// <para>
/// The stacking axis is a grid axis with a single track: the stacking range (https://drafts.csswg.org/css-grid-3/#stacking-range),
/// whose lines are the lines of the absolutely positioned boxes (https://drafts.csswg.org/css-grid-3/#abspos). The items with an
/// automatic position in the grid axis contribute to the sizes of all the tracks they could be placed in (the sizing items are
/// copied at every possible position, https://drafts.csswg.org/css-grid-3/#track-sizing), then the grid lanes placement algorithm
/// (https://drafts.csswg.org/css-grid-3/#grid-lanes-layout-algorithm) places and lays out the items one by one.
/// </para>
/// <para>
/// The positions of the stacking axis are computed in the direction of the stacking ("flow" coordinates, from the end edge of the
/// content box with <c>fill-reverse</c>), then converted to the logical coordinates of the grid. <c>track-reverse</c> places
/// the items from the last track: the placement works on the tracks in reverse order.
/// </para>
/// </remarks>
internal sealed partial class GridLayoutAlgorithm
{
    /// <summary>Whether the container is a grid lanes container (https://drafts.csswg.org/css-grid-3/#grid-lanes-container).</summary>
    private readonly bool _isLanes;

    /// <summary>The grid axis of a grid lanes container: the axis of its tracks.</summary>
    private readonly GridAxis _laneAxis;

    /// <summary>The stacking axis of a grid lanes container: the axis in which its items stack.</summary>
    private readonly GridAxis _stackAxis;

    /// <summary>Whether the items of the lanes are placed: their areas in the grid axis are their tracks (else the items with an automatic position are sized in every position).</summary>
    private bool _lanesPlaced;

    /// <summary>The size of the stacking range of the last placement (https://drafts.csswg.org/css-grid-3/#stacking-range).</summary>
    private double _stackingRange;

    /// <summary>The used size of the content box in the stacking axis (the alignment container of the stacking range).</summary>
    private double _stackingContainerSize;

    /// <summary>
    /// The size of the content box of a grid lanes subgrid in its stacking axis, given by its parent grid to the instance that
    /// places its items (the containing block of the items in that axis, when the subgrid fills a grid area of a known size), or null.
    /// </summary>
    private double? _parentStackingSize;

    private bool FillReverse => _style.Grid.LanesDirection.FillReverse;

    private bool TrackReverse => _style.Grid.LanesDirection.TrackReverse;

    /// <summary>
    /// Returns the grid axis of a grid lanes container (https://drafts.csswg.org/css-grid-3/#grid-lanes-orientation): the
    /// orientation of <c>grid-lanes-direction</c>, or "If grid-template-columns is none and grid-template-rows is not none, the grid
    /// container's block axis is the grid axis (establishing rows). Otherwise (thus by default), the grid container's inline axis is
    /// the grid axis (establishing columns)."
    /// </summary>
    internal static GridAxis GetLaneAxis(LayoutStyle style)
    {
        var grid = style.Grid;
        return grid.LanesDirection.Orientation switch
        {
            GridLanesOrientation.Row => GridAxis.Rows,
            GridLanesOrientation.Column => GridAxis.Columns,
            _ => grid.TemplateColumns.IsNone && !grid.TemplateRows.IsNone ? GridAxis.Rows : GridAxis.Columns,
        };
    }

    /// <summary>Returns whether the stacking axis (<c>fill-reverse</c>) and the grid axis (<c>track-reverse</c>) of a grid lanes container are filled from their end side.</summary>
    internal static (bool Stack, bool Grid) GetReversedAxes(LayoutStyle style)
    {
        var direction = style.Grid.LanesDirection;
        return (direction.FillReverse, direction.TrackReverse);
    }

    /// <summary>
    /// Returns the stacking axis as an alignment container: its positions are measured from the side where the stacking starts (the
    /// end side with <c>fill-reverse</c>), which is also the side of <c>normal</c> and <c>flex-start</c>.
    /// </summary>
    private AlignmentAxis GetStackingAlignmentAxis()
    {
        var axis = _stackAxis == GridAxis.Columns ? AlignmentAxis.Inline(_writingDirection) : AlignmentAxis.Block(_writingDirection);
        if (!FillReverse)
            return axis;

        var flowStart = WritingDirection.Opposite(axis.WritingModeStart);
        return axis with { Start = flowStart, FlexStart = flowStart };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Initialization
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Collects the items, creates the explicit grid of the grid axis (https://drafts.csswg.org/css-grid-3/#track-sizing: "The
    /// explicit grid and implicit grid are formed in the same way as for a regular grid container"), resolves the definite positions
    /// of the items in it, and creates the implicit tracks (the definite positions and the largest span of the items with an
    /// automatic position). The stacking axis has one track.
    /// </summary>
    /// <remarks>
    /// The explicit grid of the grid axis of a grid lanes subgrid (https://drafts.csswg.org/css-grid-3/#subgrids: "If the parent's
    /// corresponding axis is a grid axis, the subgridded axis is taken from the parent container as specified for grid containers")
    /// is the tracks it spans in its parent grid: it has no implicit tracks, and its gutters are the ones of the parent (see
    /// <see cref="ApplySubgridExtraMargins"/>).
    /// </remarks>
    private void InitializeLanes()
    {
        CollectItems();
        var grid = _style.Grid;
        _input = GetSubgridInput();
        var input = _input?.Get(_laneAxis);
        if (input is null)
        {
            _input = null;
        }

        _lanesPlaced = false;
        var lanesAreColumns = _laneAxis == GridAxis.Columns;
        var (size, maxSize, minSize) = lanesAreColumns ? (_inlineSize, _maxInlineSize, _minInlineSize) : (_blockSize, _maxBlockSize, _minBlockSize);
        if (input is not null)
        {
            if (lanesAreColumns)
            {
                _columnGap = input.ParentGap;
            }
            else
            {
                _rowGap = input.ParentGap;
            }
        }

        var intrinsicSizes = input is null && (size ?? maxSize ?? minSize) is not null ? ComputeIntrinsicRepeatSizes(size, maxSize, minSize) : null;
        var laneAxis = GridExplicitAxis.Create(grid, _laneAxis, size, maxSize, minSize, GetGap(_laneAxis), input?.Span, input?.LineNames, intrinsicSizes);
        var stackAxis = GridExplicitAxis.CreateStackingAxis();
        (_explicitColumns, _explicitRows) = lanesAreColumns ? (laneAxis, stackAxis) : (stackAxis, laneAxis);

        var count = _items.Count;
        var placements = new GridAxisPlacement[count];
        var start = 0;
        var end = laneAxis.TrackCount;
        var largestAutoSpan = 0;
        for (var i = 0; i < count; i++)
        {
            var item = _items[i];
            var itemGrid = item.Style.Grid;
            item.WritingDirection = WritingDirection.FromStyle(item.Style);
            item.IsOrthogonal = item.WritingDirection.IsOrthogonalTo(_writingDirection);
            var placement = GetAutomaticSubgridSpan(item, _laneAxis) is { } subgridSpan
                ? GridAxisPlacement.Auto(subgridSpan)
                : lanesAreColumns ? GridPlacement.Resolve(itemGrid.ColumnStart, itemGrid.ColumnEnd, laneAxis) : GridPlacement.Resolve(itemGrid.RowStart, itemGrid.RowEnd, laneAxis);
            if (input is not null)
            {
                // (a subgridded axis has no implicit tracks: the areas of the items are clamped to the explicit grid)
                placement = placement.IsDefinite ? ClampToSubgrid(placement, input.Span) : GridAxisPlacement.Auto(Math.Min(placement.Span, input.Span));
            }

            placements[i] = placement;
            if (placement.IsDefinite)
            {
                start = Math.Min(start, placement.Start);
                end = Math.Max(end, placement.End);
            }
            else
            {
                largestAutoSpan = Math.Max(largestAutoSpan, placement.Span);
            }
        }

        // The items with an automatic position are placed from the start-most line of the implicit grid
        end = Math.Max(end, start + largestAutoSpan);
        var offset = -start;
        var laneTracks = CreateTracks(laneAxis, start, end);
        var stackTracks = CreateTracks(stackAxis, 0, 1);
        if (lanesAreColumns)
        {
            (_columns, _rows, _columnOffset, _rowOffset) = (laneTracks, stackTracks, offset, 0);
        }
        else
        {
            (_columns, _rows, _columnOffset, _rowOffset) = (stackTracks, laneTracks, 0, offset);
        }

        _estimatedColumnSpace = null;
        _estimatedRowSpace = null;
        for (var i = 0; i < count; i++)
        {
            var item = _items[i];
            var placement = placements[i];
            item.IsLanesAuto = !placement.IsDefinite;
            var (laneStart, laneEnd) = placement.IsDefinite ? (placement.Start + offset, placement.End + offset) : (0, Math.Min(placement.Span, laneTracks.Length));
            SetLanePosition(item, laneStart, laneEnd);
            if (lanesAreColumns)
            {
                item.RowStart = 0;
                item.RowEnd = 1;
            }
            else
            {
                item.ColumnStart = 0;
                item.ColumnEnd = 1;
            }

            InitializeItem(item);
            InitializeLanesItem(item);
        }

        CollapseLanesAutoFitTracks(laneTracks, laneAxis, offset);

        // The subgrids subgridded in the grid axis (https://drafts.csswg.org/css-grid-3/#subgrids): the instances that place their items
        // (an auto-placed subgrid is placed in its first possible tracks until the grid lanes placement places it)
        InitializeNestedSubgrids();
    }

    /// <summary>
    /// Returns the hypothetical sizes of the tracks of the track list of the grid axis whose sizing functions are not definite when
    /// it has an automatic repetition (https://drafts.csswg.org/css-grid-3/#repeat-intrinsic): "a hypothetical size is calculated for
    /// such tracks by initializing the track sizes and resolving intrinsic track sizes [...] with the following assumptions: Ignore
    /// explicit item placement. Do not collapse any tracks. Expand the repeated track listing to capture all possible automatic
    /// placements of each item, i.e. repeat the track listing 2 + (largest span - 2)/(number of tracks in repeat()) times, rounded
    /// down to a whole number." The size of a track is its growth limit (its max-content size for an <c>auto</c> track, as in the
    /// browsers). Returns null when the sizes are not needed.
    /// </summary>
    private GridIntrinsicRepeatSizes? ComputeIntrinsicRepeatSizes(double? size, double? maxSize, double? minSize)
    {
        var grid = _style.Grid;
        var trackList = _laneAxis == GridAxis.Columns ? grid.TemplateColumns : grid.TemplateRows;
        if (trackList.AutoRepeat is not { } repeat || !HasIndefiniteTrack(trackList))
            return null;

        var gap = GetGap(_laneAxis);
        var singleRepetition = GridExplicitAxis.Create(grid, _laneAxis, size, maxSize, minSize, gap);
        var largestSpan = 1;
        foreach (var item in _items)
        {
            var itemGrid = item.Style.Grid;
            var placement = _laneAxis == GridAxis.Columns ? GridPlacement.Resolve(itemGrid.ColumnStart, itemGrid.ColumnEnd, singleRepetition) : GridPlacement.Resolve(itemGrid.RowStart, itemGrid.RowEnd, singleRepetition);
            largestSpan = Math.Max(largestSpan, placement.Span);
        }

        var count = repeat.Tracks.Length;
        var repetitions = Math.Max(1, (int)Math.Floor(2 + ((largestSpan - 2) / (double)count)));
        var axis = GridExplicitAxis.Create(grid, _laneAxis, size, maxSize, minSize, gap, repetitions: repetitions);
        var tracks = CreateTracks(axis, 0, Math.Max(axis.TrackCount, largestSpan));
        var stackTracks = CreateTracks(GridExplicitAxis.CreateStackingAxis(), 0, 1);
        (_columns, _rows) = _laneAxis == GridAxis.Columns ? (tracks, stackTracks) : (stackTracks, tracks);
        foreach (var item in _items)
        {
            item.WritingDirection = WritingDirection.FromStyle(item.Style);
            item.IsOrthogonal = item.WritingDirection.IsOrthogonalTo(_writingDirection);
            item.IsLanesAuto = true;
            var itemGrid = item.Style.Grid;
            var span = Math.Min(tracks.Length, (_laneAxis == GridAxis.Columns ? GridPlacement.Resolve(itemGrid.ColumnStart, itemGrid.ColumnEnd, axis) : GridPlacement.Resolve(itemGrid.RowStart, itemGrid.RowEnd, axis)).Span);
            SetLanePosition(item, 0, span);
            if (_laneAxis == GridAxis.Columns)
            {
                item.RowStart = 0;
                item.RowEnd = 1;
            }
            else
            {
                item.ColumnStart = 0;
                item.ColumnEnd = 1;
            }

            InitializeItem(item);
            InitializeLanesItem(item);
        }

        InitializeNestedSubgrids();
        SizeTracks(_laneAxis, GridSizingMode.Layout, resolveIntrinsicSizesOnly: true);

        var fixedSizes = new List<double>();
        var repeatSizes = new double[count];
        var end = axis.AutoRepeatStart + axis.AutoRepeatCount;
        for (var i = 0; i < axis.TrackCount; i++)
        {
            var track = tracks[i];
            var trackSize = Math.Max(track.BaseSize, track.GrowthLimit);
            if (i >= axis.AutoRepeatStart && i < end)
            {
                var index = (i - axis.AutoRepeatStart) % count;
                repeatSizes[index] = Math.Max(repeatSizes[index], trackSize);
            }
            else
            {
                fixedSizes.Add(trackSize);
            }
        }

        return new GridIntrinsicRepeatSizes([.. fixedSizes], repeatSizes);

        static bool HasIndefiniteTrack(GridTrackList trackList)
        {
            foreach (var segment in trackList.Segments)
            {
                foreach (var track in segment.Tracks)
                {
                    var hasDefiniteMax = track.Max.Kind == GridBreadthKind.LengthPercentage && !track.IsFitContent;
                    if (!hasDefiniteMax && track.Min.Kind != GridBreadthKind.LengthPercentage)
                        return true;
                }
            }

            return false;
        }
    }

    private void SetLanePosition(GridItem item, int start, int end)
    {
        if (_laneAxis == GridAxis.Columns)
        {
            item.ColumnStart = start;
            item.ColumnEnd = end;
        }
        else
        {
            item.RowStart = start;
            item.RowEnd = end;
        }

        // (the extra margins of the items of a grid lanes subgrid depend on their tracks)
        if (_input is not null)
        {
            ApplySubgridExtraMargins(item);
        }
    }

    /// <summary>
    /// Resolves the alignment of an item in the stacking axis (https://drafts.csswg.org/css-grid-3/#stacking-axis-self-alignment):
    /// <c>normal</c> aligns with the side where the stacking starts (the items are not stretched, as in the browsers), "the baseline
    /// alignment values of the self-alignment properties on grid items are treated as their fallback alignment in this axis"
    /// (https://drafts.csswg.org/css-grid-3/#baseline-alignment). An item is stretched in the stacking axis when it fills the gap
    /// after it (see <see cref="ComputeLanesAlignmentContainers"/>).
    /// </summary>
    private void InitializeLanesItem(GridItem item)
    {
        var layout = item.Style;
        var axis = GetStackingAlignmentAxis();
        var value = _stackAxis == GridAxis.Columns ? GetJustifySelf(layout) : GetAlignSelf(layout);
        var alignment = BoxAlignment.GetBaselineFallback(BoxAlignment.ResolveSelfAlignment(value.Keyword, axis, item.WritingDirection, AxisAlignment.Start), axis);
        var safe = value.Overflow == OverflowAlignment.Safe;
        var (autoStart, autoEnd) = _stackAxis == GridAxis.Columns ? (item.AutoMarginColumnStart, item.AutoMarginColumnEnd) : (item.AutoMarginRowStart, item.AutoMarginRowEnd);
        item.StretchesStack = alignment == AxisAlignment.Stretch && !autoStart && !autoEnd && GetPreferredSize(item, _stackAxis).IsAuto;

        // (a subgrid is not subgridded in the stacking axis: its axis is a standalone axis)
        if (_stackAxis == GridAxis.Columns)
        {
            item.JustifySelf = alignment;
            item.JustifySafe = safe;
            item.StretchesColumns = false;
            item.FillsColumns = false;
            item.SubgridsColumns = false;
        }
        else
        {
            item.AlignSelf = alignment;
            item.AlignSafe = safe;
            item.StretchesRows = false;
            item.SubgridsRows = false;
        }
    }

    /// <summary>
    /// Returns the alignment of the static position of an absolutely positioned child in the stacking axis (from the start side of
    /// the axis in the writing mode of the container): <c>normal</c> aligns with the side where the stacking starts.
    /// </summary>
    private AxisAlignment GetLanesStaticAlignment(GridItem item, Alignment value)
    {
        var axis = GetStackingAlignmentAxis();
        var alignment = BoxAlignment.GetBaselineFallback(BoxAlignment.ResolveSelfAlignment(value.Keyword, axis, item.WritingDirection, AxisAlignment.Start), axis);
        if (!FillReverse)
            return alignment;

        return alignment switch
        {
            AxisAlignment.End => AxisAlignment.Start,
            AxisAlignment.Center => AxisAlignment.Center,
            _ => AxisAlignment.End,
        };
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-3/#repeat-auto-fit: "All tracks occupied by explicitly placed items are considered occupied.
    /// With the sum of the spans of all auto-placed items as N, all unoccupied tracks up to the Nth such track are considered
    /// occupied. All tracks produced by the auto-fit repetition and considered unoccupied by this heuristic are assumed “empty” and
    /// are collapsed. A collapsed grid track cannot accept placement of auto-placed items." As in the browsers, the first N tracks
    /// are considered occupied (WPT css/css-grid/grid-lanes/track-sizing/auto-repeat/column-auto-repeat-019.html).
    /// </summary>
    private void CollapseLanesAutoFitTracks(GridTrack[] tracks, GridExplicitAxis axis, int offset)
    {
        if (!axis.IsAutoFit || axis.AutoRepeatCount == 0)
            return;

        var occupied = new bool[tracks.Length];
        var autoSpans = 0;
        foreach (var item in _items)
        {
            if (item.IsLanesAuto)
            {
                autoSpans += item.GetSpan(_laneAxis);
                continue;
            }

            for (var i = item.GetStart(_laneAxis); i < item.GetEnd(_laneAxis); i++)
            {
                occupied[i] = true;
            }
        }

        for (var i = 0; i < occupied.Length && i < autoSpans; i++)
        {
            occupied[i] = true;
        }

        for (var i = 0; i < axis.AutoRepeatCount; i++)
        {
            var index = offset + axis.AutoRepeatStart + i;
            if (!occupied[index])
            {
                tracks[index].IsCollapsed = true;
            }
        }
    }

    /// <summary>
    /// Adds the sizing items of an item with an automatic position in the grid axis: "In the case of spanning items with an automatic
    /// grid position, they are assumed to be placed at every possible start position, and contribute accordingly" (the collapsed
    /// tracks of <c>auto-fit</c> cannot accept them).
    /// </summary>
    private void AddLanesSizingItems(GridItem item, GridAxis axis, List<GridSizingItem> output)
    {
        var tracks = GetTracks(axis);
        var span = item.GetSpan(axis);

        // https://drafts.csswg.org/css-grid-3/#subgrid-item-contribution: "When sizing the tracks of a grid lanes container, a subgrid
        // with an automatic grid position [...] Every item is placed into every possible parent grid track that could be spanned by
        // the subgrid (ignoring any explicit placement of the item)" (with the extra margins of its placement in the subgrid)
        if (item.Subgrid is { } subgrid && subgrid.IsSubgriddedIn(axis, out var subgridAxis, out var reversed))
        {
            var hoisted = new List<GridSizingItem>();
            subgrid.CollectHoistedItems(hoisted, subgridAxis, axis, 0, span, reversed, item, everyTrack: true);
            foreach (var hoistedItem in hoisted)
            {
                for (var start = 0; start + hoistedItem.Span <= tracks.Length; start++)
                {
                    if (!SpansCollapsedTrack(tracks, start, start + hoistedItem.Span))
                    {
                        output.Add(new GridSizingItem(hoistedItem.Item, hoistedItem.Owner, hoistedItem.OwnerAxis, axis, start, start + hoistedItem.Span)
                        {
                            ExtraStart = hoistedItem.ExtraStart,
                            ExtraEnd = hoistedItem.ExtraEnd,
                            IsPlaceholder = hoistedItem.IsPlaceholder,
                            IsHypothetical = hoistedItem.IsHypothetical,
                            Shim = hoistedItem.Shim,
                            IsReversed = hoistedItem.IsReversed,
                        });
                    }
                }
            }

            return;
        }

        for (var start = 0; start + span <= tracks.Length; start++)
        {
            if (SpansCollapsedTrack(tracks, start, start + span))
                continue;

            output.Add(new GridSizingItem(item, this, axis, axis, start, start + span)
            {
                ExtraStart = item.GetExtraStart(axis),
                ExtraEnd = item.GetExtraEnd(axis),
                Shim = item.LanesShims is { } shims && start < shims.Length ? shims[start] : item.GetShim(axis),
            });
        }
    }

    private static bool SpansCollapsedTrack(GridTrack[] tracks, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (tracks[i].IsCollapsed)
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Sizes the tracks of the grid axis (https://drafts.csswg.org/css-grid-3/#track-sizing) for the layout of the container.</summary>
    private void SizeLaneTracks()
    {
        _lanesPlaced = false;
        SizeSubgridStackingAxes();
        if (ApplyParentLaneTracks())
            return;

        if (_laneAxis == GridAxis.Columns)
        {
            _columnsSized = false;
            SizeTracks(GridAxis.Columns, GridSizingMode.Layout);
            _columnsSized = true;
            ComputeTrackPositions(GridAxis.Columns, _inlineSize);
        }
        else
        {
            SizeRows();
        }
    }

    /// <summary>
    /// Sets the tracks of the grid axis of a grid lanes subgrid from the positions given by its parent grid
    /// (<see cref="ApplyParentTracks"/>), or returns false: the container is not a subgrid, or its parent did not size the
    /// tracks yet (they are then sized as the tracks of a standalone container).
    /// </summary>
    private bool ApplyParentLaneTracks()
    {
        _columnsFromParent = false;
        _rowsFromParent = false;
        if (!ApplyParentTracks(_laneAxis))
            return false;

        if (_laneAxis == GridAxis.Columns)
        {
            _columnsFromParent = true;
        }
        else
        {
            _rowsFromParent = true;
            _contentBlockSize = _blockSize ?? GetTracksExtent(_rows);
        }

        return true;
    }

    /// <summary>
    /// Sizes the tracks of the subgrids in the stacking axis (their standalone axis: the subgrids are only subgridded in the grid
    /// axis) before the grid axis is sized, when the size of their content box is definite in that axis (a fixed preferred size):
    /// the contributions of their items to the grid axis depend on them (WPT
    /// css/css-grid/grid-lanes/subgrid/grid-subgridded-to-grid-lanes/row-subgrid-scroller-padding-001.html).
    /// </summary>
    private void SizeSubgridStackingAxes()
    {
        foreach (var item in _items)
        {
            if (item.Subgrid is not { } subgrid || subgrid._input is null)
                continue;

            var preferred = GetPreferredSize(item, _stackAxis);
            if (!preferred.IsLengthPercentage || preferred.Value.HasPercentage)
                continue;

            var horizontal = IsHorizontal(_stackAxis);
            var boxModel = item.Style.Box;
            var edges = FormattingContext.ResolvePadding(boxModel, _inlineSize ?? 0).Add(boxModel.BorderWidth);
            var paddingAndBorder = horizontal ? edges.HorizontalSum() : edges.VerticalSum();
            var value = preferred.Value.Resolve(0);
            var size = Math.Max(0, item.Style.BoxSizing == BoxSizing.BorderBox ? value - paddingAndBorder : value);
            var subgridAxis = GetParentAxis(item, _stackAxis);
            if (subgrid._isLanes)
            {
                // (the stacking axis of a grid lanes subgrid has no tracks)
                subgrid._parentStackingSize = size;
                continue;
            }

            subgrid.SizeTracks(subgridAxis, GridSizingMode.Layout, size);
            subgrid.ComputeTrackPositions(subgridAxis, size);
            subgrid.SetSized(subgridAxis);
            subgrid.PrepareNestedSubgrids(subgridAxis);
        }
    }

    /// <summary>Lays out the items of a grid lanes container, adds their fragments to <paramref name="builder"/> and returns the block size of the contents and their baselines.</summary>
    private ContentLayoutResult LayoutLanes(FragmentBuilder builder)
    {
        SizeLaneTracks();
        PlaceLanesItems(GridSizingMode.Layout);

        // The block size of the contents: the size of the rows, or the stacking range (https://drafts.csswg.org/css-grid-3/#grid-lanes-intrinsic-sizes)
        var contentBlockSize = _laneAxis == GridAxis.Rows ? _contentBlockSize : _stackingRange;
        if (_content.MeasuresIntrinsicBlockSize)
            return new ContentLayoutResult(contentBlockSize);

        _containerBlockSize = _blockSize ?? _content.ContainedBlockSize ?? Clamp(contentBlockSize, _minBlockSize, _maxBlockSize);
        if (_content.BlockSizeGrowsToContents)
        {
            // The container whose block size comes from its aspect ratio grows to contain its contents (its automatic minimum
            // size): they are aligned in that size (as the rows of a grid)
            _containerBlockSize = Math.Max(_containerBlockSize, contentBlockSize);
        }

        if (!(_laneAxis == GridAxis.Columns ? _columnsFromParent : _rowsFromParent))
        {
            ComputeTrackPositions(_laneAxis, _laneAxis == GridAxis.Columns ? _inlineSize : _containerBlockSize);
        }

        ComputeLanesAlignmentContainers();
        ComputeItemBaselines(final: true);
        PlaceItems(builder);
        RecordState();
        builder.GapGeometry = BuildLanesGapGeometry();

        var (firstBaseline, lastBaseline) = _stackAxis == GridAxis.Rows ? ComputeLanesBaselines() : ComputeContainerBaselines();
        var stackTrack = GetTracks(_stackAxis)[0];
        var inFlowEnd = _stackAxis == GridAxis.Rows ? stackTrack.End : GetGridEnd(GridAxis.Rows);

        // The tracks are included in the scrollable overflow of the container, as the ones of a grid (with track-reverse, the tracks
        // that overflow the start side, where the scroll origin is, WPT css/css-grid/grid-lanes/overflow/grid-lanes-overflow-reverse-baseline-001.html)
        var inFlowInlineEnd = _stackAxis == GridAxis.Columns ? stackTrack.End : GetGridEnd(GridAxis.Columns);
        var laneStart = TrackReverse ? Math.Min(0, GetGridStart(_laneAxis)) : 0;
        return new ContentLayoutResult(contentBlockSize, firstBaseline, lastBaseline)
        {
            InFlowBlockSize = Math.Max(0, inFlowEnd),
            InFlowInlineSize = Math.Max(0, inFlowInlineEnd),
            InFlowStart = _laneAxis == GridAxis.Columns ? new LogicalOffset(laneStart, 0) : new LogicalOffset(0, laneStart),
        };
    }

    /// <summary>
    /// Returns the intrinsic inline sizes of the content box of a grid lanes container: the size of its columns sized under a
    /// min-content and max-content constraint, or, when the inline axis is the stacking axis, "the size of the stacking range when the
    /// grid lanes container is sized under a max-content constraint (min-content constraint)" (https://drafts.csswg.org/css-grid-3/#grid-lanes-intrinsic-sizes).
    /// </summary>
    private IntrinsicSizes ComputeLanesIntrinsicInlineSizes()
    {
        double minContent;
        double maxContent;
        SizeSubgridStackingAxes();
        var fromParent = ApplyParentLaneTracks();
        if (_laneAxis == GridAxis.Columns)
        {
            if (_columns.Length == 0)
                return default;

            // (the columns of a grid lanes subgrid are the ones of its parent grid)
            if (fromParent)
                return new IntrinsicSizes(GetTracksExtent(_columns), GetTracksExtent(_columns));

            _columnsSized = false;
            SizeTracks(GridAxis.Columns, GridSizingMode.MinContent);
            minContent = GetGridSize(GridAxis.Columns);
            SizeTracks(GridAxis.Columns, GridSizingMode.MaxContent);
            maxContent = GetGridSize(GridAxis.Columns);
        }
        else
        {
            // The items are sized with their max-content contributions under both constraints, as in the browsers (WPT
            // css/css-grid/grid-lanes/item-placement/row-auto-placement-min-content.html): the items are not laid out in the size
            // of the container in the stacking axis, the min-content constraint does not make them narrower
            if (!fromParent)
            {
                SizeRows();
            }

            // When the block size of the container is indefinite, its inline size is found before its rows are sized: the layout of the
            // items in the rows (their inline axis when they are orthogonal) uses the space that they would have "if any row with a
            // definite max track sizing function had that size and all other rows were infinite" (https://drafts.csswg.org/css-grid-2/#algo-grid-sizing,
            // as in Chrome: WPT css/css-grid/grid-lanes/intrinsic-sizing/grid-lanes-intrinsic-sizing-rows-003-mix1.html; the rows of a
            // container with a definite block size are used, WPT grid-lanes-intrinsic-size-dynamic-block-size-row.html); the sizes of
            // the rows then only decide the dense packing
            var rowsSized = _rowsSized;
            _rowsSized = fromParent || _blockSize is not null;
            PlaceLanesItems(GridSizingMode.MaxContent);
            _rowsSized = rowsSized;
            minContent = _stackingRange;
            maxContent = _stackingRange;
        }

        return new IntrinsicSizes(minContent, Math.Max(minContent, maxContent));
    }

    /// <summary>Returns the automatic block size of a grid lanes container with size containment (sized without its items).</summary>
    private double ComputeLanesEmptyBlockSize()
    {
        if (_laneAxis == GridAxis.Columns)
            return 0;

        if (!ApplyParentLaneTracks())
        {
            SizeRows();
        }

        return _contentBlockSize;
    }

    /// <summary>
    /// The grid lanes layout and placement algorithm (https://drafts.csswg.org/css-grid-3/#grid-lanes-layout-algorithm): "For each of
    /// the tracks in the grid axis, keep a running position initialized to zero. Maintain also a auto-placement cursor, initially
    /// pointing to the first line. For each item in order-modified document order": its tracks (its definite position, or the
    /// position whose largest running position is the smallest, the ties within the tie threshold resolved by the cursor), its
    /// position (the largest running position of its tracks), its layout (or, under a min-content or max-content constraint in the
    /// stacking axis, its contribution), and the running positions of its tracks. With dense packing, an item is placed in the highest
    /// opening left before by the spanning items that fits it (https://drafts.csswg.org/css-grid-3/#grid-lanes-dense).
    /// </summary>
    private void PlaceLanesItems(GridSizingMode mode)
    {
        _lanesPlaced = false;
        if (mode == GridSizingMode.Layout && GetLanesBreaks() is { } breaks)
        {
            PlaceFragmentedLanesItems(breaks);
            return;
        }

        var tracks = GetTracks(_laneAxis);
        var count = tracks.Length;
        var gap = GetGap(_stackAxis);
        var tolerance = GetFlowTolerance();
        var trackReverse = TrackReverse;
        var dense = _style.Grid.LanesPackDense || _style.Grid.AutoFlow.IsDense;

        // The running positions and the openings, indexed in the order of the placement (the reverse order of the tracks with
        // track-reverse)
        var running = new double[count];
        var openings = dense ? new List<(double Start, double End)>?[count] : null;
        var cursor = 0;
        var rangeEnd = 0d;
        foreach (var item in _items)
        {
            var span = Math.Min(item.GetSpan(_laneAxis), count);
            int start;
            if (item.IsLanesAuto)
            {
                start = ChooseLanesPosition(running, tracks, span, tolerance, cursor);
            }
            else
            {
                start = trackReverse ? count - item.GetEnd(_laneAxis) : item.GetStart(_laneAxis);
            }

            var position = GetRunningPosition(running, start, span);
            SetLanePosition(item, trackReverse ? count - start - span : start, trackReverse ? count - start : start + span);
            PrepareLanesSubgrid(item);

            // "Set the running position of the spanned grid axis tracks to max_pos + outer size + grid-gap": the outer size and the gap
            // are floored at zero together, as in the browsers (an item with large negative margins does not count as a negative size,
            // its gap absorbs a part of its negative size)
            var outer = MeasureLanesItem(item, mode);
            var advance = Math.Max(0, outer + gap);
            item.LanesOuterSize = Math.Max(0, outer);
            if (openings is not null && FindLanesOpening(openings, running, tracks, item, start, span, advance, position, tolerance) is { } opening)
            {
                // "instead place it into the highest such space [...] Rewind the auto-placement cursor and the running position to their
                // values before this item's placement" (the tracks have the same used size: the layout of the item is the same; the
                // running positions of the tracks that were still open at that position move after the item)
                SetLanePosition(item, trackReverse ? count - opening.Start - span : opening.Start, trackReverse ? count - opening.Start : opening.Start + span);
                PrepareLanesSubgrid(item);
                item.LanesPosition = opening.Position;
                OccupyLanesOpening(openings, running, opening.Start, span, opening.Position, opening.Position + advance);
                rangeEnd = Math.Max(rangeEnd, opening.Position + item.LanesOuterSize);
                continue;
            }

            for (var i = start; i < start + span; i++)
            {
                if (openings is not null && running[i] < position - TieEpsilon)
                {
                    (openings[i] ??= []).Add((running[i], position));
                }

                running[i] = position + advance;
            }

            item.LanesPosition = position;
            rangeEnd = Math.Max(rangeEnd, position + item.LanesOuterSize);
            if (item.IsLanesAuto)
            {
                // "Update the auto-placement cursor to point to item's last line"
                cursor = start + span;
            }
        }

        _stackingRange = rangeEnd;
        _lanesPlaced = true;
    }

    /// <summary>Gives the tracks of the grid axis to an item that is a subgrid subgridded in it, once its tracks are known (its layout uses them).</summary>
    private void PrepareLanesSubgrid(GridItem item)
    {
        if (item.Subgrid is { } subgrid && subgrid.IsSubgriddedIn(_laneAxis, out var ownAxis, out var reversed))
        {
            ShareTracksWithSubgrid(item, subgrid, _laneAxis, ownAxis, reversed);
        }
    }

    /// <summary>
    /// Returns the tie threshold of the placement (https://drafts.csswg.org/css-grid-3/#placement-tolerance): <c>normal</c> is 1em,
    /// the percentages resolve against the content box size in the grid axis (its size from the tracks when it is indefinite, WPT
    /// css/css-grid/grid-lanes/item-placement/flow-tolerance/flow-tolerance-percentage-intrinsic-row-001.html), <c>infinite</c> ties
    /// every position.
    /// </summary>
    private double GetFlowTolerance()
    {
        var tolerance = _style.Grid.FlowTolerance;
        if (tolerance.IsInfinite)
            return double.PositiveInfinity;

        if (tolerance.IsNormal)
            return _style.FontSize;

        var basis = _laneAxis == GridAxis.Columns ? _inlineSize ?? GetGridSize(GridAxis.Columns) : _blockSize ?? Clamp(_contentBlockSize, _minBlockSize, _maxBlockSize);
        return Math.Max(0, tolerance.Length.Resolve(basis));
    }

    private static double GetRunningPosition(double[] running, int start, int span)
    {
        var position = 0d;
        for (var i = start; i < start + span; i++)
        {
            position = Math.Max(position, running[i]);
        }

        return position;
    }

    /// <summary>Returns the index of a track in the order of the tracks from its index in the order of the placement.</summary>
    private int ToTrackIndex(int index, int count) => TrackReverse ? count - 1 - index : index;

    /// <summary>
    /// Resolves the grid-axis position of an item with an automatic position: "find the largest running position of the grid axis
    /// tracks that the item would span if it were placed at this line, and call this position max_pos [...] Let possible lines be the
    /// line that resulted in the smallest max_pos, and all lines that result in a max_pos within the tie threshold of this max_pos.
    /// Choose the first line in possible lines greater than or equal to the auto-placement cursor as the item's position in the grid
    /// axis; or if there are none such, choose the first one."
    /// </summary>
    private int ChooseLanesPosition(double[] running, GridTrack[] tracks, int span, double tolerance, int cursor)
    {
        var count = running.Length;
        var smallest = double.PositiveInfinity;
        for (var start = 0; start + span <= count; start++)
        {
            if (IsCollapsed(start))
                continue;

            smallest = Math.Min(smallest, GetRunningPosition(running, start, span));
        }

        if (double.IsPositiveInfinity(smallest))
            return 0;

        var first = -1;
        for (var start = 0; start + span <= count; start++)
        {
            if (IsCollapsed(start) || GetRunningPosition(running, start, span) > smallest + tolerance + TieEpsilon)
                continue;

            if (start >= cursor)
                return start;

            if (first < 0)
            {
                first = start;
            }
        }

        return Math.Max(0, first);

        bool IsCollapsed(int start)
        {
            for (var i = start; i < start + span; i++)
            {
                if (tracks[ToTrackIndex(i, count)].IsCollapsed)
                    return true;
            }

            return false;
        }
    }

    /// <summary>The precision of the comparison of the running positions (a fraction of the layout unit of the browsers, 1/64 px).</summary>
    private const double TieEpsilon = 1d / 128;

    /// <summary>
    /// https://drafts.csswg.org/css-grid-3/#grid-lanes-dense: "If the grid lanes container uses dense packing, and there exists skipped
    /// spaces in the layout (e.g. due to spanning items) into which the item, as it is sized now, could have fit if it were placed
    /// earlier, and where the spanned tracks have the same total used size as the tracks into which it is currently placed, then
    /// instead place it into the highest such space. If there are multiple valid spaces within the tie threshold of the highest space,
    /// place it in the start-most of them." An item with a definite position only fits the openings of its tracks; an opening is only
    /// used when it is higher than the normal position of the item. Returns the opening (the first track in the order of the
    /// placement and the position), or null.
    /// </summary>
    private (int Start, double Position)? FindLanesOpening(List<(double Start, double End)>?[] openings, double[] running, GridTrack[] tracks, GridItem item, int normalStart, int span, double size, double normalPosition, double tolerance)
    {
        var count = openings.Length;
        var tracksSize = GetLanesSpanSize(tracks, normalStart, span);
        var first = item.IsLanesAuto ? 0 : normalStart;
        var last = item.IsLanesAuto ? count - span : normalStart;
        var candidates = new List<(int Start, double Position)>();
        var highest = double.PositiveInfinity;
        for (var start = first; start <= last; start++)
        {
            if (Math.Abs(GetLanesSpanSize(tracks, start, span) - tracksSize) > TieEpsilon || GetHighestOpening(openings, running, start, span, size) is not { } position)
                continue;

            candidates.Add((start, position));
            highest = Math.Min(highest, position);
        }

        // (the normal position of the item is one of the candidates: the item is moved when the chosen space is higher, or as high and
        // in an opening, WPT css/css-grid/grid-lanes/tentative/item-placement/row-reverse-dense-packing-multi-span-001.html)
        foreach (var candidate in candidates)
        {
            if (candidate.Position > highest + tolerance + TieEpsilon)
                continue;

            if (candidate.Position < normalPosition - TieEpsilon)
                return candidate;

            return candidate.Position <= normalPosition + TieEpsilon && IsInOpening(candidate.Start, candidate.Position) ? candidate : null;
        }

        return null;

        bool IsInOpening(int start, double position)
        {
            for (var i = start; i < start + span; i++)
            {
                if (running[i] > position + TieEpsilon)
                    return true;
            }

            return false;
        }
    }

    /// <summary>Returns the size of the tracks spanned by an item (with the gutters between them), from the first track in the order of the placement.</summary>
    private double GetLanesSpanSize(GridTrack[] tracks, int start, int span)
    {
        var count = tracks.Length;
        var size = 0d;
        for (var i = start; i < start + span; i++)
        {
            var track = tracks[ToTrackIndex(i, count)];
            if (track.IsCollapsed)
                return double.NaN;

            size += track.BaseSize;
        }

        var (low, high) = TrackReverse ? (count - start - span, count - start) : (start, start + span);
        return size + GetGuttersWithin(_laneAxis, low, high);
    }

    /// <summary>
    /// Returns the highest position where an item of the given size (its outer size and its gap) fits in all the tracks it would span,
    /// or null: in an opening of each track, or after its running position (the tracks that are still open, WPT
    /// css/css-grid/grid-lanes/item-placement/dense-packing/column-dense-packing-multi-span-010.html).
    /// </summary>
    private static double? GetHighestOpening(List<(double Start, double End)>?[] openings, double[] running, int start, int span, double size)
    {
        // The candidate positions are the starts of the openings and the running positions
        double? best = null;
        for (var i = start; i < start + span; i++)
        {
            Consider(running[i]);
            if (openings[i] is { } list)
            {
                foreach (var (candidate, _) in list)
                {
                    Consider(candidate);
                }
            }
        }

        return best;

        void Consider(double candidate)
        {
            if ((best is null || candidate < best) && FitsAt(candidate))
            {
                best = candidate;
            }
        }

        bool FitsAt(double position)
        {
            for (var i = start; i < start + span; i++)
            {
                if (running[i] <= position + TieEpsilon)
                    continue;

                var fits = false;
                if (openings[i] is { } list)
                {
                    foreach (var (openingStart, openingEnd) in list)
                    {
                        if (openingStart <= position + TieEpsilon && position + size <= openingEnd + TieEpsilon)
                        {
                            fits = true;
                            break;
                        }
                    }
                }

                if (!fits)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Removes the space occupied by an item placed in an opening from the openings of its tracks (the space before and after it
    /// remains open); in a track where it is placed after the running position, the running position moves after it.
    /// </summary>
    private static void OccupyLanesOpening(List<(double Start, double End)>?[] openings, double[] running, int start, int span, double position, double end)
    {
        for (var i = start; i < start + span; i++)
        {
            if (running[i] <= position + TieEpsilon)
            {
                if (position > running[i] + TieEpsilon)
                {
                    (openings[i] ??= []).Add((running[i], position));
                }

                running[i] = Math.Max(running[i], end);
                continue;
            }

            var list = openings[i]!;
            for (var j = 0; j < list.Count; j++)
            {
                var (openingStart, openingEnd) = list[j];
                if (openingStart > position + TieEpsilon || end > openingEnd + TieEpsilon)
                    continue;

                list.RemoveAt(j);
                if (end < openingEnd - TieEpsilon)
                {
                    list.Insert(j, (end, openingEnd));
                }

                if (position > openingStart + TieEpsilon)
                {
                    list.Insert(j, (openingStart, position));
                }

                break;
            }
        }
    }

    /// <summary>
    /// Returns the outer size of an item in the stacking axis (negative with large negative margins): the size of its layout in its containing block ("formed by its grid area in the grid axis and the grid
    /// container's content box in the stacking axis", https://drafts.csswg.org/css-grid-3/#grid-lanes-containing-block), or its
    /// min-content or max-content contribution when the container is sized under such a constraint in the stacking axis.
    /// </summary>
    private double MeasureLanesItem(GridItem item, GridSizingMode mode)
    {
        if (mode != GridSizingMode.Layout)
        {
            var contributions = GetContributions(item, _stackAxis);
            return mode == GridSizingMode.MinContent ? contributions.MinContent : contributions.MaxContent;
        }

        var inlineArea = GetAreaSize(item, GridAxis.Columns);
        var fragment = LayoutItem(item, inlineArea, GetAreaSize(item, GridAxis.Rows));
        var size = _writingDirection.ToLogical(fragment.Size);
        var margins = GetMargins(item, inlineArea ?? 0);
        return _stackAxis == GridAxis.Columns ? size.Inline + margins.InlineSum() : size.Block + margins.BlockSum();
    }

    /// <summary>Returns the size of the containing block of the items in the stacking axis: the content box of the container, or null when it is indefinite.</summary>
    private double? GetStackingAreaSize() => (_stackAxis == GridAxis.Columns ? _inlineSize : _percentageBlockSize) ?? _parentStackingSize;

    // ------------------------------------------------------------------------------------------------------------------
    // Alignment
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Aligns the stacking range in the content box (https://drafts.csswg.org/css-grid-3/#stacking-axis-content-distribution: "the
    /// alignment subject is the stacking range"; normal, stretch and the distributions align it with the side where the stacking
    /// starts) and computes the alignment containers of the items in the stacking axis (https://drafts.csswg.org/css-grid-3/#stacking-axis-self-alignment):
    /// "the self-alignment properties only apply to items that are adjacent to a “gap” in the layout, i.e. placed in their grid
    /// track(s) either immediately before a spanning item or as the last item. The alignment subject is the item's margin box, and
    /// the alignment container is that box plus the adjacent “gap”. For the last item in a track, the alignment container extends to
    /// the lowest bottom outer edge among all the last items in all tracks" (or to the end of the content box when it is larger, as
    /// in the browsers). The stretched items are laid out again with the size of their alignment container.
    /// </summary>
    private void ComputeLanesAlignmentContainers()
    {
        var containerSize = _stackAxis == GridAxis.Columns ? _inlineSize ?? _stackingRange : _containerBlockSize;
        _stackingContainerSize = containerSize;
        var alignment = _stackAxis == GridAxis.Columns ? _style.Alignment.JustifyContent : _style.Alignment.AlignContent;
        var axis = GetStackingAlignmentAxis();
        var resolved = BoxAlignment.ResolveContentAlignment(alignment.Keyword, axis, AxisAlignment.Start);
        var (offset, _) = BoxAlignment.Distribute(resolved, containerSize - _stackingRange, 1, alignment.Overflow == OverflowAlignment.Safe);

        // The track of the stacking axis is the stacking range (the lines of the absolutely positioned boxes), in the logical coordinates
        var stackTrack = GetTracks(_stackAxis)[0];
        stackTrack.BaseSize = _stackingRange;
        stackTrack.Position = FillReverse ? containerSize - offset - _stackingRange : offset;

        // The next item in the tracks of each item: the items of each track sorted by position
        var count = GetTracks(_laneAxis).Length;
        var following = new double[_items.Count];
        Array.Fill(following, double.PositiveInfinity);
        var trackItems = new List<int>();
        for (var track = 0; track < count; track++)
        {
            trackItems.Clear();
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.GetStart(_laneAxis) <= track && track < item.GetEnd(_laneAxis))
                {
                    trackItems.Add(i);
                }
            }

            trackItems.Sort((a, b) => _items[a].LanesPosition.CompareTo(_items[b].LanesPosition));
            for (var j = 0; j + 1 < trackItems.Count; j++)
            {
                var index = trackItems[j];
                following[index] = Math.Min(following[index], _items[trackItems[j + 1]].LanesPosition);
            }
        }

        var gap = GetGap(_stackAxis);
        var stackEnd = Math.Max(offset + _stackingRange, containerSize);
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var start = offset + item.LanesPosition;
            var end = double.IsPositiveInfinity(following[i]) ? stackEnd : offset + following[i] - gap;
            item.LanesAreaStart = start;
            item.LanesAreaSize = Math.Max(item.LanesOuterSize, end - start);
            if (item.StretchesStack && item.LanesAreaSize > item.LanesOuterSize + Epsilon)
            {
                StretchLanesItem(item, item.LanesAreaSize);
            }
        }
    }

    /// <summary>Lays out an item stretched in the stacking axis: its border-box size is its alignment container minus its margins, clamped by its min and max sizes.</summary>
    private void StretchLanesItem(GridItem item, double areaSize)
    {
        var inlineArea = GetAreaSize(item, GridAxis.Columns);
        var blockArea = GetAreaSize(item, GridAxis.Rows);
        var margins = GetMargins(item, inlineArea ?? 0);
        var space = CreateItemSpace(item, inlineArea, blockArea);
        if (_stackAxis == GridAxis.Columns)
        {
            space = space with { FixedInlineSize = CreateItemSizing(item, GridAxis.Columns, areaSize, blockArea).Clamp(areaSize - margins.InlineSum()) };
        }
        else
        {
            space = space with { FixedBlockSize = CreateItemSizing(item, GridAxis.Rows, areaSize, inlineArea).Clamp(areaSize - margins.BlockSum()) };
        }

        if (item.Fragment is not null && item.Space == space)
            return;

        item.Fragment = _context.LayoutBox(item.Box, space);
        item.Space = space;
    }

    /// <summary>
    /// Aligns an item in its alignment container of the stacking axis (see <see cref="ComputeLanesAlignmentContainers"/>): the
    /// <c>auto</c> margins absorb the positive free space, else the item is aligned with its self-alignment. Returns the position
    /// of the border box from the start edge of the content box (in the logical coordinates of the grid) and the used margins.
    /// </summary>
    private (double Position, double StartMargin, double EndMargin) AlignInStack(GridItem item, double size, double startMargin, double endMargin)
    {
        var reverse = FillReverse;
        var (autoStart, autoEnd) = _stackAxis == GridAxis.Columns ? (item.AutoMarginColumnStart, item.AutoMarginColumnEnd) : (item.AutoMarginRowStart, item.AutoMarginRowEnd);

        // The margins in the direction of the stacking
        var (flowStartMargin, flowEndMargin) = reverse ? (endMargin, startMargin) : (startMargin, endMargin);
        var (flowAutoStart, flowAutoEnd) = reverse ? (autoEnd, autoStart) : (autoStart, autoEnd);
        var freeSpace = item.LanesAreaSize - size - flowStartMargin - flowEndMargin;
        double offset;
        if (flowAutoStart || flowAutoEnd)
        {
            if (freeSpace > 0)
            {
                if (flowAutoStart && flowAutoEnd)
                {
                    flowStartMargin += freeSpace / 2;
                    flowEndMargin += freeSpace / 2;
                }
                else if (flowAutoStart)
                {
                    flowStartMargin += freeSpace;
                }
                else
                {
                    flowEndMargin += freeSpace;
                }
            }

            offset = 0;
        }
        else
        {
            var safe = _stackAxis == GridAxis.Columns ? item.JustifySafe : item.AlignSafe;
            offset = BoxAlignment.GetOffset(item.GetAlignment(_stackAxis), freeSpace, safe);
        }

        var flowPosition = item.LanesAreaStart + offset + flowStartMargin;
        var position = reverse ? _stackingContainerSize - flowPosition - size : flowPosition;
        return reverse ? (position, flowEndMargin, flowStartMargin) : (position, flowStartMargin, flowEndMargin);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-3/#baseline-alignment: the baselines of a grid lanes container whose block axis is the
    /// stacking axis: "The first baseline set of the grid container in this axis is generated from the highest alignment baseline
    /// among the grid items placed first in each track, and the last baseline set from the lowest alignment baseline among the grid
    /// items placed last in each track."
    /// </summary>
    private (double? First, double? Last) ComputeLanesBaselines()
    {
        if (_items.Count == 0)
            return (null, null);

        var count = GetTracks(_laneAxis).Length;
        double? first = null;
        double? last = null;
        for (var track = 0; track < count; track++)
        {
            // (the items without a baseline are ignored: the baseline of a track is the one of its first, or last, item that has
            // one, as in the browsers)
            GridItem? firstItem = null;
            GridItem? lastItem = null;
            double firstItemBaseline = 0;
            double lastItemBaseline = 0;
            foreach (var item in _items)
            {
                if (item.GetStart(_laneAxis) > track || track >= item.GetEnd(_laneAxis) || item.Fragment is not { } fragment)
                    continue;

                if ((firstItem is null || item.LanesPosition < firstItem.LanesPosition) && GetBlockAxisBaseline(item, fragment, last: false) is { } firstBaseline)
                {
                    firstItem = item;
                    firstItemBaseline = firstBaseline;
                }

                if ((lastItem is null || item.LanesPosition >= lastItem.LanesPosition) && GetBlockAxisBaseline(item, fragment, last: true) is { } lastBaseline)
                {
                    lastItem = item;
                    lastItemBaseline = lastBaseline;
                }
            }

            if (firstItem is not null)
            {
                var baseline = firstItem.Offset.Block + firstItemBaseline;
                first = first is { } value ? Math.Min(value, baseline) : baseline;
            }

            if (lastItem is not null)
            {
                var baseline = lastItem.Offset.Block + lastItemBaseline;
                last = last is { } value ? Math.Max(value, baseline) : baseline;
            }
        }

        return (first, last);
    }
}
