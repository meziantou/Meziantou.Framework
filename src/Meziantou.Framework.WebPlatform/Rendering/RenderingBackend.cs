using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// A rasterizer plugged into the browsing contexts (<see cref="BrowsingContextOptions.RenderingBackend"/>): it turns the
/// painting of the documents into pixels, decodes the images and draws the bitmaps of the canvases. The library never draws
/// pixels by itself: it computes the layout, the fonts and the text shaping, and paints the documents into a display list
/// (the drawing operations of the boxes and of the texts, in the painting order of CSS), which the backend rasterizes. The
/// backend only rasterizes: the glyphs it draws come from the same font files as the ones the layout measured, at the
/// positions computed by the library.
/// </summary>
/// <remarks>
/// <para>
/// Without a backend (the default), nothing is painted: the geometry of the documents (the layout, the CSSOM View, the hit
/// testing) is computed as usual, but no pixel is produced, and the APIs that need pixels behave as in a user agent that
/// does not support them (e.g. <c>canvas.getContext('2d')</c> returns null).
/// </para>
/// <para>
/// The backends are the packages of this repository (<c>Meziantou.Framework.WebPlatform.Imaging</c>): the display list and
/// the services a backend implements are internal to the library and change with it, so a backend depends on the exact
/// version of the core library, and the type cannot be derived from outside of them.
/// </para>
/// <para>
/// A backend can be shared by several browsing contexts. Its services are called on the thread of the event loop of the
/// browsing context that uses them (the workers use it on their own threads), so an implementation must be thread-safe.
/// </para>
/// </remarks>
public abstract class RenderingBackend
{
    private protected RenderingBackend()
    {
    }

    /// <summary>
    /// Creates a surface of <paramref name="width"/> x <paramref name="height"/> device pixels, initially transparent black (opaque
    /// black without alpha channel), to rasterize display lists into: a screenshot, the bitmap of a canvas.
    /// </summary>
    /// <param name="width">The width of the surface in device pixels (at least 1).</param>
    /// <param name="height">The height of the surface in device pixels (at least 1).</param>
    /// <param name="options">The properties of the surface.</param>
    /// <returns>The surface, or null when it cannot be created (e.g. it is too large).</returns>
    internal abstract RenderingSurface? CreateSurface(int width, int height, RenderingSurfaceOptions options);

    /// <summary>
    /// Opens an encoded image for decoding (https://html.spec.whatwg.org/multipage/images.html#img-decoding-process): a PNG
    /// (including the animated PNG), JPEG, GIF, WebP, BMP or ICO image (and the other formats the backend supports). Only the
    /// header is read: the frames are decoded on demand by <see cref="RenderingImageDecoder.DecodeFrame"/>. SVG images are not
    /// decoded by the backend: they are rendered by the painting of their document.
    /// </summary>
    /// <param name="data">The bytes of the image resource (they must not change while the decoder is used).</param>
    /// <param name="mimeType">The MIME type essence of the resource (e.g. <c>image/png</c>), or null when it is unknown: the backend sniffs the format.</param>
    /// <returns>The decoder, or null when the data is not an image the backend can decode (an unsupported format, or a corrupted header).</returns>
    internal abstract RenderingImageDecoder? CreateImageDecoder(ReadOnlyMemory<byte> data, string? mimeType);

    /// <summary>
    /// Decodes the first frame of an image (see <see cref="CreateImageDecoder"/>), with its colors converted to sRGB and, when
    /// <paramref name="applyOrientation"/> is set, its EXIF orientation applied (<see cref="ImageMetadata.GetExifOrientation"/>).
    /// </summary>
    /// <param name="data">The bytes of the image resource.</param>
    /// <param name="mimeType">The MIME type essence of the resource, or null.</param>
    /// <param name="applyOrientation">Whether the EXIF orientation of the image is applied (<c>image-orientation: from-image</c>).</param>
    /// <returns>The decoded image, or null when the data is not an image the backend can decode.</returns>
    internal RenderingImage? DecodeImage(ReadOnlyMemory<byte> data, string? mimeType, bool applyOrientation)
    {
        using var decoder = CreateImageDecoder(data, mimeType);
        return decoder?.DecodeFrame(0, applyOrientation ? ImageMetadata.GetExifOrientation(data.Span) : ExifOrientation.TopLeft, convertToSrgb: true);
    }

