using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#concept-document-mode</summary>
internal enum DocumentMode : byte
{
    NoQuirks,
    Quirks,
    LimitedQuirks,
}

/// <summary>https://dom.spec.whatwg.org/#interface-document</summary>
/// <seealso href="https://dom.spec.whatwg.org/#document"/>
[IdlInterface("Document", "https://dom.spec.whatwg.org/#document")]
public partial class Document : Node
{
    [ThreadStatic]
    private static Document? s_currentGlobalDocument;

    /// <summary>
    /// The associated document of the entry global object (https://html.spec.whatwg.org/multipage/webappapis.html#entry):
    /// the global object of the script or callback that started the current JavaScript execution, set by the script engines.
    /// </summary>
    internal static Document? EntryGlobalDocument
    {
        get => s_entryGlobalDocument;
        set => s_entryGlobalDocument = value;
    }

    [ThreadStatic]
    private static Document? s_entryGlobalDocument;

    private static readonly UrlRecord AboutBlank = UrlParser.Parse("about:blank", baseUrl: null)!;

    private DomImplementation? _implementation;
    private Document? _templateContentsOwnerDocument;
    private int _domVersion;

    /// <summary>https://dom.spec.whatwg.org/#dom-document-document</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-document"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-document-document")]
    public Document()
        : this(isHtmlDocument: false, contentType: "application/xml")
    {
        // The origin is the one of the current global object's associated document, which is in the same realm
        OriginDocument = GetOriginDocument(CurrentGlobalDocument);
        RealmDocument = CurrentGlobalDocument;
    }

    internal static Document GetOriginDocument(Document document) => document.OriginDocument ?? document;

    internal Document(bool isHtmlDocument, string contentType)
        : base(nodeDocument: null)
    {
        NodeDocument = this;
        IsHtmlDocument = isHtmlDocument;
        ContentType = contentType;
        UrlRecord = AboutBlank;
    }

    /// <summary>The window of the current global object's associated document, if any (without creating the blank document).</summary>
    internal static Window? CurrentGlobalWindow => s_currentGlobalDocument?.RelevantWindow;

    /// <summary>
    /// The document used by constructors such as <c>new Text()</c>, which use the "current global object's associated Document".
    /// There is no JavaScript realm, so a blank HTML document is created per thread.
    /// </summary>
    internal static Document CurrentGlobalDocument
    {
        get => s_currentGlobalDocument ??= CreateHtmlDocument();
        set => s_currentGlobalDocument = value;
    }

    /// <summary>The associated document of the current global object, without creating the blank document (to save and restore it).</summary>
    internal static Document? CurrentGlobalDocumentOrNull
    {
        get => s_currentGlobalDocument;
        set => s_currentGlobalDocument = value;
    }

    internal static Document CreateHtmlDocument() => new(isHtmlDocument: true, contentType: "text/html");

    /// <summary>Gets the node type of a document, which is <c>9</c> (<c>DOCUMENT_NODE</c>).</summary>
    public override ushort NodeType => DocumentNode;

    /// <summary>Gets the node name of a document, which is <c>#document</c>.</summary>
    public override string NodeName => "#document";

    /// <summary>https://dom.spec.whatwg.org/#html-document</summary>
    internal bool IsHtmlDocument { get; }

    /// <summary>The IDs of the elements of the document tree, created on the first getElementById() call.</summary>
    internal IdMap? IdMap { get; set; }

    /// <summary>Whether an ID map exists for the document tree or one of its shadow trees (the mutation algorithms then maintain it).</summary>
    internal bool HasIdMaps { get; set; }

    /// <summary>Whether a shadow root was attached to an element of the document (the style engine then looks for the @property rules of the shadow trees).</summary>
    internal bool HasShadowRoots { get; set; }

    /// <summary>
    /// Whether an element of the document has a user agent shadow tree (the summary that the user agent provides for a details
    /// element, see <see cref="HtmlDetailsElement.ImplicitSummary"/>): the events dispatched in it are retargeted. Unlike
    /// <see cref="HasShadowRoots"/>, it does not make the style engine and the box tree builder take their general paths (the
    /// user agent shadow trees have no style sheets, and their elements are invalidated with their host).
    /// </summary>
    internal bool HasUserAgentShadowRoots { get; set; }

    /// <summary>Whether a mutation observer was ever registered on a node of the document (queuing mutation records walks the inclusive ancestors of the target, which is skipped otherwise).</summary>
    internal bool HasMutationObservers { get; set; }

    /// <summary>Whether an HTML base element was ever created in the document (the document base URL is the fallback base URL otherwise, without looking for a base element).</summary>
    internal bool HasBaseElements { get; set; }

