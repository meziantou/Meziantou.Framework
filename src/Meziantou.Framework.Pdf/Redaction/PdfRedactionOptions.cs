namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfPage.Redact(IEnumerable{PdfRectangle}, PdfRedactionOptions?)"/>.</summary>
public sealed class PdfRedactionOptions
{
    /// <summary>Gets the default options: the areas are painted black, and the annotations they touch are removed.</summary>
    public static PdfRedactionOptions Default { get; } = new();

    /// <summary>
    /// Gets the color painted over each area once its content is removed, or <see langword="null"/> to paint nothing: the
    /// areas then show what is left under them, such as the page background. The default is black.
    /// </summary>
    public PdfColor? FillColor { get; init; } = PdfColor.Black;

    /// <summary>
    /// Gets a value indicating whether the annotations whose rectangle touches an area are removed, with the values of the
    /// form fields of the widgets removed. The default is <see langword="true"/>.
    /// </summary>
    public bool RemoveAnnotations { get; init; } = true;
}
