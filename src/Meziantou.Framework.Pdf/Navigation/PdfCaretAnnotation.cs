using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A caret annotation (ISO 32000-2, 12.5.6.11): a symbol that marks where text is to be inserted.</summary>
public sealed class PdfCaretAnnotation : PdfMarkupAnnotation
{
    internal PdfCaretAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets or sets a value indicating whether the caret is drawn as a paragraph symbol (¶, <c>/Sy /P</c>) instead of a caret.</summary>
    public bool ShowsParagraphSymbol
    {
        get => Dictionary.GetName(AnnotationNames.Sy)?.Value == "P";
        set => Dictionary[AnnotationNames.Sy] = value ? PdfName.Create("P") : null;
    }
}
