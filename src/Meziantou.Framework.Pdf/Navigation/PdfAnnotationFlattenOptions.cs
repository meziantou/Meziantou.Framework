using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.Pdf;

/// <summary>The options of <see cref="PdfPage.FlattenAnnotations(PdfAnnotationFlattenOptions?)"/> and <see cref="PdfDocument.FlattenAnnotations(PdfAnnotationFlattenOptions?)"/>.</summary>
public sealed class PdfAnnotationFlattenOptions
{
    /// <summary>Gets the default options: the markup annotations shown on the screen are flattened.</summary>
    public static PdfAnnotationFlattenOptions Default { get; } = new();

    /// <summary>
    /// Gets the use the flattened page is for: the annotations hidden for it are removed without being drawn
    /// (<see cref="PdfAnnotationFlags.Hidden"/>, <see cref="PdfAnnotationFlags.NoView"/> for the screen, the annotations
    /// without <see cref="PdfAnnotationFlags.Print"/> for printing). The default is <see cref="PdfRenderIntent.View"/>.
    /// </summary>
    public PdfRenderIntent Intent { get; init; } = PdfRenderIntent.View;

    /// <summary>
    /// Gets the function that selects the annotations to flatten, or <see langword="null"/> for the markup annotations
    /// (<see cref="PdfMarkupAnnotation"/>) except the redaction annotations, which
    /// <see cref="PdfDocument.ApplyRedactions(PdfRedactionOptions?)"/> applies. The pop-up annotations are never drawn: they
    /// are removed with their parent. The widgets of the signed signature fields are never flattened.
    /// </summary>
    public Func<PdfAnnotation, bool>? Filter { get; init; }
}