    /// <summary>
    /// Whether a name attribute was ever set on an HTML embed, form, img or object element of the document: they are the
    /// elements that are named properties of the window by name (https://html.spec.whatwg.org/multipage/nav-history-apis.html#named-access-on-the-window-object).
    /// </summary>
    internal bool HasNamedObjectElements { get; set; }

    /// <summary>
    /// Whether a name attribute was ever set on an HTML embed, form, iframe, img or object element of the document, or an id
    /// attribute on an object element: they are the elements that can be named elements of the document
    /// (https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem-filter). A document without them has no
    /// named property, which the bindings check on each property access.
    /// </summary>
    internal bool HasNamedItemCandidates { get; set; }

    /// <summary>The named elements of the document tree by name, created on the first property access once the document has candidate elements.</summary>
    internal DocumentNamedItemMap? NamedItemMap { get; set; }


    /// <summary>https://dom.spec.whatwg.org/#concept-document-mode</summary>
    internal DocumentMode Mode { get; set; }

    /// <summary>https://dom.spec.whatwg.org/#concept-document-encoding</summary>
    internal WebEncodingId Encoding { get; set; } = WebEncodingId.Utf8;

    /// <summary>https://dom.spec.whatwg.org/#concept-document-url</summary>
    internal UrlRecord UrlRecord { get; set; }

    /// <summary>https://html.spec.whatwg.org/#document-base-url</summary>
    private UrlRecord? _baseUrlCache;
    private int _baseUrlCacheVersion = -1;

    /// <summary>
    /// The base URL of the document this document is a clone of, when it was cloned: a clone keeps the base URL of its
    /// source while it has no base element (https://github.com/whatwg/dom/issues/454).
    /// </summary>
    private UrlRecord? _clonedBaseUrl;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#document-base-url: the frozen base URL of the first
    /// base element with an href attribute, or the fallback base URL (the base URL of the source for a clone).
    /// </summary>
    internal UrlRecord DocumentBaseUrl
    {
        get
        {
            // The cache is invalidated by every mutation of the document: looking for a base element walks the whole document
            // when there is none, so it is skipped when no base element can be in the document
            if (!HasBaseElements)
                return _clonedBaseUrl ?? FallbackBaseUrl;

            if (_baseUrlCache is not null && _baseUrlCacheVersion == DomVersion)
                return _baseUrlCache;

            // A base element with an href attribute replaces the base URL of the source of a clone, even when its URL is ignored
            var fallbackBaseUrl = FallbackBaseUrl;
            var result = _clonedBaseUrl ?? fallbackBaseUrl;
            for (var node = FirstChild; node is not null; node = node.GetNextInTreeOrder(this))
            {
                if (node is Element element && element.IsHtml(TagId.Base) && element.GetAttributeValue("href") is { } href)
                {
                    // https://html.spec.whatwg.org/multipage/semantics.html#set-the-frozen-base-url: a data: or javascript:
                    // URL is ignored, and so is a URL that the base-uri directive does not allow
                    var urlRecord = UrlParser.Parse(href, fallbackBaseUrl, Encoding);
                    result = urlRecord is not null && urlRecord.Scheme is not ("data" or "javascript") && IsBaseAllowed(element, href, urlRecord) ? urlRecord : fallbackBaseUrl;
                    break;
                }
            }

            _baseUrlCache = result;
            _baseUrlCacheVersion = DomVersion;
            return result;
        }
    }

    // The result of the last "Is base allowed for Document?" check: the frozen base URL is set once, when the base element
    // becomes the first one with an href attribute or when its href attribute changes
    private (Element Element, string Href, bool Allowed) _baseAllowedCheck;

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#allow-base-for-document, run when the frozen base URL of <paramref name="element"/>
    /// is set (https://html.spec.whatwg.org/multipage/semantics.html#set-the-frozen-base-url): the result is kept until the
    /// first base element or its <c>href</c> attribute changes, so that the violation is reported once.
    /// </summary>
    private bool IsBaseAllowed(Element element, string href, UrlRecord url)
    {
        if (ReferenceEquals(_baseAllowedCheck.Element, element) && _baseAllowedCheck.Href == href)
            return _baseAllowedCheck.Allowed;

        var allowed = ContentSecurityPolicy.GetInlineCheckDocument(element) is null || ContentSecurityPolicy.IsBaseAllowed(url, this);
        _baseAllowedCheck = (element, href, allowed);
        return allowed;
    }

    /// <summary>https://html.spec.whatwg.org/#parser-cannot-change-the-mode-flag</summary>
    internal bool ParserCannotChangeTheMode { get; set; }

    /// <summary>Whether scripting is enabled for the document (https://html.spec.whatwg.org/#concept-n-script).</summary>
    internal bool ScriptingEnabled { get; set; }

