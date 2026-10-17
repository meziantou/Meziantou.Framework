using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// Fetches the images referenced by the computed values of the rendered elements of a document, as the user agent does when
/// it renders them (https://drafts.csswg.org/css-values-4/#fetch-an-external-resource): the background images, the border
/// images, the list markers, the generated content and the cursors are fetched with the "no-cors" mode
/// (https://drafts.csswg.org/css-backgrounds/#background-image), the masks (https://drafts.fxtf.org/css-masking/#mask-image-property)
/// and the shapes (https://drafts.csswg.org/css-shapes/#shape-outside-property) with the "cors" mode. The initiator type of
/// their resource timing is "css" (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-initiatortype). The
/// request URL modifiers of a URL (https://drafts.csswg.org/css-values-5/#request-url-modifiers) set the mode, the credentials
/// mode, the integrity metadata and the referrer policy of its request. An image is fetched once per document, mode and
/// modifiers, as by the memory cache of the user agent.
/// </summary>
internal static class CssImageFetching
{
    /// <summary>The properties whose value may reference an image to fetch, and whether the image is fetched with the "cors" mode.</summary>
    private static readonly FrozenDictionary<string, bool> ImageProperties = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["background-image"] = false,
        ["border-image-source"] = false,
        ["list-style-image"] = false,
        ["content"] = false,
        ["cursor"] = false,
        ["mask-image"] = true,
        ["mask-border-source"] = true,
        ["shape-outside"] = true,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The properties whose value may reference an element of another document, whose document is fetched when the property
    /// is required for rendering an element (https://svgwg.org/svg2-draft/linking.html#processingURL): the paint servers, the
    /// markers, the filters, the clip paths, the masks and the paths of the motion paths (see <see cref="SvgResourceDocumentCache"/>).
    /// </summary>
    private static readonly string[] ResourceProperties = ["fill", "stroke", "marker-start", "marker-mid", "marker-end", "filter", "backdrop-filter", "clip-path", "mask-image", "offset-path"];

    private static readonly FrozenSet<string> ResourcePropertySet = ResourceProperties.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The presentation attributes of the SVG elements that map to <see cref="ResourceProperties"/> (and the <c>mask</c> shorthand).</summary>
    private static readonly FrozenSet<string> ResourceAttributes = new[] { "fill", "stroke", "marker-start", "marker-mid", "marker-end", "filter", "clip-path", "mask" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] PseudoElements = ["before", "after"];
    private static readonly string[] StyledPseudoElements = ["marker", "first-letter", "first-line"];

