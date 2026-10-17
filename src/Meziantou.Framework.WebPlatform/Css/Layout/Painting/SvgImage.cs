using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// An SVG image (an <c>img</c> element, a CSS image or an SVG <c>image</c> element referencing an SVG document,
/// https://svgwg.org/svg2-draft/conform.html#secure-animated-mode): the document is parsed once, in its own isolated
/// <see cref="Document"/> without browsing context (no script runs, no external resource is fetched: only the <c>data:</c> URLs
/// of its images), and it is laid out and painted by the same pipeline as the other documents, with a viewport of the size the
/// image is drawn at (the concrete object size, https://drafts.csswg.org/css-images-3/#concrete-size-resolution). The display
/// lists of the last sizes are cached.
/// </summary>
internal sealed class SvgImage
{
    /// <summary>The maximum depth of the SVG images that contain SVG images (the deeper images are not painted).</summary>
    private const int MaxDepth = 4;

    private const int MaxCachedSizes = 8;

    private readonly List<(PhysicalSize Size, string ColorScheme, string? PreserveAspectRatio, double Zoom, DisplayList? List)> _displayLists = [];

    private readonly string _hostColorScheme;
    private string? _rootPreserveAspectRatio;

    private SvgImage(Document document, RenderingBackend? backend, MediaEnvironment environment, int depth)
    {
        Document = document;
        _hostColorScheme = environment.PrefersColorScheme;
        Backend = backend;
        Environment = environment;
        Depth = depth;
    }

    /// <summary>The document of the image.</summary>
    public Document Document { get; }

    /// <summary>The rendering backend of the document that uses the image (the images of the image are decoded by it).</summary>
    public RenderingBackend? Backend { get; }

    /// <summary>The environment of the document of the image: the size of its viewport, and the preferences of the document that uses it.</summary>
    public MediaEnvironment Environment { get; private set; }

    /// <summary>The number of SVG images that contain this one (0 for an image of a document that is not an image).</summary>
    public int Depth { get; }

    /// <summary>
    /// The zoom of the element that draws the image being painted: the lengths of the host coordinate space of the image (the strokes
    /// of <c>vector-effect: non-scaling-stroke</c>) are multiplied by it, as the image is laid out at its zoomed size.
    /// </summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>
    /// The natural dimensions of the image (https://svgwg.org/svg2-draft/coords.html#SizingSVGInCSS): the width and the height of
    /// its root svg element, and its aspect ratio.
    /// </summary>
    public ImageNaturalSize NaturalSize { get; private set; }

    /// <summary>
    /// Returns the SVG image of an image resource used by a document, parsed the first time; null when the image is not an SVG
    /// image, its bytes were not kept (no rendering backend), it is not a well-formed SVG document, or it is nested too deeply.
    /// </summary>
    public static SvgImage? Get(PaintImage image, Document host)
    {
        if (!image.IsSvg || image.Data.IsEmpty || image.SvgImageFailed)
            return null;

        if (image.SvgImage is { } cached)
            return cached;

        var depth = host.SvgImage is { } hostImage ? hostImage.Depth + 1 : 0;
        if (depth > MaxDepth)
            return null;

        var hostEnvironment = host.GetStyleEngine().Environment;
        var environment = new MediaEnvironment
        {
            DevicePixelRatio = hostEnvironment.DevicePixelRatio,
            MediaType = hostEnvironment.MediaType,
            PrefersColorScheme = hostEnvironment.PrefersColorScheme,
            PrefersReducedMotion = hostEnvironment.PrefersReducedMotion,
            PrefersReducedTransparency = hostEnvironment.PrefersReducedTransparency,
            PrefersContrast = hostEnvironment.PrefersContrast,
            ForcedColors = hostEnvironment.ForcedColors,
            ForcedColorsPalette = hostEnvironment.ForcedColorsPalette,
            DeviceWidth = hostEnvironment.DeviceWidth,
            DeviceHeight = hostEnvironment.DeviceHeight,
            ScriptingEnabled = false,
        };

        Document document;
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(image.Data.Span);
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text[1..];
            }

            // The XML parser parses the document synchronously; the document has no browsing context: it runs no script and fetches nothing
            document = XmlDocumentParser.ParseDocument(text, new XmlParserOptions { Url = image.Url.Length > 0 ? image.Url : "about:blank", ContentType = "image/svg+xml" });
        }
#pragma warning disable CA1031 // A document that cannot be parsed is a broken image
        catch (Exception)
