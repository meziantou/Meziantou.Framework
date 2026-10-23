using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The fetching of the resources of the <c>embed</c> and <c>object</c> elements
/// (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-embed-element and
/// https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element). There is no plugin: a resource is
/// displayed by the content navigable of the element (or as an image by an <c>object</c> element).
/// </summary>
internal static class EmbeddedResources
{
    /// <summary>
    /// Fetches the resource of an <c>embed</c> or <c>object</c> element: a request whose destination is the element, whose
    /// mode is "navigate" and whose credentials mode is "include". <paramref name="onComplete"/> is called from a task with the
    /// response, or null for a network error (including the URLs that cannot be fetched, e.g. <c>javascript:</c>). The
    /// response of an <c>about:blank</c> URL is an empty HTML document.
    /// </summary>
    public static void Fetch(Element element, UrlRecord url, Action<DocumentResource?> onComplete)
    {
        var document = element.NodeDocument;
        if (url.Scheme == "about")
        {
            var resource = BrowsingContext.MatchesAboutBlank(url) ? DocumentResource.Create("", "text/html") : null;
            document.QueueElementTask(() => onComplete(resource));
            return;
        }

        if (url.Scheme is not ("http" or "https" or "file" or "data" or "blob"))
        {
            document.QueueElementTask(() => onComplete(null));
            return;
        }

        var destination = element is HtmlEmbedElement ? RequestDestination.Embed : RequestDestination.Object;
        var request = new FetchRequest(url)
        {
            Client = document,
            Origin = document.SerializeOrigin(),
            Destination = destination,
            Mode = RequestMode.Navigate,
            CredentialsMode = FetchCredentialsMode.Include,
            Initiator = element,
            ReferrerPolicy = ReferrerPolicies.GetRequestPolicy("", document),
        };

        if (!SubresourceFetching.Fetch(document, request, onComplete, element.LocalName))
        {
            document.QueueElementTask(() => onComplete(null));
        }
    }

    /// <summary>Navigates the content navigable of a container to the resource it fetched, as a replacement of the active document.</summary>
    public static void Navigate(Element container, BrowsingContext navigable, UrlRecord url, DocumentResource response)
    {
        // The data:, blob: and about: URLs are processed by the navigation (e.g. the origin of the document is opaque)
        if (response.Url is { } responseUrl && UrlParser.Parse(responseUrl, baseUrl: null) is { } parsedResponseUrl)
        {
            url = parsedResponseUrl;
        }

        var fetchedResponse = url.Scheme is "http" or "https" or "file" ? response : null;
        navigable.Navigate(url, container.NodeDocument, NavigationHistoryBehavior.Replace, response: fetchedResponse);
    }

