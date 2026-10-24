using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An annotation of a page (ISO 32000-2, 12.5): a link, a note, a form field widget, ...</summary>
/// <remarks>The class cannot be derived outside the library: <see cref="PdfLinkAnnotation"/> represents the links, <see cref="PdfFileAttachmentAnnotation"/> the file attachments.</remarks>
public class PdfAnnotation
{
    private protected PdfAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
    {
        Page = page;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the page of the annotation.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the annotation dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the reference to the annotation, or <see langword="null"/> for a direct object (which the specification does not allow, but some files have).</summary>
    public PdfReference? Reference { get; }

    /// <summary>Gets the type of the annotation, such as <c>Link</c>, <c>Text</c> or <c>Widget</c>.</summary>
    public string Subtype => Dictionary.GetName(PdfNames.Subtype)?.Value ?? "";

    /// <summary>Gets or sets the location of the annotation on the page, in default user space.</summary>
    public PdfRectangle Rectangle
    {
        get => PdfRectangle.TryRead(Dictionary.Get<PdfArray>(PdfNames.Rect), out var rectangle) ? rectangle : default;
        set => Dictionary[PdfNames.Rect] = value.ToArray();
    }

    /// <summary>Gets or sets the text of the annotation, or an alternate description of its content.</summary>
    public string? Contents
    {
        get => Dictionary.GetText(PdfNames.Contents);
        set => Dictionary.SetText(PdfNames.Contents, value);
    }

    internal static PdfAnnotation Create(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
    {
        if (dictionary.GetName(PdfNames.Subtype) == PdfNames.Link)
            return new PdfLinkAnnotation(page, dictionary, reference);

        if (dictionary.GetName(PdfNames.Subtype) == PdfFileAttachmentAnnotation.FileAttachmentName)
            return new PdfFileAttachmentAnnotation(page, dictionary, reference);

        return new PdfAnnotation(page, dictionary, reference);
    }
}
