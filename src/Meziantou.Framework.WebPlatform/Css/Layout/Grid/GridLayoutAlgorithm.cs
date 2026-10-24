namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The grid layout algorithm (https://drafts.csswg.org/css-grid-2/#layout-algorithm) of the contents of one grid container:
/// places its grid items (https://drafts.csswg.org/css-grid-2/#placement), sizes its tracks (https://drafts.csswg.org/css-grid-2/#algo-grid-sizing),
/// aligns the tracks and lays out the items in their grid areas.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm works in the logical axes of the grid container: the columns are its inline axis, the rows its block axis.
/// The items are laid out by their own formatting context in a constraint space whose containing block is their grid area (in
/// the axes of the container, so the orthogonal items need no special case); a stretched item gets its size with
/// <see cref="ConstraintSpace.FixedInlineSize"/>/<see cref="ConstraintSpace.FixedBlockSize"/>, the other ones their
/// fit-content size (<see cref="ConstraintSpace.AutoInlineSizeIsFitContent"/>) or their automatic block size.
/// </para>
/// <para>
/// The intrinsic size contributions of the items in their inline axis come from <see cref="IntrinsicSizing.GetContribution"/>
/// (cached for the layout pass); in their block axis, the item is laid out with the size of its grid area in the other axis.
/// The last layout of each item is reused when the next one has the same constraint space.
/// </para>
/// </remarks>
internal sealed partial class GridLayoutAlgorithm
{
    private const double Epsilon = 1e-7;

    private readonly LayoutContext _context;
    private readonly ContainerBox _box;
    private readonly LayoutStyle _style;
    private readonly WritingDirection _writingDirection;
    private readonly ContentArea _content;

    /// <summary>The definite inline size of the content box, or null when the container is sized under a min-content or max-content constraint (its intrinsic sizes).</summary>
    private readonly double? _inlineSize;

    /// <summary>The definite block size of the content box (the available grid space of the rows), or null.</summary>
    private readonly double? _blockSize;

    /// <summary>The block size of the content box when it is definite for the percentages, or null.</summary>
    private readonly double? _percentageBlockSize;

    // The definite min and max sizes of the content box (the auto repetitions, the maximization of the tracks and the flexible tracks)
    private readonly double? _minInlineSize;
    private readonly double? _maxInlineSize;
    private readonly double? _minBlockSize;
    private readonly double? _maxBlockSize;

    private double _columnGap;
    private double _rowGap;

    /// <summary>The grid items, in order-modified document order (https://drafts.csswg.org/css-display/#order-modified-document-order).</summary>
    private readonly List<GridItem> _items = [];
    private List<(ContainerBox Box, int Index)>? _absolutelyPositioned;

    private GridExplicitAxis _explicitColumns = null!;
    private GridExplicitAxis _explicitRows = null!;
    private GridTrack[] _columns = [];
    private GridTrack[] _rows = [];

    /// <summary>The number of implicit columns before the explicit grid (the index of the first explicit line).</summary>
    private int _columnOffset;

    /// <summary>The number of implicit rows before the explicit grid.</summary>
    private int _rowOffset;

    /// <summary>Whether the tracks of the columns (rows) are sized: the areas of the items in that axis are known.</summary>
    private bool _columnsSized;
    private bool _rowsSized;

    /// <summary>The space added between the tracks of an axis that is not sized yet by the content distribution (see <see cref="EstimateDistributedSpace"/>), once computed.</summary>
    private double? _estimatedColumnSpace;
    private double? _estimatedRowSpace;

    /// <summary>The size of the rows sized with the available block size (the automatic block size of the container).</summary>
    private double _contentBlockSize;

    /// <summary>The used block size of the content box (the size the rows are aligned in).</summary>
    private double _containerBlockSize;

    private GridLayoutAlgorithm(LayoutContext context, ContainerBox box, in ContentArea content, double? inlineSize, double? blockSize, double? percentageBlockSize, double? minInlineSize = null, double? maxInlineSize = null)
    {
        _context = context;
        _box = box;
        _style = box.Style.Layout;
        _writingDirection = content.WritingDirection;
        _content = content;
        _inlineSize = inlineSize;
        _blockSize = blockSize;
        _percentageBlockSize = percentageBlockSize;

        if (blockSize is null)
        {
            _minBlockSize = content.MinBlockSize;
            _maxBlockSize = content.MaxBlockSize;
        }

        if (inlineSize is null)
        {
            (_minInlineSize, _maxInlineSize) = GetContentBoxMinMaxInlineSizes(box, _writingDirection);
            _minInlineSize = minInlineSize ?? _minInlineSize;
            _maxInlineSize = maxInlineSize ?? _maxInlineSize;
        }

        if (box.Display.Inside == DisplayInside.GridLanes)
        {
            _isLanes = true;
            _laneAxis = GetLaneAxis(_style);
            _stackAxis = _laneAxis == GridAxis.Columns ? GridAxis.Rows : GridAxis.Columns;
        }

        // https://drafts.csswg.org/css-align-3/#gap-percent: the percentages of the gaps resolve against the content box of the
        // container (against zero when its size is indefinite)
        var alignment = _style.Alignment;
        _columnGap = alignment.ColumnGap.Resolve(inlineSize ?? 0);
        _rowGap = alignment.RowGap.Resolve(percentageBlockSize ?? 0);
    }

    /// <summary>Creates the algorithm that lays out the contents of a grid container in its content box.</summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="box">The grid container.</param>
    /// <param name="content">The content box.</param>
    /// <param name="ignoresItems">Whether the grid is sized without its items (size containment).</param>
    public static GridLayoutAlgorithm ForLayout(LayoutContext context, ContainerBox box, in ContentArea content, bool ignoresItems = false)
        => new(context, box, content, content.InlineSize, content.BlockSize, content.PercentageBlockSize) { _ignoresItems = ignoresItems };

    /// <summary>Creates the algorithm that computes the intrinsic inline sizes of a grid container (its content box has no definite inline size).</summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="box">The grid container.</param>
    /// <param name="contentBlockSize">The definite block size of the content box, or null.</param>
    /// <param name="ignoresItems">Whether the grid is sized without its items (size containment).</param>
    /// <param name="minInlineSize">The definite minimum inline size of the content box given by the box that asks for the sizes (see <see cref="UsesInlineSizeLimits"/>), or null.</param>
    /// <param name="maxInlineSize">The definite maximum inline size of the content box given by the box that asks for the sizes, or null.</param>
    /// <param name="minBlockSize">When <paramref name="contentBlockSize"/> is null, the definite minimum block size of the content box (see <see cref="UsesBlockSizeLimits"/>), or null.</param>
    /// <param name="maxBlockSize">When <paramref name="contentBlockSize"/> is null, the definite maximum block size of the content box, or null.</param>
    public static GridLayoutAlgorithm ForIntrinsicSizes(LayoutContext context, ContainerBox box, double? contentBlockSize, bool ignoresItems = false, double? minInlineSize = null, double? maxInlineSize = null, double? minBlockSize = null, double? maxBlockSize = null)
    {
        var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
        var content = new ContentArea(writingDirection, LogicalOffset.Zero, 0, contentBlockSize) { MinBlockSize = minBlockSize, MaxBlockSize = maxBlockSize };
        return new GridLayoutAlgorithm(context, box, content, inlineSize: null, contentBlockSize, contentBlockSize, minInlineSize, maxInlineSize) { _ignoresItems = ignoresItems };
    }

    /// <summary>
    /// Returns whether the intrinsic inline sizes of a box depend on its definite minimum and maximum inline sizes: a grid
    /// container whose tracks of the inline axis have an automatic repetition (https://drafts.csswg.org/css-grid-2/#auto-repeat:
    /// "if the grid container has a definite size or max size in the relevant axis, then the number of repetitions is the largest
    /// possible positive integer that does not cause the grid to overflow the content box [...]; if any number of repetitions
    /// would overflow, then 1 repetition. Otherwise, if the grid container has a definite min size in the relevant axis, the
    /// number of repetitions is the smallest possible positive integer that fulfills that minimum requirement"). The box that
    /// sizes the container resolves them (its percentages, the sizes transferred through its aspect ratio).
    /// </summary>
    public static bool UsesInlineSizeLimits(ContainerBox box)
    {
        if (!box.Display.IsGridContainer || box.Kind != ContainerBoxKind.Normal)
            return false;

        var grid = box.Style.Layout.Grid;
        return grid.TemplateColumns.AutoRepeat is not null || grid.TemplateRows.AutoRepeat is not null;
    }

    /// <summary>
    /// Returns whether the intrinsic inline sizes of a box depend on its definite minimum and maximum block sizes: a grid container
    /// whose rows have an automatic repetition (https://drafts.csswg.org/css-grid-2/#auto-repeat), whose number gives the rows where
    /// the items are placed (the lanes of a grid lanes container whose stacking axis is the inline axis).
    /// </summary>
    public static bool UsesBlockSizeLimits(ContainerBox box)
        => box.Display.IsGridContainer && box.Kind == ContainerBoxKind.Normal && box.Style.Layout.Grid.TemplateRows.AutoRepeat is not null;

    /// <summary>Returns the size of the rows sized with the content box (the automatic block size of the container).</summary>
    public double ComputeRowsSize()
    {
        Initialize();
        if (_isLanes)
            return ComputeLanesEmptyBlockSize();

        RunGridSizingAlgorithm();
        return _contentBlockSize;
    }

    /// <summary>Whether the grid is sized as if it had no items: "as if the element had no content" (https://drafts.csswg.org/css-contain-2/#containment-size).</summary>
    private bool _ignoresItems;

    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of the grid container
    /// (https://drafts.csswg.org/css-grid-2/#intrinsic-sizes): "the sum of the grid container's track sizes (including gutters) in
    /// the appropriate axis, when the grid is sized under a max-content constraint (min-content constraint)".
    /// </summary>
    public IntrinsicSizes ComputeIntrinsicInlineSizes()
    {
        Initialize();
        if (_isLanes)
            return ComputeLanesIntrinsicInlineSizes();

        if (_columns.Length == 0)
            return default;

        var minContent = ComputeIntrinsicInlineSize(GridSizingMode.MinContent);
        var maxContent = ComputeIntrinsicInlineSize(GridSizingMode.MaxContent);
        return new IntrinsicSizes(minContent, Math.Max(minContent, maxContent));
    }

    /// <summary>
    /// Runs the grid sizing algorithm with the columns sized under a min-content or max-content constraint and returns the size of
    /// the columns: when the contribution of an item to the columns depends on the sizes of the rows (an orthogonal item, an item
    /// with an aspect ratio), the rows are sized, then the columns again (https://drafts.csswg.org/css-grid-2/#algo-grid-sizing).
    /// </summary>
    private double ComputeIntrinsicInlineSize(GridSizingMode mode)
    {
        _columnsSized = false;
        _rowsSized = false;
        _rowsFromParent = ApplyParentTracks(GridAxis.Rows);
        if (ApplyParentTracks(GridAxis.Columns))
            return GetTracksExtent(_columns);

        if (_rowsFromParent)
        {
            PrepareNestedSubgrids(GridAxis.Rows);
        }

        SizeTracks(GridAxis.Columns, mode);
        if (HasItemsDependingOnRows())
        {
            _columnsSized = true;
            ComputeTrackPositions(GridAxis.Columns, containerSize: null);
            PrepareNestedSubgrids(GridAxis.Columns);
            if (!_rowsFromParent)
            {
                SizeRows();
            }

            if (ColumnContributionsChanged())
            {
                SizeTracks(GridAxis.Columns, mode);
            }

            _columnsSized = false;
            _rowsSized = _rowsFromParent;
        }

        return GetGridSize(GridAxis.Columns);
    }

    private bool HasSubgrids()
    {
        foreach (var item in _items)
        {
            if (item.Subgrid is not null)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether the contribution of an item to the columns may depend on the sizes of the rows (see <see cref="ColumnContributionsChanged"/>).</summary>
    private bool HasItemsDependingOnRows()
    {
        foreach (var item in _items)
        {
            if (DependsOnRows(item))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the contributions of an item to the columns depend on the sizes of the rows: an orthogonal item, an item
    /// with a preferred aspect ratio, or an item whose intrinsic inline sizes depend on its block size when it is definite (a
    /// percentage or stretched block size with contents whose inline size depends on it: a replaced element with a percentage
    /// block size, an orthogonal flow). The intrinsic contributions of the item with and without a definite block size tell.
    /// </summary>
    private bool DependsOnRows(GridItem item)
    {
        if (item.DependsOnRows is { } known)
            return known;

        var result = item.IsOrthogonal || HasPreferredAspectRatio(item);
        if (!result && item.Box.Kind != ContainerBoxKind.TableWrapper)
        {
            var preferred = item.Style.Box.GetSize(!IsHorizontal(GridAxis.Columns));
            var stretches = item.StretchesRows && preferred.IsAuto;
            if (stretches || (preferred.IsLengthPercentage && preferred.Value.HasPercentage) || preferred.Kind == SizeKind.Stretch)
            {
                const double Probe = 100_000;
                var indefinite = IntrinsicSizing.GetContribution(_context, item.Box, _writingDirection, containingBlockBlockSize: null);
                var definite = IntrinsicSizing.GetContribution(_context, item.Box, _writingDirection, Probe, stretches);
                result = Math.Abs(indefinite.MinContent - definite.MinContent) > Epsilon || Math.Abs(indefinite.MaxContent - definite.MaxContent) > Epsilon;
            }
        }

        item.DependsOnRows = result;
        return result;
    }

    /// <summary>
    /// Returns the block size of the contents of the container: as in the browsers, an editing host without items is at least
    /// as tall as the line of its caret (see <see cref="BlockFormattingContext.HasCaretLineIfEmpty"/>).
    /// </summary>
    private double GetContentBlockSizeWithCaretLine(double contentBlockSize)
        => _items.Count == 0 && BlockFormattingContext.HasCaretLineIfEmpty(_box) ? Math.Max(contentBlockSize, _box.Style.Layout.UsedLineHeight) : contentBlockSize;

    /// <summary>Lays out the grid items, adds their fragments to <paramref name="builder"/> and returns the block size of the contents and their baselines.</summary>
    public ContentLayoutResult Layout(FragmentBuilder builder)
    {
        Initialize();
        if (_isLanes)
            return LayoutLanes(builder);

        RunGridSizingAlgorithm();

        // The block size of the contents: the size of the rows (with their percentages treated as auto when the block size of the
        // container is indefinite), at least the line of the caret of an editing host without items
        var gridBlockSize = GetContentBlockSizeWithCaretLine(_contentBlockSize);

        // The measure of the intrinsic block size only needs the sizes of the rows: the items are measured, not laid out
        if (_content.MeasuresIntrinsicBlockSize)
            return new ContentLayoutResult(gridBlockSize);

        // https://drafts.csswg.org/css-grid-2/#algo-grid-sizing: "Finally, align the tracks within the grid container according to
        // the align-content and justify-content properties" (an indefinite block size is the size of the grid, clamped by the min
        // and max block sizes of the container)
        _containerBlockSize = _blockSize ?? _content.ContainedBlockSize ?? Clamp(gridBlockSize, _minBlockSize, _maxBlockSize);
        if (_content.BlockSizeGrowsToContents)
        {
            // The container whose block size comes from its aspect ratio grows to contain its rows (its automatic minimum size):
            // the rows are aligned in that size
            _containerBlockSize = Math.Max(_containerBlockSize, gridBlockSize);
        }

        if (!_columnsFromParent)
        {
            ComputeTrackPositions(GridAxis.Columns, _inlineSize);
            PrepareNestedSubgrids(GridAxis.Columns);
        }

        if (!_rowsFromParent)
        {
            ComputeTrackPositions(GridAxis.Rows, _containerBlockSize);
            PrepareNestedSubgrids(GridAxis.Rows);
        }

        // The items of the subgrids share the baselines of the tracks of the grid (computed with the layouts that measured the items)
        if (HasSubgrids())
        {
            ComputeItemBaselines(final: false);
            PropagateBaselinesToSubgrids();
        }

        LayoutItems();
        ComputeItemBaselines(final: true);
        PlaceItems(builder);
        RecordState();
        builder.GapGeometry = BuildGapGeometry();

        var (firstBaseline, lastBaseline) = ComputeContainerBaselines();
        // The in-flow contents end with the grid (https://drafts.csswg.org/css-grid-2/#overflow: "the grid is also included in a grid
        // container's scrollable overflow region"), which the alignment may move; the tracks of a subgridded axis are the ones of
        // the parent grid, which the subgrid does not overflow
        return new ContentLayoutResult(gridBlockSize, firstBaseline, lastBaseline)
        {
            InFlowBlockSize = Math.Max(0, GetGridEnd(GridAxis.Rows)),
            InFlowInlineSize = _columnsFromParent ? null : Math.Max(0, GetGridEnd(GridAxis.Columns)),
        };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Initialization and placement
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Collects the items, creates the explicit grid, runs the grid item placement algorithm (https://drafts.csswg.org/css-grid-2/#auto-placement-algo)
    /// and creates the tracks of the implicit grid.
    /// </summary>
    private void Initialize()
    {
        if (_isLanes)
        {
            InitializeLanes();
            return;
        }

        CollectItems();
        var grid = _style.Grid;

        // https://drafts.csswg.org/css-grid-2/#subgrid-tracks: the explicit grid of a subgridded axis is the tracks the subgrid spans in
        // its parent grid (their names are inherited), its gaps are the gaps of the parent
        _input = GetSubgridInput();
        var columnsInput = _input?.Columns;
        var rowsInput = _input?.Rows;
        if (columnsInput is not null)
        {
            _columnGap = columnsInput.ParentGap;
        }

        if (rowsInput is not null)
        {
            _rowGap = rowsInput.ParentGap;
        }

        _explicitColumns = GridExplicitAxis.Create(grid, GridAxis.Columns, _inlineSize, _maxInlineSize, _minInlineSize, _columnGap, columnsInput?.Span, columnsInput?.LineNames);
        // (the block size given by contain-intrinsic-size to a container with size containment is known before its rows, WPT
        // css/css-sizing/contain-intrinsic-size/contain-intrinsic-size-025.html)
        _explicitRows = GridExplicitAxis.Create(grid, GridAxis.Rows, _blockSize ?? _content.ContainedBlockSize, _maxBlockSize, _minBlockSize, _rowGap, rowsInput?.Span, rowsInput?.LineNames);

        var count = _items.Count;
        var columns = new GridAxisPlacement[count];
        var rows = new GridAxisPlacement[count];
        for (var i = 0; i < count; i++)
        {
            var item = _items[i];
            var itemGrid = item.Style.Grid;
            item.WritingDirection = WritingDirection.FromStyle(item.Style);
            item.IsOrthogonal = item.WritingDirection.IsOrthogonalTo(_writingDirection);
            columns[i] = GetAutomaticSubgridSpan(item, GridAxis.Columns) is { } columnSpan ? GridAxisPlacement.Auto(columnSpan) : GridPlacement.Resolve(itemGrid.ColumnStart, itemGrid.ColumnEnd, _explicitColumns);
            rows[i] = GetAutomaticSubgridSpan(item, GridAxis.Rows) is { } rowSpan ? GridAxisPlacement.Auto(rowSpan) : GridPlacement.Resolve(itemGrid.RowStart, itemGrid.RowEnd, _explicitRows);
        }

        // A subgridded axis has no implicit tracks: the definite areas of the items are clamped to the explicit grid before the
        // items without a definite position are placed (the automatic placement must see the tracks they really occupy)
        for (var i = 0; i < count; i++)
        {
            if (columnsInput is not null && columns[i].IsDefinite)
            {
                columns[i] = ClampToSubgrid(columns[i], columnsInput.Span);
            }

            if (rowsInput is not null && rows[i].IsDefinite)
            {
                rows[i] = ClampToSubgrid(rows[i], rowsInput.Span);
            }
        }

        GridPlacement.PlaceItems(columns, rows, _explicitColumns.TrackCount, _explicitRows.TrackCount, grid.AutoFlow, columnsInput?.Span, rowsInput?.Span);

        // A subgridded axis has no implicit tracks: the areas of the items are clamped to the explicit grid
        for (var i = 0; i < count; i++)
        {
            if (columnsInput is not null)
            {
                columns[i] = ClampToSubgrid(columns[i], columnsInput.Span);
            }

            if (rowsInput is not null)
            {
                rows[i] = ClampToSubgrid(rows[i], rowsInput.Span);
            }
        }

        // The implicit grid: the explicit grid extended to the lines of the items
        var columnStart = 0;
        var columnEnd = _explicitColumns.TrackCount;
        var rowStart = 0;
        var rowEnd = _explicitRows.TrackCount;
        for (var i = 0; i < count; i++)
        {
            columnStart = Math.Min(columnStart, columns[i].Start);
            columnEnd = Math.Max(columnEnd, columns[i].End);
            rowStart = Math.Min(rowStart, rows[i].Start);
            rowEnd = Math.Max(rowEnd, rows[i].End);
        }

        _columnOffset = -columnStart;
        _rowOffset = -rowStart;
        _estimatedColumnSpace = null;
        _estimatedRowSpace = null;
        _columns = CreateTracks(_explicitColumns, columnStart, columnEnd);
        _rows = CreateTracks(_explicitRows, rowStart, rowEnd);
        for (var i = 0; i < count; i++)
        {
            var item = _items[i];
            item.ColumnPlacement = columns[i];
            item.RowPlacement = rows[i];
            item.ColumnStart = columns[i].Start + _columnOffset;
            item.ColumnEnd = columns[i].End + _columnOffset;
            item.RowStart = rows[i].Start + _rowOffset;
            item.RowEnd = rows[i].End + _rowOffset;
            InitializeItem(item);
        }

        // https://drafts.csswg.org/css-grid-2/#auto-repeat: "auto-fit [...] after grid item placement any empty repeated tracks are
        // collapsed. An empty track is one with no in-flow grid items placed into or spanning across it."
        CollapseEmptyAutoFitTracks(_columns, _explicitColumns, _columnOffset, GridAxis.Columns);
        CollapseEmptyAutoFitTracks(_rows, _explicitRows, _rowOffset, GridAxis.Rows);

        if (_input is not null)
        {
            foreach (var item in _items)
            {
                ApplySubgridExtraMargins(item);
            }
        }

        InitializeNestedSubgrids();
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#grid-items: the in-flow children of the container (the box tree wraps its text in
    /// anonymous items), in order-modified document order. The absolutely positioned children are not items
    /// (https://drafts.csswg.org/css-grid-2/#static-position).
    /// </summary>
    private void CollectItems()
    {
        _items.Clear();
        _absolutelyPositioned = null;
        var index = 0;
        var needsSort = false;
        var previousOrder = int.MinValue;
        foreach (var child in _box.Children)
        {
            if (child is not ContainerBox childBox || childBox.IsOutsideMarker)
                continue;

            index++;
            if (PositionedLayout.IsAbsolutelyPositioned(childBox))
            {
                (_absolutelyPositioned ??= []).Add((childBox, index));
                continue;
            }

            var order = childBox.Style.Layout.Flex.Order;
            needsSort |= order < previousOrder;
            previousOrder = order;
            _items.Add(new GridItem(childBox, index, order) { Owner = this });
        }

        if (needsSort)
        {
            // A stable sort: the items with the same order keep the document order
            _items.Sort(static (a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index));
        }

        if (_ignoresItems)
        {
            _items.Clear();
            _absolutelyPositioned = null;
        }
    }

    private static GridTrack[] CreateTracks(GridExplicitAxis axis, int start, int end)
    {
        var tracks = new GridTrack[end - start];
        for (var i = 0; i < tracks.Length; i++)
        {
            var index = start + i;
            tracks[i] = new GridTrack { Size = axis.GetTrackSize(index), IsExplicit = index >= 0 && index < axis.TrackCount };
        }

        return tracks;
    }

    private void CollapseEmptyAutoFitTracks(GridTrack[] tracks, GridExplicitAxis axis, int offset, GridAxis gridAxis)
    {
        if (!axis.IsAutoFit || axis.AutoRepeatCount == 0)
            return;

        var occupied = new bool[axis.AutoRepeatCount];
        foreach (var item in _items)
        {
            var start = item.GetStart(gridAxis) - offset - axis.AutoRepeatStart;
            var end = item.GetEnd(gridAxis) - offset - axis.AutoRepeatStart;
            for (var i = Math.Max(0, start); i < Math.Min(end, occupied.Length); i++)
            {
                occupied[i] = true;
            }
        }

        for (var i = 0; i < occupied.Length; i++)
        {
            if (!occupied[i])
            {
                tracks[offset + axis.AutoRepeatStart + i].IsCollapsed = true;
            }
        }
    }

    /// <summary>Resolves the alignment, the stretching and the auto margins of an item in the axes of the grid.</summary>
    private void InitializeItem(GridItem item)
    {
        var layout = item.Style;
        var boxModel = layout.Box;
        var auto = _writingDirection.ToLogical(new Sides<bool>(boxModel.Margin.Top.IsAuto, boxModel.Margin.Right.IsAuto, boxModel.Margin.Bottom.IsAuto, boxModel.Margin.Left.IsAuto));
        item.AutoMarginColumnStart = auto.InlineStart;
        item.AutoMarginColumnEnd = auto.InlineEnd;
        item.AutoMarginRowStart = auto.BlockStart;
        item.AutoMarginRowEnd = auto.BlockEnd;

        var (justify, justifySafe, justifyIsNormal) = ResolveSelfAlignment(item, GetJustifySelf(layout), GridAxis.Columns);
        var (align, alignSafe, alignIsNormal) = ResolveSelfAlignment(item, GetAlignSelf(layout), GridAxis.Rows);
        item.JustifySelf = justify;
        item.JustifySafe = justifySafe;
        item.AlignSelf = align;
        item.AlignSafe = alignSafe;

        // https://drafts.csswg.org/css-grid-2/#grid-item-sizing: "normal: If the grid item has no preferred aspect ratio, and no
        // natural size in the relevant axis (if it is a replaced element), the grid item is sized as for align-self: stretch.
        // Otherwise, the grid item is sized consistent with the size calculation rules for block-level elements for the
        // corresponding axis." https://drafts.csswg.org/css-align-3/#valdef-justify-self-stretch: stretch applies when the size is
        // auto and neither of the margins of the axis is auto
        var hasRatio = HasPreferredAspectRatio(item);
        item.StretchesColumns = justify == AxisAlignment.Stretch && !auto.InlineStart && !auto.InlineEnd
            && GetPreferredSize(item, GridAxis.Columns).IsAuto && !(justifyIsNormal && (hasRatio || IsReplacedWithNaturalSize(item)));
        item.StretchesRows = align == AxisAlignment.Stretch && !auto.BlockStart && !auto.BlockEnd
            && GetPreferredSize(item, GridAxis.Rows).IsAuto && !(alignIsNormal && (hasRatio || IsReplacedWithNaturalSize(item)));

        // The block size of a non-replaced item with an aspect ratio and normal alignment comes from its inline size, but while
        // its inline size is not known (its contributions to the columns), the stretch-fit block size of its grid area is
        // transferred through its aspect ratio, as in Blink (WPT
        // css/css-grid/grid-lanes/subgrid/grid-subgridded-to-grid-lanes/track-sizing/column-auto-placed-subgrid-inherited-tracks-001.html)
        item.TransfersStretchedBlockSize = align == AxisAlignment.Stretch && alignIsNormal && hasRatio && item.Box.Kind != ContainerBoxKind.Replaced
            && !auto.BlockStart && !auto.BlockEnd && GetPreferredSize(item, GridAxis.Rows).IsAuto;

        // A non-replaced item with an aspect ratio and normal alignment is sized as a block-level box in its inline axis: it fills
        // the area (unless its block size is definite, then it is transferred through the ratio)
        item.FillsColumns = !item.IsOrthogonal && justifyIsNormal && hasRatio && item.Box.Kind != ContainerBoxKind.Replaced
            && !auto.InlineStart && !auto.InlineEnd && GetPreferredSize(item, GridAxis.Columns).IsAuto;

        // https://drafts.csswg.org/css-grid-2/#subgrid-box-alignment: "The subgrid is always stretched in its subgridded dimension(s):
        // the align-self/justify-self properties on it are ignored, as are any specified width/height constraints."
        item.SubgridsColumns = IsSubgriddedItem(item, GridAxis.Columns);
        item.SubgridsRows = IsSubgriddedItem(item, GridAxis.Rows);
        if (item.SubgridsColumns)
        {
            item.JustifySelf = AxisAlignment.Stretch;
            item.StretchesColumns = true;
            item.FillsColumns = false;
        }

        if (item.SubgridsRows)
        {
            item.AlignSelf = AxisAlignment.Stretch;
            item.StretchesRows = true;
        }
    }

    /// <summary>The used <c>justify-self</c> of an item: <c>auto</c> is the <c>justify-items</c> of the container, without the <c>legacy</c> keyword.</summary>
    private Alignment GetJustifySelf(LayoutStyle item) => BoxAlignment.GetJustifySelf(item, _style);

    /// <summary>The used <c>align-self</c> of an item: <c>auto</c> is the <c>align-items</c> of the container.</summary>
    private Alignment GetAlignSelf(LayoutStyle item) => BoxAlignment.GetAlignSelf(item, _style);

    /// <summary>
    /// Returns an axis of the grid as an alignment container (https://drafts.csswg.org/css-align-3/#justify-grid): the writing mode of
    /// the container. The flow-relative positions (<c>flex-start</c>, <c>flow-start</c>) of the grid axis of a grid lanes container
    /// with <c>track-reverse</c> are on the end side (the side of its first auto-placed items).
    /// </summary>
    private AlignmentAxis GetAlignmentAxis(GridAxis axis)
    {
        var result = axis == GridAxis.Columns ? AlignmentAxis.Inline(_writingDirection) : AlignmentAxis.Block(_writingDirection);
        if (_isLanes && axis == _laneAxis && TrackReverse)
        {
            result = result with { FlexStart = WritingDirection.Opposite(result.WritingModeStart) };
        }

        return result;
    }

    /// <summary>
    /// Resolves a self-alignment value of an item in an axis of the grid (https://drafts.csswg.org/css-align-3/#self-alignment,
    /// see <see cref="BoxAlignment.ResolveSelfAlignment"/>): <c>normal</c> sizes as <c>stretch</c> (or as a block-level box, see
    /// <see cref="InitializeItem"/>) and start-aligns (https://drafts.csswg.org/css-align-3/#justify-grid).
    /// </summary>
    private (AxisAlignment Alignment, bool Safe, bool IsNormal) ResolveSelfAlignment(GridItem item, Alignment value, GridAxis axis)
    {
        var isNormal = value.Keyword is AlignmentKeyword.Normal or AlignmentKeyword.Auto or AlignmentKeyword.Legacy;
        var alignment = BoxAlignment.ResolveSelfAlignment(value.Keyword, GetAlignmentAxis(axis), item.WritingDirection, AxisAlignment.Stretch);
        return (alignment, value.Overflow == OverflowAlignment.Safe, isNormal);
    }

    /// <summary>Returns the preferred size property of an item in an axis of the grid.</summary>
    private Size GetPreferredSize(GridItem item, GridAxis axis) => item.Style.Box.GetSize(IsHorizontal(axis));

    /// <summary>Returns whether an axis of the grid is the horizontal axis.</summary>
    private bool IsHorizontal(GridAxis axis) => (axis == GridAxis.Columns) == _writingDirection.IsHorizontal;

    /// <summary>Returns whether an item has a preferred aspect ratio (https://drafts.csswg.org/css-sizing-4/#preferred-aspect-ratio).</summary>
    private bool HasPreferredAspectRatio(GridItem item)
    {
        var property = item.Style.Box.AspectRatio;
        if (property.HasRatio && property.Ratio is > 0)
            return true;

        return item.Box.Kind == ContainerBoxKind.Replaced && ReplacedContent.Get(_context, item.Box).NaturalSize.AspectRatio is > 0;
    }

    /// <summary>
    /// Returns whether an item is a replaced element that <c>normal</c> does not stretch (https://drafts.csswg.org/css-grid-2/#grid-item-sizing:
    /// "use the grid area as [...] start" for the items with a natural size): as in the browsers, every replaced element,
    /// which has at least the default object size (WPT css/css-grid/alignment/grid-item-no-aspect-ratio-stretch-1.html), but
    /// the form controls, which are stretched like the non-replaced boxes.
    /// </summary>
    private bool IsReplacedWithNaturalSize(GridItem item)
        => item.Box.Kind == ContainerBoxKind.Replaced && ReplacedContent.Get(_context, item.Box).Kind != ReplacedContentKind.FormControl;

    // ------------------------------------------------------------------------------------------------------------------
    // Grid sizing algorithm
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#algo-grid-sizing: sizes the columns, then the rows (with the sizes of the columns),
    /// then sizes the columns again if the min-content contribution of an item changed with the sizes of the rows, and the rows
    /// again.
    /// </summary>
    private void RunGridSizingAlgorithm()
    {
        _columnsSized = false;
        _rowsSized = false;

        // The subgridded axes of a subgrid have the tracks of its parent (sized by the parent)
        _columnsFromParent = ApplyParentTracks(GridAxis.Columns);
        _rowsFromParent = ApplyParentTracks(GridAxis.Rows);

        // The nested subgrids get the rows given by the parent before the columns are sized (the contributions of their items to
        // the columns may depend on them)
        if (_rowsFromParent)
        {
            PrepareNestedSubgrids(GridAxis.Rows);
        }

        SizeColumns();
        SizeRowsOrApplyParent();

        // 3. "Then, if the min-content contribution of any grid item has changed based on the row sizes and alignment calculated in
        // step 2, re-resolve the sizes of the grid columns with the new min-content and max-content contributions (once only)."
        // 4. "Next, if the min-content contribution of any grid item has changed based on the column sizes and alignment calculated
        // in step 3, re-resolve the sizes of the grid rows"
        if (!_columnsFromParent && ColumnContributionsChanged())
        {
            SizeColumns();
            SizeRowsOrApplyParent();
        }
    }

    private void SizeColumns()
    {
        if (!_columnsFromParent)
        {
            _columnsSized = false;
            SizeTracks(GridAxis.Columns, GridSizingMode.Layout);
            RememberHoistedColumnContributions();
            _columnsSized = true;
            ComputeTrackPositions(GridAxis.Columns, _inlineSize);
        }

        PrepareNestedSubgrids(GridAxis.Columns);
    }

    /// <summary>
    /// The contributions to the columns of the items of the subgrids, as the last sizing of the columns used them: they change
    /// when the rows give a size to the standalone axis of a subgrid (see <see cref="ColumnContributionsChanged"/>).
    /// </summary>
    private List<(GridSizingItem Item, double MinContent, double MaxContent)>? _hoistedColumnContributions;

    private void RememberHoistedColumnContributions()
    {
        _hoistedColumnContributions?.Clear();
        foreach (var item in _sizingItems)
        {
            if (item.Owner == this || item.IsPlaceholder)
                continue;

            var contributions = item.Owner.GetContributions(item.Item, item.OwnerAxis);
            (_hoistedColumnContributions ??= []).Add((item, contributions.MinContent, contributions.MaxContent));
        }
    }

    private void SizeRowsOrApplyParent()
    {
        if (_rowsFromParent)
        {
            _contentBlockSize = _blockSize ?? GetTracksExtent(_rows);
        }
        else
        {
            SizeRows();
        }

        PrepareNestedSubgrids(GridAxis.Rows);
    }

    /// <summary>Returns the distance between the start edge of the first track and the end edge of the last track.</summary>
    private static double GetTracksExtent(GridTrack[] tracks) => tracks.Length == 0 ? 0 : Math.Max(0, tracks[^1].End - tracks[0].Position);

    /// <summary>
    /// Sizes the rows. When the block size of the container is indefinite, its rows are sized with their percentages treated as
    /// <c>auto</c> to find its size, then sized again with that size (https://drafts.csswg.org/css-grid-2/#layout-algorithm: "Find
    /// the size of the grid container [...] cyclic percentages in track sizes are treated as auto. Given the resulting grid
    /// container size, run the Grid Sizing Algorithm to size the grid", as in the browsers).
    /// </summary>
    private void SizeRows()
    {
        _rowsSized = false;
        var rowGap = _style.Alignment.RowGap;

        // (the gutters of the rows of a grid lanes subgrid are the ones of its parent grid, also when it did not size them yet)
        var hasParentGap = _isLanes && _input is not null;
        if (!hasParentGap)
        {
            _rowGap = rowGap.Resolve(_percentageBlockSize ?? 0);
        }

        SizeTracks(GridAxis.Rows, GridSizingMode.Layout);
        _contentBlockSize = GetGridSize(GridAxis.Rows);

        // (the block size of a container with size containment does not depend on its rows)
        var containerSize = _content.ContainedBlockSize ?? Clamp(_contentBlockSize, _minBlockSize, _maxBlockSize);
        // The flexible rows whose flex factors sum to less than 1 are sized again in the resulting size too: they then only take
        // their fraction of it (as in the browsers, WPT css/css-grid/layout-algorithm/flex-sizing-rows-indefinite-height-002.html)
        var gapHasPercentage = !hasParentGap && !rowGap.IsNormal && rowGap.Length.HasPercentage;
        // The rows of a container with size containment are sized again in its known block size: its flexible rows fill it (WPT
        // css/css-sizing/contain-intrinsic-size/contain-intrinsic-size-023.html)
        if (_blockSize is null && (gapHasPercentage || HasPercentageTrack(_rows) || HasFractionalFlexSum(_rows) || _content.ContainedBlockSize is not null && HasFlexibleTrack(_rows)))
        {
            // The percentages of the row gap resolve against the size too (WPT css/css-grid/grid-model/grid-gutters-as-percentage-001.html)
            if (gapHasPercentage)
            {
                _rowGap = rowGap.Resolve(containerSize);
            }

            SizeTracks(GridAxis.Rows, GridSizingMode.Layout, containerSize);
        }

        _rowsSized = true;
        ComputeTrackPositions(GridAxis.Rows, _blockSize);

        static bool HasFlexibleTrack(GridTrack[] tracks)
        {
            foreach (var track in tracks)
            {
                if (track.IsFlexible)
                    return true;
            }

            return false;
        }

        static bool HasFractionalFlexSum(GridTrack[] tracks)
        {
            var sum = 0d;
            foreach (var track in tracks)
            {
                if (track.Size.Max.Kind == GridBreadthKind.Flex)
                {
                    sum += track.Size.Max.Flex;
                }
            }

            return sum > 0 && sum < 1;
        }

        static bool HasPercentageTrack(GridTrack[] tracks)
        {
            foreach (var track in tracks)
            {
                var size = track.Size;
                if ((size.Min.Kind == GridBreadthKind.LengthPercentage && size.Min.Length.HasPercentage) || (size.Max.Kind == GridBreadthKind.LengthPercentage && size.Max.Length.HasPercentage))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns whether the min-content contribution of an item to the columns changes with the sizes of the rows: the items whose
    /// inline size depends on their block size (the orthogonal flows, the items with a preferred aspect ratio).
    /// </summary>
    private bool ColumnContributionsChanged()
    {
        var changed = false;
        foreach (var item in _items)
        {
            if (!DependsOnRows(item))
                continue;

            var previous = item.ColumnContributions;
            if (!previous.IsValid)
                continue;

            var current = GetContributions(item, GridAxis.Columns);
            changed |= Math.Abs(current.MinContent - previous.MinContent) > Epsilon || Math.Abs(current.MaxContent - previous.MaxContent) > Epsilon;
        }

        // The items of the subgrids whose standalone axis is sized with the rows of the grid (an orthogonal subgrid, the orthogonal
        // items of a subgrid)
        if (!changed && _hoistedColumnContributions is { Count: > 0 } hoisted)
        {
            foreach (var (item, minContent, maxContent) in hoisted)
            {
                var current = item.Owner.GetContributions(item.Item, item.OwnerAxis);
                if (Math.Abs(current.MinContent - minContent) > Epsilon || Math.Abs(current.MaxContent - maxContent) > Epsilon)
                    return true;
            }
        }

        return changed;
    }

    /// <summary>Returns the size of the grid in an axis: the sizes of the tracks and of the gutters (before the alignment of the tracks).</summary>
    private double GetGridSize(GridAxis axis)
    {
        var tracks = GetTracks(axis);
        var gap = GetGap(axis);
        var size = 0d;
        UpdateGutters(tracks);
        foreach (var track in tracks)
        {
            size += track.BaseSize + (track.HasGutterBefore ? gap : 0);
        }

        return size;
    }

    /// <summary>Returns the position of the end edge of the last track (after the alignment of the tracks).</summary>
    private double GetGridEnd(GridAxis axis)
    {
        var tracks = GetTracks(axis);
        return tracks.Length == 0 ? 0 : tracks[^1].End;
    }

    /// <summary>Returns the position of the start edge of the first track of an axis (negative when the tracks overflow the start side of the content box).</summary>
    private double GetGridStart(GridAxis axis)
    {
        var tracks = GetTracks(axis);
        return tracks.Length == 0 ? 0 : tracks[0].Position;
    }

    private GridTrack[] GetTracks(GridAxis axis) => axis == GridAxis.Columns ? _columns : _rows;

    private double GetGap(GridAxis axis) => axis == GridAxis.Columns ? _columnGap : _rowGap;

    /// <summary>
    /// Sets <see cref="GridTrack.HasGutterBefore"/>: "Gutters only appear between tracks of the implicit grid", and "When a collapsed
    /// track's gutters collapse, they coincide exactly" (https://drafts.csswg.org/css-grid-2/#gutters): a gutter precedes a track that
    /// is not collapsed when a track that is not collapsed precedes it (the collapsed tracks are at the start of the gutter, as in
    /// the browsers).
    /// </summary>
    private static void UpdateGutters(GridTrack[] tracks)
    {
        var hasPrecedingTrack = false;
        foreach (var track in tracks)
        {
            track.HasGutterBefore = !track.IsCollapsed && hasPrecedingTrack;
            hasPrecedingTrack |= !track.IsCollapsed;
        }
    }

    /// <summary>
    /// Computes the positions of the tracks of an axis: the tracks and their gutters, aligned in the content box with
    /// <c>justify-content</c>/<c>align-content</c> (https://drafts.csswg.org/css-grid-2/#grid-align) when its size is known.
    /// </summary>
    private void ComputeTrackPositions(GridAxis axis, double? containerSize)
    {
        var tracks = GetTracks(axis);
        var gap = GetGap(axis);
        var gridSize = GetGridSize(axis);
        var count = 0;
        foreach (var track in tracks)
        {
            if (!track.IsCollapsed)
            {
                count++;
            }
        }

        var offset = 0d;
        var between = 0d;
        if (containerSize is { } size && count > 0)
        {
            var alignment = axis == GridAxis.Columns ? _style.Alignment.JustifyContent : _style.Alignment.AlignContent;
            (offset, between) = DistributeContentSpace(alignment, axis, size - gridSize, count);
        }

        var position = offset;
        foreach (var track in tracks)
        {
            if (track.HasGutterBefore)
            {
                position += gap + between;
            }

            track.Position = position;
            position += track.BaseSize;
        }
    }

    /// <summary>
    /// Returns the offset of the first track and the extra space between the tracks of a content distribution
    /// (https://drafts.csswg.org/css-align-3/#distribution-grid) of the free space of the grid in its content box (see
    /// <see cref="BoxAlignment.Distribute"/>): <c>normal</c> and <c>stretch</c> are start (the <c>auto</c> tracks are stretched by the
    /// track sizing); the space of a distribution is added to the gutters.
    /// </summary>
    private (double Offset, double Between) DistributeContentSpace(Alignment alignment, GridAxis axis, double freeSpace, int count)
    {
        var resolved = BoxAlignment.ResolveContentAlignment(alignment.Keyword, GetAlignmentAxis(axis), AxisAlignment.Stretch);

        // The grid axis of a grid lanes scroll container with track-reverse (its scroll origin is on the end side): with normal and
        // stretch (the stretch of the auto tracks is done by the track sizing), the overflowing tracks overflow on the start side, as
        // in the browsers (WPT css/css-grid/grid-lanes/overflow/grid-lanes-overflow-reverse-001.html); they are start-aligned
        // otherwise (WPT css/css-grid/grid-lanes/tentative/item-placement/column-reverse-dense-packing-multi-span-004.html)
        if (_isLanes && axis == _laneAxis && TrackReverse && resolved == AxisAlignment.Stretch && freeSpace < 0 && _style.IsScrollContainer)
            return (freeSpace, 0);

        return BoxAlignment.Distribute(resolved, freeSpace, count, alignment.Overflow == OverflowAlignment.Safe);
    }

    /// <summary>Returns the position of the start edge of the grid area of an item in an axis (from the start edge of the content box), after its extra margins.</summary>
    private double GetAreaStart(GridItem item, GridAxis axis) => GetTracks(axis)[item.GetStart(axis)].Position + item.GetExtraStart(axis);

    /// <summary>
    /// Returns the size of the grid area of an item in an axis of the grid (minus its extra margins), or null when the tracks of the
    /// axis are not sized yet: "If calculating the layout of a grid item in this step depends on the available space in the block
    /// axis, assume the available space that it would have if any row with a definite max track sizing function had that size and
    /// all other rows were infinite."
    /// </summary>
    private double? GetAreaSize(GridItem item, GridAxis axis)
    {
        // The containing block of the items of a grid lanes container in the stacking axis is its content box
        if (_isLanes && axis == _stackAxis)
            return GetStackingAreaSize();

        var tracks = GetTracks(axis);
        var start = item.GetStart(axis);
        var end = item.GetEnd(axis);
        var extra = item.GetExtraStart(axis) + item.GetExtraEnd(axis);
        if (axis == GridAxis.Columns ? _columnsSized : _rowsSized)
        {
            // (the tracks of a subgridded axis shared with a parent whose lines go in the other direction are in reverse order)
            var first = tracks[start];
            var last = tracks[end - 1];
            return Math.Max(0, Math.Max(first.End, last.End) - Math.Min(first.Position, last.Position) - extra);
        }

        // "If both the grid container and all tracks have definite sizes, also apply align-content to find the final effective size
        // of any gaps spanned by such items"
        ref var estimate = ref (axis == GridAxis.Columns ? ref _estimatedColumnSpace : ref _estimatedRowSpace);
        estimate ??= EstimateDistributedSpace(axis);
        var gap = GetGap(axis) + estimate.Value;
        var size = 0d;
        for (var i = start; i < end; i++)
        {
            var track = tracks[i];
            if (track.Max != GridMaxFunction.Fixed)
                return null;

            size += Math.Max(track.MaxValue, track.Min == GridMinFunction.Fixed ? track.MinValue : 0);
            if (i > start && track.HasGutterBefore)
            {
                size += gap;
            }
        }

        return Math.Max(0, size - extra);
    }

    /// <summary>
    /// Resolves the sizing functions of the tracks of an axis that is not sized yet, and returns the space that the content
    /// distribution adds between them when the size of the container and of all the tracks are definite (else 0).
    /// </summary>
    private double EstimateDistributedSpace(GridAxis axis)
    {
        var tracks = GetTracks(axis);
        var basis = axis == GridAxis.Columns ? _inlineSize : _percentageBlockSize;
        UpdateGutters(tracks);
        var gridSize = GetGuttersWithin(axis, 0, tracks.Length);
        var count = 0;
        var allFixed = true;
        foreach (var track in tracks)
        {
            track.ResolveFunctions(basis);
            allFixed &= track.Max == GridMaxFunction.Fixed && track.Min == GridMinFunction.Fixed;
            gridSize += Math.Max(track.MaxValue, track.MinValue);
            if (!track.IsCollapsed)
            {
                count++;
            }
        }

        var containerSize = axis == GridAxis.Columns ? _inlineSize : _blockSize;
        if (!allFixed || containerSize is not { } size || count == 0)
            return 0;

        var alignment = axis == GridAxis.Columns ? _style.Alignment.JustifyContent : _style.Alignment.AlignContent;
        return DistributeContentSpace(alignment, axis, size - gridSize, count).Between;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Layout of the items
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#layout-algorithm: "Lay out the grid items into their respective containing blocks. Each
    /// grid area's width and height are considered definite for this purpose."
    /// </summary>
    private void LayoutItems()
    {
        foreach (var item in _items)
        {
            LayoutItem(item, GetAreaSize(item, GridAxis.Columns), GetAreaSize(item, GridAxis.Rows), final: true);
        }
    }

    /// <summary>
    /// Aligns the items in their grid areas (https://drafts.csswg.org/css-grid-2/#alignment) and adds their fragments (in
    /// order-modified document order, the painting order of the items, https://drafts.csswg.org/css-grid-2/#z-order), and records
    /// the static positions of the absolutely positioned children.
    /// </summary>
    private void PlaceItems(FragmentBuilder builder)
    {
        var absoluteIndex = 0;
        foreach (var item in _items)
        {
            // The absolutely positioned children that precede the item in the document order (their fragments are in tree order)
            while (_absolutelyPositioned is not null && absoluteIndex < _absolutelyPositioned.Count && _absolutelyPositioned[absoluteIndex].Index < item.Index)
            {
                AddAbsolutelyPositionedChild(builder, _absolutelyPositioned[absoluteIndex++].Box);
            }

            var fragment = item.Fragment!;
            var size = _writingDirection.ToLogical(fragment.Size);
            var margins = GetMargins(item, GetAreaSize(item, GridAxis.Columns) ?? 0);
            var (inlinePosition, inlineStartMargin, inlineEndMargin) = _isLanes && _stackAxis == GridAxis.Columns
                ? AlignInStack(item, size.Inline, margins.InlineStart, margins.InlineEnd)
                : AlignInArea(item, GridAxis.Columns, size.Inline, margins.InlineStart, margins.InlineEnd);
            var (blockPosition, blockStartMargin, blockEndMargin) = _isLanes && _stackAxis == GridAxis.Rows
                ? AlignInStack(item, size.Block, margins.BlockStart, margins.BlockEnd)
                : AlignInArea(item, GridAxis.Rows, size.Block, margins.BlockStart, margins.BlockEnd);
            item.Offset = new LogicalOffset(inlinePosition, blockPosition);

            // The used margins (the auto margins resolved)
            fragment.Margin = _writingDirection.ToPhysical(new LogicalSides<double>(blockStartMargin, inlineEndMargin, blockEndMargin, inlineStartMargin));
            builder.AddChild(fragment, new LogicalOffset(_content.Offset.Inline + inlinePosition, _content.Offset.Block + blockPosition));
        }

        while (_absolutelyPositioned is not null && absoluteIndex < _absolutelyPositioned.Count)
        {
            AddAbsolutelyPositionedChild(builder, _absolutelyPositioned[absoluteIndex++].Box);
        }
    }

    /// <summary>
    /// Aligns an item in its grid area in one axis (https://drafts.csswg.org/css-grid-2/#auto-margins): the <c>auto</c> margins
    /// absorb the positive free space, else the item is aligned with its self-alignment (<c>safe</c> aligns an overflowing item
    /// to the start). Returns the position of the border box from the start edge of the content box and the used margins.
    /// </summary>
    private (double Position, double StartMargin, double EndMargin) AlignInArea(GridItem item, GridAxis axis, double size, double startMargin, double endMargin)
    {
        var areaStart = GetAreaStart(item, axis);
        var areaSize = GetAreaSize(item, axis) ?? 0;
        var freeSpace = areaSize - size - startMargin - endMargin;
        var autoStart = axis == GridAxis.Columns ? item.AutoMarginColumnStart : item.AutoMarginRowStart;
        var autoEnd = axis == GridAxis.Columns ? item.AutoMarginColumnEnd : item.AutoMarginRowEnd;
        if (autoStart || autoEnd)
        {
            // "auto margins in either axis absorb positive free space prior to alignment via the box alignment properties";
            // "Overflowing grid items resolve their auto margins to zero"
            if (freeSpace > 0)
            {
                if (autoStart && autoEnd)
                {
                    startMargin += freeSpace / 2;
                    endMargin += freeSpace / 2;
                }
                else if (autoStart)
                {
                    startMargin += freeSpace;
                }
                else
                {
                    endMargin += freeSpace;
                }
            }

            return (areaStart + startMargin, startMargin, endMargin);
        }

        // https://drafts.csswg.org/css-align-3/#overflow-values: "safe: If the size of the alignment subject overflows the alignment
        // container, the alignment subject is instead aligned as if the alignment mode were start"
        var alignment = item.GetAlignment(axis);
        var safe = axis == GridAxis.Columns ? item.JustifySafe : item.AlignSafe;
        var offset = alignment is AxisAlignment.Baseline or AxisAlignment.LastBaseline
            ? (safe && freeSpace < 0 ? 0 : GetBaselineOffset(item, axis, freeSpace))
            : BoxAlignment.GetOffset(alignment, freeSpace, safe);

        return (areaStart + offset + startMargin, startMargin, endMargin);
    }

    /// <summary>Returns the margins of an item in the axes of the grid (the <c>auto</c> margins are 0), whose percentages resolve against the inline size of its grid area.</summary>
    private LogicalSides<double> GetMargins(GridItem item, double percentageBasis)
        => _writingDirection.ToLogical(FormattingContext.ResolveMargins(item.Style.Box, percentageBasis));

    // ------------------------------------------------------------------------------------------------------------------
    // Absolutely positioned children
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#static-position: "The static position of an absolutely-positioned child of a grid
    /// container is determined as if it were the sole grid item in a grid area whose edges coincide with the content edges of the
    /// grid container" (aligned with its justify-self and align-self); "if that grid container also establishes the box's actual
    /// containing block, then the grid area specified by the grid-placement properties establishes its static-position rectangle
    /// instead" (https://drafts.csswg.org/css-align-3/#staticpos-rect).
    /// </summary>
    private void AddAbsolutelyPositionedChild(FragmentBuilder builder, ContainerBox box)
    {
        var item = new GridItem(box, 0, 0) { WritingDirection = WritingDirection.FromStyle(box.Style.Layout) };
        var (justify, _, _) = ResolveSelfAlignment(item, GetJustifySelf(item.Style), GridAxis.Columns);
        var (align, _, _) = ResolveSelfAlignment(item, GetAlignSelf(item.Style), GridAxis.Rows);
        if (_isLanes)
        {
            // The stacking axis of a grid lanes container: normal aligns with the side where the stacking starts
            if (_stackAxis == GridAxis.Columns)
            {
                justify = GetLanesStaticAlignment(item, GetJustifySelf(item.Style));
            }
            else
            {
                align = GetLanesStaticAlignment(item, GetAlignSelf(item.Style));
            }
        }

        var (inlinePosition, inlineEdge) = GetStaticEdge(justify, _content.Offset.Inline, _inlineSize ?? 0);
        var (blockPosition, blockEdge) = GetStaticEdge(align, _content.Offset.Block, _containerBlockSize);

        // When the container is the containing block, the static position is in the grid area of the box, its containing block
        // (see GridContainerState.GetAbsoluteArea)
        var inContainingBlock = ReferenceEquals(PositionedLayout.FindContainingBlock(box), _box);
        // https://drafts.csswg.org/css-align-3/#overflow-values: a safe alignment of a box that overflows falls back to the start edge
        // (see OutOfFlowCandidate.IsSafeHorizontally)
        var safeInline = GetJustifySelf(item.Style).Overflow == OverflowAlignment.Safe && inlineEdge != StaticPositionEdge.Start;
        var safeBlock = GetAlignSelf(item.Style).Overflow == OverflowAlignment.Safe && blockEdge != StaticPositionEdge.Start;
        builder.AddOutOfFlowCandidate(box, new LogicalOffset(inlinePosition, blockPosition), inlineEdge, blockEdge, staticPositionIsContainingBlock: inContainingBlock, safeInline: safeInline, safeBlock: safeBlock);

        static (double Position, StaticPositionEdge Edge) GetStaticEdge(AxisAlignment alignment, double start, double size) => alignment switch
        {
            AxisAlignment.End or AxisAlignment.LastBaseline => (start + size, StaticPositionEdge.End),
            AxisAlignment.Center => (start + (size / 2), StaticPositionEdge.Center),
            _ => (start, StaticPositionEdge.Start),
        };
    }

    /// <summary>
    /// Records the state of the layout that outlives it: the used track sizes for the resolved values of
    /// <c>grid-template-columns</c>/<c>grid-template-rows</c> (<see cref="LayoutContext.OnGridLaidOut"/>), and the lines of the
    /// grid for the absolutely positioned boxes whose containing block is the container (<see cref="GridContainerState"/>).
    /// </summary>
    private void RecordState()
    {
        var columnLines = GetLines(_columns);
        var rowLines = GetLines(_rows);

        // The lines at the edges of a subgridded axis are the edges of the content box of the subgrid for its absolutely positioned
        // boxes (the tracks of the parent grid start before its margin, border and padding: https://drafts.csswg.org/css-grid-2/#subgrid-margins)
        if (_columnsFromParent && columnLines.Length > 0 && _inlineSize is { } contentInlineSize)
        {
            columnLines[0].Start = 0;
            columnLines[^1].End = contentInlineSize;
        }

        if (_rowsFromParent && rowLines.Length > 0)
        {
            rowLines[0].Start = 0;
            rowLines[^1].End = _containerBlockSize;
        }

        var state = new GridContainerState(_context.Pass, _writingDirection, _content.Offset, _explicitColumns, _explicitRows, _columnOffset, _rowOffset, columnLines, rowLines)
        {
            SubgridRowGutterShift = GetSubgridGutterShift(GridAxis.Rows),
        };
        if (_box.LayoutState is GridContainerState previous)
        {
            state.Subgrid = previous.Subgrid;
        }

        if (!_isLanes && _items.Count > 0)
        {
            var itemRows = new Dictionary<ContainerBox, GridItemRows>(_items.Count);
            foreach (var item in _items)
            {
                var hasFixedRows = item.RowEnd > item.RowStart;
                for (var row = item.RowStart; row < item.RowEnd && hasFixedRows; row++)
                {
                    hasFixedRows = row >= 0 && row < _rows.Length && _rows[row].Min == GridMinFunction.Fixed && _rows[row].Max == GridMaxFunction.Fixed;
                }

                itemRows[item.Box] = new GridItemRows(item.RowStart, item.RowEnd, item.StretchesRows, item.ColumnStart, hasFixedRows);
            }

            state.ItemRows = itemRows;
        }

        _box.LayoutState = state;
        var columns = _input?.Columns is not null ? GridTrackSizes.ForSubgrid(_columns.Length, _explicitColumns.ExplicitLineNames) : CreateTrackSizes(_columns, _explicitColumns, _columnOffset);
        var rows = _input?.Rows is not null ? GridTrackSizes.ForSubgrid(_rows.Length, _explicitRows.ExplicitLineNames) : CreateTrackSizes(_rows, _explicitRows, _rowOffset);

        // The stacking axis of a grid lanes container has no tracks: the resolved value of its track list is the computed value
        if (_isLanes)
        {
            if (_stackAxis == GridAxis.Columns)
            {
                columns = GridTrackSizes.StackingAxis;
            }
            else
            {
                rows = GridTrackSizes.StackingAxis;
            }
        }

        _context.OnGridLaidOut(_box, columns, rows);
    }

    private static (double Start, double End)[] GetLines(GridTrack[] tracks)
    {
        var result = new (double, double)[tracks.Length];
        for (var i = 0; i < tracks.Length; i++)
        {
            result[i] = (tracks[i].Position, tracks[i].End);
        }

        return result;
    }

    /// <summary>Returns the used sizes of the tracks of an axis and the explicitly-assigned names of their lines (https://drafts.csswg.org/css-grid-2/#resolved-track-list-standalone).</summary>
    private static GridTrackSizes CreateTrackSizes(GridTrack[] tracks, GridExplicitAxis axis, int offset)
    {
        var sizes = new double[tracks.Length];
        for (var i = 0; i < tracks.Length; i++)
        {
            sizes[i] = tracks[i].BaseSize;
        }

        string[]?[]? names = null;
        if (axis.ExplicitLineNames is { } explicitNames)
        {
            names = new string[]?[tracks.Length + 1];
            for (var line = 0; line < explicitNames.Length && line + offset < names.Length; line++)
            {
                names[line + offset] = explicitNames[line];
            }
        }

        return new GridTrackSizes(sizes, names);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Baselines of the container
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#grid-baselines: the first (last) baseline of the container in its block axis, from the
    /// block-start edge of its content box: the shared baseline of the items of the first (last) non-empty row that participate in
    /// first (last) baseline alignment, else last (first) baseline alignment, else the baseline of the first (last) item in grid
    /// order of the row that has one, else a baseline synthesized from the first (last) item.
    /// </summary>
    private (double? First, double? Last) ComputeContainerBaselines()
    {
        if (_items.Count == 0)
            return (null, null);

        var firstRow = int.MaxValue;
        var lastRow = -1;
        foreach (var item in _items)
        {
            firstRow = Math.Min(firstRow, item.RowStart);
            lastRow = Math.Max(lastRow, item.RowEnd - 1);
        }

        return (GetRowBaseline(firstRow, last: false), GetRowBaseline(lastRow, last: true));
    }

    private double? GetRowBaseline(int row, bool last)
    {
        // "any grid item that spans multiple tracks is ignored if it's span does not start in the indicated track; except that for
        // step 2, it's ignored if its span does not end in the track" (last baselines: analogous, inverted)
        var preferred = last ? GridBaselineGroup.End : GridBaselineGroup.Start;
        var other = last ? GridBaselineGroup.Start : GridBaselineGroup.End;
        foreach (var group in (ReadOnlySpan<GridBaselineGroup>)[preferred, other])
        {
            foreach (var item in _items)
            {
                var inTrack = group == GridBaselineGroup.Start ? item.RowStart == row : item.RowEnd - 1 == row;
                if (item.RowBaselineGroup == group && inTrack && item.Fragment is { } fragment)
                    return item.Offset.Block + GetItemBaseline(item, fragment, GridAxis.Rows, last: item.GetAlignment(GridAxis.Rows) == AxisAlignment.LastBaseline || item.RowContentBaseline == AxisAlignment.LastBaseline).Baseline;
            }
        }

        // The items of the track in grid order (row, column, then order-modified document order)
        GridItem? candidate = null;
        GridItem? synthesized = null;
        foreach (var item in GetItemsInGridOrder(last))
        {
            if ((last ? item.RowEnd - 1 : item.RowStart) != row || item.Fragment is not { } fragment)
                continue;

            synthesized ??= item;
            if (!item.IsOrthogonal && (last ? fragment.LastBaseline : fragment.FirstBaseline) is not null)
            {
                candidate = item;
                break;
            }
        }

        var chosen = candidate ?? synthesized;
        if (chosen?.Fragment is not { } chosenFragment)
            return null;

        return chosen.Offset.Block + (GetBlockAxisBaseline(chosen, chosenFragment, last) ?? SynthesizeBlockAxisBaseline(chosenFragment));
    }

    /// <summary>Returns the items in grid order (https://drafts.csswg.org/css-grid-2/#grid-order), or in the reverse order.</summary>
    private List<GridItem> GetItemsInGridOrder(bool reverse)
    {
        var items = new List<GridItem>(_items);
        items.Sort(static (a, b) => a.RowStart != b.RowStart ? a.RowStart.CompareTo(b.RowStart) : a.ColumnStart != b.ColumnStart ? a.ColumnStart.CompareTo(b.ColumnStart) : 0);
        if (reverse)
        {
            items.Reverse();
        }

        return items;
    }

    /// <summary>
    /// Returns the baseline of an item in the block axis of the grid, from the block-start edge of its border box: its first (last)
    /// baseline when its block axis is the one of the grid (from the other side when its block-start side is the block-end side of
    /// the grid), or null (an orthogonal item, an item without baseline).
    /// </summary>
    private double? GetBlockAxisBaseline(GridItem item, BoxFragment fragment, bool last)
    {
        if (item.IsOrthogonal)
            return null;

        var blockSize = _writingDirection.ToLogical(fragment.Size).Block;
        var flipped = item.WritingDirection.BlockStart != _writingDirection.BlockStart;
        var baseline = (last != flipped) ? fragment.LastBaseline : fragment.FirstBaseline;
        if (baseline is not { } value)
            return null;

        return flipped ? blockSize - value : value;
    }

    /// <summary>
    /// Returns a baseline synthesized from the border box of an item in the block axis of the grid
    /// (https://drafts.csswg.org/css-align-3/#synthesize-baseline), from its block-start edge: the line-under edge (alphabetic) in a
    /// horizontal writing mode, the center (central) in a vertical one, as in the browsers (the line-under edge with
    /// <c>text-orientation: sideways</c>).
    /// </summary>
    private double SynthesizeBlockAxisBaseline(BoxFragment fragment)
    {
        var blockSize = _writingDirection.ToLogical(fragment.Size).Block;
        if (!_writingDirection.IsHorizontal && _box.Style.Layout.HasCentralDominantBaseline)
            return blockSize / 2;

        return _writingDirection.LineUnder == _writingDirection.BlockStart ? 0 : blockSize;
    }

    private static double Clamp(double size, double? min, double? max)
    {
        if (max is { } maximum && size > maximum)
        {
            size = maximum;
        }

        if (min is { } minimum && size < minimum)
        {
            size = minimum;
        }

        return size;
    }

    /// <summary>
    /// Returns the definite min and max inline sizes of the content box of a grid container whose inline size is indefinite (its
    /// intrinsic sizes): the lengths of <c>min-width</c>/<c>max-width</c> (the percentages and keywords are not definite).
    /// </summary>
    private static (double? Min, double? Max) GetContentBoxMinMaxInlineSizes(ContainerBox box, WritingDirection writingDirection)
    {
        var layout = box.Style.Layout;
        var boxModel = layout.Box;
        var horizontal = writingDirection.IsHorizontal;
        var paddingAndBorder = writingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, 0).Add(boxModel.BorderWidth)).InlineSum();
        return (Resolve(boxModel.GetMinSize(horizontal)), Resolve(boxModel.GetMaxSize(horizontal)));

        double? Resolve(Size size)
        {
            if (!size.IsLengthPercentage || size.Value.HasPercentage)
                return null;

            var value = size.Value.Resolve(0);
            return layout.BoxSizing == BoxSizing.BorderBox ? Math.Max(0, value - paddingAndBorder) : Math.Max(0, value);
        }
    }
}

/// <summary>The constraint the tracks of a grid are sized under (https://drafts.csswg.org/css-grid-2/#algo-terms).</summary>
internal enum GridSizingMode : byte
{
    /// <summary>The layout of the grid: the available grid space is the content box of the container (or indefinite).</summary>
    Layout,

    /// <summary>The grid container is sized under a min-content constraint (its min-content size).</summary>
    MinContent,

    /// <summary>The grid container is sized under a max-content constraint (its max-content size).</summary>
    MaxContent,
}
