using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that goes to a destination of the document (<c>GoTo</c>, ISO 32000-2, 12.6.4.2).</summary>
public sealed class PdfGoToAction : PdfAction
{
    internal PdfGoToAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the destination: an explicit destination, a named destination of the document, or a structure destination (<c>/SD</c>); <see langword="null"/> when it cannot be resolved.</summary>
    public PdfDestination? Destination => Document is null ? null : PdfDestination.Read(Document, Dictionary);
}
