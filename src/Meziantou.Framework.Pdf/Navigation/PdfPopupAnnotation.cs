using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A pop-up annotation (ISO 32000-2, 12.5.6.14): the window that shows the text of a markup annotation, its parent.</summary>
public sealed class PdfPopupAnnotation : PdfAnnotation
{
    internal PdfPopupAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets the annotation whose text the pop-up window shows (<c>/Parent</c>), or <see langword="null"/>.</summary>
    public PdfAnnotation? Parent => GetRelated(PdfNames.Parent);

    /// <summary>Gets or sets a value indicating whether the pop-up window is initially open (<c>/Open</c>).</summary>
    public bool IsOpen
    {
        get => Dictionary.GetBoolean(AnnotationNames.Open);
        set => Dictionary[AnnotationNames.Open] = value ? PdfBoolean.True : null;
    }
}
