namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for the creation of Micro QR codes by <see cref="QRCode.CreateMicroQR(string, ErrorCorrectionLevel, MicroQRCodeOptions)"/>.
/// </summary>
/// <remarks>
/// The version sets the number of modules of the symbol (9 + 2 × version per side), not the size of the image: the
/// <c>ModuleSize</c> of the renderers sets how many pixels or units a module takes.
/// </remarks>
public sealed class MicroQRCodeOptions
{
    /// <summary>
    /// Gets or sets the smallest version the symbol can have, from 1 (M1, 11 × 11 modules) to 4 (M4, 17 × 17 modules). The default is 1.
    /// </summary>
    /// <remarks>
    /// The symbol has the smallest version from <see cref="MinimumVersion"/> to <see cref="MaximumVersion"/> that supports
    /// the error correction level and the characters, and holds the data. Set both to the same value to get a symbol of
    /// that exact version.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 4.</exception>
    public int MinimumVersion
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 4);
            field = value;
        }
    } = 1;

    /// <summary>
    /// Gets or sets the largest version the symbol can have, from 1 (M1, 11 × 11 modules) to 4 (M4, 17 × 17 modules). The default is 4.
    /// </summary>
    /// <remarks>
    /// Data that does not fit in this version throws an <see cref="InvalidOperationException"/>, instead of giving a larger symbol.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 4.</exception>
    public int MaximumVersion
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 4);
            field = value;
        }
    } = 4;
}
