using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A pattern (ISO 32000-2, 8.7): a color that paints the areas it fills or strokes with a tile repeated over them
/// (<see cref="PdfTilingPattern"/>) or a shading (<see cref="PdfShadingPattern"/>). It is set with
/// <see cref="PdfContentBuilder.SetFillPattern(PdfPattern)"/> or <see cref="PdfContentBuilder.SetStrokePattern(PdfPattern)"/>.
/// </summary>
/// <remarks>The pattern space is the default coordinate space of the page, or of the form that uses the pattern, transformed by the matrix of the pattern.</remarks>
public abstract class PdfPattern
{
    private protected PdfPattern()
    {
    }

    /// <summary>Gets the reference to the pattern object in a document, creating it on first use.</summary>
    internal abstract PdfReference GetReference(PdfDocument document);

    private protected static PdfArray ToArray(PdfMatrix matrix) => PdfArray.FromNumbers(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
}
