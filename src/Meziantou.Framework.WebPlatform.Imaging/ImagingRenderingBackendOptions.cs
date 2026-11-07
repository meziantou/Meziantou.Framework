namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// Options of an <see cref="ImagingRenderingBackend"/>: the quality of the rasterization. The rasterizer is fully managed:
/// the same options give the same pixels on every platform, so that screenshots can be compared across machines.
/// </summary>
public sealed class ImagingRenderingBackendOptions
{
    /// <summary>
    /// Gets whether the edges of the shapes and of the glyphs are antialiased (unless the painting asks for aliased edges). The
    /// default is true: the coverage of a pixel is the exact area of the pixel inside of the shape.
    /// </summary>
    public bool AntiAlias { get; init; } = true;
}
