namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfImposition.CreateNUp(PdfDocument, PdfNUpOptions)"/>.</summary>
public sealed class PdfNUpOptions
{
    /// <summary>Gets the number of pages across a sheet. The default is 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 100.</exception>
    public int Columns
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 2;

    /// <summary>Gets the number of pages down a sheet. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 100.</exception>
    public int Rows
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 1;

    /// <summary>Gets the size of the sheets, or <see langword="null"/> (the default) for a grid of cells of the size of the first page.</summary>
    public PdfSize? SheetSize { get; init; }

    /// <summary>Gets the margin around the grid, in points. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double Margin
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets the space between the cells, in points. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double Spacing
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets the order of the pages on a sheet. The default is <see cref="PdfNUpOrder.Horizontal"/>.</summary>
    public PdfNUpOrder Order { get; init; }

    /// <summary>Gets a value indicating whether a thin gray border is drawn around each page. The default is <see langword="false"/>.</summary>
    public bool DrawBorders { get; init; }
}
