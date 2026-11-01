namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for the creation of standard QR codes by <see cref="QRCode.Create(string, ErrorCorrectionLevel, QRCodeOptions)"/>.
/// </summary>
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
}
