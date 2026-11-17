namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ImageProcessingExtensions.SeamCarve(Image, int, int, SeamCarvingOptions?, CancellationToken)"/>.</summary>
/// <remarks>
/// The masks are read when the operation starts; changing them afterwards changes later operations only. Each one must
/// have the size of the image.
/// </remarks>
public sealed class SeamCarvingOptions
{
    /// <summary>Gets the default options: no protected region and no region to remove.</summary>
    public static SeamCarvingOptions Default { get; } = new();

    /// <summary>
    /// Gets the pixels the seams avoid, such as faces or a logo: a seam crosses them only when every seam does. Defaults to
    /// <see langword="null"/>.
    /// </summary>
    public PixelMask? ProtectedRegion { get; init; }

    /// <summary>
    /// Gets the pixels the seams go through first, such as an object to remove: while one is left, the seams of a reduction
    /// cross as many of them as they can. Defaults to <see langword="null"/>.
    /// </summary>
    public PixelMask? RemovedRegion { get; init; }
}
