using Meziantou.Framework.Pdf.Layout;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Builds a table of a flow document: its columns, its header rows (repeated on each page the table continues on) and
/// its rows. A row and the rows its cells span move to the next page or column when they do not fit the rest of the
/// current one; rows taller than a page or a column are split between them.
/// </summary>
public sealed class PdfFlowTableBuilder
{
    private readonly PdfFlowOptions _options;
    private double _borderWidth = 0.5;
    private double _cellPadding = 4;

    internal PdfFlowTableBuilder(PdfFlowOptions options)
    {
        _options = options;
    }

    internal List<double> ColumnWeights { get; } = [];

    internal List<(List<FlowCell> Cells, bool IsHeader)> Rows { get; } = [];

    /// <summary>Gets or sets the width of the borders of the cells, in points; 0 for none. The default is 0.5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double BorderWidth
    {
        get => _borderWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _borderWidth = value;
        }
    }

    /// <summary>Gets or sets the color of the borders. The default is gray.</summary>
    public PdfColor BorderColor { get; set; } = PdfColor.FromGray(0.5);

    /// <summary>Gets or sets the space between the borders and the content of the cells, in points. The default is 4.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double CellPadding
    {
        get => _cellPadding;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _cellPadding = value;
        }
    }

    /// <summary>Gets or sets the background of the header rows, or <see langword="null"/> for none. The default is light gray.</summary>
    public PdfColor? HeaderBackground { get; set; } = PdfColor.FromGray(0.9);

    /// <summary>Sets the relative widths of the columns: the width of the table is divided in proportion. Without it, the columns of the first row have equal widths.</summary>
    /// <param name="weights">The weights, one per column.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">A weight is not positive, or there is none.</exception>
    public PdfFlowTableBuilder Columns(params ReadOnlySpan<double> weights)
    {
        if (weights.IsEmpty)
            throw new ArgumentException("A table needs a column", nameof(weights));

        foreach (var weight in weights)
        {
            if (!(weight > 0 && double.IsFinite(weight)))
                throw new ArgumentException("The weights must be positive", nameof(weights));
        }

        ColumnWeights.Clear();
        ColumnWeights.AddRange(weights);
        return this;
    }

    /// <summary>Adds a header row of texts: its cells are header cells (<c>TH</c>) of their columns.</summary>
    /// <param name="cells">The texts of the cells.</param>
    /// <returns>The builder.</returns>
    public PdfFlowTableBuilder HeaderRow(params IEnumerable<string> cells) => HeaderRow(row => AddCells(row, cells));

    /// <summary>Adds a header row: its cells are header cells (<c>TH</c>) of their columns.</summary>
    /// <param name="build">The function that adds the cells.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">A row that is not a header row was added before.</exception>
    public PdfFlowTableBuilder HeaderRow(Action<PdfFlowRowBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (Rows.Any(row => !row.IsHeader))
            throw new InvalidOperationException("The header rows come before the other rows");

        return AddRow(build, isHeader: true);
    }

    /// <summary>Adds a row of texts.</summary>
    /// <param name="cells">The texts of the cells.</param>
    /// <returns>The builder.</returns>
    public PdfFlowTableBuilder Row(params IEnumerable<string> cells) => Row(row => AddCells(row, cells));

    /// <summary>Adds a row.</summary>
    /// <param name="build">The function that adds the cells.</param>
    /// <returns>The builder.</returns>
    public PdfFlowTableBuilder Row(Action<PdfFlowRowBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return AddRow(build, isHeader: false);
    }

    private static void AddCells(PdfFlowRowBuilder row, IEnumerable<string> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        foreach (var cell in cells)
        {
            row.Cell(cell);
        }
    }

    private PdfFlowTableBuilder AddRow(Action<PdfFlowRowBuilder> build, bool isHeader)
    {
        var row = new PdfFlowRowBuilder(_options);
        build(row);
        Rows.Add((row.Cells, isHeader));
        return this;
    }
}
