namespace Meziantou.Framework.Pdf;

/// <summary>The changes of a page between two documents (<see cref="PdfDocumentComparer"/>).</summary>
public sealed class PdfPageComparison
{
    internal PdfPageComparison(int? expectedPageIndex, int? actualPageIndex, PdfPageChangeKinds changes, IReadOnlyList<string> details)
    {
        ExpectedPageIndex = expectedPageIndex;
        ActualPageIndex = actualPageIndex;
        Changes = changes;
        Details = details;
    }

    /// <summary>Gets the index of the page in the first document, or <see langword="null"/> for a page added.</summary>
    public int? ExpectedPageIndex { get; }

    /// <summary>Gets the index of the page in the second document, or <see langword="null"/> for a page removed.</summary>
    public int? ActualPageIndex { get; }

    /// <summary>Gets the kinds of changes.</summary>
    public PdfPageChangeKinds Changes { get; }

    /// <summary>Gets the changes, one per line: <c>-</c> for an element of the first document, <c>+</c> for an element of the second one, <c>~</c> for an element that moved.</summary>
    public IReadOnlyList<string> Details { get; }

    /// <summary>Gets a value indicating whether the page changed.</summary>
    public bool HasChanges => Changes != PdfPageChangeKinds.None;
}