    /// <summary>Whether the element has an ancestor media element (https://html.spec.whatwg.org/multipage/media.html#media-element).</summary>
    public static bool HasAncestorMediaElement(Element element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor is HtmlMediaElement)
                return true;
        }

        return false;
    }

    /// <summary>Whether the element has an ancestor <c>object</c> element that is not showing its fallback content.</summary>
    public static bool HasAncestorObjectNotShowingFallback(Element element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor is HtmlObjectElement { IsShowingFallbackContent: false })
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://mimesniff.spec.whatwg.org/#rules-for-text-or-binary: whether the resource is binary (it contains a binary
    /// data byte, and it does not start with a byte order mark).
    /// </summary>
    public static bool IsBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return false;

        foreach (var b in bytes[..Math.Min(bytes.Length, 1445)])
        {
            if (b is <= 0x08 or 0x0B or (>= 0x0E and <= 0x1A) or (>= 0x1C and <= 0x1F))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The computed MIME type of a resource without a Content-Type (https://mimesniff.spec.whatwg.org/#rules-for-sniffing-the-mime-type):
    /// an HTML document, an image (e.g. <c>image/png</c>), or else text or binary.
    /// </summary>
    public static string SniffType(ReadOnlySpan<byte> bytes)
    {
        var start = 0;
        while (start < bytes.Length && bytes[start] is 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
        {
            start++;
        }

        var text = bytes[start..];
        foreach (var pattern in HtmlPatterns)
        {
            if (text.Length > pattern.Length && StartsWithIgnoreAsciiCase(text, pattern) && text[pattern.Length] is 0x20 or 0x3E)
                return "text/html";
        }

        if (text.StartsWith("<!--"u8))
            return "text/html";

        if (ImageHeaders.TryGetImageInfo(bytes, "", out _))
            return "image/*";

        return IsBinary(bytes) ? "application/octet-stream" : "text/plain";
    }

    private static bool StartsWithIgnoreAsciiCase(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> upperCasePattern)
    {
        for (var i = 0; i < upperCasePattern.Length; i++)
        {
            var b = bytes[i];
            if ((b is >= (byte)'a' and <= (byte)'z' ? (byte)(b - 0x20) : b) != upperCasePattern[i])
                return false;
        }

        return true;
    }

    // https://mimesniff.spec.whatwg.org/#identifying-a-resource-with-an-unknown-mime-type: the patterns of text/html
    private static readonly byte[][] HtmlPatterns =
    [
        "<!DOCTYPE HTML"u8.ToArray(), "<HTML"u8.ToArray(), "<HEAD"u8.ToArray(), "<SCRIPT"u8.ToArray(), "<IFRAME"u8.ToArray(),
        "<H1"u8.ToArray(), "<DIV"u8.ToArray(), "<FONT"u8.ToArray(), "<TABLE"u8.ToArray(), "<A"u8.ToArray(), "<STYLE"u8.ToArray(),
        "<TITLE"u8.ToArray(), "<B"u8.ToArray(), "<BODY"u8.ToArray(), "<BR"u8.ToArray(), "<P"u8.ToArray(),
    ];

    /// <summary>
    /// Whether a content navigable can display a resource of the type: the HTML, XML, text and JSON types (as in the
    /// browsers, an <c>object</c> element falls back for the other types, which would be handled by a plugin or downloaded).
    /// </summary>
    public static bool IsNavigableType(string type)
    {
        if (MimeType.Parse(type) is not { } mimeType)
            return false;

        return mimeType.IsXml || mimeType.Type == "text" || mimeType.Subtype == "json" || mimeType.Subtype.EndsWith("+json", StringComparison.Ordinal)
            || mimeType.Essence is "application/javascript" or "application/ecmascript";
    }

    /// <summary>
    /// Creates the image an <c>embed</c> or <c>object</c> element represents from its resource when the resource is an image
    /// of the type (not an SVG document, which is displayed by the content navigable): the image is sized and painted as the
    /// image of an <c>img</c> element (<c>object-fit</c>, <c>object-position</c>, <c>image-rendering</c>).
    /// </summary>
    public static bool TryCreateImage(Element element, UrlRecord url, DocumentResource response, string type, out Rendering.PaintImage? image, out ImageNaturalSize naturalSize)
    {
        image = null;
        if (!ImageHeaders.TryGetImageInfo(GetBytes(response), type, out naturalSize))
            return false;

        ReadOnlyMemory<byte> bytes = response.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(response.Content ?? "");
        image = element.NodeDocument.CreatePaintImage(response.Url ?? url.Serialize(), naturalSize, bytes, MimeType.Parse(type)?.Essence);
        return true;
    }

    /// <summary>The bytes of a response.</summary>
    public static ReadOnlySpan<byte> GetBytes(DocumentResource resource) => resource.ContentBytes is { } bytes ? bytes.Span : System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "");
}

public partial class HtmlEmbedElement
{
    // Incremented each time the embed element setup steps are queued ("if another task has since been queued")
    private int _setupGeneration;
    private bool _wasPotentiallyActive;
    private bool _sourceChanged;
    private Document? _delayedDocument;

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#content-navigable</summary>
    internal BrowsingContext? ContentNavigable { get; set; }

    /// <summary>The image the element displays when its resource is an image (see <see cref="EmbeddedResources.TryCreateImage"/>), or null.</summary>
    internal Rendering.PaintImage? Image { get; private set; }

    /// <summary>The natural size of <see cref="Image"/>.</summary>
    internal ImageNaturalSize ImageNaturalSize { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-embed-getsvgdocument</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-embed-getsvgdocument"/>
    [IdlMember("getSVGDocument", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-embed-getsvgdocument")]
    public Document? GetSvgDocument() => NavigableContainers.GetSvgDocument(this);

    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        UpdatePotentiallyActive();
    }

    /// <summary>When the element is removed, its plugin is unloaded and its content navigable is destroyed.</summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        _wasPotentiallyActive = false;
        _setupGeneration++;
        SetDelayingTheLoadEvent(false);
        BrowsingContext.DestroyChildNavigable(this);
        Image = null;
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);

        // The name attribute only names the content navigable when it is created
        if (@namespace is null && localName is "src" or "type" && IsConnected)
        {
            _sourceChanged = true;
            UpdatePotentiallyActive();
        }
    }

    /// <summary>
    /// Queues a task that runs the embed element setup steps when the element becomes potentially active, or when its
    /// <c>src</c> or <c>type</c> attribute changes while it remains potentially active. Whether the element is potentially
    /// active is determined by the task (the element is being rendered "the last time the event loop reached step 1").
    /// </summary>
    internal void UpdatePotentiallyActive()
    {
        var document = NodeDocument;
        if (document.BrowsingContext is null)
            return;

        var generation = ++_setupGeneration;
        SetDelayingTheLoadEvent(true);
        document.QueueElementTask(() =>
        {
            if (generation != _setupGeneration)
                return;

            var isPotentiallyActive = IsPotentiallyActive();
            var wasPotentiallyActive = _wasPotentiallyActive;
            var sourceChanged = _sourceChanged;
            _wasPotentiallyActive = isPotentiallyActive;
            _sourceChanged = false;
            if (!isPotentiallyActive)
            {
                // The element represents nothing (e.g. it has neither a src nor a type attribute)
                if ((!HasAttributeWithLocalName("src") && !HasAttributeWithLocalName("type")) || EmbeddedResources.HasAncestorMediaElement(this) || EmbeddedResources.HasAncestorObjectNotShowingFallback(this))
                {
                    BrowsingContext.DestroyChildNavigable(this);
                    SetImage(null, default);
                }

                SetDelayingTheLoadEvent(false);
                return;
            }

            if (wasPotentiallyActive && !sourceChanged)
            {
                SetDelayingTheLoadEvent(false);
                return;
            }

            RunSetupSteps(generation);
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#concept-embed-active</summary>
    private bool IsPotentiallyActive()
    {
        var document = NodeDocument;
        if (!IsConnected || !document.IsFullyActive || document.BrowsingContext is null)
            return false;

        var src = GetAttributeValue("src");
        if (src is null && !HasAttributeWithLocalName("type"))
            return false;

        if (src is { Length: 0 })
            return false;

        return !EmbeddedResources.HasAncestorMediaElement(this) && !EmbeddedResources.HasAncestorObjectNotShowingFallback(this) && ComputedStyleDeclarations.IsBeingRendered(this);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-embed-element-setup-steps</summary>
    private void RunSetupSteps(int generation)
    {
        var document = NodeDocument;
        if (GetAttributeValue("src") is not { } src)
        {
            // Display no plugin
            BrowsingContext.DestroyChildNavigable(this);
            SetImage(null, default);
            SetDelayingTheLoadEvent(false);
            return;
        }

        if (UrlParser.Parse(src, document.DocumentBaseUrl, document.Encoding) is not { } url)
        {
            SetDelayingTheLoadEvent(false);
            return;
        }

        // As in the browsers, a URL that cannot be fetched (e.g. javascript:) displays no plugin, without a load event
        // (WPT embed-javascript-url.html); a network error of a fetch fires the load event
        if (url.Scheme is not ("about" or "blob" or "data" or "file" or "http" or "https"))
        {
            BrowsingContext.DestroyChildNavigable(this);
            SetImage(null, default);
            SetDelayingTheLoadEvent(false);
            return;
        }

        EmbeddedResources.Fetch(this, url, response =>
        {
            if (generation != _setupGeneration)
                return;

            if (response is null)
            {
                SetImage(null, default);
                FireEvent(new Event("load"));
                SetDelayingTheLoadEvent(false);
                return;
            }

            // As in the browsers, an image is displayed by the element itself, as the image of an img element (the
            // specification displays no plugin); an SVG image is a document displayed by the content navigable
            var type = response.MimeType?.Essence ?? EmbeddedResources.SniffType(EmbeddedResources.GetBytes(response));
            if (MimeType.Parse(type) is { IsXml: false, Type: "image" } && EmbeddedResources.TryCreateImage(this, url, response, type, out var image, out var naturalSize))
            {
                BrowsingContext.DestroyChildNavigable(this);
                SetImage(image, naturalSize);
                document.QueueElementTask(() =>
                {
                    if (generation == _setupGeneration)
                    {
                        FireEvent(new Event("load"));
                    }

                    SetDelayingTheLoadEvent(false);
                });
                return;
            }

            SetImage(null, default);

            // There is no plugin: the type of the content is always null, and the resource is displayed by the content
            // navigable (as in the browsers; the specification displays no plugin)
            if (ContentNavigable is null)
            {
                BrowsingContext.CreateChildNavigable(this);
            }

            EmbeddedResources.Navigate(this, ContentNavigable!, url, response);
            SetDelayingTheLoadEvent(false);
        });
    }

    /// <summary>Sets the image the element displays.</summary>
    private void SetImage(Rendering.PaintImage? image, ImageNaturalSize naturalSize)
    {
        if (image is null && Image is null)
            return;

        Image = image;
        ImageNaturalSize = naturalSize;
        NodeDocument.InvalidateLayout();
    }

    /// <summary>The element delays the load event of its document while its setup steps are queued and its resource is fetched.</summary>
    private void SetDelayingTheLoadEvent(bool value)
    {
        if (value == _delayedDocument is not null)
            return;

        if (value)
        {
            _delayedDocument = NodeDocument;
            _delayedDocument.EmbeddedContentDelayingTheLoadEvent++;
            return;
        }

        var document = _delayedDocument!;
        _delayedDocument = null;
        document.EmbeddedContentDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }
}

/// <summary>What an <c>object</c> element represents (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element).</summary>
internal enum ObjectElementRepresentation
{
    /// <summary>What the element represents is not determined yet (e.g. it is being parsed, or its resource is being fetched).</summary>
    Undetermined,

    /// <summary>The element represents its children (its fallback content).</summary>
    Fallback,

    /// <summary>The element represents its content navigable.</summary>
    ContentNavigable,

    /// <summary>The element represents an image.</summary>
    Image,
}

public partial class HtmlObjectElement
{
    // Incremented each time the steps to (re)determine what the element represents are queued
    private int _updateGeneration;
    private Document? _delayedDocument;

    // Whether the element represents its fallback content because it was not being rendered
    private bool _isFallbackWhileNotRendered;

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#content-navigable</summary>
    internal BrowsingContext? ContentNavigable { get; set; }

    /// <summary>What the element represents.</summary>
    internal ObjectElementRepresentation Representation { get; private set; }

    /// <summary>The natural size of the image the element represents (when <see cref="Representation"/> is <see cref="ObjectElementRepresentation.Image"/>).</summary>
    internal ImageNaturalSize ImageNaturalSize { get; private set; }

    /// <summary>The image the element represents (when <see cref="Representation"/> is <see cref="ObjectElementRepresentation.Image"/>), or null.</summary>
    internal Rendering.PaintImage? Image { get; private set; }

    /// <summary>Whether the element is showing its fallback content (its children).</summary>
    internal bool IsShowingFallbackContent => Representation == ObjectElementRepresentation.Fallback;

    /// <summary>Whether the element is on the stack of open elements of an HTML or XML parser.</summary>
    internal bool IsOnStackOfOpenElements { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-contentdocument</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-contentdocument"/>
    [IdlMember("contentDocument", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-contentdocument")]
    public Document? ContentDocument => NavigableContainers.GetContentDocument(this);

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-contentwindow</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-contentwindow"/>
    [IdlMember("contentWindow", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-contentwindow")]
    public Window? ContentWindow => ContentNavigable?.Window;

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-getsvgdocument</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-getsvgdocument"/>
    [IdlMember("getSVGDocument", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-object-getsvgdocument")]
    public Document? GetSvgDocument() => NavigableContainers.GetSvgDocument(this);

    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        QueueUpdate();
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        _updateGeneration++;
        SetDelayingTheLoadEvent(false);
        BrowsingContext.DestroyChildNavigable(this);
        Representation = ObjectElementRepresentation.Undetermined;
        Image = null;
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null || !IsConnected)
            return;

        // The name attribute only names the content navigable when it is created
        switch (localName)
        {
            case "classid":
            case "data" when !HasAttributeWithLocalName("classid"):
            case "type" when !HasAttributeWithLocalName("classid") && !HasAttributeWithLocalName("data"):
                QueueUpdate();
                break;
        }
    }

    /// <summary>
    /// Determines again what the element represents when it represents its fallback content because it was not being rendered,
    /// and it is now ("whenever [...] the element changes from being rendered to not being rendered, or vice versa"): the
    /// styles of the printing render it (see <see cref="Document.UpdatePrintResources"/>).
    /// </summary>
    internal void UpdateIfRendered()
    {
        if (_isFallbackWhileNotRendered && ComputedStyleDeclarations.IsBeingRendered(this))
        {
            _isFallbackWhileNotRendered = false;
            QueueUpdate();
        }
    }

    /// <summary>Called when the element is popped off the stack of open elements of an HTML or XML parser.</summary>
    internal void OnPoppedOffStackOfOpenElements()
    {
        IsOnStackOfOpenElements = false;
        QueueUpdate();
    }

    /// <summary>
    /// Queues a task to (re)determine what the element represents (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element).
    /// The task (and the fetch of the resource) delays the load event of the document.
    /// </summary>
    internal void QueueUpdate()
    {
        var document = NodeDocument;
        if (document.BrowsingContext is null || !IsConnected)
            return;

        var generation = ++_updateGeneration;
        SetDelayingTheLoadEvent(true);
        document.QueueElementTask(() =>
        {
            if (generation == _updateGeneration)
            {
                DetermineWhatTheElementRepresents(generation);
            }
        });
    }

    private void DetermineWhatTheElementRepresents(int generation)
    {
        var document = NodeDocument;
        _isFallbackWhileNotRendered = false;

        // The element is determined once it is popped off the stack of open elements (the specification shows the fallback
        // content until then, which would load the nested elements)
        if (IsOnStackOfOpenElements && IsConnected)
        {
            SetDelayingTheLoadEvent(false);
            return;
        }

        if (EmbeddedResources.HasAncestorMediaElement(this) || EmbeddedResources.HasAncestorObjectNotShowingFallback(this) || !IsConnected
            || document.BrowsingContext is null || !document.IsFullyActive || !ComputedStyleDeclarations.IsBeingRendered(this))
        {
            Fallback();
            _isFallbackWhileNotRendered = IsConnected && document.BrowsingContext is not null && !ComputedStyleDeclarations.IsBeingRendered(this);
            return;
        }

        if (GetAttributeValue("data") is not { Length: > 0 } data)
        {
            // There is no plugin for the type attribute
            Fallback();
            return;
        }

        if (UrlParser.Parse(data, document.DocumentBaseUrl, document.Encoding) is not { } url)
        {
            FireEvent(new Event("error"));
            Fallback();
            return;
        }

        EmbeddedResources.Fetch(this, url, response =>
        {
            if (generation != _updateGeneration)
                return;

            // The load failed (e.g. a network error, or an HTTP 404 error)
            if (response is null || response.StatusCode is < 200 or >= 400)
            {
                FireEvent(new Event("error"));
                Fallback();
                return;
            }

            var resourceType = DetermineResourceType(response);
            if (resourceType is not null && (MimeType.Parse(resourceType)?.IsXml == true || !resourceType.StartsWith("image/", StringComparison.Ordinal)))
            {
                if (!EmbeddedResources.IsNavigableType(resourceType))
                {
                    Fallback();
                    return;
                }

                if (ContentNavigable is null)
                {
                    BrowsingContext.CreateChildNavigable(this);
                }

                SetRepresentation(ObjectElementRepresentation.ContentNavigable);
                if (BrowsingContext.MatchesAboutBlank(response.Url is { } responseUrl ? UrlParser.Parse(responseUrl, baseUrl: null) ?? url : url))
                {
                    // The initial about:blank document is not replaced: the load event is fired as for a loaded document
                    QueueLoadEvent(generation);
                    return;
                }

                EmbeddedResources.Navigate(this, ContentNavigable!, url, response);
                SetDelayingTheLoadEvent(false);
                return;
            }

            if (resourceType is not null && EmbeddedResources.TryCreateImage(this, url, response, resourceType, out var image, out var naturalSize))
            {
                BrowsingContext.DestroyChildNavigable(this);
                ImageNaturalSize = naturalSize;
                Image = image;
                SetRepresentation(ObjectElementRepresentation.Image);
                QueueLoadEvent(generation);
                return;
            }

            // The type is not supported, or the image cannot be rendered
            Fallback();
        });
    }

    /// <summary>Determines the resource type (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element, step 4.9), or null when it is unknown.</summary>
    private string? DetermineResourceType(DocumentResource response)
    {
        var typeAttribute = GetAttributeValue("type");
        if (response.MimeType is { } contentType)
        {
            var binary = contentType.Essence == "application/octet-stream" || (contentType.Essence == "text/plain" && EmbeddedResources.IsBinary(EmbeddedResources.GetBytes(response)));
            if (!binary)
                return contentType.Essence;

            if (typeAttribute is not null && typeAttribute != "application/octet-stream")
            {
                if (MimeType.Parse(typeAttribute) is { IsXml: false } imageType && imageType.Type == "image")
                    return imageType.Essence;
            }

            return null;
        }

        var tentativeType = typeAttribute ?? EmbeddedResources.SniffType(EmbeddedResources.GetBytes(response));
        return tentativeType != "application/octet-stream" ? tentativeType : null;
    }

    /// <summary>Queues the task that fires the load event once the resource is completely loaded.</summary>
    private void QueueLoadEvent(int generation)
    {
        NodeDocument.QueueElementTask(() =>
        {
            if (generation == _updateGeneration)
            {
                FireEvent(new Event("load"));
            }

            SetDelayingTheLoadEvent(false);
        });
    }

    /// <summary>The fallback step: the element represents its children, and its content navigable is destroyed.</summary>
    private void Fallback()
    {
        BrowsingContext.DestroyChildNavigable(this);
        SetRepresentation(ObjectElementRepresentation.Fallback);
        SetDelayingTheLoadEvent(false);
    }

    /// <summary>
    /// Sets what the element represents. The descendant <c>embed</c> and <c>object</c> elements are updated when the element
    /// changes to or from showing its fallback content (before the load event of the document stops being delayed).
    /// </summary>
    private void SetRepresentation(ObjectElementRepresentation representation)
    {
        var wasShowingFallbackContent = IsShowingFallbackContent;
        Representation = representation;
        if (representation != ObjectElementRepresentation.Image)
        {
            Image = null;
        }

        // The element is a replaced element unless it shows its fallback content (https://html.spec.whatwg.org/multipage/rendering.html#embedded-content-rendering-rules)
        NodeDocument.InvalidateLayout(rebuildBoxTree: wasShowingFallbackContent != IsShowingFallbackContent);
        if (wasShowingFallbackContent == IsShowingFallbackContent)
            return;

        foreach (var node in Descendants())
        {
            switch (node)
            {
                case HtmlEmbedElement embed:
                    embed.UpdatePotentiallyActive();
                    break;
                case HtmlObjectElement obj:
                    obj.QueueUpdate();
                    break;
            }
        }
    }

    /// <summary>The element delays the load event of its document while its task is queued or running and its resource is fetched.</summary>
    private void SetDelayingTheLoadEvent(bool value)
    {
        if (value == _delayedDocument is not null)
            return;

        if (value)
        {
            _delayedDocument = NodeDocument;
            _delayedDocument.EmbeddedContentDelayingTheLoadEvent++;
            return;
        }

        var document = _delayedDocument!;
        _delayedDocument = null;
        document.EmbeddedContentDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }
}
