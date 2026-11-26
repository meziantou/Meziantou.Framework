using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.PdfRendering;

namespace Meziantou.Framework.FileConversionTool.Layout;

/// <summary>
/// Renders an SVG document with the web platform, as a browser shows the file: its root <c>svg</c> element is laid out at its
/// size, and it loads its resources (images, style sheets, fonts) from the loader. The documents never run scripts: the browsing
/// context has no script engine.
/// </summary>
/// <remarks>
/// The size of the document is the size of its root element, from its <c>width</c> and <c>height</c> attributes when they are
/// lengths (not percentages): a missing dimension follows the aspect ratio of the <c>viewBox</c>, or is the size of the
/// <c>viewBox</c> when both are missing. Without a <c>viewBox</c>, a missing dimension is the default size of a replaced element
/// (300x150), as for an <c>img</c> element in a browser.
/// </remarks>
internal static class SvgRenderer
{
    // The default size of a replaced element: https://www.w3.org/TR/CSS2/visudet.html#inline-replaced-width
    private const double DefaultWidth = 300;
    private const double DefaultHeight = 150;

    private const double CssPixelsPerPoint = 96.0 / 72.0;

    /// <summary>The maximum width and height of an image, in pixels.</summary>
    internal const int MaxImageDimension = 32768;

    /// <summary>The maximum number of pixels of an image: 128 megapixels (the image is drawn with 16-bit samples, 1 GB).</summary>
    internal const long MaxImagePixels = 128L * 1024 * 1024;

    /// <summary>Renders an SVG document as an image, transparent where the document paints nothing.</summary>
    /// <param name="imageWidth">The width of the image in pixels (--svg-width), or null. With only one of the dimensions, the other one keeps the aspect ratio of the document; with both, the image fits in them.</param>
    /// <param name="imageHeight">The height of the image in pixels (--svg-height), or null.</param>
    /// <exception cref="ConversionException">The document is not an SVG document, its size is empty, or the image is too large.</exception>
    public static ConversionPage RenderToImage(DocumentLoader loader, string url, string name, int pageNumber, int? imageWidth, int? imageHeight, CancellationToken cancellationToken)
    {
        var (width, height) = Measure(loader, url, name, cancellationToken);
        var scale = GetScale(width, height, imageWidth, imageHeight);
        var (pixelWidth, pixelHeight) = GetPixelSize(width, height, scale);
        if (pixelWidth > MaxImageDimension || pixelHeight > MaxImageDimension || (long)pixelWidth * pixelHeight > MaxImagePixels)
            throw new ConversionException(string.Create(CultureInfo.InvariantCulture, $"The SVG document '{name}' is too large to be rendered: {pixelWidth}x{pixelHeight} pixels. Set --svg-width or --svg-height to render it smaller"));

        // The screenshot of the viewport has the size of the document, whatever the document paints out of it (a transform of its root)
        using var context = Load(loader, url, name, width, height, cancellationToken);
        var image = context.TakeScreenshot(new ScreenshotOptions { DevicePixelRatio = scale, OmitBackground = true });
        return new LayoutRenderer.RenderedImagePage(pageNumber, image);
    }

    /// <summary>Renders an SVG document as a vector PDF page of its size (a CSS pixel is 0.75 point).</summary>
    /// <param name="printedDocuments">The PDF documents the pages belong to, which must stay alive until the pages are imported.</param>
    /// <exception cref="ConversionException">The document is not an SVG document, or its size is empty.</exception>
    public static ConversionPage RenderToPdf(DocumentLoader loader, string url, string name, int pageNumber, ConversionSettings settings, List<PdfDocument> printedDocuments, CancellationToken cancellationToken)
    {
        var (width, height) = Measure(loader, url, name, cancellationToken);

        // The size of a PDF page is at most 14400 points (200 inches)
        const double MaxPageSize = 14400 * CssPixelsPerPoint;
        if (width > MaxPageSize || height > MaxPageSize)
            throw new ConversionException(string.Create(CultureInfo.InvariantCulture, $"The SVG document '{name}' is too large for a PDF page: {width}x{height} CSS pixels"));

        using var context = Load(loader, url, name, width, height, cancellationToken);
        var document = context.PrintToPdf(new PrintOptions
        {
            PageWidth = width,
            PageHeight = height,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
            MarginLeft = 0,
            PreferCssPageSize = false,
        });
        printedDocuments.Add(document);
        if (document.Pages.Count is 0)
            throw new ConversionException($"The SVG document '{name}' has no page to convert");

        // The document fills the first page: a rounding error of the layout must not add a blank page
        return new LayoutRenderer.PrintedPdfPage(pageNumber, document.Pages[0], settings);
    }

    /// <summary>Gets the size of an image of the SVG document, in pixels, for a scale (the number of pixels per CSS pixel).</summary>
    internal static (int Width, int Height) GetPixelSize(double width, double height, double scale)
    {
        // The size of the screenshot of a viewport is rounded to device pixels
        return (ToPixels(width * scale), ToPixels(height * scale));

        static int ToPixels(double value) => value >= int.MaxValue ? int.MaxValue : (int)Math.Max(1, Math.Round(value, MidpointRounding.AwayFromZero));
    }

