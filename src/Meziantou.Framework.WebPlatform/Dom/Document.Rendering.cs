using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>
    /// The rendering backend of the browsing context of the document (<see cref="BrowsingContextOptions.RenderingBackend"/>), or of
    /// the document that uses the document as an SVG image, or null: nothing is rasterized.
    /// </summary>
    internal RenderingBackend? RenderingBackend => BrowsingContext is { } browsingContext ? browsingContext.Options.RenderingBackend : SvgImage?.Backend;

    /// <summary>The SVG image whose document is this document (see <see cref="Css.Layout.SvgImage"/>), or null.</summary>
    internal Css.Layout.SvgImage? SvgImage { get; set; }

    /// <summary>
    /// The document with a browsing context that loaded this document as the external resource of a use element (see
    /// <see cref="SvgUseElement"/>), or null: it fetches the external resources of the use elements of this document.
    /// </summary>
    internal Document? SvgResourceHost { get; set; }

    /// <summary>The external resource documents loaded by the use elements of the document, or null.</summary>
    internal SvgResourceDocumentCache? SvgResourceDocuments { get; set; }

    /// <summary>The view of the fragment of the URL of the document (see <see cref="Svg.SvgViewSpec.Get"/>), for a version of the DOM.</summary>
    internal (int Version, string Fragment, Svg.SvgViewSpec? Spec)? SvgViewSpecCache { get; set; }

    /// <summary>The number of resource documents between this document and the document that loaded them (0 for a document that is not a resource document).</summary>
    internal int SvgResourceDepth { get; set; }

    /// <summary>
    /// Returns the display list of the viewport of the document (see <see cref="Css.Layout.Painter"/>): the layout is computed
    /// when it is out of date, and the document is painted again only when its layout, its styles or its scroll positions
    /// changed. Returns null when the document is not rendered.
    /// </summary>
    internal DisplayList? GetDisplayList() => GetLayoutEngine().GetDisplayList();

    /// <summary>
    /// Creates the image the painting draws for an image resource: the encoded bytes of a raster image are kept only when the
    /// document has a rendering backend, which decodes them when the image is first drawn (without a backend, the display list
    /// draws an image without pixels, which the paint timing observes); the ones of an SVG image are always kept, as its document
    /// is painted by the library (see <see cref="Css.Layout.SvgImage"/>). The natural size is the one of
    /// <c>image-orientation: from-image</c>; an animated image is played from now, on the clock of the event loop of the document.
    /// </summary>
    /// <param name="url">The URL of the image.</param>
    /// <param name="naturalSize">The natural size of the image (read from its header).</param>
    /// <param name="data">The encoded bytes of the image.</param>
    /// <param name="mimeType">The MIME type essence of the resource, or null.</param>
    /// <param name="isCorsSameOrigin">Whether the resource is CORS-same-origin (<c>image-orientation: none</c> applies to it, and it does not taint the canvases).</param>
    internal PaintImage CreatePaintImage(string url, ImageNaturalSize naturalSize, ReadOnlyMemory<byte> data, string? mimeType, bool isCorsSameOrigin = true)
    {
        if (RenderingBackend is null)
            return new(url, naturalSize.Width ?? 0, naturalSize.Height ?? 0, mimeType == "image/svg+xml" ? data : default, mimeType) { IsCorsSameOrigin = isCorsSameOrigin };

        var clock = EventLoop?.TimeProvider;
        return new(url, naturalSize.Width ?? 0, naturalSize.Height ?? 0, data, mimeType, ImageMetadata.GetExifOrientation(data.Span))
        {
            IsCorsSameOrigin = isCorsSameOrigin,
            AnimationClock = clock is null ? null : (clock, clock.GetTimestamp()),
        };
    }

    private bool _mayHavePageColorSchemes;
    private bool _pageColorSchemesComputed;
    private string? _pageColorSchemes;

    /// <summary>
    /// The page's supported color schemes (https://html.spec.whatwg.org/multipage/semantics.html#meta-color-scheme): the content
    /// of the first <c>meta</c> element of the document whose name is <c>color-scheme</c> and whose content is a valid value of
    /// the <c>color-scheme</c> property (in lowercase), or null. They are the color schemes of <c>color-scheme: normal</c>
    /// (https://drafts.csswg.org/css-color-adjust-1/#valdef-color-scheme-normal).
    /// </summary>
    internal string? PageColorSchemes
    {
        get
        {
            if (!_mayHavePageColorSchemes)
                return null;

            if (!_pageColorSchemesComputed)
            {
                _pageColorSchemes = null;
                foreach (var node in Descendants())
                {
                    if (node is HtmlMetaElement meta && Infra.InfraAscii.EqualsIgnoreAsciiCase(meta.GetAttributeValue("name") ?? "", "color-scheme") && meta.GetAttributeValue("content") is { } content
                        && Css.Om.Css.Supports("color-scheme", content))
                    {
                        var value = Infra.InfraAscii.ToAsciiLowercase(content).Trim();
                        if (value is not ("normal" or "inherit" or "initial" or "unset" or "revert" or "revert-layer"))
                        {
                            _pageColorSchemes = value;
                        }

                        break;
                    }
                }

                _pageColorSchemesComputed = true;
            }

            return _pageColorSchemes;
        }
    }

    /// <summary>Called when a <c>meta</c> element is inserted in or removed from the document, or when its name or content changes.</summary>
    internal void InvalidatePageColorSchemes(HtmlMetaElement meta, string? oldName = null)
    {
        if (!Infra.InfraAscii.EqualsIgnoreAsciiCase(meta.GetAttributeValue("name") ?? "", "color-scheme") && !Infra.InfraAscii.EqualsIgnoreAsciiCase(oldName ?? "", "color-scheme"))
            return;

        _mayHavePageColorSchemes = true;
        _pageColorSchemesComputed = false;
    }
}
