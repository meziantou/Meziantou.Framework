using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The PNG predictor the writer applies to the samples of an image before it compresses them with <c>FlateDecode</c> (ISO 32000-2, 7.4.4.4).</summary>
/// <param name="Colors">The number of components of a sample.</param>
/// <param name="BitsPerComponent">The bits of a component: 8 or 16.</param>
/// <param name="Columns">The number of samples of a row.</param>
internal sealed record PdfPngPredictor(int Colors, int BitsPerComponent, int Columns)
{
    public int RowLength => (int)((((long)Columns * Colors * BitsPerComponent) + 7) / 8);

    public int BytesPerPixel => Math.Max(1, ((Colors * BitsPerComponent) + 7) / 8);

    /// <summary>Gets the <c>/DecodeParms</c> of the filter: predictor 15, whose rows each say their PNG filter.</summary>
    public PdfDictionary CreateParameters() => new()
    {
        [PdfNames.Predictor] = PdfInteger.Get(15),
        [PdfNames.Colors] = PdfInteger.Get(Colors),
        [PdfNames.BitsPerComponent] = PdfInteger.Get(BitsPerComponent),
        [PdfNames.Columns] = PdfInteger.Get(Columns),
    };
}
