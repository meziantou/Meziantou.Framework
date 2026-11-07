namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The widget of a visible signature (ISO 32000-2, 12.7.5.5 and 12.5.5): the page and the rectangle it is shown in, and
/// what it shows.
/// </summary>
public sealed class PdfSignatureAppearance
{
    /// <summary>Initializes a new instance of the <see cref="PdfSignatureAppearance"/> class.</summary>
    /// <param name="pageIndex">The index of the page, from 0.</param>
    /// <param name="rectangle">The rectangle of the widget, in the default user space of the page.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative, or the rectangle is empty.</exception>
    public PdfSignatureAppearance(int pageIndex, PdfRectangle rectangle)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (!(rectangle.Width > 0 && rectangle.Height > 0))
            throw new ArgumentOutOfRangeException(nameof(rectangle), rectangle, "The rectangle of a visible signature is empty");

        PageIndex = pageIndex;
        Rectangle = rectangle;
    }

    /// <summary>Gets the index of the page, from 0.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the rectangle of the widget, in the default user space of the page.</summary>
    public PdfRectangle Rectangle { get; }

    /// <summary>
    /// Gets the drawing of the appearance stream, a form whose coordinates go from (0, 0) to the width and the height of
    /// <see cref="Rectangle"/>; <see langword="null"/> for an empty appearance.
    /// </summary>
    public Action<PdfContentBuilder>? Draw { get; init; }
}
