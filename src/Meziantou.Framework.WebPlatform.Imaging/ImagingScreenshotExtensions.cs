using Meziantou.Framework.Imaging;
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
/// are loaded (e.g. after <see cref="DomEventLoop.RunUntilIdleAsync"/>). The pixels are in sRGB, not premultiplied by alpha
/// (the parts of the viewport that nothing paints are transparent).
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

    /// <summary>Renders the viewport of a document as pixels in the format of <c>ImageData</c> (red, green, blue, alpha; not premultiplied).</summary>
    /// <remarks>With a <paramref name="pool"/>, the array is rented from it (it can be longer than the pixels): the caller returns it.</remarks>
    internal static (byte[] Pixels, int Width, int Height) RenderViewport(Document document, ScreenshotOptions? options, System.Buffers.ArrayPool<byte>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var displayList = GetDisplayList(document);
        return Render(document, displayList, new PhysicalRect(0, 0, displayList.Size.Width, displayList.Size.Height), options, pool);
    }

    private static (byte[] Pixels, int Width, int Height) RenderElement(Element element, ScreenshotOptions? options)
    {
        ArgumentNullException.ThrowIfNull(element);
        var document = element.OwnerDocument ?? throw new InvalidOperationException("The element has no document.");
        var displayList = GetDisplayList(document);
        var rect = element.GetBoundingClientRect();
        var area = new PhysicalRect(Math.Min(rect.Left, rect.Right), Math.Min(rect.Top, rect.Bottom), Math.Abs(rect.Width), Math.Abs(rect.Height));
        if (area.IsEmpty)
            throw new InvalidOperationException("The element is not rendered or its border box is empty.");

        return Render(document, displayList, area, options);
    }

    private static DisplayList GetDisplayList(Document document)
    {
        if (document.BrowsingContext is null)
            throw new InvalidOperationException("The document is not rendered: it is not the active document of a browsing context.");

        return document.GetDisplayList() ?? throw new InvalidOperationException("The document is not rendered.");
    }

    /// <summary>Rasterizes an area of the viewport (in CSS px), rounded outwards to device pixels.</summary>
    internal static (byte[] Pixels, int Width, int Height) Render(Document document, DisplayList displayList, PhysicalRect area, ScreenshotOptions? options, System.Buffers.ArrayPool<byte>? pool = null)
    {
        var devicePixelRatio = options?.DevicePixelRatio ?? StyleEngine.CreateEnvironment(document).DevicePixelRatio;
        if (!double.IsFinite(devicePixelRatio) || devicePixelRatio <= 0)
            throw new ArgumentException("The device pixel ratio must be a positive number.", nameof(options));

        // A small tolerance avoids an extra row or column of pixels for the rounding errors of the layout
        const double Epsilon = 1e-6;
        var left = Math.Floor((area.Left * devicePixelRatio) + Epsilon);
        var top = Math.Floor((area.Top * devicePixelRatio) + Epsilon);
        var width = (int)Math.Max(1, Math.Ceiling((area.Right * devicePixelRatio) - Epsilon) - left);
        var height = (int)Math.Max(1, Math.Ceiling((area.Bottom * devicePixelRatio) - Epsilon) - top);

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