    /// <summary>
    /// The allow declarative shadow roots flag (https://html.spec.whatwg.org/multipage/dom.html#concept-document-allow-declarative-shadow-roots),
    /// set for the documents parsed with declarative shadow roots and the documents of a browsing context.
    /// </summary>
    internal bool AllowDeclarativeShadowRoots
    {
        get => field || BrowsingContext is not null;
        set;
    }

    /// <summary>Incremented on every mutation of the tree; used to invalidate caches (live collections, XPath iterators).</summary>
    internal int DomVersion => _domVersion;

    /// <summary>
    /// The version of the style sheets of the document, changed when one of them changes through the CSSOM (see
    /// <see cref="Css.Cascade.CssStyleVersion"/>).
    /// </summary>
    internal int StyleSheetsVersion { get; set; } = Css.Cascade.CssStyleVersion.Next();

    /// <summary>
    /// Increments <see cref="DomVersion"/> for a change that <see cref="StyleInvalidations"/> does not describe: all the computed
    /// styles are invalidated.
    /// </summary>
    internal void IncrementDomVersion()
    {
        StyleInvalidations.SetFull();
        IncrementDomVersionCore();
    }

    private void IncrementDomVersionCore()
    {
        _domVersion++;
        RequestFocusFixupIfNeeded();
        RequestCssResourcesUpdate();
    }

    /// <summary>
    /// Incremented when a node is inserted in or removed from a tree of the document (not when an attribute or a text
    /// changes): used to invalidate the caches that only depend on the structure of the trees (e.g. getElementsByTagName()).
    /// </summary>
    internal int TreeVersion { get; private set; }

    /// <summary>Increments <see cref="TreeVersion"/> and <see cref="DomVersion"/>, when a node is inserted in or removed from a parent.</summary>
    internal void IncrementTreeVersion(Node parent, Node child, bool removed)
    {
        TreeVersion++;
        if (parent.IsConnected)
        {
            StyleInvalidations.Add(new StyleInvalidation(removed ? StyleInvalidationKind.ChildRemoved : StyleInvalidationKind.ChildInserted, parent, child));
            if (removed && child is Element)
            {
                OnElementRemovedForStyles(parent, child);
            }
        }

        IncrementDomVersionCore();
    }

    /// <summary>
    /// Incremented when the state of an element that selectors can match changes without a mutation of the tree (e.g. the
    /// checkedness or the value of a form control, a modal dialog, a popover): used to invalidate the computed styles.
    /// </summary>
    internal int ElementStateVersion { get; private set; }

    /// <summary>
    /// Increments <see cref="ElementStateVersion"/> for a change that <see cref="StyleInvalidations"/> does not describe: all the
    /// computed styles are invalidated.
    /// </summary>
    internal void IncrementElementStateVersion()
    {
        StyleInvalidations.SetFull();
        IncrementElementStateVersionCore();
    }

    private void IncrementElementStateVersionCore()
    {
        ElementStateVersion++;
        RequestFocusFixupIfNeeded();
        RequestCssResourcesUpdate();
    }

    /// <summary>Whether a rendering opportunity is scheduled to run the focus fixup rule.</summary>
    internal bool FocusFixupRequested { get; set; }

    /// <summary>
    /// When an element is focused, a change of the tree or of the state of an element can make it not focusable (hidden,
    /// disabled, inert...): the focus fixup rule runs at the next rendering opportunity
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#focus-fixup-rule).
    /// </summary>
    private void RequestFocusFixupIfNeeded()
    {
        if (FocusedArea is null || FocusFixupRequested || EventLoop is not { } eventLoop)
            return;

        FocusFixupRequested = true;
        eventLoop.RequestRenderingOpportunity(this);
    }

    // Live ranges and node iterators (https://dom.spec.whatwg.org/#concept-live-range). Weak references are used so that
    // ranges and iterators that are no longer referenced do not slow down the mutation algorithms forever.
    // They are shared by all the documents of the thread, as the boundary points of a range and the root of an
    // iterator can move to another document when a node is adopted.
    [ThreadStatic]
    private static List<WeakReference<Range>>? s_liveRangeRegistry;

    [ThreadStatic]
    private static List<WeakReference<NodeIterator>>? s_nodeIteratorRegistry;

    private static List<WeakReference<Range>> LiveRangeRegistry => s_liveRangeRegistry ??= [];
    private static List<WeakReference<NodeIterator>> NodeIteratorRegistry => s_nodeIteratorRegistry ??= [];

    internal bool HasLiveRanges => LiveRangeRegistry.Count > 0;

    internal bool HasNodeIterators => NodeIteratorRegistry.Count > 0;

