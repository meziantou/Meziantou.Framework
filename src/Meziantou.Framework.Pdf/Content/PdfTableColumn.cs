namespace Meziantou.Framework.Pdf;

/// <summary>A column of a <see cref="PdfTable"/>: its width and the alignment of its cells.</summary>
public sealed class PdfTableColumn
{
    /// <summary>Initializes a new instance of the <see cref="PdfTableColumn"/> class, with an automatic width.</summary>
    public PdfTableColumn()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfTableColumn"/> class.</summary>
    /// <param name="width">The width of the column.</param>
    /// <param name="alignment">The horizontal alignment of the text of its cells.</param>
    public PdfTableColumn(PdfTableColumnWidth width, PdfTextAlignment alignment = PdfTextAlignment.Left)
    {
        Width = width;
        Alignment = alignment;
    }

    /// <summary>Gets or sets the width of the column. The default is <see cref="PdfTableColumnWidth.Auto"/>.</summary>
    public PdfTableColumnWidth Width { get; set; }

    /// <summary>Gets or sets the horizontal alignment of the text of the cells of the column, unless a cell sets its own. The default is <see cref="PdfTextAlignment.Left"/>.</summary>
    public PdfTextAlignment Alignment { get; set; }
}
