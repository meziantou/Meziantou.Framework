namespace Meziantou.Framework.Pdf;

/// <summary>The result of <see cref="PdfContentBuilder.DrawTable(PdfTable, PdfRectangle, PdfTableDrawResult?)"/>: pass it to the next call to continue the table.</summary>
public sealed class PdfTableDrawResult
{
    internal PdfTableDrawResult(bool isComplete, double bottom, int rowCount, int nextRow, string[]? pendingTexts, bool drewNothing, double availableWidth, double[] columnWidths)
    {
        AvailableWidth = availableWidth;
        ColumnWidths = columnWidths;
        IsComplete = isComplete;
        Bottom = bottom;
        RowCount = rowCount;
        NextRow = nextRow;
        PendingTexts = pendingTexts;
        DrewNothing = drewNothing;
    }

    /// <summary>Gets a value indicating whether the last row of the table is drawn.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the y coordinate of the bottom of the part of the table that was drawn: the top of the rectangle when nothing was drawn.</summary>
    public double Bottom { get; }

    /// <summary>Gets the number of rows drawn, without the header rows drawn again at the top of a continuation; a row split between two rectangles is counted in both.</summary>
    public int RowCount { get; }

    // The row the next part starts with
    internal int NextRow { get; }

    // The texts left in the cells of the row split between two rectangles, or null when the next part starts with a whole row
    internal string[]? PendingTexts { get; }

    // No row was drawn: the next rectangle must make progress
    internal bool DrewNothing { get; }

    // The widths of the columns in the width of the rectangle, reused by the next part when its rectangle is as wide
    internal double AvailableWidth { get; }

    internal double[] ColumnWidths { get; }
}
