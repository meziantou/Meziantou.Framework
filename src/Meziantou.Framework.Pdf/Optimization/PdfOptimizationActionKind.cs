namespace Meziantou.Framework.Pdf;

/// <summary>The kinds of changes of a <see cref="PdfOptimizationPlan"/>.</summary>
public enum PdfOptimizationActionKind
{
    /// <summary>Removes the document information dictionary (<c>/Info</c> of the trailer, ISO 32000-2, 14.3.3).</summary>
    RemoveDocumentInformation,

    /// <summary>Removes the XMP metadata stream of the document (<c>/Metadata</c> of the catalog, 14.3.2).</summary>
    RemoveXmpMetadata,

    /// <summary>Removes JavaScript: the <c>/JavaScript</c> name tree, and the actions that run JavaScript (12.6.4.17).</summary>
    RemoveJavaScript,

    /// <summary>Removes the embedded files of the document (the <c>/EmbeddedFiles</c> name tree and the associated files, 7.11.4).</summary>
    RemoveEmbeddedFiles,

    /// <summary>Removes a file attachment annotation (12.5.6.15).</summary>
    RemoveFileAttachmentAnnotation,

    /// <summary>Removes the thumbnail image of a page (<c>/Thumb</c>, 12.3.4).</summary>
    RemovePageThumbnail,

    /// <summary>Removes a markup annotation (a comment, 12.5.6.2) or a pop-up annotation.</summary>
    RemoveComment,

    /// <summary>Removes the action or destination run when the document is opened (<c>/OpenAction</c> of the catalog, 7.7.2).</summary>
    RemoveOpenAction,

    /// <summary>Removes the XFA form (<c>/XFA</c> of the interactive form dictionary, 12.7.9).</summary>
    RemoveXfa,

    /// <summary>Removes the interactive form and the widget annotations of its fields (12.7).</summary>
    RemoveFormFields,

    /// <summary>Removes the outline (bookmarks, 12.3.3).</summary>
    RemoveBookmarks,

    /// <summary>
    /// Makes the optional content of a page permanent (8.11): the content of the groups hidden in the default
    /// configuration is removed, and the content of the visible groups is kept as ordinary content.
    /// </summary>
    FlattenOptionalContent,

    /// <summary>Removes the optional content properties of the document (<c>/OCProperties</c> of the catalog), once its content is flattened.</summary>
    RemoveOptionalContentProperties,

    /// <summary>Removes a resource that no content of the pages uses (7.8.3).</summary>
    PruneUnusedResource,

    /// <summary>Removes an indirect object that cannot be reached from the trailer.</summary>
    PruneUnreachableObject,

    /// <summary>Compresses a stream that has no filter with <c>FlateDecode</c> (7.4.4).</summary>
    CompressStream,

    /// <summary>Resamples an image whose resolution on the pages is above the target resolution.</summary>
    DownsampleImage,
}
