namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The data of a node that most nodes do not need, allocated on first use to keep the nodes small.</summary>
internal class NodeRareData
{
    public ChildNodeList? ChildNodes;
    public List<RegisteredObserver>? RegisteredObservers;
    public SlottableData? Slottable;
    public HtmlCollection? Children;

    /// <summary>The event handlers of the event handler IDL attributes, by event type.</summary>
    public Dictionary<string, EventHandlerAttribute>? EventHandlers;

    /// <summary>The event listener list of the node (see <see cref="EventTarget.EventListenerList"/>).</summary>
    public EventListenerEntry[]? EventListeners;

    /// <summary>
    /// Incremented whenever the children of the node change, once the version was requested (see <see cref="Node.ChildrenVersion"/>):
    /// the caches that compare it are created with the rare data, so the nodes without rare data do not count.
    /// </summary>
    public int ChildrenVersion;
}

/// <summary>
/// The data of an element that most elements do not need. The data of the features used by few elements (form controls,
/// custom elements, style elements...) is stored in objects allocated when a value is first set, so the rare data of an
/// element with several classes, an inline style or child nodes requested by a script stays small. Reading a value does not
/// allocate them.
/// </summary>
internal sealed class ElementRareData : NodeRareData
{
    private FormControlData? _formControl;
    private CustomElementData? _customElement;
    private StyleElementData? _styleElement;
    private AttributeCacheData? _attributeCache;
    private LayoutData? _layout;
    private HtmlFeatureData? _htmlFeature;

    public ShadowRoot? ShadowRoot;
    public DomTokenList? ClassList;
    public NamedNodeMap? AttributeMap;
    public DomStringMap? Dataset;

    /// <summary>The declarations of the style attribute (https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style).</summary>
    public Css.Om.CssStyleProperties? Style;

    /// <summary>https://fullscreen.spec.whatwg.org/#fullscreen-flag</summary>
    public bool FullscreenFlag;

    /// <summary>https://fullscreen.spec.whatwg.org/#iframe-fullscreen-flag</summary>
    public bool IframeFullscreenFlag;

    /// <summary>https://fullscreen.spec.whatwg.org/#keyboard-lock-flag</summary>
    public bool KeyboardLockFlag;

    /// <summary>The element had a duplicate-attribute parse error during tokenization (https://w3c.github.io/webappsec-csp/#is-element-nonceable).</summary>
    public bool HadDuplicateAttributeParseError;

    /// <summary>
    /// Whether the <c>style</c> attribute is set without the inline checks of the content security policies: by the CSSOM
    /// (the updating flag of the declarations, https://drafts.csswg.org/cssom/#cssstyledeclaration-updating-flag), or by the
    /// cloning of an element.
    /// </summary>
    public bool SkipStyleAttributeCheck;

    private FormControlData? GetFormControl(bool create) => create ? _formControl ??= new() : _formControl;

    private CustomElementData? GetCustomElement(bool create) => create ? _customElement ??= new() : _customElement;

    private StyleElementData? GetStyleElement(bool create) => create ? _styleElement ??= new() : _styleElement;

    private AttributeCacheData? GetAttributeCache(bool create) => create ? _attributeCache ??= new() : _attributeCache;

    private LayoutData? GetLayout(bool create) => create ? _layout ??= new() : _layout;

    private HtmlFeatureData? GetHtmlFeature(bool create) => create ? _htmlFeature ??= new() : _htmlFeature;

    // ----------------------------------------------------------------------------------------------------------------
    // Attribute caches
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The value of the class attribute parsed in <see cref="ClassNames"/>, when the attribute has an <see cref="Attr"/> node
    /// (the classes of the other elements are cached in the attribute, see <see cref="Element.GetClassNames"/>).
    /// </summary>
    public string? ClassNamesSource
    {
        get => _attributeCache?.ClassNamesSource;
        set => GetAttributeCache(value is not null)?.ClassNamesSource = value;
    }

    public List<string>? ClassNames
    {
        get => _attributeCache?.ClassNames;
        set => GetAttributeCache(value is not null)?.ClassNames = value;
    }

    /// <summary>The SVG DOM objects reflecting the attributes (SVGAnimatedLength...), by IDL attribute name.</summary>
    public Dictionary<string, object>? SvgProperties
    {
        get => _attributeCache?.SvgProperties;
        set => GetAttributeCache(value is not null)?.SvgProperties = value;
    }

    /// <summary>The DOMTokenList objects of reflected attributes (e.g. relList), by content attribute name.</summary>
    public Dictionary<string, DomTokenList>? TokenLists
    {
        get => _attributeCache?.TokenLists;
        set => GetAttributeCache(value is not null)?.TokenLists = value;
    }

