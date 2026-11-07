using Meziantou.Framework.Imaging;
using JpegChromaSubsampling = Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling;
using JpegEncoder = Meziantou.Framework.Imaging.Formats.JpegEncoder;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// Takes screenshots of the documents of a browsing context: the viewport of a browsing context or of a document, or the area
/// of an element (its border box, as <c>getBoundingClientRect()</c>), at the device pixel ratio of the viewport.
/// </summary>
/// <remarks>
/// <para>
/// A screenshot paints the current state of the document: the layout is computed when it is out of date (as by the geometry
/// APIs), so call it from the thread of the <see cref="DomEventLoop"/> of the browsing context, after the resources you expect
/// are loaded (e.g. after <see cref="DomEventLoop.RunUntilIdleAsync"/>). The pixels are in sRGB, not premultiplied by alpha:
/// a top-level document is painted over the color of its canvas (white, or a dark color for a dark color scheme) unless
/// <see cref="ScreenshotOptions.OmitBackground"/> is set, and the parts that nothing paints (e.g. a nested document, which has
/// no canvas color) are transparent.
/// </para>
/// <para>
/// The <see cref="ImagingRenderingBackend"/> of the browsing context (<see cref="BrowsingContextOptions.RenderingBackend"/>)
/// rasterizes the screenshot. Without one, a temporary backend with the default options is used, but the images are not
/// drawn: their encoded bytes are only kept by the documents of a browsing context that has a rendering backend.
/// </para>
/// </remarks>
public static class ImagingScreenshotExtensions
{
    /// <summary>Renders the viewport of the active document of a browsing context.</summary>
    /// <param name="browsingContext">The browsing context.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>An image of the size of the viewport in device pixels. The caller owns it.</returns>
    /// <exception cref="InvalidOperationException">The document is not rendered.</exception>
    public static Image<Rgba32> TakeScreenshot(this BrowsingContext browsingContext, ScreenshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browsingContext);
        return TakeScreenshot(browsingContext.Document, options);
    }

    /// <summary>Renders the viewport of a document (the area of its <c>iframe</c> for the document of a nested browsing context).</summary>
    /// <param name="document">The document, which must be the active document of a browsing context.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>An image of the size of the viewport in device pixels. The caller owns it.</returns>
    /// <exception cref="InvalidOperationException">The document is not rendered (it has no browsing context).</exception>
    public static Image<Rgba32> TakeScreenshot(this Document document, ScreenshotOptions? options = null)
    {
        var (pixels, width, height) = RenderViewport(document, options);
        return ToImage(pixels, width, height);
    }

    /// <summary>
    /// Renders the area of an element: its border box in the viewport (the rectangle of <c>getBoundingClientRect()</c>, which
    /// includes its transforms), with what is painted over it. The parts of the element outside of the viewport are painted too.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>An image of the size of the border box of the element in device pixels (rounded outwards). The caller owns it.</returns>
    /// <exception cref="InvalidOperationException">The document of the element is not rendered, or the element has no box or an empty box.</exception>
    public static Image<Rgba32> TakeScreenshot(this Element element, ScreenshotOptions? options = null)
    {
        var (pixels, width, height) = RenderElement(element, options);
        return ToImage(pixels, width, height);
    }

    /// <summary>Renders the viewport of the active document of a browsing context, encoded as PNG.</summary>
    /// <inheritdoc cref="TakeScreenshot(BrowsingContext, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="TakeScreenshot(BrowsingContext, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the PNG image.</returns>
    public static byte[] TakeScreenshotAsPng(this BrowsingContext browsingContext, ScreenshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browsingContext);
        return TakeScreenshotAsPng(browsingContext.Document, options);
    }

    /// <summary>Renders the viewport of a document, encoded as PNG.</summary>
    /// <inheritdoc cref="TakeScreenshot(Document, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="TakeScreenshot(Document, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the PNG image.</returns>
    public static byte[] TakeScreenshotAsPng(this Document document, ScreenshotOptions? options = null)
    {
        var (pixels, width, height) = RenderViewport(document, options);
        return PngEncoder.Encode(width, height, pixels);
    }

    /// <summary>Renders the area of an element (see <see cref="TakeScreenshot(Element, ScreenshotOptions?)"/>), encoded as PNG.</summary>
    /// <inheritdoc cref="TakeScreenshot(Element, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="TakeScreenshot(Element, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the PNG image.</returns>
    public static byte[] TakeScreenshotAsPng(this Element element, ScreenshotOptions? options = null)
    {
        var (pixels, width, height) = RenderElement(element, options);
        return PngEncoder.Encode(width, height, pixels);
    }

    /// <summary>Renders the viewport of the active document of a browsing context, encoded as JPEG (composited over white).</summary>
    /// <inheritdoc cref="TakeScreenshot(BrowsingContext, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="TakeScreenshot(BrowsingContext, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the JPEG image, of the quality of <see cref="ScreenshotOptions.Quality"/>.</returns>
    public static byte[] TakeScreenshotAsJpeg(this BrowsingContext browsingContext, ScreenshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browsingContext);
        return TakeScreenshotAsJpeg(browsingContext.Document, options);
    }

    /// <summary>Renders the viewport of a document, encoded as JPEG (composited over white).</summary>
    /// <inheritdoc cref="TakeScreenshot(Document, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="TakeScreenshot(Document, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the JPEG image, of the quality of <see cref="ScreenshotOptions.Quality"/>.</returns>
    public static byte[] TakeScreenshotAsJpeg(this Document document, ScreenshotOptions? options = null)
    {
        var (pixels, width, height) = RenderViewport(document, WithoutTransparency(options));
        return EncodeJpeg(pixels, width, height, options);
    }

    /// <summary>Renders the area of an element (see <see cref="TakeScreenshot(Element, ScreenshotOptions?)"/>), encoded as JPEG (composited over white).</summary>
    /// <inheritdoc cref="TakeScreenshot(Element, ScreenshotOptions?)" path="/param"/>
    /// <inheritdoc cref="TakeScreenshot(Element, ScreenshotOptions?)" path="/exception"/>
    /// <returns>The bytes of the JPEG image, of the quality of <see cref="ScreenshotOptions.Quality"/>.</returns>
    public static byte[] TakeScreenshotAsJpeg(this Element element, ScreenshotOptions? options = null)
    {
        var (pixels, width, height) = RenderElement(element, WithoutTransparency(options));
        return EncodeJpeg(pixels, width, height, options);
    }

    /// <summary>The JPEG images have no alpha channel: the background of the canvas is not omitted.</summary>
    private static ScreenshotOptions? WithoutTransparency(ScreenshotOptions? options)
        => options is { OmitBackground: true } ? new ScreenshotOptions { DevicePixelRatio = options.DevicePixelRatio, FullPage = options.FullPage, Clip = options.Clip, Quality = options.Quality } : options;

    private static byte[] EncodeJpeg(byte[] pixels, int width, int height, ScreenshotOptions? options)
    {
        // The quality of the JPEG screenshots of Chromium (Page.captureScreenshot)
        const int DefaultQuality = 80;
        var quality = options?.Quality ?? DefaultQuality;
        if (quality is < 0 or > 100)
            throw new ArgumentException("The quality must be between 0 and 100.", nameof(options));

        // The parts that the document does not paint (e.g. a transparent iframe) are white. The chroma is subsampled (4:2:0),
        // as by Chromium.
        var encoder = new JpegEncoder
        {
            // The lowest quality of the encoder is 1
            Quality = Math.Max(1, quality),
            ChromaSubsampling = JpegChromaSubsampling.Ratio420,
            BackgroundColor = new Rgba64(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue),
        };

        using var image = ToImage(pixels, width, height);
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Renders the viewport of a document as pixels in the format of <c>ImageData</c> (red, green, blue, alpha; not premultiplied).</summary>
    /// <remarks>With a <paramref name="pool"/>, the array is rented from it (it can be longer than the pixels): the caller returns it.</remarks>
    internal static (byte[] Pixels, int Width, int Height) RenderViewport(Document document, ScreenshotOptions? options, System.Buffers.ArrayPool<byte>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var displayList = GetDisplayList(document, options?.OmitBackground ?? false, options?.FullPage ?? false);
        var area = new PhysicalRect(0, 0, displayList.Size.Width, displayList.Size.Height);

        // On a mobile device, the viewport of a top-level document is its visual viewport: the viewport of the device scaled by
        // the page scale (the layout viewport is wider when the page is scaled down), whole CSS pixels as in Chromium
        var pageScale = document.GetPageScale();
        if (pageScale != 1 && options?.FullPage != true && document.BrowsingContext?.Top.Options.Viewport is { IsMobile: true } viewport)
        {
            const double Epsilon = 1e-6;
            area = new PhysicalRect(0, 0, Math.Min(area.Width, Math.Floor((viewport.Width / pageScale) + Epsilon)), Math.Min(area.Height, Math.Floor((viewport.Height / pageScale) + Epsilon)));
        }

        // The size of the screenshot of a viewport is rounded to device pixels (e.g. a viewport of 839px at a device pixel ratio
        // of 2.625 is 2202 pixels high), as in Chromium; a clip is rounded outwards
        var roundSize = options?.Clip is null;

        if (options?.Clip is { } clip)
        {
            if (!double.IsFinite(clip.X) || !double.IsFinite(clip.Y) || !double.IsFinite(clip.Width) || !double.IsFinite(clip.Height) || clip.Width <= 0 || clip.Height <= 0)
                throw new ArgumentException("The clip must have a finite position and a positive size.", nameof(options));

            // As in Playwright, the clip is clipped to the viewport (or to the document)
            var left = Math.Max(area.Left, clip.X);
            var top = Math.Max(area.Top, clip.Y);
            var right = Math.Min(area.Right, clip.X + clip.Width);
            var bottom = Math.Min(area.Bottom, clip.Y + clip.Height);
            if (right <= left || bottom <= top)
                throw new ArgumentException("The clip is outside of the viewport (or of the document).", nameof(options));

            area = new PhysicalRect(left, top, right - left, bottom - top);
        }

        return Render(document, displayList, area, options, pool, pageScale, roundSize);
    }

    private static (byte[] Pixels, int Width, int Height) RenderElement(Element element, ScreenshotOptions? options)
    {
        ArgumentNullException.ThrowIfNull(element);
        var document = element.OwnerDocument ?? throw new InvalidOperationException("The element has no document.");
        var displayList = GetDisplayList(document, options?.OmitBackground ?? false, fullPage: false);
        var rect = element.GetBoundingClientRect();
        var area = new PhysicalRect(Math.Min(rect.Left, rect.Right), Math.Min(rect.Top, rect.Bottom), Math.Abs(rect.Width), Math.Abs(rect.Height));
        if (area.IsEmpty)
            throw new InvalidOperationException("The element is not rendered or its border box is empty.");

        return Render(document, displayList, area, options, pageScale: document.GetPageScale());
    }

    private static DisplayList GetDisplayList(Document document, bool transparentCanvas, bool fullPage)
    {
        if (document.BrowsingContext is null)
            throw new InvalidOperationException("The document is not rendered: it is not the active document of a browsing context.");

        return document.PaintScreenshot(transparentCanvas, fullPage) ?? throw new InvalidOperationException("The document is not rendered.");
    }

    /// <summary>
    /// Rasterizes an area of the viewport (in CSS px), rounded outwards to device pixels (or with a size rounded to device pixels
    /// with <paramref name="roundSize"/>): the device pixel ratio is multiplied by the page scale of a mobile device.
    /// </summary>
    internal static (byte[] Pixels, int Width, int Height) Render(Document document, DisplayList displayList, PhysicalRect area, ScreenshotOptions? options, System.Buffers.ArrayPool<byte>? pool = null, double pageScale = 1, bool roundSize = false)
    {
        var devicePixelRatio = (options?.DevicePixelRatio ?? StyleEngine.CreateEnvironment(document).DevicePixelRatio) * pageScale;
        if (!double.IsFinite(devicePixelRatio) || devicePixelRatio <= 0)
            throw new ArgumentException("The device pixel ratio must be a positive number.", nameof(options));

        // A small tolerance avoids an extra row or column of pixels for the rounding errors of the layout
        const double Epsilon = 1e-6;
        var left = Math.Floor((area.Left * devicePixelRatio) + Epsilon);
        var top = Math.Floor((area.Top * devicePixelRatio) + Epsilon);
        var width = (int)Math.Max(1, roundSize ? Math.Round(area.Width * devicePixelRatio, MidpointRounding.AwayFromZero) : Math.Ceiling((area.Right * devicePixelRatio) - Epsilon) - left);
        var height = (int)Math.Max(1, roundSize ? Math.Round(area.Height * devicePixelRatio, MidpointRounding.AwayFromZero) : Math.Ceiling((area.Bottom * devicePixelRatio) - Epsilon) - top);

        var backend = document.BrowsingContext?.Options.RenderingBackend as ImagingRenderingBackend ?? new ImagingRenderingBackend();
        var transform = DisplayMatrix.Translation(-left, -top).PreConcat(DisplayMatrix.Scale(devicePixelRatio, devicePixelRatio));
        var transformed = displayList.WithTransform(transform);

        // The page is drawn with 16-bit samples (the layers too): the colors are rounded to 8 bits once
        using var image = new Image<Rgba64>(width, height);
        backend.Rasterize(image.Frames[0], transformed, RenderingColorSpace.Srgb);
        var length = checked(width * height * 4);
        var pixels = pool is null ? new byte[length] : pool.Rent(length);
        image.Frames[0].ProcessPixelRows(pixels, static (accessor, pixels) =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                PixelConversions.ToBytes(accessor.GetRowSpan(y), pixels.AsSpan(y * accessor.Width * 4, accessor.Width * 4));
            }
        });

        return (pixels, width, height);
    }

    internal static Image<Rgba32> ToImage(byte[] pixels, int width, int height) => Image.ImportPixelBytes<Rgba32>(pixels, width, height, width * 4);
}
