namespace Meziantou.Framework.Pdf;

/// <summary>Lays out the rows of a <see cref="PdfTable"/> in a rectangle, then draws them.</summary>
internal static class PdfTableRenderer
{
    private const double Tolerance = 1e-9;

    public static PdfTableDrawResult Draw(PdfContentBuilder builder, PdfTable table, PdfRectangle rectangle, PdfTableDrawResult? previous)
    {
        if (table.Columns.Count == 0)
            throw new InvalidOperationException("The table has no column");

        if (previous is { IsComplete: true })
            throw new ArgumentException("The table is already complete", nameof(previous));

        // Lay out first: a character a font does not have leaves the content unchanged
        var widths = previous is not null && previous.AvailableWidth == rectangle.Width && previous.ColumnWidths.Length == table.Columns.Count ? previous.ColumnWidths : table.GetColumnWidths(rectangle.Width);
        var offsets = new double[widths.Length + 1];
        for (var i = 0; i < widths.Length; i++)
        {
            offsets[i + 1] = offsets[i] + widths[i];
        }

        var headerCount = Math.Min(table.HeaderRowCount, table.Rows.Count);
        var placements = new List<Placement>();
        var y = rectangle.Top;
        for (var i = 0; i < headerCount; i++)
        {
            var header = LayoutRow(table, table.Rows[i], rectangle.Left, offsets, pendingTexts: null);
            if (header.Height > y - rectangle.Bottom + Tolerance)
                throw new InvalidOperationException("The rectangle is too small for the header rows of the table");

            placements.Add(Placement.Whole(header, y));
            y -= header.Height;
        }

        var nextRow = previous?.NextRow ?? headerCount;
        var pendingTexts = previous?.PendingTexts;
        var bodyRows = 0;
        while (nextRow < table.Rows.Count)
        {
            var row = LayoutRow(table, table.Rows[nextRow], rectangle.Left, offsets, pendingTexts);
            var available = y - rectangle.Bottom;
            if (row.Height <= available + Tolerance)
            {
                placements.Add(Placement.Whole(row, y));
                y -= row.Height;
                bodyRows++;
                nextRow++;
                pendingTexts = null;
                continue;
            }

            // The row does not fit: it starts the next rectangle, unless it is the first row of this one
            if (bodyRows > 0)
                break;

            var split = Split(table, row, y, available);
            if (split is not null)
            {
                placements.Add(split.Value.Placement);
                y = rectangle.Bottom;
                bodyRows++;
                pendingTexts = split.Value.PendingTexts;
            }

            break;
        }

        var isComplete = nextRow >= table.Rows.Count;
        if (!isComplete && bodyRows == 0)
        {
            // No room for a line of the next row: start the table in the next rectangle, unless it has no room either
            if (previous is { DrewNothing: true })
                throw new InvalidOperationException("The rectangle is too small for the header rows of the table and a line of the next row");

            return new PdfTableDrawResult(isComplete: false, rectangle.Top, rowCount: 0, previous?.NextRow ?? headerCount, previous?.PendingTexts, drewNothing: true, rectangle.Width, widths);
        }

        Render(builder, table, placements);
        var rowCount = placements.Count - (previous is null || previous.DrewNothing ? 0 : headerCount);
        return new PdfTableDrawResult(isComplete, y, rowCount, nextRow, pendingTexts, drewNothing: false, rectangle.Width, widths);
    }

    private static RowLayout LayoutRow(PdfTable table, PdfTableRow row, double left, double[] offsets, string[]? pendingTexts)
    {
        var columnCount = offsets.Length - 1;
        var cells = new List<CellLayout>(columnCount);
        var column = 0;
        var contentHeight = 0d;
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var cell = row.Cells[i] ?? throw new ArgumentException("A row of the table has a null cell", nameof(table));
            if (column + cell.ColumnSpan > columnCount)
                throw new ArgumentException("The cells of a row of the table span more columns than the table has", nameof(table));

            var font = cell.Font ?? row.Font ?? table.Font;
            var fontSize = cell.FontSize ?? row.FontSize ?? table.FontSize;
            if (!double.IsFinite(fontSize) || fontSize <= 0)
                throw new ArgumentException("The font size of a cell of the table must be a finite number, more than 0", nameof(table));

            var x = left + offsets[column];
            var width = offsets[column + cell.ColumnSpan] - offsets[column];
            var text = pendingTexts is not null && i < pendingTexts.Length ? pendingTexts[i] : cell.Text;
            var lines = PdfTextWrapper.Wrap(text, font, fontSize, Math.Max(width - (2 * table.CellPadding), 0), table.Hyphenator);
            var layout = new CellLayout(
                text,
                x,
                width,
                font,
                fontSize,
                cell.Alignment ?? table.Columns[column].Alignment,
                cell.TextColor ?? row.TextColor ?? table.TextColor,
                cell.Background ?? row.Background,
                lines);
            cells.Add(layout);
            contentHeight = Math.Max(contentHeight, GetContentHeight(layout, lines.Count, table.LineHeight));
            column += cell.ColumnSpan;
        }

