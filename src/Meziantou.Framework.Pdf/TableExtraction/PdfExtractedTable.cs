namespace Meziantou.Framework.Pdf;

/// <summary>A table rebuilt from the rules and the text of a page (<see cref="PdfTableExtractor"/>).</summary>
public sealed class PdfExtractedTable
{
    private readonly string[][] _cells;

    internal PdfExtractedTable(IReadOnlyList<double> columnBoundaries, IReadOnlyList<double> rowBoundaries, string[][] cells)
    {
        ColumnBoundaries = columnBoundaries;
        RowBoundaries = rowBoundaries;
        _cells = cells;
    }

    /// <summary>Gets the number of rows.</summary>
    public int RowCount => _cells.Length;

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount => ColumnBoundaries.Count - 1;

    /// <summary>Gets the x coordinates of the vertical rules of the grid, from left to right, in default user space.</summary>
    public IReadOnlyList<double> ColumnBoundaries { get; }

    /// <summary>Gets the y coordinates of the horizontal rules of the grid, from top to bottom, in default user space.</summary>
    public IReadOnlyList<double> RowBoundaries { get; }

    /// <summary>Gets the rows, from top to bottom, each with the text of its cells from left to right.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows => _cells;

    /// <summary>Gets the text of a cell: its lines separated by line feeds, the words of a line by spaces.</summary>
    /// <param name="row">The index of the row, from the top.</param>
    /// <param name="column">The index of the column, from the left.</param>
    /// <exception cref="IndexOutOfRangeException">The row or the column is out of the table.</exception>
    public string this[int row, int column] => _cells[row][column];
}
