using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kind of a sizing value of a table part (https://drafts.csswg.org/css-tables-3/#computing-cell-measures).</summary>
internal enum TableSizeKind : byte
{
    /// <summary><c>auto</c>, a keyword, or a mix of a length and a percentage (treated as <c>auto</c>, as in the browsers).</summary>
    Auto,

    /// <summary>A length.</summary>
    Fixed,

    /// <summary>A percentage.</summary>
    Percent,
}

/// <summary>The measures of a column of the table grid (https://drafts.csswg.org/css-tables-3/#computing-column-measures).</summary>
[StructLayout(LayoutKind.Auto)]
internal struct TableColumnMeasure
{
    /// <summary>The min-content inline size of the column.</summary>
    public double Min;

    /// <summary>The max-content inline size of the column.</summary>
    public double Max;

    /// <summary>The intrinsic percentage width of the column (50 for 50%), or 0.</summary>
    public double Percent;

    /// <summary>Whether the column is constrained (https://drafts.csswg.org/css-tables-3/#constrainedness): a length width.</summary>
    public bool IsConstrained;

    /// <summary>
    /// In fixed mode, the borders and paddings added to the percentage width of the column: the ones of a cell of the first row
    /// whose <c>box-sizing</c> is <c>content-box</c> (as in the browsers, WPT css/css-tables/tentative/td-box-sizing-001.html).
    /// </summary>
    public double PercentEdges;

    /// <summary>Whether a cell originates in the column.</summary>
    public bool HasOriginatingCell;

    /// <summary>
    /// Whether the column is merged with the previous one (https://drafts.csswg.org/css-tables-3/#dimensioning-the-row-column-grid--step2):
    /// in the automatic table layout, a column where no cell originates and whose width is <c>auto</c> or 0px takes no space
    /// (no width, no border spacing), as in the browsers (WPT css/css-tables/column-track-merging.html).
    /// </summary>
    public bool IsMergeable;

    /// <summary>The percentage width resolved against the given size, at least the min-content width.</summary>
    public readonly double ResolvePercent(double basis) => Math.Max(Min, Percent * basis / 100);
}

/// <summary>
/// The state of a table grid box for a layout pass (in <see cref="LayoutBox.LayoutState"/>): its structure, its collapsed borders
/// and the measures of its columns, which do not depend on the available space.
/// </summary>
internal sealed class TableState
{
    public required int Pass { get; init; }

    public required TableGridModel Model { get; init; }

    /// <summary>Whether the table is laid out in fixed mode (https://drafts.csswg.org/css-tables-3/#fixed-layout-mode).</summary>
    public required bool IsFixed { get; init; }

    /// <summary>The collapsed borders, or null in the separated borders model.</summary>
    public TableCollapsedBorders? CollapsedBorders { get; init; }

    /// <summary>The measures of the columns (computed on first use).</summary>
    public TableColumnMeasure[]? Columns { get; set; }

    public static TableState Get(LayoutContext context, ContainerBox grid)
    {
        if (grid.LayoutState is TableState state && state.Pass == context.Pass)
            return state;

        var layout = grid.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);

        // https://drafts.csswg.org/css-tables-3/#table-layout-property: "A table-root is said to be laid out in fixed mode whenever
        // the computed value of the table-layout property is equal to fixed, and the specified width of the table root is not auto
        // or max-content" (a mix of a length and a percentage is a percentage width)
        var width = layout.Box.GetSize(writingDirection.IsHorizontal);
        var isFixed = layout.Table.TableLayout == TableLayout.Fixed && width.Kind is not (SizeKind.Auto or SizeKind.MaxContent);
        var model = TableGridModel.Build(grid);
        TableCollapsedBorders? borders = null;
        if (layout.Table.BorderCollapse == BorderCollapse.Collapse)
        {
            borders = TableCollapsedBorders.Compute(grid, model, writingDirection);
        }
        else
        {
            foreach (var cell in model.Cells)
            {
                cell.Border = writingDirection.ToLogical(cell.Box.Style.Layout.Box.BorderWidth);
            }
        }

        state = new TableState { Pass = context.Pass, Model = model, IsFixed = isFixed, CollapsedBorders = borders };
        grid.LayoutState = state;
        return state;
    }
}

/// <summary>
/// The table layout (https://drafts.csswg.org/css-tables-3/#table-layout-algorithm) of a table grid box: the measures of the
/// columns, the width of the table and its distribution to the columns (automatic and fixed table layout), the heights of the
/// rows and of the row groups, the layout and the alignment of the cells, and the fragments of the row groups, the rows, the
/// columns and the cells.
/// </summary>
/// <remarks>
/// The algorithms follow CSS Tables 3 and, where it is not defined (the distribution of the heights, the colspan distribution),
/// the browsers (the TablesNG implementation of Chromium, which the tentative WPT tests describe). Everything is computed in the
/// logical axes of the table grid box: the columns are in its inline axis.
/// </remarks>
internal sealed partial class TableLayoutAlgorithm
{
    private readonly LayoutContext _context;
    private readonly ContainerBox _grid;
    private readonly LayoutStyle _style;
    private readonly WritingDirection _writingDirection;
    private readonly TableState _state;

