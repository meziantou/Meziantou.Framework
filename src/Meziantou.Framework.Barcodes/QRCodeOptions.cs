namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for the creation of standard QR codes by <see cref="QRCode.Create(string, ErrorCorrectionLevel, QRCodeOptions)"/>.
/// </summary>
/// <remarks>
/// The version sets the number of modules of the symbol (17 + 4 × version per side), not the size of the image: the
/// <c>ModuleSize</c> of the renderers sets how many pixels or units a module takes.
/// </remarks>
public sealed class QRCodeOptions
{
    /// <summary>The ECI assignment number of UTF-8 (26).</summary>
    public const int Utf8EciAssignmentNumber = 26;

    /// <summary>
    /// Gets or sets the ECI (Extended Channel Interpretation) assignment number declared at the start of the data, so
    /// that readers know the character encoding of its bytes instead of guessing it. The default is <see langword="null"/>:
    /// no ECI designator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A text is encoded as UTF-8, so the only assignment number it accepts is <see cref="Utf8EciAssignmentNumber"/>.
    /// Binary data accepts any assignment number from 0 to 999999, such as 3 for ISO-8859-1.
    /// </para>
    /// <para>
    /// The designator takes 12 to 28 bits, which can give a larger symbol. With an ECI designator, a text is never encoded
    /// in Kanji mode (its kanji and kana are UTF-8, like the other characters).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 999999.</exception>
    public int? EciAssignmentNumber
    {
        get;
        set
        {
            if (value is < 0 or > 999999)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The ECI assignment number must be between 0 and 999999.");

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the smallest version the symbol can have, from 1 (21 × 21 modules) to 40 (177 × 177 modules). The default is 1.
    /// </summary>
    /// <remarks>
    /// The symbol has the smallest version from <see cref="MinimumVersion"/> to <see cref="MaximumVersion"/> that holds the
    /// data. Set both to the same value to get a symbol of that exact version: data shorter than its capacity is padded.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 40.</exception>
    public int MinimumVersion
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 40);
            field = value;
        }
    } = 1;

    /// <summary>
    /// Gets or sets the largest version the symbol can have, from 1 (21 × 21 modules) to 40 (177 × 177 modules). The default is 40.
    /// </summary>
    /// <remarks>
    /// Data that does not fit in this version throws an <see cref="InvalidOperationException"/>, instead of giving a larger symbol.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 40.</exception>
    public int MaximumVersion
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 40);
            field = value;
        }
    } = 40;
}
