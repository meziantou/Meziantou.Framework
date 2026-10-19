using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Computes the styles of the elements of a document (https://drafts.csswg.org/css-cascade-5/): the style rules of the
/// user agent style sheet, the presentational hints, the style sheets of the document and of the shadow trees (including
/// the adopted style sheets) and the style attributes are cascaded, then the values are computed.
/// </summary>
/// <remarks>
/// The styles are computed on demand and cached until the document, a style sheet or the environment (viewport and user
/// preferences) changes.
/// </remarks>
internal sealed partial class StyleEngine
{
    private static readonly ConcurrentDictionary<(bool Quirks, bool Html, bool Scripting, bool SvgImage, bool ForcedColors), CascadeRuleSet> UserAgentRuleSets = new();
    private static readonly Lazy<CascadeRuleSet> PresentationalHintRuleSet = new(CreatePresentationalHintRuleSet);

    /// <summary>The properties that apply to ::marker, besides the font, animation, transition and custom properties (see <see cref="IsMarkerProperty"/>).</summary>
    private static readonly FrozenSet<string> MarkerProperties = new[]
    {
        "line-height", "white-space-collapse", "text-wrap-mode", "color", "text-combine-upright", "text-orientation", "unicode-bidi", "direction", "content",
        "hyphens", "hyphenate-character", "hyphenate-limit-chars", "letter-spacing", "line-break", "overflow-wrap", "tab-size", "text-transform",
        "word-break", "word-spacing", "text-autospace", "text-spacing-trim", "text-decoration-skip-ink", "text-emphasis-color",
        "text-emphasis-position", "text-emphasis-style", "text-shadow", "text-underline-position", "cursor",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FirstLineProperties = new[]
    {
        "line-height", "color", "opacity", "letter-spacing", "word-spacing", "text-transform", "text-justify", "text-shadow",
        "text-underline-position", "text-underline-offset", "ruby-position", "baseline-shift", "baseline-source", "alignment-baseline",
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly Document _document;
    private readonly MediaEnvironment? _environmentOverride;
    private readonly Dictionary<Node, TreeStyles> _trees = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<string, CascadedValue> EmptyCascadedValues = new(StringComparer.Ordinal);

    private Dictionary<(Element Element, string? PseudoElement), ComputedStyle> _styles = [];
    private Dictionary<(Element Element, string? PseudoElement), ComputedStyle> _spareStyles = [];
    private bool _fullRecalcPending;
    private bool _includeStartingStyle;
    private bool _recalculating;

    /// <summary>The list of the matched declaration blocks of the last cascade, reused by the next one.</summary>
    private List<MatchedBlock>? _blocks;

    /// <summary>
    /// The cascaded values of the matched declaration blocks, by hash code of the blocks: the elements that match the same
    /// rules (e.g. the items of a list) share their cascaded values, which are never modified (see <see cref="ComputedStyle"/>).
    /// </summary>
    private readonly Dictionary<int, SharedCascade> _sharedCascades = [];

    /// <summary>The styles of the element instances of the use-element shadow trees (see <see cref="GetUseInstanceStyle"/>), by element and parent style.</summary>
    private Dictionary<(Element Element, ComputedStyle Parent), ComputedStyle>? _useInstanceStyles;

    /// <summary>The root of the use-element shadow tree whose element instance is being cascaded (see <see cref="GetUseInstanceStyle"/>), or null.</summary>
    private Element? _useInstanceRoot;

    /// <summary>
    /// The link that matches <c>:visited</c> in the cascade that runs: the visited link of the element whose visited style is
    /// cascaded (see <see cref="CascadeVisitedStyle"/>), or null for the cascade of the computed styles, where no link is visited.
    /// </summary>
    private Element? _cascadeVisitedLink;

    /// <summary>The history of the visited links of the document for this generation of styles, or null: no link is visited.</summary>
    private VisitedLinkStore? _visitedLinks;
    private long _visitedLinksVersion;
    private Urls.UrlRecord? _visitedLinksDocumentUrl;

    /// <summary>Whether the document has a history of visited links: its links may match <c>:visited</c> for the painting (see <see cref="ComputedStyle.VisitedLink"/>).</summary>
    internal bool HasVisitedLinks => _visitedLinks is not null;

    /// <summary>The property whose winning declaration is looked up by the cascade that runs (see <see cref="GetCascadeOrigin"/>), or null.</summary>
    private string? _originQueryProperty;

    /// <summary>The origin of the winning declaration of <see cref="_originQueryProperty"/> in the last cascade, or null when no declaration applies.</summary>
    private CascadeOrigin? _originQueryResult;

    /// <summary>
    /// The properties whose computed value is being computed, by style (a property needed again while it is computed is a
    /// dependency cycle). It is shared by the styles of the engine, so that each style does not allocate its own set.
    /// </summary>
    internal ComputingPropertyStack ComputingProperties { get; } = new();

    /// <summary>The styles whose computed value of a property is computed before the one of a descendant (see <see cref="ComputedStyle"/>).</summary>
    internal List<ComputedStyle> PendingAncestors { get; } = [];

    /// <summary>Returns the shared cascaded values whose logical properties are mapped for each writing mode and direction (see <see cref="_logicalCascades"/>).</summary>
    internal List<(string WritingMode, bool RightToLeft, Dictionary<string, CascadedValue> Mapped)> GetLogicalCascades(Dictionary<string, CascadedValue> cascaded) => _logicalCascades.GetOrCreateValue(cascaded);
    private readonly Dictionary<ShadowRoot, PartElementMap> _partElementMaps = new(ReferenceEqualityComparer.Instance);
    private int _domVersion = -1;
    private int _elementStateVersion = -1;
    private int _styleVersion = -1;
    private int _animationVersion;
    private int _fontVersion;
    private int _scrollStateVersion;
    private int _version;
    private bool _inlineStylesDeclareAnimations;
    private bool _stylesDeclareContentVisibility;
    private bool _shadowTreesDeclareAnimations;
    private bool _shadowTreesHaveScrollStateQueries;

    /// <summary>The styles computed before the last change of the styles, used by the transitions (the before-change styles).</summary>
    private readonly Dictionary<(Element Element, string? PseudoElement), ComputedStyle> _beforeChangeStyles = [];

    /// <summary>Whether the before-change styles have the same computed values as the new styles when their cascaded values are the same.</summary>
    internal bool BeforeChangeComparableByCascade { get; private set; }
    private (int Dom, int ElementState, int Style, MediaEnvironment? Environment) _animationFlushVersion = (-1, -1, -1, null);
    private MediaEnvironment? _environment;
    private Dictionary<string, CustomPropertyRegistration>? _registeredProperties;
    private readonly Dictionary<string, double> _randomBaseValues = new(StringComparer.Ordinal);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Element, Dictionary<string, double>> _elementRandomBaseValues = [];

    /// <summary>
    /// The cascaded values shared by several styles whose logical properties are mapped to the physical ones, by writing mode
    /// and direction (see <see cref="ComputedStyle"/>): the elements that share cascaded values usually have the same writing
    /// mode, so they share the mapped values too.
    /// </summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Dictionary<string, CascadedValue>, List<(string WritingMode, bool RightToLeft, Dictionary<string, CascadedValue> Mapped)>> _logicalCascades = [];

    /// <param name="document">The document whose elements are styled.</param>
    /// <param name="environmentOverride">The environment to use instead of the one of the browsing context of the document.</param>
    public StyleEngine(Document document, MediaEnvironment? environmentOverride = null)
    {
        _document = document;
        _environmentOverride = environmentOverride;
    }

    public Document Document => _document;

    /// <summary>
    /// Returns the random base value of a random caching key (https://drafts.csswg.org/css-values-5/#random-caching-key): a
    /// random number in [0, 1), the same for the lifetime of the document (or of the element when it is element-scoped).
    /// </summary>
    public double GetRandomBaseValue(CssRandomSharing sharing, Element element, string? pseudoElement)
    {
        var key = sharing.CachingKey;
        Dictionary<string, double> values;
        if (sharing.ElementScoped)
        {
            values = _elementRandomBaseValues.GetValue(element, static _ => new(StringComparer.Ordinal));
            key = pseudoElement + "|" + key;
        }
        else
        {
            values = _randomBaseValues;
        }

        if (!values.TryGetValue(key, out var value))
        {
#pragma warning disable CA5394 // The random() values of CSS are not used for security
            value = Random.Shared.NextDouble();
#pragma warning restore CA5394
            values[key] = value;
        }

        return value;
    }

    /// <summary>
    /// A counter incremented when the computed styles are invalidated: the DOM, the state of an element, a style sheet, the
    /// environment (e.g. the viewport size), the animated values or the fonts changed. The caches that depend on the computed
    /// styles (the box tree and the layout, see <see cref="Layout.LayoutEngine"/>) compare it with the value they were computed with.
    /// </summary>
    public int Version
    {
        get
        {
            EnsureUpToDate();
            return _version;
        }
    }

    /// <summary>The environment the media queries and the viewport-relative lengths are evaluated against.</summary>
    public MediaEnvironment Environment
    {
        get
        {
            EnsureUpToDate();
            return _environment!;
        }
    }

    /// <summary>
    /// The registered custom properties of the document, by name (https://drafts.css-houdini.org/css-properties-values-api/#determining-registration):
    /// the properties registered by CSS.registerProperty(), then the @property rules of the style sheets of the document.
    /// </summary>
    public Dictionary<string, CustomPropertyRegistration> RegisteredProperties
    {
        get
        {
            EnsureUpToDate();
            if (_registeredProperties is null)
            {
                var registrations = new Dictionary<string, CustomPropertyRegistration>(StringComparer.Ordinal);
                foreach (var (name, rule) in GetTreeStyles(_document).PropertyRules)
                {
                    registrations[name] = rule.Registration;
                }

                // The @property rules of the shadow trees are registered globally (https://drafts.css-houdini.org/css-properties-values-api/#shadow-dom);
                // the rules of the document win
                if (_document.HasShadowRoots)
                {
                    foreach (var shadowRoot in _document.ShadowIncludingInclusiveDescendants().OfType<ShadowRoot>())
                    {
                        foreach (var (name, rule) in GetTreeStyles(shadowRoot).PropertyRules)
                        {
                            registrations.TryAdd(name, rule.Registration);
                        }
                    }
                }

                foreach (var (name, registration) in _document.RegisteredPropertySet)
                {
                    registrations[name] = registration;
                }

                _registeredProperties = registrations;
            }

            return _registeredProperties;
        }
    }

    /// <summary>
    /// Returns the custom function with the name (https://drafts.csswg.org/css-mixins-1/#function-rule), dereferenced as a
    /// tree-scoped reference (https://drafts.csswg.org/css-scoping/#css-tree-scoped-reference): the @function rules of the
    /// tree, then the ones of the trees of the shadow hosts, up to the document. Returns null when there is no such function.
    /// </summary>
    /// <param name="name">The name of the function.</param>
    /// <param name="treeScope">The root of the tree of the style sheet of the reference, or null for the document.</param>
    /// <param name="definitionScope">The root of the tree of the @function rule: the tree-scoped references of its body are looked up from it.</param>
    public CssFunctionRule? FindFunction(string name, Node? treeScope, out Node definitionScope)
    {
        EnsureUpToDate();
        definitionScope = _document;
        var root = treeScope ?? _document;
        while (root is Dom.Document or ShadowRoot)
        {
            if (GetTreeStyles(root).FunctionRules.TryGetValue(name, out var function))
            {
                definitionScope = root;
                return function;
            }

            if (root is not ShadowRoot shadowRoot)
                break;

            root = shadowRoot.Host.GetRoot();
        }

        return null;
    }

    /// <summary>
    /// Checks whether the environment of the document changed (the size of its viewport, its device pixel ratio...) since the
    /// styles were last used: the styles are then invalidated, and the images that use srcset or picture react to the change
    /// (see <see cref="Document.QueueReactToEnvironmentChangesOfImages"/>).
    /// </summary>
    public void CheckEnvironment() => EnsureUpToDate();

    /// <summary>
    /// Returns whether the styles of the document may create CSS animations or transitions: a rule of its style sheets or a
    /// style attribute declares an animation or a transition property, or the document has shadow trees.
    /// </summary>
    public bool MayHaveDeclarativeAnimations()
    {
        EnsureUpToDate();
        return _inlineStylesDeclareAnimations || _document.InlineStylesDeclareAnimations || _document.HasShadowRoots || _document.HasViewTransitionPseudoTrees || GetTreeStyles(_document).Rules.DeclaresAnimations;
    }

    /// <summary>
    /// Returns whether the styles of the document may use <c>content-visibility</c> or <c>contain-intrinsic-size</c>: a rule of
    /// its style sheets, of the style sheets of its shadow trees whose styles were computed or a style attribute declares them.
    /// The rendering steps then lay out the document to determine the relevance to the user of the elements with
    /// <c>content-visibility: auto</c> (https://drafts.csswg.org/css-contain-2/#relevant-to-the-user) and to record the last
    /// remembered sizes (https://drafts.csswg.org/css-sizing-4/#last-remembered).
    /// </summary>
    public bool MayHaveContentVisibility()
    {
        EnsureUpToDate();
        return _stylesDeclareContentVisibility || _document.InlineStylesDeclareContentVisibility || GetTreeStyles(_document).Rules.DeclaresContentVisibility;
    }

    /// <summary>
    /// Returns whether the styles of the document may use position options (https://drafts.csswg.org/css-anchor-position-1/#position-try-fallbacks):
    /// a rule of its style sheets or a style attribute declares <c>position-try-fallbacks</c>. The rendering steps then lay out the
    /// document to record the last successful position options (https://drafts.csswg.org/css-anchor-position-1/#last-successful-recording),
    /// as the browsers render the document from its loading.
    /// </summary>
    public bool MayHavePositionTryFallbacks()
    {
        EnsureUpToDate();
        return _document.InlineStylesDeclarePositionTryFallbacks || GetTreeStyles(_document).Rules.DeclaresPositionTryFallbacks;
    }

    /// <summary>
    /// Returns whether the styles of the document may use container scroll-state queries: a rule of its style sheets, or of the
    /// style sheets of its shadow trees whose styles were computed, is in a @container rule with a scroll-state query. The
    /// rendering steps then lay out the document to take the snapshots of the scroll-state query containers
    /// (https://drafts.csswg.org/css-conditional-5/#updating-scroll-state).
    /// </summary>
    public bool MayHaveScrollStateQueries()
    {
        EnsureUpToDate();
        return _shadowTreesHaveScrollStateQueries || GetTreeStyles(_document).Rules.HasScrollStateQueries;
    }

    /// <summary>
    /// Returns whether the styles of the document may generate scroll navigation controls (https://drafts.csswg.org/css-overflow-5/#scroll-navigation):
    /// a rule of its style sheets declares <c>scroll-marker-group</c> or <c>scroll-target-group</c>, or styles a
    /// <c>::scroll-button()</c>. The rendering steps then lay out the document to determine the active scroll markers
    /// (<c>:target-current</c>) and the scroll buttons that can scroll, as the browsers render the document from its loading.
    /// </summary>
    public bool MayHaveScrollNavigationControls()
    {
        EnsureUpToDate();
        return GetTreeStyles(_document).Rules.HasScrollNavigationControls;
    }

    /// <summary>
    /// Returns whether the styles changed since the last update of the CSS animations and transitions of the document
    /// (<see cref="Dom.Document.FlushAnimationStyles"/>), and records the current versions.
    /// </summary>
    public bool BeginAnimationFlush()
    {
        EnsureUpToDate();
        var version = (_domVersion, _elementStateVersion, _styleVersion, _environment);
        if (version == _animationFlushVersion)
            return false;

        _animationFlushVersion = version;
        return true;
    }

    /// <summary>Removes the before-change styles of the elements that are no longer connected.</summary>
    public void PruneBeforeChangeStyles()
    {
        if (_beforeChangeStyles.Count == 0)
            return;

        foreach (var key in _beforeChangeStyles.Keys.ToArray())
        {
            if (!key.Element.IsConnected)
            {
                _beforeChangeStyles.Remove(key);
            }
        }
    }

    /// <summary>Returns whether the rules of the document may apply to a pseudo-element.</summary>
    public bool MayHavePseudoElementRules(string pseudoElement)
    {
        EnsureUpToDate();
        return GetTreeStyles(_document).Rules.MayHaveRulesFor(pseudoElement);
    }

    /// <summary>
    /// Returns whether a style rule or an animation may apply to a pseudo-element of an element (e.g. <c>before</c>,
    /// <c>first-letter</c>). When it returns false, the pseudo-element has the default style (e.g. <c>content: none</c> for
    /// <c>::before</c>), so the box tree does not compute it. The candidate rules of the element (by id, class, tag and the
    /// universal rules) of the user agent and of the document are checked; the elements that the rules of a shadow tree
    /// may style (the elements of a shadow tree, the shadow hosts and the slotted elements) are not checked (true).
    /// </summary>
    public bool MayHavePseudoElementRules(Element element, string pseudoElement)
    {
        EnsureUpToDate();
        if (_styles.ContainsKey((element, pseudoElement)) || _document.AnimationsOrNull?.GetElementAnimations(element)?.Get(pseudoElement) is not null)
            return true;

        if (element.GetRoot() is not Dom.Document || element.AttachedShadowRoot is not null || element.Slottable?.AssignedSlot is not null)
            return true;

        return GetUserAgentRuleSet(_document, _environment!).MayHaveRulesFor(element, pseudoElement) || GetTreeStyles(_document).Rules.MayHaveRulesFor(element, pseudoElement);
    }

    /// <summary>
    /// Returns whether an author style rule may apply to a pseudo-element of an element (by the name of the pseudo-element of
    /// the rules, e.g. <c>scroll-button</c>), like <see cref="MayHavePseudoElementRules(Element, string)"/> without the rules of
    /// the user agent: for the pseudo-elements that the user agent style sheet styles for every element but that only exist when
    /// an author rule gives them a <c>content</c> (the scroll navigation controls).
    /// </summary>
    public bool MayHaveAuthorPseudoElementRules(Element element, string pseudoElement)
    {
        EnsureUpToDate();

        // (the elements of a document without shadow tree are only styled by the rules of the document: their root is not looked up)
        if (!_document.HasShadowRoots || (element.GetRoot() is Dom.Document && element.AttachedShadowRoot is null && element.Slottable?.AssignedSlot is null))
            return GetTreeStyles(_document).Rules.MayHaveRulesFor(element, pseudoElement);

        // An element that the rules of a shadow tree may style (see MayHavePseudoElementRules): the candidate rules are not
        // checked, only whether a tree has a rule for the pseudo-element (the style of the element is computed: so are its trees)
        foreach (var tree in _trees.Values)
        {
            var rules = tree.Rules;
            if (rules.MayHaveRulesFor(pseudoElement) || HasRule(rules.HostRules, pseudoElement) || HasRule(rules.SlottedRules, pseudoElement) || HasRule(rules.PartRules, pseudoElement))
                return true;
        }

        return false;

        static bool HasRule(List<CascadeRule> rules, string pseudoElement)
        {
            foreach (var rule in rules)
            {
                if (string.Equals(rule.PseudoElement, pseudoElement, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Creates the style of an anonymous box (https://drafts.csswg.org/css-display/#anonymous) whose parent box has the
    /// style: the inherited properties are inherited from it, the other properties have their initial value. The style
    /// belongs to the element of the parent style, as a pseudo-element without rules.
    /// </summary>
    public ComputedStyle CreateAnonymousStyle(ComputedStyle parent) => new(this, parent.Element, AnonymousBoxPseudoElement, parent, EmptyCascadedValues, cascadedShared: true) { IncludeTransitions = false };

    /// <summary>The name of the pseudo-element of the styles of the anonymous boxes (no rule matches it).</summary>
    internal const string AnonymousBoxPseudoElement = "-internal-anonymous-box";

    /// <summary>
    /// The properties that the anonymous fieldset content box inherits from its fieldset
    /// (https://html.spec.whatwg.org/multipage/rendering.html#anonymous-fieldset-content-box), with the properties of the
    /// scroll containers and of the multi-column containers that were added to CSS since (as in Blink).
    /// </summary>
    private static readonly string[] FieldsetContentProperties =
    [
        "align-content", "align-items", "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius",
        "column-count", "column-fill", "column-gap", "column-rule-color", "column-rule-style", "column-rule-width", "column-width", "column-height", "column-wrap",
        "row-rule-color", "row-rule-style", "row-rule-width", "column-rule-break", "row-rule-break", "rule-overlap",
        "column-rule-visibility-items", "row-rule-visibility-items",
        "column-rule-inset-cap-start", "column-rule-inset-cap-end", "column-rule-inset-junction-start", "column-rule-inset-junction-end",
        "row-rule-inset-cap-start", "row-rule-inset-cap-end", "row-rule-inset-junction-start", "row-rule-inset-junction-end",
        "flex-direction", "flex-wrap", "flex-line-count", "grid-auto-columns", "grid-auto-flow", "grid-auto-rows", "row-gap", "grid-template-areas", "grid-template-columns", "grid-template-rows",
        "justify-content", "justify-items", "overflow-x", "overflow-y", "padding-top", "padding-right", "padding-bottom", "padding-left", "text-overflow", "unicode-bidi",
        "scrollbar-gutter", "scrollbar-width", "scrollbar-color", "scroll-snap-type", "scroll-behavior", "overscroll-behavior-x", "overscroll-behavior-y", "overflow-anchor",
        "scroll-padding-top", "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left", "overflow-clip-margin",
    ];

    /// <summary>The used values of the properties of a fieldset that apply to its anonymous fieldset content box instead of its own box.</summary>
    private static readonly (string Name, string Value)[] FieldsetFrameOverrides =
    [
        ("overflow-x", "visible"), ("overflow-y", "visible"), ("align-content", "normal"),
        ("column-count", "auto"), ("column-width", "auto"), ("column-height", "auto"), ("text-overflow", "clip"),
    ];

    /// <summary>
    /// Creates the styles of the two boxes of a fieldset (https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements):
    /// the style of its principal box, where the properties of the contents do not apply (overflow, the columns, the content
    /// alignment; it keeps the paddings for its sizing, but they are the ones of the content box, which is laid out in its
    /// padding box, see <see cref="Layout.FieldsetFormattingContext"/>), and the style of its anonymous fieldset content box,
    /// which inherits these properties from the fieldset and is a containing block of the absolutely positioned descendants
    /// when the fieldset is one (as in Blink). <paramref name="contentDisplay"/> is the display of the content box: grid, flex or flow-root.
    /// </summary>
    internal (ComputedStyle Frame, ComputedStyle Content) CreateFieldsetStyles(ComputedStyle style, string contentDisplay)
    {
        var frame = style.CreateOverriddenStyle(FieldsetFrameOverrides);
        var cascaded = new Dictionary<string, CascadedValue>(FieldsetContentProperties.Length + 2, StringComparer.Ordinal);
        foreach (var name in FieldsetContentProperties)
        {
            cascaded[name] = new CascadedValue(new CssLonghandValue(name, "inherit"), 0, source: null);
        }

        // "The 'block-size' property is expected to be set to '100%'" (the layout of the fieldset gives the content box the
        // block size that remains after the legend; the percentages of its contents resolve through it)
        cascaded["display"] = new CascadedValue(new CssLonghandValue("display", contentDisplay), 0, source: null);
        cascaded["block-size"] = new CascadedValue(new CssLonghandValue("block-size", "100%"), 0, source: null);
        if (style.Layout.EstablishesFixedPositionContainingBlock)
        {
            // (layout containment makes the content box a containing block of the fixed positioned descendants)
            cascaded["contain"] = new CascadedValue(new CssLonghandValue("contain", "layout"), 0, source: null);
        }
        else if (style.Layout.Position != Layout.PositionKind.Static)
        {
            cascaded["position"] = new CascadedValue(new CssLonghandValue("position", "relative"), 0, source: null);
        }

        return (frame, new ComputedStyle(this, style.Element, AnonymousBoxPseudoElement, style, cascaded) { IncludeTransitions = false });
    }

    /// <summary>
    /// Creates the style of the inner editor of a text control (see <see cref="Layout.TextControlLayout"/>), an anonymous block
    /// container that inherits from the control: as in the browsers, it has the <c>unicode-bidi</c> of the control (the
    /// paragraph of its value); the inner editor of a text field (not <paramref name="multiline"/>) does not wrap and preserves
    /// the white space (<c>white-space: pre</c>), is a scroll container that hides its overflow, has the <c>text-overflow</c>
    /// of the control and a line at least as tall as the normal line height; the inner editor of a <c>textarea</c> always
    /// preserves the white space (it collapses none, whatever <c>white-space-collapse</c> is, but <c>break-spaces</c>) and
    /// does not wrap when the <c>wrap</c> attribute is <c>off</c> (<paramref name="wraps"/>,
    /// https://html.spec.whatwg.org/multipage/rendering.html#the-textarea-element-2).
    /// </summary>
    internal ComputedStyle CreateInnerEditorStyle(ComputedStyle style, bool multiline, bool wraps)
    {
        var cascaded = new Dictionary<string, CascadedValue>(8, StringComparer.Ordinal);
        Set("display", "block");
        Set("unicode-bidi", "inherit");
        if (!multiline)
        {
            foreach (var (name, value) in GetTextFieldLineOverrides(style))
            {
                Set(name, value);
            }
        }
        else
        {
            // As in the browsers, a textarea always preserves the white space of its value (white-space: normal behaves as
            // pre-wrap, nowrap as pre)
            if (style.Layout.WhiteSpaceCollapse != Layout.WhiteSpaceCollapse.BreakSpaces)
            {
                Set("white-space-collapse", "preserve");
            }

            if (!wraps)
            {
                Set("text-wrap-mode", "nowrap");
            }
        }

        return new ComputedStyle(this, style.Element, AnonymousBoxPseudoElement, style, cascaded) { IncludeTransitions = false };

        void Set(string name, string value) => cascaded[name] = new CascadedValue(new CssLonghandValue(name, value), 0, source: null);
    }

    /// <summary>
    /// Creates the style of the box of the <c>::placeholder</c> pseudo-element of a text control from its computed style: a
    /// block container, with the writing mode, the direction and the text orientation of the control
    /// (https://drafts.csswg.org/css-pseudo-4/#placeholder-pseudo: the properties that apply to <c>::first-line</c> apply to
    /// it, so not these ones); the one of a text field is a line that is clipped, as its inner editor (see
    /// <see cref="CreateInnerEditorStyle"/>), with the line height of the control (as in the browsers).
    /// </summary>
    internal static ComputedStyle CreatePlaceholderBoxStyle(ComputedStyle placeholderStyle, ComputedStyle controlStyle, bool multiline)
    {
        var overrides = new List<(string Name, string Value)> { ("display", "block"), ("writing-mode", "inherit"), ("direction", "inherit"), ("text-orientation", "inherit") };
        if (!multiline)
        {
            overrides.Add(("line-height", "inherit"));
            overrides.AddRange(GetTextFieldLineOverrides(controlStyle));
        }

        return placeholderStyle.CreateOverriddenStyle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(overrides));
    }

    /// <summary>The used values of the properties of the line of a text field (its inner editor or its placeholder).</summary>
    private static IEnumerable<(string Name, string Value)> GetTextFieldLineOverrides(ComputedStyle controlStyle)
    {
        yield return ("white-space-collapse", "preserve");
        yield return ("text-wrap-mode", "nowrap");
        yield return ("overflow-x", "hidden");
        yield return ("overflow-y", "hidden");
        yield return ("text-overflow", "inherit");

        // As in the browsers, a line height smaller than the normal one does not shrink the line of a text field
        var layout = controlStyle.Layout;
        if (layout.UsedLineHeight < controlStyle.Fonts.PrimaryFont.GetMetrics(layout.FontSize).NormalLineHeight)
        {
            yield return ("line-height", "normal");
        }
    }

    /// <summary>
    /// Returns the @keyframes rule with the name, dereferenced as a tree-scoped reference
    /// (https://drafts.csswg.org/css-scoping/#css-tree-scoped-reference): the rules of the tree, then the ones of the trees of
    /// the shadow hosts, up to the document. Returns null when there is no such rule.
    /// </summary>
    /// <param name="viewTransitionRoot">The originating element of the view transition pseudo-element the keyframes animate, or null.</param>
    public CssKeyframesRule? FindKeyframes(string name, Node? treeScope, Element? viewTransitionRoot = null)
    {
        EnsureUpToDate();
        var root = treeScope ?? _document;
        while (root is Dom.Document or ShadowRoot)
        {
            if (GetTreeStyles(root).KeyframesRules.TryGetValue(name, out var rule))
                return rule;

            if (root is not ShadowRoot shadowRoot)
                break;

            root = shadowRoot.Host.GetRoot();
        }

        // The keyframes of the user agent style sheets of the view transitions (https://drafts.csswg.org/css-view-transitions-1/#ua-styles),
        // which the author style sheets can use too (https://github.com/w3c/csswg-drafts/issues/7560)
        if (name.StartsWith("-ua-", StringComparison.Ordinal))
        {
            // The keyframes of the transition of the pseudo-element, else the ones of another active transition (the document's first)
            if (viewTransitionRoot is not null && GetViewTransitionRules(viewTransitionRoot) is { } ownRules && ownRules.Keyframes.TryGetValue(name, out var ownRule))
                return ownRule;

            if (ViewTransitionKeyframes.Value.TryGetValue(name, out var staticRule))
                return staticRule;

            foreach (var transition in _document.GetActiveViewTransitions())
            {
                if (transition.RootElement is { } transitionRoot && GetViewTransitionRules(transitionRoot) is { } dynamicRules && dynamicRules.Keyframes.TryGetValue(name, out var dynamicRule))
                    return dynamicRule;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the @view-transition rule of the style sheets of the document that applies (https://drafts.csswg.org/css-view-transitions-2/#resolve-view-transition-rule:
    /// the last one, not in a conditional rule that does not match; the rules of the shadow trees are ignored), or null.
    /// </summary>
    public CssViewTransitionRule? GetViewTransitionRule()
    {
        EnsureUpToDate();
        return GetTreeStyles(_document).ViewTransitionRule;
    }

    private Dictionary<Element, (string Text, CascadeRuleSet Rules, Dictionary<string, CssKeyframesRule> Keyframes)>? _viewTransitionRules;

    /// <summary>
    /// Returns the rules of the dynamic view transition style sheet of the active view transition whose root element is the
    /// element (https://drafts.csswg.org/css-view-transitions-1/#document-dynamic-view-transition-style-sheet), "a style sheet in
    /// the user-agent origin, ordered after the global view transition user agent style sheet", or null. Each transition has its
    /// own rules: the names of two concurrent transitions (a scoped one and the one of the document) can be the same.
    /// </summary>
    private (CascadeRuleSet Rules, Dictionary<string, CssKeyframesRule> Keyframes)? GetViewTransitionRules(Element root)
    {
        var text = _document.GetActiveViewTransition(root)?.DynamicStyleSheet;
        if (text is null)
        {
            _viewTransitionRules?.Remove(root);
            return null;
        }

        var cache = _viewTransitionRules ??= new(ReferenceEqualityComparer.Instance);
        if (!cache.TryGetValue(root, out var entry) || !string.Equals(entry.Text, text, StringComparison.Ordinal))
        {
            // The rules of the transitions that ended are released
            if (cache.Count > 0)
            {
                foreach (var key in cache.Keys.ToArray())
                {
                    if (_document.GetActiveViewTransition(key) is null)
                    {
                        cache.Remove(key);
                    }
                }
            }

            var collector = new CascadeRuleCollector(new MediaEnvironment(), CascadeOrigin.UserAgent, treeRoot: null);
            collector.AddRules(ParseUserAgentStyleSheet(text), baseUrl: null);
            entry = (text, collector.Complete(), collector.KeyframesRules);
            cache[root] = entry;
        }

        return (entry.Rules, entry.Keyframes);
    }

    /// <summary>The keyframes of the global view transition user agent style sheet (https://drafts.csswg.org/css-view-transitions-1/#ua-styles).</summary>
    private const string ViewTransitionKeyframesStyleSheet = """
        @keyframes -ua-view-transition-fade-out { to { opacity: 0; } }
        @keyframes -ua-view-transition-fade-in { from { opacity: 0; } }
        @keyframes -ua-mix-blend-mode-plus-lighter { from { mix-blend-mode: plus-lighter } to { mix-blend-mode: plus-lighter } }
        """;

    private static readonly Lazy<Dictionary<string, CssKeyframesRule>> ViewTransitionKeyframes = new(static () =>
    {
        var collector = new CascadeRuleCollector(new MediaEnvironment(), CascadeOrigin.UserAgent, treeRoot: null);
        collector.AddRules(ParseUserAgentStyleSheet(ViewTransitionKeyframesStyleSheet), baseUrl: null);
        collector.Complete();
        return collector.KeyframesRules;
    });

    /// <summary>
    /// Returns the @counter-style rule with the name (https://drafts.csswg.org/css-counter-styles-3/#the-counter-style-rule) and
    /// the root of its tree, dereferenced as a tree-scoped reference (https://drafts.csswg.org/css-scoping/#css-tree-scoped-reference):
    /// the rules of the tree, then the ones of the trees of the shadow hosts, up to the document. Returns null when there is
    /// no such rule (the name may be a predefined counter style).
    /// </summary>
    public (CssCounterStyleRule Rule, Node Scope)? FindCounterStyle(string name, Node? treeScope)
    {
        EnsureUpToDate();
        var root = treeScope ?? _document;
        while (root is Dom.Document or ShadowRoot)
        {
            if (GetTreeStyles(root).CounterStyleRules.TryGetValue(name, out var rule))
                return (rule, root);

            if (root is not ShadowRoot shadowRoot)
                break;

            root = shadowRoot.Host.GetRoot();
        }

        return null;
    }

    /// <summary>
    /// Returns the @position-try rule with the name (https://drafts.csswg.org/css-anchor-position-1/#fallback-rule), dereferenced
    /// as a tree-scoped reference (https://drafts.csswg.org/css-scoping/#css-tree-scoped-reference): the rules of the tree, then
    /// the ones of the trees of the shadow hosts, up to the document. Returns null when there is no such rule.
    /// </summary>
    public CssPositionTryRule? FindPositionTry(string name, Node? treeScope)
    {
        EnsureUpToDate();
        var root = treeScope ?? _document;
        while (root is Dom.Document or ShadowRoot)
        {
            if (GetTreeStyles(root).PositionTryRules.TryGetValue(name, out var rule))
                return rule;

            if (root is not ShadowRoot shadowRoot)
                break;

            root = shadowRoot.Host.GetRoot();
        }

        return null;
    }

    /// <summary>
    /// Returns the rank of the cascade layer of a @font-face rule of the style sheets of the document
    /// (https://drafts.csswg.org/css-cascade-5/#layering): <see cref="CascadeRule.Unlayered"/> for the unlayered rules, and
    /// for the rules that do not apply (e.g. in a @media rule whose condition is false).
    /// </summary>
    public int GetFontFaceLayer(CssFontFaceRule rule)
    {
        EnsureUpToDate();
        return GetTreeStyles(_document).FontFaceLayers.TryGetValue(rule, out var layer) ? layer : CascadeRule.Unlayered;
    }

    /// <summary>
    /// Returns the @font-face rules of the style sheets of a tree (a document or a shadow root) that apply (e.g. not in a
    /// @media rule whose condition is false), in order, with the rank of their cascade layer.
    /// </summary>
    internal IReadOnlyDictionary<CssFontFaceRule, int> GetFontFaceRules(Node root)
    {
        EnsureUpToDate();
        return GetTreeStyles(root).FontFaceLayers;
    }

    /// <summary>
    /// Returns the @font-feature-values rules of the style sheets of the document that apply (e.g. not in a @media rule whose
    /// condition is false), from the weakest cascade layer to the strongest one, then in order.
    /// </summary>
    internal IReadOnlyList<CssFontFeatureValuesRule> GetFontFeatureValuesRules()
    {
        EnsureUpToDate();
        return GetTreeStyles(_document).FontFeatureValuesRules;
    }

    /// <summary>
    /// Returns the @font-palette-values rules of the style sheets of the document that apply (e.g. not in a @media rule whose
    /// condition is false), from the weakest cascade layer to the strongest one, then in order.
    /// </summary>
    internal IReadOnlyList<CssFontPaletteValuesRule> GetFontPaletteValuesRules()
    {
        EnsureUpToDate();
        return GetTreeStyles(_document).FontPaletteValuesRules;
    }

    /// <summary>
    /// Returns the origin of the winning declaration of a longhand property of an element (https://drafts.csswg.org/css-cascade-5/#cascade-origin;
    /// the presentational hints and the style attribute are in the author origin), or null when no declaration of the
    /// property applies to the element (the value is inherited or initial). The cascade of the element runs again: the
    /// origins are not kept with the cascaded values. A declaration that rolls back the cascade (revert, revert-layer,
    /// revert-rule) is the winning declaration.
    /// </summary>
    internal CascadeOrigin? GetCascadeOrigin(Element element, string property)
    {
        // The styles the cascade reads (e.g. the ones of the query containers) are computed first
        var style = GetStyle(element);
        _originQueryProperty = property;
        _originQueryResult = null;
        try
        {
            Cascade(element, pseudoElement: null, style.Parent, out _);
            return _originQueryResult;
        }
        finally
        {
            _originQueryProperty = null;
            _originQueryResult = null;
        }
    }

    /// <summary>Returns the computed style of an element, or of one of its pseudo-elements (e.g. "before").</summary>
    public ComputedStyle GetStyle(Element element, string? pseudoElement = null)
    {
        EnsureUpToDate();

        // The styles are computed for one version: a change of the versions while they are computed (e.g. when a style reads
        // the environment) is seen by the next read, so that a style never inherits from
        // the style of its parent of another generation (see SuspendUpdates)
        _updatesSuspended++;
        try
        {
            return GetStyleCore(element, pseudoElement);
        }
        finally
        {
            _updatesSuspended--;
        }
    }

    private ComputedStyle GetStyleCore(Element element, string? pseudoElement)
    {
        if (_fullRecalcPending && !_recalculating)
        {
            _fullRecalcPending = false;
            _recalculating = true;
            try
            {
                RecalculateAllStyles();
            }
            finally
            {
                _recalculating = false;
            }
        }

        if (_styles.TryGetValue((element, pseudoElement), out var style))
            return style;

        // The ancestors are computed first (iteratively, the trees can be deep)
        if (pseudoElement is null)
        {
            var missing = new List<Element>();
            for (var ancestor = GetInheritanceParent(element); ancestor is not null && !_styles.ContainsKey((ancestor, null)); ancestor = GetInheritanceParent(ancestor))
            {
                missing.Add(ancestor);
            }

            // (a style may be computed by the computation of another one, e.g. when the styles are computed again after a change
            // of the style sheets: it is not computed twice, its descendants would inherit from the first one)
            for (var i = missing.Count - 1; i >= 0; i--)
            {
                if (!_styles.ContainsKey((missing[i], null)))
                {
                    ComputeStyle(missing[i], pseudoElement: null);
                }
            }
        }
        else if (IsHighlightPseudoElement(pseudoElement))
        {
            // The highlight pseudo-elements inherit from the highlight pseudo-elements of the ancestors, computed first
            var missing = new List<Element>();
            for (var ancestor = GetInheritanceParent(element); ancestor is not null && !_styles.ContainsKey((ancestor, pseudoElement)); ancestor = GetInheritanceParent(ancestor))
            {
                missing.Add(ancestor);
            }

            for (var i = missing.Count - 1; i >= 0; i--)
            {
                if (!_styles.ContainsKey((missing[i], pseudoElement)))
                {
                    ComputeStyle(missing[i], pseudoElement);
                }
            }
        }

        return _styles.TryGetValue((element, pseudoElement), out style) ? style : ComputeStyle(element, pseudoElement);
    }

    /// <summary>
    /// Returns the style of the instance of an element in a use-element shadow tree (https://svgwg.org/svg2-draft/struct.html#UseShadowTree),
    /// which the painting does not clone: the element instance inherits from <paramref name="parent"/> (the style of the use element
    /// for the root of the shadow tree, the instance of its parent otherwise), and the selectors are matched as in the shadow tree
    /// (https://svgwg.org/svg2-draft/struct.html#UseStyleInheritance): the style sheets of the document apply, but the combinators
    /// do not cross the root of the shadow tree (<paramref name="instanceRoot"/>, the element referenced by the use element), which
    /// has no parent nor sibling. The animations do not apply to the element instances.
    /// </summary>
    public ComputedStyle GetUseInstanceStyle(Element element, ComputedStyle parent, Element instanceRoot)
    {
        EnsureUpToDate();
        var key = (element, parent);
        if (_useInstanceStyles is not null && _useInstanceStyles.TryGetValue(key, out var cached))
            return cached;

        var previousRoot = _useInstanceRoot;
        _useInstanceRoot = instanceRoot;
        Dictionary<string, CascadedValue> cascaded;
        bool shared;
        try
        {
            cascaded = Cascade(element, pseudoElement: null, parent, out shared);
        }
        finally
        {
            _useInstanceRoot = previousRoot;
        }

        var style = new ComputedStyle(this, element, pseudoElement: null, parent, cascaded, shared) { IsCascadeOfRules = true, UseInstanceRoot = instanceRoot };
        (_useInstanceStyles ??= new(UseInstanceKeyComparer.Instance))[key] = style;
        return style;
    }

    private sealed class UseInstanceKeyComparer : IEqualityComparer<(Element Element, ComputedStyle Parent)>
    {
        public static UseInstanceKeyComparer Instance { get; } = new();

        public bool Equals((Element Element, ComputedStyle Parent) x, (Element Element, ComputedStyle Parent) y) => ReferenceEquals(x.Element, y.Element) && ReferenceEquals(x.Parent, y.Parent);

        public int GetHashCode((Element Element, ComputedStyle Parent) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Element), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Parent));
    }

    /// <summary>Returns whether a rule of the style sheets of a tree (the document or a shadow root) declares a property whose value may reference an image to fetch.</summary>
    public bool TreeReferencesImages(Node root)
    {
        EnsureUpToDate();
        return GetTreeStyles(root).Rules.ReferencesImages;
    }

    /// <summary>Returns whether a rule of the style sheets of a tree (the document or a shadow root) declares a property whose value may reference an element of another document to fetch.</summary>
    public bool TreeReferencesExternalResources(Node root)
    {
        EnsureUpToDate();
        return GetTreeStyles(root).Rules.ReferencesExternalResources;
    }

    private ComputedStyle ComputeStyle(Element element, string? pseudoElement)
    {
        var parent = pseudoElement is not null ? GetPseudoElementParentStyle(element, pseudoElement) : GetInheritanceParent(element) is { } parentElement ? _styles.GetValueOrDefault((parentElement, null)) ?? GetStyle(parentElement) : null;

        // The children of a details element other than its summary are slotted in its ::details-content pseudo-element
        // (https://html.spec.whatwg.org/multipage/rendering.html#the-details-and-summary-elements): they inherit from it,
        // as its own pseudo-elements do (e.g. ::details-content::before)
        if (pseudoElement is null ? IsInDetailsContent(element) : pseudoElement.StartsWith(DetailsContentPrefix, StringComparison.Ordinal))
        {
            parent = GetStyle(pseudoElement is null ? element.ParentElement! : element, DetailsContent);
        }

        // https://drafts.csswg.org/css-pseudo-4/#highlight-cascade: the highlight pseudo-element of the parent element in the flat tree
        ComputedStyle? highlightParent = null;
        if (IsHighlightPseudoElement(pseudoElement) && GetInheritanceParent(element) is { } highlightParentElement)
        {
            highlightParent = _styles.GetValueOrDefault((highlightParentElement, pseudoElement)) ?? GetStyle(highlightParentElement, pseudoElement);
        }

        // The children of a select element rendered as a drop-down box other than its button are slotted in its select
        // popover (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2): they inherit from
        // ::picker(select), as its own pseudo-elements do (e.g. ::picker(select)::before)
        if (pseudoElement is null ? IsInSelectPicker(element) : pseudoElement.StartsWith(SelectPickerPrefix, StringComparison.Ordinal))
        {
            parent = GetStyle(pseudoElement is null ? element.ParentElement! : element, SelectPicker);
        }

        var cascaded = Cascade(element, pseudoElement, parent, out var shared);
        var style = new ComputedStyle(this, element, pseudoElement, parent, cascaded, shared) { HighlightParent = highlightParent, IsCascadeOfRules = true };
        if (pseudoElement is null && element is HtmlSelectElement && HasBaseAppearance(style))
        {
            // The rules of the user agent of the base appearance replace the ones of the native appearance
            _selectHasBaseAppearance = true;
            try
            {
                cascaded = Cascade(element, pseudoElement, parent, out shared);
            }
            finally
            {
                _selectHasBaseAppearance = false;
            }

            style = new ComputedStyle(this, element, pseudoElement, parent, cascaded, shared) { HighlightParent = highlightParent, IsCascadeOfRules = true };
        }
        else if (pseudoElement is null && HasControlBaseAppearance(style))
        {
            // The Basic Appearance User Agent Stylesheet applies to the control (see StyleEngine.FormControls.cs)
            var previous = _controlWithBaseAppearance;
            _controlWithBaseAppearance = element;
            try
            {
                cascaded = Cascade(element, pseudoElement, parent, out shared);
            }
            finally
            {
                _controlWithBaseAppearance = previous;
            }

            style = new ComputedStyle(this, element, pseudoElement, parent, cascaded, shared) { HighlightParent = highlightParent };
        }

        style.Generation = _version;
        _styles[(element, pseudoElement)] = style;
        if (pseudoElement is not null)
        {
            AddPseudoElementStyleName(element, pseudoElement);
        }
        else if (element is WebVtt.WebVttElement)
        {
            _hasWebVttStyles = true;
        }

        // The CSS transitions and animations of the element are created or updated from its style (by the style engine of the
        // document only): the transitions compare the style with the style before the change
        _beforeChangeStyles.Remove((element, pseudoElement), out var beforeChange);
        if (beforeChange is not null)
        {
            // Kept for this generation: the style is computed again, with the same before-change style, when the size of its
            // query container is known (see InvalidateQueryContainerDescendants)
            (_usedBeforeChangeStyles ??= [])[(element, pseudoElement)] = beforeChange;
        }
        var mayHaveTransitions = _environmentOverride is null && CssTransitionUpdater.MayHaveTransitions(style);
        if (mayHaveTransitions && beforeChange is not null)
        {
            beforeChange = beforeChange.WithCurrentAnimations();
        }

        // An element without before-change style (e.g. inserted, or not rendered) transitions from its starting style
        // (https://drafts.csswg.org/css-transitions-2/#defining-before-change-style)
        if (mayHaveTransitions && (beforeChange is null || beforeChange.IsDisplayNoneForAnimations()))
        {
            beforeChange = TreeHasStartingStyleRules(element) ? ComputeStartingStyle(element, pseudoElement, parent, highlightParent) : null;
        }

        var mayHaveAnimations = _environmentOverride is null && CssAnimationUpdater.MayHaveAnimations(style);
        if (mayHaveTransitions || mayHaveAnimations)
        {
            // The changes of the transitions and animations by a style that the layout may compute again with the size of its
            // query container are undone then (see InvalidateQueryContainerDescendants): they are recorded from the first
            // computation of the style in this generation, whose before-change style the next computations use
            TransitionChanges? transitionChanges = null;
            AnimationChanges? animationChanges = null;
            if (ContainerQueries.IsInSizeContainer(style))
            {
                if (mayHaveTransitions)
                {
                    ref var changes = ref CollectionsMarshal.GetValueRefOrAddDefault(_transitionChanges ??= [], (element, pseudoElement), out _);
                    transitionChanges = changes ??= new TransitionChanges(CssTransition.LastGeneration);
                }

                if (mayHaveAnimations)
                {
                    ref var changes = ref CollectionsMarshal.GetValueRefOrAddDefault(_animationChanges ??= [], (element, pseudoElement), out _);
                    animationChanges = changes ??= new AnimationChanges(_document.AnimationsOrNull?.GetCssAnimations(element, pseudoElement) is { } animations ? [.. animations] : null);
                }
            }

            var changed = false;
            using (_document.Animations.SuppressInvalidation())
            {
                if (mayHaveTransitions)
                {
                    changed = CssTransitionUpdater.Update(style, beforeChange, transitionChanges);
                }

                if (mayHaveAnimations)
                {
                    changed |= CssAnimationUpdater.Update(style, animationChanges);
                }
            }

            // The style is computed again with the effects of the animations (the values computed so far may be animated)
            if (changed)
            {
                var replaced = style;
                style = new ComputedStyle(this, element, pseudoElement, parent, cascaded, shared) { HighlightParent = highlightParent, IsCascadeOfRules = true, Generation = _version };
                _styles[(element, pseudoElement)] = style;
                if (pseudoElement is null)
                {
                    DiscardStylesInheritingFrom(element, replaced);
                }
            }
        }

        return style;
    }

    /// <summary>
    /// Discards the styles of the pseudo-elements and of the descendants of an element computed with a style of the element that
    /// was replaced by its style with the effects of its new animations (e.g. by a flush of the styles of the animations while
    /// the animations were updated): they are computed again with the new style, with the same before-change styles.
    /// </summary>
    private void DiscardStylesInheritingFrom(Element element, ComputedStyle replaced)
    {
        var found = false;
        if (_pseudoElementStyleNames.TryGetValue(element, out var names))
        {
            foreach (var name in names)
            {
                found |= _styles.TryGetValue((element, name), out var pseudoStyle) && ReferenceEquals(pseudoStyle.Parent, replaced);
            }
        }

        for (var child = element.FirstElementChild; child is not null && !found; child = child.NextElementSibling)
        {
            found = _styles.ContainsKey((child, null));
        }

        if (!found)
            return;

        AddBoxTreeChange(element);
        var node = (Node?)element;
        while (node is not null)
        {
            if (node is Element current)
            {
                if (!ReferenceEquals(current, element))
                {
                    DiscardStyleOfThisGeneration((current, null));
                }

                if (_pseudoElementStyleNames.Remove(current, out var pseudoNames))
                {
                    foreach (var name in pseudoNames)
                    {
                        DiscardStyleOfThisGeneration((current, name));
                    }
                }
            }

            node = node.GetNextInTreeOrder(element);
        }

        void DiscardStyleOfThisGeneration((Element Element, string? PseudoElement) key)
        {
            if (_styles.Remove(key) && _usedBeforeChangeStyles is not null && _usedBeforeChangeStyles.Remove(key, out var beforeChange))
            {
                _beforeChangeStyles[key] = beforeChange;
            }
        }
    }

    /// <summary>
    /// Returns the style a pseudo-element inherits from: the style of its originating element, or, for a pseudo-element of a
    /// view transition, the style of its parent in the view transition tree
    /// (https://drafts.csswg.org/css-view-transitions-1/#pseudo-element-tree: "its originating pseudo-element is its parent").
    /// </summary>
    private ComputedStyle GetPseudoElementParentStyle(Element element, string pseudoElement)
    {
        if (Layout.ViewTransitionPseudoTree.IsViewTransitionPseudoElement(pseudoElement) && Layout.ViewTransitionPseudoTree.GetParent(_document.GetViewTransitionPseudoTree(element), pseudoElement) is { } parent)
            return GetStyle(element, parent);

        // https://drafts.csswg.org/css-forms-1/#slider-pseudos: the ::slider-fill is in the ::slider-track
        if (pseudoElement == Layout.FormControlContents.SliderFill)
            return GetStyle(element, Layout.FormControlContents.SliderTrack);

        return GetStyle(element);
    }

    /// <summary>
    /// Returns the style of the element of a style computed from the declarations of the user agent origin only: the style of
    /// the element in a document without author style sheet (nor presentational hints), which the colors of the forced colors
    /// mode come from (https://drafts.csswg.org/css-color-adjust-1/#forced-colors-properties). It inherits from the same style,
    /// and the animations do not apply to it.
    /// </summary>
    internal ComputedStyle CreateUserAgentStyle(ComputedStyle style)
    {
        // (the link of a visited style matches :visited: its color is VisitedText)
        var (previous, previousUseInstanceRoot) = (_userAgentOriginOnly, _useInstanceRoot);
        (_userAgentOriginOnly, _useInstanceRoot) = (true, style.UseInstanceRoot);
        try
        {
            var cascaded = Cascade(style.Element, style.PseudoElement, style.Parent, out _, style.IsVisitedStyle ? style.VisitedLink : null);
            return new ComputedStyle(this, style.Element, style.PseudoElement, style.Parent, cascaded, cascadedShared: true, animated: false) { HighlightParent = style.HighlightParent, IncludeTransitions = false };
        }
        finally
        {
            (_userAgentOriginOnly, _useInstanceRoot) = (previous, previousUseInstanceRoot);
        }
    }

    /// <summary>
    /// Returns the cascaded values of the visited style of a style computed by the engine for an element in a visited link (see
    /// <see cref="ComputedStyle.VisitedOrSelf"/>): the cascade of the element where its link matches <c>:visited</c> instead of
    /// <c>:link</c>. Only the painting of the colors uses them.
    /// </summary>
    internal Dictionary<string, CascadedValue> CascadeVisitedStyle(ComputedStyle style)
    {
        var (previousUseInstanceRoot, previousUserAgentOriginOnly, previousStartingStyle) = (_useInstanceRoot, _userAgentOriginOnly, _includeStartingStyle);
        (_useInstanceRoot, _userAgentOriginOnly, _includeStartingStyle) = (style.UseInstanceRoot, false, false);
        try
        {
            return Cascade(style.Element, style.PseudoElement, style.Parent, out _, style.VisitedLink);
        }
        finally
        {
            (_useInstanceRoot, _userAgentOriginOnly, _includeStartingStyle) = (previousUseInstanceRoot, previousUserAgentOriginOnly, previousStartingStyle);
        }
    }

    /// <summary>Whether the cascade only has the declarations of the user agent origin (see <see cref="CreateUserAgentStyle"/>).</summary>
    private bool _userAgentOriginOnly;

    private bool TreeHasStartingStyleRules(Element element) => element.GetRoot() is Dom.Document or ShadowRoot && GetTreeStyles(element.GetRoot()).Rules.HasStartingStyleRules;

    /// <summary>
    /// Computes the starting style of an element (https://drafts.csswg.org/css-transitions-2/#starting-style): the style with the
    /// rules of the @starting-style rules, which inherits from the after-change style of the parent
    /// (https://drafts.csswg.org/css-transitions-2/#starting-style: the transitions of the parent are not used).
    /// </summary>
    private ComputedStyle ComputeStartingStyle(Element element, string? pseudoElement, ComputedStyle? parent, ComputedStyle? highlightParent)
    {
        _includeStartingStyle = true;
        try
        {
            parent = parent?.CreateAfterChangeStyle();
            var cascaded = Cascade(element, pseudoElement, parent, out _);
            return new ComputedStyle(this, element, pseudoElement, parent, cascaded, cascadedShared: true) { HighlightParent = highlightParent, IncludeTransitions = false };
        }
        finally
        {
            _includeStartingStyle = false;
        }
    }

    /// <summary>
    /// Returns whether the pseudo-element is a highlight pseudo-element (https://drafts.csswg.org/css-pseudo-4/#highlight-pseudo-element):
    /// <c>::selection</c>, <c>::search-text</c>, <c>::target-text</c>, <c>::spelling-error</c>, <c>::grammar-error</c> and <c>::highlight()</c>.
    /// </summary>
    internal static bool IsHighlightPseudoElement(string? pseudoElement) => GetPseudoElementName(pseudoElement) is "selection" or "search-text" or "target-text" or "spelling-error" or "grammar-error" or "highlight";

    /// <summary>
    /// Returns the element the element inherits from: its parent in the flat tree
    /// (https://drafts.csswg.org/css-scoping/#flat-tree), that is the assigned slot of a slotted element or the shadow host
    /// of the children of a shadow root; the parent element otherwise.
    /// </summary>
    /// <summary>The name of the <c>::details-content</c> pseudo-element (https://drafts.csswg.org/css-pseudo-4/#details-content-pseudo).</summary>
    internal const string DetailsContent = "details-content";

    /// <summary>
    /// The prefix of the names of the pseudo-elements of <c>::details-content</c>, an element-backed pseudo-element
    /// (https://drafts.csswg.org/css-pseudo-4/#element-backed): "details-content::before", "details-content::after",
    /// "details-content::first-line" and "details-content::first-letter" (the other sub-pseudo-elements are not supported).
    /// </summary>
    internal const string DetailsContentPrefix = DetailsContent + "::";

    /// <summary>Whether the element is a child of a details element that is slotted in its <c>::details-content</c> pseudo-element (not its summary).</summary>
    internal static bool IsInDetailsContent(Element element)
    {
        if (element.ParentNode is not Element parent || !parent.IsHtml(TagId.Details) || element.Slottable?.AssignedSlot is not null)
            return false;

        if (!element.IsHtml(TagId.Summary))
            return true;

        // Only the first summary child is the summary of the details element
        for (var sibling = element.PreviousElementSibling; sibling is not null; sibling = sibling.PreviousElementSibling)
        {
            if (sibling.IsHtml(TagId.Summary))
                return true;
        }

        return false;
    }

    internal static Element? GetInheritanceParent(Element element)
    {
        if (element.Slottable?.AssignedSlot is { } slot)
            return slot;

        return element.ParentNode switch
        {
            Element parent => parent,
            ShadowRoot shadowRoot => shadowRoot.Host,

            // https://w3c.github.io/webvtt/#applying-css-properties: the root list of WebVTT node objects of a cue and the
            // WebVTT region objects inherit from the media element the cue is rendered for
            null when element is WebVtt.WebVttElement vttElement => vttElement.Host,
            _ => null,
        };
    }

    /// <summary>
    /// Suspends the checks of the versions the computed styles depend on until the returned scope is disposed: the layout reads
    /// the styles of the whole document for one version (see <see cref="Layout.LayoutEngine"/>). Without it, a change of a
    /// version (e.g. <see cref="Document.StyleSheetsVersion"/>) in the middle of the box tree construction would start a new
    /// generation of styles, and lose the styles computed so far as the before-change styles of the transitions
    /// (https://drafts.csswg.org/css-transitions-1/#before-change-style).
    /// </summary>
    internal UpdateSuspension SuspendUpdates()
    {
        _updatesSuspended++;
        return new UpdateSuspension(this);
    }

    private int _updatesSuspended;

    /// <summary>The scope returned by <see cref="SuspendUpdates"/>.</summary>
    internal readonly struct UpdateSuspension(StyleEngine engine) : IDisposable
    {
        public void Dispose() => engine._updatesSuspended--;
    }

    private void EnsureUpToDate()
    {
        if (_updatesSuspended > 0)
            return;

        // The SMIL animations are sampled when something changed: their values are presentation attributes and properties
        _document.SmilOrNull?.Flush();

        var environment = _environmentOverride ?? CreateEnvironment(_document);
        var domVersion = _document.DomVersion;
        var elementStateVersion = _document.ElementStateVersion;
        var styleVersion = _document.StyleSheetsVersion;
        var animationVersion = _document.AnimationsOrNull?.Version ?? 0;

        // The computed values of the font-relative units depend on the fonts (e.g. a web font was loaded)
        var fontVersion = _document.FontSelectorOrNull?.Version ?? 0;

        // The container scroll-state queries depend on the snapshots of the scroll-state query containers (see ContainerQueries)
        // (and the :target-current, :target-before and :target-after pseudo-classes and the :enabled and :disabled pseudo-classes
        // of the scroll buttons on the snapshot of the scroll navigation controls: the sum changes when one of the versions does)
        var scrollStateVersion = _environmentOverride is null && _document.LayoutEngineOrNull is { } layoutEngine ? layoutEngine.ScrollStateSnapshotVersion + layoutEngine.ScrollMarkerSnapshotVersion : 0;

        // The links that match :visited depend on the history of the visited links and on the URL of the document, which their
        // URLs are relative to (the other changes of the base URL are changes of the tree)
        var visitedLinks = _document.VisitedLinks;
        var visitedLinksVersion = visitedLinks?.Version ?? 0;
        var visitedLinksDocumentUrl = visitedLinks is null ? null : _document.UrlRecord;
        if (domVersion == _domVersion && elementStateVersion == _elementStateVersion && styleVersion == _styleVersion && environment == _environment && animationVersion == _animationVersion && fontVersion == _fontVersion
            && scrollStateVersion == _scrollStateVersion && ReferenceEquals(visitedLinks, _visitedLinks) && visitedLinksVersion == _visitedLinksVersion && ReferenceEquals(visitedLinksDocumentUrl, _visitedLinksDocumentUrl))
        {
            return;
        }

        _version++;
        _useInstanceStyles?.Clear();
        _usedBeforeChangeStyles?.Clear();
        CommitPendingCancellations();
        _transitionChanges?.Clear();
        var domChanged = domVersion != _domVersion;
        if (domChanged)
        {
            RemoveDisconnectedTrees();
            _partElementMaps.Clear();
        }

        // The changes of the document and of the animations since the last update, when they are known element by element and
        // nothing else changed (the style sheets, the environment, the fonts...): only the styles of the affected elements are
        // invalidated (see StyleEngine.Invalidation.cs)
        var globalChange = styleVersion != _styleVersion || environment != _environment || fontVersion != _fontVersion || scrollStateVersion != _scrollStateVersion
            || !ReferenceEquals(visitedLinks, _visitedLinks) || visitedLinksVersion != _visitedLinksVersion || !ReferenceEquals(visitedLinksDocumentUrl, _visitedLinksDocumentUrl);
        var invalidations = ConsumeElementInvalidations(domChanged || elementStateVersion != _elementStateVersion, animationVersion != _animationVersion);

        // The computed values only depend on the cascaded values (and the inherited values) when the style sheets and the
        // environment did not change (e.g. a viewport unit, a font, a registered property)
        BeforeChangeComparableByCascade = styleVersion == _styleVersion && environment == _environment && fontVersion == _fontVersion;

        // https://html.spec.whatwg.org/multipage/images.html#img-environment-changes: the images that use srcset or picture
        // react to the changes of the environment (the size of the viewport, the device pixel ratio...)
        if (_environment is not null && _environmentOverride is null && environment != _environment)
        {
            _document.QueueReactToEnvironmentChangesOfImages();
        }

        _fontVersion = fontVersion;
        _domVersion = domVersion;
        _elementStateVersion = elementStateVersion;
        _styleVersion = styleVersion;
        _environment = environment;
        _animationVersion = animationVersion;
        _scrollStateVersion = scrollStateVersion;
        _visitedLinks = visitedLinks;
        _visitedLinksVersion = visitedLinksVersion;
        _visitedLinksDocumentUrl = visitedLinksDocumentUrl;
        if (!globalChange && invalidations is not null && TryInvalidateElements(invalidations, domChanged))
            return;

        FullInvalidationCount++;
        AddFullBoxTreeChange();
        var previousStyles = _styles;
        _styles = _spareStyles;
        _pseudoElementStyleNames.Clear();
        _hasWebVttStyles = false;
        _sharedCascades.Clear();
        _queriedContainerSizes?.Clear();
        _queriedAnchoredFallbacks?.Clear();
        _registeredProperties = null;
        _viewportUnitGutters = null;

        // The styles before the change are kept for the transitions (https://drafts.csswg.org/css-transitions-1/#before-change-style),
        // and the styles of all the elements are computed again, as browsers recompute the styles after a change: the next
        // change compares the styles with this state
        if (_environmentOverride is null && MayHaveTransitions())
        {
            foreach (var (key, style) in previousStyles)
            {
                _beforeChangeStyles[key] = style;
            }

            if (domChanged)
            {
                PruneBeforeChangeStyles();
            }

            _fullRecalcPending = _document.BrowsingContext is not null;
        }
        else
        {
            _beforeChangeStyles.Clear();
        }

        previousStyles.Clear();
        _spareStyles = previousStyles;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Viewport-percentage lengths (https://drafts.csswg.org/css-values-4/#viewport-relative-lengths)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The scrollbar gutters subtracted by the viewport units, computed once per generation of styles (see <see cref="GetViewportUnitSize"/>).</summary>
    private (double Width, double Height)? _viewportUnitGutters;
    private bool _resolvingViewportUnitGutters;

    /// <summary>
    /// Returns the size the viewport-percentage lengths are relative to (https://drafts.csswg.org/css-values-4/#viewport-relative-lengths):
    /// the size of the viewport of the environment, reduced by the scrollbar gutters that do not depend on the contents
    /// (<c>overflow: scroll</c> or <c>scrollbar-gutter: stable</c> applied to the viewport, see
    /// <see cref="Layout.ScrollContainers.GetViewportUnitGutters"/>). There is no user agent interface that shows and hides
    /// (no dynamic toolbar): the small, large and dynamic viewports are the same. The media queries use the whole viewport
    /// (<see cref="MediaEnvironment.Width"/>, "and always in the case of media queries").
    /// </summary>
    /// <remarks>
    /// The gutters depend on computed values of the root and body elements: a viewport unit used by one of these properties
    /// (e.g. through <c>if()</c>, a cycle) uses the size of the viewport without gutters.
    /// </remarks>
    internal (double Width, double Height) GetViewportUnitSize(MediaEnvironment environment, string? pseudoElement = null)
    {
        // The viewport of the paged media is the page area of the first page (https://drafts.csswg.org/css-values-4/#viewport-relative-lengths);
        // the viewport of the page contexts and of the page-margin boxes is the default page box of the printing, as in Blink
        // (https://github.com/w3c/csswg-drafts/issues/5437: they cannot depend on the size of the page)
        if (PrintPageSize is { } pageSize && pseudoElement is not null && pseudoElement.StartsWith(PageContextPseudoElement, StringComparison.Ordinal))
            return (pageSize.Width, pageSize.Height);

        if (PrintViewportSize is { } printViewport)
            return (printViewport.Width, printViewport.Height);

        var gutters = _viewportUnitGutters;
        if (gutters is null && _environmentOverride is null && !_resolvingViewportUnitGutters)
        {
            _resolvingViewportUnitGutters = true;
            try
            {
                gutters = Layout.ScrollContainers.GetViewportUnitGutters(this);
            }
            finally
            {
                _resolvingViewportUnitGutters = false;
            }

            _viewportUnitGutters = gutters;
        }

        if (gutters is not { } value)
            return (environment.Width, environment.Height);

        return (Math.Max(0, environment.Width - value.Width), Math.Max(0, environment.Height - value.Height));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Size container queries (https://drafts.csswg.org/css-conditional-5/#size-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The sizes of the query containers that the styles of this generation were computed with (see
    /// <see cref="RecordQueriedContainerSize"/>), by container element.
    /// </summary>
    private Dictionary<Element, QueriedContainerSize>? _queriedContainerSizes;

    /// <summary>The before-change styles used by the styles computed in this generation (see <see cref="InvalidateQueryContainerDescendants"/>).</summary>
    private Dictionary<(Element Element, string? PseudoElement), ComputedStyle>? _usedBeforeChangeStyles;

    /// <summary>
    /// The changes of the transitions by the computations of the styles in a size container in this generation, undone when the
    /// styles are discarded (see <see cref="InvalidateQueryContainerDescendants"/>).
    /// </summary>
    private Dictionary<(Element Element, string? PseudoElement), TransitionChanges>? _transitionChanges;

    /// <summary>
    /// The changes of the CSS animations by the computations of the styles in a size container since the styles were settled,
    /// undone when the styles are discarded (see <see cref="InvalidateQueryContainerDescendants"/>).
    /// </summary>
    private Dictionary<(Element Element, string? PseudoElement), AnimationChanges>? _animationChanges;

    /// <summary>
    /// Whether the size container queries use the sizes computed by the layout of the document (see <see cref="ContainerQueries"/>):
    /// false for an engine whose environment is overridden (it does not compute the styles of the rendering).
    /// </summary>
    internal bool UsesLayoutContainerSizes => _environmentOverride is null || PrintViewportSize is not null;

    /// <summary>
    /// The size of the viewport of the styles of the printing of the document (see <see cref="Layout.PrintSession"/>): the page
    /// area of the first page, which the viewport-relative lengths refer to; null for the other style engines.
    /// </summary>
    internal Layout.PhysicalSize? PrintViewportSize { get; init; }

    /// <summary>
    /// The size of the default page box of the printing of the document (the page size of the options, see
    /// <see cref="Layout.PrintSession"/>), which the viewport-relative lengths of the page contexts refer to; null for the other
    /// style engines.
    /// </summary>
    internal Layout.PhysicalSize? PrintPageSize { get; init; }

    /// <summary>
    /// Records the size of a query container in a physical axis used by a size container query or a container-relative length
    /// (null when it is not known): the layout compares it with the size of the container once it is laid out (see
    /// <see cref="IsQueriedWithAnotherSize"/>).
    /// </summary>
    internal void RecordQueriedContainerSize(Element container, bool horizontal, double? size)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_queriedContainerSizes ??= new(ReferenceEqualityComparer.Instance), container, out _);
        entry.Record(horizontal, size);
    }

    /// <summary>
    /// Whether a style of this generation queried the size of a query container (a size container query or a container-relative
    /// length): the styles may then depend on the layout (see <see cref="ContainerQueries.GetStyleWithContainerLayout"/>).
    /// </summary>
    internal bool HasQueriedContainerSizes => _queriedContainerSizes is { Count: > 0 };

    /// <summary>
    /// Returns whether the styles of this generation queried the size of a container with a size other than the size of its
    /// content box (in the coordinates of its computed values; a size is null when it is not known): its
    /// descendants must be styled again (see <see cref="InvalidateQueryContainerDescendants"/>).
    /// </summary>
    internal bool IsQueriedWithAnotherSize(Element container, double? width, double? height)
        => _queriedContainerSizes is not null && _queriedContainerSizes.TryGetValue(container, out var entry) && entry.Differs(width, height);

    /// <summary>
    /// Discards the computed styles of the descendants of a query container in the flat tree and of its pseudo-elements (the
    /// elements whose container queries and container-relative lengths may refer to it), so that they are computed again with
    /// its size: the style and layout interleaving of the size container queries (https://drafts.csswg.org/css-contain-3/#size-container).
    /// The styles keep the before-change styles of their first computation (the transitions compare the new styles with the styles
    /// before the change), and the sizes queried by the descendants are forgotten.
    /// </summary>
    internal void InvalidateQueryContainerDescendants(Element container)
    {
        AddBoxTreeChange(container);
        var subtree = new Dictionary<Element, bool>(ReferenceEqualityComparer.Instance) { [container] = true };
        var keys = new List<(Element Element, string? PseudoElement)>();
        foreach (var key in _styles.Keys)
        {
            if (ReferenceEquals(key.Element, container) ? key.PseudoElement is not null : IsInSubtree(subtree, key.Element))
            {
                keys.Add(key);
            }
        }

        var mayHaveTransitions = _environmentOverride is null && MayHaveTransitions();
        foreach (var key in keys)
        {
            _styles.Remove(key, out var discarded);
            if (_usedBeforeChangeStyles is not null && _usedBeforeChangeStyles.Remove(key, out var beforeChange))
            {
                _beforeChangeStyles[key] = beforeChange;
            }
            else if (mayHaveTransitions && discarded!.Generation != _version)
            {
                // A style kept from a previous generation (the styles are invalidated element by element) is the style before the change
                _beforeChangeStyles[key] = discarded;
            }

            if (_transitionChanges is not null && _transitionChanges.Remove(key, out var changes))
            {
                UndoTransitionChanges(key, changes);
            }

            if (_animationChanges is not null && _animationChanges.Remove(key, out var animationChanges))
            {
                UndoAnimationChanges(key, animationChanges);
            }
        }

        if (_queriedContainerSizes is { Count: > 0 } queried)
        {
            List<Element>? removed = null;
            foreach (var element in queried.Keys)
            {
                if (!ReferenceEquals(element, container) && IsInSubtree(subtree, element))
                {
                    (removed ??= []).Add(element);
                }
            }

            if (removed is not null)
            {
                foreach (var element in removed)
                {
                    queried.Remove(element);
                }
            }
        }

        _queriedContainerSizes?.Remove(container);
        if (_queriedAnchoredFallbacks is { Count: > 0 } queriedFallbacks)
        {
            List<Element>? removed = null;
            foreach (var element in queriedFallbacks.Keys)
            {
                if (ReferenceEquals(element, container) || IsInSubtree(subtree, element))
                {
                    (removed ??= []).Add(element);
                }
            }

            if (removed is not null)
            {
                foreach (var element in removed)
                {
                    queriedFallbacks.Remove(element);
                }
            }
        }

        // Whether an element is a descendant of the container in the flat tree, memoized for its ancestors
        static bool IsInSubtree(Dictionary<Element, bool> subtree, Element element)
        {
            List<Element>? chain = null;
            var result = false;
            for (Element? current = element; current is not null; current = GetInheritanceParent(current))
            {
                if (subtree.TryGetValue(current, out result))
                    break;

                (chain ??= []).Add(current);
            }

            if (chain is not null)
            {
                foreach (var item in chain)
                {
                    subtree[item] = result;
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Undoes the changes of the transitions of an element by its discarded style (see <see cref="TransitionChanges"/>): the new
    /// style updates the transitions as they were before its first computation.
    /// </summary>
    private void UndoTransitionChanges((Element Element, string? PseudoElement) key, TransitionChanges changes)
    {
        if (_document.AnimationsOrNull is not { } animations)
            return;

        // (the style of the element is computed again: the changes do not invalidate it)
        using var suppression = animations.SuppressInvalidation();

        // The transitions started by the discarded style are canceled: the new style starts its own (e.g. from a
        // @starting-style rule in a @container rule). They were not sampled yet, so no event is fired
        var transitions = animations.GetTransitions(key.Element, key.PseudoElement);
        if (transitions is not null)
        {
            List<string>? started = null;
            foreach (var (property, transition) in transitions.Running)
            {
                if (transition.TransitionGeneration > changes.Generation)
                {
                    (started ??= []).Add(property);
                }
            }

            if (started is not null)
            {
                foreach (var property in started)
                {
                    transitions.Running.Remove(property, out var transition);
                    transition!.Discard();
                }
            }
        }

        // The transitions it canceled run again (e.g. when the container was laid out with another size to measure it), except
        // the ones started by the discarded computations
        if (changes.PendingCancellations is { } canceled)
        {
            transitions ??= animations.GetOrCreateTransitions(key.Element, key.PseudoElement);
            foreach (var transition in canceled)
            {
                transition.IsCancellationPending = false;
                if (transition.TransitionGeneration > changes.Generation)
                {
                    transition.Discard();
                }
                else
                {
                    transitions.Running[transition.TransitionProperty] = transition;
                }
            }
        }

        if (changes.RemovedCompleted is { } completed)
        {
            transitions ??= animations.GetOrCreateTransitions(key.Element, key.PseudoElement);
            foreach (var (property, value) in completed)
            {
                transitions.Completed.TryAdd(property, value);
            }
        }

        animations.RemoveTransitionsIfEmpty(key.Element, key.PseudoElement);
    }

    /// <summary>
    /// Undoes the changes of the CSS animations of an element by its discarded style (see <see cref="AnimationChanges"/>): the new
    /// style updates the animations as they were before its first computation. The animations created by the discarded style were
    /// not sampled yet, so no event is fired.
    /// </summary>
    private void UndoAnimationChanges((Element Element, string? PseudoElement) key, AnimationChanges changes)
    {
        if (_document.AnimationsOrNull is not { } animations)
            return;

        // (the style of the element is computed again: the changes do not invalidate it)
        using var suppression = animations.SuppressInvalidation();
        var before = changes.Animations ?? [];
        if (animations.GetCssAnimations(key.Element, key.PseudoElement) is { } current)
        {
            foreach (var animation in current)
            {
                if (!before.Contains(animation))
                {
                    animation.Discard();
                }
            }
        }

        // The animations it canceled run again, except the ones created by the discarded computations
        if (changes.PendingCancellations is { } canceled)
        {
            foreach (var animation in canceled)
            {
                animation.IsCancellationPending = false;
                if (!before.Contains(animation))
                {
                    animation.Discard();
                }
            }
        }

        animations.SetCssAnimations(key.Element, key.PseudoElement, before);
    }

    /// <summary>
    /// Cancels the transitions and animations whose cancellation is pending (see <see cref="Animation.IsCancellationPending"/>):
    /// the styles are settled (a new generation of styles starts, or the animations are flushed, sampled or read), and the styles
    /// that canceled them are no longer discarded by the layout. The <c>transitioncancel</c> and <c>animationcancel</c> events
    /// are queued. The styles do not change: the transitions and animations already had no effect. The CSS animations of the
    /// settled styles are no longer undone (e.g. an animation created by them may be sampled from now on).
    /// </summary>
    internal void CommitPendingCancellations()
    {
        if ((_transitionChanges is null && _animationChanges is null) || _document.AnimationsOrNull is not { } animations)
            return;

        using (animations.SuppressInvalidation())
        {
            if (_transitionChanges is not null)
            {
                foreach (var changes in _transitionChanges.Values)
                {
                    if (changes.PendingCancellations is { } canceled)
                    {
                        changes.PendingCancellations = null;
                        foreach (var transition in canceled)
                        {
                            transition.IsCancellationPending = false;
                            transition.CancelByStyle();
                        }
                    }
                }
            }

            if (_animationChanges is not null)
            {
                foreach (var changes in _animationChanges.Values)
                {
                    if (changes.PendingCancellations is { } canceled)
                    {
                        foreach (var animation in canceled)
                        {
                            animation.IsCancellationPending = false;
                            animation.CancelByStyle();
                        }
                    }
                }

                _animationChanges.Clear();
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Anchored container queries (https://drafts.csswg.org/css-anchor-position-2/#anchored-container-queries)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The position options of the anchored query containers that the styles of this generation were computed with (the entry of
    /// <c>position-try-fallbacks</c>, "" for the base styles; null when two styles used different options), by container element.
    /// </summary>
    private Dictionary<Element, string?>? _queriedAnchoredFallbacks;

    /// <summary>
    /// Records the position option of an anchored query container used by an anchored query ("" for the base styles): the layout
    /// compares it with the option it applies to the container (see <see cref="IsQueriedWithAnotherFallback"/>).
    /// </summary>
    internal void RecordQueriedAnchoredFallback(Element container, string option)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_queriedAnchoredFallbacks ??= new(ReferenceEqualityComparer.Instance), container, out var exists);
        entry = !exists || string.Equals(entry, option, StringComparison.Ordinal) ? option : null;
    }

    /// <summary>
    /// Returns whether the styles of this generation queried another position option of an anchored query container than the one
    /// its layout applies ("" for the base styles): its descendants must be styled again (see <see cref="InvalidateQueryContainerDescendants"/>).
    /// </summary>
    internal bool IsQueriedWithAnotherFallback(Element container, string option)
        => _queriedAnchoredFallbacks is not null && _queriedAnchoredFallbacks.TryGetValue(container, out var queried) && !string.Equals(queried, option, StringComparison.Ordinal);

    /// <summary>The sizes of a query container used by the styles (see <see cref="RecordQueriedContainerSize"/>).</summary>
    private struct QueriedContainerSize
    {
        private double? _width;
        private double? _height;
        private bool _widthQueried;
        private bool _heightQueried;

        /// <summary>Whether two styles used different sizes (e.g. a style computed before the layout of the container and one after).</summary>
        private bool _inconsistent;

        public void Record(bool horizontal, double? size)
        {
            if (horizontal)
            {
                _inconsistent |= _widthQueried && _width != size;
                _width = size;
                _widthQueried = true;
            }
            else
            {
                _inconsistent |= _heightQueried && _height != size;
                _height = size;
                _heightQueried = true;
            }
        }

        public readonly bool Differs(double? width, double? height) => _inconsistent || (_widthQueried && _width != width) || (_heightQueried && _height != height);
    }

    /// <summary>Returns whether the styles of the document may start transitions or have declarative animations.</summary>
    private bool MayHaveTransitions() =>
        _document.InlineStylesDeclareAnimations || _inlineStylesDeclareAnimations || _shadowTreesDeclareAnimations || _document.HasViewTransitionPseudoTrees
        || _document.AnimationsOrNull?.HasDeclarativeAnimations == true || GetTreeStyles(_document).Rules.DeclaresAnimations;

    /// <summary>
    /// Computes the styles of all the elements of the document (and of their ::marker, ::before and ::after pseudo-elements when
    /// the rules may apply to them): the transitions of the elements whose style changed start.
    /// </summary>
    private void RecalculateAllStyles()
    {
        var pseudoElements = new List<string>(3);
        var rules = GetTreeStyles(_document).Rules;
        foreach (var pseudoElement in (ReadOnlySpan<string>)["marker", "before", "after"])
        {
            if (rules.MayHaveRulesFor(pseudoElement))
            {
                pseudoElements.Add(pseudoElement);
            }
        }

        foreach (var node in _document.ShadowIncludingInclusiveDescendants())
        {
            if (node is not Element element)
                continue;

            GetStyle(element);
            foreach (var pseudoElement in pseudoElements)
            {
                GetStyle(element, pseudoElement);
            }
        }

        // The pseudo-elements of the active view transition
        _document.ComputeViewTransitionPseudoElementStyles();
    }

    /// <summary>
    /// Removes the style rules of the shadow trees that are no longer in the document, so that the removed shadow roots
    /// and their hosts are not retained by the document (the style rules are collected again if a tree is inserted back).
    /// </summary>
    private void RemoveDisconnectedTrees()
    {
        List<Node>? removed = null;
        foreach (var root in _trees.Keys)
        {
            if (root is ShadowRoot && (!root.IsConnected || !ReferenceEquals(root.NodeDocument, _document)))
            {
                (removed ??= []).Add(root);
            }
        }

        if (removed is not null)
        {
            foreach (var root in removed)
            {
                _trees.Remove(root);
            }
        }
    }

    /// <summary>
    /// The environments computed by the outermost <see cref="CreateEnvironment"/> call: the environment of a nested document
    /// depends on the style of its container, whose document's environment is computed again by each style lookup (the cost
    /// would be exponential in the nesting depth).
    /// </summary>
    [ThreadStatic]
    private static Dictionary<Document, MediaEnvironment>? s_environments;

    /// <summary>Returns the environment of a document: the viewport of its browsing context (the size of the iframe for a nested browsing context).</summary>
    internal static MediaEnvironment CreateEnvironment(Document document)
    {
        // The environment of a top-level document does not depend on a style
        if (document.BrowsingContext is not { Container: not null })
            return CreateEnvironmentCore(document);

        var environments = s_environments;
        if (environments is not null && environments.TryGetValue(document, out var environment))
            return environment;

        var isOutermost = environments is null;
        if (isOutermost)
        {
            s_environments = environments = [];
        }

        try
        {
            environment = CreateEnvironmentCore(document);
            environments![document] = environment;
            return environment;
        }
        finally
        {
            if (isOutermost)
            {
                s_environments = null;
            }
        }
    }

    private static MediaEnvironment CreateEnvironmentCore(Document document)
    {
        var browsingContext = document.BrowsingContext;
        if (browsingContext is null)
            return document.SvgImage?.Environment ?? MediaEnvironment.Default;

        // The viewport of a top-level browsing context is the one of the options, or the size requested by the features of
        // window.open() (the screen stays the viewport of the options)
        var top = browsingContext.Top;
        var viewport = top.Options.Viewport ?? ViewportOptions.Default;
        // While the documents are displayed fullscreen, the viewport is the one the host answered (the screen), if any
        var width = top.FullscreenViewportSize?.Width ?? top.ClientWindowViewportWidth ?? viewport.Width;
        var height = top.FullscreenViewportSize?.Height ?? top.ClientWindowViewportHeight ?? viewport.Height;
        var devicePixelRatio = viewport.DevicePixelRatio;
        // https://drafts.csswg.org/css-color-adjust-1/#forced: the palette of the forced colors mode sets the preferred color
        // scheme when its canvas is clearly light or dark, and the preferred contrast
        var forcedColors = ForcedColorsPalette.Get(viewport);
        var prefersColorScheme = forcedColors?.PrefersColorScheme ?? (viewport.PreferredColorScheme == PreferredColorScheme.Dark ? "dark" : "light");
        var prefersContrast = forcedColors?.PrefersContrast ?? viewport.PreferredContrast switch
        {
            PreferredContrast.More => "more",
            PreferredContrast.Less => "less",
            PreferredContrast.Custom => "custom",
            _ => "no-preference",
        };
        if (browsingContext.Container is { } container)
        {
            // The viewport of a nested browsing context is the content box of its container laid out in the parent document
            // (see LayoutEngine.GetNestedViewportSize), or empty when the container is not being rendered
            if (ComputedStyleDeclarations.IsBeingRendered(container))
            {
                (width, height) = GetContainerViewportSize(container);

                // https://drafts.csswg.org/css-viewport/#zoom-property: the effective zoom of the container scales the
                // contents of the nested document as a change of its device pixel ratio (as in the browsers)
                var parentDevicePixelRatio = CreateEnvironment(container.NodeDocument).DevicePixelRatio;
                var containerStyle = container.NodeDocument.GetStyleEngine().GetStyle(container);
                devicePixelRatio = parentDevicePixelRatio * containerStyle.Layout.EffectiveZoom;

                // https://drafts.csswg.org/css-color-adjust-1/#color-scheme-effect: the preferred color scheme of an embedded
                // document is the used color scheme of the element that embeds it; as in the browsers, an element that supports
                // no color scheme (color-scheme: normal in a page without supported color schemes) passes on the preferred
                // color scheme of its document
                var parentEnvironment = CreateEnvironment(container.NodeDocument);
                prefersColorScheme = containerStyle.GetComputedValue("color-scheme") is "normal" or "" && parentEnvironment.PageColorSchemes is null
                    ? parentEnvironment.PrefersColorScheme
                    : CssValueComputer.GetUsedColorScheme(containerStyle);
            }
            else
            {
                width = 0;
                height = 0;
            }
        }

        // The environment is computed by each style lookup: the previous one is reused when it is the same
        var pageColorSchemes = document.PageColorSchemes;
        var scriptingEnabled = browsingContext.Options.ScriptingEnabled;
        if (document.LastMediaEnvironment is { } last
            && last.Width == width
            && last.Height == height
            && last.DeviceWidth == viewport.Width
            && last.DeviceHeight == viewport.Height
            && last.DevicePixelRatio == devicePixelRatio
            && last.MediaType == viewport.MediaType
            && last.PrefersColorScheme == prefersColorScheme
            && last.PageColorSchemes == pageColorSchemes
            && last.PrefersReducedMotion == viewport.PrefersReducedMotion
            && last.PrefersReducedTransparency == viewport.PrefersReducedTransparency
            && last.PrefersContrast == prefersContrast
            && last.ForcedColors == viewport.ForcedColors
            && ReferenceEquals(last.ForcedColorsPalette, forcedColors)
            && last.ScriptingEnabled == scriptingEnabled)
        {
            return last;
        }

        var environment = new MediaEnvironment
        {
            Width = width,
            Height = height,
            DeviceWidth = viewport.Width,
            DeviceHeight = viewport.Height,
            DevicePixelRatio = devicePixelRatio,
            MediaType = viewport.MediaType,
            PrefersColorScheme = prefersColorScheme,
            PageColorSchemes = pageColorSchemes,
            PrefersReducedMotion = viewport.PrefersReducedMotion,
            PrefersReducedTransparency = viewport.PrefersReducedTransparency,
            PrefersContrast = prefersContrast,
            ForcedColors = viewport.ForcedColors,
            ForcedColorsPalette = forcedColors,
            ScriptingEnabled = scriptingEnabled,
        };
        document.LastMediaEnvironment = environment;
        return environment;
    }

    /// <summary>
    /// Returns the size of the viewport of a nested browsing context: the content box of its container from the layout of the
    /// parent document, or without layout (during the layout of the parent document, before its first layout of the container)
    /// the computed width and height of the container when they are lengths, otherwise the width and height attributes,
    /// 300x150 by default.
    /// </summary>
    private static (double Width, double Height) GetContainerViewportSize(Element container)
    {
        if (container.NodeDocument.GetLayoutEngine().GetNestedViewportSize(container) is { } size)
            return size;

        return (GetContainerDimension(container, horizontal: true), GetContainerDimension(container, horizontal: false));
    }

    private static double GetContainerDimension(Element container, bool horizontal)
    {
        var style = container.NodeDocument.GetStyleEngine().GetStyle(container);
        if (CssValueComputer.ParsePixels(style.GetComputedValue(horizontal ? "width" : "height")) is { } size)
        {
            // https://drafts.csswg.org/css-sizing-3/#box-sizing: the border and the padding are not part of the content box
            if (style.GetComputedValue("box-sizing") == "border-box")
            {
                foreach (var property in horizontal ? (ReadOnlySpan<string>)["padding-left", "padding-right"] : ["padding-top", "padding-bottom"])
                {
                    size -= CssValueComputer.ParsePixels(style.GetComputedValue(property)) ?? 0;
                }

                size -= horizontal
                    ? style.GetUsedBorderWidth("border-left-width") + style.GetUsedBorderWidth("border-right-width")
                    : style.GetUsedBorderWidth("border-top-width") + style.GetUsedBorderWidth("border-bottom-width");
            }

            return Math.Max(0, size);
        }

        var value = container.GetAttributeValue(horizontal ? "width" : "height");
        return value is not null && Html.Microsyntaxes.TryParseNonNegativeInteger(value, out var result) ? result : horizontal ? 300 : 150;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Style sheets
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The style rules of the style sheets of a tree (a document or a shadow root).</summary>
    private sealed class TreeStyles
    {
        public required List<CssStyleSheet> Sheets { get; init; }

        public required int StyleVersion { get; init; }

        /// <summary>The version of the DOM the style sheets were last collected for (they change with the DOM, the CSSOM and the environment).</summary>
        public required int DomVersion { get; set; }

        public required MediaEnvironment Environment { get; init; }

        public required CascadeRuleSet Rules { get; init; }

        public required Dictionary<string, CssPropertyRule> PropertyRules { get; init; }

        public required Dictionary<string, CssFunctionRule> FunctionRules { get; init; }

        public required Dictionary<string, CssMixinRule> MixinRules { get; init; }

        public required Dictionary<CssFontFaceRule, int> FontFaceLayers { get; init; }

        public required List<CssFontFeatureValuesRule> FontFeatureValuesRules { get; init; }

        public required List<CssFontPaletteValuesRule> FontPaletteValuesRules { get; init; }

        public required Dictionary<string, CssKeyframesRule> KeyframesRules { get; init; }

        public CssViewTransitionRule? ViewTransitionRule { get; init; }

        public required Dictionary<string, CssPositionTryRule> PositionTryRules { get; init; }

        public required Dictionary<string, CssCounterStyleRule> CounterStyleRules { get; init; }

        /// <summary>The @page rules, in the order of appearance (see <see cref="CascadeRuleCollector.PageRules"/>).</summary>
        public required List<(CssPageRule Rule, int Layer, string? BaseUrl)> PageRules { get; init; }

        /// <summary>
        /// The mixins of the trees of the shadow hosts, from the closest one to the document, the @apply rules of the tree were
        /// resolved with; null when the tree has no @apply rule or is a document. The rules are collected again when they change.
        /// </summary>
        public List<Dictionary<string, CssMixinRule>>? OuterMixinRules { get; init; }

        /// <summary>The number of shadow roots between the tree and the document (the encapsulation context, https://drafts.csswg.org/css-cascade-5/#cascade-context).</summary>
        public required int Depth { get; init; }
    }

    private TreeStyles GetTreeStyles(Node root)
    {
        // The style sheets of a tree are collected again only when the DOM changed: this method is called for each element
        List<CssStyleSheet>? sheets = null;
        if (_trees.TryGetValue(root, out var styles) && styles.StyleVersion == _styleVersion && styles.Environment == _environment)
        {
            if (styles.DomVersion == _domVersion)
                return styles;

            // The @apply rules also depend on the mixins of the trees of the shadow hosts (e.g. a @mixin rule added to the document)
            sheets = GetStyleSheets(root);
            if (styles.Sheets.SequenceEqual(sheets) && (styles.OuterMixinRules is null || OuterMixinRulesEqual(root, styles.OuterMixinRules)))
            {
                styles.DomVersion = _domVersion;
                return styles;
            }
        }

        sheets ??= GetStyleSheets(root);

        var collector = new CascadeRuleCollector(_environment!, CascadeOrigin.Author, root);
        var baseUrl = _document.DocumentBaseUrl.Serialize();
        foreach (var sheet in sheets)
        {
            collector.AddStyleSheet(sheet, sheet.BaseUrl ?? baseUrl);
        }

        collector.Complete();

        // The @apply rules are applied once the mixins of the tree are known (https://drafts.csswg.org/css-mixins-1/#apply-rule):
        // the style sheets are collected again, with the mixins of the tree and of the trees of the shadow hosts
        List<Dictionary<string, CssMixinRule>>? outerMixinRules = null;
        if (collector.HasApplyRules)
        {
            outerMixinRules = root is ShadowRoot ? GetOuterMixinRules(root) : null;
            collector = CreateCollectorWithMixins(root, collector.MixinRules);
            foreach (var sheet in sheets)
            {
                collector.AddStyleSheet(sheet, sheet.BaseUrl ?? baseUrl);
            }
        }

        styles = new TreeStyles
        {
            Sheets = sheets,
            StyleVersion = _styleVersion,
            DomVersion = _domVersion,
            Environment = _environment!,
            Rules = collector.Complete(),
            PropertyRules = collector.PropertyRules,
            FunctionRules = collector.FunctionRules,
            MixinRules = collector.MixinRules,
            FontFaceLayers = collector.FontFaceLayers,
            FontFeatureValuesRules = collector.FontFeatureValuesRules,
            FontPaletteValuesRules = collector.FontPaletteValuesRules,
            KeyframesRules = collector.KeyframesRules,
            ViewTransitionRule = collector.ViewTransitionRule,
            PositionTryRules = collector.PositionTryRules,
            CounterStyleRules = collector.CounterStyleRules,
            PageRules = collector.PageRules,
            OuterMixinRules = outerMixinRules,
            Depth = GetDepth(root),
        };

        _trees[root] = styles;
        if (root is not Dom.Document && styles.Rules.DeclaresAnimations)
        {
            _shadowTreesDeclareAnimations = true;
        }

        if (root is not Dom.Document && styles.Rules.DeclaresContentVisibility)
        {
            _stylesDeclareContentVisibility = true;
        }

        if (root is not Dom.Document && styles.Rules.HasScrollStateQueries)
        {
            _shadowTreesHaveScrollStateQueries = true;
        }

        return styles;
    }

    // The closure is created in its own method: it would otherwise be allocated on each call of GetTreeStyles (for each element)
    private CascadeRuleCollector CreateCollectorWithMixins(Node root, Dictionary<string, CssMixinRule> mixins)
        => new(_environment!, CascadeOrigin.Author, root) { MixinResolver = (name, scope) => FindMixin(name, scope ?? root, root, mixins) };

    /// <summary>Returns the mixins of the trees of the shadow hosts of the shadow tree, from the closest one to the document.</summary>
    private List<Dictionary<string, CssMixinRule>> GetOuterMixinRules(Node root)
    {
        var result = new List<Dictionary<string, CssMixinRule>>();
        while (root is ShadowRoot shadowRoot)
        {
            root = shadowRoot.Host.GetRoot();
            if (root is not (Dom.Document or ShadowRoot))
                break;

            result.Add(GetTreeStyles(root).MixinRules);
        }

        return result;
    }

    /// <summary>Returns whether the mixins of the trees of the shadow hosts of the shadow tree are the ones its @apply rules were resolved with.</summary>
    private bool OuterMixinRulesEqual(Node root, List<Dictionary<string, CssMixinRule>> outerMixinRules)
    {
        var index = 0;
        while (root is ShadowRoot shadowRoot)
        {
            root = shadowRoot.Host.GetRoot();
            if (root is not (Dom.Document or ShadowRoot))
                break;

            // The mixins of a tree are the same instance while its style sheets are not collected again
            if (index >= outerMixinRules.Count || !ReferenceEquals(GetTreeStyles(root).MixinRules, outerMixinRules[index]))
                return false;

            index++;
        }

        return index == outerMixinRules.Count;
    }

    /// <summary>
    /// Returns the mixin with the name (https://drafts.csswg.org/css-mixins-1/#mixin-rule), dereferenced as a tree-scoped
    /// reference (https://drafts.csswg.org/css-scoping/#css-tree-scoped-reference): the @mixin rules of the tree, then the ones
    /// of the trees of the shadow hosts, up to the document.
    /// </summary>
    /// <param name="name">The name of the mixin.</param>
    /// <param name="treeScope">The root of the tree the reference is looked up from.</param>
    /// <param name="collectedRoot">The root of the tree being collected, whose mixins are <paramref name="collectedMixins"/>.</param>
    /// <param name="collectedMixins">The mixins of the tree being collected.</param>
    private (CssMixinRule Rule, Node Scope)? FindMixin(string name, Node treeScope, Node collectedRoot, Dictionary<string, CssMixinRule> collectedMixins)
    {
        var root = treeScope;
        while (root is Dom.Document or ShadowRoot)
        {
            var mixins = ReferenceEquals(root, collectedRoot) ? collectedMixins : GetTreeStyles(root).MixinRules;
            if (mixins.TryGetValue(name, out var mixin))
                return (mixin, root);

            if (root is not ShadowRoot shadowRoot)
                break;

            root = shadowRoot.Host.GetRoot();
        }

        return null;
    }

    private static int GetDepth(Node root)
    {
        var depth = 0;
        while (root is ShadowRoot shadowRoot)
        {
            depth++;
            root = shadowRoot.Host.GetRoot();
        }

        return depth;
    }

    /// <summary>
    /// Returns the style sheets of a document or a shadow root that apply: the style sheets of the tree in tree order, then the
    /// adopted style sheets (https://drafts.csswg.org/cssom/#documentorshadowroot-final-css-style-sheets), without the
    /// disabled style sheets, the alternative style sheets and the style sheets whose media do not match.
    /// </summary>
    private List<CssStyleSheet> GetStyleSheets(Node root)
    {
        var result = new List<CssStyleSheet>();
        StyleSheetList list;
        IList<CssStyleSheet> adopted;
        switch (root)
        {
            case Document document:
                list = document.StyleSheets;
                adopted = document.AdoptedStyleSheets;
                break;
            case ShadowRoot shadowRoot:
                list = shadowRoot.StyleSheets;
                adopted = shadowRoot.AdoptedStyleSheets;
                break;
            default:
                return result;
        }

        // https://drafts.csswg.org/cssom/#css-style-sheet-set: the style sheets whose title is not the preferred style sheet set
        // name are alternative style sheets
        var preferredTitle = root is Dom.Document { PreferredStyleSheetSetName: { Length: > 0 } name } ? name : null;
        foreach (var sheet in list)
        {
            // The style sheet sets cannot be selected: the alternate style sheets never apply
            if (sheet.Alternate)
                continue;

            if (!string.IsNullOrEmpty(sheet.Title))
            {
                preferredTitle ??= sheet.Title;
                if (sheet.Title != preferredTitle)
                    continue;
            }

            if (IsEnabled(sheet))
            {
                result.Add(sheet);
            }
        }

        foreach (var sheet in adopted)
        {
            if (IsEnabled(sheet))
            {
                result.Add(sheet);
            }
        }

        return result;
    }

    private bool IsEnabled(CssStyleSheet sheet) => !sheet.Disabled && MediaQueryEvaluator.Matches(sheet.Media, _environment!);

    /// <summary>
    /// The default margins of the body element (https://html.spec.whatwg.org/multipage/rendering.html#the-page): like in the
    /// browsers, they are in the user agent style sheet (revert rolls back to them), and the margin attributes are presentational hints.
    /// </summary>
    private const string BodyMarginStyleSheet = "body { margin: 8px; }";

    /// <summary>
    /// The rules of the user agent that are not in a CSS block of the specifications: a textarea element renders as an
    /// inline-block box (https://html.spec.whatwg.org/multipage/rendering.html#the-textarea-element-2), the style attribute of
    /// the second slot of a details element, its <c>::details-content</c> pseudo-element ("display: block; content-visibility: hidden;"
    /// when the details element is closed, "display: block;" otherwise: https://html.spec.whatwg.org/multipage/rendering.html#the-details-and-summary-elements;
    /// the rules of the author win, as over the declarations of a shadow tree of the user agent), the replaced elements
    /// clip their contents to their content box unless the author sets <c>overflow: visible</c>
    /// (https://drafts.csswg.org/css-overflow-4/#overflow-control: "Host languages should define UA style sheet rules that apply a
    /// default value of clip to such elements and set their overflow-clip-margin to content-box", the rules of the browsers), the default style
    /// of the markers (https://drafts.csswg.org/css-lists/#ua-stylesheet), the default style of ::backdrop
    /// (https://drafts.csswg.org/css-position-4/#backdrop), the default styles of the scroll navigation controls
    /// (https://drafts.csswg.org/css-overflow-5/#scroll-navigation: a ::scroll-marker has "a default style like the HTML a
    /// element", the ::scroll-button() pseudo-elements "are styled identically to the button element": the rules of the links
    /// and of the buttons of this style sheet and of the HTML one), the global view transition user agent style sheet
    /// (https://drafts.csswg.org/css-view-transitions-1/#ua-styles; its keyframes are in <see cref="ViewTransitionKeyframesStyleSheet"/>;
    /// the root element of a scoped view transition has the default name <c>root</c> too, "to allow it to be independently
    /// selected", and view transition scoping "for the duration of the transition"),
    /// and the colors of the browsers that depend on the
    /// color scheme: the system colors (https://drafts.csswg.org/css-color-4/#css-system-colors) of the links, of mark (instead
    /// of the suggested yellow and black) and of the form controls, the dark gray color of the placeholders of the browsers
    /// (https://drafts.csswg.org/css-pseudo-4/#placeholder-pseudo), the <c>overflow-wrap: break-word</c> of the textarea of
    /// the browsers (the words that overflow its inner editor are broken), and the ruby rules of the default style sheet of CSS Ruby
    /// (https://drafts.csswg.org/css-ruby-1/#default-stylesheet) that the HTML one does not have (the bases, the annotation
    /// containers, the size and the line height of the annotations; <c>font-variant-east-asian: ruby</c> is not applied),
    /// and the <c>text-autospace: no-autospace</c> of the preformatted and monospace elements of the current HTML Standard
    /// (https://html.spec.whatwg.org/multipage/rendering.html#phrasing-content-3), which the pinned one does not have.
    /// The <c>sub</c> and <c>sup</c> elements keep the inherited <c>line-height</c>, as in the browsers (the HTML style sheet
    /// sets <c>line-height: normal</c> on them, which no browser does: a subscript in a paragraph with a large line height
    /// would make its line taller).
    /// The display of the hidden table parts (https://html.spec.whatwg.org/multipage/rendering.html#tables-2:
    /// "colgroup, colgroup[hidden] { display: table-column-group; }"...: they are collapsed, not removed) is repeated with the
    /// specificity of the rule of the hidden attribute, "[hidden]:not([hidden=until-found i]):not(embed) { display: none }",
    /// which otherwise wins against the rules of the specification that are meant to override it. The boxes of the parts of
    /// the form controls (see <see cref="Layout.FormControlContents"/>) are blocks, and the button of a file upload control an
    /// inline-block that does not wrap.
    /// </summary>
    private const string SupplementalStyleSheet = """
        textarea { display: inline-block; overflow-wrap: break-word; }
        details::details-content { display: block; }
        details:not([open])::details-content { content-visibility: hidden; }
        img, video, canvas { overflow: clip; overflow-clip-margin: content-box; }
        ::backdrop { position: fixed; inset: 0; }
        ::scroll-marker-group { display: block; }
        ::scroll-marker, ::column::scroll-marker { color: LinkText; text-decoration: underline; cursor: pointer; }
        ::scroll-marker:active, ::column::scroll-marker:active { color: ActiveText; }
        ::scroll-marker:focus-visible, ::column::scroll-marker:focus-visible, ::scroll-button(*):focus-visible { outline: auto; }
        ::scroll-button(*) {
          letter-spacing: initial;
          word-spacing: initial;
          line-height: initial;
          text-transform: initial;
          text-indent: initial;
          text-shadow: initial;
          appearance: auto;
          text-align: center;
          display: inline-block;
          box-sizing: border-box;
          align-content: center;
          cursor: default;
          background-color: ButtonFace;
          border-color: ButtonBorder;
          color: ButtonText;
          padding-block: 1px;
          padding-inline: 6px;
          border-width: 2px;
          border-style: outset;
        }
        :root { view-transition-name: root; }
        :active-view-transition:not(:root) { view-transition-name: root; view-transition-scope: all !important; }
        ::view-transition { position: absolute; inset: 0; }
        ::view-transition-group(*) { position: absolute; top: 0; left: 0; animation-duration: 0.25s; animation-fill-mode: both; }
        ::view-transition-image-pair(*) { position: absolute; inset: 0; }
        ::view-transition-old(*), ::view-transition-new(*) { position: absolute; display: block; inset-block-start: 0; inline-size: 100%; block-size: auto; }
        ::view-transition-image-pair(*), ::view-transition-old(*), ::view-transition-new(*), ::view-transition-group-children(*) {
          animation-duration: inherit;
          animation-fill-mode: inherit;
          animation-delay: inherit;
          animation-timing-function: inherit;
          animation-iteration-count: inherit;
          animation-direction: inherit;
          animation-play-state: inherit;
        }
        ::view-transition-group-children(*) { position: absolute; inset: 0; border-style: solid; border-color: transparent; }
        ::marker, ::before::marker, ::after::marker { unicode-bidi: isolate; font-variant-numeric: tabular-nums; white-space: pre; text-transform: none; }
        pre, listing, plaintext, xmp, tt, code, kbd, samp { text-autospace: no-autospace; }
        sub, sup { line-height: inherit; }
        :link { color: LinkText; }
        :visited { color: VisitedText; }
        :link:active, :visited:active { color: ActiveText; }
        mark { background-color: Mark; color: MarkText; }
        input, select, textarea { background-color: Field; color: FieldText; }
        ::placeholder { color: #757575; }
        input:is([type=checkbox i], [type=radio i], [type=range i], [type=file i], [type=image i]) { background-color: initial; }
        input:is([type=reset i], [type=button i], [type=submit i], [type=color i]), button, ::file-selector-button {
          background-color: ButtonFace;
          border-color: ButtonBorder;
          color: ButtonText;
        }
        input:is([type=reset i], [type=button i], [type=submit i]), button, ::file-selector-button {
          padding-block: 1px;
          padding-inline: 6px;
          border-width: 2px;
          border-style: outset;
        }
        colgroup[hidden]:not([hidden=until-found i]):not(embed) { display: table-column-group; }
        col[hidden]:not([hidden=until-found i]):not(embed) { display: table-column; }
        thead[hidden]:not([hidden=until-found i]):not(embed) { display: table-header-group; }
        tbody[hidden]:not([hidden=until-found i]):not(embed) { display: table-row-group; }
        tfoot[hidden]:not([hidden=until-found i]):not(embed) { display: table-footer-group; }
        tr[hidden]:not([hidden=until-found i]):not(embed) { display: table-row; }
        ::file-selector-button { display: inline-block; white-space: nowrap; margin-inline-end: 4px; }
        ::slider-track, ::slider-fill, ::slider-thumb, ::color-swatch { display: block; }
        meter::slider-thumb, progress::slider-thumb { display: none; }
        rb { display: ruby-base; white-space-collapse: collapse; }
        rtc { display: ruby-text-container; }
        rtc, rt { text-emphasis: none; white-space: nowrap; line-height: 1; }
        rtc, :not(rtc) > rt { font-size: 50%; }
        rtc > rt { font-size: 100%; }
        rtc:lang(zh-TW), :not(rtc) > rt:lang(zh-TW) { font-size: 30%; }
        rtc:lang(zh), rt:lang(zh) { ruby-align: center; }
        ruby { text-indent: 0; }
        """;

    /// <summary>
    /// The user agent style sheet of the Fullscreen API (https://fullscreen.spec.whatwg.org/#user-agent-level-style-sheet-defaults):
    /// the elements of any namespace that are displayed fullscreen fill the viewport. The <c>:fullscreen</c> pseudo-class is
    /// written first in the compound selectors: it does not match in most documents.
    /// </summary>
    private const string FullscreenStyleSheet = """
        *|*:fullscreen:not(:root) {
          position: fixed !important;
          inset: 0 !important;
          margin: 0 !important;
          box-sizing: border-box !important;
          min-width: 0 !important;
          max-width: none !important;
          min-height: 0 !important;
          max-height: none !important;
          width: 100% !important;
          height: 100% !important;
          transform: none !important;
          /* intentionally not !important */
          object-fit: contain;
        }
        iframe:fullscreen {
          border: none !important;
          padding: 0 !important;
        }
        *|*:fullscreen:not(:root)::backdrop {
          background: black;
        }
        """;

    /// <summary>
    /// The default style of the MathML links (an <c>a</c> element with an <c>href</c> attribute, https://w3c.github.io/mathml-core/#the-a-element):
    /// neither the user agent style sheet of MathML Core nor the one of HTML (whose rules are in the HTML namespace) styles
    /// them; as in Gecko and WebKit, they have the colors and the underline of the HTML links.
    /// </summary>
    private const string MathMLSupplementalStyleSheet = """
        :link { color: LinkText; }
        :visited { color: VisitedText; }
        :link:active, :visited:active { color: ActiveText; }
        :link, :visited { text-decoration: underline; cursor: pointer; }
        """;

    /// <summary>
    /// The rules of the user agent style sheet of SVG (https://svgwg.org/svg2-draft/styling.html#UAStyleSheet) that the
    /// library implements: the elements that establish a viewport clip their content (the outermost svg elements clip it as the
    /// replaced elements, the foreignObject elements are not scroll containers, see <see cref="Layout.ScrollContainers.GetUsedOverflow"/>),
    /// the transform origin of the SVG elements (other than the outermost svg elements, which have a CSS box) is the origin of
    /// their user space, and <c>xml:space="preserve"</c> preserves the spaces
    /// (https://drafts.csswg.org/css-text-4/#valdef-white-space-collapse-preserve-spaces). As in the browsers, the auto size of
    /// the root svg element of a document is the size of the viewport (https://svgwg.org/svg2-draft/geometry.html#Sizing: auto is
    /// 100% for an outermost svg element). The colors of the SVG content are not forced in forced colors mode, but the ones of
    /// the foreign content are (https://drafts.csswg.org/css-color-adjust-1/#forced-color-adjust-prop: "In order to not break
    /// SVG content, UAs are expected to add the following rules to their UA style sheet").
    /// </summary>
    private const string SvgUserAgentStyleSheet = """
        @namespace xml url(http://www.w3.org/XML/1998/namespace);
        svg:not(:root), symbol, image, marker, pattern, foreignObject { overflow: hidden; }
        svg:root { width: 100%; height: 100%; }
        *:not(svg), *:not(foreignObject) > svg { transform-origin: 0 0; }
        *[xml|space=preserve] { white-space-collapse: preserve-spaces; }
        svg { forced-color-adjust: preserve-parent-color; }
        foreignObject { forced-color-adjust: auto; }
        """;

    /// <summary>
    /// The rules of the user agent of the documents of the SVG images (<see cref="Layout.SvgImage"/>): the root svg element fills
    /// the viewport of the image, the concrete object size (https://drafts.csswg.org/css-images-3/#concrete-size-resolution), whatever
    /// its width and height.
    /// </summary>
    private const string SvgImageUserAgentStyleSheet = """
        svg:root { width: 100% !important; height: 100% !important; }
        """;

    /// <summary>
    /// The rules of the user agent in forced colors mode (https://drafts.csswg.org/css-color-adjust-1/#forced): the colors of
    /// the user agent style sheet that are not system colors are replaced with system colors, which the forced colors of the
    /// elements come from (the links, <c>mark</c> and the form controls already have system colors). As in Chromium, the
    /// placeholders have the color of the disabled text.
    /// </summary>
    private const string ForcedColorsUserAgentStyleSheet = """
        ::placeholder { color: GrayText; }
        """;

    private static CascadeRuleSet GetUserAgentRuleSet(Document document, MediaEnvironment environment)
    {
        var key = (document.Mode == DocumentMode.Quirks, document.IsHtmlDocument, environment.ScriptingEnabled, document.SvgImage is not null, environment.ForcedColors);
        return UserAgentRuleSets.GetOrAdd(key, static key =>
        {
            var collector = new CascadeRuleCollector(new MediaEnvironment { ScriptingEnabled = key.Scripting }, CascadeOrigin.UserAgent, treeRoot: null);
            collector.AddRules(ParseUserAgentStyleSheet(HtmlUserAgentStyleSheet.Default), baseUrl: null);
            collector.AddRules(ParseUserAgentStyleSheet(BodyMarginStyleSheet), baseUrl: null);
            collector.AddRules(ParseUserAgentStyleSheet(SupplementalStyleSheet), baseUrl: null);
            AddSelectRules(collector);
            AddControlRules(collector);
            collector.AddRules(ParseUserAgentStyleSheet(FullscreenStyleSheet), baseUrl: null);
            collector.AddRules(ParseUserAgentStyleSheet(MathMLUserAgentStyleSheet.Default, Namespaces.MathML), baseUrl: null);
            collector.AddRules(ParseUserAgentStyleSheet(MathMLSupplementalStyleSheet, Namespaces.MathML), baseUrl: null);
            collector.AddRules(ParseUserAgentStyleSheet(SvgUserAgentStyleSheet, Namespaces.Svg), baseUrl: null);
            if (key.Quirks)
            {
                collector.AddRules(ParseUserAgentStyleSheet(HtmlUserAgentStyleSheet.Quirks), baseUrl: null);
            }

            if (key.Html)
            {
                collector.AddRules(ParseUserAgentStyleSheet(HtmlUserAgentStyleSheet.HtmlDocuments), baseUrl: null);
            }

            if (key.SvgImage)
            {
                collector.AddRules(ParseUserAgentStyleSheet(SvgImageUserAgentStyleSheet, Namespaces.Svg), baseUrl: null);
            }

            if (key.ForcedColors)
            {
                collector.AddRules(ParseUserAgentStyleSheet(ForcedColorsUserAgentStyleSheet), baseUrl: null);
            }

            return collector.Complete();
        });
    }

    /// <summary>
    /// Returns the declaration of the tables inherit color from body quirk
    /// (https://quirks.spec.whatwg.org/#the-tables-inherit-color-from-body-quirk): the used value of the color of the body
    /// element of the document, or the initial value when the document has no body element.
    /// </summary>
    private List<CssDeclarationEntry> GetTableColorQuirkDeclarations()
    {
        var color = _document.Body is { } body ? GetStyle(body).GetComputedValue("color") : "initial";
        var declarations = new List<CssDeclarationEntry>();
        CssStyleDeclaration.ParseDeclarationBlock("color: " + color, declarations, atRule: null);
        return declarations;
    }

    private static readonly Lazy<List<CssDeclarationEntry>> MarginBlockStartQuirkDeclarations = new(static () => ParseDeclarations("margin-block-start: 0"));
    private static readonly Lazy<List<CssDeclarationEntry>> MarginBlockEndQuirkDeclarations = new(static () => ParseDeclarations("margin-block-end: 0"));
    private static readonly Lazy<List<CssDeclarationEntry>> MarginBlockQuirkDeclarations = new(static () => ParseDeclarations("margin-block: 0"));

    private static List<CssDeclarationEntry> ParseDeclarations(string text)
    {
        var declarations = new List<CssDeclarationEntry>();
        CssStyleDeclaration.ParseDeclarationBlock(text, declarations, atRule: null);
        return declarations;
    }

    /// <summary>
    /// Returns the declarations of the user agent rules of the margin collapsing quirks of an element in quirks mode
    /// (https://html.spec.whatwg.org/multipage/rendering.html#margin-collapsing-quirks), or null when none applies. They depend
    /// on the siblings and the children of the element: the styles are computed again when the DOM changes.
    /// </summary>
    private static List<CssDeclarationEntry>? GetMarginCollapsingQuirkDeclarations(Element element)
    {
        // "The elements with default margins are the following elements: blockquote, dir, dl, h1, h2, h3, h4, h5, h6, listing,
        // menu, ol, p, plaintext, pre, ul, xmp."
        if (!element.IsHtmlNamespace || element.TagId is not (TagId.Blockquote or TagId.Dir or TagId.Dl or TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6
            or TagId.Listing or TagId.Menu or TagId.Ol or TagId.P or TagId.Plaintext or TagId.Pre or TagId.Ul or TagId.Xmp))
        {
            return null;
        }

        if (element.ParentNode is not Element { IsHtmlNamespace: true } parent || parent.TagId is not (TagId.Body or TagId.Td or TagId.Th))
            return null;

        var isCell = parent.TagId is not TagId.Body;
        var noPreviousSibling = !HasSubstantialSibling(element, previous: true);
        var noFollowingSibling = isCell && !HasSubstantialSibling(element, previous: false);
        var isBlank = !HasSubstantialChild(element);

        // "any element with default margins that is the child of a body, td, or th element and has no substantial previous
        // siblings is expected to have a user-agent level style sheet rule that sets its margin-block-start property to zero";
        // "[...] has no substantial following siblings, and is blank [...] sets its margin-block-start property to zero" (td, th)
        var start = noPreviousSibling || (isCell && noFollowingSibling && isBlank);

        // "[...] has no substantial previous siblings, and is blank, is expected to have a user-agent level style sheet rule that
        // sets its margin-block-end property to zero also"; "any p element that is the child of a td or th element and has no
        // substantial following siblings, is expected to have a user-agent level style sheet rule that sets its
        // margin-block-end property to zero"
        var end = (noPreviousSibling && isBlank) || (element.TagId == TagId.P && noFollowingSibling);
        return (start, end) switch
        {
            (true, true) => MarginBlockQuirkDeclarations.Value,
            (true, false) => MarginBlockStartQuirkDeclarations.Value,
            (false, true) => MarginBlockEndQuirkDeclarations.Value,
            _ => null,
        };

        // "A node is substantial if it is a text node that is not inter-element whitespace, or if it is an element node."
        static bool IsSubstantial(Node node) => node is Element || node is Text text && !Infra.InfraString.IsAsciiWhitespaceOnly(text.Data);

        static bool HasSubstantialSibling(Element element, bool previous)
        {
            for (var sibling = previous ? element.PreviousSibling : element.NextSibling; sibling is not null; sibling = previous ? sibling.PreviousSibling : sibling.NextSibling)
            {
                if (IsSubstantial(sibling))
                    return true;
            }

            return false;
        }

        // "A node is blank if it is an element that contains no substantial nodes."
        static bool HasSubstantialChild(Element element)
        {
            for (var child = element.FirstChild; child is not null; child = child.NextSibling)
            {
                if (IsSubstantial(child))
                    return true;
            }

            return false;
        }
    }

    /// <summary>The declaration of the rule of the user agent style sheet that centers the text of the th elements (https://html.spec.whatwg.org/multipage/rendering.html#tables-2).</summary>
    private static readonly Lazy<List<CssDeclarationEntry>> ThTextAlignDeclarations = new(static () =>
    {
        var declarations = new List<CssDeclarationEntry>();
        CssStyleDeclaration.ParseDeclarationBlock("text-align: center", declarations, atRule: null);
        return declarations;
    });

    private static CascadeRuleSet CreatePresentationalHintRuleSet()
    {
        var collector = new CascadeRuleCollector(MediaEnvironment.Default, CascadeOrigin.Author, treeRoot: null) { ZeroSpecificity = true, PresentationalHints = true };
        collector.AddRules(ParseUserAgentStyleSheet(HtmlUserAgentStyleSheet.PresentationalHints), baseUrl: null);
        return collector.Complete();
    }

    /// <summary>Parses a style sheet of the user agent: its rules are in the HTML namespace, or in the given namespace.</summary>
    private static List<CssRule> ParseUserAgentStyleSheet(string text, string defaultNamespace = Namespaces.Html) => new CssRuleParser(sheet: null).ParseStylesheet("@namespace url(" + defaultNamespace + ");\n" + text);

    // ----------------------------------------------------------------------------------------------------------------
    // Cascade
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the rank of the encapsulation context of a tree (https://drafts.csswg.org/css-cascade-5/#cascade-context) in
    /// the tree of trees (shadow-including tree order): the trees of the rules that match an element are its tree and the
    /// trees of its shadow-including ancestors (the ::part() rules), ranked by depth, the trees of the slots it is assigned to
    /// (the ::slotted() rules), whose hosts are its shadow-including ancestors, also ranked by depth, then its shadow tree
    /// (the :host rules), which comes last whatever its depth.
    /// </summary>
    private static int GetContext(int depth, bool shadowTreeOfElement = false) => shadowTreeOfElement ? int.MaxValue : depth;

    /// <summary>A declaration block that applies to the element (its context is the rank returned by <see cref="GetContext"/>).</summary>
    private readonly record struct MatchedBlock(List<CssDeclarationEntry> Declarations, bool Important, CascadeOrigin Origin, int Context, bool Attached, int Layer, Specificity Specificity, int Proximity, int Order, CascadedValueSource? Source)
    {
        public string? BaseUrl => Source?.BaseUrl;

        public Node? TreeScope => Source?.TreeScope;

        public CssMixinApplication? Mixin => Source?.Mixin;

        /// <summary>The originating element of a ::part() rule (the shadow host it matches), whose attributes attr() reads; null for the element itself.</summary>
        public Element? OriginatingElement { get; init; }

        /// <summary>The rank of the origin and importance (https://drafts.csswg.org/css-cascade-5/#cascade-origin).</summary>
        public int OriginImportance => Important ? 5 - (int)Origin : (int)Origin;

        /// <summary>For normal declarations, the outer context wins; for important declarations, the inner context wins.</summary>
        public int ContextRank => Important ? Context : -Context;

        /// <summary>For normal declarations, the last layer wins and the unlayered declarations win; the order is reversed for important declarations.</summary>
        public int LayerRank => Important ? (Layer == CascadeRule.Unlayered ? int.MinValue : -Layer) : Layer;

        public static int Compare(MatchedBlock x, MatchedBlock y)
        {
            var result = x.OriginImportance.CompareTo(y.OriginImportance);
            if (result != 0)
                return result;

            result = x.ContextRank.CompareTo(y.ContextRank);
            if (result != 0)
                return result;

            result = x.Attached.CompareTo(y.Attached);
            if (result != 0)
                return result;

            result = x.LayerRank.CompareTo(y.LayerRank);
            if (result != 0)
                return result;

            result = x.Specificity.CompareTo(y.Specificity);
            if (result != 0)
                return result;

            result = x.Proximity.CompareTo(y.Proximity);
            if (result != 0)
                return result;

            return x.Order.CompareTo(y.Order);
        }

        /// <summary>Returns whether the block is in a lower cascade layer (or a lower origin, context...) than the other one, for revert-layer.</summary>
        public bool IsInLowerLayerThan(MatchedBlock other)
        {
            if (OriginImportance != other.OriginImportance)
                return OriginImportance < other.OriginImportance;

            if (ContextRank != other.ContextRank)
                return ContextRank < other.ContextRank;

            if (Attached != other.Attached)
                return !Attached;

            return LayerRank < other.LayerRank;
        }
    }

    /// <summary>The proximity of an unscoped rule: it loses against the scoped rules (https://drafts.csswg.org/css-cascade-6/#cascade-proximity).</summary>
    private const int NoProximity = int.MinValue;

    /// <param name="shared">Whether the cascaded values are shared with other elements: they must not be modified.</param>
    /// <param name="parent">The style the element inherits from.</param>
    /// <param name="visitedLink">The link that matches <c>:visited</c> (the cascade of a visited style, see <see cref="CascadeVisitedStyle"/>), or null.</param>
    private Dictionary<string, CascadedValue> Cascade(Element element, string? pseudoElement, ComputedStyle? parent, out bool shared, Element? visitedLink = null)
    {
        // The subject of the conditions of the rules of the select element (a cascade can be nested in another one)
        var (previousElement, previousPseudoElement, previousParent, previousState, previousVisitedLink) = (_cascadeElement, _cascadePseudoElement, _cascadeParent, _cascadeSelectState, _cascadeVisitedLink);
        (_cascadeElement, _cascadePseudoElement, _cascadeParent, _cascadeSelectState, _cascadeVisitedLink) = (element, pseudoElement, parent, null, visitedLink);
        try
        {
            return CascadeCore(element, pseudoElement, out shared);
        }
        finally
        {
            (_cascadeElement, _cascadePseudoElement, _cascadeParent, _cascadeSelectState, _cascadeVisitedLink) = (previousElement, previousPseudoElement, previousParent, previousState, previousVisitedLink);
        }
    }

    private Dictionary<string, CascadedValue> CascadeCore(Element element, string? pseudoElement, out bool shared)
    {
        // The query of GetCascadeOrigin() is the one of this cascade, not of the nested ones
        var originQueryProperty = _originQueryProperty;
        _originQueryProperty = null;

        // The WebVTT objects of the cues rendered over a video element have their own cascade (see StyleEngine.WebVtt.cs)
        if (element is WebVtt.WebVttElement vttElement)
            return CascadeWebVtt(vttElement, pseudoElement, out shared);

        // The list is reused by the next cascade (a nested cascade, e.g. for a container query, uses its own list)
        var blocks = _blocks ?? [];
        _blocks = null;
        var root = element.GetRoot();
        var ownTree = root is Dom.Document or ShadowRoot ? GetTreeStyles(root) : null;
        var depth = GetContext(ownTree?.Depth ?? 0);
        var ownContext = new SelectorContext(root, (root as ShadowRoot)?.Host) { UseInstanceRoot = _useInstanceRoot, VisitedLink = _cascadeVisitedLink };

        // The presentational hints and the style attribute are declarations of the element: its cascaded values are not shared
        // The starting style is never shared
        var hasOwnDeclarations = _includeStartingStyle;

        // The user agent style sheet
        AddMatchingRules(blocks, GetUserAgentRuleSet(_document, _environment!), element, pseudoElement, ownContext, depth);

        // The dynamic view transition style sheet (the sizes, the transforms and the animations of the groups of the active view transition)
        if (pseudoElement is not null && Layout.ViewTransitionPseudoTree.IsViewTransitionPseudoElement(pseudoElement) && GetViewTransitionRules(element) is { } viewTransitionRules)
        {
            AddMatchingRules(blocks, viewTransitionRules.Rules, element, pseudoElement, ownContext, depth);
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#tables-2: a rule of the user agent style sheet matches the th
        // elements whose parent node has the initial value of text-align (a shorthand of text-align-all and text-align-last)
        if (pseudoElement is null && element.IsHtml(TagId.Th) && GetInheritanceParent(element) is { } thParent && GetStyle(thParent) is var thParentStyle && thParentStyle.GetComputedValue("text-align-all") == "start" && thParentStyle.GetComputedValue("text-align-last") == "auto")
        {
            blocks.Add(new MatchedBlock(ThTextAlignDeclarations.Value, Important: false, CascadeOrigin.UserAgent, depth, Attached: false, CascadeRule.Unlayered, new Specificity(0, 0, 1), NoProximity, -1, Source: null));
        }

        // https://quirks.spec.whatwg.org/#the-tables-inherit-color-from-body-quirk: like the browsers, a rule of the user agent
        // style sheet sets the color of the table elements to the color of the body element (whatever their parent is). The
        // styles are computed again when the DOM changes, e.g. when the body element is removed.
        if (pseudoElement is null && _document.Mode == DocumentMode.Quirks && element.IsHtml(TagId.Table))
        {
            blocks.Add(new MatchedBlock(GetTableColorQuirkDeclarations(), Important: false, CascadeOrigin.UserAgent, depth, Attached: false, CascadeRule.Unlayered, new Specificity(0, 0, 1), NoProximity, -1, Source: null));
            hasOwnDeclarations = true;
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#margin-collapsing-quirks
        if (pseudoElement is null && _document.Mode == DocumentMode.Quirks && GetMarginCollapsingQuirkDeclarations(element) is { } quirkDeclarations)
        {
            blocks.Add(new MatchedBlock(quirkDeclarations, Important: false, CascadeOrigin.UserAgent, depth, Attached: false, CascadeRule.Unlayered, new Specificity(1, 0, 0), NoProximity, int.MaxValue, Source: null));
        }

        // The presentational hints
        if (_userAgentOriginOnly)
        {
            // (only the declarations of the user agent origin: the presentational hints are in the author origin)
        }
        else if (element.IsHtmlNamespace)
        {
            AddMatchingRules(blocks, PresentationalHintRuleSet.Value, element, pseudoElement, ownContext, depth);
            if (pseudoElement is null && PresentationalHints.GetDeclarations(element, _environment!, visitedLink: ReferenceEquals(element, _cascadeVisitedLink)) is { Count: > 0 } hints)
            {
                blocks.Add(new MatchedBlock(hints, Important: false, CascadeOrigin.Author, depth, Attached: false, CascadeRule.PresentationalHints, default, NoProximity, int.MaxValue, Source: null));
                hasOwnDeclarations = true;
            }
        }
        else if (pseudoElement is null && element.NamespaceUri == Namespaces.Svg && PresentationalHints.GetSvgDeclarations(element) is { Count: > 0 } svgHints)
        {
            blocks.Add(new MatchedBlock(svgHints, Important: false, CascadeOrigin.Author, depth, Attached: false, CascadeRule.PresentationalHints, default, NoProximity, int.MaxValue, Source: null));
            hasOwnDeclarations = true;
        }
        else if (pseudoElement is null && element.NamespaceUri == Namespaces.MathML && PresentationalHints.GetMathMLDeclarations(element) is { Count: > 0 } mathMLHints)
        {
            blocks.Add(new MatchedBlock(mathMLHints, Important: false, CascadeOrigin.Author, depth, Attached: false, CascadeRule.PresentationalHints, default, NoProximity, int.MaxValue, Source: null));
            hasOwnDeclarations = true;
        }

        // The style sheets of the tree of the element
        if (ownTree is not null && !_userAgentOriginOnly)
        {
            AddMatchingRules(blocks, ownTree.Rules, element, pseudoElement, ownContext, depth);
        }

        // The :host rules of the shadow tree of a shadow host
        if (element.AttachedShadowRoot is { } shadowRoot && !_userAgentOriginOnly)
        {
            var shadowTree = GetTreeStyles(shadowRoot);
            if (shadowTree.Rules.HostRules.Count > 0)
            {
                AddMatchingRules(blocks, shadowTree.Rules.HostRules, element, pseudoElement, new SelectorContext(shadowRoot, element) { VisitedLink = _cascadeVisitedLink }, GetContext(shadowTree.Depth, shadowTreeOfElement: true));
            }
        }

        // The ::slotted() rules of the shadow trees the element is assigned to a slot of (https://drafts.csswg.org/css-scoping/#slotted-pseudo).
        // A slot is not slotted: it is replaced by its assigned nodes in the flattened slottables of the slot it is assigned to.
        for (var slot = element is HtmlSlotElement || _userAgentOriginOnly ? null : element.Slottable?.AssignedSlot; slot is not null; slot = slot.Slottable?.AssignedSlot)
        {
            if (slot.GetRoot() is not ShadowRoot slotRoot)
                break;

            var slotTree = GetTreeStyles(slotRoot);
            foreach (var rule in slotTree.Rules.SlottedRules)
            {
                if (rule.PseudoElement != GetPseudoElementName(pseudoElement) || rule.PseudoElementArgument != GetPseudoElementArgument(pseudoElement) || rule.SlottedSelector is not { } slotted)
                    continue;

                var slotContext = new SelectorContext(slotRoot, slotRoot.Host);
                if (SelectorMatcher.MatchesOriginatingElement(rule.Selector, slot, slotContext) && SelectorMatcher.Matches(slotted, element, ownContext) && MatchesContainerQueries(rule, element, pseudoElement))
                {
                    AddBlocks(blocks, rule, GetContext(slotTree.Depth), NoProximity);
                }
            }
        }

        // The ::part() rules of the trees the element is exposed to as a part
        if (root is ShadowRoot partRoot && !_userAgentOriginOnly)
        {
            AddPartRules(blocks, element, pseudoElement, partRoot);
        }

        // The style attribute (https://drafts.csswg.org/css-style-attr/)
        if (pseudoElement is null && element.GetAttributeValue("style") is not null && !_userAgentOriginOnly)
        {
            hasOwnDeclarations = true;
            var declarations = element.GetInlineStyle().Declarations;
            if (!_inlineStylesDeclareAnimations && declarations.Exists(static declaration => CascadeRuleSet.IsAnimationProperty(declaration.Name)))
            {
                _inlineStylesDeclareAnimations = true;
            }

            if (!_stylesDeclareContentVisibility && declarations.Exists(static declaration => CascadeRuleSet.IsContentVisibilityProperty(declaration.Name)))
            {
                _stylesDeclareContentVisibility = true;
            }

            if (!_inlineStylesDeclareTimelines && declarations.Exists(CascadeRuleSet.IsTimelineDeclaration))
            {
                _inlineStylesDeclareTimelines = true;
            }

            if (!_inlineStylesUseTreeCountingFunctions && StyleInvalidationFeatures.UsesTreeCountingFunction(declarations))
            {
                _inlineStylesUseTreeCountingFunctions = true;
            }

            var source = CascadedValueSource.Create(_document.DocumentBaseUrl.Serialize(), root, mixin: null);
            if (declarations.Exists(d => !d.Important))
            {
                blocks.Add(new MatchedBlock(declarations, Important: false, CascadeOrigin.Author, depth, Attached: true, CascadeRule.Unlayered, default, NoProximity, 0, source));
            }

            if (declarations.Exists(d => d.Important))
            {
                blocks.Add(new MatchedBlock(declarations, Important: true, CascadeOrigin.Author, depth, Attached: true, CascadeRule.Unlayered, default, NoProximity, 0, source));
            }
        }

        blocks.Sort(MatchedBlock.Compare);
        if (originQueryProperty is not null)
        {
            _originQueryResult = FindWinningOrigin(blocks, originQueryProperty);
        }

        // Only a limited set of properties apply to ::marker, ::first-line and ::first-letter: the other declarations are ignored
        var applies = GetPseudoElementPropertyFilter(pseudoElement);

        Dictionary<string, CascadedValue> cascaded;
        var blocksReferenced = false;
        if (applies is null && !hasOwnDeclarations)
        {
            cascaded = GetSharedCascadedValues(blocks, out blocksReferenced);
            shared = true;
        }
        else
        {
            cascaded = ComputeCascadedValues(blocks);
            blocksReferenced = ReferencesBlocks(cascaded);
            shared = ReferenceEquals(cascaded, EmptyCascadedValues);
        }

        if (!blocksReferenced)
        {
            blocks.Clear();
            _blocks = blocks;
        }

        if (applies is not null)
        {
            foreach (var name in cascaded.Keys)
            {
                if (!applies(name))
                {
                    cascaded.Remove(name);
                }
            }
        }

        return cascaded;
    }

    /// <summary>
    /// Returns whether a property applies to ::marker (https://drafts.csswg.org/css-pseudo-4/#marker-pseudo): the font
    /// properties (and line-height, a longhand of font), white-space, color, text-combine-upright, unicode-bidi, direction,
    /// content, the animation and transition properties, and, as in browsers, the inherited text and text decoration
    /// properties, cursor and the custom properties.
    /// </summary>
    /// <summary>
    /// Returns the filter of the properties that apply to a pseudo-element (::marker, ::first-line and ::first-letter), or
    /// null when all the properties apply. The declarations and the animations of the other properties are ignored.
    /// </summary>
    internal static Func<string, bool>? GetPseudoElementPropertyFilter(string? pseudoElement) => pseudoElement switch
    {
        "marker" => IsMarkerProperty,
        "first-line" or DetailsContentPrefix + "first-line" or SelectPickerPrefix + "first-line" => IsFirstLineProperty,
        "first-letter" or DetailsContentPrefix + "first-letter" or SelectPickerPrefix + "first-letter" => IsFirstLetterProperty,
        _ => null,
    };

    private static bool IsMarkerProperty(string name) => name.StartsWith("font-", StringComparison.Ordinal)
        || name.StartsWith("animation", StringComparison.Ordinal)
        || name.StartsWith("transition", StringComparison.Ordinal)
        || name.StartsWith("--", StringComparison.Ordinal)
        || MarkerProperties.Contains(name);

    /// <summary>
    /// Returns whether a property applies to ::first-line (https://drafts.csswg.org/css-pseudo-4/#first-line-styling): the
    /// font, color, opacity, background and text decoration properties, the typesetting properties that apply to inline
    /// elements, ruby-position, the inline layout properties (line-height, the longhands of vertical-align) and the custom properties.
    /// </summary>
    private static bool IsFirstLineProperty(string name) => name.StartsWith("font-", StringComparison.Ordinal)
        || name.StartsWith("background-", StringComparison.Ordinal)
        || name.StartsWith("text-decoration-", StringComparison.Ordinal)
        || name.StartsWith("text-emphasis-", StringComparison.Ordinal)
        || name.StartsWith("--", StringComparison.Ordinal)
        || FirstLineProperties.Contains(name);

    /// <summary>
    /// Returns whether a property applies to ::first-letter (https://drafts.csswg.org/css-pseudo-4/#first-letter-styling):
    /// the properties of ::first-line, the margin, padding, border and box-shadow properties, float, and the properties of the
    /// initial letters (https://drafts.csswg.org/css-inline-3/#initial-letter-styling).
    /// </summary>
    private static bool IsFirstLetterProperty(string name) => IsFirstLineProperty(name)
        || name.StartsWith("margin-", StringComparison.Ordinal)
        || name.StartsWith("padding-", StringComparison.Ordinal)
        || name.StartsWith("border-", StringComparison.Ordinal)
        || name is "box-shadow" or "float" or "initial-letter" or "initial-letter-align" or "initial-letter-wrap";

    /// <summary>
    /// Adds the ::part() rules that match an element of a shadow tree (https://drafts.csswg.org/css-shadow-1/#part): for each
    /// shadow root whose part element map contains the element (its own shadow root, then the shadow roots it is forwarded to
    /// by exportparts), the rules of the tree of the shadow host whose originating element matches the host, and the
    /// :host::part() rules of the shadow tree.
    /// </summary>
    private void AddPartRules(List<MatchedBlock> blocks, Element element, string? pseudoElement, ShadowRoot root)
    {
        for (ShadowRoot? shadowRoot = root; shadowRoot is not null; shadowRoot = shadowRoot.Host.GetRoot() as ShadowRoot)
        {
            // The names the element is exposed under, and the names its pseudo-element is exposed under (exportparts="::before: name")
            var map = GetPartElementMap(shadowRoot);
            var names = map.GetNames(element, pseudoElement: null);
            var pseudoElementNames = pseudoElement is null ? null : map.GetNames(element, GetPseudoElementName(pseudoElement));
            if (names is null && pseudoElementNames is null)
                break;

            var host = shadowRoot.Host;
            var hostRoot = host.GetRoot();
            if (hostRoot is Dom.Document or ShadowRoot)
            {
                var hostTree = GetTreeStyles(hostRoot);
                AddMatchingPartRules(blocks, hostTree, element, pseudoElement, names, pseudoElementNames, host, new SelectorContext(hostRoot, (hostRoot as ShadowRoot)?.Host), featurelessHost: false);
            }

            AddMatchingPartRules(blocks, GetTreeStyles(shadowRoot), element, pseudoElement, names, pseudoElementNames, host, new SelectorContext(shadowRoot, host), featurelessHost: true);
        }
    }

    private void AddMatchingPartRules(List<MatchedBlock> blocks, TreeStyles tree, Element element, string? pseudoElement, List<string>? names, List<string>? pseudoElementNames, Element host, SelectorContext context, bool featurelessHost)
    {
        foreach (var rule in tree.Rules.PartRules)
        {
            // The featureless shadow host does not match the implied universal selector of "::part(name)": the originating
            // element of the rules of its shadow tree must be explicit (e.g. :host::part(name))
            if (featurelessHost && rule.Selector.Compounds[^1].Selectors[0] is PseudoElementSelector)
                continue;

            // ::part(name)::before applies to the ::before of the part; ::part(name) applies to a pseudo-element exposed as a part
            List<string>? exposedNames;
            if (string.Equals(rule.PseudoElement, GetPseudoElementName(pseudoElement), StringComparison.Ordinal) && string.Equals(rule.PseudoElementArgument, GetPseudoElementArgument(pseudoElement), StringComparison.Ordinal))
            {
                exposedNames = names;
            }
            else if (rule.PseudoElement is null && pseudoElement is not null)
            {
                exposedNames = pseudoElementNames;
            }
            else
            {
                continue;
            }

            // The element must be exposed under all the names of ::part()
            if (exposedNames is null || !rule.PartNames.All(name => exposedNames.Contains(name, StringComparer.Ordinal)))
                continue;

            if (!SelectorMatcher.MatchesPart(rule.Selector, element) || !MatchesContainerQueries(rule, element, pseudoElement))
                continue;

            if (rule.Scope is { } scope)
            {
                if (MatchScoped(rule, scope, host, context) is { } proximity)
                {
                    AddBlocks(blocks, rule, GetContext(tree.Depth), -proximity, host);
                }
            }
            else if (Matches(rule, host, context))
            {
                AddBlocks(blocks, rule, GetContext(tree.Depth), NoProximity, host);
            }
        }
    }

    private PartElementMap GetPartElementMap(ShadowRoot shadowRoot)
    {
        if (!_partElementMaps.TryGetValue(shadowRoot, out var map))
        {
            map = PartElementMap.Calculate(shadowRoot, GetPartElementMap);
            _partElementMaps[shadowRoot] = map;
        }

        return map;
    }

    private void AddMatchingRules(List<MatchedBlock> blocks, CascadeRuleSet ruleSet, Element element, string? pseudoElement, SelectorContext context, int depth)
    {
        if (!ruleSet.MayHaveRulesFor(GetPseudoElementName(pseudoElement)))
            return;

        foreach (var rules in ruleSet.GetCandidates(element))
        {
            AddMatchingRules(blocks, rules, element, pseudoElement, context, depth);
        }
    }

    private void AddMatchingRules(List<MatchedBlock> blocks, List<CascadeRule> rules, Element element, string? pseudoElement, SelectorContext context, int depth)
    {
        // The pseudo-classes written after a scroll navigation pseudo-element match the state of the pseudo-element
        if (Layout.ScrollControls.IsScrollControl(pseudoElement))
        {
            context = context with { PseudoElement = pseudoElement };
        }

        foreach (var rule in rules)
        {
            if (!MatchesPseudoElement(rule, element, pseudoElement) || (rule.StartingStyle && !_includeStartingStyle))
                continue;

            if (rule.Scope is { } scope)
            {
                if (MatchScoped(rule, scope, element, context) is { } proximity && MatchesContainerQueries(rule, element, pseudoElement))
                {
                    AddBlocks(blocks, rule, depth, -proximity);
                }
            }
            else if (rule.SelectCondition != SelectRuleCondition.None && !MatchesSelectCondition(rule.SelectCondition))
            {
                // A rule of the user agent for another rendering of the select element
            }
            else if (Matches(rule, element, context) && MatchesContainerQueries(rule, element, pseudoElement) && (!rule.OnlyIfTableBorderIsNotZero || PresentationalHints.HasNonZeroTableBorder(element)))
            {
                AddBlocks(blocks, rule, depth, NoProximity);
            }
        }
    }

    /// <summary>
    /// Returns whether the container queries of the @container rules that contain the rule are true for the element
    /// (https://drafts.csswg.org/css-conditional-5/#container-rule): a query is true when any of its conditions is true.
    /// </summary>
    private bool MatchesContainerQueries(CascadeRule rule, Element element, string? pseudoElement)
    {
        for (var query = rule.Containers; query is not null; query = query.Parent)
        {
            var matches = false;
            foreach (var condition in query.Conditions)
            {
                if (ContainerQueries.Evaluate(condition, this, element, pseudoElement) == true)
                {
                    matches = true;
                    break;
                }
            }

            if (!matches)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns whether the pseudo-element of the rule is the pseudo-element: <c>::highlight(*)</c> matches every custom
    /// highlight, with a zero specificity (https://drafts.csswg.org/css-pseudo-4/#selectordef-highlight).
    /// </summary>
    /// <remarks>
    /// <c>::scroll-button(*)</c> matches the four scroll buttons, and a physical direction matches the button of the corresponding
    /// flow-relative direction in the writing mode of the originating element (https://drafts.csswg.org/css-overflow-5/#scroll-buttons).
    /// </remarks>
    private bool MatchesPseudoElement(CascadeRule rule, Element element, string? pseudoElement)
    {
        var name = GetPseudoElementName(pseudoElement);
        if (!string.Equals(rule.PseudoElement, name, StringComparison.Ordinal))
            return false;

        // The ::column::scroll-marker rules style the scroll marker of every column
        if (name == Layout.ScrollControls.ColumnMarkerRuleName)
            return true;

        if (name == Layout.ScrollControls.ButtonRuleName)
        {
            return rule.PseudoElementArgument switch
            {
                "*" => true,
                "up" or "right" or "down" or "left" => Layout.ScrollControls.GetButton(rule.PseudoElementArgument, Layout.WritingDirection.FromStyle(GetStyle(element).Layout)) == pseudoElement,
                var argument => Layout.ScrollControls.GetButton(argument, default) == pseudoElement,
            };
        }

        if (name is not null && name.StartsWith("view-transition-", StringComparison.Ordinal))
            return MatchesViewTransitionPseudoElement(rule, element, pseudoElement!);

        return string.Equals(rule.PseudoElementArgument, GetPseudoElementArgument(pseudoElement), StringComparison.Ordinal) || (name == "highlight" && rule.PseudoElementArgument == "*");
    }

    /// <summary>
    /// Returns whether the argument of a named view transition pseudo-element selector (https://drafts.csswg.org/css-view-transitions-2/#named-view-transition-pseudo:
    /// <c>*</c> or a name, followed by classes) matches a pseudo-element: its view transition name, the classes of its captured
    /// element (<c>view-transition-class</c>), and <c>:only-child</c> in the view transition tree.
    /// </summary>
    private bool MatchesViewTransitionPseudoElement(CascadeRule rule, Element element, string pseudoElement)
    {
        if (rule.PseudoElementArgument is not { } argument || GetPseudoElementArgument(pseudoElement) is not { } name)
            return false;

        var tree = _document.GetViewTransitionPseudoTree(element);

        // The classes follow the name: the dots of the identifiers are escaped
        var nameEnd = FindUnescapedDot(argument, 0);
        var nameSelector = nameEnd < 0 ? argument : argument[..nameEnd];
        if (nameSelector.Length > 0 && nameSelector != "*" && !string.Equals(nameSelector, name, StringComparison.Ordinal))
            return false;

        if (nameEnd >= 0)
        {
            if (tree?.FindGroup(name)?.Captured.ClassList is not { Count: > 0 } classList)
                return false;

            var start = nameEnd + 1;
            while (start <= argument.Length)
            {
                var end = FindUnescapedDot(argument, start);
                var className = end < 0 ? argument[start..] : argument[start..end];
                if (!classList.Contains(className, StringComparer.Ordinal))
                    return false;

                if (end < 0)
                    break;

                start = end + 1;
            }
        }

        if (rule.PseudoElementNotOnlyChild && tree?.IsOnlyChild(pseudoElement) == true)
            return false;

        return !rule.PseudoElementOnlyChild || tree?.IsOnlyChild(pseudoElement) == true;

        static int FindUnescapedDot(string text, int start)
        {
            for (var i = start; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                }
                else if (text[i] == '.')
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>The name of a pseudo-element as stored by the cascade: "picker(select)" → "picker".</summary>
    private static string? GetPseudoElementName(string? pseudoElement)
    {
        if (pseudoElement is null)
            return null;

        var index = pseudoElement.IndexOf('(', StringComparison.Ordinal);
        if (index < 0)
            return pseudoElement;

        // A pseudo-element of the select popover: "picker(select)::before" → "picker::before"
        return pseudoElement.StartsWith(SelectPickerPrefix, StringComparison.Ordinal) ? string.Concat(pseudoElement.AsSpan(0, index), pseudoElement.AsSpan(SelectPicker.Length)) : pseudoElement[..index];
    }

    private static string? GetPseudoElementArgument(string? pseudoElement)
    {
        if (pseudoElement is null)
            return null;

        var index = pseudoElement.IndexOf('(', StringComparison.Ordinal);
        if (index < 0)
            return null;

        return pseudoElement.StartsWith(SelectPickerPrefix, StringComparison.Ordinal) ? "select" : pseudoElement[(index + 1)..^1];
    }

    private static bool Matches(CascadeRule rule, Element element, SelectorContext context) => rule.PseudoElement is null && rule.Kind != CascadeRuleKind.Part
        ? SelectorMatcher.MatchesComplex(rule.Selector, element, context)
        : SelectorMatcher.MatchesOriginatingElement(rule.Selector, element, context);

    private static void AddBlocks(List<MatchedBlock> blocks, CascadeRule rule, int depth, int proximity, Element? originatingElement = null)
    {
        if (rule.HasNormal)
        {
            blocks.Add(new MatchedBlock(rule.Declarations, Important: false, rule.Origin, depth, Attached: false, rule.Layer, rule.Specificity, proximity, rule.Order, rule.Source) { OriginatingElement = originatingElement });
        }

        if (rule.HasImportant)
        {
            blocks.Add(new MatchedBlock(rule.Declarations, Important: true, rule.Origin, depth, Attached: false, rule.Layer, rule.Specificity, proximity, rule.Order, rule.Source) { OriginatingElement = originatingElement });
        }
    }

    /// <summary>Matches a scoped style rule; returns the scope proximity (the number of generations between the element and the closest scoping root), or null.</summary>
    private static int? MatchScoped(CascadeRule rule, CascadeScope scope, Element element, SelectorContext context)
    {
        // The scoping roots of the nested @scope rules are memoized during the matching of a rule: without memoization, the
        // number of selector matches is exponential in the nesting depth
        var ownsCache = s_scopeStartCache is null;
        if (ownsCache)
        {
            s_scopeStartCache = [];
        }

        try
        {
            foreach (var (root, proximity) in GetScopingRoots(scope, element, context))
            {
                if (rule.ScopeRelative && ReferenceEquals(root, element))
                    continue;

                if (Matches(rule, element, context with { ScopingRoot = root }))
                    return proximity;
            }

            return null;
        }
        finally
        {
            if (ownsCache)
            {
                s_scopeStartCache = null;
            }
        }
    }

    /// <summary>The results of <see cref="MatchesScopeStart"/> during the matching of a scoped rule (the DOM does not change meanwhile).</summary>
    [ThreadStatic]
    private static Dictionary<(CascadeScope Scope, Element Candidate, SelectorContext Context), bool>? s_scopeStartCache;

    /// <summary>Returns the scoping roots of the scope that contain the element in their scope, the closest first.</summary>
    private static IEnumerable<(Node Root, int Proximity)> GetScopingRoots(CascadeScope scope, Element element, SelectorContext context)
    {
        var proximity = 0;
        for (Node? node = element; node is not null; node = GetScopeParent(node, context))
        {
            bool isRoot;
            if (scope.Start is { } start)
            {
                isRoot = node is Element candidate && MatchesScopeStart(scope, start, candidate, context);
            }
            else
            {
                // The implicit scoping root of a nested @scope rule is in the scope of the enclosing @scope rule
                isRoot = ReferenceEquals(node, scope.ImplicitRoot) && (scope.Parent is null || (node is Element implicitRoot && GetScopingRoots(scope.Parent, implicitRoot, context).Any()));
            }

            if (isRoot && IsInScope(scope, element, node, context))
            {
                yield return (node, proximity);
            }

            proximity++;
        }
    }

    /// <summary>
    /// The parent of a node when looking for scoping roots: the ancestors of an element of a shadow tree include its shadow
    /// host (which :host matches), but not the ancestors of the host.
    /// </summary>
    private static Node? GetScopeParent(Node node, SelectorContext context)
    {
        if (ReferenceEquals(node, context.ShadowHost))
            return null;

        return node is ShadowRoot shadowRoot && ReferenceEquals(shadowRoot.Host, context.ShadowHost) ? shadowRoot.Host : node.ParentNode;
    }

    private static bool MatchesScopeStart(CascadeScope scope, SelectorList start, Element candidate, SelectorContext context)
    {
        if (scope.Parent is null)
            return SelectorMatcher.Matches(start, candidate, context);

        var cache = s_scopeStartCache;
        if (cache is not null && cache.TryGetValue((scope, candidate, context), out var cached))
            return cached;

        var result = MatchesNestedScopeStart(scope, start, candidate, context);
        cache?.TryAdd((scope, candidate, context), result);
        return result;
    }

    private static bool MatchesNestedScopeStart(CascadeScope scope, SelectorList start, Element candidate, SelectorContext context)
    {
        // The scoping roots of a nested @scope rule are in the scope of the enclosing @scope rule
        foreach (var (parentRoot, _) in GetScopingRoots(scope.Parent!, candidate, context))
        {
            if (SelectorMatcher.Matches(start, candidate, context with { ScopingRoot = parentRoot }))
                return true;
        }

        return false;
    }

    /// <summary>Returns whether the element is in the scope of the scoping root: it is not a scoping limit or a descendant of one.</summary>
    private static bool IsInScope(CascadeScope scope, Element element, Node root, SelectorContext context)
    {
        if (scope.End is not { } end)
            return true;

        // The scoping root itself is a scoping limit when it matches <scope-end> (e.g. to (:scope))
        var limitContext = context with { ScopingRoot = root };
        for (Node? node = element; node is not null; node = GetScopeParent(node, context))
        {
            if (node is Element candidate && SelectorMatcher.Matches(end, candidate, limitContext))
                return false;

            if (ReferenceEquals(node, root))
                break;
        }

        return true;
    }

    /// <summary>
    /// Returns the cascaded values of the matched declaration blocks, shared by the elements that match the same blocks.
    /// </summary>
    /// <param name="blocksReferenced">Whether the cascaded values reference the list of the blocks (it must not be reused).</param>
    private Dictionary<string, CascadedValue> GetSharedCascadedValues(List<MatchedBlock> blocks, out bool blocksReferenced)
    {
        var span = CollectionsMarshal.AsSpan(blocks);
        var hash = new HashCode();
        foreach (ref readonly var block in span)
        {
            hash.Add(block.Order);
            hash.Add(block.Important);
            hash.Add(block.Declarations.Count);
        }

        var key = hash.ToHashCode();
        _sharedCascades.TryGetValue(key, out var first);
        for (var entry = first; entry is not null; entry = entry.Next)
        {
            if (span.SequenceEqual(entry.Blocks))
            {
                blocksReferenced = false;
                return entry.Values;
            }
        }

        var values = ComputeCascadedValues(blocks);
        blocksReferenced = ReferencesBlocks(values);
        _sharedCascades[key] = new SharedCascade(blocks.ToArray(), values, first);
        return values;
    }

    /// <summary>The origin of the winning declaration of a property among the sorted declaration blocks, or null (see <see cref="GetCascadeOrigin"/>).</summary>
    private static CascadeOrigin? FindWinningOrigin(List<MatchedBlock> blocks, string property)
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            var block = blocks[i];
            foreach (var declaration in block.Declarations)
            {
                if (declaration.Important == block.Important && declaration.Name == property)
                    return block.Origin;
            }
        }

        return null;
    }

    /// <summary>The cascaded values of a sequence of matched declaration blocks (the entries with the same hash code are chained).</summary>
    private sealed class SharedCascade(MatchedBlock[] blocks, Dictionary<string, CascadedValue> values, SharedCascade? next)
    {
        public MatchedBlock[] Blocks { get; } = blocks;

        public Dictionary<string, CascadedValue> Values { get; } = values;

        public SharedCascade? Next { get; } = next;
    }

    /// <summary>Returns whether a cascaded value references the declaration blocks of the cascade, to roll back the cascade after a substitution.</summary>
    private static bool ReferencesBlocks(Dictionary<string, CascadedValue> cascaded)
    {
        foreach (var value in cascaded.Values)
        {
            if (value.RollBackSubstitution is not null)
                return true;
        }

        return false;
    }

    /// <summary>Determines the cascaded value of each property (https://drafts.csswg.org/css-cascade-5/#cascaded).</summary>
    private static Dictionary<string, CascadedValue> ComputeCascadedValues(List<MatchedBlock> blocks)
    {
        // The number of declarations is an upper bound of the number of properties: the dictionary is not resized
        var capacity = 0;
        foreach (var block in blocks)
        {
            capacity += block.Declarations.Count;
        }

        // Many elements have no declaration (e.g. span, or most of the pseudo-elements): they share an empty dictionary, which
        // is never modified (the logical properties are mapped in place only when there are cascaded values)
        if (capacity == 0)
            return EmptyCascadedValues;

        var result = new Dictionary<string, CascadedValue>(capacity, StringComparer.Ordinal);
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            var block = blocks[i];
            var declarations = block.Declarations;
            for (var j = 0; j < declarations.Count; j++)
            {
                var declaration = declarations[j];
                if (declaration.Important != block.Important)
                    continue;

                var name = declaration.Name;
                if (result.ContainsKey(name))
                    continue;

                var value = declaration.Value;
                var rank = ((long)i << 20) | (uint)j;
                result[name] = GetRollback(value) == Rollback.None
                    ? WithSubstitutionRollback(new CascadedValue(value, rank, block.Source) { OriginatingElement = block.OriginatingElement, IsImportant = block.Important }, blocks, i)
                    : RollBack(blocks, name, i, value, rank);
            }
        }

        return result;
    }

    /// <summary>The keywords that roll back the cascade (https://drafts.csswg.org/css-cascade-5/#rollback-cascade).</summary>
    private enum Rollback
    {
        None,
        Origin,
        Layer,
        Rule,
    }

    private static Rollback GetRollback(CssLonghandValue value)
    {
        if (value.ShorthandName is not null)
            return Rollback.None;

        var text = value.Value;
        if (IsKeyword(text, "revert"))
            return Rollback.Origin;

        if (IsKeyword(text, "revert-layer"))
            return Rollback.Layer;

        return IsKeyword(text, "revert-rule") ? Rollback.Rule : Rollback.None;
    }

    /// <summary>
    /// Rolls back the cascade of a property whose winning declaration is revert, revert-layer or revert-rule
    /// (https://drafts.csswg.org/css-cascade-5/#default): revert keeps the declarations of the lower origins (the
    /// presentational hints are part of the author origin), revert-layer the declarations of the lower layers (the
    /// presentational hints are below the author layers, and the important declarations of the lower layers of the origin
    /// are kept), and revert-rule the declarations of the current rollback but the ones of its style rule (in the order of
    /// the cascade, not of appearance). The rollback continues while the winning declaration is a rollback keyword; a
    /// declaration reached twice is a cycle (e.g. revert-rule !important and revert-layer), and the property is then unset,
    /// like when no declaration remains.
    /// </summary>
    /// <param name="blocks">The declaration blocks, in the order of the cascade.</param>
    /// <param name="name">The name of the property.</param>
    /// <param name="index">The index of the block of the winning declaration.</param>
    /// <param name="value">The winning declaration (a rollback keyword).</param>
    /// <param name="rank">The rank of the winning declaration: the declaration it rolls back to takes its place among the logical and physical properties.</param>
    private static CascadedValue RollBack(List<MatchedBlock> blocks, string name, int index, CssLonghandValue value, long rank)
    {
        var visited = new HashSet<int>();
        var from = blocks[index];
        var rollback = Rollback.None;
        List<List<CssDeclarationEntry>>? removedRules = null;
        while (visited.Add(index))
        {
            var block = blocks[index];
            switch (GetRollback(value))
            {
                case Rollback.Origin:
                    rollback = Rollback.Origin;
                    from = block;
                    removedRules = null;
                    break;

                case Rollback.Layer:
                    rollback = Rollback.Layer;
                    from = block;
                    removedRules = null;
                    break;

                default:
                    // The declarations of the style rule (the normal and the important ones, of each of its selectors) are removed
                    (removedRules ??= []).Add(block.Declarations);
                    break;
            }

            var found = false;
            for (var i = blocks.Count - 1; i >= 0 && !found; i--)
            {
                var candidate = blocks[i];
                if (removedRules is not null && removedRules.Exists(rule => ReferenceEquals(rule, candidate.Declarations)))
                    continue;

                var isLower = rollback switch
                {
                    Rollback.Origin => candidate.Origin < from.Origin,
                    // An important revert-layer rolls back to the layers below its own in the order of the normal
                    // declarations: the declarations of the following layers, important or not, are not part of the rollback
                    Rollback.Layer when from.Important => IsInLowerNormalLayer(candidate, from),
                    Rollback.Layer => candidate.IsInLowerLayerThan(from) || IsImportantInLowerLayer(candidate, from),
                    _ => true,
                };

                if (!isLower)
                    continue;

                var declarations = candidate.Declarations;
                for (var j = 0; j < declarations.Count; j++)
                {
                    var declaration = declarations[j];
                    if (declaration.Important == candidate.Important && declaration.Name == name)
                    {
                        value = declaration.Value;
                        if (GetRollback(value) == Rollback.None)
                            return WithSubstitutionRollback(new CascadedValue(value, rank, candidate.Source) { Position = ((long)i << 20) | (uint)j, OriginatingElement = candidate.OriginatingElement, IsImportant = candidate.Important }, blocks, i);

                        index = i;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
                break;
        }

        // Without declaration to roll back to, or in a cycle, the property behaves like unset
        return new CascadedValue(new CssLonghandValue(name, "unset"), rank, source: null) { Position = -1 };

        // The important declarations of a lower layer than the normal declaration with revert-layer, in the same origin
        // and context, are part of the rollback (the cascade orders them before the normal declarations)
        static bool IsInLowerNormalLayer(MatchedBlock candidate, MatchedBlock origin)
        {
            var normalCandidate = candidate with { Important = false };
            var normalOrigin = origin with { Important = false };
            return normalCandidate.IsInLowerLayerThan(normalOrigin);
        }

        static bool IsImportantInLowerLayer(MatchedBlock candidate, MatchedBlock from) => candidate.Important && !from.Important
            && candidate.Origin == from.Origin && candidate.Context == from.Context && candidate.Attached == from.Attached && candidate.Layer < from.Layer;
    }

    /// <summary>
    /// A CSS-wide keyword that results from the substitution of the arbitrary substitution functions of a value (e.g.
    /// var(--unknown, revert-layer)) is treated as the keyword (https://drafts.csswg.org/css-values-5/#substitute-arbitrary-substitution-function):
    /// revert, revert-layer and revert-rule then roll back the cascade from the declaration, when the value is computed.
    /// </summary>
    private static CascadedValue WithSubstitutionRollback(CascadedValue cascaded, List<MatchedBlock> blocks, int index)
    {
        var value = cascaded.Value;
        if (value.ShorthandName is null && !CssSubstitution.MayContainSubstitution(value.ComputationValue ?? value.Value))
            return cascaded;

        return cascaded with { RollBackSubstitution = CreateSubstitutionRollback(blocks, index, value, cascaded.Rank) };
    }

    // The closure is created in its own method: it would otherwise be allocated on each call, for the values without substitution too
    private static Func<string, CascadedValue> CreateSubstitutionRollback(List<MatchedBlock> blocks, int index, CssLonghandValue value, long rank) =>
        keyword => RollBack(blocks, value.Name, index, new CssLonghandValue(value.Name, keyword), rank);

    private static bool IsKeyword(string value, string keyword) => value.Length == keyword.Length && Infra.InfraAscii.EqualsIgnoreAsciiCase(value, keyword);
}

/// <summary>The cascaded value of a property (https://drafts.csswg.org/css-cascade-5/#cascaded).</summary>
/// <remarks>The values are stored in a dictionary for each element: the rarely used state is in objects shared by the values of a rule (<see cref="Source"/>), or allocated when needed.</remarks>
internal readonly struct CascadedValue
{
    private readonly CascadedValueExtra? _extra;

    /// <param name="value">The declared value.</param>
    /// <param name="rank">The precedence of the winning declaration (the higher the more important): it orders the logical and physical properties that map to each other.</param>
    /// <param name="source">The base URL, the tree scope and the mixin of the declaration, or null when they are all null.</param>
    public CascadedValue(CssLonghandValue value, long rank, CascadedValueSource? source)
    {
        Value = value;
        Rank = rank;
        Source = source;
    }

    /// <summary>The declared value.</summary>
    public CssLonghandValue Value { get; }

    /// <summary>The precedence of the winning declaration (the higher the more important): it orders the logical and physical properties that map to each other.</summary>
    public long Rank { get; }

    /// <summary>The base URL, the tree scope and the mixin of the declaration, or null when they are all null.</summary>
    public CascadedValueSource? Source { get; }

    /// <summary>The URL the relative URLs are resolved against.</summary>
    public string? BaseUrl => Source?.BaseUrl;

    /// <summary>The root of the tree of the style sheet of the declaration (the tree-scoped references are looked up from it), or null.</summary>
    public Node? TreeScope => Source?.TreeScope;

    /// <summary>The application of the mixin of the declaration, or null: the value is substituted in its frame (see <see cref="CssMixinApplication"/>).</summary>
    public CssMixinApplication? Mixin => Source?.Mixin;

    /// <summary>
    /// The precedence of the declaration of the value: the rank of the winning declaration, or, when it rolls back the cascade
    /// (revert, revert-layer, revert-rule), of the declaration it rolls back to (-1 when there is none).
    /// </summary>
    public long Position
    {
        get => _extra?.Position ?? Rank;
        init => _extra = _extra is not null ? _extra with { Position = value } : value == Rank ? null : new CascadedValueExtra(value, RollBackSubstitution: null, OriginatingElement: null);
    }

    /// <summary>
    /// Rolls back the cascade from the declaration of the value, when its substitution results in revert, revert-layer or
    /// revert-rule (the keyword); null when the value contains no arbitrary substitution function.
    /// </summary>
    public Func<string, CascadedValue>? RollBackSubstitution
    {
        get => _extra?.RollBackSubstitution;
        init => _extra = _extra is not null ? _extra with { RollBackSubstitution = value } : new CascadedValueExtra(Rank, value, OriginatingElement: null);
    }

    /// <summary>The originating element of the ::part() rule of the declaration (the shadow host), whose attributes attr() reads; null for the element itself.</summary>
    public Element? OriginatingElement
    {
        get => _extra?.OriginatingElement;
        init => _extra = _extra is not null ? _extra with { OriginatingElement = value } : value is null ? null : new CascadedValueExtra(Rank, RollBackSubstitution: null, value);
    }

    /// <summary>Whether the declaration of the value is important: it wins over the animations (https://drafts.csswg.org/css-cascade-5/#cascade-origin).</summary>
    public bool IsImportant
    {
        get => _extra?.Important ?? false;
        init => _extra = _extra is not null ? _extra with { Important = value } : !value ? null : new CascadedValueExtra(Rank, RollBackSubstitution: null, OriginatingElement: null, Important: true);
    }

    /// <summary>
    /// Returns whether the value wins over the value of a logical or physical property that maps to the same property
    /// (https://drafts.csswg.org/css-logical/#logical-property-group): the winning declaration with the highest rank, unless
    /// it rolls back the cascade past the other value, whose declaration is in a lower block, then the declaration it
    /// rolls back to must have a higher precedence than the one of the other value.
    /// </summary>
    public bool WinsOver(CascadedValue other)
    {
        if (Rank < other.Rank)
            return !other.WinsOver(this);

        const long BlockMask = ~((1L << 20) - 1);
        return Position == Rank || other.Position >= (Rank & BlockMask) || Position >= other.Position;
    }
}

/// <summary>The rarely used state of a cascaded value: the values that roll back the cascade, and the declarations of the ::part() rules.</summary>
internal sealed record CascadedValueExtra(long Position, Func<string, CascadedValue>? RollBackSubstitution, Element? OriginatingElement, bool Important = false);

/// <summary>The base URL, the tree scope and the mixin of the declarations of a rule (see <see cref="CascadedValue"/>).</summary>
internal sealed record CascadedValueSource(string? BaseUrl, Node? TreeScope, CssMixinApplication? Mixin)
{
    /// <summary>Returns the source, or null when the base URL, the tree scope and the mixin are null.</summary>
    public static CascadedValueSource? Create(string? baseUrl, Node? treeScope, CssMixinApplication? mixin)
        => baseUrl is null && treeScope is null && mixin is null ? null : new(baseUrl, treeScope, mixin);
}

/// <summary>
/// The properties whose computed value is being computed, by style: the computations are nested, and there are only a few
/// at a time, so a stack searched linearly is faster than a set (no hash code of the style and of the property name).
/// </summary>
internal sealed class ComputingPropertyStack
{
    private (ComputedStyle Style, string Property)[] _items = new (ComputedStyle, string)[16];
    private int _count;

    /// <summary>Adds the property of the style; returns false when it is already being computed.</summary>
    public bool Add(ComputedStyle style, string property)
    {
        if (Contains(style, property))
            return false;

        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _count * 2);
        }

        _items[_count++] = (style, property);
        return true;
    }

    public bool Contains(ComputedStyle style, string property)
    {
        var items = _items;
        for (var i = _count - 1; i >= 0; i--)
        {
            ref var item = ref items[i];
            if (ReferenceEquals(item.Style, style) && (ReferenceEquals(item.Property, property) || string.Equals(item.Property, property, StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    /// <summary>Removes the property of the style (the last one added, as the computations are nested).</summary>
    public void Remove(ComputedStyle style, string property)
    {
        var items = _items;
        if (_count > 0 && ReferenceEquals(items[_count - 1].Style, style) && string.Equals(items[_count - 1].Property, property, StringComparison.Ordinal))
        {
            items[--_count] = default;
            return;
        }

        for (var i = _count - 1; i >= 0; i--)
        {
            ref var item = ref items[i];
            if (ReferenceEquals(item.Style, style) && string.Equals(item.Property, property, StringComparison.Ordinal))
            {
                Array.Copy(items, i + 1, items, i, _count - i - 1);
                items[--_count] = default;
                return;
            }
        }
    }
}
