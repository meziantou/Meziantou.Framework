using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A polygon annotation (ISO 32000-2, 12.5.6.9): a closed shape of vertices, drawn with the color of the annotation and filled with its interior color.</summary>
public sealed class PdfPolygonAnnotation : PdfMarkupAnnotation
{
    internal PdfPolygonAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets the vertices (<c>/Vertices</c>), in default user space.</summary>
    public IReadOnlyList<PdfPoint> Vertices
    {
        get => ReadPoints(Dictionary.Get<PdfArray>(AnnotationNames.Vertices));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Dictionary[AnnotationNames.Vertices] = WritePoints(value);
        }
    }

    /// <summary>Gets or sets the color that fills the polygon (<c>/IC</c>), or <see langword="null"/> for a polygon that is not filled.</summary>
    public PdfColor? InteriorColor
    {
        get => ReadColor(AnnotationNames.IC);
        set => WriteColor(AnnotationNames.IC, value);
    }
}
