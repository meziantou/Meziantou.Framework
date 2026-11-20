using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A square annotation (ISO 32000-2, 12.5.6.8): a rectangle, drawn with the color of the annotation and filled with its interior color.</summary>
public sealed class PdfSquareAnnotation : PdfMarkupAnnotation
{
    internal PdfSquareAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the color that fills the shape (<c>/IC</c>), or <see langword="null"/> for a shape that is not filled.</summary>
    public PdfColor? InteriorColor
    {
        get => ReadColor(AnnotationNames.IC);
        set => WriteColor(AnnotationNames.IC, value);
    }
}