    /// <summary>Gets the number of pixels per CSS pixel of the image of a document of <paramref name="width"/>x<paramref name="height"/> CSS pixels.</summary>
    internal static double GetScale(double width, double height, int? imageWidth, int? imageHeight) => (imageWidth, imageHeight) switch
    {
        ({ } w, { } h) => Math.Min(w / width, h / height),
        ({ } w, null) => w / width,
        (null, { } h) => h / height,
        _ => 1,
    };

    /// <summary>
    /// Gets the size of the root <c>svg</c> element, in CSS pixels, from its dimensions (resolved by the layout when they are
    /// lengths), and its <c>viewBox</c>.
    /// </summary>
    /// <param name="width">The width of the element, or null when it is not a length (missing, <c>auto</c>, or a percentage).</param>
    /// <param name="height">The height of the element, or null when it is not a length.</param>
    /// <param name="viewBox">The value of the <c>viewBox</c> attribute, or null.</param>
    internal static (double Width, double Height) GetSize(double? width, double? height, string? viewBox)
    {
        var box = ParseViewBox(viewBox);
        return (width, height, box) switch
        {
            ({ } w, { } h, _) => (w, h),
            ({ } w, null, { } b) => (w, w * b.Height / b.Width),
            (null, { } h, { } b) => (h * b.Width / b.Height, h),
            (null, null, { } b) => (b.Width, b.Height),
            _ => (width ?? DefaultWidth, height ?? DefaultHeight),
        };
    }

    /// <summary>Parses the width and the height of a <c>viewBox</c> attribute (https://svgwg.org/svg2-draft/coords.html#ViewBoxAttribute), or null when it is not valid or empty.</summary>
    internal static (double Width, double Height)? ParseViewBox(string? value)
    {
        if (value is null)
            return null;

        Span<double> numbers = stackalloc double[4];
        var count = 0;
        foreach (var range in value.AsSpan().SplitAny(" \t\r\n,"))
        {
            var part = value.AsSpan()[range];
            if (part.IsEmpty)
                continue;

            if (count is 4 || !double.TryParse(part, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                return null;

            numbers[count++] = number;
        }

        // A viewBox with a width or a height that is not positive disables the rendering of the element: it is not used for the size
        if (count is not 4 || numbers[2] <= 0 || numbers[3] <= 0)
            return null;

        return (numbers[2], numbers[3]);
    }

    /// <summary>Gets a value indicating whether a <c>width</c> or <c>height</c> attribute of the root element is a length the layout resolves to a size: not missing, not <c>auto</c>, not a percentage of the viewport.</summary>
    internal static bool IsLength(string? value)
    {
        var trimmed = value.AsSpan().Trim(" \t\r\n\f");
        return !trimmed.IsEmpty && !trimmed.EndsWith('%') && !trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Loads the document in a viewport of the default size, and gets its size in CSS pixels.</summary>
    /// <exception cref="ConversionException">The document is not an SVG document, or its size is empty.</exception>
    private static (double Width, double Height) Measure(DocumentLoader loader, string url, string name, CancellationToken cancellationToken)
    {
        using var context = CreateContext(loader, viewport: null);
        var root = Navigate(context, url, name, cancellationToken);

        // The layout resolves the lengths of the dimensions (units, font-relative lengths)
        var rect = root.GetBoundingClientRect();
        var (width, height) = GetSize(IsLength(root.GetAttribute("width")) ? rect.Width : null, IsLength(root.GetAttribute("height")) ? rect.Height : null, root.GetAttribute("viewBox"));
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ConversionException(string.Create(CultureInfo.InvariantCulture, $"The SVG document '{name}' has an empty size: {width}x{height}"));

        return (width, height);
    }

    /// <summary>Loads the document in a viewport of its size, and sets the size of its root element in CSS pixels, so that it fills the viewport.</summary>
    private static BrowsingContext Load(DocumentLoader loader, string url, string name, double width, double height, CancellationToken cancellationToken)
    {
        // The viewport has no scrollbar: they would cover the document when it paints out of the viewport
        var context = CreateContext(loader, new ViewportOptions { Width = width, Height = height, ScrollbarsVisible = false });
        try
        {
            var root = Navigate(context, url, name, cancellationToken);
            root.SetAttribute("width", string.Create(CultureInfo.InvariantCulture, $"{width:R}px"));
            root.SetAttribute("height", string.Create(CultureInfo.InvariantCulture, $"{height:R}px"));
            context.EventLoop.RunUntilIdle();
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static BrowsingContext CreateContext(DocumentLoader loader, ViewportOptions? viewport) => new(new BrowsingContextOptions
    {
        DocumentLoader = loader,
        RenderingBackend = new ImagingRenderingBackend(),
        Viewport = viewport,
    });

    /// <summary>Navigates to the document, and gets its root <c>svg</c> element.</summary>
    /// <exception cref="ConversionException">The document is not an SVG document.</exception>
    private static Element Navigate(BrowsingContext context, string url, string name, CancellationToken cancellationToken)
    {
        context.Navigate(url);
        context.EventLoop.RunUntilIdle();
        cancellationToken.ThrowIfCancellationRequested();

        // A document that is not well-formed is a document of the error of the XML parser
        return context.Document.DocumentElement is { LocalName: "svg", NamespaceUri: "http://www.w3.org/2000/svg" } element
            ? element
            : throw new ConversionException($"The input '{name}' is not an SVG document: its root element must be an svg element of the SVG namespace, and the document must be well-formed XML");
    }
}
