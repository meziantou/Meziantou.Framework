using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A polyline annotation (ISO 32000-2, 12.5.6.9): connected straight lines, with endings such as arrows at the first and last vertices.</summary>
public sealed class PdfPolyLineAnnotation : PdfMarkupAnnotation
{
    internal PdfPolyLineAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
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

    /// <summary>Gets or sets the ending at the first vertex (<c>/LE</c>).</summary>
    public PdfLineEnding StartEnding
    {
        get => PdfLineEndingNames.Read(Dictionary).Start;
        set => PdfLineEndingNames.Write(Dictionary, value, EndEnding);
    }

    /// <summary>Gets or sets the ending at the last vertex (<c>/LE</c>).</summary>
    public PdfLineEnding EndEnding
    {
        get => PdfLineEndingNames.Read(Dictionary).End;
        set => PdfLineEndingNames.Write(Dictionary, StartEnding, value);
    }

    /// <summary>Gets or sets the color that fills the closed endings (<c>/IC</c>), or <see langword="null"/>.</summary>
    public PdfColor? InteriorColor
    {
        get => ReadColor(AnnotationNames.IC);
        set => WriteColor(AnnotationNames.IC, value);
    }
}
