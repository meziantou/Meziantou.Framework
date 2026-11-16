using System.Globalization;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Bates numbering: a number stamped on every page, in sequence across the documents of a production, with a prefix and a
/// suffix, such as <c>ABC000123</c>. The number of a page is <see cref="StartNumber"/> plus the index of the page.
/// </summary>
public sealed class PdfBatesNumbering
{
    /// <summary>Gets the text before the number, such as the case code. The default is none.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>Gets the text after the number. The default is none.</summary>
    public string Suffix { get; init; } = "";

    /// <summary>Gets the least number of digits of the number, padded with zeros. The default is 6.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 18.</exception>
    public int Digits
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 18);
            field = value;
        }
    } = 6;

    /// <summary>Gets the number of the first page. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long StartNumber
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 1;

    /// <summary>Formats a Bates number: the prefix, the number padded to <see cref="Digits"/>, the suffix.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The text.</returns>
    public string Format(long number) => Prefix + number.ToString("D" + Digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + Suffix;
}
