namespace Meziantou.Framework.Charts;

/// <summary>The grid of small charts of a plot: a column per facet of x and a row per facet of y, or the facets of x wrapped in rows.</summary>
internal sealed class FacetGrid
{
    private FacetGrid(int columns, int rows, IReadOnlyList<FacetCell> cells, bool wraps, bool hasRowLabels)
    {
        Columns = columns;
        Rows = rows;
        Cells = cells;
        Wraps = wraps;
        HasRowLabels = hasRowLabels;
    }

    public int Columns { get; }

    public int Rows { get; }

    public IReadOnlyList<FacetCell> Cells { get; }

    /// <summary>Gets a value that indicates whether every row of charts has headers (wrapped facets), rather than only the first row.</summary>
    public bool Wraps { get; }

    public bool HasRowLabels { get; }

    public bool IsFaceted => Cells.Count > 1 || Cells[0].XKey is not null || Cells[0].YKey is not null;

    public bool HasHeaders => Cells.Any(cell => cell.HasHeader);

    public static FacetGrid Create(IEnumerable<MarkData> marks, FacetOptions options)
    {
        var data = marks.ToList();
        var xKeys = options.XDomain ?? ScaleResolver.GetCategories([.. data.Select(mark => mark.FacetX).OfType<ChannelValues>()]);
        var yKeys = options.YDomain ?? ScaleResolver.GetCategories([.. data.Select(mark => mark.FacetY).OfType<ChannelValues>()]);
        if (xKeys.Count is 0 && yKeys.Count is 0)
            return new FacetGrid(1, 1, [new FacetCell(0, 0, null, null, IsLeft: true, IsRight: true, IsBottom: true, HasHeader: false)], wraps: false, hasRowLabels: false);

        var cells = new List<FacetCell>();
        if (yKeys.Count is 0 && options.Columns is { } wrapColumns)
        {
            var columns = Math.Min(wrapColumns, xKeys.Count);
            var rows = (xKeys.Count + columns - 1) / columns;
            for (var i = 0; i < xKeys.Count; i++)
            {
                var column = i % columns;
                cells.Add(new FacetCell(column, i / columns, xKeys[i], null, IsLeft: column is 0, IsRight: column == columns - 1 || i == xKeys.Count - 1, IsBottom: i + columns >= xKeys.Count, HasHeader: true));
            }

            return new FacetGrid(columns, rows, cells, wraps: true, hasRowLabels: false);
        }

        var columnCount = Math.Max(1, xKeys.Count);
        var rowCount = Math.Max(1, yKeys.Count);
        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                cells.Add(new FacetCell(
                    column,
                    row,
                    xKeys.Count is 0 ? null : xKeys[column],
                    yKeys.Count is 0 ? null : yKeys[row],
                    IsLeft: column is 0,
                    IsRight: column == columnCount - 1,
                    IsBottom: row == rowCount - 1,
                    HasHeader: row is 0 && xKeys.Count > 0));
            }
        }

        return new FacetGrid(columnCount, rowCount, cells, wraps: false, hasRowLabels: yKeys.Count > 0);
    }
}