        // The columns without cells: empty cells, as high as a line of the font of the row
        var rowFont = row.Font ?? table.Font;
        var rowFontSize = row.FontSize ?? table.FontSize;
        for (; column < columnCount; column++)
        {
            var layout = new CellLayout("", left + offsets[column], offsets[column + 1] - offsets[column], rowFont, rowFontSize, PdfTextAlignment.Left, TextColor: null, row.Background, Lines: []);
            cells.Add(layout);
            contentHeight = Math.Max(contentHeight, GetContentHeight(layout, 1, table.LineHeight));
        }

        return new RowLayout([.. cells], contentHeight + (2 * table.CellPadding));
    }

    // The height of the first lines of a cell: from the ascent of the first line to the descent of the last one
    private static double GetContentHeight(CellLayout cell, int lineCount, double lineHeight)
    {
        if (lineCount == 0)
            return 0;

        var ascent = cell.Font.Ascent * cell.FontSize / 1000;
        var descent = cell.Font.Descent * cell.FontSize / 1000;
        return ascent - descent + ((lineCount - 1) * lineHeight * cell.FontSize);
    }

    // Splits a row between two rectangles: the lines of each cell that fit in the height left
    private static (Placement Placement, string[] PendingTexts)? Split(PdfTable table, RowLayout row, double top, double available)
    {
        var contentHeight = available - (2 * table.CellPadding);
        var counts = new int[row.Cells.Length];
        var pendingTexts = new string[row.Cells.Length];
        var progress = false;
        for (var i = 0; i < row.Cells.Length; i++)
        {
            var cell = row.Cells[i];
            var count = contentHeight < 0 ? 0 : PdfContentBuilder.GetFittingLineCount(cell.Lines.Count, cell.Font, cell.FontSize, table.LineHeight, contentHeight);
            counts[i] = count;
            pendingTexts[i] = count < cell.Lines.Count ? cell.Text[cell.Lines[count].Start..] : "";
            for (var line = 0; line < count; line++)
            {
                progress |= cell.Lines[line].Text.Length > 0;
            }
        }

        if (!progress)
            return null;

        return (new Placement(row, top, available, counts), pendingTexts);
    }

    private static void Render(PdfContentBuilder builder, PdfTable table, List<Placement> placements)
    {
        // The backgrounds, under the texts and the borders
        var hasBackground = placements.Exists(placement => Array.Exists(placement.Row.Cells, cell => cell.Background is not null));
        if (hasBackground)
        {
            builder.SaveGraphicsState();
            PdfColor? fill = null;
            foreach (var placement in placements)
            {
                foreach (var cell in placement.Row.Cells)
                {
                    if (cell.Background is not { } background)
                        continue;

                    if (fill != background)
                    {
                        builder.SetFillColor(background);
                        fill = background;
                    }

                    builder.Rectangle(cell.X, placement.Top - placement.Height, cell.Width, placement.Height).Fill();
                }
            }

            builder.RestoreGraphicsState();
        }

        foreach (var placement in placements)
        {
            for (var i = 0; i < placement.Row.Cells.Length; i++)
            {
                var cell = placement.Row.Cells[i];
                var count = placement.LineCounts?[i] ?? cell.Lines.Count;
                if (count == 0)
                    continue;

                if (cell.TextColor is { } color)
                {
                    builder.SaveGraphicsState().SetFillColor(color);
                }

                builder.DrawLines(cell.Lines, 0, count, cell.Font, cell.FontSize, cell.X + table.CellPadding, Math.Max(cell.Width - (2 * table.CellPadding), 0), placement.Top - table.CellPadding, cell.Alignment, table.LineHeight);
                if (cell.TextColor is not null)
                {
                    builder.RestoreGraphicsState();
                }
            }
        }

        if (table.BorderWidth > 0)
        {
            builder.SaveGraphicsState().SetStrokeColor(table.BorderColor).SetLineWidth(table.BorderWidth);
            foreach (var placement in placements)
            {
                foreach (var cell in placement.Row.Cells)
                {
                    builder.Rectangle(cell.X, placement.Top - placement.Height, cell.Width, placement.Height);
                }
            }

            builder.Stroke().RestoreGraphicsState();
        }
    }

    private sealed record CellLayout(string Text, double X, double Width, PdfFont Font, double FontSize, PdfTextAlignment Alignment, PdfColor? TextColor, PdfColor? Background, List<PdfTextWrapper.Line> Lines);

    private sealed record RowLayout(CellLayout[] Cells, double Height);

    // A row drawn at a position: all the lines of its cells, or the first lines of each cell for a split row
    private readonly record struct Placement(RowLayout Row, double Top, double Height, int[]? LineCounts)
    {
        public static Placement Whole(RowLayout row, double top) => new(row, top, row.Height, LineCounts: null);
    }
}
