namespace Meziantou.Framework.SnapshotTesting.Pdf;

/// <summary>
/// The optional parts of the JSON metadata file of a PDF snapshot. The PDF version, the number of pages, and the size and
/// rotation of every page are always written.
/// </summary>
[Flags]
public enum PdfSnapshotMetadata
{
    /// <summary>Only the PDF version, the number of pages, and the size and rotation of every page.</summary>
    None = 0,

    /// <summary>
    /// The document information: title, author, subject, keywords, creator, producer, language, PDF/A and PDF/UA
    /// conformance, and whether the document is encrypted.
    /// </summary>
    DocumentInformation = 1 << 0,

    /// <summary>
    /// The creation and modification dates of the document information. They usually change every time a document is
    /// generated, so they are not written by default.
    /// </summary>
    Dates = 1 << 1,

    /// <summary>The text of every page, one entry per line, as extracted by <see cref="Meziantou.Framework.Pdf.PdfPage.ExtractText()"/>.</summary>
    Text = 1 << 2,

    /// <summary>The outlines (bookmarks) of the document: the title of every item, nested as in the document.</summary>
    Outlines = 1 << 3,

    /// <summary>The annotations of every page: subtype, rectangle, contents, and the URI of links.</summary>
    Annotations = 1 << 4,

    /// <summary>
    /// What every page shows, one line per element (<see cref="Meziantou.Framework.Pdf.PdfPageElements"/>): the lines of
    /// text with their bounding boxes, the images with their size, digest and position, the paths with their paint,
    /// colors and bounding boxes, and the shadings. A change of the content shows as changed lines in the diff of the
    /// snapshot. It is verbose, so <see cref="All"/> does not include it.
    /// </summary>
    Content = 1 << 5,

    /// <summary>The default metadata: <see cref="DocumentInformation"/>.</summary>
    Default = DocumentInformation,

    /// <summary>All the metadata, except <see cref="Content"/>.</summary>
    All = DocumentInformation | Dates | Text | Outlines | Annotations,
}