    /// <summary>
    /// Creates an image from pixels in the format of <see cref="RenderingSurface.ReadPixels"/> (RGBA, not premultiplied, sRGB),
    /// e.g. the bitmap of an <c>ImageBitmap</c>.
    /// </summary>
    /// <returns>The image, or null when it cannot be created (e.g. it is too large).</returns>
    internal virtual RenderingImage? CreateImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        using var surface = CreateSurface(width, height, RenderingSurfaceOptions.Default);
        if (surface is null)
            return null;

        surface.WritePixels(0, 0, width, height, pixels);
        return surface.CreateSnapshot();
    }

    /// <summary>
    /// Encodes pixels in the format of <see cref="RenderingSurface.ReadPixels"/> as an image file of a type other than PNG
    /// (https://html.spec.whatwg.org/multipage/canvas.html#a-serialisation-of-the-bitmap-as-a-file): <c>image/jpeg</c>,
    /// <c>image/webp</c>... The library encodes the PNG images itself. The formats without alpha channel are composited over
    /// opaque black.
    /// </summary>
    /// <param name="width">The width in pixels (at least 1).</param>
    /// <param name="height">The height in pixels (at least 1).</param>
    /// <param name="pixels">The pixels (RGBA, not premultiplied, sRGB).</param>
    /// <param name="mimeType">The MIME type of the image file (lowercase).</param>
    /// <param name="quality">The quality from 0 to 1 of a lossy format, or null for the default quality of the backend.</param>
    /// <returns>The bytes of the file, or null when the backend does not support the type (the image is then encoded as PNG).</returns>
    internal virtual byte[]? EncodeImage(int width, int height, ReadOnlySpan<byte> pixels, string mimeType, double? quality) => null;
}

/// <summary>The properties of a <see cref="RenderingSurface"/>.</summary>
/// <param name="HasAlpha">
/// Whether the surface has an alpha channel (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-alpha): an
/// opaque surface is initially opaque black.
/// </param>
/// <param name="ColorSpace">The color space of the pixels (https://html.spec.whatwg.org/multipage/canvas.html#predefinedcolorspace).</param>
/// <param name="WillReadFrequently">
/// Whether the pixels will be read often: a hint to keep the surface in memory rather than on a GPU
/// (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-will-read-frequently).
/// </param>
internal sealed record RenderingSurfaceOptions(bool HasAlpha = true, RenderingColorSpace ColorSpace = RenderingColorSpace.Srgb, bool WillReadFrequently = false)
{
    /// <summary>
    /// Whether the surface is the bitmap of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-bitmap):
    /// its pixels have 8 bits per premultiplied component, as in the browsers (the pixels read with <c>getImageData()</c> and
    /// written back, or transferred as an <c>ImageBitmap</c>, are the pixels the canvas paints). The other surfaces (e.g. the
    /// snapshots of the view transitions) can keep the precision of the rasterizer.
    /// </summary>
    public bool IsCanvasBitmap { get; init; }

    /// <summary>The default options: a transparent sRGB surface.</summary>
    public static RenderingSurfaceOptions Default { get; } = new();
}

/// <summary>The color spaces of the surfaces and of the pixels (https://html.spec.whatwg.org/multipage/canvas.html#predefinedcolorspace).</summary>
internal enum RenderingColorSpace : byte
{
    /// <summary>sRGB.</summary>
    Srgb,

    /// <summary>Display P3.</summary>
    DisplayP3,
}

/// <summary>
/// A bitmap a <see cref="RenderingBackend"/> rasterizes display lists into: the pixels of a screenshot or of a canvas. The
/// surface keeps its pixels between the calls: each <see cref="Draw"/> starts with the identity transform, no clip and the
/// default compositing (the state of a drawing is in its display list).
/// </summary>
internal abstract class RenderingSurface : IDisposable
{
    /// <summary>The width of the surface in device pixels.</summary>
    public abstract int Width { get; }

    /// <summary>The height of the surface in device pixels.</summary>
    public abstract int Height { get; }

    /// <summary>
    /// Rasterizes a display list over the pixels of the surface. The coordinates of the display list are device pixels: the
    /// caller adds a scale transform for a device pixel ratio other than 1 (see <see cref="DisplayList.WithTransform"/>).
    /// </summary>
    public abstract void Draw(DisplayList displayList);

