using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>Options for reading the Data Matrix, PDF417 and Aztec symbols of an image.</summary>
public sealed class Barcode2DReadingOptions
{
    /// <summary>Gets or sets the types of symbols to read, or <see langword="null"/> to read them all. Default is <see langword="null"/>.</summary>
    public IReadOnlyCollection<Barcode2DType>? Types { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the symbols printed light on a dark background are read too. Default is
    /// <see langword="true"/>.
    /// </summary>
    public bool TryInverted { get; set; } = true;

    /// <summary>Gets or sets the maximum number of symbols to read. Default is <see cref="int.MaxValue"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaximumResults
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = int.MaxValue;
}
