namespace Meziantou.Framework.Pdf;

/// <summary>The changes <see cref="PdfOptimizer.CreatePlan(PdfDocument, PdfOptimizationOptions)"/> plans. Every change is off by default.</summary>
public sealed class PdfOptimizationOptions
{
    /// <summary>Gets a value indicating whether the document information dictionary and the XMP metadata of the document are removed.</summary>
    public bool RemoveMetadata { get; init; }

    /// <summary>Gets a value indicating whether JavaScript is removed: the scripts of the document and the actions that run scripts.</summary>
    public bool RemoveJavaScript { get; init; }

    /// <summary>Gets a value indicating whether the embedded files and the file attachment annotations are removed.</summary>
    public bool RemoveAttachments { get; init; }

    /// <summary>Gets a value indicating whether the thumbnail images of the pages are removed.</summary>
    public bool RemovePageThumbnails { get; init; }

    /// <summary>Gets a value indicating whether the comments are removed: the markup annotations (notes, highlights, stamps, drawings...) and their pop-ups.</summary>
    public bool RemoveComments { get; init; }

    /// <summary>Gets a value indicating whether the action run when the document is opened is removed.</summary>
    public bool RemoveOpenAction { get; init; }

    /// <summary>Gets a value indicating whether the XFA form is removed; the AcroForm fields are kept.</summary>
    public bool RemoveXfa { get; init; }

    /// <summary>Gets a value indicating whether the interactive form is removed, with the widgets of its fields. Use <see cref="PdfInteractiveForm.Flatten"/> to keep the values on the pages.</summary>
    public bool RemoveFormFields { get; init; }

    /// <summary>Gets a value indicating whether the outline (bookmarks) is removed.</summary>
    public bool RemoveBookmarks { get; init; }

    /// <summary>
    /// Gets a value indicating whether the optional content (layers) is flattened: the content hidden in the default
    /// configuration is removed, the visible content is kept as ordinary content, and the document no longer has layers.
    /// </summary>
    public bool FlattenOptionalContent { get; init; }

    /// <summary>Gets a value indicating whether the resources of the pages that their content does not use are removed.</summary>
    public bool PruneUnusedResources { get; init; }

    /// <summary>
    /// Gets a value indicating whether the indirect objects that cannot be reached from the trailer are removed. A full
    /// save never writes them: this matters for an incremental save, which writes them as free entries.
    /// </summary>
    public bool PruneUnreachableObjects { get; init; }

    /// <summary>
    /// Gets a value indicating whether the streams that have no filter are compressed with <c>FlateDecode</c>, when it makes
    /// them smaller. A full save with <see cref="PdfSaveOptions.CompressStreams"/> compresses them too: this matters for an
    /// incremental save, or a save that does not compress.
    /// </summary>
    public bool CompressStreams { get; init; }

    /// <summary>
    /// Gets the resolution, in pixels per inch, above which the images are resampled to it, or <see langword="null"/> (the
    /// default) to keep the images. The resolution of an image is the highest of its placements on the pages.
    /// </summary>
    /// <remarks>
    /// The images of 8 bits per component in gray, RGB or an ICC-based color space of 1 or 3 components, without color key
    /// mask or decode array, are resampled; the others are kept. A JPEG image is compressed as JPEG again, the others
    /// without loss.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double? MaxImageResolution
    {
        get;
        init
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the plan may change a document that has signatures. The default is
    /// <see langword="false"/>: <see cref="PdfOptimizer.Apply(PdfOptimizationPlan)"/> refuses to apply a plan that would
    /// invalidate them.
    /// </summary>
    public bool AllowSignatureInvalidation { get; init; }
}
