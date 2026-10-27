using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The alignment of the contents of a table cell in its row (https://drafts.csswg.org/css-tables-3/#row-layout, <c>vertical-align</c>).</summary>
internal enum TableCellAlignment : byte
{
    Baseline,
    Top,
    Middle,
    Bottom,
}

internal sealed partial class TableLayoutAlgorithm
{
    /// <summary>The block sizes and baselines of a row.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct RowSizing
    {
        public double BlockSize;

        /// <summary>The distance between the block-start edge of the row and its baseline (the largest one of its baseline-aligned cells).</summary>
        public double Ascent;

        public double Descent;

        public bool HasBaselineCells;

        /// <summary>Whether the block size of the row is a length (the row or one of its cells): it grows last.</summary>
        public bool IsFixed;

        /// <summary>The percentage block size of the row (50 for 50%), or 0.</summary>
        public double Percent;

        /// <summary>Whether a cell that spans several rows originates in the row.</summary>
        public bool HasRowSpanningCell;

        /// <summary>The offset of the block-start edge of the row from the block-start edge of the border box of the table.</summary>
        public double Offset;
    }

    /// <summary>The measure and the layout of a cell.</summary>
    private sealed class CellLayout(TableCell cell)
    {
        public TableCell Cell { get; } = cell;

        public BoxFragment? Fragment;
        public double InlineSize;

        /// <summary>The block size of the cell: the block size of its contents, at least its specified height.</summary>
        public double BlockSize;

        /// <summary>The block size of the contents of the cell (its automatic block size, with its borders and paddings).</summary>
        public double ContentBlockSize;

        public double Baseline;
        public bool HasBaseline;
        public TableCellAlignment Alignment;

        /// <summary>
        /// Whether the contents of the cell are aligned even when they overflow it (https://drafts.csswg.org/css-align-3/#overflow-values:
        /// an <c>unsafe</c> <c>align-content</c>); otherwise the contents that are taller than the cell stay at its block-start edge.
        /// </summary>
        public bool IsUnsafeAlignment;

        /// <summary>
        /// Whether the cell is aligned with the baseline of its row and sets it: a cell without content has no baseline to align
        /// (as in the browsers, WPT css/css-tables/baseline-empty-cell-001.html: it does not move the contents of the other cells).
        /// </summary>
        public bool IsBaselineAligned => Alignment == TableCellAlignment.Baseline && (HasBaseline || Fragment is { Children.Count: > 0 });
    }

    /// <summary>The block size of a row group.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct SectionSizing
    {
        public double BlockSize;
        public bool IsFixed;
        public double Percent;
        public bool IsBody;
    }

    /// <summary>
    /// Lays out the table grid box (https://drafts.csswg.org/css-tables-3/#table-layout-algorithm) with the given border-box
    /// inline size (the used width of the table, computed by the table wrapper box) and returns its fragment.
    /// </summary>
    /// <param name="inlineSize">The used border-box inline size of the table grid box.</param>
    /// <param name="paddingBasis">The inline size of the containing block of the table (the basis of the percentages of its paddings).</param>
    /// <param name="percentageBlockSize">The block size of the containing block of the table when it is definite (the basis of a percentage <c>height</c>).</param>
    /// <param name="fixedBlockSize">The border-box block size imposed by the parent of the table (a stretched flex or grid item), or null.</param>
    public BoxFragment Layout(double inlineSize, double paddingBasis, double? percentageBlockSize, double? fixedBlockSize)
    {
        var model = Model;
        var border = Border;
        var padding = GetPadding(paddingBasis);
        var edgesInlineStart = border.InlineStart + padding.InlineStart;
        var edgesBlockStart = border.BlockStart + padding.BlockStart;
        var edgesInline = border.InlineSum() + padding.InlineSum();
        var edgesBlock = border.BlockSum() + padding.BlockSum();

        // https://drafts.csswg.org/css-tables-3/#width-distribution: the assignable table width is distributed to the columns
        var totalInlineSpacing = GetTotalInlineSpacing(visibleOnly: true);
        var widths = ComputeColumnWidths(Math.Max(0, inlineSize - edgesInline - totalInlineSpacing));
        var columnCount = widths.Length;
        var columnStarts = new double[columnCount + 1];
        var position = edgesInlineStart + (VisibleColumnCount > 0 ? InlineSpacing : 0);
        for (var i = 0; i < columnCount; i++)
        {
            columnStarts[i] = position;
            if (!IsColumnSkipped(i))
            {
                position += widths[i] + InlineSpacing;
            }
        }

        columnStarts[columnCount] = position;

        // The table is at least as wide as its columns (a width imposed by the parent smaller than GRIDMIN)
        // A table without column: its rows are as wide as its content box (https://github.com/w3c/csswg-drafts/issues/10132)
        var columnsEnd = VisibleColumnCount > 0 ? position - InlineSpacing : position;
        inlineSize = Math.Max(inlineSize, position + border.InlineEnd + padding.InlineEnd);

        // The inline size of the columns spanned by a cell, without the collapsed columns
        double GetCellInlineSize(TableCell cell)
        {
            var end = Math.Min(columnCount, cell.Column + cell.ColumnSpan);
            var start = cell.Column;
            if (start >= end)
                return 0;

            // The end edge of the last visible column spanned by the cell
            var endPosition = columnStarts[end] - InlineSpacing;
            for (var i = end - 1; i >= start && IsColumnSkipped(i); i--)
            {
                endPosition = columnStarts[i] - InlineSpacing;
            }

            return Math.Max(0, endPosition - columnStarts[start]);
        }

        // The percentages of the paddings of the cells resolve against the assignable width of the table (as in the browsers,
        // WPT css/css-tables/tentative/element-sizing.html), with the border spacing between the columns (see GetCellSpace)
        var contentInlineSize = Math.Max(0, inlineSize - edgesInline - totalInlineSpacing);

        // A table without column (its rows have no cell) has no border spacing between its rows, unless its height is
        // distributed to them (as in the browsers, WPT css/css-tables/tentative/table-rows-with-zero-columns.html)
        var specifiedBlockSize = fixedBlockSize ?? ResolveTableBlockSize(percentageBlockSize, edgesBlock);
        var isTableBlockSizeSpecified = specifiedBlockSize is not null || !_style.Box.GetSize(!_writingDirection.IsHorizontal).IsAuto;

        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: the block size transferred from the inline size is a minimum block
        // size of the table, like its height (as in the browsers)
        if (fixedBlockSize is null && _style.Box.AspectRatio.Ratio is { } ratio)
        {
            var blockPerInline = _writingDirection.IsHorizontal ? 1 / ratio : ratio;
            var ratioBlockSize = _style.BoxSizing == BoxSizing.BorderBox && !_style.Box.AspectRatio.IsAuto
                ? inlineSize * blockPerInline
                : (Math.Max(0, inlineSize - edgesInline) * blockPerInline) + edgesBlock;
            specifiedBlockSize = Math.Max(specifiedBlockSize ?? 0, ratioBlockSize);
        }

        if (VisibleColumnCount == 0 && !(specifiedBlockSize > edgesBlock))
        {
            _blockSpacing = 0;
        }

        // https://drafts.csswg.org/css-tables-3/#row-layout: the first layout of the cells, with their automatic block size
        var cells = new CellLayout[model.Cells.Count];
        var rows = new RowSizing[model.Rows.Count];
        var horizontal = _writingDirection.IsHorizontal;
        var blockIsHorizontal = !horizontal;
        for (var r = 0; r < rows.Length; r++)
        {
            var row = model.Rows[r];
            ref var rowSizing = ref rows[r];
            switch (Classify(row.Box.Style.Layout.Box.GetSize(blockIsHorizontal), out var rowHeight))
            {
                case TableSizeKind.Fixed:
                    rowSizing.BlockSize = Math.Max(0, rowHeight);
                    rowSizing.IsFixed = true;
                    break;
                case TableSizeKind.Percent:
                    rowSizing.Percent = rowHeight;
                    break;
            }
        }

        for (var i = 0; i < cells.Length; i++)
        {
            var cell = model.Cells[i];
            ref var row = ref rows[cell.Row];

            // The block size of a cell is restricted by a block size of the table (even a percentage that does not resolve, as in
            // the browsers: WPT css/css-tables/height-distribution/percentage-sizing-of-table-cell-007.html), or by the height of
            // its row group, its row or the cell itself when it is a length (a percentage of an automatic block size restricts nothing)
            var cellHeightKind = Classify(cell.Box.Style.Layout.Box.GetSize(blockIsHorizontal), out var cellHeight);
            var isRestricted = isTableBlockSizeSpecified || cellHeightKind == TableSizeKind.Fixed || IsFixedBlockSize(model.Rows[cell.Row].Box)
                || (model.Sections[model.Rows[cell.Row].Section].Box is { } cellSection && IsFixedBlockSize(cellSection));
            var layout = cells[i] = MeasureCellBlockSize(cell, GetCellInlineSize(cell), contentInlineSize, isRestricted);
            if (cell.RowSpan > 1)
            {
                row.HasRowSpanningCell = true;
            }

            switch (cellHeightKind)
            {
                case TableSizeKind.Fixed when cell.RowSpan == 1:
                    row.IsFixed = true;
                    break;
                case TableSizeKind.Percent when cell.RowSpan == 1:
                    row.Percent = Math.Max(row.Percent, cellHeight);
                    break;
            }

            // "cells spanning multiple rows are considered having a height of 0px": they do not change the row (as in the browsers,
            // their baseline neither)
            if (cell.RowSpan > 1)
                continue;

            // The baselines are aligned with the contents of the cells; the specified height of a cell is a minimum height of its row
            if (layout.IsBaselineAligned)
            {
                row.HasBaselineCells = true;
                row.Ascent = Math.Max(row.Ascent, layout.Baseline);
                row.Descent = Math.Max(row.Descent, layout.ContentBlockSize - layout.Baseline);
            }

            row.BlockSize = Math.Max(row.BlockSize, layout.BlockSize);
        }

        for (var r = 0; r < rows.Length; r++)
        {
            ref var row = ref rows[r];
            if (row.HasBaselineCells)
            {
                row.BlockSize = Math.Max(row.BlockSize, row.Ascent + row.Descent);
            }
        }

        // The cells that span several rows distribute their block size to the rows
        DistributeRowSpanningCells(cells, rows);

        // https://drafts.csswg.org/css-tables-3/#computing-the-table-height: the row groups, then the table height
        var sections = new SectionSizing[model.Sections.Count];
        var visibleRows = 0;
        var sectionsWithRows = 0;
        for (var s = 0; s < sections.Length; s++)
        {
            var section = model.Sections[s];
            ref var sizing = ref sections[s];
            sizing.IsBody = section.Box is null || section.Box.Display.Internal == DisplayInternal.TableRowGroup;
            sizing.BlockSize = GetRowsBlockSize(rows, section.StartRow, section.RowCount);
            var sectionRows = section.RowCount;
            visibleRows += sectionRows;
            if (sectionRows > 0)
            {
                sectionsWithRows++;
            }

            if (section.Box is { } sectionBox)
            {
                switch (Classify(sectionBox.Style.Layout.Box.GetSize(blockIsHorizontal), out var sectionHeight))
                {
                    case TableSizeKind.Fixed:
                        sizing.IsFixed = true;
                        if (sectionHeight > sizing.BlockSize)
                        {
                            sizing.BlockSize = sectionHeight;
                            DistributeExcessBlockSize(rows, section.StartRow, section.RowCount, sectionHeight, isRowSpanDistribution: false, percentageBasis: sectionHeight);
                        }

                        break;
                    case TableSizeKind.Percent:
                        sizing.Percent = sectionHeight;
                        break;
                }
            }
        }

        // The border spacing before, between and after the row groups (the one between their rows is in their block size)
        var totalBlockSpacing = visibleRows > 0 ? BlockSpacing * (sectionsWithRows + 1) : 0;
        var contentsBlockSize = totalBlockSpacing;
        foreach (var section in sections)
        {
            contentsBlockSize += section.BlockSize;
        }

        var intrinsicBlockSize = contentsBlockSize + edgesBlock;
        var tableBlockSize = intrinsicBlockSize;
        if (specifiedBlockSize is { } specified && specified > tableBlockSize)
        {
            tableBlockSize = specified;
            DistributeTableBlockSize(sections, rows, tableBlockSize - edgesBlock - totalBlockSpacing);
        }

        // https://drafts.csswg.org/css-tables-3/#bounding-box-assignment: the positions of the rows. The collapsed rows take no
        // space (https://drafts.csswg.org/css-tables-3/#visible-track): the table is shorter
        var collapsedBlockSize = 0d;
        var hasVisibleRow = false;
        for (var r = 0; r < rows.Length; r++)
        {
            if (model.Rows[r].IsCollapsed)
            {
                collapsedBlockSize += rows[r].BlockSize + BlockSpacing;
            }
            else
            {
                hasVisibleRow = true;
            }
        }

        if (!hasVisibleRow && rows.Length > 0)
        {
            collapsedBlockSize -= BlockSpacing;
        }

        tableBlockSize -= collapsedBlockSize;
        var blockPosition = edgesBlockStart + (hasVisibleRow ? BlockSpacing : 0);
        var sectionStarts = new double[sections.Length];
        var sectionSizes = new double[sections.Length];
        for (var s = 0; s < sections.Length; s++)
        {
            var section = model.Sections[s];
            sectionStarts[s] = blockPosition;
            var hasVisibleRows = false;
            for (var r = section.StartRow; r < section.EndRow; r++)
            {
                rows[r].Offset = blockPosition;
                if (!model.Rows[r].IsCollapsed)
                {
                    hasVisibleRows = true;
                    blockPosition += rows[r].BlockSize + BlockSpacing;
                }
            }

            if (hasVisibleRows)
            {
                sectionSizes[s] = blockPosition - BlockSpacing - sectionStarts[s];
            }
            else if (section.RowCount == 0)
            {
                sectionSizes[s] = sections[s].BlockSize;
                blockPosition += sections[s].BlockSize;
            }
        }

        tableBlockSize = Math.Max(tableBlockSize, blockPosition + border.BlockEnd + padding.BlockEnd);

        // The final layout of the cells in the block size of the rows they span, and the alignment of their contents
        var builder = new FragmentBuilder(_writingDirection);
        var isHeightExplicit = IsTableHeightExplicit(percentageBlockSize);
        double? firstBaseline = null;
        double? lastBaseline = null;
        for (var i = 0; i < cells.Length; i++)
        {
            var cellLayout = cells[i];
            var cell = cellLayout.Cell;
            var start = rows[cell.Row].Offset;
            var end = start;
            for (var r = cell.Row; r <= cell.EndRow; r++)
            {
                if (!model.Rows[r].IsCollapsed)
                {
                    end = rows[r].Offset + rows[r].BlockSize;
                }
            }

            LayoutCell(cellLayout, Math.Max(0, end - start), contentInlineSize, isHeightExplicit);
        }

        // The baselines of the rows from the final layout of their cells (their percentage block sizes are resolved), then the
        // alignment of the contents of the cells
        for (var r = 0; r < rows.Length; r++)
        {
            if (!rows[r].HasBaselineCells)
                continue;

            var ascent = 0d;
            var first = model.Rows[r].FirstCell;
            for (var i = first; i < first + model.Rows[r].CellCount; i++)
            {
                if (cells[i].Cell.RowSpan == 1 && cells[i].IsBaselineAligned)
                {
                    ascent = Math.Max(ascent, cells[i].Baseline);
                }
            }

            rows[r].Ascent = ascent;
        }

        foreach (var cellLayout in cells)
        {
            AlignCell(cellLayout, rows[cellLayout.Cell.Row]);
        }

        // The baselines of the table: its first and last rows, even when they are empty (https://drafts.csswg.org/css-tables-3/#row-layout,
        // WPT css/css-tables/tentative/baseline-table.html)
        for (var r = 0; r < rows.Length; r++)
        {
            if (model.Rows[r].IsCollapsed)
                continue;

            var baseline = rows[r].Offset + GetRowBaseline(cells, r, rows[r]);
            firstBaseline ??= baseline;
            lastBaseline = baseline;
        }

        // The fragments: the columns and the column groups, then the row groups, the rows and the cells
        var rowsStart = edgesBlockStart + (hasVisibleRow ? BlockSpacing : 0);
        var rowsEnd = hasVisibleRow ? blockPosition - BlockSpacing : blockPosition;
        var columnsStart = columnStarts[0];
        if (VisibleColumnCount == 0)
        {
            columnsStart = edgesInlineStart;
            columnsEnd = Math.Max(columnsStart, inlineSize - border.InlineEnd - padding.InlineEnd);
        }

        AddColumnFragments(builder, columnStarts, rowsStart, rowsEnd);

        var cellIndex = 0;
        for (var s = 0; s < sections.Length; s++)
        {
            var section = model.Sections[s];
            var sectionBuilder = section.Box is null ? builder : new FragmentBuilder(_writingDirection);
            var origin = section.Box is null ? new LogicalOffset(0, 0) : new LogicalOffset(columnsStart, sectionStarts[s]);
            for (var r = section.StartRow; r < section.EndRow; r++)
            {
                var row = model.Rows[r];
                var rowBuilder = new FragmentBuilder(_writingDirection);
                var rowBlockSize = row.IsCollapsed ? 0 : rows[r].BlockSize;
                for (; cellIndex < cells.Length && cells[cellIndex].Cell.Row == r; cellIndex++)
                {
                    var cellLayout = cells[cellIndex];
                    if (cellLayout.Cell.Box.Style.Layout.Position == PositionKind.Relative && GetSpecifiedBlockSize(row.Box) is { } rowBlockBasis)
                    {
                        PositionedLayout.SetRelativeOffset(cellLayout.Cell.Box, cellLayout.Fragment!, GetPartSpace(contentInlineSize, rowBlockBasis));
                    }

                    rowBuilder.AddChild(cellLayout.Fragment!, new LogicalOffset(columnStarts[cellLayout.Cell.Column] - columnsStart, 0));
                }

                var rowSize = _writingDirection.ToPhysical(new LogicalSize(Math.Max(0, columnsEnd - columnsStart), rowBlockSize));
                var rowFragment = new BoxFragment(row.Box, rowSize, default, default, default, rowBuilder.Build(rowSize));
                PositionedLayout.SetRelativeOffset(row.Box, rowFragment, GetPartSpace(contentInlineSize, GetSpecifiedBlockSize(section.Box ?? _grid)));
                PositionedLayout.LayoutOutOfFlowDescendants(_context, row.Box, rowFragment, rowBuilder);
                sectionBuilder.AddChild(rowFragment, new LogicalOffset(columnsStart, rows[r].Offset) - origin);
            }

            if (section.Box is { } sectionBox)
            {
                var sectionSize = _writingDirection.ToPhysical(new LogicalSize(Math.Max(0, columnsEnd - columnsStart), Math.Max(0, sectionSizes[s])));
                var sectionFragment = new BoxFragment(sectionBox, sectionSize, default, default, default, sectionBuilder.Build(sectionSize));
                PositionedLayout.SetRelativeOffset(sectionBox, sectionFragment, GetPartSpace(contentInlineSize, GetSpecifiedBlockSize(_grid)));
                PositionedLayout.LayoutOutOfFlowDescendants(_context, sectionBox, sectionFragment, sectionBuilder);
                builder.AddChild(sectionFragment, origin);
            }
        }

        var size = _writingDirection.ToPhysical(new LogicalSize(inlineSize, tableBlockSize));
        var fragment = new BoxFragment(_grid, size, _writingDirection.ToPhysical(border), _writingDirection.ToPhysical(padding), default, builder.Build(size))
        {
            FirstBaseline = firstBaseline,
            LastBaseline = lastBaseline,
            ContentsBlockSize = Math.Max(0, tableBlockSize - edgesBlock),
            IntrinsicBlockSize = intrinsicBlockSize,
        };

        PositionedLayout.LayoutOutOfFlowDescendants(_context, _grid, fragment, builder);
        return fragment;
    }

    /// <summary>The constraint space of the internal table boxes (the basis of their relative offsets): the content box of the table.</summary>
    private ConstraintSpace GetPartSpace(double contentInlineSize, double? blockSize = null) => ConstraintSpace.ForContentBox(_writingDirection, contentInlineSize, blockSize);

    /// <summary>
    /// Returns the specified block size of the table or of one of its parts when it is a length: the basis of the percentage
    /// insets, in the block axis, of its relatively positioned children (a row group in the table, a row in its row group, a
    /// cell in its row). As in the browsers, they do not resolve against the used block size, and they behave as <c>auto</c>
    /// when the block size is not a length (WPT css/css-position/position-relative-011.html).
    /// </summary>
    private double? GetSpecifiedBlockSize(ContainerBox box)
        => Classify(box.Style.Layout.Box.GetSize(!_writingDirection.IsHorizontal), out var size) == TableSizeKind.Fixed ? size : null;

    /// <summary>Returns whether the block size of a part of the table is a length.</summary>
    private bool IsFixedBlockSize(ContainerBox box) => Classify(box.Style.Layout.Box.GetSize(!_writingDirection.IsHorizontal), out _) == TableSizeKind.Fixed;

    /// <summary>
    /// Returns the border-box block size given by the <c>height</c> of the table grid box (https://drafts.csswg.org/css-tables-3/#computing-the-table-height):
    /// "If the table has a height property with a value other than auto, it is treated as a minimum height for the table grid"
    /// (its <c>min-height</c> too), or null.
    /// </summary>
    private double? ResolveTableBlockSize(double? percentageBlockSize, double edges)
    {
        var blockIsHorizontal = !_writingDirection.IsHorizontal;
        var sizing = new AxisSizing(_context, _grid)
        {
            Horizontal = blockIsHorizontal,
            IsInlineAxis = false,
            PercentageBasis = percentageBlockSize,
            PaddingAndBorder = edges,
            Edges = edges,
            StretchSize = percentageBlockSize,
        };

        var boxModel = _style.Box;
        var height = sizing.Resolve(boxModel.GetSize(blockIsHorizontal), SizingProperty.Preferred);
        if (height is not null && sizing.Resolve(boxModel.GetMaxSize(blockIsHorizontal), SizingProperty.Maximum) is { } max)
        {
            height = Math.Min(height.Value, max);
        }

        if (sizing.Resolve(boxModel.GetMinSize(blockIsHorizontal), SizingProperty.Minimum) is { } min)
        {
            height = Math.Max(height ?? 0, min);
        }

        return height;
    }

    /// <summary>
    /// Returns whether the cells are "considered to have their height specified explicitly" for the percentages of their
    /// children (https://drafts.csswg.org/css-tables-3/#table-cell-content-layout-second-pass): "if the computed height of its
    /// table-root ancestor is a length or percentage" (the computed height of the cell is checked per cell). As in the
    /// browsers, a percentage that does not resolve (the block size of the containing block of the table is indefinite) behaves
    /// as <c>auto</c> (WPT css/css-tables/percent-height-table-cell-child.html).
    /// </summary>
    private bool IsTableHeightExplicit(double? percentageBlockSize) => _style.Box.GetSize(!_writingDirection.IsHorizontal).TryResolve(percentageBlockSize) is not null;

    /// <summary>
    /// Returns the block size of a range of rows, with the border spacing between them. The collapsed rows are sized as the
    /// visible ones: they are removed once the rows are sized (as in the browsers, WPT css/css-tables/visibility-collapse-row-004.html).
    /// </summary>
    private double GetRowsBlockSize(RowSizing[] rows, int start, int count)
    {
        var size = 0d;
        for (var r = start; r < start + count; r++)
        {
            size += rows[r].BlockSize;
        }

        return size + (count > 1 ? BlockSpacing * (count - 1) : 0);
    }

    /// <summary>
    /// Lays out a cell with its automatic block size, in the inline size of the columns it spans (the first pass of the row
    /// layout, https://drafts.csswg.org/css-tables-3/#row-layout), and returns its block size (at least its specified height)
    /// and its baseline.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="inlineSize">The inline size of the columns that the cell spans.</param>
    /// <param name="contentInlineSize">The assignable inline size of the table.</param>
    /// <param name="isRestricted">Whether the block size of the cell is restricted (see <see cref="ConstraintSpace.IsRestrictedTableCell"/>).</param>
    private CellLayout MeasureCellBlockSize(TableCell cell, double inlineSize, double contentInlineSize, bool isRestricted)
    {
        var cellStyle = cell.Box.Style.Layout;
        var result = new CellLayout(cell)
        {
            InlineSize = inlineSize,
            Alignment = GetAlignment(cell.Box, cellStyle),
            IsUnsafeAlignment = cellStyle.Alignment.AlignContent is { Overflow: OverflowAlignment.Unsafe, Keyword: not AlignmentKeyword.Normal },
        };
        var space = GetCellSpace(cell, inlineSize, contentInlineSize) with { MeasuresIntrinsicBlockSize = true, IsRestrictedTableCell = isRestricted };
        var fragment = _context.LayoutBox(cell.Box, space);
        result.Fragment = fragment;

        // The specified block size of the cell is a minimum: the contents make it taller, whatever its overflow (as in the
        // browsers, WPT css/css-tables/table-cell-overflow-explicit-height-001.html)
        var contentBlockSize = fragment.WritingDirection.IsOrthogonalTo(_writingDirection) ? _writingDirection.ToLogical(fragment.Size).Block : fragment.IntrinsicBlockSize;
        result.HasBaseline = TryGetCellBaseline(cell, fragment, out result.Baseline);

        // In a fragmentation context, the monolithic contents of the cell that overflow a fragmentainer take no block size in the
        // next ones: the cell is shorter once fragmented (see StripRelayout.GetCellBlockSizeReduction)
        if (_context.StripRelayout is { } stripRelayout)
        {
            stripRelayout.RecordTableCell(cell.Box);
            if (stripRelayout.GetCellBlockSizeReduction(cell.Box) is var reduction and > 0)
            {
                contentBlockSize = Math.Max(0, contentBlockSize - reduction);
                if (!result.HasBaseline)
                {
                    // (the synthesized baseline is the block-end content edge)
                    result.Baseline = Math.Max(0, result.Baseline - reduction);
                }
            }
        }

        result.ContentBlockSize = contentBlockSize;
        result.BlockSize = Math.Max(contentBlockSize, GetSpecifiedCellBlockSize(cell, fragment));
        return result;
    }

    /// <summary>
    /// Returns the border-box block size given by the <c>height</c> of a cell (the percentages are ignored; as in the browsers,
    /// <c>min-height</c> and <c>max-height</c> do not apply to the cells: WPT css/css-logical/logicalprops-block-size.html),
    /// with the table cell height box sizing quirk (https://quirks.spec.whatwg.org/#the-table-cell-height-box-sizing-quirk): "In
    /// quirks mode, elements that have a computed value of the display property of table-cell must act as they have used value
    /// of the box-sizing property of border-box, but only for the purpose of the height, min-height and max-height properties".
    /// </summary>
    private double GetSpecifiedCellBlockSize(TableCell cell, BoxFragment fragment)
    {
        var layout = cell.Box.Style.Layout;
        var blockIsHorizontal = !_writingDirection.IsHorizontal;
        var edges = _writingDirection.ToLogical(fragment.Border.Add(fragment.Padding)).BlockSum();
        var isBorderBox = layout.BoxSizing == BoxSizing.BorderBox || _context.IsQuirksMode;
        var result = 0d;
        if (Classify(layout.Box.GetSize(blockIsHorizontal), out var height) == TableSizeKind.Fixed)
        {
            result = isBorderBox ? Math.Max(height, edges) : height + edges;
        }

        return result;
    }

    /// <summary>
    /// The constraint space of a cell: the content box of the table is its containing block, its inline size is the one of its
    /// columns. The percentages of its paddings resolve against the inline size of the columns with the border spacing between
    /// them: the content box of the table "minus the leading and trailing border spacing" (as in Blink and Gecko, WPT
    /// css/css-tables/tentative/padding-percentage.html).
    /// </summary>
    private ConstraintSpace GetCellSpace(TableCell cell, double inlineSize, double contentInlineSize) => new()
    {
        WritingDirection = _writingDirection,
        AvailableInlineSize = inlineSize,
        PercentageInlineSize = contentInlineSize + (Math.Max(0, VisibleColumnCount - 1) * InlineSpacing),
        FixedInlineSize = inlineSize,
        TableCellBorder = IsCollapsed ? _writingDirection.ToPhysical(cell.Border) : null,
        QuirksPercentageBlockSize = null,
    };

    /// <summary>
    /// Returns the baseline of a cell (https://drafts.csswg.org/css-tables-3/#row-layout): "the baseline of the first in-flow line
    /// box in the cell, or the first in-flow table-row in the cell, whichever comes first. If there is no such line box or
    /// table-row, the baseline is the bottom of content edge of the cell box". Returns whether the cell has a baseline.
    /// </summary>
    private bool TryGetCellBaseline(TableCell cell, BoxFragment fragment, out double baseline)
    {
        if (fragment.FirstBaseline is { } first && fragment.WritingDirection.BlockStart == _writingDirection.BlockStart)
        {
            baseline = first;
            return true;
        }

        // The bottom of the content box of the cell before it is stretched to its rows: its automatic block size or its specified
        // height (the cell box is at least that tall), without its block-end border and padding
        var edges = _writingDirection.ToLogical(fragment.Border.Add(fragment.Padding));
        var blockSize = fragment.WritingDirection.IsOrthogonalTo(_writingDirection) ? _writingDirection.ToLogical(fragment.Size).Block : fragment.IntrinsicBlockSize;
        blockSize = Math.Max(blockSize, GetSpecifiedCellBlockSize(cell, fragment));
        baseline = SynthesizeBaseline(edges.BlockStart, Math.Max(0, blockSize - edges.BlockEnd));
        return false;
    }

    /// <summary>
    /// Returns the alignment of a cell (https://drafts.csswg.org/css-tables-3/#row-layout): "Other values do not apply to cells;
    /// the cell is aligned at the baseline instead". https://drafts.csswg.org/css-align-3/#distribution-block: "For table cells,
    /// the behavior of align-content: normal depends on the computed value of vertical-align", the other values align the
    /// contents of the cell (a distribution uses its fallback alignment, <c>last baseline</c> is <c>end</c>).
    /// </summary>
    private static TableCellAlignment GetAlignment(ContainerBox cell, LayoutStyle style)
    {
        if (cell.Style.GetComputedValue("align-content") != "normal")
        {
            var alignContent = style.Alignment.AlignContent;
            if (alignContent.Keyword == AlignmentKeyword.Baseline)
                return TableCellAlignment.Baseline;

            var axis = AlignmentAxis.Block(WritingDirection.FromStyle(style));
            return BoxAlignment.ResolveContentAlignment(alignContent.Keyword, axis, AxisAlignment.Start) switch
            {
                AxisAlignment.End => TableCellAlignment.Bottom,
                AxisAlignment.Center or AxisAlignment.SpaceAround or AxisAlignment.SpaceEvenly => TableCellAlignment.Middle,
                _ => TableCellAlignment.Top,
            };
        }

        var verticalAlign = style.VerticalAlign;
        return verticalAlign.BaselineShift.Kind switch
        {
            BaselineShiftKind.Top => TableCellAlignment.Top,
            BaselineShiftKind.Bottom => TableCellAlignment.Bottom,
            BaselineShiftKind.Center => TableCellAlignment.Middle,
            _ => verticalAlign.AlignmentBaseline == AlignmentBaseline.Middle ? TableCellAlignment.Middle : TableCellAlignment.Baseline,
        };
    }

    /// <summary>
    /// Lays out a cell in the block size of the rows it spans (https://drafts.csswg.org/css-tables-3/#table-cell-content-layout-second-pass):
    /// the percentage block sizes of its children resolve against it when the cell is "considered to have its height specified
    /// explicitly". The first layout is kept when it has the final size and the percentages are not resolved.
    /// </summary>
    private void LayoutCell(CellLayout layout, double blockSize, double contentInlineSize, bool isTableHeightExplicit)
    {
        var cell = layout.Cell;
        var measured = layout.Fragment!;
        var blockIsHorizontal = !_writingDirection.IsHorizontal;
        var isExplicit = isTableHeightExplicit || Classify(cell.Box.Style.Layout.Box.GetSize(blockIsHorizontal), out _) == TableSizeKind.Fixed;
        BoxFragment fragment;
        if (!isExplicit && _writingDirection.ToLogical(measured.Size).Block == blockSize && (cell.Box.Style.Layout.ContainerType & (ContainerType.Size | ContainerType.InlineSize)) == ContainerType.Normal)
        {
            fragment = measured;
        }
        else
        {
            var space = GetCellSpace(cell, layout.InlineSize, contentInlineSize) with { FixedBlockSize = blockSize, IsFixedBlockSizeIndefinite = !isExplicit };
            fragment = _context.LayoutBox(cell.Box, space);
        }

        layout.Fragment = fragment;
        layout.BlockSize = blockSize;
        layout.HasBaseline = TryGetCellBaseline(cell, fragment, out layout.Baseline);
        if (!layout.HasBaseline && _context.StripRelayout?.GetCellBlockSizeReduction(cell.Box) is > 0 and var reduction)
        {
            // (the synthesized baseline is the block-end content edge, see MeasureCellBlockSize)
            layout.Baseline = Math.Max(0, layout.Baseline - reduction);
        }
    }

    /// <summary>
    /// Aligns the contents of a cell in its block size (https://drafts.csswg.org/css-tables-3/#row-layout): "cell boxes that are
    /// smaller than the collective height of the rows they span receive extra top and/or bottom padding such that their content
    /// does not move vertically but their top/bottom edges meet the ones of the first/last row they span". The contents of an
    /// orthogonal cell are aligned in the block axis of the cell (the inline axis of the table, where its size is the one of its
    /// columns): <c>top</c> is its block-start edge and <c>bottom</c> its block-end edge, the baseline alignment is <c>top</c>
    /// (as in the browsers).
    /// </summary>
    private void AlignCell(CellLayout layout, in RowSizing row)
    {
        var fragment = layout.Fragment!;
        var isOrthogonal = fragment.WritingDirection.IsOrthogonalTo(_writingDirection);
        var alignmentDirection = isOrthogonal ? fragment.WritingDirection : _writingDirection;
        var blockSize = isOrthogonal ? fragment.WritingDirection.ToLogical(fragment.Size).Block : layout.BlockSize;
        var contentsBlockSize = fragment.IntrinsicBlockSize;
        var offset = layout.Alignment switch
        {
            TableCellAlignment.Middle => (blockSize - contentsBlockSize) / 2,
            TableCellAlignment.Bottom => blockSize - contentsBlockSize,
            TableCellAlignment.Baseline when row.HasBaselineCells && layout.IsBaselineAligned && !isOrthogonal => row.Ascent - layout.Baseline,
            _ => 0,
        };

        if (offset > 0 || (offset < 0 && layout.IsUnsafeAlignment && layout.Alignment != TableCellAlignment.Baseline))
        {
            var physical = alignmentDirection.BlockStart switch
            {
                PhysicalSide.Top => new PhysicalOffset(0, offset),
                PhysicalSide.Bottom => new PhysicalOffset(0, -offset),
                PhysicalSide.Left => new PhysicalOffset(offset, 0),
                _ => new PhysicalOffset(-offset, 0),
            };

            fragment.MoveInFlowChildren(physical);
            fragment.FirstBaseline += offset;
            fragment.LastBaseline += offset;
        }
    }

    /// <summary>
    /// Returns the baseline of a row (https://drafts.csswg.org/css-tables-3/#row-layout): "The maximum distance between the top of
    /// the cell box and the baseline over all cells that have vertical-align: baseline is used to set the baseline of the row.
    /// If a row doesn't have any cell that has vertical-align: baseline, the baseline of that row is the bottom content edge of
    /// the lowest cell in the row" (see <see cref="SynthesizeBaseline"/> in a vertical writing mode).
    /// </summary>
    private double GetRowBaseline(CellLayout[] cells, int row, in RowSizing sizing)
    {
        if (sizing.HasBaselineCells)
            return sizing.Ascent;

        // A row without cell: its block-start edge (as in the browsers)
        var baseline = 0d;
        var first = Model.Rows[row].FirstCell;
        for (var i = first; i < first + Model.Rows[row].CellCount; i++)
        {
            var fragment = cells[i].Fragment!;
            var edges = _writingDirection.ToLogical(fragment.Border.Add(fragment.Padding));
            baseline = Math.Max(baseline, SynthesizeBaseline(edges.BlockStart, Math.Min(sizing.BlockSize, _writingDirection.ToLogical(fragment.Size).Block) - edges.BlockEnd));
        }

        return baseline;
    }

    /// <summary>
    /// Returns the baseline synthesized from the content box of a cell (https://drafts.csswg.org/css-align-3/#synthesize-baseline),
    /// from its block-start border edge: its block-end content edge (the alphabetic baseline of a horizontal table), or the
    /// middle of its content box when the dominant baseline of the table is the central baseline (a vertical writing mode,
    /// unless <c>text-orientation</c> is <c>sideways</c>: see <see cref="LayoutStyle.HasCentralDominantBaseline"/>).
    /// </summary>
    /// <param name="contentStart">The block-start content edge of the cell.</param>
    /// <param name="contentEnd">The block-end content edge of the cell.</param>
    private double SynthesizeBaseline(double contentStart, double contentEnd)
        => !_writingDirection.IsHorizontal && _style.HasCentralDominantBaseline ? (contentStart + Math.Max(contentStart, contentEnd)) / 2 : contentEnd;

    /// <summary>Adds the fragments of the table-column and table-column-group boxes (their geometry for the CSSOM: https://drafts.csswg.org/css-tables-3/#bounding-box-assignment).</summary>
    private void AddColumnFragments(FragmentBuilder builder, double[] columnStarts, double rowsStart, double rowsEnd)
    {
        var columnCount = columnStarts.Length - 1;
        foreach (var columnBox in Model.ColumnBoxes)
        {
            var start = Math.Min(columnBox.StartColumn, columnCount);
            var end = Math.Min(columnBox.StartColumn + columnBox.ColumnCount, columnCount);
            var inlineStart = columnStarts[start];
            var inlineEnd = inlineStart;
            for (var i = start; i < end; i++)
            {
                if (!IsColumnSkipped(i))
                {
                    inlineEnd = columnStarts[i + 1] - InlineSpacing;
                }
            }

            var size = _writingDirection.ToPhysical(new LogicalSize(Math.Max(0, inlineEnd - inlineStart), Math.Max(0, rowsEnd - rowsStart)));
            builder.AddChild(new BoxFragment(columnBox.Box, size, default, default, default, children: null), new LogicalOffset(inlineStart, rowsStart));
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Height distribution
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Distributes the block sizes of the cells that span several rows to the rows they span (the row layout,
    /// https://drafts.csswg.org/css-tables-3/#row-layout), as the browsers: the cells are sorted by their last row, then by their
    /// first row (the enclosed cells first), then by their block size (the taller first).
    /// </summary>
    private void DistributeRowSpanningCells(CellLayout[] cells, RowSizing[] rows)
    {
        List<CellLayout>? spanning = null;
        foreach (var cell in cells)
        {
            if (cell.Cell.RowSpan > 1)
            {
                (spanning ??= []).Add(cell);
            }
        }

        if (spanning is null)
            return;

        var ordered = spanning.OrderBy(cell => cell.Cell.EndRow).ThenByDescending(cell => cell.Cell.Row).ThenByDescending(cell => cell.BlockSize).ToList();
        foreach (var cell in ordered)
        {
            DistributeExcessBlockSize(rows, cell.Cell.Row, cell.Cell.RowSpan, cell.BlockSize, isRowSpanDistribution: true, percentageBasis: null);
        }
    }

    /// <summary>
    /// Distributes the block size in excess of the sizes of a range of rows to them (https://drafts.csswg.org/css-tables-3/#height-distribution-algorithm,
    /// the algorithm of the browsers, https://github.com/w3c/csswg-drafts/issues/4418): the percentage rows grow to their
    /// percentage size first, then (for a cell spanning the rows) the rows where another spanning cell originates grow equally,
    /// else the non-empty rows whose block size is not fixed grow in proportion to their sizes, else the empty rows (the last
    /// one for a spanning cell, the ones whose block size is not fixed for a row group or a table), else the rows with a fixed
    /// block size grow in proportion to their sizes.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="start">The first row of the range.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="desiredBlockSize">The block size of the range (with the border spacing between its rows).</param>
    /// <param name="isRowSpanDistribution">Whether the size is the one of a cell that spans the rows.</param>
    /// <param name="percentageBasis">The basis of the percentage rows, or null when it is indefinite (they are not fixed then).</param>
    private void DistributeExcessBlockSize(RowSizing[] rows, int start, int count, double desiredBlockSize, bool isRowSpanDistribution, double? percentageBasis)
    {
        var excess = desiredBlockSize - GetRowsBlockSize(rows, start, count);
        if (excess <= 0)
            return;

        var end = start + count;

        // 1. The percentage rows grow to their percentage size, in proportion to their deficit
        if (percentageBasis is { } basis)
        {
            var totalDeficit = 0d;
            for (var r = start; r < end; r++)
            {
                if (rows[r].Percent > 0)
                {
                    totalDeficit += Math.Max(0, (rows[r].Percent * basis / 100) - rows[r].BlockSize);
                }
            }

            if (totalDeficit > 0)
            {
                var distributed = Math.Min(totalDeficit, excess);
                for (var r = start; r < end; r++)
                {
                    if (rows[r].Percent > 0)
                    {
                        var deficit = Math.Max(0, (rows[r].Percent * basis / 100) - rows[r].BlockSize);
                        rows[r].BlockSize += distributed * deficit / totalDeficit;
                    }
                }

                excess -= distributed;
                if (excess <= 0)
                    return;
            }
        }

        bool IsConstrained(in RowSizing row) => row.IsFixed || (row.Percent > 0 && percentageBasis is not null);

        // 2. A spanning cell: the rows where another spanning cell originates grow equally
        if (isRowSpanDistribution)
        {
            var spanningRows = 0;
            for (var r = start + 1; r < end; r++)
            {
                if (rows[r].HasRowSpanningCell)
                {
                    spanningRows++;
                }
            }

            if (spanningRows > 0)
            {
                for (var r = start + 1; r < end; r++)
                {
                    if (rows[r].HasRowSpanningCell)
                    {
                        rows[r].BlockSize += excess / spanningRows;
                    }
                }

                return;
            }
        }

        // 3. The non-empty rows that are not constrained, in proportion to their sizes
        var unconstrainedSize = 0d;
        var constrainedSize = 0d;
        var constrainedCount = 0;
        var emptyUnconstrained = 0;
        var empty = 0;
        var lastEmpty = -1;
        for (var r = start; r < end; r++)
        {
            ref var row = ref rows[r];
            if (row.BlockSize == 0)
            {
                empty++;
                lastEmpty = r;
                if (!IsConstrained(row))
                {
                    emptyUnconstrained++;
                }
            }
            else if (IsConstrained(row))
            {
                constrainedCount++;
                constrainedSize += row.BlockSize;
            }
            else
            {
                unconstrainedSize += row.BlockSize;
            }
        }

        if (unconstrainedSize > 0)
        {
            for (var r = start; r < end; r++)
            {
                if (rows[r].BlockSize > 0 && !IsConstrained(rows[r]))
                {
                    rows[r].BlockSize += excess * rows[r].BlockSize / unconstrainedSize;
                }
            }

            return;
        }

        // 4. The empty rows
        if (empty > 0)
        {
            if (isRowSpanDistribution)
            {
                // All the rows are empty (or constrained): the last empty row gets the size when all the rows are empty
                if (constrainedCount == 0)
                {
                    rows[lastEmpty].BlockSize += excess;
                    return;
                }
            }
            else if (emptyUnconstrained > 0)
            {
                for (var r = start; r < end; r++)
                {
                    if (rows[r].BlockSize == 0 && !IsConstrained(rows[r]))
                    {
                        rows[r].BlockSize += excess / emptyUnconstrained;
                    }
                }

                return;
            }
            else if (constrainedCount == 0)
            {
                for (var r = start; r < end; r++)
                {
                    rows[r].BlockSize += excess / empty;
                }

                return;
            }
        }

        // 5. The constrained rows, in proportion to their sizes
        if (constrainedCount > 0 && constrainedSize > 0)
        {
            for (var r = start; r < end; r++)
            {
                if (rows[r].BlockSize > 0 && IsConstrained(rows[r]))
                {
                    rows[r].BlockSize += excess * rows[r].BlockSize / constrainedSize;
                }
            }

            return;
        }

        // The rows are empty and constrained: the last one grows
        if (lastEmpty >= 0)
        {
            rows[lastEmpty].BlockSize += excess;
        }
    }

    /// <summary>
    /// Distributes the block size of the table (its content box without the border spacing) to its row groups, then to their
    /// rows (the algorithm of the browsers, see WPT css/css-tables/tentative/table-height-redistribution.html): the percentage
    /// row groups grow to their percentage first, then the excess goes to the <c>auto</c> row groups, else to the fixed ones,
    /// else to the percentage ones (the body groups first, in proportion to their sizes, or equally).
    /// </summary>
    private void DistributeTableBlockSize(SectionSizing[] sections, RowSizing[] rows, double distributable)
    {
        if (sections.Length == 0 || distributable <= 0)
            return;

        var minimum = 0d;
        var percentGuess = 0d;
        foreach (var section in sections)
        {
            minimum += section.BlockSize;
            percentGuess += section.Percent > 0 ? GetPercentSize(section) : section.BlockSize;
        }

        if (distributable <= minimum)
            return;

        double GetPercentSize(in SectionSizing section) => Math.Max(section.BlockSize, section.Percent * distributable / 100);

        var original = new double[sections.Length];
        for (var s = 0; s < sections.Length; s++)
        {
            original[s] = sections[s].BlockSize;
        }

        if (distributable <= percentGuess)
        {
            // The percentage row groups grow in proportion to the difference between their percentage size and their size
            var toDistribute = distributable - minimum;
            var totalDifference = percentGuess - minimum;
            for (var s = 0; s < sections.Length; s++)
            {
                if (sections[s].Percent > 0 && totalDifference > 0)
                {
                    sections[s].BlockSize += toDistribute * (GetPercentSize(sections[s]) - sections[s].BlockSize) / totalDifference;
                }
            }
        }
        else
        {
            for (var s = 0; s < sections.Length; s++)
            {
                if (sections[s].Percent > 0)
                {
                    sections[s].BlockSize = GetPercentSize(sections[s]);
                }
            }

            var remaining = distributable - percentGuess;
            if (!Distribute(sections, remaining, static s => s.Percent <= 0 && !s.IsFixed)
                && !Distribute(sections, remaining, static s => s.Percent <= 0 && s.IsFixed))
            {
                Distribute(sections, remaining, static s => s.Percent > 0);
            }
        }

        // The row groups distribute their new block size to their rows
        for (var s = 0; s < sections.Length; s++)
        {
            if (sections[s].BlockSize > original[s])
            {
                var section = Model.Sections[s];
                DistributeExcessBlockSize(rows, section.StartRow, section.RowCount, sections[s].BlockSize, isRowSpanDistribution: false, percentageBasis: sections[s].BlockSize);
            }
        }

        static bool Distribute(SectionSizing[] sections, double size, Func<SectionSizing, bool> predicate)
        {
            var any = false;
            var anyBody = false;
            foreach (var section in sections)
            {
                if (predicate(section))
                {
                    any = true;
                    anyBody |= section.IsBody;
                }
            }

            if (!any)
                return false;

            var total = 0d;
            var count = 0;
            foreach (var section in sections)
            {
                if (predicate(section) && (!anyBody || section.IsBody))
                {
                    total += section.BlockSize;
                    count++;
                }
            }

            for (var s = 0; s < sections.Length; s++)
            {
                if (predicate(sections[s]) && (!anyBody || sections[s].IsBody))
                {
                    sections[s].BlockSize += total > 0 ? size * sections[s].BlockSize / total : size / count;
                }
            }

            return true;
        }
    }
}
