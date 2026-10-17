using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A table cell placed in the table grid (https://drafts.csswg.org/css-tables-3/#table-grid): the slots it covers, and its measures.</summary>
internal sealed class TableCell(ContainerBox box, int row, int column, int rowSpan, int columnSpan)
{
    /// <summary>The box of the cell.</summary>
    public ContainerBox Box { get; } = box;

    /// <summary>The row the cell originates from (an index in <see cref="TableGridModel.Rows"/>).</summary>
    public int Row { get; } = row;

    /// <summary>The column the cell originates from (an index in <see cref="TableGridModel.Columns"/>).</summary>
    public int Column { get; } = column;

    /// <summary>The number of rows the cell spans (at least 1, clipped to its row group).</summary>
    public int RowSpan { get; } = rowSpan;

    /// <summary>The number of columns the cell spans (at least 1).</summary>
    public int ColumnSpan { get; } = columnSpan;

    /// <summary>The last row the cell spans.</summary>
    public int EndRow => Row + RowSpan - 1;

    /// <summary>The last column the cell spans.</summary>
    public int EndColumn => Column + ColumnSpan - 1;

    /// <summary>The used borders of the cell, in the logical axes of the table (the half of the collapsed borders in the collapsing border model).</summary>
    public LogicalSides<double> Border { get; set; }

    /// <summary>The outer min-content inline size of the cell (border box, https://drafts.csswg.org/css-tables-3/#outer-min-content).</summary>
    public double MinInlineSize { get; set; }

    /// <summary>The outer max-content inline size of the cell (border box, https://drafts.csswg.org/css-tables-3/#outer-max-content).</summary>
    public double MaxInlineSize { get; set; }

    /// <summary>The percentage contribution of the cell (https://drafts.csswg.org/css-tables-3/#percentage-contribution), in percents, or 0.</summary>
    public double Percent { get; set; }

    /// <summary>Whether the cell has a <c>width</c> that is a length (https://drafts.csswg.org/css-tables-3/#constrainedness).</summary>
    public bool IsConstrained { get; set; }
}

/// <summary>A row of the table grid: its table-row box.</summary>
internal sealed class TableRow(ContainerBox box, int section)
{
    public ContainerBox Box { get; } = box;

    /// <summary>The row group of the row (an index in <see cref="TableGridModel.Sections"/>).</summary>
    public int Section { get; } = section;

    /// <summary>The first cell of the row in <see cref="TableGridModel.Cells"/> (the cells originating in the row are consecutive).</summary>
    public int FirstCell { get; set; }

    /// <summary>The number of cells originating in the row.</summary>
    public int CellCount { get; set; }

    /// <summary>Whether the row is collapsed (https://drafts.csswg.org/css-tables-3/#visible-track): <c>visibility: collapse</c> on the row or its row group.</summary>
    public bool IsCollapsed { get; set; }
}

/// <summary>A row group of the table, in the layout order (the header group first, the footer group last), or a sequence of rows that are direct children of the table.</summary>
internal sealed class TableSection(ContainerBox? box, int startRow)
{
    /// <summary>The table-row-group box, or null for the rows that are direct children of the table grid box.</summary>
    public ContainerBox? Box { get; } = box;

    public int StartRow { get; } = startRow;

    public int RowCount { get; set; }

    public int EndRow => StartRow + RowCount;
}

/// <summary>A column of the table grid: its table-column box and its table-column-group box, if any.</summary>
internal readonly record struct TableColumn(ContainerBox? Column, ContainerBox? Group)
{
    /// <summary>Whether the column is collapsed (https://drafts.csswg.org/css-tables-3/#visible-track): <c>visibility: collapse</c> on the column or its group.</summary>
    public bool IsCollapsed => Column?.Style.Layout.Visibility == Visibility.Collapse || Group?.Style.Layout.Visibility == Visibility.Collapse;
}

/// <summary>A table-column or table-column-group box and the columns it spans (for its fragment).</summary>
internal readonly record struct TableColumnBox(ContainerBox Box, int StartColumn, int ColumnCount);

/// <summary>
/// The structure of a table (https://drafts.csswg.org/css-tables-3/#dimensioning-the-row-column-grid): the rows, the columns
/// and the slots of the cells, computed from the box tree with the HTML table processing model
/// (https://html.spec.whatwg.org/multipage/tables.html#forming-a-table): the <c>span</c> of the HTML <c>col</c> and
/// <c>colgroup</c> elements and the <c>colspan</c>/<c>rowspan</c> of the HTML <c>td</c>/<c>th</c> elements (when the box is a
/// table-column, table-column-group or table-cell box of such an element), the cells growing downward (<c>rowspan=0</c>), the
/// header group first and the footer group last. The merging of the columns
/// (https://drafts.csswg.org/css-tables-3/#dimensioning-the-row-column-grid--step2) is done by the column measures (the
/// columns where no cell originates take no space, see <see cref="TableColumnMeasure.IsMergeable"/>).
/// </summary>
internal sealed class TableGridModel
{
    private TableGridModel()
    {
    }

    public List<TableSection> Sections { get; } = [];

    public List<TableRow> Rows { get; } = [];

    public List<TableColumn> Columns { get; } = [];

    /// <summary>The cells, in the order of the rows they originate in (the order of the box tree in a row).</summary>
    public List<TableCell> Cells { get; } = [];

    /// <summary>The table-column and table-column-group boxes, with the columns they span (empty ones included).</summary>
    public List<TableColumnBox> ColumnBoxes { get; } = [];

    /// <summary>Whether the table has at least one slot (https://drafts.csswg.org/css-tables-3/#table-layout-algorithm).</summary>
    public bool HasSlots => Rows.Count > 0 && Columns.Count > 0;

    /// <summary>Builds the model of a table grid box.</summary>
    /// <param name="grid">The table grid box.</param>
    public static TableGridModel Build(ContainerBox grid)
    {
        var model = new TableGridModel();

        // The columns of the column groups and of the columns (https://html.spec.whatwg.org/multipage/tables.html#forming-a-table, steps 8-9)
        var definedColumns = 0;
        foreach (var child in grid.Children)
        {
            if (child is not ContainerBox container)
                continue;

            if (container.Display.Internal == DisplayInternal.TableColumnGroup)
            {
                var start = definedColumns;
                var hasColumns = false;
                foreach (var columnChild in container.Children)
                {
                    if (columnChild is ContainerBox { Display.Internal: DisplayInternal.TableColumn } column)
                    {
                        hasColumns = true;
                        var span = GetSpan(column);
                        model.ColumnBoxes.Add(new TableColumnBox(column, definedColumns, span));
                        for (var i = 0; i < span; i++)
                        {
                            model.Columns.Add(new TableColumn(column, container));
                        }

                        definedColumns += span;
                    }
                }

                if (!hasColumns)
                {
                    var span = GetSpan(container);
                    for (var i = 0; i < span; i++)
                    {
                        model.Columns.Add(new TableColumn(null, container));
                    }

                    definedColumns += span;
                }

                model.ColumnBoxes.Add(new TableColumnBox(container, start, definedColumns - start));
            }
            else if (container.Display.Internal == DisplayInternal.TableColumn)
            {
                var span = GetSpan(container);
                model.ColumnBoxes.Add(new TableColumnBox(container, definedColumns, span));
                for (var i = 0; i < span; i++)
                {
                    model.Columns.Add(new TableColumn(container, null));
                }

                definedColumns += span;
            }
        }

        // The row groups in the layout order: the first header group, the other groups and rows, the first footer group
        // (https://drafts.csswg.org/css-tables-3/#table-header-group)
        ContainerBox? header = null;
        ContainerBox? footer = null;
        foreach (var child in grid.Children)
        {
            if (child is ContainerBox { Display.Internal: DisplayInternal.TableHeaderGroup } headerGroup && header is null)
            {
                header = headerGroup;
            }
            else if (child is ContainerBox { Display.Internal: DisplayInternal.TableFooterGroup } footerGroup && footer is null)
            {
                footer = footerGroup;
            }
        }

        var placement = new CellPlacement(model);
        if (header is not null)
        {
            placement.AddSection(header);
        }

        TableSection? looseRows = null;
        foreach (var child in grid.Children)
        {
            if (child is not ContainerBox container)
                continue;

            if (container.Display.Internal == DisplayInternal.TableRow)
            {
                looseRows ??= placement.StartSection(null);
                placement.AddRow(looseRows, container);
                continue;
            }

            if (!container.Display.IsTableRowGroup || ReferenceEquals(container, header) || ReferenceEquals(container, footer))
                continue;

            if (looseRows is not null)
            {
                placement.EndSection(looseRows);
                looseRows = null;
            }

            placement.AddSection(container);
        }

        if (looseRows is not null)
        {
            placement.EndSection(looseRows);
        }

        if (footer is not null)
        {
            placement.AddSection(footer);
        }

        // The columns created by the cells (https://html.spec.whatwg.org/multipage/tables.html#algorithm-for-growing-downward-growing-cells)
        while (model.Columns.Count < placement.ColumnCount)
        {
            model.Columns.Add(default);
        }

        foreach (var section in model.Sections)
        {
            var collapsed = section.Box?.Style.Layout.Visibility == Visibility.Collapse;
            for (var i = section.StartRow; i < section.EndRow; i++)
            {
                var row = model.Rows[i];
                row.IsCollapsed = collapsed || row.Box.Style.Layout.Visibility == Visibility.Collapse;
            }
        }

        return model;
    }

    /// <summary>Returns the number of columns of a table-column or table-column-group box: the <c>span</c> of an HTML <c>col</c>/<c>colgroup</c> element, else 1.</summary>
    private static int GetSpan(ContainerBox box)
    {
        // https://html.spec.whatwg.org/multipage/tables.html#attr-col-span: clamped to the range [1, 1000]
        if (box.PseudoElement is null && box.Element is HtmlTableColElement element)
            return (int)Math.Clamp(element.Span, 1, 1000);

        return 1;
    }

    /// <summary>The placement of the cells in the slots of the grid (https://html.spec.whatwg.org/multipage/tables.html#forming-a-table).</summary>
    private sealed class CellPlacement(TableGridModel model)
    {
        /// <summary>For each column, the number of rows below the current one that a cell placed above covers.</summary>
        private readonly List<int> _coveredRows = [];

        public int ColumnCount { get; private set; } = model.Columns.Count;

        public TableSection StartSection(ContainerBox? box)
        {
            var section = new TableSection(box, model.Rows.Count);
            model.Sections.Add(section);
            return section;
        }

        public void AddSection(ContainerBox box)
        {
            var section = StartSection(box);
            foreach (var child in box.Children)
            {
                if (child is ContainerBox { Display.Internal: DisplayInternal.TableRow })
                {
                    section.RowCount++;
                }
            }

            foreach (var child in box.Children)
            {
                if (child is ContainerBox { Display.Internal: DisplayInternal.TableRow } row)
                {
                    AddRow(section, row);
                }
            }

            EndSection(section);
        }

        /// <summary>Adds a row to a section (the rows of a section of rows without group are counted as they are added).</summary>
        public void AddRow(TableSection section, ContainerBox box)
        {
            var rowIndex = model.Rows.Count;
            var row = new TableRow(box, model.Sections.Count - 1) { FirstCell = model.Cells.Count };
            model.Rows.Add(row);
            if (section.Box is null)
            {
                section.RowCount++;
            }

            // The rows of the section after this one (the cells growing downward cover them; the rows of a section without group
            // are not known yet: its cells do not grow beyond the rows of the section, as in an HTML table)
            var remainingRows = section.Box is null ? int.MaxValue : section.EndRow - rowIndex;
            var column = 0;
            foreach (var child in box.Children)
            {
                if (child is not ContainerBox { Display.Internal: DisplayInternal.TableCell } cellBox)
                    continue;

                while (column < _coveredRows.Count && _coveredRows[column] > 0)
                {
                    column++;
                }

                var (rowSpan, columnSpan) = GetSpans(cellBox);
                rowSpan = rowSpan == 0 ? remainingRows : Math.Min(rowSpan, remainingRows);
                var cell = new TableCell(cellBox, rowIndex, column, rowSpan, columnSpan);
                model.Cells.Add(cell);
                for (var i = column; i < column + columnSpan; i++)
                {
                    while (_coveredRows.Count <= i)
                    {
                        _coveredRows.Add(0);
                    }

                    _coveredRows[i] = Math.Max(_coveredRows[i], rowSpan);
                }

                column += columnSpan;
                ColumnCount = Math.Max(ColumnCount, column);
            }

            row.CellCount = model.Cells.Count - row.FirstCell;

            // The next row: the covered slots move down
            for (var i = 0; i < _coveredRows.Count; i++)
            {
                if (_coveredRows[i] > 0)
                {
                    _coveredRows[i]--;
                }
            }

            ColumnCount = Math.Max(ColumnCount, _coveredRows.Count);
        }

        /// <summary>
        /// Ends a row group (https://html.spec.whatwg.org/multipage/tables.html#algorithm-for-ending-a-row-group): the cells that
        /// span more rows than the group has are clipped to the group.
        /// </summary>
        public void EndSection(TableSection section)
        {
            _coveredRows.Clear();
            if (section.Box is not null)
                return;

            // The cells of a section of rows without group that grow beyond its last row
            for (var i = section.StartRow; i < section.EndRow; i++)
            {
                var row = model.Rows[i];
                for (var c = row.FirstCell; c < row.FirstCell + row.CellCount; c++)
                {
                    var cell = model.Cells[c];
                    if (cell.EndRow >= section.EndRow)
                    {
                        model.Cells[c] = new TableCell(cell.Box, cell.Row, cell.Column, section.EndRow - cell.Row, cell.ColumnSpan);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the <c>rowspan</c> (0 for a cell that grows downward) and the <c>colspan</c> of a cell: the attributes of an
        /// HTML <c>td</c>/<c>th</c> element (https://html.spec.whatwg.org/multipage/tables.html#forming-a-table, step 13) or of
        /// a MathML <c>mtd</c> element, else 1.
        /// </summary>
        private static (int RowSpan, int ColumnSpan) GetSpans(ContainerBox cell)
        {
            if (cell.PseudoElement is not null)
                return (1, 1);

            if (cell.Element is MathMLElement { LocalName: "mtd" } mathCell)
            {
                // "The columnspan (respectively rowspan) attribute has the same syntax and semantics as the colspan (respectively
                // rowspan) attribute on the <td> element from [HTML]." (https://w3c.github.io/mathml-core/#entry-in-table-or-matrix-mtd)
                var mathRowSpan = Html.Reflection.GetUnsignedLong(mathCell, "rowspan", positive: false, defaultValue: 1, clampedMin: 0, clampedMax: 65534);
                var mathColumnSpan = Html.Reflection.GetUnsignedLong(mathCell, "columnspan", positive: false, defaultValue: 1, clampedMin: 1, clampedMax: 1000);
                return ((int)mathRowSpan, (int)mathColumnSpan);
            }

            if (cell.Element is not HtmlTableCellElement element)
                return (1, 1);

            // "If parsing that value failed, or returned zero, or if the attribute is absent, then let colspan be 1, instead. If
            // colspan is greater than 1000, let it be 1000 instead." "If parsing that value failed or if the attribute is absent,
            // then let rowspan be 1, instead. If rowspan is greater than 65534, let it be 65534 instead."
            return ((int)element.RowSpan, (int)Math.Max(1u, element.ColSpan));
        }
    }
}
