using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that opens a file with the application of the system (<c>Launch</c>, ISO 32000-2, 12.6.4.6).</summary>
public sealed class PdfLaunchAction : PdfAction
{
    internal PdfLaunchAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the path of the file (<c>/F</c>, or the <c>/F</c> of the Windows parameters <c>/Win</c>).</summary>
    public string? FileName => ReadFileName(Dictionary[PdfNames.F]) ?? Dictionary.Get<PdfDictionary>(PdfName.Create("Win"))?.GetText(PdfNames.F);

    /// <summary>Gets whether a PDF file opens in a new window (<c>/NewWindow</c>), or <see langword="null"/> when the viewer chooses.</summary>
    public bool? NewWindow => Dictionary.Get<PdfBoolean>(PdfName.Create("NewWindow"))?.Value;
}
