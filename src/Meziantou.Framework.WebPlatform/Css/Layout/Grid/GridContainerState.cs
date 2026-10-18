namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The state of the last layout of a grid container (in <see cref="LayoutBox.LayoutState"/>): the lines of its grid, for the
/// absolutely positioned boxes whose containing block is the container (https://drafts.csswg.org/css-grid-2/#abspos-items),
/// which are laid out after its contents (<see cref="PositionedLayout.LayoutOutOfFlowDescendants"/>), and the input given by
/// its parent grid when it is a subgrid (<see cref="Subgrid"/>).
/// </summary>
internal sealed class GridContainerState(
    int pass,
    WritingDirection writingDirection,
    LogicalOffset contentOffset,
    GridExplicitAxis? explicitColumns,
    GridExplicitAxis? explicitRows,
    int columnOffset,
    int rowOffset,
    (double Start, double End)[] columns,
    (double Start, double End)[] rows)
{
    /// <summary>The layout pass of the layout that recorded the lines (the lines of a previous pass are stale).</summary>
    public int Pass { get; } = pass;

    /// <summary>The input of a subgrid, given by its parent grid before it is laid out or measured, or null.</summary>
    public SubgridInput? Subgrid { get; set; }

    /// <summary>Creates the state of a box that only has a subgrid input (it has not been laid out yet).</summary>
    public static GridContainerState ForSubgrid(SubgridInput input) => new(0, default, default, null, null, 0, 0, [], []) { Subgrid = input };

    /// <summary>
    /// Returns the containing block of an absolutely positioned box whose containing block is the grid container
    /// (https://drafts.csswg.org/css-grid-2/#abspos-items), in the logical coordinates of the border box of the container:
    /// "the containing block corresponds to the grid area determined by its grid-placement properties [...] an auto value for a
    /// grid-placement property contributes a special line to the placement whose position is that of the corresponding padding
    /// edge of the grid container [...] If a grid-placement property refers to a non-existent line either by explicitly
    /// specifying such a line or by spanning outside of the existing implicit grid, it is instead treated as specifying auto".
    /// </summary>
    /// <param name="box">The absolutely positioned box.</param>
    /// <param name="paddingBox">The padding box of the container, in the logical coordinates of its border box.</param>
    public LogicalRect GetAbsoluteArea(ContainerBox box, LogicalRect paddingBox)
    {
        if (explicitColumns is null || explicitRows is null)
            return paddingBox;

        var grid = box.Style.Layout.Grid;
        var (inlineStart, inlineEnd) = ResolveAxis(grid.ColumnStart, grid.ColumnEnd, explicitColumns, columnOffset, columns, contentOffset.Inline, paddingBox.InlineStart, paddingBox.InlineEnd);
        var (blockStart, blockEnd) = ResolveAxis(grid.RowStart, grid.RowEnd, explicitRows, rowOffset, rows, contentOffset.Block, paddingBox.BlockStart, paddingBox.BlockEnd);
        return new LogicalRect(new LogicalOffset(inlineStart, blockStart), new LogicalSize(Math.Max(0, inlineEnd - inlineStart), Math.Max(0, blockEnd - blockStart)));
    }

    /// <summary>
    /// The difference between the row gutters of a subgrid and the ones of its parent grid on each side of a gutter when its rows
    /// are subgridded, else 0: the rows of the subgrid are the tracks of its parent, shrunk by this amount on the side of their
    /// gutters (the items get it as extra margins, https://drafts.csswg.org/css-grid-2/#subgrid-margins).
    /// </summary>
    public double SubgridRowGutterShift { get; init; }

    /// <summary>
    /// Returns the gutters between the rows (https://drafts.csswg.org/css-grid-2/#gutters) that are not empty: their block-start
    /// and block-end edges, from the block-start edge of the border box of the container.
    /// </summary>
    public IEnumerable<(double Start, double End)> GetRowGaps()
    {
        for (var i = 0; i + 1 < rows.Length; i++)
        {
            var (end, nextStart) = (rows[i].End - SubgridRowGutterShift, rows[i + 1].Start + SubgridRowGutterShift);
            if (nextStart > end)
            {
                yield return (contentOffset.Block + end, contentOffset.Block + nextStart);
            }
        }
    }

    /// <summary>The block-start and block-end edges of the rows, from the block-start edge of the border box of the container.</summary>
    public (double Start, double End)[] GetRows()
    {
        var result = new (double Start, double End)[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var start = rows[i].Start + (i > 0 ? SubgridRowGutterShift : 0);
            var end = rows[i].End - (i + 1 < rows.Length ? SubgridRowGutterShift : 0);
            result[i] = (contentOffset.Block + start, contentOffset.Block + Math.Max(start, end));
        }

        return result;
    }

    /// <summary>
    /// The rows of each item (the index of its first row and of the row after its last one) and whether it is stretched in the
    /// block axis, for the fragmentation of the container, and its first column (with the rows, the position of the item in the
    /// reading flow of <c>reading-flow: grid-rows</c> and <c>grid-columns</c>); null for a grid lanes container.
    /// </summary>
    public Dictionary<ContainerBox, GridItemRows>? ItemRows { get; set; }

    /// <summary>The writing mode of the grid container (the axes of the logical coordinates).</summary>
    public WritingDirection WritingDirection { get; } = writingDirection;

    private static (double Start, double End) ResolveAxis(GridLine start, GridLine end, GridExplicitAxis axis, int offset, (double Start, double End)[] tracks, double contentOffset, double paddingStart, double paddingEnd)
    {
        var (startLine, endLine) = GridPlacement.ResolveAbsolute(start, end, axis);

        // The lines of the implicit grid are numbered from 0 to the number of tracks: the other ones do not exist
        var count = tracks.Length;
        double startPosition;
        if (startLine is { } s && s + offset >= 0 && s + offset <= count)
        {
            // (a grid without track has one line, at the start edge of its content box)
            var index = s + offset;
            startPosition = contentOffset + (count == 0 ? 0 : index < count ? tracks[index].Start : tracks[count - 1].End);
        }
        else
        {
            startPosition = paddingStart;
        }

        double endPosition;
        if (endLine is { } e && e + offset >= 0 && e + offset <= count)
        {
            var index = e + offset;
            endPosition = contentOffset + (count == 0 ? 0 : index > 0 ? tracks[index - 1].End : tracks[0].Start);
        }
        else
        {
            endPosition = paddingEnd;
        }

        return (startPosition, endPosition);
    }
}

/// <summary>The rows of a grid item, whether it is stretched in the block axis, and its first column (see <see cref="GridContainerState.ItemRows"/>).</summary>
/// <param name="Start">The index of the first row of the item.</param>
/// <param name="End">The index of the row after its last row.</param>
/// <param name="Stretches">Whether the item is stretched in the block axis.</param>
/// <param name="ColumnStart">The index of the first column of the item.</param>
/// <param name="HasFixedRows">Whether the track sizing functions of the rows of the item are fixed (a length, a resolved percentage): the rows do not grow with the item when it is fragmented.</param>
internal readonly record struct GridItemRows(int Start, int End, bool Stretches, int ColumnStart, bool HasFixedRows = false);
