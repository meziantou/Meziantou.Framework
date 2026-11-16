namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfImposition.SplitSpreads(PdfDocument, PdfSpreadSplitOptions?)"/>.</summary>
public sealed class PdfSpreadSplitOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfSpreadSplitOptions Default { get; } = new();

    /// <summary>Gets a value indicating whether only the pages wider than tall, as they are displayed, are split. The default is <see langword="true"/>: a scanned book usually has a portrait cover.</summary>
    public bool OnlyLandscapePages { get; init; } = true;

    /// <summary>Gets a value indicating whether the right half comes first, for the books bound on the right (Japanese, Arabic, Hebrew). The default is <see langword="false"/>.</summary>
    public bool RightToLeft { get; init; }

    /// <summary>Gets the width, in points, of the gutter removed between the halves: half of it on each side of the middle. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double Gutter
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }
}
