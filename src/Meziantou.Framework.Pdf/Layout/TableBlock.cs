using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A table of a flow document: its columns, and its cells placed in a grid (the cells that span rows take the columns of the rows below them).</summary>
internal sealed record TableBlock(double[] Weights, int RowCount, int HeaderRowCount, List<TableCell> Cells, double BorderWidth, PdfColor BorderColor, double CellPadding, PdfColor? HeaderBackground) : FlowBlock
{
    // The size of a table: the cells of a hostile or mistaken table are bounded
    private const int MaxColumns = 256;

    public static TableBlock Create(PdfFlowTableBuilder builder)
    {
        if (builder.Rows.Count == 0)
            throw new ArgumentException("A table needs a row", nameof(builder));

        var columnCount = builder.ColumnWeights.Count > 0 ? builder.ColumnWeights.Count : Math.Max(1, builder.Rows[0].Cells.Sum(cell => cell.ColumnSpan));
        if (columnCount > MaxColumns)
            throw new ArgumentException($"A table has at most {MaxColumns} columns", nameof(builder));

        var weights = builder.ColumnWeights.Count > 0 ? [.. builder.ColumnWeights] : Enumerable.Repeat(1d, columnCount).ToArray();
        var occupied = new HashSet<(int Row, int Column)>();
        var cells = new List<TableCell>();
        for (var row = 0; row < builder.Rows.Count; row++)
        {
            var column = 0;
            foreach (var cell in builder.Rows[row].Cells)
            {
                while (occupied.Contains((row, column)))
                {
                    column++;
                }

                if (column + cell.ColumnSpan > columnCount)
                    throw new ArgumentException($"The cells of row {row + 1} do not fit the {columnCount} columns of the table", nameof(builder));

                var rowSpan = Math.Min(cell.RowSpan, builder.Rows.Count - row);
                for (var r = row; r < row + rowSpan; r++)
                {
                    for (var c = column; c < column + cell.ColumnSpan; c++)
                    {
                        occupied.Add((r, c));
                    }
                }

                cells.Add(new TableCell(row, column, cell.ColumnSpan, rowSpan, builder.Rows[row].IsHeader, cell.Blocks));
                column += cell.ColumnSpan;
            }
        }

        var headerRows = builder.Rows.TakeWhile(row => row.IsHeader).Count();
        return new TableBlock(weights, builder.Rows.Count, headerRows, cells, builder.BorderWidth, builder.BorderColor, builder.CellPadding, builder.HeaderBackground);
    }
}