    public TableLayoutAlgorithm(LayoutContext context, ContainerBox grid)
    {
        _context = context;
        _grid = grid;
        _style = grid.Style.Layout;
        _writingDirection = WritingDirection.FromStyle(_style);
        _state = TableState.Get(context, grid);
        var table = _style.Table;
        if (!IsCollapsed)
        {
            // https://drafts.csswg.org/css-writing-modes-4/#dimension-mapping: the first length is the spacing between the columns,
            // the second the spacing between the rows (they are not physical)
            InlineSpacing = table.BorderSpacingInline;
            _blockSpacing = table.BorderSpacingBlock;
        }
    }

    private TableGridModel Model => _state.Model;

    /// <summary>Whether the table is laid out in fixed mode.</summary>
    public bool IsFixed => _state.IsFixed;

    /// <summary>Whether the table is laid out in the collapsing border model (https://drafts.csswg.org/css-tables-3/#collapsed-borders-mode).</summary>
    public bool IsCollapsed => _state.CollapsedBorders is not null;

    /// <summary>The border spacing between the columns (0 in the collapsing border model).</summary>
    public double InlineSpacing { get; }

    /// <summary>The border spacing between the rows (0 in the collapsing border model).</summary>
    public double BlockSpacing => _blockSpacing;

    private double _blockSpacing;

    /// <summary>The used borders of the table grid box, in its logical axes (https://drafts.csswg.org/css-tables-3/#table-intrinsic-offsets).</summary>
    public LogicalSides<double> Border => _state.CollapsedBorders is { } collapsed ? collapsed.TableBorder : _writingDirection.ToLogical(_style.Box.BorderWidth);

    /// <summary>Returns the used paddings of the table grid box (0 in the collapsing border model, https://drafts.csswg.org/css-tables-3/#collapsed-style-overrides).</summary>
    public LogicalSides<double> GetPadding(double percentageBasis) => IsCollapsed ? default : _writingDirection.ToLogical(FormattingContext.ResolvePadding(_style.Box, percentageBasis));

