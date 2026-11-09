namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for the creation of Data Matrix symbols by <see cref="Barcode2D.CreateDataMatrix(string, DataMatrixOptions?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// An ECC 200 symbol is square, from 10 × 10 to 144 × 144 modules, or rectangular: 8 × 18, 8 × 32, 12 × 26, 12 × 36,
/// 16 × 36 or 16 × 48 modules (height × width). Among the sizes that satisfy these options and hold the data, the symbol
/// has the one with the fewest modules. Set <see cref="Height"/> and <see cref="Width"/> to get a symbol of that exact size:
/// data shorter than its capacity is padded.
/// </para>
/// <para>
/// The sizes are numbers of modules, not the size of the image: the <c>ModuleSize</c> of the renderers sets how many
/// pixels or units a module takes.
/// </para>
/// </remarks>
public sealed class DataMatrixOptions
{
    /// <summary>Gets or sets the shapes the symbol can have. The default is <see cref="DataMatrixShape.Square"/>.</summary>
    /// <remarks>The shape is ignored when <see cref="Height"/> or <see cref="Width"/> is set.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="DataMatrixShape"/> value.</exception>
    public DataMatrixShape Shape
    {
        get;
        set
        {
            if (value is not (DataMatrixShape.Square or DataMatrixShape.Rectangle or DataMatrixShape.Any))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The shape must be Square, Rectangle or Any.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the exact height of the symbol in modules, or <see langword="null"/> for any height.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not the height of a Data Matrix size.</exception>
    public int? Height
    {
        get;
        set
        {
            if (value is not null && !Internal.DataMatrix.DataMatrixSymbolSize.IsHeight(value.Value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not the height of a Data Matrix symbol.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the exact width of the symbol in modules, or <see langword="null"/> for any width.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not the width of a Data Matrix size.</exception>
    public int? Width
    {
        get;
        set
        {
            if (value is not null && !Internal.DataMatrix.DataMatrixSymbolSize.IsWidth(value.Value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not the width of a Data Matrix symbol.");

            field = value;
        }
    }
}
