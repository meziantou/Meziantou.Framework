namespace Meziantou.Framework.Pdf;

public sealed partial class PdfPage
{
    /// <summary>
    /// Adds a text watermark, such as "DRAFT" or "CONFIDENTIAL", centered on the page as it is displayed (cropped and
    /// rotated), at another <see cref="PdfWatermarkOptions.Position"/>, or repeated over the page
    /// (<see cref="PdfWatermarkOptions.IsTiled"/>), as an artifact (14.8.2.2) so that the readers of tagged documents skip it.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfWatermarkOptions.Default"/>.</param>
    /// <remarks>
    /// <para>
    /// The text is drawn once in a form XObject. <see cref="PdfDocument.AddWatermark(string, PdfWatermarkOptions?)"/> adds
    /// the same form to every page. The watermark is content of the page: its text is extracted with the text of the page.
    /// </para>
    /// <para>
    /// The method adds the watermark to this page: it ignores <see cref="PdfWatermarkOptions.FirstPageIndex"/> and
    /// <see cref="PdfWatermarkOptions.LastPageIndex"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty, or a character has no glyph in the font.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The opacity is not between 0 and 1, the font size is not a positive number, or the rotation is not a finite number.</exception>
    public void AddWatermark(string text, PdfWatermarkOptions? options = null)
    {
        options ??= PdfWatermarkOptions.Default;
        var watermark = PdfWatermark.Create(Document, text, options);
        watermark.Draw(this, options);
    }
}
