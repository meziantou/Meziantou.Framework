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
}

/// <summary>The data of an element that most elements do not need.</summary>
internal sealed class ElementRareData : NodeRareData
{
    public ShadowRoot? ShadowRoot;

    /// <summary>https://fullscreen.spec.whatwg.org/#fullscreen-flag</summary>
    public bool FullscreenFlag;

    /// <summary>https://fullscreen.spec.whatwg.org/#iframe-fullscreen-flag</summary>
    public bool IframeFullscreenFlag;

    /// <summary>https://fullscreen.spec.whatwg.org/#keyboard-lock-flag</summary>
    public bool KeyboardLockFlag;

    public string? IsValue;
    public DomTokenList? ClassList;
    public NamedNodeMap? AttributeMap;
    public string? ClassNamesSource;
    public List<string>? ClassNames;

    public DomStringMap? Dataset;

    /// <summary>The objects of the pseudo-elements of the element returned by <see cref="Element.Pseudo(string)"/>, by name (see <see cref="Css.Om.CssPseudoElement.Name"/>).</summary>
    public Dictionary<string, Css.Om.CssPseudoElement>? PseudoElements;

    /// <summary>The declarations of the style attribute (https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style).</summary>
    public Css.Om.CssStyleProperties? Style;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap</summary>
    public Css.TypedOm.StylePropertyMap? AttributeStyleMap;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-element-computedstylemapcache-slot</summary>
    public Css.TypedOm.StylePropertyMapReadOnly? ComputedStyleMap;

    /// <summary>The style sheet of a style element (https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block).</summary>
    public Css.Om.CssStyleSheet? StyleSheet;

    /// <summary>Whether the inline style sheet of a style element is blocked by a content security policy (its load event is an error event).</summary>
    public bool StyleSheetBlockedByContentSecurityPolicy;

    /// <summary>Whether a style element is on the stack of open elements of a parser (its style block is updated when it is popped when the document has a content security policy).</summary>
    public bool IsOnStackOfOpenElements;

    /// <summary>
    /// The position (line, 0-based column) in the document of the source text of a style element or an SVG script element
    /// created by the HTML parser of a document that has a content security policy, for the source location of its
    /// violations (and of the errors of the script).
    /// </summary>
    public (int Line, int Column)? InlineSourcePosition;

    /// <summary>The element had a duplicate-attribute parse error during tokenization (https://w3c.github.io/webappsec-csp/#is-element-nonceable).</summary>
    public bool HadDuplicateAttributeParseError;

    /// <summary>
    /// The value of the <c>style</c> attribute when it is blocked by a content security policy (its declarations are not
    /// applied, https://html.spec.whatwg.org/multipage/dom.html#the-style-attribute), or null.
    /// </summary>
    public string? BlockedStyleAttribute;

    /// <summary>
    /// Whether the <c>style</c> attribute is set without the inline checks of the content security policies: by the CSSOM
    /// (the updating flag of the declarations, https://drafts.csswg.org/cssom/#cssstyledeclaration-updating-flag), or by the
    /// cloning of an element.
    /// </summary>
    public bool SkipStyleAttributeCheck;

    /// <summary>Whether <see cref="StyleSheet"/> is up to date (it is computed lazily until it is first requested).</summary>
    public bool StyleSheetUpToDate;

    /// <summary>Whether a task that fires the load event of the style sheet of a style element is queued.</summary>
    public bool StyleSheetLoadEventPending;

    /// <summary>The document whose scripts are blocked by the style sheet of a style element until its imported style sheets are fetched.</summary>
    public Document? DocumentWhoseScriptsAreBlocked;

    /// <summary>The document whose rendering is blocked by a style element until the imported style sheets of its style sheet are fetched.</summary>
    public Document? DocumentWhoseRenderingIsBlocked;

    /// <summary>The form associated by the form element pointer of the parser (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#parser-inserted-flag).</summary>
    public HtmlFormElement? ParserFormOwner;