    /// <summary>Returns the number of columns that take space: not collapsed nor merged.</summary>
    private int VisibleColumnCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Model.Columns.Count; i++)
            {
                if (!IsColumnSkipped(i))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Returns whether a column takes no space: it is collapsed (https://drafts.csswg.org/css-tables-3/#visible-track) or merged.</summary>
    private bool IsColumnSkipped(int index) => Model.Columns[index].IsCollapsed || GetColumnMeasures()[index].IsMergeable;

    /// <summary>
    /// The total horizontal border spacing (https://drafts.csswg.org/css-tables-3/#total-horizontal-border-spacing): the border
    /// spacing times one plus the number of columns, for a table with at least one column.
    /// </summary>
    private double GetTotalInlineSpacing(bool visibleOnly)
    {
        var count = visibleOnly ? VisibleColumnCount : Model.Columns.Count;
        return count > 0 ? InlineSpacing * (count + 1) : 0;
    }

    /// <summary>Returns the kind and the value of a sizing value of a table part.</summary>
    internal static TableSizeKind Classify(Size size, out double value)
    {
        value = 0;
        if (size.Kind != SizeKind.LengthPercentage)
            return TableSizeKind.Auto;

        var length = size.Value;
        if (length.IsFixed)
        {
            value = length.Length;
            return TableSizeKind.Fixed;
        }

        // calc() mixing a length and a percentage is treated as auto (WPT css/css-tables/auto-layout-calc-width-001.html)
        if (length.Calc is null && length.Length == 0)
        {
            value = length.Percentage;
            return TableSizeKind.Percent;
        }

        return TableSizeKind.Auto;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Intrinsic sizes and width of the table
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns GRIDMIN and GRIDMAX (https://drafts.csswg.org/css-tables-3/#computing-the-table-width): the sums of the min-content
    /// and max-content widths of the columns plus the undistributable space (the border spacing, the borders and the paddings of
    /// the table, the percentages resolved against zero), as border-box sizes of the table grid box.
    /// </summary>
    /// <param name="expandsPercentages">
    /// Whether the max-content width of the percentage columns makes the table wider (the <c>auto</c> width of a table in its
    /// layout, as in the browsers; not its intrinsic sizes: WPT css/css-tables/tentative/table-width-redistribution.html).
    /// </param>
    public IntrinsicSizes ComputeGridIntrinsicSizes(bool expandsPercentages = false)
    {
        var columns = GetColumnMeasures();
        var padding = GetPadding(0);
        var undistributable = Border.InlineSum() + padding.InlineSum() + GetTotalInlineSpacing(visibleOnly: true);
        var min = 0d;
        var max = 0d;
        if (IsFixed)
        {
            // https://drafts.csswg.org/css-tables-3/#width-distribution-in-fixed-mode: the min-content width of the percent and
            // auto columns is zero
            for (var i = 0; i < columns.Length; i++)
            {
                if (IsColumnSkipped(i))
                    continue;

                if (columns[i].IsConstrained)
                {
                    min += columns[i].Max;
                }

                max += columns[i].Max;
            }

            return new IntrinsicSizes(min + undistributable, Math.Max(min, max) + undistributable);
        }

        // The max-content width of the percentage columns: a column of P% whose max-content width is M needs a table of M / P%,
        // the other columns need their max-content width in the remaining percentage (as the browsers)
        var maxPercentSize = 0d;
        var nonPercentMax = 0d;
        var remainingPercent = 100d;
        for (var i = 0; i < columns.Length; i++)
        {
            if (IsColumnSkipped(i))
                continue;

            var column = columns[i];
            min += column.Min;
            max += column.Max;
            if (column.Percent > 0)
            {
                if (column.Max > 0)
                {
                    maxPercentSize = Math.Max(maxPercentSize, column.Max * 100 / column.Percent);
                }

                remainingPercent -= column.Percent;
            }
            else
            {
                nonPercentMax += column.Max;
            }
        }

        remainingPercent = Math.Clamp(remainingPercent, 0, 100);
        if (!expandsPercentages)
        {
            maxPercentSize = 0;
        }
        else if (nonPercentMax > 0)
        {
            max = Math.Max(max, remainingPercent > 0 ? nonPercentMax * 100 / remainingPercent : MaxTableInlineSize);
        }

        max = Math.Max(max, maxPercentSize);
        return new IntrinsicSizes(min + undistributable, Math.Max(min, max) + undistributable);
    }

    /// <summary>The largest inline size of a table whose percentage columns leave no space to its other columns (as the browsers).</summary>
    internal const double MaxTableInlineSize = 1_000_000;

    /// <summary>
    /// Returns the used width of the table grid box (border box, https://drafts.csswg.org/css-tables-3/#computing-the-table-width):
    /// the resolved <c>width</c> (an <c>auto</c> width is the fit-content size in the available space), at least the used
    /// min-width of the table (its <c>min-width</c>, GRIDMIN and CAPMIN).
    /// </summary>
    /// <param name="availableInlineSize">The available inline size, the margins of the table removed.</param>
    /// <param name="percentageBasis">The inline size of the containing block of the table wrapper box, or null when it is indefinite.</param>
    /// <param name="captionMin">CAPMIN: the largest min-content contribution of the captions.</param>
    /// <param name="percentageBlockSize">The block size of the containing block of the table when it is definite (for an <c>aspect-ratio</c>).</param>
    public double ComputeTableInlineSize(double availableInlineSize, double? percentageBasis, double captionMin, double? percentageBlockSize = null)
    {
        var grid = ComputeGridIntrinsicSizes();
        var edges = Border.InlineSum() + GetPadding(percentageBasis ?? 0).InlineSum();
        var horizontal = _writingDirection.IsHorizontal;
        var sizing = new AxisSizing(_context, _grid)
        {
            Horizontal = horizontal,
            IsInlineAxis = true,
            PercentageBasis = percentageBasis,
            PaddingAndBorder = edges,
            Edges = edges,
            StretchSize = Math.Max(availableInlineSize, edges),
        };

        // An auto (or fit-content) width is the fit-content size in the available space, where the percentage columns may make
        // the table wider (https://drafts.csswg.org/css-tables-3/#used-width-of-table)
        var boxModel = _style.Box;
        var preferred = boxModel.GetSize(horizontal);
        double width;
        if (preferred.Kind != SizeKind.FitContent && sizing.Resolve(preferred, SizingProperty.Preferred) is { } specified)
        {
            width = specified;
        }
        else if (preferred.IsAuto && boxModel.AspectRatio.Ratio is { } ratio && ResolveTableBlockSize(percentageBlockSize, Border.BlockSum() + GetPadding(percentageBasis ?? 0).BlockSum()) is { } blockSize)
        {
            // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: the inline size transferred from the definite block size
            var blockEdges = Border.BlockSum() + GetPadding(percentageBasis ?? 0).BlockSum();
            var inlinePerBlock = horizontal ? ratio : 1 / ratio;
            width = _style.BoxSizing == BoxSizing.BorderBox && !boxModel.AspectRatio.IsAuto
                ? blockSize * inlinePerBlock
                : (Math.Max(0, blockSize - blockEdges) * inlinePerBlock) + edges;
        }
        else
        {
            // https://drafts.csswg.org/css-tables-3/#used-width-of-table: "If the table-root has width: auto, the used width is
            // the greater of min(GRIDMAX, the table's containing block width), the used min-width of the table"
            width = Math.Min(ComputeGridIntrinsicSizes(expandsPercentages: true).MaxContent, Math.Max(0, availableInlineSize));
        }

        if (sizing.Resolve(boxModel.GetMaxSize(horizontal), SizingProperty.Maximum) is { } max && width > max)
        {
            width = max;
        }

        var usedMin = Math.Max(grid.MinContent, captionMin);
        if (sizing.Resolve(boxModel.GetMinSize(horizontal), SizingProperty.Minimum) is { } min)
        {
            usedMin = Math.Max(usedMin, min);
        }

        return Math.Max(Math.Max(width, usedMin), edges);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Column measures
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the measures of the columns (https://drafts.csswg.org/css-tables-3/#computing-column-measures), computed once per layout pass.</summary>
    private TableColumnMeasure[] GetColumnMeasures()
    {
        if (_state.Columns is { } cached)
            return cached;

        var columns = new TableColumnMeasure[Model.Columns.Count];
        if (IsFixed)
        {
            ComputeFixedColumnMeasures(columns);
        }
        else
        {
            ComputeAutoColumnMeasures(columns);
        }

        _state.Columns = columns;
        return columns;
    }

    /// <summary>
    /// Returns the box whose width applies to a column (as the browsers): its table-column box when its width is not
    /// <c>auto</c>, else its table-column-group box when its width is not <c>auto</c> ("colgroup css_width specifies width of
    /// generated cols", WPT css/css-tables/tentative/colgroup-col.html), else its table-column box.
    /// </summary>
    private ContainerBox? GetColumnSizingBox(int index)
    {
        var column = Model.Columns[index];
        var horizontal = _writingDirection.IsHorizontal;
        if (column.Column is { } columnBox && !columnBox.Style.Layout.Box.GetSize(horizontal).IsAuto)
            return columnBox;

        if (column.Group is { } group && !group.Style.Layout.Box.GetSize(horizontal).IsAuto)
            return group;

        return column.Column ?? column.Group;
    }

    /// <summary>
    /// Applies the widths of the column groups and the columns to the measures of their columns (https://drafts.csswg.org/css-tables-3/#outer-min-content):
    /// as in the browsers, a length makes the column constrained with that max-content width (its min-content width is given by
    /// its cells), a percentage is its percentage contribution (0% is ignored); the min-width is a minimum.
    /// </summary>
    private void ApplyColumnBoxes(TableColumnMeasure[] columns)
    {
        var horizontal = _writingDirection.IsHorizontal;
        for (var i = 0; i < columns.Length; i++)
        {
            if (GetColumnSizingBox(i) is not { } box)
                continue;

            ref var measure = ref columns[i];
            var boxModel = box.Style.Layout.Box;
            var minWidth = Classify(boxModel.GetMinSize(horizontal), out var minValue) == TableSizeKind.Fixed ? minValue : 0;
            var maxWidth = Classify(boxModel.GetMaxSize(horizontal), out var maxValue) == TableSizeKind.Fixed ? maxValue : double.PositiveInfinity;
            measure.Min = Math.Max(measure.Min, minWidth);
            switch (Classify(boxModel.GetSize(horizontal), out var width))
            {
                case TableSizeKind.Fixed:
                    measure.Max = Math.Max(minWidth, Math.Min(maxWidth, width));
                    measure.IsConstrained = true;
                    break;

                case TableSizeKind.Percent:
                    var percent = width;
                    if (Classify(boxModel.GetMaxSize(horizontal), out var maxPercent) == TableSizeKind.Percent)
                    {
                        percent = Math.Min(percent, maxPercent);
                    }

                    measure.Percent = Math.Max(measure.Percent, percent);
                    break;
            }
        }
    }

    /// <summary>
    /// Adds the measures of a cell (or of the share of a spanning cell) to the measures of a column, as the browsers: the
    /// max-content width of a constrained column is given by its constrained cells only.
    /// </summary>
    private static void Encompass(ref TableColumnMeasure column, double min, double max, double percent, bool isConstrained)
    {
        if (isConstrained)
        {
            column.Max = column.IsConstrained ? Math.Max(column.Max, max) : max;
            column.IsConstrained = true;
        }
        else if (!column.IsConstrained)
        {
            column.Max = Math.Max(column.Max, max);
        }

        column.Min = Math.Max(column.Min, min);
        column.Percent = Math.Max(column.Percent, percent);
    }

    /// <summary>The measures of the columns in the automatic table layout (https://drafts.csswg.org/css-tables-3/#computing-column-measures).</summary>
    private void ComputeAutoColumnMeasures(TableColumnMeasure[] columns)
    {
        ApplyColumnBoxes(columns);

        // The cells that span one column
        var maxSpan = 1;
        foreach (var cell in Model.Cells)
        {
            MeasureCell(cell);
            columns[cell.Column].HasOriginatingCell = true;
            if (cell.ColumnSpan > 1)
            {
                maxSpan = Math.Max(maxSpan, cell.ColumnSpan);
                continue;
            }

            Encompass(ref columns[cell.Column], cell.MinInlineSize, cell.MaxInlineSize, cell.Percent, cell.IsConstrained);
        }

        var horizontal = _writingDirection.IsHorizontal;
        for (var i = 0; i < columns.Length; i++)
        {
            columns[i].Max = Math.Max(columns[i].Max, columns[i].Min);

            // https://drafts.csswg.org/css-tables-3/#dimensioning-the-row-column-grid--step2: the columns where no cell
            // originates are merged, unless a column box gives them a width (as in the browsers)
            if (!columns[i].HasOriginatingCell)
            {
                var width = 0d;
                var kind = GetColumnSizingBox(i) is { } box ? Classify(box.Style.Layout.Box.GetSize(horizontal), out width) : TableSizeKind.Auto;
                columns[i].IsMergeable = kind == TableSizeKind.Auto || (kind == TableSizeKind.Fixed && width == 0);
            }
        }

        // The cells that span several columns, by increasing span
        if (maxSpan > 1)
        {
            var spanning = new List<TableCell>();
            foreach (var cell in Model.Cells)
            {
                if (cell.ColumnSpan > 1)
                {
                    spanning.Add(cell);
                }
            }

            // A stable sort: the cells with the same span keep the tree order
            var ordered = spanning.OrderBy(cell => cell.ColumnSpan).ToList();
            foreach (var cell in ordered)
            {
                DistributeSpanningCell(cell, columns);
            }
        }

        // https://drafts.csswg.org/css-tables-3/#intrinsic-percentage-width-of-a-column: "the smaller of the intrinsic percentage
        // width of the column based on cells of span up to N and 100% minus the sum of the intrinsic percentage width of all
        // prior columns in the table"
        var totalPercent = 0d;
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i].Percent > 0)
            {
                columns[i].Percent = Math.Max(0, Math.Min(columns[i].Percent, 100 - totalPercent));
                totalPercent += columns[i].Percent;
            }
        }
    }

    /// <summary>
    /// Computes the outer min-content and max-content widths of a cell (https://drafts.csswg.org/css-tables-3/#outer-min-content),
    /// its percentage contribution and its constrainedness. As in the browsers, <c>max-width</c> also limits the min-content
    /// width, and <c>min-width</c> wins over <c>max-width</c>.
    /// </summary>
    private void MeasureCell(TableCell cell)
    {
        var layout = cell.Box.Style.Layout;
        var boxModel = layout.Box;
        var horizontal = _writingDirection.IsHorizontal;
        var padding = _writingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, 0));
        var edges = padding.InlineSum() + cell.Border.InlineSum();

        IntrinsicSizes content;
        if (WritingDirection.FromStyle(layout).IsOrthogonalTo(_writingDirection))
        {
            // An orthogonal cell contributes its block size (the margins of the cells are ignored)
            var contribution = IntrinsicSizing.GetContribution(_context, cell.Box, _writingDirection, containingBlockBlockSize: null);
            var margins = _writingDirection.ToLogical(FormattingContext.ResolveMargins(boxModel, 0)).InlineSum();
            content = contribution.Add(-margins);
        }
        else
        {
            // The block size of a cell whose height is a length is definite for its contents (the percentages of its children)
            double? contentBlockSize = null;
            if (Classify(boxModel.GetSize(!horizontal), out var height) == TableSizeKind.Fixed)
            {
                var blockEdges = _writingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, 0)).BlockSum() + cell.Border.BlockSum();
                contentBlockSize = layout.BoxSizing == BoxSizing.BorderBox || _context.IsQuirksMode ? Math.Max(0, height - blockEdges) : height;
            }

            content = IntrinsicSizing.GetIntrinsicInlineSizes(_context, cell.Box, contentBlockSize).Add(edges);
        }

        double? cssWidth = null;
        cell.Percent = 0;
        switch (Classify(boxModel.GetSize(horizontal), out var width))
        {
            case TableSizeKind.Fixed:
                cssWidth = ToBorderBox(layout, width, edges);
                break;

            case TableSizeKind.Percent:
                cell.Percent = width;
                if (Classify(boxModel.GetMaxSize(horizontal), out var maxPercent) == TableSizeKind.Percent)
                {
                    cell.Percent = Math.Min(cell.Percent, maxPercent);
                }

                break;
        }

        double? cssMin = Classify(boxModel.GetMinSize(horizontal), out var minWidth) == TableSizeKind.Fixed ? ToBorderBox(layout, minWidth, edges) : null;
        double? cssMax = Classify(boxModel.GetMaxSize(horizontal), out var maxWidth) == TableSizeKind.Fixed ? ToBorderBox(layout, maxWidth, edges) : null;
        var min = Math.Min(content.MinContent, cssMax ?? double.PositiveInfinity);
        if (cssMin is { } minimum)
        {
            min = Math.Max(min, minimum);
        }

        if (cssWidth is { } quirkWidth && UsesNoWrapMinimumWidthQuirk(cell))
        {
            min = Math.Max(min, quirkWidth);
        }

        var preferred = cssWidth is { } constrained ? Math.Max(constrained, Math.Min(content.MinContent, cssMax ?? double.PositiveInfinity)) : content.MaxContent;
        var max = Math.Max(min, Math.Min(preferred, cssMax ?? double.PositiveInfinity));
        cell.MinInlineSize = Math.Max(min, edges);
        cell.MaxInlineSize = Math.Max(max, cell.MinInlineSize);
        cell.IsConstrained = cssWidth is not null;
    }

    /// <summary>Returns the border-box size of a length of a table part with <c>box-sizing</c>.</summary>
    private static double ToBorderBox(LayoutStyle style, double value, double edges)
        => style.BoxSizing == BoxSizing.BorderBox ? Math.Max(value, edges) : Math.Max(0, value) + edges;

    /// <summary>
    /// https://quirks.spec.whatwg.org/#the-table-cell-nowrap-minimum-width-calculation-quirk: in quirks mode, the minimum width
    /// of a <c>td</c> or <c>th</c> element that has a <c>nowrap</c> attribute and a fixed <c>width</c> is that width.
    /// </summary>
    private bool UsesNoWrapMinimumWidthQuirk(TableCell cell)
        => _context.IsQuirksMode && cell.Box.PseudoElement is null && cell.Box.Element is Dom.HtmlTableCellElement element && element.HasAttributeWithLocalName("nowrap");

    /// <summary>
    /// Distributes the min-content and max-content widths of a cell that spans several columns to them (as the browsers): its
    /// percentage in excess goes to the non-percentage columns in proportion to their max-content widths, its widths (minus the
    /// border spacing between the columns) are distributed with the width distribution algorithm, and the columns keep the
    /// largest of their widths and of their share.
    /// </summary>
    private void DistributeSpanningCell(TableCell cell, TableColumnMeasure[] columns)
    {
        // The merged columns are ignored
        var indices = new List<int>(cell.ColumnSpan);
        for (var i = cell.Column; i < Math.Min(columns.Length, cell.Column + cell.ColumnSpan); i++)
        {
            if (!columns[i].IsMergeable)
            {
                indices.Add(i);
            }
        }

        if (indices.Count == 0)
            return;

        var innerSpacing = InlineSpacing * (indices.Count - 1);
        var cellMin = Math.Max(0, cell.MinInlineSize - innerSpacing);
        var cellMax = Math.Max(0, cell.MaxInlineSize - innerSpacing);
        var span = new TableColumnMeasure[indices.Count];
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = columns[indices[i]];
        }

        // https://drafts.csswg.org/css-tables-3/#intrinsic-percentage-width-of-a-column-based-on-cells-of-span-up-to-n-n--1
        if (cell.Percent > 0)
        {
            var columnsPercent = 0d;
            var nonPercentCount = 0;
            var nonPercentMax = 0d;
            foreach (var column in span)
            {
                if (column.Percent > 0)
                {
                    columnsPercent += column.Percent;
                }
                else
                {
                    nonPercentCount++;
                    nonPercentMax += column.Max;
                }
            }

            var surplus = cell.Percent - columnsPercent;
            if (surplus > 0 && nonPercentCount > 0)
            {
                for (var i = 0; i < span.Length; i++)
                {
                    if (span[i].Percent > 0)
                        continue;

                    span[i].Percent = nonPercentMax > 0 ? surplus * span[i].Max / nonPercentMax : surplus / nonPercentCount;
                }
            }
        }

        Span<double> sizes = span.Length <= 64 ? stackalloc double[span.Length] : new double[span.Length];
        DistributeAuto(cellMin, span, sizes, growsConstrainedColumns: true);
        for (var i = 0; i < span.Length; i++)
        {
            span[i].Min = Math.Max(span[i].Min, sizes[i]);
        }

        DistributeAuto(cellMax, span, sizes, growsConstrainedColumns: cell.IsConstrained);
        for (var i = 0; i < span.Length; i++)
        {
            span[i].Max = Math.Max(Math.Max(span[i].Min, span[i].Max), sizes[i]);
            columns[indices[i]] = span[i];
        }
    }

    /// <summary>The measures of the columns in fixed mode (https://drafts.csswg.org/css-tables-3/#fixed-layout-mode): the widths of the columns and of the cells of the first row.</summary>
    private void ComputeFixedColumnMeasures(TableColumnMeasure[] columns)
    {
        var horizontal = _writingDirection.IsHorizontal;
        var defined = new bool[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var column = Model.Columns[i];
            var box = column.Column ?? column.Group;
            if (column.Column is { } columnBox && Classify(columnBox.Style.Layout.Box.GetSize(horizontal), out _) == TableSizeKind.Auto && column.Group is { } group)
            {
                box = group;
            }

            if (box is null)
                continue;

            switch (Classify(box.Style.Layout.Box.GetSize(horizontal), out var width))
            {
                case TableSizeKind.Fixed:
                    columns[i].Min = width;
                    columns[i].Max = width;
                    columns[i].IsConstrained = true;
                    defined[i] = true;
                    break;
                case TableSizeKind.Percent:
                    columns[i].Percent = width;
                    defined[i] = true;
                    break;
            }
        }

        // The cells of the first row (with their borders and paddings), for the columns without width
        foreach (var cell in Model.Cells)
        {
            if (cell.Row != 0)
                break;

            var layout = cell.Box.Style.Layout;
            var padding = _writingDirection.ToLogical(FormattingContext.ResolvePadding(layout.Box, 0));
            var edges = padding.InlineSum() + cell.Border.InlineSum();
            var end = Math.Min(columns.Length, cell.Column + cell.ColumnSpan);
            var count = end - cell.Column;
            if (count <= 0)
                continue;

            switch (Classify(layout.Box.GetSize(horizontal), out var width))
            {
                case TableSizeKind.Fixed:
                    // The width of the cell minus the border spacing between its columns, shared by its columns
                    var share = Math.Max(0, ToBorderBox(layout, width, edges) - (InlineSpacing * (count - 1))) / count;
                    for (var i = cell.Column; i < end; i++)
                    {
                        if (!defined[i])
                        {
                            columns[i].Min = share;
                            columns[i].Max = share;
                            columns[i].IsConstrained = true;
                        }
                    }

                    break;

                case TableSizeKind.Percent:
                    // https://drafts.csswg.org/css-tables-3/#width-distribution-in-fixed-mode: "Cells ignore their border and padding
                    // size if their width is a percentage"; the browsers add them for box-sizing: content-box
                    var percentEdges = layout.BoxSizing == BoxSizing.ContentBox ? edges / count : 0;
                    for (var i = cell.Column; i < end; i++)
                    {
                        if (!defined[i])
                        {
                            columns[i].Percent = width / count;
                            columns[i].PercentEdges = percentEdges;
                        }
                    }

                    break;
            }
        }

        for (var i = 0; i < columns.Length; i++)
        {
            columns[i].HasOriginatingCell = true;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Width distribution
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the used widths of the columns for the given assignable table width (https://drafts.csswg.org/css-tables-3/#width-distribution-algorithm).</summary>
    public double[] ComputeColumnWidths(double assignableWidth)
    {
        var measures = GetColumnMeasures();
        var widths = new double[measures.Length];
        if (measures.Length == 0)
            return widths;

        // The collapsed columns do not take space: the visible columns share the width
        var visible = measures;
        int[]? map = null;
        if (VisibleColumnCount != measures.Length)
        {
            map = new int[VisibleColumnCount];
            visible = new TableColumnMeasure[map.Length];
            var index = 0;
            for (var i = 0; i < measures.Length; i++)
            {
                if (!IsColumnSkipped(i))
                {
                    map[index] = i;
                    visible[index++] = measures[i];
                }
            }
        }

        var sizes = new double[visible.Length];
        if (IsFixed)
        {
            DistributeFixed(assignableWidth, visible, sizes);
        }
        else
        {
            DistributeAuto(assignableWidth, visible, sizes, growsConstrainedColumns: true);
        }

        if (map is null)
            return sizes;

        for (var i = 0; i < map.Length; i++)
        {
            widths[map[i]] = sizes[i];
        }

        return widths;
    }

    /// <summary>
    /// The width distribution algorithm of the automatic table layout (https://drafts.csswg.org/css-tables-3/#distribution-algorithm):
    /// the min-content, min-content-percentage, min-content-specified and max-content sizing-guesses, interpolated between the two
    /// guesses that bound the target width, or the excess width distributed above the max-content guess
    /// (https://drafts.csswg.org/css-tables-3/#distributing-width-to-columns), as in the browsers.
    /// </summary>
    /// <param name="target">The width to distribute.</param>
    /// <param name="columns">The measures of the columns.</param>
    /// <param name="sizes">The widths of the columns.</param>
    /// <param name="growsConstrainedColumns">
    /// Whether the width above the max-content guess may go to the constrained columns when there is no auto column: not for the
    /// max-content width of a spanning cell whose width is not a length (as the browsers).
    /// </param>
    private static void DistributeAuto(double target, ReadOnlySpan<TableColumnMeasure> columns, Span<double> sizes, bool growsConstrainedColumns)
    {
        const int MinGuess = 0, PercentGuess = 1, SpecifiedGuess = 2, MaxGuess = 3;
        Span<double> guesses = stackalloc double[4];
        Span<double> increases = stackalloc double[4];
        var percentCount = 0;
        var fixedCount = 0;
        var autoCount = 0;
        var totalPercent = 0d;
        var totalAutoMax = 0d;
        var totalFixedMax = 0d;
        foreach (var column in columns)
        {
            if (column.Percent > 0)
            {
                percentCount++;
                totalPercent += column.Percent;
                var percentSize = column.ResolvePercent(target);
                guesses[MinGuess] += column.Min;
                guesses[PercentGuess] += percentSize;
                guesses[SpecifiedGuess] += percentSize;
                guesses[MaxGuess] += percentSize;
                increases[PercentGuess] += percentSize - column.Min;
            }
            else if (column.IsConstrained)
            {
                fixedCount++;
                totalFixedMax += column.Max;
                guesses[MinGuess] += column.Min;
                guesses[PercentGuess] += column.Min;
                guesses[SpecifiedGuess] += column.Max;
                guesses[MaxGuess] += column.Max;
                increases[SpecifiedGuess] += column.Max - column.Min;
            }
            else
            {
                autoCount++;
                totalAutoMax += column.Max;
                guesses[MinGuess] += column.Min;
                guesses[PercentGuess] += column.Min;
                guesses[SpecifiedGuess] += column.Min;
                guesses[MaxGuess] += column.Max;
                increases[MaxGuess] += column.Max - column.Min;
            }
        }

        // The columns are never narrower than their min-content width
        target = Math.Max(target, guesses[MinGuess]);
        var guess = 4;
        for (var i = 0; i < 4; i++)
        {
            if (guesses[i] >= target)
            {
                guess = i;
                break;
            }
        }

        switch (guess)
        {
            case MinGuess:
                for (var i = 0; i < columns.Length; i++)
                {
                    sizes[i] = columns[i].Min;
                }

                break;

            case PercentGuess:
            {
                // The percentage columns grow in proportion to the difference between their percentage size and their min-content width
                var distributable = target - guesses[MinGuess];
                for (var i = 0; i < columns.Length; i++)
                {
                    var column = columns[i];
                    if (column.Percent > 0)
                    {
                        var increase = column.ResolvePercent(target) - column.Min;
                        sizes[i] = column.Min + (increases[PercentGuess] > 0 ? distributable * increase / increases[PercentGuess] : distributable / percentCount);
                    }
                    else
                    {
                        sizes[i] = column.Min;
                    }
                }

                break;
            }

            case SpecifiedGuess:
            {
                // The constrained columns grow, the percentage columns have their percentage size, the auto columns their min-content width
                var distributable = target - guesses[PercentGuess];
                for (var i = 0; i < columns.Length; i++)
                {
                    var column = columns[i];
                    if (column.Percent > 0)
                    {
                        sizes[i] = column.ResolvePercent(target);
                    }
                    else if (column.IsConstrained)
                    {
                        var increase = column.Max - column.Min;
                        sizes[i] = column.Min + (increases[SpecifiedGuess] > 0 ? distributable * increase / increases[SpecifiedGuess] : distributable / fixedCount);
                    }
                    else
                    {
                        sizes[i] = column.Min;
                    }
                }

                break;
            }

            case MaxGuess:
            {
                // The auto columns grow; when the target is exactly the max-content guess, the columns get their max-content width
                var distributable = target - guesses[SpecifiedGuess];
                var isExact = target == guesses[MaxGuess];
                for (var i = 0; i < columns.Length; i++)
                {
                    var column = columns[i];
                    if (column.Percent > 0)
                    {
                        sizes[i] = column.ResolvePercent(target);
                    }
                    else if (column.IsConstrained || isExact)
                    {
                        sizes[i] = column.Max;
                    }
                    else
                    {
                        var increase = column.Max - column.Min;
                        sizes[i] = column.Min + (increases[MaxGuess] > 0 ? distributable * increase / increases[MaxGuess] : distributable / autoCount);
                    }
                }

                break;
            }

            default:
            {
                // Above the max-content guess: the excess goes to the auto columns, else to the constrained columns, else to the
                // percentage columns (https://drafts.csswg.org/css-tables-3/#distributing-width-to-columns)
                var distributable = target - guesses[MaxGuess];
                for (var i = 0; i < columns.Length; i++)
                {
                    var column = columns[i];
                    sizes[i] = column.Percent > 0 ? column.ResolvePercent(target) : column.Max;
                }

                if (autoCount > 0)
                {
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var column = columns[i];
                        if (column.Percent <= 0 && !column.IsConstrained)
                        {
                            sizes[i] += totalAutoMax > 0 ? distributable * column.Max / totalAutoMax : distributable / autoCount;
                        }
                    }
                }
                else if (fixedCount > 0 && growsConstrainedColumns)
                {
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var column = columns[i];
                        if (column.Percent <= 0 && column.IsConstrained)
                        {
                            sizes[i] += totalFixedMax > 0 ? distributable * column.Max / totalFixedMax : distributable / fixedCount;
                        }
                    }
                }
                else if (percentCount > 0)
                {
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var column = columns[i];
                        if (column.Percent > 0)
                        {
                            sizes[i] += totalPercent > 0 ? distributable * column.Percent / totalPercent : distributable / percentCount;
                        }
                    }
                }

                break;
            }
        }
    }

    /// <summary>
    /// The width distribution in fixed mode (https://drafts.csswg.org/css-tables-3/#width-distribution-in-fixed-mode): the
    /// constrained columns get their width, the percentage columns their percentage of the assignable width (scaled down to fit
    /// in the remaining width), the auto columns share the remaining width equally; without auto column, the excess goes to the
    /// constrained columns in proportion to their widths, else to the percentage columns in proportion to their percentages,
    /// else equally to all the columns.
    /// </summary>
    private static void DistributeFixed(double target, ReadOnlySpan<TableColumnMeasure> columns, Span<double> sizes)
    {
        var fixedTotal = 0d;
        var percentTotal = 0d;
        var totalPercent = 0d;
        var autoCount = 0;
        var fixedCount = 0;
        var percentCount = 0;
        foreach (var column in columns)
        {
            if (column.IsConstrained)
            {
                fixedCount++;
                fixedTotal += column.Max;
            }
            else if (column.Percent > 0)
            {
                percentCount++;
                totalPercent += column.Percent;
                percentTotal += (column.Percent * target / 100) + column.PercentEdges;
            }
            else
            {
                autoCount++;
            }
        }

        // "If, when percentages are resolved based on the assignable table width, the sum of columns widths based on this
        // resolution would exceed the assignable table width, they are instead to be resolved relative to their percentage value
        // such that the sum of columns width meets the assignable table width exactly"
        var percentScale = 1d;
        var remainingForPercent = Math.Max(0, target - fixedTotal);
        if (percentTotal > remainingForPercent && percentTotal > 0)
        {
            percentScale = remainingForPercent / percentTotal;
        }

        var used = 0d;
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            sizes[i] = column.IsConstrained ? column.Max : column.Percent > 0 ? ((column.Percent * target / 100) + column.PercentEdges) * percentScale : 0;
            used += sizes[i];
        }

        var excess = target - used;
        if (excess <= 0)
            return;

        if (autoCount > 0)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                if (!columns[i].IsConstrained && columns[i].Percent <= 0)
                {
                    sizes[i] += excess / autoCount;
                }
            }
        }
        else if (fixedCount > 0 && fixedTotal > 0)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                if (columns[i].IsConstrained)
                {
                    sizes[i] += excess * columns[i].Max / fixedTotal;
                }
            }
        }
        else if (percentCount > 0 && totalPercent > 0)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                if (!columns[i].IsConstrained && columns[i].Percent > 0)
                {
                    sizes[i] += excess * columns[i].Percent / totalPercent;
                }
            }
        }
        else
        {
            for (var i = 0; i < columns.Length; i++)
            {
                sizes[i] += excess / columns.Length;
            }
        }
    }
}
