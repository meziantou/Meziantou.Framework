namespace Meziantou.Framework.Pdf;

/// <summary>A cell of a <see cref="PdfTableRow"/>: a text, broken into lines that fit the width of its column. The properties that are <see langword="null"/> take the value of the row, then of the table.</summary>
public sealed class PdfTableCell
{
    /// <summary>Initializes a new instance of the <see cref="PdfTableCell"/> class.</summary>
    /// <param name="text">The text of the cell. A line feed starts a new paragraph.</param>
    public PdfTableCell(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>Gets or sets the text of the cell. A line feed starts a new paragraph.</summary>
    public string Text
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>Gets or sets the number of columns the cell spans, 1 or more. The default is 1.</summary>
    public int ColumnSpan
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1;

    /// <summary>Gets or sets the font of the text, or <see langword="null"/> for the font of the row.</summary>
    public PdfFont? Font { get; set; }

    /// <summary>Gets or sets the font size of the text, or <see langword="null"/> for the font size of the row.</summary>
    public double? FontSize { get; set; }

    /// <summary>Gets or sets the horizontal alignment of the text, or <see langword="null"/> for the alignment of the column.</summary>
    public PdfTextAlignment? Alignment { get; set; }

    /// <summary>Gets or sets the color of the text, or <see langword="null"/> for the color of the row.</summary>
    public PdfColor? TextColor { get; set; }

    /// <summary>Gets or sets the color that fills the cell, or <see langword="null"/> for the background of the row.</summary>
    public PdfColor? Background { get; set; }
}
