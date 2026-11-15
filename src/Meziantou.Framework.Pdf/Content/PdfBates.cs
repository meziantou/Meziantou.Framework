namespace Meziantou.Framework.Pdf;

/// <summary>Stamps continuous Bates numbers on the pages of several documents, such as the documents of a legal production.</summary>
public static class PdfBates
{
    /// <summary>
    /// Stamps the headers and footers of the options on every page of the documents, in order: the numbers of a document
    /// follow those of the previous one, from <see cref="PdfBatesNumbering.StartNumber"/>.
    /// </summary>
    /// <param name="documents">The documents, in the order of the production.</param>
    /// <param name="options">The headers and footers, whose texts use the <c>{bates}</c> token, and the Bates numbering (<see cref="PdfHeaderFooterOptions.Bates"/>).</param>
    /// <returns>The range of numbers of each document.</returns>
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
            var count = document.Pages.Count;
            var last = next + count - 1;
            ranges.Add(new PdfBatesRange(document, next, last, numbering.Format(next), numbering.Format(last)));
            next += count;
        }

        return ranges;
    }
}
