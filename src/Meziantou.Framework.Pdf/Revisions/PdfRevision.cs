using Meziantou.Framework.Pdf.Signing;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A revision of a document (ISO 32000-2, 7.5.6): the file as it was written by a full save or by one of the
/// incremental updates that follow it. A revision is a prefix of the file, so the document as it was then can be read
/// back.
/// </summary>
public sealed class PdfRevision
{
    private readonly PdfDocument _document;

    internal PdfRevision(PdfDocument document, int index, long length, bool isLatest)
    {
        _document = document;
        Index = index;
        Length = length;
        IsLatest = isLatest;
    }

    /// <summary>Gets the zero-based index of the revision: 0 for the original file, then one per incremental update.</summary>
    public int Index { get; }

    /// <summary>Gets the length of the revision in bytes: the prefix of the file it is made of.</summary>
    public long Length { get; }

    /// <summary>Gets a value indicating whether the revision is the last one of the file.</summary>
    public bool IsLatest { get; }

    /// <summary>Opens the document as it was in this revision.</summary>
    /// <param name="options">The read options, or <see langword="null"/> for those the document was opened with.</param>
    /// <returns>The document of the revision.</returns>
    /// <exception cref="InvalidPdfContentException">The revision cannot be read.</exception>
    /// <exception cref="PdfResourceLimitException">The revision is larger than 2 GB.</exception>
    public PdfDocument Open(PdfReadOptions? options = null)
    {
        var reader = _document.Reader!;
        if (Length > Array.MaxLength)
            throw new PdfResourceLimitException("A revision larger than 2 GB cannot be opened");

        var data = reader.Input is Parsing.MemoryPdfInput memory ? memory.Data[..(int)Length] : reader.Input.GetMemory(0, (int)Length);
        return PdfDocument.Open(data, options ?? reader.Options);
    }

    /// <summary>
    /// Lists the changes made after this revision: the objects, pages, annotations, form fields and signatures that
    /// differ between this revision and the document as it is now, the changes not saved yet included.
    /// </summary>
    /// <returns>The changes, by kind.</returns>
    /// <exception cref="InvalidPdfContentException">The revision cannot be read.</exception>
    public IReadOnlyList<PdfModification> GetModifications() => PdfModificationAnalyzer.Compare(Open(), _document);
}
