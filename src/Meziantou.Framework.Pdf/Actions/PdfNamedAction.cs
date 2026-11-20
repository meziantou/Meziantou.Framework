using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A named action (<c>Named</c>, ISO 32000-2, 12.6.4.12): a command of the viewer, such as going to the next page.</summary>
public sealed class PdfNamedAction : PdfAction
{
    internal PdfNamedAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the name of the command (<c>/N</c>): <c>NextPage</c>, <c>PrevPage</c>, <c>FirstPage</c>, <c>LastPage</c>, or a name a viewer knows.</summary>
    public string Name => Dictionary.GetName(PdfNames.N)?.Value ?? "";
}
