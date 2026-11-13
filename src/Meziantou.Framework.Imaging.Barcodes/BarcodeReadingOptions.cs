using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>Options for reading the linear barcodes of an image.</summary>
public sealed class BarcodeReadingOptions
{
    /// <summary>Gets or sets the types of barcodes to read, or <see langword="null"/> to read them all. Default is <see langword="null"/>.</summary>
    public IReadOnlyCollection<BarcodeType>? Types { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the barcodes printed light on a dark background are read, when the image has
    /// no barcodes printed dark on light. Default is <see langword="true"/>.
    /// </summary>
    public bool TryInverted { get; set; } = true;

    /// <summary>Gets or sets the maximum number of barcodes to read. Default is <see cref="int.MaxValue"/>.</summary>
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
