namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for the creation of Aztec symbols by <see cref="Barcode2D.CreateAztec(string, AztecOptions?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// A compact Aztec symbol has 1 to 4 layers (15 × 15 to 27 × 27 modules), a full-range symbol 1 to 32 layers
/// (19 × 19 to 151 × 151 modules). Among the sizes that satisfy these options and hold the data with the requested error
/// correction, the symbol has the smallest one.
/// </para>
/// <para>
/// The sizes are numbers of modules, not the size of the image: the <c>ModuleSize</c> of the renderers sets how many
/// pixels or units a module takes.
/// </para>
/// </remarks>
public sealed class AztecOptions
{
    /// <summary>
    /// Gets or sets the minimum percentage of the data codewords added as error correction codewords, from 5 to 95. The
    /// default is 23, which ISO/IEC 24778 recommends (the symbol also gets 3 more codewords).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 5 or greater than 95.</exception>
    public int ErrorCorrectionPercent
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 5);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 95);
            field = value;
        }
    } = 23;

    /// <summary>
    /// Gets or sets whether the symbol is compact (<see langword="true"/>), full-range (<see langword="false"/>), or the
    /// smallest of both (<see langword="null"/>, the default).
    /// </summary>
    public bool? Compact { get; set; }

    /// <summary>
    /// Gets or sets the exact number of layers, from 1 to 32 (1 to 4 for a compact symbol), or <see langword="null"/> for the
    /// smallest number that holds the data.
    /// </summary>
    /// <remarks>The data must still fit with <see cref="ErrorCorrectionPercent"/>. The error correction codewords fill the space the data leaves, which can be more.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 32.</exception>
    public int? Layers
    {
        get;
        set
        {
            if (value is < 1 or > 32)
                throw new ArgumentOutOfRangeException(nameof(value), value, "An Aztec symbol has 1 to 32 layers.");

            field = value;
        }
    }
}
