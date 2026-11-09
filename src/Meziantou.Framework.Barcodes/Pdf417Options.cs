namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for the creation of PDF417 symbols by <see cref="Barcode2D.CreatePdf417(string, Pdf417Options?)"/>.
/// </summary>
/// <remarks>
/// A PDF417 symbol has 3 to 90 rows of 1 to 30 data columns, between a start pattern, a left row indicator, a right row
/// indicator and a stop pattern. Each column is 17 modules wide, so a symbol is 69 + 17 × columns modules wide.
/// </remarks>
public sealed class Pdf417Options
{
    /// <summary>
    /// Gets or sets the error correction level (also called the security level), from 0 (2 error correction codewords) to
    /// 8 (512 codewords), or <see langword="null"/> for the minimum level ISO/IEC 15438 recommends for the data length (2 to 5).
    /// </summary>
    /// <remarks>
    /// A symbol holds at most 928 codewords, error correction included. When the level is <see langword="null"/> and the data
    /// does not fit with the recommended level, the highest level that fits is used, down to 2.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 0 or greater than 8.</exception>
    public int? SecurityLevel
    {
        get;
        set
        {
            if (value is < 0 or > 8)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The error correction level of a PDF417 symbol is from 0 to 8.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of data columns, from 1 to 30, or <see langword="null"/> to choose it from the data.
    /// </summary>
    /// <remarks>The number of rows is the smallest that holds the data with this number of columns.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 30.</exception>
    public int? Columns
    {
        get;
        set
        {
            if (value is < 1 or > 30)
                throw new ArgumentOutOfRangeException(nameof(value), value, "A PDF417 symbol has 1 to 30 data columns.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the height of each row, in modules. The default is 3, the minimum ISO/IEC 15438 recommends.
    /// </summary>
    /// <remarks>Each row of the symbol is this number of rows in <see cref="Barcode2D"/>, whose modules are square.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 20.</exception>
    public int RowHeight
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 20);
            field = value;
        }
    } = 3;
}