    /// <summary>The explicitly set attr-elements of element reflection (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#explicitly-set-attr-element).</summary>
    public Dictionary<string, WeakReference<Element>>? ExplicitlySetAttrElements
    {
        get => _attributeCache?.ExplicitlySetAttrElements;
        set => GetAttributeCache(value is not null)?.ExplicitlySetAttrElements = value;
    }

    /// <summary>The explicitly set attr-elements of FrozenArray&lt;Element&gt; reflection, by content attribute name.</summary>
    public Dictionary<string, List<WeakReference<Element>>>? ExplicitlySetAttrElementLists
    {
        get => _attributeCache?.ExplicitlySetAttrElementLists;
        set => GetAttributeCache(value is not null)?.ExplicitlySetAttrElementLists = value;
    }

    /// <summary>The cached attr-associated elements of FrozenArray&lt;Element&gt; reflection, by content attribute name.</summary>
    public Dictionary<string, Element[]>? CachedAttrAssociatedElements
    {
        get => _attributeCache?.CachedAttrAssociatedElements;
        set => GetAttributeCache(value is not null)?.CachedAttrAssociatedElements = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CSS and layout
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The objects of the pseudo-elements of the element returned by <see cref="Element.Pseudo(string)"/>, by name (see <see cref="Css.Om.CssPseudoElement.Name"/>).</summary>
    public Dictionary<string, Css.Om.CssPseudoElement>? PseudoElements
    {
        get => _layout?.PseudoElements;
        set => GetLayout(value is not null)?.PseudoElements = value;
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap</summary>
    public Css.TypedOm.StylePropertyMap? AttributeStyleMap
    {
        get => _layout?.AttributeStyleMap;
        set => GetLayout(value is not null)?.AttributeStyleMap = value;
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-element-computedstylemapcache-slot</summary>
    public Css.TypedOm.StylePropertyMapReadOnly? ComputedStyleMap
    {
        get => _layout?.ComputedStyleMap;
        set => GetLayout(value is not null)?.ComputedStyleMap = value;
    }

    /// <summary>
    /// The proximity to the viewport and the "currently relevant to the user" flag of an element with <c>content-visibility: auto</c>
    /// (https://drafts.csswg.org/css-contain-2/#proximity-to-the-viewport), see <see cref="Css.Layout.ContentRelevancy"/>.
    /// </summary>
    public Css.Layout.ContentVisibilityAutoState? ContentVisibilityAuto
    {
        get => _layout?.ContentVisibilityAuto;
        set => GetLayout(value is not null)?.ContentVisibilityAuto = value;
    }

    /// <summary>The inline size of the last remembered size of the element (https://drafts.csswg.org/css-sizing-4/#last-remembered), in the layout's px, see <see cref="Css.Layout.LastRememberedSizes"/>.</summary>
    public double? LastRememberedInlineSize
    {
        get => _layout?.LastRememberedInlineSize;
        set => GetLayout(value is not null)?.LastRememberedInlineSize = value;
    }

    /// <summary>The block size of the last remembered size of the element (https://drafts.csswg.org/css-sizing-4/#last-remembered).</summary>
    public double? LastRememberedBlockSize
    {
        get => _layout?.LastRememberedBlockSize;
        set => GetLayout(value is not null)?.LastRememberedBlockSize = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Style elements
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The style sheet of a style element (https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block).</summary>
    public Css.Om.CssStyleSheet? StyleSheet
    {
        get => _styleElement?.StyleSheet;
        set => GetStyleElement(value is not null)?.StyleSheet = value;
    }

    /// <summary>Whether the inline style sheet of a style element is blocked by a content security policy (its load event is an error event).</summary>
    public bool StyleSheetBlockedByContentSecurityPolicy
    {
        get => _styleElement?.StyleSheetBlockedByContentSecurityPolicy ?? false;
        set => GetStyleElement(value)?.StyleSheetBlockedByContentSecurityPolicy = value;
    }

    /// <summary>Whether a style element is on the stack of open elements of a parser (its style block is updated when it is popped when the document has a content security policy).</summary>
    public bool IsOnStackOfOpenElements
    {
        get => _styleElement?.IsOnStackOfOpenElements ?? false;
        set => GetStyleElement(value)?.IsOnStackOfOpenElements = value;
    }

    /// <summary>
    /// The position (line, 0-based column) in the document of the source text of a style element or an SVG script element
    /// created by the HTML parser of a document that has a content security policy, for the source location of its
    /// violations (and of the errors of the script).
    /// </summary>
    public (int Line, int Column)? InlineSourcePosition
    {
        get => _styleElement?.InlineSourcePosition;
        set => GetStyleElement(value is not null)?.InlineSourcePosition = value;
    }

    /// <summary>Whether <see cref="StyleSheet"/> is up to date (it is computed lazily until it is first requested).</summary>
    public bool StyleSheetUpToDate
    {
        get => _styleElement?.StyleSheetUpToDate ?? false;
        set => GetStyleElement(value)?.StyleSheetUpToDate = value;
    }

    /// <summary>Whether a task that fires the load event of the style sheet of a style element is queued.</summary>
    public bool StyleSheetLoadEventPending
    {
        get => _styleElement?.StyleSheetLoadEventPending ?? false;
        set => GetStyleElement(value)?.StyleSheetLoadEventPending = value;
    }

    /// <summary>The document whose scripts are blocked by the style sheet of a style element until its imported style sheets are fetched.</summary>
    public Document? DocumentWhoseScriptsAreBlocked
    {
        get => _styleElement?.DocumentWhoseScriptsAreBlocked;
        set => GetStyleElement(value is not null)?.DocumentWhoseScriptsAreBlocked = value;
    }

    /// <summary>The document whose rendering is blocked by a style element until the imported style sheets of its style sheet are fetched.</summary>
    public Document? DocumentWhoseRenderingIsBlocked
    {
        get => _styleElement?.DocumentWhoseRenderingIsBlocked;
        set => GetStyleElement(value is not null)?.DocumentWhoseRenderingIsBlocked = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // HTML features
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The value of the <c>style</c> attribute when it is blocked by a content security policy (its declarations are not
    /// applied, https://html.spec.whatwg.org/multipage/dom.html#the-style-attribute), or null.
    /// </summary>
    public string? BlockedStyleAttribute
    {
        get => _htmlFeature?.BlockedStyleAttribute;
        set => GetHtmlFeature(value is not null)?.BlockedStyleAttribute = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#cryptographicnonce</summary>
    public string? CryptographicNonce
    {
        get => _htmlFeature?.CryptographicNonce;
        set => GetHtmlFeature(value is not null)?.CryptographicNonce = value;
    }

    /// <summary>The lazy load resumption steps of an img or iframe element (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-load-resumption-steps).</summary>
    public Action? LazyLoadResumptionSteps
    {
        get => _htmlFeature?.LazyLoadResumptionSteps;
        set => GetHtmlFeature(value is not null)?.LazyLoadResumptionSteps = value;
    }

    /// <summary>The popover state of an HTML element (https://html.spec.whatwg.org/multipage/popover.html).</summary>
    public PopoverData? Popover
    {
        get => _htmlFeature?.Popover;
        set => GetHtmlFeature(value is not null)?.Popover = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Form controls
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The form associated by the form element pointer of the parser (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#parser-inserted-flag).</summary>
    public HtmlFormElement? ParserFormOwner
    {
        get => _formControl?.ParserFormOwner;
        set => GetFormControl(value is not null)?.ParserFormOwner = value;
    }

    /// <summary>The labels of a labelable element.</summary>
    public NodeList? Labels
    {
        get => _formControl?.Labels;
        set => GetFormControl(value is not null)?.Labels = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#custom-validity-error-message</summary>
    public string? CustomValidityErrorMessage
    {
        get => _formControl?.CustomValidityErrorMessage;
        set => GetFormControl(value is not null)?.CustomValidityErrorMessage = value;
    }

    public ValidityState? Validity
    {
        get => _formControl?.Validity;
        set => GetFormControl(value is not null)?.Validity = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#user-validity</summary>
    public bool UserValidity
    {
        get => _formControl?.UserValidity ?? false;
        set => GetFormControl(value)?.UserValidity = value;
    }

    /// <summary>The value of a text control before the user edited it, since it is focused (the change is committed when it loses the focus).</summary>
    public string? ValueBeforeUserEdit
    {
        get => _formControl?.ValueBeforeUserEdit;
        set => GetFormControl(value is not null)?.ValueBeforeUserEdit = value;
    }

    /// <summary>The undo history of the value of a text control (see <see cref="Html.TextControlEditing"/>), or null when the user did not edit it since a script set it.</summary>
    public Html.TextControlHistory? TextEditHistory
    {
        get => _formControl?.TextEditHistory;
        set => GetFormControl(value is not null)?.TextEditHistory = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Custom elements
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://dom.spec.whatwg.org/#concept-element-is-value</summary>
    public string? IsValue
    {
        get => _customElement?.IsValue;
        set => GetCustomElement(value is not null)?.IsValue = value;
    }

    /// <summary>The custom element registry, when it is not the registry of the node document.</summary>
    public CustomElementRegistry? CustomElementRegistry
    {
        get => _customElement?.CustomElementRegistry;
        set => GetCustomElement(value is not null)?.CustomElementRegistry = value;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-custom-element-definition</summary>
    public Html.CustomElementDefinition? CustomElementDefinition
    {
        get => _customElement?.CustomElementDefinition;
        set => GetCustomElement(value is not null)?.CustomElementDefinition = value;
    }

    /// <summary>The object created by a .NET custom element constructor.</summary>
    public CustomElement? CustomElementInstance
    {
        get => _customElement?.CustomElementInstance;
        set => GetCustomElement(value is not null)?.CustomElementInstance = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-reaction-queue</summary>
    public Queue<Html.CustomElementReaction> CustomElementReactionQueue => GetCustomElement(create: true)!.ReactionQueue ??= new();

    public Queue<Html.CustomElementReaction>? CustomElementReactionQueueOrNull => _customElement?.ReactionQueue;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#attached-internals</summary>
    public ElementInternals? AttachedInternals
    {
        get => _customElement?.AttachedInternals;
        set => GetCustomElement(value is not null)?.AttachedInternals = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#states-set</summary>
    public CustomStateSet? States
    {
        get => _customElement?.States;
        set => GetCustomElement(value is not null)?.States = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#internal-content-attribute-map</summary>
    public Dictionary<string, string>? InternalContentAttributeMap
    {
        get => _customElement?.InternalContentAttributeMap;
        set => GetCustomElement(value is not null)?.InternalContentAttributeMap = value;
    }

    /// <summary>The state of a form-associated custom element.</summary>
    public Html.FormAssociatedCustomElementData? FormAssociatedCustomElement
    {
        get => _customElement?.FormAssociatedCustomElement;
        set => GetCustomElement(value is not null)?.FormAssociatedCustomElement = value;
    }

    private sealed class AttributeCacheData
    {
        public string? ClassNamesSource;
        public List<string>? ClassNames;
        public Dictionary<string, object>? SvgProperties;
        public Dictionary<string, DomTokenList>? TokenLists;
        public Dictionary<string, WeakReference<Element>>? ExplicitlySetAttrElements;
        public Dictionary<string, List<WeakReference<Element>>>? ExplicitlySetAttrElementLists;
        public Dictionary<string, Element[]>? CachedAttrAssociatedElements;
    }

    private sealed class LayoutData
    {
        public Dictionary<string, Css.Om.CssPseudoElement>? PseudoElements;
        public Css.TypedOm.StylePropertyMap? AttributeStyleMap;
        public Css.TypedOm.StylePropertyMapReadOnly? ComputedStyleMap;
        public Css.Layout.ContentVisibilityAutoState? ContentVisibilityAuto;
        public double? LastRememberedInlineSize;
        public double? LastRememberedBlockSize;
    }

    private sealed class StyleElementData
    {
        public Css.Om.CssStyleSheet? StyleSheet;
        public Document? DocumentWhoseScriptsAreBlocked;
        public Document? DocumentWhoseRenderingIsBlocked;
        public (int Line, int Column)? InlineSourcePosition;
        public bool StyleSheetBlockedByContentSecurityPolicy;
        public bool IsOnStackOfOpenElements;
        public bool StyleSheetUpToDate;
        public bool StyleSheetLoadEventPending;
    }

    private sealed class HtmlFeatureData
    {
        public string? BlockedStyleAttribute;
        public string? CryptographicNonce;
        public Action? LazyLoadResumptionSteps;
        public PopoverData? Popover;
    }

    private sealed class FormControlData
    {
        public HtmlFormElement? ParserFormOwner;
        public NodeList? Labels;
        public string? CustomValidityErrorMessage;
        public ValidityState? Validity;
        public string? ValueBeforeUserEdit;
        public Html.TextControlHistory? TextEditHistory;
        public bool UserValidity;
    }

    private sealed class CustomElementData
    {
        public string? IsValue;
        public CustomElementRegistry? CustomElementRegistry;
        public Html.CustomElementDefinition? CustomElementDefinition;
        public CustomElement? CustomElementInstance;
        public Queue<Html.CustomElementReaction>? ReactionQueue;
        public ElementInternals? AttachedInternals;
        public CustomStateSet? States;
        public Dictionary<string, string>? InternalContentAttributeMap;
        public Html.FormAssociatedCustomElementData? FormAssociatedCustomElement;
    }
}