#pragma warning restore CA1031
        {
            image.SvgImageFailed = true;
            return null;
        }

        if (document.DocumentElement is not SvgSvgElement root)
        {
            image.SvgImageFailed = true;
            return null;
        }

        var result = new SvgImage(document, host.RenderingBackend, environment, depth)
        {
            NaturalSize = ImageHeaders.GetSvgNaturalSize(root.GetAttributeValue("width"), root.GetAttributeValue("height"), root.GetAttributeValue("viewBox")),
        };

        // The view of the fragment of the URL of the image (svgView(), the id of a view element) overrides the viewBox and the
        // preserveAspectRatio of the root
        var view = Svg.SvgViewSpec.Get(document);
        result._rootPreserveAspectRatio = view?.PreserveAspectRatio ?? root.GetAttributeValue("preserveAspectRatio") ?? "";
        if (view?.ViewBox is not null)
        {
            result.NaturalSize = ImageHeaders.GetSvgNaturalSize(root.GetAttributeValue("width"), root.GetAttributeValue("height"), view.ViewBox);
        }

        // As in the browsers, an image with a natural width and height but without viewBox is scaled to the size it is drawn at: a
        // viewBox of its natural size is synthesized (https://github.com/w3c/svgwg/issues/876)
        if (!root.HasAttribute("viewBox") && view?.ViewBox is null && result.NaturalSize is { Width: > 0 and var naturalWidth, Height: > 0 and var naturalHeight })
        {
            root.SetAttribute("viewBox", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"0 0 {naturalWidth} {naturalHeight}"));
        }

        document.SvgImage = result;
        image.SvgImage = result;

        // The images of the image (data: URLs only) are loaded now that the document is the document of an image
        foreach (var imageElement in document.Descendants().OfType<SvgImageElement>().ToList())
        {
            imageElement.LoadImageOfSvgImage();
        }

        return result;
    }

    /// <summary>
    /// Rasterizes the image laid out in a viewport of a size in pixels (one pixel per CSS px) with a backend: the image drawn
    /// into a canvas pattern or an <c>ImageBitmap</c> (https://html.spec.whatwg.org/multipage/canvas.html#image-sources-for-2d-rendering-contexts).
    /// Returns null when the backend cannot create the bitmap.
    /// </summary>
    public RenderingImage? Rasterize(RenderingBackend backend, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (width <= 0 || height <= 0)
            return null;

        using var surface = backend.CreateSurface(width, height, RenderingSurfaceOptions.Default);
        if (surface is null)
            return null;

        if (GetDisplayList(new PhysicalSize(width, height)) is { } list)
        {
            surface.Draw(list);
        }

        return surface.CreateSnapshot();
    }

    /// <summary>
    /// Returns the display list of the image drawn in a rectangle of a size (its viewport, in CSS px), or null when nothing is
    /// painted. The display list is in the coordinates of the viewport of the image (from (0, 0) to <paramref name="size"/>), not clipped.
    /// </summary>
    /// <param name="size">The size of the viewport of the image.</param>
    /// <param name="colorScheme">
    /// The used color scheme of the element that uses the image (<c>light</c> or <c>dark</c>): the value of the
    /// <c>prefers-color-scheme</c> media feature in the image (https://github.com/w3c/csswg-drafts/issues/7213), or null for the
    /// preference of the document that uses the image.
    /// </param>
    /// <param name="preserveAspectRatio">
    /// The preserveAspectRatio attribute of the SVG image element that draws the image, which replaces the one of the root of the
    /// image (https://svgwg.org/svg2-draft/embedded.html#ImageElement), or null.
    /// </param>
    /// <param name="zoom">The zoom of the element that draws the image (see <see cref="Zoom"/>).</param>
    public DisplayList? GetDisplayList(PhysicalSize size, string? colorScheme = null, string? preserveAspectRatio = null, double zoom = 1)
    {
        if (!(zoom > 0) || !double.IsFinite(zoom))
        {
            zoom = 1;
        }

        if (!(size.Width > 0) || !(size.Height > 0) || !double.IsFinite(size.Width) || !double.IsFinite(size.Height))
            return null;

        colorScheme ??= _hostColorScheme;
        foreach (var (cachedSize, cachedScheme, cachedPreserveAspectRatio, cachedZoom, cachedList) in _displayLists)
        {
            if (cachedSize == size && cachedScheme == colorScheme && cachedPreserveAspectRatio == preserveAspectRatio && cachedZoom == zoom)
                return cachedList;
        }

        // The document is private to the image: the preserveAspectRatio of the image element is set on its root
        if (Document.DocumentElement is { } root)
        {
            var value = preserveAspectRatio ?? _rootPreserveAspectRatio ?? "";
            if ((root.GetAttributeValue("preserveAspectRatio") ?? "") != value)
            {
                if (value.Length == 0)
                {
                    root.RemoveAttribute("preserveAspectRatio");
                }
                else
                {
                    root.SetAttribute("preserveAspectRatio", value);
                }
            }
        }

        Environment = Environment with { Width = size.Width, Height = size.Height, PrefersColorScheme = colorScheme };
        DisplayList? list;
        try
        {
            if (Zoom != zoom)
            {
                Zoom = zoom;
                Document.GetLayoutEngine().InvalidatePaint();
            }

            // The document of the image has no rendering opportunity: the steps of the rendering update that depend on its
            // layout run before it is painted (the proximity to the viewport of the elements with content-visibility: auto,
            // https://drafts.csswg.org/css-contain-2/#proximity-to-the-viewport, which skip their contents until it is determined)
            if (Document.GetStyleEngine().MayHaveContentVisibility())
            {
                Document.GetLayoutEngine().UpdateRendering();
            }

            list = Document.GetDisplayList();
        }
#pragma warning disable CA1031 // A document that cannot be laid out or painted is a broken image
        catch (Exception)
#pragma warning restore CA1031
        {
            list = null;
        }

        if (_displayLists.Count >= MaxCachedSizes)
        {
            _displayLists.RemoveAt(0);
        }

        _displayLists.Add((size, colorScheme, preserveAspectRatio, zoom, list));
        return list;
    }
}
