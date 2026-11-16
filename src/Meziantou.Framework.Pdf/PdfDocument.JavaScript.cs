using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    private static readonly PdfName NamesName = PdfName.Create("Names");
    private static readonly PdfName JavaScriptName = PdfName.Create("JavaScript");

    /// <summary>
    /// Gets the document-level scripts (the <c>/JavaScript</c> name tree of the name dictionary, ISO 32000-2, 7.7.4,
    /// 12.6.4.17), by name: the scripts a viewer runs when it opens the document, which usually declare the functions the
    /// scripts of the fields call. The library does not run them.
    /// </summary>
    public IReadOnlyDictionary<string, string> JavaScripts
    {
        get
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (Catalog.GetResolved(NamesName) is PdfDictionary names && names.GetResolved(JavaScriptName) is PdfDictionary tree)
                {
                    foreach (var (key, value) in PdfNameTree.GetEntries(tree))
                    {
                        if (PdfJavaScriptAction.Read(PdfReference.ResolveIfReference(value)) is { } action)
                        {
                            result.TryAdd(key.ToText(), action.Script);
                        }
                    }
                }
            }
            catch (PdfException)
            {
            }

            return result;
        }
    }
}