    internal void RegisterLiveRange(Range range) => LiveRangeRegistry.Add(new WeakReference<Range>(range));

    internal void UnregisterLiveRange(Range range) => LiveRangeRegistry.RemoveAll(r => !r.TryGetTarget(out var target) || ReferenceEquals(target, range));

    internal void RegisterNodeIterator(NodeIterator iterator) => NodeIteratorRegistry.Add(new WeakReference<NodeIterator>(iterator));

    internal IEnumerable<Range> GetLiveRanges()
    {
        var hasDeadReferences = false;
        foreach (var reference in LiveRangeRegistry.ToArray())
        {
            if (reference.TryGetTarget(out var range))
            {
                yield return range;
            }
            else
            {
                hasDeadReferences = true;
            }
        }

        if (hasDeadReferences)
        {
            LiveRangeRegistry.RemoveAll(r => !r.TryGetTarget(out _));
        }
    }

    /// <summary>
    /// Insert steps of the live ranges: https://dom.spec.whatwg.org/#concept-node-insert (step 5), for nodes inserted before
    /// <paramref name="child"/>.
    /// </summary>
    internal void UpdateLiveRangesForInsertion(Node parent, Node child, int count)
    {
        // The index walks the previous siblings: it is computed only for a range that has a boundary point in the parent. The
        // registry is shared by the documents of the thread, so it often contains only ranges of other documents.
        var childIndex = -1;
        foreach (var range in GetLiveRanges())
        {
            if (!range.IsAffectedByInsertion(parent))
                continue;

            if (childIndex < 0)
            {
                childIndex = child.GetIndex();
            }

            range.OnInsertion(parent, childIndex, count);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#live-range-pre-remove-steps</summary>
    internal void LiveRangePreRemoveSteps(Node node)
    {
        var parent = node.ParentNode!;

        // The index walks the previous siblings: removing the children one by one would be quadratic if it was computed when
        // no range has a boundary point in the parent or in the removed subtree
        var index = -1;
        foreach (var range in GetLiveRanges())
        {
            if (!range.IsAffectedByRemoval(node, parent))
                continue;

            if (index < 0)
            {
                index = node.GetIndex();
            }

            range.OnPreRemove(node, parent, index);
        }
    }

    /// <summary>Updates the live ranges when the data of a CharacterData node is replaced.</summary>
    internal void UpdateLiveRangesForReplaceData(Node node, uint offset, uint count, uint dataLength)
    {
        foreach (var range in GetLiveRanges())
        {
            range.OnReplaceData(node, offset, count, dataLength);
        }
    }

    internal IEnumerable<Range> GetLiveRangesSnapshot() => GetLiveRanges().ToList();

    /// <summary>https://dom.spec.whatwg.org/#nodeiterator-pre-removing-steps</summary>
    internal void NodeIteratorPreRemoveSteps(Node node)
    {
        var registry = NodeIteratorRegistry;
        foreach (var reference in registry.ToArray())
        {
            if (!reference.TryGetTarget(out var iterator))
            {
                registry.Remove(reference);
                continue;
            }

            if (ReferenceEquals(iterator.Root.NodeDocument, node.NodeDocument))
            {
                iterator.PreRemovingSteps(node);
            }
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#document-custom-element-registry</summary>
    internal CustomElementRegistry? CustomElementRegistryStorage { get; set; }

    /// <summary>https://dom.spec.whatwg.org/#dom-documentorshadowroot-customelementregistry</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-documentorshadowroot-customelementregistry"/>
    [IdlMember("customElementRegistry", "https://dom.spec.whatwg.org/#dom-documentorshadowroot-customelementregistry")]
    public CustomElementRegistry? CustomElementRegistry => CustomElementRegistryStorage;

    /// <summary>Whether a slot element was ever created in or adopted into this document (used to skip the slot algorithms).</summary>
    internal bool HasSlots { get; set; }

    /// <summary>Gets the <c>DOMImplementation</c> object associated with the document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-implementation"/>
    [IdlMember("implementation", "https://dom.spec.whatwg.org/#dom-document-implementation")]
    public DomImplementation Implementation => _implementation ??= new DomImplementation(this);

    /// <summary>Gets the serialized URL of the document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-url"/>
    [IdlMember("URL", "https://dom.spec.whatwg.org/#dom-document-url")]
    public string Url => UrlRecord.Serialize();

    /// <summary>Gets the serialized URL of the document (alias of <c>URL</c>).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-documenturi"/>
    [IdlMember("documentURI", "https://dom.spec.whatwg.org/#dom-document-documenturi")]
    public string DocumentUri => UrlRecord.Serialize();

    /// <summary>Gets <c>BackCompat</c> when the document is in quirks mode, otherwise <c>CSS1Compat</c>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-compatmode"/>
    [IdlMember("compatMode", "https://dom.spec.whatwg.org/#dom-document-compatmode")]
    public string CompatMode => Mode == DocumentMode.Quirks ? "BackCompat" : "CSS1Compat";

    /// <summary>Gets the name of the character encoding of the document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-characterset"/>
    [IdlMember("characterSet", "https://dom.spec.whatwg.org/#dom-document-characterset")]
    public string CharacterSet => WebEncodingRegistry.GetName(Encoding);

    /// <summary>Gets the name of the character encoding of the document (alias of <c>characterSet</c>).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-charset"/>
    [IdlMember("charset", "https://dom.spec.whatwg.org/#dom-document-charset")]
    public string Charset => CharacterSet;

    /// <summary>Gets the name of the character encoding of the document (alias of <c>characterSet</c>).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-inputencoding"/>
    [IdlMember("inputEncoding", "https://dom.spec.whatwg.org/#dom-document-inputencoding")]
    public string InputEncoding => CharacterSet;

    /// <summary>Gets the content type (MIME type) of the document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-contenttype"/>
    [IdlMember("contentType", "https://dom.spec.whatwg.org/#dom-document-contenttype")]
    public string ContentType { get; internal set; }

    /// <summary>Gets the document type node that is a child of the document, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-doctype"/>
    [IdlMember("doctype", "https://dom.spec.whatwg.org/#dom-document-doctype")]
    public DocumentType? Doctype
    {
        get
        {
            for (var child = FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is DocumentType doctype)
                    return doctype;
            }

            return null;
        }
    }

    /// <summary>Gets the root element of the document, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-documentelement"/>
    [IdlMember("documentElement", "https://dom.spec.whatwg.org/#dom-document-documentelement")]
    public Element? DocumentElement
    {
        get
        {
            for (var child = FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is Element element)
                    return element;
            }

            return null;
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createelement</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createelement"/>
    [IdlMember("createElement", "https://dom.spec.whatwg.org/#dom-document-createelement")]
    public Element CreateElement(string localName, ElementCreationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(localName);
        if (!XmlNames.IsValidElementLocalName(localName))
            throw DomException.InvalidCharacter($"'{localName}' is not a valid element name");

        if (IsHtmlDocument)
        {
            localName = InfraAscii.ToAsciiLowercase(localName);
        }

        using var scope = CEReactionsScope.Enter();
        var @namespace = IsHtmlDocument || ContentType == "application/xhtml+xml" ? Namespaces.Html : null;
        var (registry, isValue) = FlattenElementCreationOptions(options);
        return ElementFactory.CreateElement(this, localName, @namespace, prefix: null, isValue, synchronousCustomElements: true, registry);
    }

    /// <summary>
    /// Creates the HTML element whose element interface is <typeparamref name="T"/>, such as <c>a</c> for
    /// <see cref="HtmlAnchorElement"/>. Use <see cref="CreateElement{T}(string, ElementCreationOptions?)"/> for the
    /// interfaces of several elements, such as <see cref="HtmlHeadingElement"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not the element interface of exactly one element.</exception>
    /// <exception cref="ArgumentException">The document does not create HTML elements (an XML document).</exception>
    [NotInIdl("Typed helper")]
    public T CreateElement<T>(ElementCreationOptions? options = null) where T : HtmlElement
    {
        var localNames = ElementFactory.GetHtmlLocalNames(typeof(T));
        if (localNames.Length != 1)
        {
            var reason = localNames.Length == 0 ? "is not the element interface of a single element" : $"is the element interface of several elements ({string.Join(", ", localNames)})";
            throw new InvalidOperationException($"{typeof(T).Name} {reason}. Use CreateElement<{typeof(T).Name}>(localName) instead.");
        }

        return CreateElement<T>(localNames[0], options);
    }

    /// <summary>Creates an element, as <see cref="CreateElement(string, ElementCreationOptions?)"/> does, and returns it as a <typeparamref name="T"/>.</summary>
    /// <exception cref="ArgumentException">The created element is not a <typeparamref name="T"/>.</exception>
    [NotInIdl("Typed helper")]
    public T CreateElement<T>(string localName, ElementCreationOptions? options = null) where T : Element
    {
        var element = CreateElement(localName, options);
        if (element is T result)
            return result;

        throw new ArgumentException($"The element '{element.LocalName}' is a {element.GetType().Name}, not a {typeof(T).Name}", nameof(localName));
    }

    /// <summary>https://dom.spec.whatwg.org/#flatten-element-creation-options</summary>
    private (CustomElementRegistry? Registry, string? IsValue) FlattenElementCreationOptions(ElementCreationOptions? options)
    {
        var registry = CustomElementRegistryStorage;
        var isValue = options?.Is;
        if (options is { HasCustomElementRegistry: true })
        {
            if (isValue is not null)
                throw DomException.NotSupported("The is and customElementRegistry options cannot be used together");

            registry = options.CustomElementRegistry;
        }

        if (registry is { IsScoped: false } && !ReferenceEquals(registry, CustomElementRegistryStorage))
            throw DomException.NotSupported("The registry is the global registry of another document");

        return (registry, isValue);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createelementns</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createelementns"/>
    [IdlMember("createElementNS", "https://dom.spec.whatwg.org/#dom-document-createelementns")]
    public Element CreateElementNS(string? @namespace, string qualifiedName, ElementCreationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var (ns, prefix, localName) = XmlNames.ValidateAndExtract(@namespace, qualifiedName, isElement: true);
        using var scope = CEReactionsScope.Enter();
        var (registry, isValue) = FlattenElementCreationOptions(options);
        return ElementFactory.CreateElement(this, localName, ns, prefix, isValue, synchronousCustomElements: true, registry);
    }

    /// <summary>Creates an empty document fragment owned by the document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createdocumentfragment"/>
    [IdlMember("createDocumentFragment", "https://dom.spec.whatwg.org/#dom-document-createdocumentfragment")]
    public DocumentFragment CreateDocumentFragment() => new(this);

    /// <summary>Creates a text node with the given data.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createtextnode"/>
    [IdlMember("createTextNode", "https://dom.spec.whatwg.org/#dom-document-createtextnode")]
    public Text CreateTextNode(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new Text(this, data);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createcdatasection</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createcdatasection"/>
    [IdlMember("createCDATASection", "https://dom.spec.whatwg.org/#dom-document-createcdatasection")]
    public CDataSection CreateCDataSection(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (IsHtmlDocument)
            throw DomException.NotSupported("CDATA sections are not supported in HTML documents");

        if (data.Contains("]]>", StringComparison.Ordinal))
            throw DomException.InvalidCharacter("The data cannot contain ']]>'");

        return new CDataSection(this, data);
    }

    /// <summary>Creates a comment node with the given data.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createcomment"/>
    [IdlMember("createComment", "https://dom.spec.whatwg.org/#dom-document-createcomment")]
    public Comment CreateComment(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new Comment(this, data);
    }

    /// <summary>Creates a processing instruction with the given target and data.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createprocessinginstruction"/>
    [IdlMember("createProcessingInstruction", "https://dom.spec.whatwg.org/#dom-document-createprocessinginstruction")]
    public ProcessingInstruction CreateProcessingInstruction(string target, string data) => new(this, target, data, validate: true);

    /// <summary>https://dom.spec.whatwg.org/#dom-document-importnode</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-importnode"/>
    [IdlMember("importNode", "https://dom.spec.whatwg.org/#dom-document-importnode")]
    public Node ImportNode(Node node, bool subtree = false) => ImportNodeCore(node, subtree, registry: null);

    /// <summary>https://dom.spec.whatwg.org/#dom-document-importnode</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-importnode"/>
    [IdlMember("importNode", "https://dom.spec.whatwg.org/#dom-document-importnode")]
    public Node ImportNode(Node node, ImportNodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ImportNodeCore(node, subtree: !options.SelfOnly, options.CustomElementRegistry);
    }

    private Node ImportNodeCore(Node node, bool subtree, CustomElementRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var scope = CEReactionsScope.Enter();
        if (node is Document or ShadowRoot)
            throw DomException.NotSupported("Documents and shadow roots cannot be imported");

        if (registry is { IsScoped: false } && !ReferenceEquals(registry, CustomElementRegistryStorage))
            throw DomException.NotSupported("The registry is the global registry of another document");

        registry ??= CustomElementRegistryStorage;
        return node.Clone(this, subtree, parent: null, fallbackRegistry: registry);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-adoptnode</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-adoptnode"/>
    [IdlMember("adoptNode", "https://dom.spec.whatwg.org/#dom-document-adoptnode")]
    public Node AdoptNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is Document)
            throw DomException.NotSupported("Documents cannot be adopted");

        if (node is ShadowRoot)
            throw DomException.HierarchyRequest("Shadow roots cannot be adopted");

        using var scope = CEReactionsScope.Enter();
        Adopt(node);
        return node;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createattribute</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createattribute"/>
    [IdlMember("createAttribute", "https://dom.spec.whatwg.org/#dom-document-createattribute")]
    public Attr CreateAttribute(string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        if (!XmlNames.IsValidAttributeLocalName(localName))
            throw DomException.InvalidCharacter($"'{localName}' is not a valid attribute name");

        if (IsHtmlDocument)
        {
            localName = InfraAscii.ToAsciiLowercase(localName);
        }

        return new Attr(this, localName, @namespace: null, prefix: null, "");
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createattributens</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-document-createattributens"/>
    [IdlMember("createAttributeNS", "https://dom.spec.whatwg.org/#dom-document-createattributens")]
    public Attr CreateAttributeNS(string? @namespace, string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var (ns, prefix, localName) = XmlNames.ValidateAndExtract(@namespace, qualifiedName, isElement: false);
        return new Attr(this, localName, ns, prefix, "");
    }

    /// <summary>
    /// Returns the first element of the document, in tree order, with the given ID, or null when there is none.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-nonelementparentnode-getelementbyid"/>
    [IdlMember("getElementById", "https://dom.spec.whatwg.org/#dom-nonelementparentnode-getelementbyid")]
    public Element? GetElementById(string elementId) => GetElementByIdCore(elementId);

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#the-document-object:get-the-parent</summary>
    internal override EventTarget? GetTheParent(Event @event) => @event.Type == "load" || BrowsingContext is null ? null : RelevantWindow;

    /// <summary>https://dom.spec.whatwg.org/#concept-node-adopt</summary>
    internal void Adopt(Node node)
    {
        var oldDocument = node.NodeDocument;
        if (ReferenceEquals(oldDocument, this))
        {
            node.ParentNode?.RemoveChildCore(node, suppressObservers: false);
            return;
        }

        using var scope = CEReactionsScope.Enter();
        if (node.ParentNode is not null)
        {
            node.ParentNode.RemoveChildCore(node, suppressObservers: false);

            // The unload event handlers of the child navigables of the node inserted it again (see Node.RemoveCore)
            if (node.ParentNode is not null)
                return;
        }

        // The shadow roots of the adopted nodes keep their ID maps, which the new document must maintain
        if (oldDocument.HasIdMaps)
        {
            HasIdMaps = true;
        }

        if (oldDocument.HasShadowRoots)
        {
            HasShadowRoots = true;
        }

        if (oldDocument.HasUserAgentShadowRoots)
        {
            HasUserAgentShadowRoots = true;
        }

        // The adopted nodes keep their registered observers
        if (oldDocument.HasMutationObservers)
        {
            HasMutationObservers = true;
        }

        if (oldDocument.HasBaseElements)
        {
            HasBaseElements = true;
        }

        if (oldDocument.HasNamedObjectElements)
        {
            HasNamedObjectElements = true;
        }

        if (oldDocument.HasNamedItemCandidates)
        {
            HasNamedItemCandidates = true;
        }

        var nodes = node.ShadowIncludingInclusiveDescendants().ToList();
        var hasRegistries = oldDocument.CustomElementRegistryStorage is not null || CustomElementRegistryStorage is not null;
        var oldRealmWindow = oldDocument.RealmWindow;
        if (ReferenceEquals(oldRealmWindow, RealmWindow))
        {
            oldRealmWindow = null;
        }

        foreach (var inclusiveDescendant in nodes)
        {
            // The node keeps the realm where it was created
            if (oldRealmWindow is not null)
            {
                inclusiveDescendant.KeepCreationRealm(oldRealmWindow);
            }

            // The registry of the old document is not the registry of the new document: the element stores its registry
            var registry = hasRegistries && inclusiveDescendant is Element element ? element.CustomElementRegistry : null;
            inclusiveDescendant.NodeDocument = this;
            if (!hasRegistries)
                continue;

            if (inclusiveDescendant is ShadowRoot shadowRoot)
            {
                if ((shadowRoot.CustomElementRegistryStorage is null && !shadowRoot.KeepCustomElementRegistryNull) || CustomElementRegistry.IsGlobal(shadowRoot.CustomElementRegistryStorage))
                {
                    shadowRoot.CustomElementRegistryStorage = CustomElementRegistry.GetEffectiveGlobal(CustomElementRegistryStorage);
                }
            }
            else if (inclusiveDescendant is Element adoptedElement)
            {
                if (registry is null || !registry.IsScoped)
                {
                    var parent = adoptedElement.ParentNode;
                    var newRegistry = registry is not null || parent is null or (DocumentFragment and not ShadowRoot)
                        ? CustomElementRegistryStorage
                        : CustomElementRegistry.LookUp(parent);
                    registry = CustomElementRegistry.GetEffectiveGlobal(newRegistry);
                }

                adoptedElement.CustomElementRegistry = registry;
            }
        }

        foreach (var inclusiveDescendant in nodes)
        {
            if (inclusiveDescendant is Element { IsCustom: true } customElement)
            {
                CustomElementReactions.EnqueueCallbackReaction(customElement, CustomElementCallbackKind.Adopted, [oldDocument, this]);
            }

            inclusiveDescendant.AdoptingSteps(oldDocument);
        }
    }

    /// <summary>https://html.spec.whatwg.org/#appropriate-template-contents-owner-document</summary>
    internal Document GetAppropriateTemplateContentsOwnerDocument()
    {
        if (IsTemplateContentsOwnerDocument)
            return this;

        if (_templateContentsOwnerDocument is null)
        {
            _templateContentsOwnerDocument = new Document(IsHtmlDocument, IsHtmlDocument ? "text/html" : ContentType)
            {
                IsTemplateContentsOwnerDocument = true,
                RealmDocument = this,
            };
        }

        return _templateContentsOwnerDocument;
    }

    internal bool IsTemplateContentsOwnerDocument { get; private init; }

    /// <summary>Whether the document is the template contents owner document of <paramref name="document"/>.</summary>
    internal bool IsTemplateContentsOwnerDocumentOf(Document document) => ReferenceEquals(document._templateContentsOwnerDocument, this);

    /// <summary>
    /// Gets or sets the text content of the document; the value is always null and setting it has no effect.
    /// </summary>
    public override string? TextContent
    {
        get => null;
        set
        {
            // Setting textContent on a document does nothing
        }
    }

    /// <summary>
    /// Documents are cloned with the "clone a node" algorithm (https://dom.spec.whatwg.org/#concept-node-clone): the copy has
    /// the encoding, content type, URL, origin, type, mode and allow declarative shadow roots flag of the document. It also
    /// keeps the base URL of the document (https://github.com/whatwg/dom/issues/454): its fallback base URL is the one of
    /// the document (the base URL inherited by an <c>about:blank</c> or <c>about:srcdoc</c> document), and its base URL is
    /// the current base URL of the document until a base element is inserted into it.
    /// </summary>
    internal override Node CloneSingleNode(Document document)
    {
        return new Document(IsHtmlDocument, ContentType)
        {
            Encoding = Encoding,
            UrlRecord = UrlRecord,
            AboutBaseUrl = AboutBaseUrl,
            _clonedBaseUrl = DocumentBaseUrl,
            Mode = Mode,
            AllowDeclarativeShadowRoots = AllowDeclarativeShadowRoots,
            OriginDocument = GetOriginDocument(this),
            RealmDocument = this,
        };
    }

    private protected override bool IsEqualNodeCore(Node otherNode) => otherNode is Document;
}

/// <summary>Represents the options of <c>createElement()</c> and <c>createElementNS()</c>.</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-elementcreationoptions"/>
[IdlInterface("ElementCreationOptions", "https://dom.spec.whatwg.org/#dictdef-elementcreationoptions")]
public sealed class ElementCreationOptions
{
    /// <summary>Gets the custom element registry used to look up the definition of the created element.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-elementcreationoptions-customelementregistry"/>
    [IdlMember("customElementRegistry", "https://dom.spec.whatwg.org/#dom-elementcreationoptions-customelementregistry")]
    public CustomElementRegistry? CustomElementRegistry
    {
        get;
        init
        {
            field = value;
            HasCustomElementRegistry = true;
        }
    }

    /// <summary>Whether the member exists (it may exist with a null value).</summary>
    internal bool HasCustomElementRegistry { get; private init; }

    /// <summary>Gets the name of the customized built-in element to create.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-elementcreationoptions-is"/>
    [IdlMember("is", "https://dom.spec.whatwg.org/#dom-elementcreationoptions-is")]
    public string? Is { get; init; }
}

/// <summary>Represents the options of <c>importNode()</c>.</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-importnodeoptions"/>
[IdlInterface("ImportNodeOptions", "https://dom.spec.whatwg.org/#dictdef-importnodeoptions")]
public sealed class ImportNodeOptions
{
    /// <summary>Gets the custom element registry associated with the imported nodes.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-importnodeoptions-customelementregistry"/>
    [IdlMember("customElementRegistry", "https://dom.spec.whatwg.org/#dom-importnodeoptions-customelementregistry")]
    public CustomElementRegistry? CustomElementRegistry
    {
        get;
        init
        {
            field = value;
            HasCustomElementRegistry = true;
        }
    }

    /// <summary>Whether the member exists (it may exist with a null value).</summary>
    internal bool HasCustomElementRegistry { get; private init; }

    /// <summary>Gets a value indicating whether only the node itself is imported, without its descendants.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-importnodeoptions-selfonly"/>
    [IdlMember("selfOnly", "https://dom.spec.whatwg.org/#dom-importnodeoptions-selfonly")]
    public bool SelfOnly { get; init; }
}
