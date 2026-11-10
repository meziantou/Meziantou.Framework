namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfPage.AddWatermark(string, PdfWatermarkOptions?)"/> and <see cref="PdfDocument.AddWatermark(string, PdfWatermarkOptions?)"/>.</summary>
public sealed class PdfWatermarkOptions
{
    /// <summary>Gets the default options: gray Helvetica Bold at 30% opacity, along the diagonal of the page, over its content.</summary>
    public static PdfWatermarkOptions Default { get; } = new();

    /// <summary>Gets the font, or <see langword="null"/> for Helvetica Bold. The standard 14 fonts only show the characters of <c>WinAnsiEncoding</c>.</summary>
    public PdfFont? Font { get; init; }

    /// <summary>Gets the font size, in points, or <see langword="null"/> to fit the text in 80% of the page.</summary>
    public double? FontSize { get; init; }

    /// <summary>Gets the color of the text. The default is gray.</summary>
    public PdfColor Color { get; init; } = PdfColor.FromGray(0.5);

    /// <summary>Gets the opacity of the text, from 0 (invisible) to 1 (opaque). The default is 0.3.</summary>
    public double Opacity { get; init; } = 0.3;

    /// <summary>
    /// Gets the angle of the text, in degrees, counterclockwise as the page is displayed, or <see langword="null"/> to
    /// follow the diagonal from the lower-left corner to the upper-right corner.
    /// </summary>
    public double? Rotation { get; init; }

    /// <summary>
    /// Gets where the watermark is painted. The default, <see cref="PdfContentPlacement.Over"/>, shows it over images
    /// and backgrounds; <see cref="PdfContentPlacement.Under"/> keeps the content of the page above it.
    /// </summary>
    public PdfContentPlacement Placement { get; init; }
}
