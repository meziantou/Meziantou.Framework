using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>The labelling of a range of pages (ISO 32000-2, 12.4.2): a numbering style, a prefix and the number of its first page.</summary>
public sealed record PdfPageLabel
{
    /// <summary>Initializes a new instance of the <see cref="PdfPageLabel"/> class.</summary>
    /// <param name="style">The numbering style.</param>
    /// <param name="prefix">The text before the number, or <see langword="null"/>.</param>
    /// <param name="startNumber">The number of the first page of the range, 1 or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startNumber"/> is less than 1.</exception>
    public PdfPageLabel(PdfPageLabelStyle style, string? prefix = null, int startNumber = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startNumber, 1);
        if (!Enum.IsDefined(style))
            throw new ArgumentOutOfRangeException(nameof(style), style, "The style is not valid");

        Style = style;
        Prefix = prefix;
        StartNumber = startNumber;
    }

    /// <summary>Gets the numbering style.</summary>
    public PdfPageLabelStyle Style { get; }

    /// <summary>Gets the text before the number, or <see langword="null"/>.</summary>
    public string? Prefix { get; }

    /// <summary>Gets the number of the first page of the range.</summary>
    public int StartNumber { get; }

    /// <summary>Gets the label of a page of the range.</summary>
    /// <param name="offset">The index of the page in the range, from 0.</param>
    /// <returns>The label: the prefix followed by the number of the page in the style.</returns>
    public string GetLabel(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var number = (long)StartNumber + offset;
        var text = Style switch
        {
            PdfPageLabelStyle.Decimal => number.ToString(CultureInfo.InvariantCulture),
            PdfPageLabelStyle.UppercaseRoman => ToRoman(number),
            PdfPageLabelStyle.LowercaseRoman => ToRoman(number).ToLowerInvariant(),
            PdfPageLabelStyle.UppercaseLetters => ToLetters(number),
            PdfPageLabelStyle.LowercaseLetters => ToLetters(number).ToLowerInvariant(),
            _ => "",
        };
        return Prefix + text;
    }

    private static string ToRoman(long number)
    {
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var result = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                result.Append(symbols[i]);
                number -= values[i];
            }
        }

        return result.ToString();
    }

    /// <summary>A to Z for 1 to 26, AA to ZZ for 27 to 52, AAA to ZZZ for 53 to 78... (12.4.2).</summary>
    private static string ToLetters(long number)
    {
        var letter = (char)('A' + ((number - 1) % 26));
        return new string(letter, (int)Math.Min(((number - 1) / 26) + 1, 1000));
    }
}
