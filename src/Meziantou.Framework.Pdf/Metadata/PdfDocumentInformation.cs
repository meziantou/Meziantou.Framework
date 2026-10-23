using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The document information dictionary (ISO 32000-2, 14.3.3), the <c>/Info</c> entry of the trailer. Setting a value
/// creates the dictionary; setting <see langword="null"/> removes the entry.
/// </summary>
/// <remarks>PDF 2.0 deprecates every entry but the dates in favor of the XMP metadata: <see cref="PdfDocument.UpdateXmpMetadata"/> writes these values to it.</remarks>
public sealed class PdfDocumentInformation
{
    private readonly PdfDocument _document;

    internal PdfDocumentInformation(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>Gets the information dictionary, or <see langword="null"/> when the document has none.</summary>
    public PdfDictionary? Dictionary => _document.Trailer.Get<PdfDictionary>(PdfNames.Info);

    /// <summary>Gets or sets the title of the document.</summary>
    public string? Title
    {
        get => GetText(PdfNames.Title);
        set => SetText(PdfNames.Title, value);
    }

    /// <summary>Gets or sets the name of the person who created the document.</summary>
    public string? Author
    {
        get => GetText(PdfNames.Author);
        set => SetText(PdfNames.Author, value);
    }

    /// <summary>Gets or sets the subject of the document.</summary>
    public string? Subject
    {
        get => GetText(PdfNames.Subject);
        set => SetText(PdfNames.Subject, value);
    }

    /// <summary>Gets or sets the keywords of the document.</summary>
    public string? Keywords
    {
        get => GetText(PdfNames.Keywords);
        set => SetText(PdfNames.Keywords, value);
    }

    /// <summary>Gets or sets the name of the application that created the original document.</summary>
    public string? Creator
    {
        get => GetText(PdfNames.Creator);
        set => SetText(PdfNames.Creator, value);
    }

    /// <summary>Gets or sets the name of the application that converted the document to PDF.</summary>
    public string? Producer
    {
        get => GetText(PdfNames.Producer);
        set => SetText(PdfNames.Producer, value);
    }

    /// <summary>Gets or sets the date the document was created, or <see langword="null"/> when it is missing or not a valid date.</summary>
    public DateTimeOffset? CreationDate
    {
        get => GetDate(PdfNames.CreationDate);
        set => SetDate(PdfNames.CreationDate, value);
    }

    /// <summary>Gets or sets the date the document was last modified, or <see langword="null"/> when it is missing or not a valid date.</summary>
    public DateTimeOffset? ModificationDate
    {
        get => GetDate(PdfNames.ModDate);
        set => SetDate(PdfNames.ModDate, value);
    }

    /// <summary>Gets the value of an entry as a text, such as a custom entry.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public string? GetText(string key) => GetText(PdfName.Create(key));

    /// <summary>Sets the value of an entry to a text, or removes it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The text, or <see langword="null"/> to remove the entry.</param>
    public void SetText(string key, string? value) => SetText(PdfName.Create(key), value);

    private string? GetText(PdfName key) => Dictionary?.GetText(key);

    private void SetText(PdfName key, string? value)
    {
        if (value is null)
        {
            Dictionary?.Remove(key);
            return;
        }

        GetOrCreateDictionary()[key] = PdfString.FromText(value);
    }

    private DateTimeOffset? GetDate(PdfName key) => PdfDate.TryParse(GetText(key), out var date) ? date : null;

    private void SetDate(PdfName key, DateTimeOffset? value)
    {
        if (value is null)
        {
            Dictionary?.Remove(key);
            return;
        }

        GetOrCreateDictionary()[key] = PdfString.FromText(PdfDate.Format(value.Value));
    }

    private PdfDictionary GetOrCreateDictionary()
    {
        if (Dictionary is { } existing)
            return existing;

        var dictionary = new PdfDictionary();
        _document.Trailer[PdfNames.Info] = _document.Objects.Add(dictionary);
        return dictionary;
    }
}