    /// <summary>
    /// Returns whether declarations may reference an image to fetch: a property of <see cref="ImageProperties"/> whose value
    /// has a function (e.g. <c>url()</c>, <c>image-set()</c>, <c>var()</c>) or a URL token.
    /// </summary>
    public static bool ReferencesImages(IEnumerable<CssDeclarationEntry> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (ImageProperties.ContainsKey(declaration.Name) && declaration.Value.Value.Contains('(', StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether declarations may reference an element of another document: a property of <see cref="ResourceProperties"/>
    /// whose value has a URL that is not a fragment (or a substitution function, which may result in one).
    /// </summary>
    public static bool ReferencesExternalResources(IEnumerable<CssDeclarationEntry> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (ResourcePropertySet.Contains(declaration.Name) && MayReferenceExternalResource(declaration.Value.Value))
                return true;
        }

        return false;
    }

    /// <summary>Whether a specified value has a URL that is not a fragment (<c>url(#id)</c>), or a substitution function.</summary>
    private static bool MayReferenceExternalResource(string value)
    {
        for (var index = value.IndexOf("url(", StringComparison.OrdinalIgnoreCase); index >= 0; index = value.IndexOf("url(", index + 4, StringComparison.OrdinalIgnoreCase))
        {
            var start = index + 4;
            while (start < value.Length && value[start] is ' ' or '"' or '\'')
            {
                start++;
            }

            if (start >= value.Length || value[start] != '#')
                return true;
        }

        return value.Contains("var(", StringComparison.OrdinalIgnoreCase) || value.Contains("attr(", StringComparison.OrdinalIgnoreCase) || value.Contains("if(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fetches the images referenced by the computed values of the rendered elements of a document that were not fetched yet.</summary>
    public static void FetchUsedImages(Document document, HashSet<(string Url, bool Cors, CssRequestModifiers Modifiers)> fetchedImages)
    {
        var engine = document.GetStyleEngine();
        var (mayReferenceImages, mayReferenceResources) = MayReferenceImages(document, engine);
        if (!document.CueStyleSheetsMayReferenceImages && !mayReferenceImages && !mayReferenceResources)
            return;

        var baseUrl = document.DocumentBaseUrl;
        var urls = new List<(string Url, CssRequestModifiers Modifiers)>();
        foreach (var element in EnumerateRenderedElements(document, engine))
        {
            var elementStyle = engine.GetStyle(element);
            if (mayReferenceResources)
            {
                FetchResourceDocuments(document, element, elementStyle, baseUrl, urls);
            }

            if (!mayReferenceImages && !document.CueStyleSheetsMayReferenceImages)
                continue;

            FetchImages(document, element, elementStyle, baseUrl, urls, fetchedImages);

            // The cues rendered over a video element (the size of the video only matters for their layout)
            if (element is HtmlVideoElement { CueDisplay: { } cueDisplay } video && cueDisplay.UpdateWithLastSize())
            {
                FetchCueImages(document, video, cueDisplay.Items, fetchedImages);
            }

            // The ::before and ::after pseudo-elements are generated when their content is not none or normal
            foreach (var pseudoElement in PseudoElements)
            {
                var pseudoStyle = engine.GetStyle(element, pseudoElement);
                if (pseudoStyle.GetComputedValue("content") is not ("none" or "normal"))
                {
                    FetchImages(document, element, pseudoStyle, baseUrl, urls, fetchedImages);
                }
            }

            // The ::marker, ::first-letter and ::first-line pseudo-elements have images of their own when a rule styles them
            // (the content of a marker, the background of the first letter)
            foreach (var pseudoElement in StyledPseudoElements)
            {
                // (only a list item has a marker)
                if (pseudoElement == "marker" && !engine.GetStyle(element).GetComputedValue("display").Contains("list-item", StringComparison.Ordinal))
                    continue;

                if (engine.MayHavePseudoElementRules(element, pseudoElement) && engine.GetStyle(element, pseudoElement) is { HasCascadedValues: true } pseudoStyle)
                {
                    FetchImages(document, element, pseudoStyle, baseUrl, urls, fetchedImages);
                }
            }
        }
    }

    /// <summary>
    /// Fetches the images referenced by the computed values of styles that are not the styles of the elements (the page
    /// contexts and the page-margin boxes of the printing, see <see cref="Layout.PrintSession"/>) that were not fetched yet.
    /// </summary>
    public static void FetchImages(Document document, IEnumerable<ComputedStyle> styles, HashSet<(string Url, bool Cors, CssRequestModifiers Modifiers)> fetchedImages)
    {
        var urls = new List<(string Url, CssRequestModifiers Modifiers)>();
        foreach (var style in styles)
        {
            FetchImages(document, style.Element, style, document.DocumentBaseUrl, urls, fetchedImages);
        }
    }

    /// <summary>
    /// Fetches the images referenced by the computed values of the cues and of the regions rendered over a video element
    /// (the <c>background-image</c> of the <c>::cue</c>, <c>::cue()</c> and <c>::cue-region</c> pseudo-elements) that were not
    /// fetched yet: the layout calls it when the cues that are displayed change.
    /// </summary>
    public static void FetchCueImages(Document document, HtmlVideoElement video, IReadOnlyList<WebVtt.WebVttDisplayItem> items, HashSet<(string Url, bool Cors, CssRequestModifiers Modifiers)> fetchedImages)
    {
        var engine = document.GetStyleEngine();
        var baseUrl = document.DocumentBaseUrl;
        var urls = new List<(string Url, CssRequestModifiers Modifiers)>();
        foreach (var item in items)
        {
            FetchCueImages(document, video, item.Element, engine, baseUrl, urls, fetchedImages);
        }
    }

    private static void FetchCueImages(Document document, HtmlVideoElement video, WebVtt.WebVttElement element, StyleEngine engine, UrlRecord baseUrl, List<(string Url, CssRequestModifiers Modifiers)> urls, HashSet<(string Url, bool Cors, CssRequestModifiers Modifiers)> fetchedImages)
    {
        FetchImages(document, video, engine.GetStyle(element), baseUrl, urls, fetchedImages);
        if (element.Kind == WebVtt.WebVttElementKind.CueRoot)
        {
            FetchImages(document, video, engine.GetStyle(element, WebVtt.WebVttElement.BackgroundBoxPseudoElement), baseUrl, urls, fetchedImages);
        }

        for (var child = element.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is WebVtt.WebVttElement childElement)
            {
                FetchCueImages(document, video, childElement, engine, baseUrl, urls, fetchedImages);
            }
        }
    }

    /// <summary>Returns whether a computed value of an element (or of its ::before, ::after and ::marker pseudo-elements) that may reference an image references the URL.</summary>
    public static bool UsesImage(Document document, Element element, string url)
    {
        var engine = document.GetStyleEngine();
        var baseUrl = document.DocumentBaseUrl;
        var urls = new List<(string Url, CssRequestModifiers Modifiers)>();
        foreach (var pseudoElement in (ReadOnlySpan<string?>)[null, "before", "after", "marker", "first-letter", "first-line"])
        {
            var style = engine.GetStyle(element, pseudoElement);
            foreach (var (property, _) in ImageProperties)
            {
                var value = style.GetComputedValue(property);
                if (!value.Contains('(', StringComparison.Ordinal))
                    continue;

                urls.Clear();
                CollectUrls(CssParser.ParseListOfComponentValues(value), urls, engine.Environment.DevicePixelRatio);
                foreach (var (candidate, _) in urls)
                {
                    if (UrlParser.Parse(candidate, baseUrl) is { } parsed && parsed.Serialize() == url)
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>Fetches the images referenced by the computed values of an element or of one of its pseudo-elements.</summary>
    private static void FetchImages(Document document, Element element, ComputedStyle style, UrlRecord baseUrl, List<(string Url, CssRequestModifiers Modifiers)> urls, HashSet<(string Url, bool Cors, CssRequestModifiers Modifiers)> fetchedImages)
    {
        foreach (var (property, defaultCors) in ImageProperties)
        {
            var value = style.GetComputedValue(property);
            if (!value.Contains('(', StringComparison.Ordinal))
                continue;

            urls.Clear();
            CollectUrls(CssParser.ParseListOfComponentValues(value), urls, style.Engine.Environment.DevicePixelRatio);
            foreach (var (url, modifiers) in urls)
            {
                // An empty URL and a fragment (a reference to an element of the document, e.g. an SVG mask) are not fetched
                if (url.Length == 0 || url[0] == '#' || UrlParser.Parse(url, baseUrl) is not { } parsedUrl)
                    continue;

                // https://drafts.csswg.org/css-values-4/#fetch-a-style-resource: the request URL modifiers apply to the request
                var cors = modifiers.IsCors(defaultCors);
                if (!fetchedImages.Add((parsedUrl.Serialize(), cors, modifiers)))
                    continue;

                var request = SubresourceFetching.CreateRequest(document, parsedUrl, RequestDestination.Image, cors, modifiers.ReferrerPolicy, initiator: element, useCredentials: modifiers.UsesCredentials, integrity: modifiers.IntegrityMetadata);
                var key = parsedUrl.Serialize();
                // https://drafts.csswg.org/css-images-3/#valdef-image-orientation-none: the orientation of the images that are not
                // CORS-same-origin is always applied (a CORS request fails when the response is not shared)
                var corsSameOrigin = cors || parsedUrl.Scheme == "data" || (!document.Origin.IsOpaque && OriginRecord.IsSameOrigin(document.Origin, OriginRecord.FromUrl(parsedUrl)));

                // The fetches started before the load event delay it (see Document.CssImagesDelayingTheLoadEvent)
                var delaysLoadEvent = document.ReadyState != DocumentReadyState.Complete;
                if (delaysLoadEvent)
                {
                    document.CssImagesDelayingTheLoadEvent++;
                }

                var isMaskReference = property == "mask-image" && parsedUrl.Fragment is { Length: > 0 };
                var started = SubresourceFetching.Fetch(document, request, resource =>
                {
                    // A mask image whose URL has a fragment may reference a mask element of the document of the URL
                    if (isMaskReference)
                    {
                        (document.SvgResourceDocuments ??= new SvgResourceDocumentCache()).AddFetchedForProperty(parsedUrl, modifiers, resource);
                    }

                    OnImageFetched(document, key, resource, corsSameOrigin);
                    if (delaysLoadEvent)
                    {
                        document.CssImagesDelayingTheLoadEvent--;
                        document.TryQueueLoadEvent();
                    }
                }, initiatorType: "css");
                if (!started && delaysLoadEvent)
                {
                    document.CssImagesDelayingTheLoadEvent--;
                }
            }
        }
    }

    /// <summary>
    /// Records the natural size of a fetched image (read from its header, see <see cref="ImageHeaders"/>), used by the layout
    /// of the generated content and of the list markers, and marks the layout of the document as out of date.
    /// </summary>
    private static void OnImageFetched(Document document, string url, DocumentResource? resource, bool corsSameOrigin)
    {
        ImageNaturalSize? naturalSize = null;
        if (resource is not null && ImageHeaders.TryGetImageInfo(EmbeddedResources.GetBytes(resource), resource.MimeType?.Essence ?? "", out var size))
        {
            // The natural size of an image that is not CORS-same-origin keeps its EXIF orientation with image-orientation: none
            naturalSize = corsSameOrigin ? size : size with { OrientationSwapsAxes = false };

            // The encoded bytes are kept for the painting when the document has a rendering backend (without one, the image is
            // painted into the display list without its bytes, for the paint timing)
            var bytes = resource.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "");
            (document.CssPaintImages ??= new(StringComparer.Ordinal))[url] = document.CreatePaintImage(url, size, bytes, resource.MimeType?.Essence, corsSameOrigin);

            // https://w3c.github.io/paint-timing/#sec-modifications-CSS: the image requests of the CSS images finished loading
            document.ProcessCssImageThatFinishedLoading(url, size, bytes.Length);
        }

        (document.CssImageNaturalSizes ??= new(StringComparer.Ordinal))[url] = naturalSize;

        // The marker of a list item whose list-style-image is not a valid image is the one of its list-style-type (see
        // IsInvalidImage): the boxes are built again
        document.InvalidateLayout(rebuildBoxTree: naturalSize is null);
    }

    /// <summary>
    /// Returns whether an image value (a computed <c>&lt;image&gt;</c>) is the URL of a resource that was fetched and is not an
    /// image (a network error, a resource that cannot be decoded): an invalid image
    /// (https://drafts.csswg.org/css-images-3/#invalid-image). False while the image is fetched.
    /// </summary>
    public static bool IsInvalidImage(Document document, string image)
    {
        if (document.CssImageNaturalSizes is not { } sizes)
            return false;

        var devicePixelRatio = document.GetStyleEngine().Environment.DevicePixelRatio;
        var urls = new List<(string Url, CssRequestModifiers Modifiers)>();
        CollectUrls(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(image)), urls, devicePixelRatio);
        return urls.Count > 0 && UrlParser.Parse(urls[0].Url, document.DocumentBaseUrl) is { } url && sizes.TryGetValue(url.Serialize(), out var naturalSize) && naturalSize is null;
    }

    /// <summary>
    /// Returns the natural dimensions (https://drafts.csswg.org/css-images-3/#natural-dimensions) of an image value (a computed
    /// <c>&lt;image&gt;</c>, e.g. <c>url("a.png")</c> or a gradient), in CSS pixels: the natural size of the fetched image of a
    /// URL (0 while it is fetched or when it is not an image, as in the browsers), no natural dimension for a generated
    /// image (a gradient). The natural resolution of the image of an <c>image-set()</c> is the resolution of its option.
    /// </summary>
    public static ImageNaturalSize GetNaturalSize(Document document, string image)
    {
        var devicePixelRatio = document.GetStyleEngine().Environment.DevicePixelRatio;
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(image));
        var resolution = values is [CssFunction function] && CssImageSet.IsImageSet(function) && CssImageSet.Select(function, devicePixelRatio) is { Resolution: > 0 } option ? option.Resolution : 1;
        var urls = new List<(string Url, CssRequestModifiers Modifiers)>();
        CollectUrls(values, urls, devicePixelRatio);
        if (urls.Count == 0)
            return default;

        if (UrlParser.Parse(urls[0].Url, document.DocumentBaseUrl) is { } url && document.CssImageNaturalSizes is { } sizes && sizes.TryGetValue(url.Serialize(), out var naturalSize) && naturalSize is { } size)
            return size.Scale(1 / resolution);

        return new ImageNaturalSize(0, 0, null);
    }

    /// <summary>
    /// Returns whether a style sheet of the document or of a shadow tree, a style attribute or a presentational hint may
    /// reference an image, and whether one may reference an element of another document (see <see cref="ResourceProperties"/>).
    /// </summary>
    private static (bool Images, bool Resources) MayReferenceImages(Document document, StyleEngine engine)
    {
        var images = engine.TreeReferencesImages(document);
        var resources = engine.TreeReferencesExternalResources(document);
        for (Node? node = document.FirstChild; node is not null && !(images && resources); node = Next(node))
        {
            if (node is not Element element)
                continue;

            if (element.AttachedShadowRoot is { } shadowRoot)
            {
                images |= engine.TreeReferencesImages(shadowRoot);
                resources |= engine.TreeReferencesExternalResources(shadowRoot);
            }

            if (element.AttributeCount == 0)
                continue;

            if (element.HasAttribute("style"))
            {
                var declarations = element.GetInlineStyle().Declarations;
                images = images || ReferencesImages(declarations);
                resources = resources || ReferencesExternalResources(declarations);
            }

            // The background attribute is a presentational hint of background-image (https://html.spec.whatwg.org/multipage/rendering.html#tables-2)
            images = images || element.HasAttribute("background");

            // The presentation attributes of the SVG elements (a mask is fetched as an image)
            if (!(images && resources) && element is SvgElement)
            {
                foreach (var attribute in element.Attributes)
                {
                    if (attribute.NamespaceUri is null && attribute.Value.Contains("url(", StringComparison.OrdinalIgnoreCase) && ResourceAttributes.Contains(attribute.LocalName) && MayReferenceExternalResource(attribute.Value))
                    {
                        images = images || attribute.LocalName == "mask";
                        resources = resources || attribute.LocalName != "mask";
                    }
                }
            }
        }

        return (images, resources);
    }

    /// <summary>
    /// Fetches the documents of the elements of other documents referenced by the computed values of an element (see
    /// <see cref="ResourceProperties"/>) that are not fetched yet: a URL with a fragment that is not a URL of the document. The
    /// fetches started before the load event delay it, and the document is rendered again when a resource document is loaded.
    /// </summary>
    private static void FetchResourceDocuments(Document document, Element element, ComputedStyle style, UrlRecord baseUrl, List<(string Url, CssRequestModifiers Modifiers)> urls)
    {
        foreach (var property in ResourceProperties)
        {
            // (a mask image is fetched as an image: its response is also the resource document of its URL, see FetchImages).
            // An inherited value is the value of an ancestor, which is rendered too: only the declared values are examined
            if (property == "mask-image" || !style.MayHaveCascadedValue(property))
                continue;

            var value = style.GetComputedValue(property);
            if (!value.Contains("url(", StringComparison.Ordinal))
                continue;

            urls.Clear();
            CollectUrls(CssParser.ParseListOfComponentValues(value), urls, style.Engine.Environment.DevicePixelRatio);
            foreach (var (url, modifiers) in urls)
            {
                if (url.Length == 0 || url[0] == '#' || UrlParser.Parse(url, baseUrl) is not { Fragment.Length: > 0 } parsedUrl
                    || parsedUrl.Serialize(excludeFragment: true) == document.UrlRecord.Serialize(excludeFragment: true))
                {
                    continue;
                }

                var delaysLoadEvent = document.ReadyState != DocumentReadyState.Complete;
                if (delaysLoadEvent)
                {
                    document.CssImagesDelayingTheLoadEvent++;
                }

                var cache = document.SvgResourceDocuments ??= new SvgResourceDocumentCache();
                var started = cache.LoadForProperty(document, element, parsedUrl, modifiers, () =>
                {
                    document.InvalidateLayout();
                    if (delaysLoadEvent)
                    {
                        document.CssImagesDelayingTheLoadEvent--;
                        document.TryQueueLoadEvent();
                    }
                });
                if (!started && delaysLoadEvent)
                {
                    document.CssImagesDelayingTheLoadEvent--;
                }
            }
        }
    }

    /// <summary>
    /// Enumerates the elements of the document and of its shadow trees that are rendered: the elements whose flat tree
    /// ancestors are not <c>display: none</c>, except the children of a shadow host that are not assigned to a slot.
    /// </summary>
    private static IEnumerable<Element> EnumerateRenderedElements(Document document, StyleEngine engine)
    {
        Node? node = document.FirstChild;
        while (node is not null)
        {
            if (node is Element element)
            {
                var notSlotted = element.ParentNode is Element { AttachedShadowRoot: not null } && element.Slottable?.AssignedSlot is null;
                if (notSlotted || engine.GetStyle(element).GetComputedValue("display") == "none")
                {
                    node = NextSkippingChildren(node);
                    continue;
                }

                yield return element;
            }

            node = Next(node);
        }
    }

    /// <summary>The next node in the tree order of the document and of its shadow trees (the shadow root of a host precedes its children).</summary>
    private static Node? Next(Node node)
    {
        if (node is Element { AttachedShadowRoot: { } shadowRoot })
            return shadowRoot.FirstChild ?? node.FirstChild ?? NextSkippingChildren(node);

        return node.FirstChild ?? NextSkippingChildren(node);
    }

    private static Node? NextSkippingChildren(Node node)
    {
        for (var current = node; current is not null;)
        {
            if (current.NextSibling is { } sibling)
                return sibling;

            switch (current.ParentNode)
            {
                case ShadowRoot shadowRoot:
                    // The children of the host follow its shadow tree
                    if (shadowRoot.Host.FirstChild is { } hostChild)
                        return hostChild;

                    current = shadowRoot.Host;
                    break;
                case { } parent:
                    current = parent;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Collects the URLs of a computed value and their request URL modifiers: the <c>url()</c> and <c>src()</c> functions,
    /// and the image of an <c>image-set()</c> for the resolution of the device (https://drafts.csswg.org/css-images-4/#image-set-notation,
    /// see <see cref="CssImageSet.Select"/>).
    /// </summary>
    private static void CollectUrls(List<CssComponentValue> values, List<(string Url, CssRequestModifiers Modifiers)> urls, double devicePixelRatio)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Url }:
                case CssFunction urlFunction when InfraAscii.EqualsIgnoreAsciiCase(urlFunction.Name, "url") || InfraAscii.EqualsIgnoreAsciiCase(urlFunction.Name, "src"):
                    if (CssUrlModifiers.TryGetUrl(value, out var url, out var modifiers))
                    {
                        urls.Add((url, modifiers));
                    }

                    break;

                case CssFunction function when CssImageSet.IsImageSet(function):
                    if (CssImageSet.Select(function, devicePixelRatio) is not { Resolution: > 0 } option)
                        break;

                    if (option.Image is CssPreservedToken { Type: CssTokenType.String } optionString)
                    {
                        urls.Add((optionString.Token.Value, CssRequestModifiers.None));
                    }
                    else
                    {
                        CollectUrls([option.Image], urls, devicePixelRatio);
                    }

                    break;

                case CssFunction function:
                    CollectUrls(function.Value, urls, devicePixelRatio);
                    break;

                case CssSimpleBlock block:
                    CollectUrls(block.Value, urls, devicePixelRatio);
                    break;
            }
        }
    }
}
