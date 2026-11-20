using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that goes to a destination of another PDF file (<c>GoToR</c>, ISO 32000-2, 12.6.4.3).</summary>
public sealed class PdfGoToRemoteAction : PdfAction
{
    internal PdfGoToRemoteAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the path of the file (<c>/F</c>).</summary>
    public string? FileName => ReadFileName(Dictionary[PdfNames.F]);

    /// <summary>Gets the zero-based index of the page of the destination, or <see langword="null"/> for a named destination.</summary>
    public int? PageIndex => Dictionary.GetResolved(PdfNames.D) is PdfArray { Count: > 0 } destination && destination.GetResolved(0) is PdfInteger page && page.Value is >= 0 and <= int.MaxValue ? (int)page.Value : null;

    /// <summary>Gets the name of the destination, or <see langword="null"/> for an explicit destination.</summary>
    public string? DestinationName => Dictionary.GetResolved(PdfNames.D) switch
    {
        PdfString name => name.ToText(),
        PdfName name => name.Value,
        _ => null,
    };

    /// <summary>Gets whether the file opens in a new window (<c>/NewWindow</c>), or <see langword="null"/> when the viewer chooses.</summary>
    public bool? NewWindow => Dictionary.Get<PdfBoolean>(PdfName.Create("NewWindow"))?.Value;
}
