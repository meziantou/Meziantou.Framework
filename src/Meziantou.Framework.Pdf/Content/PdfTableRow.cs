namespace Meziantou.Framework.Pdf;

/// <summary>A row of a <see cref="PdfTable"/>. The properties that are <see langword="null"/> take the value of the table.</summary>
public sealed class PdfTableRow
{
    /// <summary>Initializes a new instance of the <see cref="PdfTableRow"/> class, without cells.</summary>
    public PdfTableRow()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfTableRow"/> class.</summary>
    /// <param name="cells">The texts of the cells, one per column.</param>
    public PdfTableRow(params ReadOnlySpan<string> cells)
    {
        foreach (var cell in cells)
        {
            AddCell(cell);
        }
    }

    /// <summary>Gets the cells of the row, from the left. The columns after the last cell are empty.</summary>
    public IList<PdfTableCell> Cells { get; } = [];

    /// <summary>Gets or sets the font of the cells, or <see langword="null"/> for the font of the table.</summary>
    public PdfFont? Font { get; set; }

    /// <summary>Gets or sets the font size of the cells, or <see langword="null"/> for the font size of the table.</summary>
    public double? FontSize { get; set; }

    /// <summary>Gets or sets the color of the text of the cells, or <see langword="null"/> for the color of the table.</summary>
    public PdfColor? TextColor { get; set; }

    /// <summary>Gets or sets the color that fills the cells, or <see langword="null"/> for no background.</summary>
    public PdfColor? Background { get; set; }

    /// <summary>Adds a cell at the end of the row.</summary>
    /// <param name="text">The text of the cell.</param>
    /// <returns>The cell, to set its properties.</returns>
    public PdfTableCell AddCell(string text)
    {
        var cell = new PdfTableCell(text);
        Cells.Add(cell);
        return cell;
    }
}