    /// <summary>The labels of a labelable element.</summary>
    public NodeList? Labels;

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#custom-validity-error-message</summary>
    public string? CustomValidityErrorMessage;

    public ValidityState? Validity;

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#user-validity</summary>
    public bool UserValidity;

    /// <summary>The value of a text control before the user edited it, since it is focused (the change is committed when it loses the focus).</summary>
    public string? ValueBeforeUserEdit;

    /// <summary>The undo history of the value of a text control (see <see cref="Html.TextControlEditing"/>), or null when the user did not edit it since a script set it.</summary>
    public Html.TextControlHistory? TextEditHistory;

    /// <summary>The SVG DOM objects reflecting the attributes (SVGAnimatedLength...), by IDL attribute name.</summary>
    public Dictionary<string, object>? SvgProperties;

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#cryptographicnonce</summary>
    public string? CryptographicNonce;

    /// <summary>The DOMTokenList objects of reflected attributes (e.g. relList), by content attribute name.</summary>
    public Dictionary<string, DomTokenList>? TokenLists;

    /// <summary>The explicitly set attr-elements of element reflection (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#explicitly-set-attr-element).</summary>
    public Dictionary<string, WeakReference<Element>>? ExplicitlySetAttrElements;

    /// <summary>The explicitly set attr-elements of FrozenArray&lt;Element&gt; reflection, by content attribute name.</summary>
    public Dictionary<string, List<WeakReference<Element>>>? ExplicitlySetAttrElementLists;

    /// <summary>The lazy load resumption steps of an img or iframe element (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-load-resumption-steps).</summary>
    public Action? LazyLoadResumptionSteps;

    /// <summary>The cached attr-associated elements of FrozenArray&lt;Element&gt; reflection, by content attribute name.</summary>
    public Dictionary<string, Element[]>? CachedAttrAssociatedElements;

    /// <summary>The popover state of an HTML element (https://html.spec.whatwg.org/multipage/popover.html).</summary>
    public PopoverData? Popover;

    /// <summary>The custom element registry, when it is not the registry of the node document.</summary>
    public CustomElementRegistry? CustomElementRegistry;

    /// <summary>https://dom.spec.whatwg.org/#concept-element-custom-element-definition</summary>
    public Html.CustomElementDefinition? CustomElementDefinition;

    /// <summary>The object created by a .NET custom element constructor.</summary>
    public CustomElement? CustomElementInstance;

    private Queue<Html.CustomElementReaction>? _customElementReactionQueue;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-reaction-queue</summary>
    public Queue<Html.CustomElementReaction> CustomElementReactionQueue => _customElementReactionQueue ??= new();

    public Queue<Html.CustomElementReaction>? CustomElementReactionQueueOrNull => _customElementReactionQueue;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#attached-internals</summary>
    public ElementInternals? AttachedInternals;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#states-set</summary>
    public CustomStateSet? States;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#internal-content-attribute-map</summary>
    public Dictionary<string, string>? InternalContentAttributeMap;

    /// <summary>The state of a form-associated custom element.</summary>
    public Html.FormAssociatedCustomElementData? FormAssociatedCustomElement;

    /// <summary>
    /// The proximity to the viewport and the "currently relevant to the user" flag of an element with <c>content-visibility: auto</c>
    /// (https://drafts.csswg.org/css-contain-2/#proximity-to-the-viewport), see <see cref="Css.Layout.ContentRelevancy"/>.
    /// </summary>
    public Css.Layout.ContentVisibilityAutoState? ContentVisibilityAuto;

    /// <summary>The inline size of the last remembered size of the element (https://drafts.csswg.org/css-sizing-4/#last-remembered), in the layout's px, see <see cref="Css.Layout.LastRememberedSizes"/>.</summary>
    public double? LastRememberedInlineSize;

    /// <summary>The block size of the last remembered size of the element (https://drafts.csswg.org/css-sizing-4/#last-remembered).</summary>
    public double? LastRememberedBlockSize;
}
