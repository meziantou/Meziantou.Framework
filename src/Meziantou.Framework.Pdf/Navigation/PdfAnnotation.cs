using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An annotation of a page (ISO 32000-2, 12.5): a link, a note, a form field widget, ...</summary>
/// <remarks>The class cannot be derived outside the library: <see cref="PdfLinkAnnotation"/> represents the links, <see cref="PdfFileAttachmentAnnotation"/> the file attachments, <see cref="PdfMarkupAnnotation"/> the other markup annotations (notes, text markups, stamps...).</remarks>
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

    /// <summary>Gets or sets the flags of the annotation (<c>/F</c>, 12.5.3): whether it is printed, hidden, locked... The bits the enumeration does not name are kept.</summary>
    public PdfAnnotationFlags Flags
    {
        get => (PdfAnnotationFlags)unchecked((int)Dictionary.GetInteger(PdfNames.F, 0));
        set => Dictionary[PdfNames.F] = value == PdfAnnotationFlags.None ? null : PdfInteger.Get((int)value);
    }

    /// <summary>
    /// Gets or sets the color of the annotation (<c>/C</c>, 12.5.2): the background of its icon, the title bar of its
    /// pop-up window, the border of a link, the color of a text markup; <see langword="null"/> when it is transparent or
    /// has none. Changing it does not change an appearance stream the annotation already has.
    /// </summary>
    public PdfColor? Color
    {
        get
        {
            var components = Dictionary.Get<PdfArray>(PdfNames.C);
            double Get(int index) => Math.Clamp(components!.GetNumber(index), 0, 1);
            return components?.Count switch
            {
                1 => PdfColor.FromGray(Get(0)),
                3 => PdfColor.FromRgb(Get(0), Get(1), Get(2)),
                4 => PdfColor.FromCmyk(Get(0), Get(1), Get(2), Get(3)),
                _ => null,
            };
        }

        set => Dictionary[PdfNames.C] = value is { } color ? PdfArray.FromNumbers(color.GetComponents()) : null;
    }

    internal static PdfAnnotation Create(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
    {
        if (dictionary.GetName(PdfNames.Subtype) == PdfNames.Link)
            return new PdfLinkAnnotation(page, dictionary, reference);

        if (dictionary.GetName(PdfNames.Subtype) == PdfFileAttachmentAnnotation.FileAttachmentName)
            return new PdfFileAttachmentAnnotation(page, dictionary, reference);

        if (dictionary.GetName(PdfNames.Subtype) is { } subtype && PdfMarkupAnnotation.Subtypes.Contains(subtype.Value))
            return new PdfMarkupAnnotation(page, dictionary, reference);

        return new PdfAnnotation(page, dictionary, reference);
    }
}
