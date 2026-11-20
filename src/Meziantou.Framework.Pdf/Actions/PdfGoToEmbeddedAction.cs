using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that goes to a destination of an embedded file (<c>GoToE</c>, ISO 32000-2, 12.6.4.4).</summary>
public sealed class PdfGoToEmbeddedAction : PdfAction
{
    internal PdfGoToEmbeddedAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the path of the file that contains the target (<c>/F</c>), or <see langword="null"/> when the target is in this document.</summary>
    public string? FileName => ReadFileName(Dictionary[PdfNames.F]);

    /// <summary>Gets the name of the embedded file of the target (<c>/T /N</c>), or <see langword="null"/>.</summary>
    public string? TargetFileName => Dictionary.Get<PdfDictionary>(PdfName.Create("T"))?.GetText(PdfNames.N);

    /// <summary>Gets whether the file opens in a new window (<c>/NewWindow</c>), or <see langword="null"/> when the viewer chooses.</summary>
    public bool? NewWindow => Dictionary.Get<PdfBoolean>(PdfName.Create("NewWindow"))?.Value;
}
