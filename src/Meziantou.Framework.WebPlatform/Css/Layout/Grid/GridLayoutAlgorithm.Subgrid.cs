namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The subgrids (https://drafts.csswg.org/css-grid-2/#subgrids): a grid item that is a grid container whose
/// <c>grid-template-columns</c>/<c>grid-template-rows</c> is <c>subgrid</c> takes the tracks it spans in its parent grid as its
/// explicit grid in that axis, and its items participate in the sizing of the tracks of the parent.
/// </summary>
/// <remarks>
/// <para>
/// When a grid places its items, it gives each subgrid its input (<see cref="SubgridInput"/>, in the state of its box: the span,
/// the inherited line names and areas, the extra margins of the items at its edges) and creates an instance of the algorithm for
/// it that places its items (<see cref="GridItem.Subgrid"/>, recursively). When the grid sizes the tracks of an axis, the
/// subgrids subgridded in that axis are treated as empty and their items (recursively) are sizing items of the grid with their
/// span mapped to its tracks (<see cref="CollectHoistedItems"/>). Once the tracks of an axis are sized, the instances of the
/// subgrids share them (for the areas of their items in the other axis), and the inputs get their positions in the content box
/// of each subgrid (<see cref="PrepareNestedSubgrids"/>): the layout of a subgrid (a new instance of the algorithm, created by
/// its formatting context) uses them instead of sizing its subgridded axes.
/// </para>
/// <para>
/// Not done: the baseline alignment of the items of a subgrid with the items of its parent, the re-sizing of an axis whose
/// min-content contributions changed for the items of the subgrids.
/// </para>
/// </remarks>
internal sealed partial class GridLayoutAlgorithm
{
    /// <summary>The input of the container when it is a subgrid, or null (a standalone grid).</summary>
    private SubgridInput? _input;

    /// <summary>Whether the tracks of an axis are the subgridded tracks of the input (they are not sized by the algorithm).</summary>
    private bool _columnsFromParent;
    private bool _rowsFromParent;

    /// <summary>Returns the input of the subgrid from the state of its box, given by its parent in the current layout pass, or null.</summary>
    private SubgridInput? GetSubgridInput()
        => _box.LayoutState is GridContainerState { Subgrid: { } input } && input.Pass == _context.Pass ? input : null;

    /// <summary>
    /// Returns whether an item may be a subgrid: a grid container whose track list is <c>subgrid</c> in an axis, unless "the grid
    /// container is otherwise forced to establish an independent formatting context (for example, due to layout containment or
    /// absolute positioning)" (https://drafts.csswg.org/css-grid-2/#subgrid-listing).
    /// </summary>
    private static bool IsSubgridCandidate(GridItem item)
    {
        var box = item.Box;
        if (box.Kind != ContainerBoxKind.Normal || !box.Display.IsGridContainer)
            return false;

        return (HasSubgridTrackList(item, GridAxis.Columns) || HasSubgridTrackList(item, GridAxis.Rows)) && !EstablishesIndependentGrid(box);
    }

    /// <summary>
    /// Returns whether a grid container is "forced to establish an independent formatting context" (https://drafts.csswg.org/css-grid-2/#subgrid-listing),
    /// so that it cannot be a subgrid: the layout and paint containments and the size containers.
    /// </summary>
    private static bool EstablishesIndependentGrid(ContainerBox box)
        => UsedContainment.HasLayoutContainment(box) || UsedContainment.HasPaintContainment(box)
            || (box.Style.Layout.ContainerType & (ContainerType.Size | ContainerType.InlineSize)) != ContainerType.Normal;

    /// <summary>
    /// Returns whether the track list of an axis of an item (its own axis) is <c>subgrid</c>. A grid lanes container is only a
    /// subgrid in its grid axis (https://drafts.csswg.org/css-grid-3/#subgrids: a grid lanes subgrid; "A grid lanes container that
    /// is not subgridded in its grid axis establishes an independent formatting context"): its stacking axis has no tracks.
    /// </summary>
    private static bool HasSubgridTrackList(GridItem item, GridAxis itemAxis)
    {
        var grid = item.Style.Grid;
        if (!(itemAxis == GridAxis.Columns ? grid.TemplateColumns : grid.TemplateRows).IsSubgrid)
            return false;

        return item.Box.Display.Inside != DisplayInside.GridLanes || GetLaneAxis(item.Style) == itemAxis;
    }

    /// <summary>Returns the axis of the parent grid that corresponds to an axis of an item (the axes are swapped for an orthogonal item).</summary>
    private static GridAxis GetParentAxis(GridItem item, GridAxis itemAxis) => (itemAxis == GridAxis.Columns) != item.IsOrthogonal ? GridAxis.Columns : GridAxis.Rows;

    /// <summary>Returns whether an item is a subgrid subgridded in an axis of the grid.</summary>
    private static bool IsSubgriddedItem(GridItem item, GridAxis axis)
    {
        if (!IsSubgridCandidate(item))
            return false;

        return HasSubgridTrackList(item, GetParentAxis(item, axis));
    }

    /// <summary>Returns whether the subgrid (this instance) is subgridded in an axis of its parent, and its own corresponding axis.</summary>
    internal bool IsSubgriddedIn(GridAxis parentAxis, out GridAxis ownAxis, out bool reversed)
    {
        if (_input?.Columns is { } columns && columns.ParentAxis == parentAxis)
        {
            ownAxis = GridAxis.Columns;
            reversed = columns.IsReversed;
            return true;
        }

        if (_input?.Rows is { } rows && rows.ParentAxis == parentAxis)
        {
            ownAxis = GridAxis.Rows;
            reversed = rows.IsReversed;
            return true;
        }

        ownAxis = default;
        reversed = false;
        return false;
    }

    /// <summary>
    /// Adds the items of the subgrid (this instance) to the sizing items of an axis of an ancestor grid: "any grid item which is
    /// subgridded in the grid container's inline axis is treated as empty and its grid items (the grandchildren) are treated as
    /// direct children of the grid container (their grandparent). This introspection is recursive." The spans are mapped to the
    /// tracks of the ancestor; the extra margins of the items are the ones of the subgrids (https://drafts.csswg.org/css-grid-2/#subgrid-margins).
    /// </summary>
    /// <param name="output">The sizing items of the ancestor.</param>
    /// <param name="ownAxis">The subgridded axis of the subgrid.</param>
    /// <param name="targetAxis">The sized axis of the ancestor.</param>
    /// <param name="parentStart">The first track of the subgrid in the ancestor.</param>
    /// <param name="parentEnd">The track after the last track of the subgrid in the ancestor.</param>
    /// <param name="reversed">Whether the lines of the subgrid go in the opposite direction of the lines of the ancestor.</param>
    /// <param name="subgridItem">The item of the subgrid in its parent grid.</param>
    /// <param name="everyTrack">Whether every track of the subgrid contributes its extra margins (see <see cref="AddEdgePlaceholders"/>).</param>
    internal void CollectHoistedItems(List<GridSizingItem> output, GridAxis ownAxis, GridAxis targetAxis, int parentStart, int parentEnd, bool reversed, GridItem subgridItem, bool everyTrack = false)
    {
        AddEdgePlaceholders(output, ownAxis, targetAxis, parentStart, parentEnd, reversed, subgridItem, everyTrack);
        foreach (var item in _items)
        {
            if (_isLanes && item.IsLanesAuto)
            {
                AddLanesHoistedItems(output, item, ownAxis, targetAxis, parentStart, parentEnd, reversed, everyTrack);
                continue;
            }

            var start = item.GetStart(ownAxis);
            var end = item.GetEnd(ownAxis);
            var (mappedStart, mappedEnd) = reversed ? (parentEnd - end, parentEnd - start) : (parentStart + start, parentStart + end);
            if (item.Subgrid is { } nested && nested.IsSubgriddedIn(ownAxis, out var nestedAxis, out var nestedReversed))
            {
                nested.CollectHoistedItems(output, nestedAxis, targetAxis, mappedStart, mappedEnd, reversed != nestedReversed, item, everyTrack);
                continue;
            }

            var extraStart = item.GetExtraStart(ownAxis);
            var extraEnd = item.GetExtraEnd(ownAxis);
            output.Add(new GridSizingItem(item, this, ownAxis, targetAxis, mappedStart, mappedEnd)
            {
                ExtraStart = reversed ? extraEnd : extraStart,
                ExtraEnd = reversed ? extraStart : extraEnd,
                Shim = reversed ? 0 : item.GetShim(ownAxis),
                ShimVariation = reversed ? default : item.GetShimVariation(ownAxis),
                IsReversed = reversed,
            });
        }
    }

    /// <summary>
    /// Adds an item of a grid lanes subgrid (this instance) that has an automatic position in its grid axis to the sizing items of
    /// an ancestor grid (https://drafts.csswg.org/css-grid-3/#subgrid-item-contribution): "Any such item is placed into every
    /// possible parent grid track that could be spanned by the grid lanes subgrid. [...] Any such item receives the largest
    /// margin/border/padding contribution of each edge at which it could hypothetically be placed. If the item spans the entire
    /// subgrid, it receives both." The extra margins of the position with the largest ones (an edge of the subgrid and half the
    /// difference of the gutters on the other side, see <see cref="ApplySubgridExtraMargins"/>) are used in every position. An
    /// item that is itself a subgrid contributes its items in every position, with the extra margins of its first one.
    /// </summary>
    private void AddLanesHoistedItems(List<GridSizingItem> output, GridItem item, GridAxis ownAxis, GridAxis targetAxis, int parentStart, int parentEnd, bool reversed, bool everyTrack)
    {
        var count = parentEnd - parentStart;
        var span = Math.Min(item.GetSpan(ownAxis), count);
        var nested = item.Subgrid;
        var nestedAxis = default(GridAxis);
        var nestedReversed = false;
        if (nested is not null && !nested.IsSubgriddedIn(ownAxis, out nestedAxis, out nestedReversed))
        {
            nested = null;
        }

        var (extraStart, extraEnd) = GetLargestLanesExtraMargins(ownAxis, span, count);
        for (var start = 0; start + span <= count; start++)
        {
            var end = start + span;
            var (mappedStart, mappedEnd) = reversed ? (parentEnd - end, parentEnd - start) : (parentStart + start, parentStart + end);
            if (nested is not null)
            {
                nested.CollectHoistedItems(output, nestedAxis, targetAxis, mappedStart, mappedEnd, reversed != nestedReversed, item, everyTrack);
                continue;
            }

            output.Add(new GridSizingItem(item, this, ownAxis, targetAxis, mappedStart, mappedEnd)
            {
                ExtraStart = reversed ? extraEnd : extraStart,
                ExtraEnd = reversed ? extraStart : extraEnd,
                IsReversed = reversed,
                IsHypothetical = true,
            });
        }
    }

    /// <summary>
    /// Returns the extra margins of an item of a grid lanes subgrid in the position of its grid axis where they are the largest
    /// (see <see cref="AddLanesHoistedItems"/>): at the start edge or at the end edge of the subgrid, or both for an item that
    /// spans the entire subgrid.
    /// </summary>
    private (double Start, double End) GetLargestLanesExtraMargins(GridAxis ownAxis, int span, int count)
    {
        if (_input?.Get(ownAxis) is not { } input)
            return (0, 0);

        if (span >= count)
            return (input.EdgeStart, input.EdgeEnd);

        var half = (GetOwnGap(ownAxis, input) - input.ParentGap) / 2;
        return input.EdgeStart >= input.EdgeEnd ? (input.EdgeStart, half) : (half, input.EdgeEnd);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#subgrid-edge-placeholders: the margins, borders and paddings of the edges of a subgrid
    /// take space in its edge tracks, whatever its items: a hypothetical empty item is contributed to the track sizing algorithm
    /// in the track of each edge, with the margin, border and padding of that edge (both edges for a subgrid of one track).
    /// </summary>
    /// <remarks>
    /// The specification contributes one hypothetical item "spanning the entire subgrid" for an empty subgrid, and derives the
    /// hypothetical items of a non-empty subgrid from "the set of items spanning into the occupied track closest to that edge"
    /// (their sizes inflated by the edge, their span extended to it), which also enlarges the occupied track. The browsers only
    /// give the space of each edge to the edge track (WPT css/css-grid/subgrid/grid-gap-002.html; an empty subgrid: WPT
    /// css/css-grid/subgrid/orthogonal-writing-mode-002.html), also when it has items: an item whose extra margin on the side of
    /// a gutter is negative (a subgrid with smaller gutters than its parent) can contribute less than the edge (WPT
    /// css/css-grid/subgrid/item-percentage-height-001.html).
    /// </remarks>
    private void AddEdgePlaceholders(List<GridSizingItem> output, GridAxis ownAxis, GridAxis targetAxis, int parentStart, int parentEnd, bool reversed, GridItem subgridItem, bool everyTrack)
    {
        if (_input?.Get(ownAxis) is not { } input)
            return;

        var span = parentEnd - parentStart;
        if (everyTrack)
        {
            // The subgrid has an automatic position in a grid lanes container (or is in such a subgrid): each of its tracks
            // contributes the extra margins an item would have in it, the edges of the subgrid and half the difference of the
            // gutters, also when it is empty (as in Blink, WPT css/css-grid/grid-lanes/subgrid/grid-subgridded-to-grid-lanes/gap/column-subgrid-grid-gap-015.html)
            var half = (GetOwnGap(ownAxis, input) - input.ParentGap) / 2;
            if (input.EdgeStart == 0 && input.EdgeEnd == 0 && half <= 0)
                return;

            for (var i = 0; i < span; i++)
            {
                Add(i, i + 1, i == 0 ? input.EdgeStart : half, i == span - 1 ? input.EdgeEnd : half);
            }

            return;
        }

        if (input.EdgeStart == 0 && input.EdgeEnd == 0)
            return;

        if (span == 1)
        {
            Add(0, 1, input.EdgeStart, input.EdgeEnd);
            return;
        }

        if (input.EdgeStart != 0)
        {
            Add(0, 1, input.EdgeStart, 0);
        }

        if (input.EdgeEnd != 0)
        {
            Add(span - 1, span, 0, input.EdgeEnd);
        }

        void Add(int start, int end, double extraStart, double extraEnd)
        {
            var (mappedStart, mappedEnd) = reversed ? (parentEnd - end, parentEnd - start) : (parentStart + start, parentStart + end);
            output.Add(new GridSizingItem(subgridItem, this, ownAxis, targetAxis, mappedStart, mappedEnd)
            {
                ExtraStart = reversed ? extraEnd : extraStart,
                ExtraEnd = reversed ? extraStart : extraEnd,
                IsPlaceholder = true,
            });
        }
    }

    /// <summary>
    /// Returns the span of a subgrid in an axis when its grid span is automatic: "its used grid span is taken from the number of
    /// explicit tracks specified for that axis by its grid-template-* properties, floored at one" (the line names of its
    /// <c>&lt;line-name-list&gt;</c>, https://drafts.csswg.org/css-grid-2/#subgrid-span), or null when the span is not automatic.
    /// </summary>
    private static int? GetAutomaticSubgridSpan(GridItem item, GridAxis parentAxis)
    {
        if (!IsSubgriddedItem(item, parentAxis))
            return null;

        var grid = item.Style.Grid;
        var itemAxis = GetParentAxis(item, parentAxis);
        var (start, end) = parentAxis == GridAxis.Columns ? (grid.ColumnStart, grid.ColumnEnd) : (grid.RowStart, grid.RowEnd);
        if (!start.IsAuto || !end.IsAuto)
            return null;

        var trackList = itemAxis == GridAxis.Columns ? grid.TemplateColumns : grid.TemplateRows;
        var lines = 0;
        foreach (var segment in trackList.Segments)
        {
            if (segment.Repeat != GridRepeatKind.AutoFill)
            {
                lines += segment.LineNames.Length * segment.Count;
            }
        }

        return Math.Clamp(lines - 1, 1, GridExplicitAxis.MaxLine);
    }

    /// <summary>
    /// "The subgrid does not have any implicit grid tracks in the subgridded dimension(s) [...] each grid item's grid area is clamped
    /// to the subgrid's explicit grid" (https://drafts.csswg.org/css-grid-2/#subgrid-implicit), as for an overly-large grid: a span
    /// outside of the grid is clamped to its last line, an area placed completely outside is truncated to a span of 1 in the last
    /// track on that side.
    /// </summary>
    private static GridAxisPlacement ClampToSubgrid(GridAxisPlacement placement, int span)
    {
        if (placement.End <= 0)
            return GridAxisPlacement.Definite(0, 1);

        if (placement.Start >= span)
            return GridAxisPlacement.Definite(span - 1, span);

        return GridAxisPlacement.Definite(Math.Max(0, placement.Start), Math.Min(span, placement.End));
    }

    /// <summary>
    /// Sets the extra margins of the items in the subgridded axes (https://drafts.csswg.org/css-grid-2/#subgrid-margins): the items
    /// at an edge of the subgrid get the extra margin of that edge (its margin, border, padding and scrollbar gutter, plus its own
    /// extra margin in its parent), the other ones "half the size of the difference between the subgrid's gutters (row-gap /
    /// column-gap) and its parent grid's gutters".
    /// </summary>
    private void ApplySubgridExtraMargins(GridItem item)
    {
        if (_input?.Columns is { } columns)
        {
            var half = (GetOwnGap(GridAxis.Columns, columns) - columns.ParentGap) / 2;
            item.ExtraColumnStart = item.ColumnStart == 0 ? columns.EdgeStart : half;
            item.ExtraColumnEnd = item.ColumnEnd == _columns.Length ? columns.EdgeEnd : half;
        }

        if (_input?.Rows is { } rows)
        {
            var half = (GetOwnGap(GridAxis.Rows, rows) - rows.ParentGap) / 2;
            item.ExtraRowStart = item.RowStart == 0 ? rows.EdgeStart : half;
            item.ExtraRowEnd = item.RowEnd == _rows.Length ? rows.EdgeEnd : half;
        }
    }

    /// <summary>The used gap of a subgridded axis of the subgrid: "A value of normal indicates that the subgrid has the same size gutters as its parent grid".</summary>
    private double GetOwnGap(GridAxis axis, SubgridAxisInput input)
    {
        var gap = axis == GridAxis.Columns ? _style.Alignment.ColumnGap : _style.Alignment.RowGap;
        return gap.IsNormal ? input.InheritedGap : gap.Resolve(axis == GridAxis.Columns ? _inlineSize ?? 0 : _percentageBlockSize ?? 0);
    }

    /// <summary>
    /// Creates the inputs and the instances of the algorithm of the items that are subgrids, which place their items (recursively),
    /// once the items of the grid are placed.
    /// </summary>
    private void InitializeNestedSubgrids()
    {
        foreach (var item in _items)
        {
            item.Subgrid = null;
            if (!IsSubgridCandidate(item))
                continue;

            // "In grid lanes layout, auto-placed subgrids don't inherit any line names from their parent grid, because that would make
            // the placement of the item dependent on layout results" (https://drafts.csswg.org/css-grid-3/#subgrids)
            var inheritsNames = !item.IsLanesAuto;
            var columns = CreateSubgridAxisInput(item, GridAxis.Columns, inheritsNames);
            var rows = CreateSubgridAxisInput(item, GridAxis.Rows, inheritsNames);
            if (columns is null && rows is null)
                continue;

            var input = new SubgridInput { Pass = _context.Pass, Columns = columns, Rows = rows };
            if (item.Box.LayoutState is GridContainerState state)
            {
                state.Subgrid = input;
            }
            else
            {
                item.Box.LayoutState = GridContainerState.ForSubgrid(input);
            }

            // The instance that places the items of the subgrid and computes their contributions (the subgrid has no size yet,
            // but a block size that is a length)
            var blockSize = GetDefiniteSubgridBlockSize(item);
            var nested = ForIntrinsicSizes(_context, item.Box, blockSize);
            nested.Initialize();
            item.Subgrid = nested;

            // The standalone axis of a subgrid is sized now: the contributions of its items to the tracks of the subgridded axis
            // depend on it (the orthogonal items of a subgrid, the items of an orthogonal subgrid). With the block size of the
            // subgrid when it is known, else as if its grid area were infinite (https://drafts.csswg.org/css-grid-2/#algo-grid-sizing:
            // "assume the available space that it would have if any row with a definite max track sizing function had that size
            // and all other rows were infinite"). The axis is sized again once the grid area of the subgrid is known.
            if (nested._isLanes)
            {
                // (the stacking axis of a grid lanes subgrid has no tracks: the containing block of its items in that axis is
                // its content box)
                nested._parentStackingSize = EstimateStandaloneSize(item, GetParentAxis(item, nested._stackAxis));
                continue;
            }

            var standaloneAxis = rows is null ? GridAxis.Rows : GridAxis.Columns;
            var standaloneSize = (rows is null ? blockSize : null) ?? EstimateStandaloneSize(item, GetParentAxis(item, standaloneAxis));
            if (standaloneSize is { } definiteSize)
            {
                // (the automatic minimum size of the subgrid in its inline axis: it is not narrower than its min-content size)
                if (standaloneAxis == GridAxis.Columns && item.Style.Box.GetMinSize(item.WritingDirection.IsHorizontal).IsAuto && !item.Style.IsScrollContainer)
                {
                    nested.SizeTracks(GridAxis.Columns, GridSizingMode.MinContent);
                    standaloneSize = definiteSize = Math.Max(definiteSize, nested.GetGridSize(GridAxis.Columns));
                }

                nested.SizeTracks(standaloneAxis, GridSizingMode.Layout, definiteSize);
            }
            else
            {
                nested.SizeTracks(standaloneAxis, GridSizingMode.MaxContent);
            }

            nested.ComputeTrackPositions(standaloneAxis, standaloneSize);
            nested.SetSized(standaloneAxis);
            nested.PrepareNestedSubgrids(standaloneAxis);
        }
    }

    /// <summary>
    /// Returns the size of the content box of a subgrid item in an axis of the grid before the tracks of that axis are sized, when
    /// the subgrid fills a grid area whose size is known (tracks with a definite max track sizing function, the content box of a
    /// grid lanes container in its stacking axis), else null.
    /// </summary>
    private double? EstimateStandaloneSize(GridItem item, GridAxis axis)
    {
        var isStackingAxis = _isLanes && axis == _stackAxis;
        if (!isStackingAxis && !(axis == GridAxis.Columns ? item.StretchesColumns : item.StretchesRows))
            return null;

        if (GetAreaSize(item, axis) is not { } area)
            return null;

        var edges = GetSubgridEdges(item);
        var startSide = axis == GridAxis.Columns ? _writingDirection.InlineStart : _writingDirection.BlockStart;
        return Math.Max(0, area - edges.Get(startSide) - edges.Get(Opposite(startSide)));
    }

    /// <summary>Returns the block size of the content box of a subgrid item when its preferred block size is a length, else null.</summary>
    private double? GetDefiniteSubgridBlockSize(GridItem item)
    {
        var boxModel = item.Style.Box;
        var horizontal = !item.WritingDirection.IsHorizontal;
        if (boxModel.GetSize(horizontal) is not { IsLengthPercentage: true, Value.HasPercentage: false } size)
            return null;

        var edges = item.Style.BoxSizing == BoxSizing.BorderBox
            ? item.WritingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, _inlineSize ?? 0).Add(boxModel.BorderWidth)).BlockSum()
            : 0;
        return Math.Max(0, size.Value.Resolve(0) - edges);
    }

    /// <summary>Returns the input of an axis of a subgrid item (its own axis), or null when it is not subgridded.</summary>
    /// <param name="item">The subgrid item.</param>
    /// <param name="itemAxis">The axis of the subgrid.</param>
    /// <param name="inheritsNames">Whether the subgrid inherits the line names and the named areas of the grid (not an auto-placed subgrid of a grid lanes container).</param>
    private SubgridAxisInput? CreateSubgridAxisInput(GridItem item, GridAxis itemAxis, bool inheritsNames = true)
    {
        if (!HasSubgridTrackList(item, itemAxis))
            return null;

        var parentAxis = GetParentAxis(item, itemAxis);

        // https://drafts.csswg.org/css-grid-3/#subgrids: a subgrid of a grid lanes container is only subgridded in the grid axis ("if the
        // parent's corresponding axis is a stacking axis, the subgridded axis also acts as a stacking axis": a standalone axis)
        if (_isLanes && parentAxis != _laneAxis)
            return null;

        var start = item.GetStart(parentAxis);
        var end = item.GetEnd(parentAxis);
        var span = end - start;

        // The start edge of the axis of the subgrid, compared with the start edge of the axis of the grid
        var itemStartSide = itemAxis == GridAxis.Columns ? item.WritingDirection.InlineStart : item.WritingDirection.BlockStart;
        var parentStartSide = parentAxis == GridAxis.Columns ? _writingDirection.InlineStart : _writingDirection.BlockStart;
        var reversed = itemStartSide != parentStartSide;

        var edges = GetSubgridEdges(item);
        var outerStart = reversed ? item.GetExtraEnd(parentAxis) : item.GetExtraStart(parentAxis);
        var outerEnd = reversed ? item.GetExtraStart(parentAxis) : item.GetExtraEnd(parentAxis);

        // The line names of the parent at the lines of the subgrid, and its named areas that the subgrid overlaps
        var names = new string[]?[span + 1];
        var hasNames = false;
        for (var line = 0; line <= span && inheritsNames; line++)
        {
            var parentLine = reversed ? end - line : start + line;
            names[line] = GetLineNamesForSubgrid(parentAxis, parentLine);
            hasNames |= names[line] is not null;
        }

        List<(string Name, int Start, int End)>? areas = null;
        foreach (var (name, areaStart, areaEnd) in inheritsNames ? GetNamedAreas(parentAxis) : [])
        {
            var low = Math.Max(areaStart, start);
            var high = Math.Min(areaEnd, end);
            if (low >= high)
                continue;

            var (subgridStart, subgridEnd) = reversed ? (end - high, end - low) : (low - start, high - start);
            (areas ??= []).Add((name, subgridStart, subgridEnd));
            AddName(names, subgridStart, name + "-start");
            AddName(names, subgridEnd, name + "-end");
            hasNames = true;
        }

        return new SubgridAxisInput
        {
            ParentAxis = parentAxis,
            IsReversed = reversed,
            Span = span,
            LineNames = hasNames ? names : null,
            Areas = areas,
            EdgeStart = edges.Get(itemStartSide) + outerStart,
            EdgeEnd = edges.Get(Opposite(itemStartSide)) + outerEnd,
            ParentGap = GetGap(parentAxis),
            InheritedGap = _input?.Get(parentAxis) is { } ownInput ? GetOwnGap(parentAxis, ownInput) : GetGap(parentAxis),
        };

        static void AddName(string[]?[] names, int line, string name)
        {
            var list = names[line];
            if (list is null)
            {
                names[line] = [name];
            }
            else if (Array.IndexOf(list, name) < 0)
            {
                names[line] = [.. list, name];
            }
        }
    }

    /// <summary>Returns the margins, borders, paddings and scrollbar gutters of a subgrid item on its physical sides (the percentages resolve against the inline size of the grid).</summary>
    private Sides<double> GetSubgridEdges(GridItem item)
    {
        var boxModel = item.Style.Box;
        var basis = _inlineSize ?? 0;
        var gutters = item.WritingDirection.ToPhysical(IntrinsicSizing.GetScrollbarGutters(_context, item.Box, item.WritingDirection));
        return FormattingContext.ResolveMargins(boxModel, basis).Add(FormattingContext.ResolvePadding(boxModel, basis)).Add(boxModel.BorderWidth).Add(gutters);
    }

    private static PhysicalSide Opposite(PhysicalSide side) => side switch
    {
        PhysicalSide.Top => PhysicalSide.Bottom,
        PhysicalSide.Bottom => PhysicalSide.Top,
        PhysicalSide.Left => PhysicalSide.Right,
        _ => PhysicalSide.Left,
    };

    /// <summary>
    /// Returns the explicitly-assigned names of a line of the grid (in the lines of the implicit grid) that a subgrid inherits: its
    /// own names and the names it inherited from its own parent when it is a subgrid (https://drafts.csswg.org/css-grid-2/#subgrid-line-name-inheritance).
    /// </summary>
    private string[]? GetLineNamesForSubgrid(GridAxis axis, int line)
    {
        var explicitAxis = axis == GridAxis.Columns ? _explicitColumns : _explicitRows;
        var explicitLine = line - (axis == GridAxis.Columns ? _columnOffset : _rowOffset);
        string[]? names = null;
        if (explicitAxis.ExplicitLineNames is { } explicitNames && explicitLine >= 0 && explicitLine < explicitNames.Length)
        {
            names = explicitNames[explicitLine];
        }

        if (_input?.Get(axis) is { LineNames: { } inherited } && explicitLine >= 0 && explicitLine < inherited.Length && inherited[explicitLine] is { } inheritedNames)
        {
            names = names is null ? inheritedNames : [.. names, .. inheritedNames];
        }

        return names;
    }

    /// <summary>Returns the named areas of the grid in an axis (in the lines of the implicit grid): the areas of <c>grid-template-areas</c> and the areas inherited as a subgrid.</summary>
    private IEnumerable<(string Name, int Start, int End)> GetNamedAreas(GridAxis axis)
    {
        var offset = axis == GridAxis.Columns ? _columnOffset : _rowOffset;
        var areas = _style.Grid.TemplateAreas;
        if (!areas.IsNone)
        {
            foreach (var name in areas.AreaNames)
            {
                if (areas.TryGetArea(name, out var area))
                {
                    yield return axis == GridAxis.Columns ? (name, area.ColumnStart - 1 + offset, area.ColumnEnd - 1 + offset) : (name, area.RowStart - 1 + offset, area.RowEnd - 1 + offset);
                }
            }
        }

        if (_input?.Get(axis)?.Areas is { } inherited)
        {
            foreach (var area in inherited)
            {
                yield return area;
            }
        }
    }

    /// <summary>
    /// Once the tracks of an axis are sized and positioned: the instances of the subgrids subgridded in the axis share the tracks,
    /// and their inputs get the positions of the tracks in their content box; a subgrid that is subgridded in the other axis sizes
    /// its own tracks of the axis (the areas of its items, which are sizing items of the grid in the other axis). Recursive.
    /// </summary>
    private void PrepareNestedSubgrids(GridAxis axis)
    {
        foreach (var item in _items)
        {
            if (item.Subgrid is not { } subgrid)
                continue;

            var input = subgrid._input;
            if (subgrid.IsSubgriddedIn(axis, out var ownAxis, out var reversed))
            {
                ShareTracksWithSubgrid(item, subgrid, axis, ownAxis, reversed);
            }
            else if (input is not null && (input.Columns is not null || input.Rows is not null))
            {
                // The axis is a standalone axis of the subgrid, whose items are sizing items of the grid in the other axis: their areas
                // in this axis are the own tracks of the subgrid, sized with its content box (the subgrid fills its grid area)
                var subgridAxis = GetParentAxis(item, axis);
                var edges = GetSubgridEdges(item);
                var startSide = axis == GridAxis.Columns ? _writingDirection.InlineStart : _writingDirection.BlockStart;
                var size = Math.Max(0, (GetAreaSize(item, axis) ?? 0) - edges.Get(startSide) - edges.Get(Opposite(startSide)));
                if (subgrid._isLanes)
                {
                    // The stacking axis of a grid lanes subgrid: the size of its content box when it fills its grid area
                    if (item.Stretches(axis))
                    {
                        subgrid._parentStackingSize = size;
                    }

                    continue;
                }

                subgrid.SizeTracks(subgridAxis, GridSizingMode.Layout, size);
                subgrid.ComputeTrackPositions(subgridAxis, size);
                subgrid.SetSized(subgridAxis);
                subgrid.PrepareNestedSubgrids(subgridAxis);
            }
        }
    }

    /// <summary>Gives the tracks of an axis (sized and positioned) to a subgrid subgridded in it, and their positions in its content box to its input.</summary>
    private void ShareTracksWithSubgrid(GridItem item, GridLayoutAlgorithm subgrid, GridAxis axis, GridAxis ownAxis, bool reversed)
    {
        var tracks = GetTracks(axis);
        var start = item.GetStart(axis);
        var span = item.GetSpan(axis);
        var shared = new GridTrack[span];
        for (var i = 0; i < span; i++)
        {
            shared[i] = tracks[reversed ? item.GetEnd(axis) - 1 - i : start + i];
        }

        subgrid.SetTracks(ownAxis, shared);
        subgrid._input!.Get(ownAxis)!.Tracks = GetSubgridTrackPositions(item, axis, reversed);
        subgrid.PrepareNestedSubgrids(ownAxis);
    }

    /// <summary>Replaces the tracks of an axis by the tracks of the parent grid (a subgridded axis of the instance that places the items of a subgrid).</summary>
    private void SetTracks(GridAxis axis, GridTrack[] tracks)
    {
        if (axis == GridAxis.Columns)
        {
            _columns = tracks;
        }
        else
        {
            _rows = tracks;
        }

        SetSized(axis);
    }

    private void SetSized(GridAxis axis)
    {
        if (axis == GridAxis.Columns)
        {
            _columnsSized = true;
        }
        else
        {
            _rowsSized = true;
        }
    }

    /// <summary>
    /// Returns the positions of the tracks spanned by a subgrid item in the content box of the subgrid, in the direction of its own
    /// axis: its content box is its grid area minus its extra margins, margins, borders, paddings and scrollbar gutters.
    /// </summary>
    private (double Start, double End)[] GetSubgridTrackPositions(GridItem item, GridAxis axis, bool reversed)
    {
        var tracks = GetTracks(axis);
        var start = item.GetStart(axis);
        var end = item.GetEnd(axis);
        var edges = GetSubgridEdges(item);
        var startSide = axis == GridAxis.Columns ? _writingDirection.InlineStart : _writingDirection.BlockStart;
        var contentStart = tracks[start].Position + item.GetExtraStart(axis) + edges.Get(startSide);
        var contentEnd = tracks[end - 1].End - item.GetExtraEnd(axis) - edges.Get(Opposite(startSide));
        var result = new (double, double)[end - start];
        for (var i = 0; i < result.Length; i++)
        {
            var track = tracks[reversed ? end - 1 - i : start + i];
            result[i] = reversed ? (contentEnd - track.End, contentEnd - track.Position) : (track.Position - contentStart, track.End - contentStart);
        }

        return result;
    }

    /// <summary>
    /// Sets the tracks of a subgridded axis from the positions given by the parent grid (https://drafts.csswg.org/css-grid-2/#subgrid-tracks:
    /// "its track sizes are governed by the parent grid"), or returns false when the parent did not size them yet.
    /// </summary>
    private bool ApplyParentTracks(GridAxis axis)
    {
        if (_input?.Get(axis) is not { Tracks: { } positions })
            return false;

        var tracks = GetTracks(axis);
        for (var i = 0; i < tracks.Length && i < positions.Length; i++)
        {
            tracks[i].Position = positions[i].Start;
            tracks[i].BaseSize = Math.Max(0, positions[i].End - positions[i].Start);
        }

        SetSized(axis);
        return true;
    }
}
