namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Adds a text watermark, such as "DRAFT" or "CONFIDENTIAL", to the pages from
    /// <see cref="PdfWatermarkOptions.FirstPageIndex"/> to <see cref="PdfWatermarkOptions.LastPageIndex"/> (every page by
    /// default): the text is drawn once, in a form XObject that each page paints, centered on the page as it is displayed
    /// and fitted to its size, at another <see cref="PdfWatermarkOptions.Position"/>, or repeated over the page
    /// (<see cref="PdfWatermarkOptions.IsTiled"/>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfWatermarkOptions.Default"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty, or a character has no glyph in the font.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The opacity is not between 0 and 1, the font size is not a positive number, or the rotation is not a finite number.</exception>
    /// <seealso cref="PdfPage.AddWatermark(string, PdfWatermarkOptions?)"/>
    public void AddWatermark(string text, PdfWatermarkOptions? options = null)
    {
        options ??= PdfWatermarkOptions.Default;
        var watermark = PdfWatermark.Create(this, text, options);
        var lastIndex = Math.Min(options.LastPageIndex ?? Pages.Count - 1, Pages.Count - 1);
        for (var index = options.FirstPageIndex; index <= lastIndex; index++)
        {
            watermark.Draw(Pages[index], options);
        }
    }
}
