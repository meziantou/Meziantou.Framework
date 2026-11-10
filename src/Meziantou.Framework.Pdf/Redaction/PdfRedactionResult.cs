namespace Meziantou.Framework.Pdf;

/// <summary>What <see cref="PdfPage.Redact(IEnumerable{PdfRectangle}, PdfRedactionOptions?)"/> removed from a page.</summary>
public sealed class PdfRedactionResult
{
    internal PdfRedactionResult()
    {
    }

    /// <summary>Gets the number of glyphs removed from the text of the page and of its forms, the invisible ones included.</summary>
    public int RemovedGlyphCount { get; internal set; }

    /// <summary>Gets the number of subpaths removed: those that lie inside an area.</summary>
    public int RemovedPathCount { get; internal set; }

    /// <summary>Gets the number of painted paths that cross an area: they are kept, clipped so that nothing of them is painted in the areas.</summary>
    public int ClippedPathCount { get; internal set; }

    /// <summary>Gets the number of images whose pixels in the areas are erased: each one is replaced by a copy without them, or removed when no pixel is left.</summary>
    public int RedactedImageCount { get; internal set; }

    /// <summary>Gets the number of annotations removed.</summary>
    public int RemovedAnnotationCount { get; internal set; }

    /// <summary>Gets a value indicating whether anything was removed from the page.</summary>
    public bool HasChanges => RemovedGlyphCount > 0 || RemovedPathCount > 0 || ClippedPathCount > 0 || RedactedImageCount > 0 || RemovedAnnotationCount > 0;
}
