using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>Gets the portfolio of the document (<c>/Collection</c>, ISO 32000-2, 12.3.5), or <see langword="null"/> when the document is not a portfolio.</summary>
    public PdfPortfolio? Portfolio => Catalog.Get<PdfDictionary>(PdfEncryptedPayload.CollectionName) is { } collection ? new PdfPortfolio(collection) : null;

    /// <summary>Makes the document a portfolio: viewers show its embedded files (<see cref="EmbeddedFiles"/>) as a list. A portfolio the document already is, is returned as it is.</summary>
    /// <returns>The portfolio.</returns>
    public PdfPortfolio CreatePortfolio()
    {
        if (Portfolio is { } portfolio)
            return portfolio;

        var collection = new PdfDictionary { [PdfNames.Type] = PdfEncryptedPayload.CollectionName };
        Catalog[PdfEncryptedPayload.CollectionName] = collection;
        return new PdfPortfolio(collection);
    }
}
