namespace Meziantou.Framework.Imaging;

/// <summary>What the entries of an <see cref="ImageCollection"/> are.</summary>
/// <remarks>
/// Neither kind is an animation. Entries never acquire a duration, a blend or disposal operation, or a loop count, and an
/// <see cref="ImageCollection"/> is never exposed as the frames of an <see cref="Image"/>: a consumer that plays frames
/// cannot accidentally receive the pages of a document or the sizes of an icon.
/// </remarks>
public enum ImageCollectionKind
{
    /// <summary>The kind is unknown (an empty collection that was not created for a specific container).</summary>
    Unknown = 0,

    /// <summary>
    /// The entries are the pages of a document, in document order (TIFF and BigTIFF). Pages may differ in size, pixel
    /// format and metadata.
    /// </summary>
    Pages = 1,

    /// <summary>
    /// The entries are alternative representations of the same drawing at different sizes and color depths (ICO and CUR).
    /// Their order carries no meaning; <see cref="ImageCollection.SelectBySize"/> picks one.
    /// </summary>
    Representations = 2,
}
