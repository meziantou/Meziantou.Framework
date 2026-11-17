using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>The size of the pages of a PDF output: the size of each image (<see cref="Fit"/>) or a fixed size.</summary>
internal sealed class PageSize
{
    private const double PointsPerMillimeter = 72 / 25.4;

    private PageSize(PdfSize? size, string text)
    {
        Size = size;
        Text = text;
    }

    public static PageSize Fit { get; } = new(size: null, "fit");

    /// <summary>Gets the size, or null for <see cref="Fit"/>.</summary>
    public PdfSize? Size { get; }

    /// <summary>Gets the text the size was parsed from.</summary>
    public string Text { get; }

    /// <summary>Parses <c>fit</c>, a paper name (<c>A3</c>, <c>A4</c>, <c>A5</c>, <c>Letter</c>, <c>Legal</c>) or a size such as <c>210x297mm</c>, <c>8.5x11in</c> or <c>595x842pt</c>.</summary>
    /// <exception cref="FormatException">The value is not a valid page size.</exception>
    public static PageSize Parse(string value) => value.Equals("fit", StringComparison.OrdinalIgnoreCase) ? Fit : new PageSize(ParseSize(value), value);

    /// <summary>Parses a paper name or a size (see <see cref="Parse"/>), without <c>fit</c>.</summary>
    /// <exception cref="FormatException">The value is not a valid page size.</exception>
    public static PageSize ParseFixed(string value) => new(ParseSize(value), value);

    /// <summary>Parses a paper name or a size (see <see cref="Parse"/>), without <c>fit</c>, to its size in points.</summary>
    /// <exception cref="FormatException">The value is not a valid page size.</exception>
    public static PdfSize ParseSize(string value)
    {
        var trimmed = value.Trim();
        switch (trimmed.ToUpperInvariant())
        {
            case "A3": return PdfSize.A3;
            case "A4": return PdfSize.A4;
            case "A5": return PdfSize.A5;
            case "LETTER": return PdfSize.Letter;
            case "LEGAL": return PdfSize.Legal;
        }

        var unitLength = 0;
        while (unitLength < trimmed.Length && char.IsAsciiLetter(trimmed[^(unitLength + 1)]))
        {
            unitLength++;
        }

        var unit = trimmed[^unitLength..].ToUpperInvariant();
        var scale = unit switch
        {
            "MM" => PointsPerMillimeter,
            "CM" => PointsPerMillimeter * 10,
            "IN" => 72,
            "PT" => 1,
            _ => throw new FormatException($"The page size '{value}' is not valid. Use fit, A3, A4, A5, Letter, Legal or a size such as 210x297mm, 8.5x11in or 595x842pt"),
        };

        var dimensions = trimmed[..^unitLength].Split('x', 'X');
        if (dimensions.Length is not 2
            || !double.TryParse(dimensions[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(dimensions[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var height)
            || width <= 0 || height <= 0 || width * scale > 14400 || height * scale > 14400)
        {
            throw new FormatException($"The page size '{value}' is not valid. Use a size such as 210x297mm, 8.5x11in or 595x842pt, of at most 200 inches");
        }

        return new PdfSize(width * scale, height * scale);
    }

    public static double MillimetersToPoints(double value) => value * PointsPerMillimeter;

    public override string ToString() => Text;
}
