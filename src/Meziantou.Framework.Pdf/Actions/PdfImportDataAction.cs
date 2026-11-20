using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that imports the values of the form fields from an FDF file (<c>ImportData</c>, ISO 32000-2, 12.7.6.4).</summary>
public sealed class PdfImportDataAction : PdfAction
{
    internal PdfImportDataAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the path of the FDF file (<c>/F</c>).</summary>
    public string? FileName => ReadFileName(Dictionary[PdfNames.F]);
}