    /// <summary>Replaces all the pixels of the surface with a color (transparent black clears it).</summary>
    public abstract void Clear(DisplayColor color);

    /// <summary>
    /// Copies the pixels of a rectangle of the surface to <paramref name="destination"/>: 4 bytes per pixel in the order R, G,
    /// B, A, not premultiplied, in the color space of the surface, row by row (the format of <c>ImageData</c>). The pixels
    /// outside of the surface are transparent black.
    /// </summary>
    public abstract void ReadPixels(int x, int y, int width, int height, Span<byte> destination);

    /// <summary>Replaces the pixels of a rectangle of the surface with <paramref name="source"/> (the format of <see cref="ReadPixels"/>), without compositing.</summary>
    public abstract void WritePixels(int x, int y, int width, int height, ReadOnlySpan<byte> source);

    /// <summary>Returns an immutable copy of the pixels of the surface, e.g. to draw a canvas into another canvas or into a page.</summary>
    public abstract RenderingImage CreateSnapshot();

    /// <summary>Releases the pixels of the surface.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the surface.</summary>
    protected abstract void Dispose(bool disposing);
}

/// <summary>An immutable bitmap of a <see cref="RenderingBackend"/>: a decoded image, or a snapshot of a surface.</summary>
internal abstract class RenderingImage : IDisposable
{
    /// <summary>The width of the image in pixels.</summary>
    public abstract int Width { get; }

    /// <summary>The height of the image in pixels.</summary>
    public abstract int Height { get; }

    /// <summary>Copies the pixels of the image to <paramref name="destination"/>, in the format of <see cref="RenderingSurface.ReadPixels"/> (sRGB).</summary>
    public abstract void ReadPixels(Span<byte> destination);

    /// <summary>Releases the pixels of the image.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the image.</summary>
    protected abstract void Dispose(bool disposing);
}

/// <summary>
/// An encoded image opened by a <see cref="RenderingBackend"/> (<see cref="RenderingBackend.CreateImageDecoder"/>): its size
/// and its frames are known, the pixels of a frame are decoded on demand. A decoder is not thread-safe: its user serializes
/// the calls (see <see cref="PaintImage"/>).
/// </summary>
internal abstract class RenderingImageDecoder : IDisposable
{
    /// <summary>The width of the encoded image in pixels (before its EXIF orientation is applied).</summary>
    public abstract int Width { get; }

    /// <summary>The height of the encoded image in pixels (before its EXIF orientation is applied).</summary>
    public abstract int Height { get; }

    /// <summary>The number of frames of the image (1 for a still image, more for an animated GIF, PNG or WebP image).</summary>
    public abstract int FrameCount { get; }

    /// <summary>
    /// The number of times an animation is repeated after it is played once: -1 for an animation that loops forever, 0 for an
    /// animation that is played once (or a still image).
    /// </summary>
    public abstract int RepetitionCount { get; }

    /// <summary>Returns the duration of a frame of an animation, in milliseconds (as encoded: the user agents give a minimal duration to the very short frames).</summary>
    public abstract double GetFrameDuration(int index);

    /// <summary>
    /// Decodes a frame: the whole canvas of the image as displayed at that frame of the animation (the previous frames are
    /// composited with their disposal and blending operations), with its EXIF orientation applied.
    /// </summary>
    /// <param name="index">The index of the frame (0 for a still image).</param>
    /// <param name="orientation">The orientation applied to the pixels (<see cref="ExifOrientation.TopLeft"/> keeps them as encoded).</param>
    /// <param name="convertToSrgb">
    /// Whether the colors are converted from the color profile of the image to sRGB (the default); otherwise the encoded values
    /// are kept (<c>colorSpaceConversion: "none"</c> of <c>createImageBitmap()</c>).
    /// </param>
    /// <returns>The pixels of the frame, or null when the frame cannot be decoded (the data is corrupted). An incomplete frame is decoded up to its last complete row.</returns>
    public abstract RenderingImage? DecodeFrame(int index, ExifOrientation orientation, bool convertToSrgb);

    /// <summary>Releases the resources of the decoder.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the decoder.</summary>
    protected abstract void Dispose(bool disposing);
}
