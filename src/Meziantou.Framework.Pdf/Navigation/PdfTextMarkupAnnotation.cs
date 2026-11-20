using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A text markup annotation (ISO 32000-2, 12.5.6.10): a highlight, an underline, a squiggly underline or a strikeout of regions of the page, such as the lines of a text.</summary>
public sealed class PdfTextMarkupAnnotation : PdfMarkupAnnotation
{
    internal PdfTextMarkupAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets the kind of markup.</summary>
    public PdfTextMarkupKind Kind => Subtype switch
    {
        "Underline" => PdfTextMarkupKind.Underline,
        "Squiggly" => PdfTextMarkupKind.Squiggly,
        "StrikeOut" => PdfTextMarkupKind.StrikeOut,
        _ => PdfTextMarkupKind.Highlight,
    };

    /// <summary>
    /// Gets or sets the regions of the markup (<c>/QuadPoints</c>), in default user space: one rectangle per line of the
    /// text. The getter returns the bounds of each quadrilateral; read <see cref="PdfAnnotation.Dictionary"/> for the
    /// quadrilaterals of a rotated text.
    /// </summary>
    public IReadOnlyList<PdfRectangle> Regions
    {
        get => ReadQuadrilaterals(Dictionary.Get<PdfArray>(AnnotationNames.QuadPoints));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Dictionary[AnnotationNames.QuadPoints] = WriteQuadrilaterals(value);
        }
    }
}
