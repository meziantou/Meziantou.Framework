using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The collapsing border model (https://drafts.csswg.org/css2/#collapsing-borders, https://drafts.csswg.org/css-tables-3/#border-collapsing):
/// the borders of the cells, the rows, the row groups, the columns, the column groups and the table that are on the same
/// edge of the grid are resolved to one border, the half of which is the used border of each side.
/// </summary>
/// <remarks>
/// The conflicts are resolved as in CSS 2.1 §17.6.2.1 (https://drafts.csswg.org/css2/#border-conflict-resolution), as in the
/// browsers: <c>hidden</c> wins, <c>none</c> loses, the wider border wins, then the style (double, solid, dashed, dotted,
/// ridge, outset, groove, inset), then the box type (cell, row, row group, column, column group, table), then the box further
/// to the inline-start and block-start side. The borders are resolved per edge segment of the grid (a side of a slot): a cell
/// uses the half of the widest segment of each of its sides, the table the half of the widest segment of each of its sides
/// (https://drafts.csswg.org/css-tables-3/#conflict-resolution-for-collapsed-borders: "set the table-root border-width to half
/// the maximum width found during the harmonization processes for that border").
/// </remarks>
internal sealed class TableCollapsedBorders
{
    /// <summary>The segments between the columns: (rows) x (columns + 1).</summary>
    private readonly Edge[] _inlineEdges;

    /// <summary>The segments between the rows: (rows + 1) x (columns).</summary>
    private readonly Edge[] _blockEdges;

    private readonly int _rows;
    private readonly int _columns;

    /// <summary>
    /// The grid line whose segments each grid line uses: the first of the lines that are only separated by collapsed tracks
    /// (https://drafts.csswg.org/css-tables-3/#visible-track), which are one line once the tracks are removed.
    /// </summary>
    private readonly int[] _rowLines;
    private readonly int[] _columnLines;

    private TableCollapsedBorders(int rows, int columns, int[] rowLines, int[] columnLines)
    {
        _rows = rows;
        _columns = columns;
        _rowLines = rowLines;
        _columnLines = columnLines;
        _inlineEdges = new Edge[rows * (columns + 1)];
        _blockEdges = new Edge[(rows + 1) * columns];
    }

    /// <summary>The used borders of the table grid box: the half of the widest border of each of its sides.</summary>
    public LogicalSides<double> TableBorder { get; private set; }

    /// <summary>The precedence of the box types (https://drafts.csswg.org/css2/#border-conflict-resolution).</summary>
    internal enum Source : byte
    {
        None,
        Table,
        ColumnGroup,
        Column,
        RowGroup,
        Row,
        Cell,
    }

    /// <summary>A resolved border of a segment of the grid: its width and style, and the box and side it comes from (for its color).</summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct Edge
    {
        public double Width;
        public BorderStyle Style;
        public Source Source;

        /// <summary>The style of the box the border comes from, or null when no box has a border on the segment.</summary>
        public Cascade.ComputedStyle? Owner;

        /// <summary>The physical side of <see cref="Owner"/> the border comes from.</summary>
        public PhysicalSide Side;

        /// <summary>Whether the border is painted (a width and a style other than none and hidden).</summary>
        public readonly bool IsVisible => Width > 0 && Style is not (BorderStyle.None or BorderStyle.Hidden) && Owner is not null;
    }

    /// <summary>The number of rows of the grid.</summary>
    public int RowCount => _rows;

    /// <summary>The number of columns of the grid.</summary>
    public int ColumnCount => _columns;

    /// <summary>Returns the resolved border of the segment of the grid line before the column <paramref name="line"/> (the last line is <see cref="ColumnCount"/>) in a row.</summary>
    internal Edge GetInlineSegment(int row, int line) => GetInlineEdge(row, line);

    /// <summary>Returns the resolved border of the segment of the grid line before the row <paramref name="line"/> (the last line is <see cref="RowCount"/>) in a column.</summary>
    internal Edge GetBlockSegment(int line, int column) => GetBlockEdge(line, column);

    /// <summary>Returns the column line whose segments a column line uses: the first of the lines that only collapsed columns separate from it.</summary>
    internal int GetMergedColumnLine(int line) => _columnLines[line];

    /// <summary>Returns the row line whose segments a row line uses: the first of the lines that only collapsed rows separate from it.</summary>
    internal int GetMergedRowLine(int line) => _rowLines[line];

    /// <summary>Returns whether a resolved border takes precedence over another one (the conflict resolution, for the corners of the painting).</summary>
    internal static int Compare(Edge a, Edge b) => Wins(a, b) ? 1 : Wins(b, a) ? -1 : 0;

    /// <summary>Resolves the collapsed borders of a table, sets the used borders of its cells and returns the borders of the table.</summary>
    public static TableCollapsedBorders Compute(ContainerBox grid, TableGridModel model, WritingDirection writingDirection)
    {
        var rows = model.Rows.Count;
        var columns = model.Columns.Count;

        // https://drafts.csswg.org/css-tables-3/#visible-track: the collapsed tracks are removed, the borders of their boxes are
        // not harmonized with the others, and the lines on both sides of them are one line (WPT
        // css/CSS2/tables/border-collapse-visibility-collapse-002.tentative.html)
        var collapsedRows = new bool[rows];
        for (var row = 0; row < rows; row++)
        {
            collapsedRows[row] = model.Rows[row].IsCollapsed;
        }

        var collapsedColumns = new bool[columns];
        for (var column = 0; column < columns; column++)
        {
            collapsedColumns[column] = model.Columns[column].IsCollapsed;
        }

        var borders = new TableCollapsedBorders(rows, columns, GetMergedLines(collapsedRows), GetMergedLines(collapsedColumns));
        var tableSides = GetSides(grid, writingDirection);
        // A table without slots has no border (as in the browsers, WPT css/css-tables/tentative/tbody-height-redistribution.html)
        if (rows == 0 || columns == 0)
            return borders;

        // The borders are applied by increasing precedence: an equal border of a box of the same type applied later loses (the
        // one further to the start wins)
        borders.ApplyBox(tableSides, Source.Table, 0, rows, 0, columns);
        foreach (var columnBox in model.ColumnBoxes)
        {
            if (columnBox.ColumnCount == 0 || columnBox.StartColumn >= columns)
                continue;

            var source = columnBox.Box.Display.Internal == DisplayInternal.TableColumnGroup ? Source.ColumnGroup : Source.Column;
            if (source == Source.ColumnGroup && !AreAllCollapsed(collapsedColumns, columnBox.StartColumn, Math.Min(columns, columnBox.StartColumn + columnBox.ColumnCount)))
            {
                borders.ApplyBox(GetSides(columnBox.Box, writingDirection), source, 0, rows, columnBox.StartColumn, Math.Min(columns, columnBox.StartColumn + columnBox.ColumnCount));
            }
        }

        foreach (var columnBox in model.ColumnBoxes)
        {
            if (columnBox.ColumnCount == 0 || columnBox.StartColumn >= columns || columnBox.Box.Display.Internal != DisplayInternal.TableColumn)
                continue;

            // Each column of a column box with a span has the borders of the box
            var sides = GetSides(columnBox.Box, writingDirection);
            for (var column = columnBox.StartColumn; column < Math.Min(columns, columnBox.StartColumn + columnBox.ColumnCount); column++)
            {
                if (!collapsedColumns[column])
                {
                    borders.ApplyBox(sides, Source.Column, 0, rows, column, column + 1);
                }
            }
        }

        foreach (var section in model.Sections)
        {
            if (section.Box is { } sectionBox && section.RowCount > 0 && !AreAllCollapsed(collapsedRows, section.StartRow, section.EndRow))
            {
                borders.ApplyBox(GetSides(sectionBox, writingDirection), Source.RowGroup, section.StartRow, section.EndRow, 0, columns);
            }
        }

        for (var row = 0; row < rows; row++)
        {
            if (!collapsedRows[row])
            {
                borders.ApplyBox(GetSides(model.Rows[row].Box, writingDirection), Source.Row, row, row + 1, 0, columns);
            }
        }

        foreach (var cell in model.Cells)
        {
            var endColumn = Math.Min(columns, cell.EndColumn + 1);
            if (!AreAllCollapsed(collapsedRows, cell.Row, Math.Min(rows, cell.EndRow + 1)) && !AreAllCollapsed(collapsedColumns, cell.Column, endColumn))
            {
                borders.ApplyBox(GetSides(cell.Box, writingDirection), Source.Cell, cell.Row, cell.EndRow + 1, cell.Column, endColumn);
            }
        }

        // The used borders of the cells: the half of the widest segment of each side
        foreach (var cell in model.Cells)
        {
            var endColumn = Math.Min(columns - 1, cell.EndColumn);
            double inlineStart = 0, inlineEnd = 0, blockStart = 0, blockEnd = 0;
            for (var row = cell.Row; row <= cell.EndRow; row++)
            {
                inlineStart = Math.Max(inlineStart, borders.GetInlineEdge(row, cell.Column).Width);
                inlineEnd = Math.Max(inlineEnd, borders.GetInlineEdge(row, endColumn + 1).Width);
            }

            for (var column = cell.Column; column <= endColumn; column++)
            {
                blockStart = Math.Max(blockStart, borders.GetBlockEdge(cell.Row, column).Width);
                blockEnd = Math.Max(blockEnd, borders.GetBlockEdge(cell.EndRow + 1, column).Width);
            }

            cell.Border = new LogicalSides<double>(blockStart / 2, inlineEnd / 2, blockEnd / 2, inlineStart / 2);
        }

        double tableInlineStart = 0, tableInlineEnd = 0, tableBlockStart = 0, tableBlockEnd = 0;
        for (var row = 0; row < rows; row++)
        {
            tableInlineStart = Math.Max(tableInlineStart, borders.GetInlineEdge(row, 0).Width);
            tableInlineEnd = Math.Max(tableInlineEnd, borders.GetInlineEdge(row, columns).Width);
        }

        for (var column = 0; column < columns; column++)
        {
            tableBlockStart = Math.Max(tableBlockStart, borders.GetBlockEdge(0, column).Width);
            tableBlockEnd = Math.Max(tableBlockEnd, borders.GetBlockEdge(rows, column).Width);
        }

        borders.TableBorder = new LogicalSides<double>(tableBlockStart / 2, tableInlineEnd / 2, tableBlockEnd / 2, tableInlineStart / 2);
        return borders;
    }

    private ref Edge GetInlineEdge(int row, int line) => ref _inlineEdges[(row * (_columns + 1)) + _columnLines[line]];

    private ref Edge GetBlockEdge(int line, int column) => ref _blockEdges[(_rowLines[line] * _columns) + column];

    /// <summary>Returns, for each grid line of the tracks, the first of the lines that only collapsed tracks separate from it.</summary>
    private static int[] GetMergedLines(bool[] collapsedTracks)
    {
        var lines = new int[collapsedTracks.Length + 1];
        for (var line = 1; line < lines.Length; line++)
        {
            lines[line] = collapsedTracks[line - 1] ? lines[line - 1] : line;
        }

        return lines;
    }

    private static bool AreAllCollapsed(bool[] collapsedTracks, int start, int end)
    {
        for (var track = start; track < end; track++)
        {
            if (!collapsedTracks[track])
                return false;
        }

        return true;
    }

    /// <summary>Applies the borders of a box that covers the given rows and columns to the segments of its sides.</summary>
    private void ApplyBox(LogicalSides<Edge> sides, Source source, int startRow, int endRow, int startColumn, int endColumn)
    {
        endRow = Math.Min(endRow, _rows);
        for (var row = startRow; row < endRow; row++)
        {
            Apply(ref GetInlineEdge(row, startColumn), sides.InlineStart, source);
            Apply(ref GetInlineEdge(row, endColumn), sides.InlineEnd, source);
        }

        for (var column = startColumn; column < endColumn; column++)
        {
            Apply(ref GetBlockEdge(startRow, column), sides.BlockStart, source);
            Apply(ref GetBlockEdge(endRow, column), sides.BlockEnd, source);
        }
    }

    private static void Apply(ref Edge edge, Edge candidate, Source source)
    {
        candidate.Source = source;
        if (Wins(candidate, edge))
        {
            edge = candidate;
        }
    }

    /// <summary>Returns whether a border wins over the current border of a segment (https://drafts.csswg.org/css2/#border-conflict-resolution).</summary>
    private static bool Wins(Edge candidate, Edge current)
    {
        // "Borders with the 'border-style' of 'hidden' take precedence over all other conflicting borders"
        if (current.Style == BorderStyle.Hidden)
            return false;

        if (candidate.Style == BorderStyle.Hidden)
            return true;

        // "Borders with a style of 'none' have the lowest priority"
        if (candidate.Style == BorderStyle.None)
            return current.Source == Source.None;

        if (current.Style == BorderStyle.None)
            return true;

        // "the narrow borders are discarded in favor of wider ones. If several have the same 'border-width' then styles are
        // preferred in this order: 'double', 'solid', 'dashed', 'dotted', 'ridge', 'outset', 'groove', and the lowest: 'inset'"
        if (candidate.Width != current.Width)
            return candidate.Width > current.Width;

        var candidateRank = GetStyleRank(candidate.Style);
        var currentRank = GetStyleRank(current.Style);
        if (candidateRank != currentRank)
            return candidateRank > currentRank;

        // "If border styles differ only in color, then a style set on a cell wins over one on a row, which wins over a row
        // group, column, column group and, lastly, table"
        return candidate.Source > current.Source;
    }

    private static int GetStyleRank(BorderStyle style) => style switch
    {
        BorderStyle.Double => 8,
        BorderStyle.Solid => 7,
        BorderStyle.Dashed => 6,
        BorderStyle.Dotted => 5,
        BorderStyle.Ridge => 4,
        BorderStyle.Outset => 3,
        BorderStyle.Groove => 2,
        BorderStyle.Inset => 1,
        _ => 0,
    };

    /// <summary>Returns the borders of a box in the logical axes of the table.</summary>
    private static LogicalSides<Edge> GetSides(ContainerBox box, WritingDirection writingDirection)
    {
        var boxModel = box.Style.Layout.Box;
        var width = writingDirection.ToLogical(boxModel.BorderWidth);
        var style = writingDirection.ToLogical(boxModel.BorderStyle);
        var owner = box.Style;
        return new LogicalSides<Edge>(
            new Edge { Width = width.BlockStart, Style = style.BlockStart, Owner = owner, Side = writingDirection.BlockStart },
            new Edge { Width = width.InlineEnd, Style = style.InlineEnd, Owner = owner, Side = writingDirection.InlineEnd },
            new Edge { Width = width.BlockEnd, Style = style.BlockEnd, Owner = owner, Side = writingDirection.BlockEnd },
            new Edge { Width = width.InlineStart, Style = style.InlineStart, Owner = owner, Side = writingDirection.InlineStart });
    }
}
