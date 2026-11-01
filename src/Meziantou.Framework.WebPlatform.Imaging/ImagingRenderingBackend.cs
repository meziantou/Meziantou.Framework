using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// A fully managed <see cref="RenderingBackend"/>: set it as the <see cref="BrowsingContextOptions.RenderingBackend"/> of a
/// browsing context to rasterize its documents (see <see cref="ImagingScreenshotExtensions"/> to take screenshots), to draw its
/// canvases and to decode its images. The display lists are drawn, and the images are decoded and encoded, by
/// <see href="https://www.nuget.org/packages/Meziantou.Framework.Imaging">Meziantou.Framework.Imaging</see> (no native library).
/// </summary>
/// <remarks>
/// <para>
/// The backend only rasterizes: the layout, the font matching and the text shaping are done by the library, and the glyphs
/// are drawn by id at the positions the layout computed, from the same font files. No font of the system is used, and the same
/// document gives the same pixels on every platform.
/// </para>
/// <para>
/// A backend can be shared by several browsing contexts and threads. It caches the fonts and the coverages of the glyphs it draws.
/// </para>
/// </remarks>
public sealed class ImagingRenderingBackend : RenderingBackend
{
    /// <summary>Creates a backend with the default options.</summary>
    public ImagingRenderingBackend()
        : this(options: null)
    {
    }

    /// <summary>Creates a backend.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public ImagingRenderingBackend(ImagingRenderingBackendOptions? options)
    {
        Options = options ?? new ImagingRenderingBackendOptions();
    }

    /// <summary>Gets the options of the backend.</summary>
    public ImagingRenderingBackendOptions Options { get; }

    /// <summary>The fonts of the display lists.</summary>
    internal FontCache Fonts { get; } = new();

    /// <summary>The coverages of the glyphs drawn by the backend (shared by its threads: the cache is thread-safe).</summary>
    internal Meziantou.Framework.Imaging.Typography.GlyphCache GlyphCache { get; } = new(MaxGlyphCacheBytes);

    /// <summary>The most bytes of the coverages of the glyphs kept by a backend.</summary>
    private const long MaxGlyphCacheBytes = 32L * 1024 * 1024;

    /// <inheritdoc/>
    internal override RenderingSurface? CreateSurface(int width, int height, RenderingSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ImagingRenderingSurface.Create(this, width, height, options);
    }

    /// <inheritdoc/>
    internal override RenderingImageDecoder? CreateImageDecoder(ReadOnlyMemory<byte> data, string? mimeType) => ImagingImageDecoder.Create(data);

    /// <inheritdoc/>
    internal override RenderingImage? CreateImage(int width, int height, ReadOnlySpan<byte> pixels, ImageDataPixelFormat pixelFormat = ImageDataPixelFormat.RgbaUnorm8)
    {
        var bytesPerPixel = pixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 8 : 4;
        if (width < 1 || height < 1 || pixels.Length < (long)width * height * bytesPerPixel)
            return null;

        if (pixelFormat == ImageDataPixelFormat.RgbaFloat16)
            return new ImagingRenderingImage(Meziantou.Framework.Imaging.Image.ImportPixelBytes<Meziantou.Framework.Imaging.RgbaHalf>(pixels[..(width * height * 8)], width, height, width * 8), RenderingColorSpace.Srgb);

        return new ImagingRenderingImage(width, height, pixels[..(width * height * 4)]);
    }

    /// <inheritdoc/>
    internal override byte[]? EncodeImage(int width, int height, ReadOnlySpan<byte> pixels, string mimeType, double? quality)
    {
        if (width < 1 || height < 1 || pixels.Length < (long)width * height * 4)
            return null;

        return ImagingImageEncoder.Encode(width, height, pixels[..(width * height * 4)], mimeType, quality);
    }

    /// <summary>Draws a display list over the pixels of a frame (the coordinates of the display list are the pixels of the frame).</summary>
    internal void Rasterize(Meziantou.Framework.Imaging.ImageFrame target, DisplayList displayList, RenderingColorSpace colorSpace)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(displayList);
        DisplayListRasterizer.Rasterize(this, target, displayList, colorSpace);
    }

    /// <summary>Returns the pixels of an image of a display list (decoded the first time), or null when it cannot be decoded.</summary>
    /// <remarks>The images are decoded and cached by the <see cref="PaintImage"/> for this backend, so they are always images of this backend.</remarks>
    internal ImagingRenderingImage? GetImage(PaintImage image) => image.GetDecodedImage(this) as ImagingRenderingImage;
}
