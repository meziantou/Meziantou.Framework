using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>The result of <see cref="PdfDocumentComparer.Compare(PdfDocument, PdfDocument, PdfComparisonOptions?)"/>.</summary>
public sealed class PdfComparisonResult
{
    internal PdfComparisonResult(IReadOnlyList<PdfPageComparison> pages)
    {
        Pages = pages;
    }

    /// <summary>Gets the comparison of each page, in the order of the pages.</summary>
    public IReadOnlyList<PdfPageComparison> Pages { get; }

    /// <summary>Gets a value indicating whether the documents show the same elements.</summary>
    public bool AreEquivalent => Pages.All(page => !page.HasChanges);

    /// <summary>Writes the changes as a readable diff: a header per page that changed, then its details.</summary>
    /// <returns>The diff, or an empty string when nothing changed.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var page in Pages.Where(page => page.HasChanges))
        {
            var title = (page.ExpectedPageIndex, page.ActualPageIndex) switch
            {
                (null, { } actual) => $"Page {actual + 1}",
                ({ } expected, null) => $"Page {expected + 1}",
                ({ } expected, { } actual) when expected == actual => $"Page {expected + 1}",
                var (expected, actual) => $"Page {expected + 1} -> {actual + 1}",
            };
            builder.Append(title).Append(": ").Append(page.Changes.ToString()).Append('\n');
            foreach (var detail in page.Details)
            {
                builder.Append("  ").Append(detail).Append('\n');
            }
        }

        return builder.ToString();
    }
}
