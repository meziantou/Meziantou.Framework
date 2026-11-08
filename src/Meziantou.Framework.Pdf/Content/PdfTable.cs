namespace Meziantou.Framework.Pdf;

/// <summary>
/// A table, drawn by <see cref="PdfContentBuilder.DrawTable(PdfTable, PdfRectangle, PdfTableDrawResult?)"/>: columns
/// with fixed, relative or automatic widths, rows of cells whose texts are broken into lines, borders, and header
/// rows drawn again at the top of each page.
/// </summary>
public sealed class PdfTable
{
    /// <summary>Initializes a new instance of the <see cref="PdfTable"/> class, without columns.</summary>
    public PdfTable()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfTable"/> class.</summary>
    /// <param name="columns">The columns of the table.</param>
    public PdfTable(params ReadOnlySpan<PdfTableColumn> columns)
    {
        foreach (var column in columns)
        {
            ArgumentNullException.ThrowIfNull(column, nameof(columns));
            Columns.Add(column);
        }
    }

    /// <summary>Gets the columns of the table, from the left.</summary>
    public IList<PdfTableColumn> Columns { get; } = [];

    /// <summary>Gets the rows of the table, from the top: the header rows first.</summary>
    public IList<PdfTableRow> Rows { get; } = [];

    /// <summary>Gets or sets the number of rows at the start of <see cref="Rows"/> that are drawn again at the top of each continuation of the table. The default is 0.</summary>
    public int HeaderRowCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets or sets the font of the cells. The default is Helvetica.</summary>
    public PdfFont Font
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

    /// <summary>Gets or sets the font size of the cells, more than 0. The default is 10.</summary>
    public double FontSize
    {
        get;
        set
        {
            CheckPositive(value);
            field = value;
        }
    } = 10;

    /// <summary>Gets or sets the distance between the baselines of two lines of a cell, as a multiple of the font size, more than 0. The default is 1.2.</summary>
    public double LineHeight
    {
        get;
        set
        {
            CheckPositive(value);
            field = value;
        }
    } = 1.2;

    /// <summary>Gets or sets the color of the text of the cells, or <see langword="null"/> for the fill color of the graphics state. The default is <see langword="null"/>.</summary>
    public PdfColor? TextColor { get; set; }

    /// <summary>Gets or sets the space between the borders of a cell and its text, in points, 0 or more. The default is 4.</summary>
    public double CellPadding
    {
        get;
        set
        {
            CheckNonNegative(value);
            field = value;
        }
    } = 4;

    /// <summary>Gets or sets the width of the borders of the cells, in points, 0 or more: 0 draws no border. The default is 0.5.</summary>
    public double BorderWidth
    {
        get;
        set
        {
            CheckNonNegative(value);
            field = value;
        }
    } = 0.5;

    /// <summary>Gets or sets the color of the borders. The default is black.</summary>
    public PdfColor BorderColor { get; set; } = PdfColor.Black;

    /// <summary>Gets or sets the hyphenator of the words that do not fit at the end of a line of a cell, or <see langword="null"/> to break the lines between the words only. The default is <see langword="null"/>.</summary>
    public IPdfHyphenator? Hyphenator { get; set; }

    /// <summary>Adds a row at the end of the table.</summary>
    /// <param name="cells">The texts of the cells, one per column.</param>
    /// <returns>The row, to set its properties.</returns>
    public PdfTableRow AddRow(params ReadOnlySpan<string> cells)
    {
        var row = new PdfTableRow(cells);
        Rows.Add(row);
        return row;
    }

    /// <summary>Adds a header row after the header rows of the table, and increments <see cref="HeaderRowCount"/>.</summary>
    /// <param name="cells">The texts of the cells, one per column.</param>
    /// <returns>The row, to set its properties.</returns>
    public PdfTableRow AddHeaderRow(params ReadOnlySpan<string> cells)
    {
        var row = new PdfTableRow(cells);
        var index = Math.Min(HeaderRowCount, Rows.Count);
        Rows.Insert(index, row);
        HeaderRowCount = index + 1;
        return row;
    }

    /// <summary>Computes the widths of the columns of the table in a width.</summary>
    /// <param name="availableWidth">The width the table is drawn in.</param>
    /// <returns>The width of each column, in points.</returns>
    /// <remarks>
    /// The fixed columns get their width. The automatic columns get the width of their widest paragraph, or less when
    /// the table would be wider than <paramref name="availableWidth"/>: down to their widest word, then in proportion to
    /// it. The relative columns share the width left. The cells that span several columns do not count in the automatic
    /// widths.
    /// </remarks>
    /// <exception cref="ArgumentException">A character of a cell has no glyph in its font.</exception>
    public double[] GetColumnWidths(double availableWidth)
    {
        var count = Columns.Count;
        var widths = new double[count];
        var minWidths = new double[count];
        var maxWidths = new double[count];
        var hasAuto = false;
        var fixedWidth = 0d;
        var totalWeight = 0d;
        for (var i = 0; i < count; i++)
        {
            var width = Columns[i].Width;
            switch (width.Type)
            {
                case PdfTableColumnWidthType.Fixed:
                    widths[i] = width.Value;
                    fixedWidth += width.Value;
                    break;
                case PdfTableColumnWidthType.Relative:
                    totalWeight += width.Value;
                    break;
                default:
                    hasAuto = true;
                    minWidths[i] = 2 * CellPadding;
                    maxWidths[i] = 2 * CellPadding;
                    break;
            }
        }

        if (hasAuto)
        {
            foreach (var row in Rows)
            {
                var column = 0;
                foreach (var cell in row.Cells)
                {
                    if (column >= count)
                        break;

                    if (cell.ColumnSpan == 1 && Columns[column].Width.Type == PdfTableColumnWidthType.Auto)
                    {
                        var (min, max) = PdfTextWrapper.MeasureWidths(cell.Text, cell.Font ?? row.Font ?? Font, cell.FontSize ?? row.FontSize ?? FontSize);
                        minWidths[column] = Math.Max(minWidths[column], min + (2 * CellPadding));
                        maxWidths[column] = Math.Max(maxWidths[column], max + (2 * CellPadding));
                    }

                    column += cell.ColumnSpan;
                }
            }

            var available = Math.Max(availableWidth - fixedWidth, 0);
            var totalMin = 0d;
            var totalMax = 0d;
            for (var i = 0; i < count; i++)
            {
                totalMin += minWidths[i];
                totalMax += maxWidths[i];
            }

            for (var i = 0; i < count; i++)
            {
                if (Columns[i].Width.Type != PdfTableColumnWidthType.Auto)
                    continue;

                if (totalMax <= available)
                {
                    widths[i] = maxWidths[i];
                }
                else if (totalMin <= available)
                {
                    // Between the widest word and the widest paragraph, in proportion to the difference
                    widths[i] = minWidths[i] + ((maxWidths[i] - minWidths[i]) * (available - totalMin) / (totalMax - totalMin));
                }
                else
                {
                    widths[i] = available * minWidths[i] / totalMin;
                }

                fixedWidth += widths[i];
            }
        }

        if (totalWeight > 0)
        {
            var left = Math.Max(availableWidth - fixedWidth, 0);
            for (var i = 0; i < count; i++)
            {
                var width = Columns[i].Width;
                if (width.Type == PdfTableColumnWidthType.Relative)
                {
                    widths[i] = left * width.Value / totalWeight;
                }
            }
        }

        return widths;
    }

    private static void CheckPositive(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a finite number, more than 0");
    }

    private static void CheckNonNegative(double value)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a finite number, 0 or more");
    }
}
