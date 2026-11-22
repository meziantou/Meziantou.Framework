namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfPage.AddWatermark(string, PdfWatermarkOptions?)"/> and <see cref="PdfDocument.AddWatermark(string, PdfWatermarkOptions?)"/>.</summary>
public sealed class PdfWatermarkOptions
{
    /// <summary>Gets the default options: gray Helvetica Bold at 30% opacity, along the diagonal of the page, over its content.</summary>
    public static PdfWatermarkOptions Default { get; } = new();

    /// <summary>Gets the font, or <see langword="null"/> for Helvetica Bold. The standard 14 fonts only show the characters of <c>WinAnsiEncoding</c>.</summary>
    public PdfFont? Font { get; init; }

    /// <summary>
    /// Gets the font size, in points, or <see langword="null"/> to fit the text in 80% of the page when it is painted
    /// once in the center, and for 1/20 of the diagonal of the page when it is in another <see cref="Position"/> or
    /// <see cref="IsTiled"/>.
    /// </summary>
    public double? FontSize { get; init; }

    /// <summary>Gets the color of the text. The default is gray.</summary>
    public PdfColor Color { get; init; } = PdfColor.FromGray(0.5);

    /// <summary>Gets the opacity of the text, from 0 (invisible) to 1 (opaque). The default is 0.3.</summary>
    public double Opacity { get; init; } = 0.3;

    /// <summary>
    /// Gets the angle of the text, in degrees, counterclockwise as the page is displayed, or <see langword="null"/> to
    /// follow the diagonal from the lower-left corner to the upper-right corner when the text is in the center or
    /// <see cref="IsTiled"/>, and to keep it horizontal in the other positions.
    /// </summary>
    public double? Rotation { get; init; }

    /// <summary>
    /// Gets where the watermark is painted. The default, <see cref="PdfContentPlacement.Over"/>, shows it over images
    /// and backgrounds; <see cref="PdfContentPlacement.Under"/> keeps the content of the page above it.
    /// </summary>
    public PdfContentPlacement Placement { get; init; }

    /// <summary>Gets where the text is placed on the page as it is displayed. The default is <see cref="PdfWatermarkPosition.Center"/>. A tiled watermark ignores it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of <see cref="PdfWatermarkPosition"/>.</exception>
    public PdfWatermarkPosition Position
    {
        get;
        init
        {
            if (value is < PdfWatermarkPosition.Center or > PdfWatermarkPosition.BottomRight)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The position is unknown");

            field = value;
        }
    }

    /// <summary>
    /// Gets the distance, in points, between the text and the edges of the page as it is displayed. The default is 36
    /// (half an inch). A centered or tiled watermark ignores it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public double Margin
    {
        get;
        init
        {
            if (!(value >= 0 && double.IsFinite(value)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The margin must be a finite number greater than or equal to 0");

            field = value;
        }
    } = 36;

    /// <summary>
    /// Gets whether the text is repeated over the whole page, in rows whose copies are offset by half a copy from one row
    /// to the next, instead of being painted once at <see cref="Position"/>. The default is <see langword="false"/>.
    /// </summary>
    public bool IsTiled { get; init; }

    /// <summary>
    /// Gets the space, in points, between the copies of a tiled watermark, horizontally and vertically, or
    /// <see langword="null"/> (the default) for the height of the text.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a finite number.</exception>
    public double? TileSpacing
    {
        get;
        init
        {
            if (value is { } spacing && !(spacing >= 0 && double.IsFinite(spacing)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The spacing must be a finite number greater than or equal to 0");

            field = value;
        }
    }

    /// <summary>
    /// Gets the index of the first page <see cref="PdfDocument.AddWatermark(string, PdfWatermarkOptions?)"/> adds the
    /// watermark to. The default is 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int FirstPageIndex
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets the index of the last page <see cref="PdfDocument.AddWatermark(string, PdfWatermarkOptions?)"/> adds the
    /// watermark to, or <see langword="null"/> (the default) for the last page of the document. An index after the last
    /// page stands for the last page.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? LastPageIndex
    {
        get;
        init
        {
            if (value is { } index)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(value));
            }

            field = value;
        }
    }
}
