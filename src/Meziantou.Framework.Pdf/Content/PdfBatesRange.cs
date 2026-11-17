namespace Meziantou.Framework.Pdf;

/// <summary>The Bates numbers <see cref="PdfBates.Apply"/> stamped on a document.</summary>
public sealed class PdfBatesRange
{
    internal PdfBatesRange(PdfDocument document, long firstNumber, long lastNumber, string first, string last)
    {
        Document = document;
        FirstNumber = firstNumber;
        LastNumber = lastNumber;
        First = first;
        Last = last;
    }

    /// <summary>Gets the document.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the number of the first page.</summary>
    public long FirstNumber { get; }

    /// <summary>Gets the number of the last page: <see cref="FirstNumber"/> minus 1 when the document has no pages.</summary>
    public long LastNumber { get; }

    /// <summary>Gets the Bates number of the first page, such as <c>ABC000001</c>.</summary>
    public string First { get; }

    /// <summary>Gets the Bates number of the last page.</summary>
    public string Last { get; }

    /// <inheritdoc/>
    public override string ToString() => First + " - " + Last;
}
