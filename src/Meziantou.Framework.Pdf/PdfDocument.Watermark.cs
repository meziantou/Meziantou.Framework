namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Adds a text watermark, such as "DRAFT" or "CONFIDENTIAL", to every page: the text is drawn once, in a form XObject
    /// that each page paints, centered on the page as it is displayed and fitted to its size.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfWatermarkOptions.Default"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty, or a character has no glyph in the font.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The opacity is not between 0 and 1, or the font size is not a positive number.</exception>
    /// <seealso cref="PdfPage.AddWatermark(string, PdfWatermarkOptions?)"/>
    public void AddWatermark(string text, PdfWatermarkOptions? options = null)
    {
        options ??= PdfWatermarkOptions.Default;
        var watermark = PdfWatermark.Create(this, text, options);
        foreach (var page in Pages)
        {
            watermark.Draw(page, options);
        }
    }
}
