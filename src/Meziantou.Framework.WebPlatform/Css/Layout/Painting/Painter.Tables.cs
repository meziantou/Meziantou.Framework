using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the tables (https://drafts.csswg.org/css-tables-3/#drawing-cell-backgrounds,
// https://drafts.csswg.org/css2/#collapsing-borders): the backgrounds of the column groups, the columns, the row groups and the
// rows are painted in their cells, and the collapsed borders are painted by the table after the backgrounds and the borders of
// its block-level descendants (https://drafts.csswg.org/css-tables-3/#drawing-backgrounds-and-borders).
internal sealed partial class Painter
{
    /// <summary>
    /// The tables whose collapsed borders are painted after a fragment (the key): the last of their block-level descendants
    /// painted with them. The tables of a fragment are painted from the last one: a table nested in another one is first.
    /// </summary>
    private Dictionary<Fragment, List<BoxFragment>>? _pendingCollapsedBorders;

    /// <summary>Whether a box is a row, a row group, a column or a column group: its background is painted in the cells, it has no border of its own.</summary>
    private static bool IsTablePart(ContainerBox box)
        => box.Display.Internal is DisplayInternal.TableRow or DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup or DisplayInternal.TableColumn or DisplayInternal.TableColumnGroup;

    /// <summary>
    /// Paints the backgrounds of the parts of a table grid in its cells (https://drafts.csswg.org/css-tables-3/#drawing-cell-backgrounds):
    /// for each cell, the backgrounds of its column group, its column, its row group and its row, positioned in the area of the
    /// part and painted in the cell (the whole cell, when it spans other rows or columns than the part: the border box of the
    /// cell is the background painting area). In the collapsing border model, the collapsed borders are painted in the same phase
    /// of the painting order, after the backgrounds and the borders of the block-level descendants of the table (as in Blink and
    /// WebKit, https://github.com/w3c/csswg-drafts/issues/11570: they cover the borders of the blocks of the cells and the
    /// collapsed borders of the tables nested in the cells, and they are below the floats and the inline content).
    /// </summary>
    private void PaintTableGrid(BoxFragment grid)
    {
        if (grid.Box.LayoutState is not TableState state)
            return;

        var model = state.Model;
        Dictionary<ContainerBox, TableCell>? cells = null;
        Fragment? lastCell = null;
        var lastCellIsLayer = false;
        foreach (var child in grid.ChildrenSpan)
        {
            if (child is not BoxFragment part)
                continue;

            if (part.Box.Display.Internal == DisplayInternal.TableRow)
            {
                PaintRow(part, section: null);
            }
            else if (part.Box.Display.Internal is DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup)
            {
                var paintsShadows = !IsTablePartLayer(part);
                if (paintsShadows)
                {
                    PaintTablePartShadows(part, inset: false, state.CollapsedBorders is not null);
                }

                foreach (var row in part.ChildrenSpan)
                {
                    if (row is BoxFragment rowFragment)
                    {
                        PaintRow(rowFragment, part);
                    }
                }

                if (paintsShadows)
                {
                    PaintTablePartShadows(part, inset: true, state.CollapsedBorders is not null);
                }
            }
        }

        // The clips of the cells are restored (the clips of the table are synchronized again by the next painted fragment)
        SyncClips(GetClipChain(grid, includeSelf: false));
        if (state.CollapsedBorders is not null && lastCell is not null)
        {
            // (a cell that is painted as a layer, or in the layer of its row or of its row group, is painted after the blocks of
            // the table: the borders then follow the last cell, above its background, as in the browsers)
            var last = lastCellIsLayer ? lastCell : PaintOrder.GetLastPaintedBlock(grid) ?? lastCell;
            _pendingCollapsedBorders ??= new(ReferenceEqualityComparer.Instance);
            if (!_pendingCollapsedBorders.TryGetValue(last, out var grids))
            {
                _pendingCollapsedBorders[last] = grids = [];
            }

            grids.Add(grid);
        }

        void PaintRow(BoxFragment row, BoxFragment? section)
        {
            // The shadows of a row are painted with its background: the outer ones below it (and above the backgrounds of the
            // previous rows), the inset ones above it (and below the backgrounds of its cells)
            var paintsShadows = !IsTablePartLayer(row) && (section is null || !IsTablePartLayer(section));
            if (paintsShadows)
            {
                PaintTablePartShadows(row, inset: false, state.CollapsedBorders is not null);
            }

            PaintRowCells(row, section);
            if (paintsShadows)
            {
                PaintTablePartShadows(row, inset: true, state.CollapsedBorders is not null);
            }
        }

        void PaintRowCells(BoxFragment row, BoxFragment? section)
        {
            foreach (var child in row.ChildrenSpan)
            {
                if (child is not BoxFragment cellFragment || cellFragment.Box.Display.Internal != DisplayInternal.TableCell)
                    continue;

                lastCell = cellFragment;
                lastCellIsLayer = IsTablePartLayer(cellFragment) || IsTablePartLayer(row) || (section is not null && IsTablePartLayer(section));
                if (cellFragment.Box.Style.Layout.Visibility != Visibility.Visible || (state.CollapsedBorders is null && IsHiddenEmptyCell(cellFragment)))
                    continue;

                if (cells is null)
                {
                    cells = new Dictionary<ContainerBox, TableCell>(model.Cells.Count);
                    foreach (var modelCell in model.Cells)
                    {
                        cells[modelCell.Box] = modelCell;
                    }
                }

                var column = cells.TryGetValue(cellFragment.Box, out var cell) && cell.Column < model.Columns.Count ? model.Columns[cell.Column] : default;
                var columnGroup = column.Group is { } group ? FindColumnFragment(group) : null;
                var columnFragment = column.Column is { } columnBox ? FindColumnFragment(columnBox) : null;

                // A row or a row group that is painted as a layer (it is positioned, or it establishes a stacking context) paints
                // its background itself, with the backgrounds of the rows of the row group (see PaintTablePartLayer)
                var layerSection = section is not null && IsTablePartLayer(section);
                var paintedSection = layerSection ? null : section;
                var paintedRow = layerSection || IsTablePartLayer(row) ? null : row;
                if (!HasBackground(columnGroup) && !HasBackground(columnFragment) && !HasBackground(paintedSection) && !HasBackground(paintedRow))
                    continue;

                SyncClips(GetClipChain(cellFragment, includeSelf: false));
                var cellRect = GetSnappedBorderBox(cellFragment, cellFragment.GetOffsetInRoot() - _group.Origin);
                _builder.Save();
                _builder.Add(new ClipRectItem(cellRect));
                if (BorderShapeGeometry.Get(cellFragment) is { } cellShape)
                {
                    // The backgrounds of the parts painted in a cell are clipped by the shape of its border, as its own background
                    ClipToPath(PathOperations.Translate(cellShape.BackgroundPath, cellFragment.BorderBoxRect.Translate(cellFragment.GetOffsetInRoot() - _group.Origin).Offset));
                }

                PaintPartBackground(columnGroup, cellRect);
                PaintPartBackground(columnFragment, cellRect);
                PaintPartBackground(paintedSection, cellRect);
                PaintPartBackground(paintedRow, cellRect);
                _builder.Restore();
            }
        }

        BoxFragment? FindColumnFragment(ContainerBox box)
        {
            foreach (var child in grid.ChildrenSpan)
            {
                if (child is BoxFragment fragment && ReferenceEquals(fragment.Box, box))
                    return fragment;
            }

            return null;
        }

        void PaintPartBackground(BoxFragment? part, PhysicalRect cellRect)
        {
            if (HasBackground(part))
            {
                PaintTablePartBackground(part!, cellRect);
            }
        }

        static bool HasBackground(BoxFragment? fragment) => HasTablePartBackground(fragment);
    }

    private static bool HasTablePartBackground(BoxFragment? fragment) => fragment is not null && fragment.Box.Style.Layout.Visibility == Visibility.Visible && HasBackground(fragment.Box.Style);

    /// <summary>
    /// Paints the background of a part of a table grid in a cell: the area of the part is the background positioning area, the
    /// border box of the cell the background painting area (the clip of the cell is set by the caller).
    /// </summary>
    private void PaintTablePartBackground(BoxFragment part, PhysicalRect cellRect)
    {
        var partRect = GetSnappedBorderBox(part, part.GetOffsetInRoot() - _group.Origin);
        PaintBackground(part, part.Box.Style, partRect, default, default, paintingBox: cellRect);
    }

    /// <summary>
    /// Paints the outer or the inset shadows of a row or of a row group (https://drafts.csswg.org/css-backgrounds/#box-shadow
    /// applies to all the elements: the border box of a row is the area of its cells with the spacing between them). "Outer
    /// shadows have no effect on internal table elements in the collapsing border model."
    /// </summary>
    private void PaintTablePartShadows(BoxFragment part, bool inset, bool collapsedBorders)
    {
        var box = part.Box;
        if (box.IsAnonymous || (collapsedBorders && !inset) || box.Style.Layout.Visibility != Visibility.Visible || box.Style.GetComputedValue("box-shadow") is "none" or "")
            return;

        SyncClips(GetClipChain(part, includeSelf: false));
        PaintBoxShadows(part, GetSnappedBorderBox(part, part.GetOffsetInRoot() - _group.Origin), inset);
    }

    /// <summary>
    /// Whether a row or a row group is painted as a layer of the painting order (https://drafts.csswg.org/css2/#elaborate-stacking-contexts):
    /// it is positioned or it establishes a stacking context. Its background is then painted with it, above the content painted
    /// before it (as in the browsers), instead of with the table.
    /// </summary>
    private static bool IsTablePartLayer(BoxFragment part)
        => part.Box is { IsAnonymous: false } box && (box.Style.Layout.IsPositioned || part.EstablishesStackingContext);

    /// <summary>
    /// Paints the background of a row or a row group that is painted as a layer (see <see cref="IsTablePartLayer"/>) in its cells;
    /// a row group also paints the backgrounds of its rows that are not layers.
    /// </summary>
    private void PaintTablePartLayer(BoxFragment part)
    {
        if (!IsTablePartLayer(part))
            return;

        var grid = part.Parent is BoxFragment { Box.Kind: ContainerBoxKind.TableGrid } parent ? parent
            : part.Parent is BoxFragment { Parent: BoxFragment { Box.Kind: ContainerBoxKind.TableGrid } ancestor } ? ancestor : null;
        var collapsedBorders = grid?.Box.LayoutState is TableState { CollapsedBorders: not null };
        var hidesEmptyCells = grid?.Box.LayoutState is TableState { CollapsedBorders: null };
        PaintTablePartShadows(part, inset: false, collapsedBorders);
        if (part.Box.Display.Internal == DisplayInternal.TableRow)
        {
            PaintRow(part, section: null);
        }
        else
        {
            foreach (var child in part.ChildrenSpan)
            {
                if (child is BoxFragment { Box.Display.Internal: DisplayInternal.TableRow } row)
                {
                    // (the shadows of a row that is a layer too are painted by the row)
                    var paintsShadows = !IsTablePartLayer(row);
                    if (paintsShadows)
                    {
                        PaintTablePartShadows(row, inset: false, collapsedBorders);
                    }

                    PaintRow(row, part);
                    if (paintsShadows)
                    {
                        PaintTablePartShadows(row, inset: true, collapsedBorders);
                    }
                }
            }
        }

        PaintTablePartShadows(part, inset: true, collapsedBorders);

        void PaintRow(BoxFragment row, BoxFragment? section)
        {
            // (the row of a row group that is a layer: its background is painted by the row group, unless it is a layer too)
            var paintedRow = section is null || !IsTablePartLayer(row) ? row : null;
            if (!HasTablePartBackground(section) && !HasTablePartBackground(paintedRow))
                return;

            foreach (var child in row.ChildrenSpan)
            {
                if (child is not BoxFragment { Box.Display.Internal: DisplayInternal.TableCell } cellFragment
                    || cellFragment.Box.Style.Layout.Visibility != Visibility.Visible || (hidesEmptyCells && IsHiddenEmptyCell(cellFragment)))
                {
                    continue;
                }

                SyncClips(GetClipChain(cellFragment, includeSelf: false));
                var cellRect = GetSnappedBorderBox(cellFragment, cellFragment.GetOffsetInRoot() - _group.Origin);
                _builder.Save();
                _builder.Add(new ClipRectItem(cellRect));
                if (HasTablePartBackground(section))
                {
                    PaintTablePartBackground(section!, cellRect);
                }

                if (HasTablePartBackground(paintedRow))
                {
                    PaintTablePartBackground(paintedRow!, cellRect);
                }

                _builder.Restore();
            }

            SyncClips(GetClipChain(part, includeSelf: false));
        }
    }

    /// <summary>Paints the collapsed borders of the tables whose last block-level descendant was just painted.</summary>
    private void PaintPendingCollapsedBorders(Fragment fragment)
    {
        if (_pendingCollapsedBorders is null || !_pendingCollapsedBorders.Remove(fragment, out var grids))
            return;

        for (var i = grids.Count - 1; i >= 0; i--)
        {
            SyncClips(GetClipChain(grids[i], includeSelf: false));
            PaintCollapsedBorders(grids[i]);
        }
    }

    /// <summary>
    /// Paints the collapsed borders of a table (https://drafts.csswg.org/css2/#collapsing-borders): each segment of the grid lines
    /// with its resolved border, centered on the grid line, the segments by increasing precedence so the winning border covers the
    /// corners (the segments are extended by half of the widest border crossing them). The grid lines are the edges of the border
    /// boxes of the cells: the column lines are positions in the inline axis of the table and the row lines in its block axis (the
    /// horizontal and the vertical axes of a horizontal table, the reverse in a vertical one).
    /// </summary>
    private void PaintCollapsedBorders(BoxFragment grid)
    {
        if (grid.Box.LayoutState is not TableState { CollapsedBorders: { } borders } state)
            return;

        var model = state.Model;
        var rows = borders.RowCount;
        var columns = borders.ColumnCount;
        var writingDirection = grid.WritingDirection;
        var horizontal = writingDirection.IsHorizontal;
        var columnLines = new double?[columns + 1];
        var rowLines = new double?[rows + 1];

        // The table is fragmented (https://drafts.csswg.org/css-break-3/#box-splitting): the borders of the cells of this fragment
        // of the table are painted; the block-start and block-end edges of the fragments of a cell that is broken are not grid
        // lines (the borders along the cell run up to them, and no border is painted across them)
        var isFragmented = !grid.IsFirstFragment || !grid.IsLastFragment;
        var sliceStarts = isFragmented ? new double?[rows] : null;
        var sliceEnds = isFragmented ? new double?[rows] : null;

        // The segments inside of a spanning cell are not painted
        var interiorInline = new bool[rows * (columns + 1)];
        var interiorBlock = new bool[(rows + 1) * columns];
        foreach (var cell in model.Cells)
        {
            var endColumn = Math.Min(columns, cell.EndColumn + 1);
            var endRow = Math.Min(rows, cell.EndRow + 1);
            for (var row = cell.Row; row < endRow; row++)
            {
                for (var line = cell.Column + 1; line < endColumn; line++)
                {
                    interiorInline[(row * (columns + 1)) + line] = true;
                }
            }

            for (var line = cell.Row + 1; line < endRow; line++)
            {
                for (var column = cell.Column; column < endColumn; column++)
                {
                    interiorBlock[(line * columns) + column] = true;
                }
            }
        }

        foreach (var cell in model.Cells)
        {
            if (GetCellFragment(cell.Box) is not { } fragment)
                continue;

            // The border box of a cell ends at the middle of the grid lines (its borders are half of the collapsed borders)
            var rect = fragment.BorderBoxRect.Translate(fragment.GetOffsetInRoot() - _group.Origin);
            var endColumn = Math.Min(columns, cell.EndColumn + 1);
            var endRow = Math.Min(rows, cell.EndRow + 1);
            columnLines[cell.Column] ??= GetEdge(rect, writingDirection.InlineStart);
            columnLines[endColumn] ??= GetEdge(rect, writingDirection.InlineEnd);
            if (fragment.IsFirstFragment || sliceStarts is null)
            {
                rowLines[cell.Row] ??= GetEdge(rect, writingDirection.BlockStart);
            }
            else if (cell.Row < rows)
            {
                sliceStarts[cell.Row] ??= GetEdge(rect, writingDirection.BlockStart);
            }

            if (fragment.IsLastFragment || sliceEnds is null)
            {
                rowLines[endRow] ??= GetEdge(rect, writingDirection.BlockEnd);
            }
            else if (endRow > 0)
            {
                sliceEnds[endRow - 1] ??= GetEdge(rect, writingDirection.BlockEnd);
            }
        }

        // The fragment of a cell that is in this fragment of the table
        BoxFragment? GetCellFragment(ContainerBox cell)
        {
            for (var fragment = _engine.GetFirstFragment(cell); fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                if (fragment is not BoxFragment box)
                    continue;

                if (!isFragmented)
                    return box;

                for (var ancestor = fragment.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ReferenceEquals(ancestor, grid))
                        return box;

                    if (ancestor is BoxFragment { Box.Kind: ContainerBoxKind.TableGrid })
                        break;
                }
            }

            return null;
        }

        // A grid line inside of a spanning cell that is the edge of no cell (no cell originates in the tracks that the cell spans)
        // has no position: the tracks before it are empty, so the segments of the edges of the spanning cell are painted along
        // the last track that it spans
        foreach (var cell in model.Cells)
        {
            FillInteriorLines(columnLines, cell.Column, Math.Min(columns, cell.EndColumn + 1));
            FillInteriorLines(rowLines, cell.Row, Math.Min(rows, cell.EndRow + 1));
        }

        static void FillInteriorLines(double?[] lines, int start, int end)
        {
            for (var line = start + 1; line < end; line++)
            {
                lines[line] ??= lines[line - 1];
            }
        }

        var segments = new List<(TableCollapsedBorders.Edge Edge, PhysicalRect Rect, bool Vertical, int Position)>();
        for (var row = 0; row < rows; row++)
        {
            for (var line = 0; line <= columns; line++)
            {
                var edge = borders.GetInlineSegment(row, line);
                if (!edge.IsVisible || interiorInline[(row * (columns + 1)) + line] || columnLines[line] is not { } position
                    || (rowLines[row] ?? sliceStarts?[row]) is not { } start || (rowLines[row + 1] ?? sliceEnds?[row]) is not { } end)
                {
                    continue;
                }

                // Extended by half of the widest border of the row lines at each end (not at the edge of a fragment of the row)
                var (first, last) = start <= end ? (start, end) : (end, start);
                var startWidth = rowLines[row] is null ? 0 : MaxBlockWidth(row, line);
                var endWidth = rowLines[row + 1] is null ? 0 : MaxBlockWidth(row + 1, line);
                var before = start <= end ? startWidth : endWidth;
                var after = start <= end ? endWidth : startWidth;
                segments.Add((edge, GetSegmentRect(position, edge.Width, first - (before / 2), last + (after / 2), alongBlockAxis: true), horizontal, (row * (columns + 1)) + line));
            }
        }

        for (var line = 0; line <= rows; line++)
        {
            for (var column = 0; column < columns; column++)
            {
                var edge = borders.GetBlockSegment(line, column);
                if (!edge.IsVisible || interiorBlock[(line * columns) + column] || rowLines[line] is not { } position || columnLines[column] is not { } start || columnLines[column + 1] is not { } end)
                    continue;

                var (first, last) = start <= end ? (start, end) : (end, start);
                var before = MaxInlineWidth(line, start <= end ? column : column + 1);
                var after = MaxInlineWidth(line, start <= end ? column + 1 : column);
                segments.Add((edge, GetSegmentRect(position, edge.Width, first - (before / 2), last + (after / 2), alongBlockAxis: false), !horizontal, (line * (columns + 1)) + column));
            }
        }

        // The rectangle of a segment centered on a grid line: it runs along the block axis of the table (a column line) or along
        // its inline axis (a row line), which is the vertical axis for a column line of a horizontal table and for a row line of a
        // vertical table
        PhysicalRect GetSegmentRect(double position, double width, double start, double end, bool alongBlockAxis)
        {
            var near = SnapToDevicePixels(position - (width / 2));
            var snappedStart = SnapToDevicePixels(start);
            var length = SnapToDevicePixels(end) - snappedStart;
            return alongBlockAxis == horizontal ? new PhysicalRect(near, snappedStart, width, length) : new PhysicalRect(snappedStart, near, length, width);
        }

        static double GetEdge(PhysicalRect rect, PhysicalSide side) => side switch
        {
            PhysicalSide.Top => rect.Top,
            PhysicalSide.Right => rect.Right,
            PhysicalSide.Bottom => rect.Bottom,
            _ => rect.Left,
        };

        // The borders with the lowest precedence first. Between the borders that only differ in color, the one that starts
        // further to the block-start, then to the inline-start, side of the table covers the corner (as in Blink and WebKit, WPT
        // css/CSS2/tables/border-conflict-element-001d.xht: the corner has the color of the border of the cell before it)
        segments.Sort((a, b) => TableCollapsedBorders.Compare(a.Edge, b.Edge) is not 0 and var result ? result : b.Position.CompareTo(a.Position));

        // A border on a grid line that is at a break is half in each fragment of the table: the borders are clipped by the
        // fragment in the block axis
        var gridRect = isFragmented ? grid.BorderBoxRect.Translate(grid.GetOffsetInRoot() - _group.Origin) : default;
        foreach (var (edge, segmentRect, vertical, _) in segments)
        {
            var rect = segmentRect;
            if (isFragmented)
            {
                if (horizontal)
                {
                    var top = Math.Max(rect.Top, gridRect.Top);
                    rect = new PhysicalRect(rect.Left, top, rect.Width, Math.Max(0, Math.Min(rect.Bottom, gridRect.Bottom) - top));
                }
                else
                {
                    var left = Math.Max(rect.Left, gridRect.Left);
                    rect = new PhysicalRect(left, rect.Top, Math.Max(0, Math.Min(rect.Right, gridRect.Right) - left), rect.Height);
                }
            }

            var color = GetColor(edge.Owner!, edge.Side switch
            {
                PhysicalSide.Top => "border-top-color",
                PhysicalSide.Right => "border-right-color",
                PhysicalSide.Bottom => "border-bottom-color",
                _ => "border-left-color",
            });
            if (color.IsTransparent || rect.IsEmpty)
                continue;

            BorderPainter.PaintRule(_builder, rect, vertical, edge.Style, color, edge.Owner!.Element, _devicePixelRatio);
        }

        // The widest border of the segments of a row line that meet a column line
        double MaxBlockWidth(int rowLine, int columnLine)
        {
            var width = 0d;
            if (columnLine > 0 && borders.GetBlockSegment(rowLine, columnLine - 1) is { IsVisible: true } left)
            {
                width = left.Width;
            }

            if (columnLine < columns && borders.GetBlockSegment(rowLine, columnLine) is { IsVisible: true } right)
            {
                width = Math.Max(width, right.Width);
            }

            return width;
        }

        double MaxInlineWidth(int rowLine, int columnLine)
        {
            var width = 0d;
            if (rowLine > 0 && borders.GetInlineSegment(rowLine - 1, columnLine) is { IsVisible: true } above)
            {
                width = above.Width;
            }

            if (rowLine < rows && borders.GetInlineSegment(rowLine, columnLine) is { IsVisible: true } below)
            {
                width = Math.Max(width, below.Width);
            }

            return width;
        }
    }
}
