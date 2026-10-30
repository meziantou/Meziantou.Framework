namespace Meziantou.Framework.Imaging;

/// <summary>Selects how the decoders treat files that deviate from their specification (<see cref="ImageConfiguration.DecodingStrictness"/>).</summary>
/// <remarks>
/// The mode applies to every operation that reads a file: identification, eager loads, sequential readers and collections.
/// It never changes the resource limits: a file read leniently is charged and bounded exactly like a file read strictly.
/// </remarks>
public enum DecodingStrictness
{
    /// <summary>
    /// A file that deviates from its specification throws an <see cref="InvalidImageContentException"/>. This is the
    /// default.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// The decoders accept a documented list of deviations that the reference decoders and the browsers accept, and repair
    /// them the way those decoders do (the list is in the format reference of the library). A deviation whose repair would
    /// be ambiguous, or would invent pixels, is still an <see cref="InvalidImageContentException"/>.
    /// </summary>
    Lenient = 1,
}
