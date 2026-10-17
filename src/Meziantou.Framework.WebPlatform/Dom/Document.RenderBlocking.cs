using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The render-blocking mechanism (https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism)
public partial class Document
{
    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#render-blocking-element-set (null when it is empty).</summary>
    private List<Element>? _renderBlockingElements;

    /// <summary>
    /// Whether the document can be render-blocked: it is being loaded by a navigation of a browsing context, and the
    /// "implementation-defined timeout" of https://html.spec.whatwg.org/multipage/dom.html#render-blocked has not been
    /// exceeded. The timeout is not a duration: it is exceeded when the document is first rendered (a rendering update finds
    /// it not render-blocked, as the browsers that never block again a document that started to render) or when its readiness
    /// becomes "complete", whichever comes first. The documents that are not loaded by a navigation (the initial about:blank
    /// document, the documents without browsing context) are never render-blocked.
    /// </summary>
    private bool _canBeRenderBlocked;

    /// <summary>The rendering update whose "filter non-renderable documents" step last checked the document, and its result.</summary>
    private long _renderingUpdateChecked = -1;
    private bool _renderingUpdateSkipped;

    /// <summary>Whether a rendering update skipped the document because it was render-blocked: a rendering opportunity is requested once it is no longer render-blocked.</summary>
    private bool _renderingUpdateWasSkipped;

    /// <summary>Whether the render-blocking element set is not empty.</summary>
    internal bool HasRenderBlockingElements => _renderBlockingElements is { Count: > 0 };

    /// <summary>
    /// Whether nodes inserted into the document may change its render-blocked state: an inserted element may be the one a
    /// <c>link rel=expect</c> element waits for, and an inserted body element ends the period during which render-blocking
    /// elements can be added.
    /// </summary>
    internal bool ObservesInsertionsForRenderBlocking => _renderBlockingElements is { Count: > 0 } || _renderingUpdateWasSkipped;

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#allows-adding-render-blocking-elements</summary>
    internal bool AllowsAddingRenderBlockingElements => _canBeRenderBlocked && ContentType == "text/html" && Body is null;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#render-blocked: the render-blocking element set is not empty, or the
    /// document allows adding render-blocking elements. As in the browsers, which start to render a document when its parser
    /// inserts the body element, a document that is being parsed is also render-blocked until its parser inserts the body
    /// element, although a script inserted one before (the specification only considers whether the document has a body
    /// element): a parser-blocking script of the head then keeps blocking the rendering.
    /// </summary>
    internal bool IsRenderBlocked => _canBeRenderBlocked && BrowsingContext is not null
        && (HasRenderBlockingElements || AllowsAddingRenderBlockingElements || (ContentType == "text/html" && ActiveParser is Html.Parsing.HtmlScriptingParser { IsScriptCreated: false, TreeBuilder.HasInsertedBodyElement: false }));

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#render-blocking</summary>
    internal bool IsRenderBlocking(Element element) => _renderBlockingElements is { } elements && elements.Contains(element) && IsRenderBlocked;

    /// <summary>The document starts to be loaded by a navigation: it can be render-blocked until it is rendered or completely loaded.</summary>
    internal void StartRenderBlockingPeriod() => _canBeRenderBlocked = true;

