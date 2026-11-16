namespace Meziantou.Framework.Pdf;

/// <summary>Stamps continuous Bates numbers on the pages of several documents, such as the documents of a legal production.</summary>
public static class PdfBates
{
    /// <summary>
    /// Stamps the headers and footers of the options on the pages of the documents, in order: the numbers of a document
    /// follow those of the previous one, from <see cref="PdfBatesNumbering.StartNumber"/>. Only the pages stamped
    /// (<see cref="PdfHeaderFooterOptions.FirstPageIndex"/> to <see cref="PdfHeaderFooterOptions.LastPageIndex"/>) are
    /// numbered.
    /// </summary>
    /// <param name="documents">The documents, in the order of the production.</param>
    /// <param name="options">The headers and footers, whose texts use the <c>{bates}</c> token, and the Bates numbering (<see cref="PdfHeaderFooterOptions.Bates"/>).</param>
    /// <returns>The range of numbers of each document. A document with no page stamped has an empty range: its last number is the one before its first.</returns>
    /// <exception cref="ArgumentException"><see cref="PdfHeaderFooterOptions.Bates"/> is not set, or a text is not valid (see <see cref="PdfDocument.AddHeaderFooter"/>).</exception>
    public static IReadOnlyList<PdfBatesRange> Apply(IEnumerable<PdfDocument> documents, PdfHeaderFooterOptions options)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(options);
        var bates = options.Bates ?? throw new ArgumentException("The options have no Bates numbering", nameof(options));

        var ranges = new List<PdfBatesRange>();
        var next = bates.StartNumber;
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document, nameof(documents));
            var numbering = new PdfBatesNumbering { Prefix = bates.Prefix, Suffix = bates.Suffix, Digits = bates.Digits, StartNumber = next };
            var documentOptions = options.WithBates(numbering);
            document.AddHeaderFooter(documentOptions);
            // The pages stamped: those between FirstPageIndex and LastPageIndex
            var pageCount = document.Pages.Count;
            var lastIndex = Math.Min(options.LastPageIndex ?? pageCount - 1, pageCount - 1);
            var count = Math.Max(0, lastIndex - options.FirstPageIndex + 1);
            var last = next + count - 1;
            ranges.Add(new PdfBatesRange(document, next, last, numbering.Format(next), numbering.Format(last)));
            next += count;
        }

        return ranges;
    }
}
