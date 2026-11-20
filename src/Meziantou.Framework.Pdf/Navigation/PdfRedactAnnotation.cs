using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A redaction annotation (ISO 32000-2, 12.5.6.23): marks regions of the page whose content is to be removed. The content
/// stays until the redaction is applied, by <see cref="PdfDocument.ApplyRedactions(PdfRedactionOptions?)"/> or by a viewer.
/// </summary>
public sealed class PdfRedactAnnotation : PdfMarkupAnnotation
{
    internal PdfRedactAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>
    /// Gets or sets the regions to remove (<c>/QuadPoints</c>), in default user space; when the annotation has none, the
    /// region is its rectangle. The getter returns the bounds of each quadrilateral, all of them: a region left out would
    /// stay readable once the redaction is applied.
    /// </summary>
    public IReadOnlyList<PdfRectangle> Regions
    {
        get
        {
            var regions = ReadQuadrilaterals(Dictionary.Get<PdfArray>(AnnotationNames.QuadPoints), int.MaxValue);
            return regions.Count > 0 ? regions : [Rectangle];
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Dictionary[AnnotationNames.QuadPoints] = value.Count == 0 ? null : WriteQuadrilaterals(value);
        }
    }

    /// <summary>Gets or sets the color that fills the regions once they are redacted (<c>/IC</c>), or <see langword="null"/> to leave them empty.</summary>
    public PdfColor? InteriorColor
    {
        get => ReadColor(AnnotationNames.IC);
        set => WriteColor(AnnotationNames.IC, value);
    }

    /// <summary>Gets or sets the text drawn in the regions once they are redacted (<c>/OverlayText</c>), such as <c>REDACTED</c>.</summary>
    public string? OverlayText
    {
        get => Dictionary.GetText(AnnotationNames.OverlayText);
        set => Dictionary.SetText(AnnotationNames.OverlayText, value);
    }

    /// <summary>Gets or sets a value indicating whether <see cref="OverlayText"/> is repeated to fill the regions (<c>/Repeat</c>).</summary>
    public bool RepeatOverlayText
    {
        get => Dictionary.GetBoolean(AnnotationNames.Repeat);
        set => Dictionary[AnnotationNames.Repeat] = value ? PdfBoolean.True : null;
    }
}