    /// <summary>The timeout of the render-blocking mechanism is exceeded (see <see cref="_canBeRenderBlocked"/>): the document is never render-blocked again.</summary>
    internal void EndRenderBlockingPeriod()
    {
        if (!_canBeRenderBlocked)
            return;

        _canBeRenderBlocked = false;
        _renderBlockingElements = null;
        RequestSkippedRenderingUpdate();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#block-rendering</summary>
    internal void BlockRendering(Element element)
    {
        if (!AllowsAddingRenderBlockingElements || BrowsingContext is null)
            return;

        _renderBlockingElements ??= [];
        if (!_renderBlockingElements.Contains(element))
        {
            _renderBlockingElements.Add(element);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#unblock-rendering</summary>
    internal void UnblockRendering(Element element)
    {
        if (_renderBlockingElements is { } elements && elements.Remove(element))
        {
            RequestSkippedRenderingUpdate();
        }
    }

    /// <summary>
    /// "Filter non-renderable documents" of https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering:
    /// returns whether the rendering update skips the document because it is render-blocked. The answer is the same for all
    /// the steps of a rendering update. A document that is not skipped is rendered: it is never render-blocked again.
    /// </summary>
    internal bool IsSkippedByRenderingUpdate(long renderingUpdate)
    {
        if (!_canBeRenderBlocked)
            return false;

        if (_renderingUpdateChecked != renderingUpdate)
        {
            _renderingUpdateChecked = renderingUpdate;
            _renderingUpdateSkipped = IsRenderBlocked;
            if (_renderingUpdateSkipped)
            {
                _renderingUpdateWasSkipped = true;
            }
            else if (BrowsingContext is not null)
            {
                // This rendering update renders the document: no other one is needed for the steps that waited
                _renderingUpdateWasSkipped = false;
                EndRenderBlockingPeriod();
            }
        }

        return _renderingUpdateSkipped;
    }

    /// <summary>Requests the rendering update that was skipped while the document was render-blocked, once it is no longer render-blocked.</summary>
    internal void RequestSkippedRenderingUpdate()
    {
        if (_renderingUpdateWasSkipped && !IsRenderBlocked)
        {
            _renderingUpdateWasSkipped = false;
            EventLoop?.RequestRenderingUpdate();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#process-internal-resource-links: the <c>link rel=expect</c> elements
    /// of the render-blocking element set are processed again. When <paramref name="element"/> is not null, only the links
    /// whose expected element may be <paramref name="element"/> are (an element that was inserted or popped off the stack of
    /// open elements of the parser: the other links cannot have changed).
    /// </summary>
    internal void ProcessInternalResourceLinks(Element? element = null)
    {
        if (_renderBlockingElements is not { Count: > 0 } elements)
            return;

        foreach (var blockingElement in elements.ToArray())
        {
            if (blockingElement is HtmlLinkElement link && link.IsExpectLink && (element is null || link.MayExpect(element)))
            {
                link.ProcessInternalResourceLink();
            }
        }
    }

    /// <summary>
    /// Nodes were inserted into the document while <see cref="ObservesInsertionsForRenderBlocking"/>: the insertion steps of
    /// https://html.spec.whatwg.org/multipage/links.html#link-type-expect for the elements that are not on the stack of open
    /// elements of the parser, and the rendering update that waits for the body element.
    /// </summary>
    internal void OnNodeInsertedForRenderBlocking(Node node)
    {
        if (HasRenderBlockingElements && node is Element root && !IsOnStackOfOpenElements(root))
        {
            for (Node? current = root; current is not null && HasRenderBlockingElements; current = current.GetNextInTreeOrder(root))
            {
                if (current is Element element)
                {
                    ProcessInternalResourceLinks(element);
                }
            }
        }

        RequestSkippedRenderingUpdate();
    }

    /// <summary>
    /// Whether the blocking tokens set of an element (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#blocking-tokens-set)
    /// contains "render": its <c>blocking</c> attribute makes it potentially render-blocking.
    /// </summary>
    internal static bool HasRenderBlockingToken(Element element)
    {
        if (element.GetAttributeValue("blocking") is not { Length: > 0 } value)
            return false;

        foreach (var token in value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Infra.InfraAscii.EqualsIgnoreAsciiCase(token, "render"))
                return true;
        }

        return false;
    }

    /// <summary>Whether the element is on the stack of open elements of the HTML parser of the document (an element that the parser is inserting is about to be pushed onto it).</summary>
    internal bool IsOnStackOfOpenElements(Element element) => ActiveParser is Html.Parsing.HtmlScriptingParser parser && parser.TreeBuilder.IsOnStackOfOpenElements(element);
}
