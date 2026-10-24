namespace Meziantou.Framework.Pdf;

/// <summary>The attributes of a file embedded in a document (<see cref="PdfEmbeddedFileCollection.Add"/>, <see cref="PdfAnnotationCollection.AddFileAttachment(PdfRectangle, string, ReadOnlySpan{byte}, PdfEmbeddedFileOptions?, PdfFileAttachmentIcon)"/>).</summary>
public sealed class PdfEmbeddedFileOptions
{
    /// <summary>Gets the description of the file (<c>/Desc</c>), which PDF/UA-2 requires for the files of the document.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the MIME type of the file (<c>/Subtype</c> of the embedded file stream), such as <c>text/csv</c>. The
    /// default, <see langword="null"/>, writes <c>application/octet-stream</c>: PDF/A-3 and PDF/A-4 need a MIME type.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>Gets how the file relates to the content of the document (<c>/AFRelationship</c>). The default is <see cref="PdfAssociatedFileRelationship.Unspecified"/>.</summary>
    public PdfAssociatedFileRelationship Relationship { get; init; }

    /// <summary>Gets the date the file was created (<c>/Params /CreationDate</c>), or <see langword="null"/> to leave it out.</summary>
    public DateTimeOffset? CreationDate { get; init; }

    /// <summary>Gets the date the file was last modified (<c>/Params /ModDate</c>), or <see langword="null"/> to leave it out.</summary>
    /// <remarks>The library never writes a date it is not given: the output of a save depends only on the document.</remarks>
    public DateTimeOffset? ModificationDate { get; init; }
}
