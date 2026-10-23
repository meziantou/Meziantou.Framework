using System.Collections.Immutable;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>The cascade origins (https://drafts.csswg.org/css-cascade-5/#cascading-origins).</summary>
internal enum CascadeOrigin : byte
{
    UserAgent,
    User,
    Author,
}

/// <summary>How the subject of a rule is matched.</summary>
internal enum CascadeRuleKind : byte
{
    /// <summary>The rule matches the elements of the tree of its style sheet.</summary>
    Element,

    /// <summary>The subject is the shadow host (:host, :host(), :host-context()): the rule matches the host of the shadow tree of its style sheet.</summary>
    Host,

    /// <summary>The rule ends with ::slotted(): it matches the elements assigned to the slots of the shadow tree of its style sheet.</summary>
    Slotted,

    /// <summary>The rule ends with ::part(): it matches the parts of the shadow trees of the shadow hosts its originating element matches.</summary>
    Part,
}

/// <summary>The scoping roots and limits of a @scope rule (https://drafts.csswg.org/css-cascade-6/#scoped-styles).</summary>
internal sealed class CascadeScope
{
    /// <summary>The &lt;scope-start&gt; selector, or null when the scoping root is <see cref="ImplicitRoot"/>.</summary>
    public SelectorList? Start { get; init; }

    /// <summary>The &lt;scope-end&gt; selector (the scoping limits), or null.</summary>
    public SelectorList? End { get; init; }

    /// <summary>The scoping root when there is no &lt;scope-start&gt;: the parent element of the owner node of the style sheet, or the root of the tree.</summary>
    public Node? ImplicitRoot { get; init; }

    /// <summary>The enclosing @scope rule: the scoping roots of this rule are in its scope.</summary>
    public CascadeScope? Parent { get; init; }
}

/// <summary>The conditions of the @container rules that contain a style rule: the conditions of every level must match.</summary>
internal sealed class CascadeContainerQuery
{
    /// <summary>The &lt;container-condition&gt;s of the @container rule: the query matches when any of them is true.</summary>
    public required IReadOnlyList<ContainerCondition> Conditions { get; init; }

    /// <summary>The enclosing @container rule.</summary>
    public CascadeContainerQuery? Parent { get; init; }
}

/// <summary>A style rule (or a selector of a style rule) of the cascade.</summary>
internal sealed class CascadeRule
{
    public required ComplexSelector Selector { get; init; }

    /// <summary>The declarations of the rule (the list of the CSSOM declaration block).</summary>
    public required List<CssDeclarationEntry> Declarations { get; init; }

    public required CascadeOrigin Origin { get; init; }

    public Specificity Specificity { get; set; }

    /// <summary>The rank of the cascade layer (https://drafts.csswg.org/css-cascade-5/#layering): <see cref="Unlayered"/> for the unlayered rules.</summary>
    public int Layer { get; set; } = Unlayered;

    /// <summary>The order of appearance.</summary>
    public int Order { get; init; }

    public CascadeScope? Scope { get; init; }

    /// <summary>The container queries of the enclosing @container rules, or null.</summary>
    public CascadeContainerQuery? Containers { get; init; }

    /// <summary>
    /// Whether the selector of a scoped rule is implicitly relative to the scoping root (it contains neither :scope nor
    /// &amp;): the subject is then a descendant of the scoping root (https://drafts.csswg.org/css-cascade-6/#scoped-rules).
    /// </summary>
    public bool ScopeRelative { get; init; }

    public CascadeRuleKind Kind { get; init; }

    /// <summary>The pseudo-element the rule applies to (e.g. "before"), or null for the elements.</summary>
    public string? PseudoElement { get; init; }

    /// <summary>The argument of the pseudo-element (e.g. "select" for ::picker(select)), or null.</summary>
    public string? PseudoElementArgument { get; init; }

    /// <summary>
    /// Whether <c>:only-child</c> follows the view transition pseudo-element of the selector (e.g. <c>::view-transition-old(*):only-child</c>):
    /// it is matched against the pseudo-element in the view transition tree (https://drafts.csswg.org/css-view-transitions-1/#pseudo-element-tree).
    /// </summary>
    public bool PseudoElementOnlyChild { get; init; }

    /// <summary>Whether <c>:not(:only-child)</c> follows the view transition pseudo-element of the selector.</summary>
    public bool PseudoElementNotOnlyChild { get; init; }

    /// <summary>The argument of ::slotted().</summary>
    public SelectorList? SlottedSelector { get; init; }

    /// <summary>The part names of ::part().</summary>
    public ImmutableArray<string> PartNames { get; init; } = [];

    /// <summary>The URL the relative URLs of the declarations are resolved against.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The root of the tree of the style sheet of the rule (a document or a shadow root), or null for the user agent style
    /// sheet: the tree-scoped names of the declarations (the custom functions) are looked up from this tree.
    /// </summary>
    public Node? TreeScope { get; init; }

    /// <summary>The application of the mixin whose @result rule contains the rule, or null: the values of its declarations are substituted in the frame of the application.</summary>
    public CssMixinApplication? Mixin { get; init; }

    /// <summary>The namespace prefixes declared by the @namespace rules of the style sheet of the rule, or null.</summary>
    public IReadOnlyDictionary<string, string>? NamespacePrefixes { get; init; }

    /// <summary>The <see cref="BaseUrl"/>, <see cref="TreeScope"/> and <see cref="Mixin"/> of the rule, shared by its cascaded values (null when they are all null).</summary>
    public CascadedValueSource? Source
    {
        get
        {
            // Created on first use (the properties are initialized after the constructor). A rule of the user agent style
            // sheet is shared by the threads: they may create equivalent instances.
            var source = Volatile.Read(ref _source);
            if (source is null)
            {
                source = (object?)CascadedValueSource.Create(BaseUrl, TreeScope, Mixin, NamespacePrefixes) ?? NoSource;
                Volatile.Write(ref _source, source);
            }

            return source as CascadedValueSource;
        }
    }

    private static readonly object NoSource = new();
    private object? _source;

    public bool HasImportant { get; init; }

    /// <summary>Whether the rule is in a @starting-style rule: it only applies to the starting style (https://drafts.csswg.org/css-transitions-2/#defining-before-change-style).</summary>
    public bool StartingStyle { get; init; }

    public bool HasNormal { get; init; }

    /// <summary>
    /// Whether the rule is a presentational hint marked "only if border is not equivalent to zero"
    /// (https://html.spec.whatwg.org/multipage/rendering.html#magic-border-selector): it only applies when the border attribute
    /// of the table of its selector (the element, or the table of the cell) is not zero.
    /// </summary>
    public bool OnlyIfTableBorderIsNotZero { get; init; }

    /// <summary>
    /// The rendering of the select element that a rule of the user agent style sheet requires
    /// (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2), which a selector cannot express.
    /// </summary>
    public SelectRuleCondition SelectCondition { get; init; }

    public const int Unlayered = int.MaxValue;

    /// <summary>The layer of the presentational hints: before all the other layers of the author origin.</summary>
    public const int PresentationalHints = -1;
}

/// <summary>
/// The conditions of the rules of the user agent style sheet that depend on the rendering of a select element
/// (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2: "The following styles are expected to apply to
/// select elements when they are being rendered [...]"), or on the appearance of another form control
/// (https://drafts.csswg.org/css-forms-1/#appearance: "When a control is in that state, the user agent applies styles from
/// the Basic Appearance User Agent Stylesheet to that control").
/// </summary>
internal enum SelectRuleCondition : byte
{
    None,

    /// <summary>Rendered as a drop-down box.</summary>
    DropDown,

    /// <summary>Rendered with native appearance or primitive appearance.</summary>
    NativeAppearance,

    /// <summary>Rendered as a drop-down box with native appearance or primitive appearance.</summary>
    DropDownNativeAppearance,

    /// <summary>Rendered with base appearance (the rules of the contents apply when the contents are rendered with base appearance).</summary>
    BaseAppearance,

    /// <summary>Rendered as a drop-down box with base appearance.</summary>
    DropDownBaseAppearance,

    /// <summary>Rendered as a list box with base appearance.</summary>
    ListBoxBaseAppearance,

    /// <summary>Rendered as a list box with native appearance or primitive appearance.</summary>
    ListBoxNativeAppearance,

    /// <summary>
    /// The subject is a form control other than a select element, or one of its pseudo-elements, and the control has base
    /// appearance (https://drafts.csswg.org/css-forms-1/#appearance: the computed value of its appearance property is base).
    /// </summary>
    ControlBaseAppearance,

    /// <summary>The subject is not a form control with base appearance, nor one of its pseudo-elements: the rules of the parts of the controls with primitive appearance.</summary>
    ControlNativeAppearance,
}

/// <summary>The rules of a tree (or of the user agent style sheet), indexed by the rightmost compound selector of their selector.</summary>
internal sealed class CascadeRuleSet
{
    // The names are compared ignoring the case (they are case-insensitive in quirks mode, and for the HTML elements): the
    // candidates are a superset of the rules that match, which are then matched, and the names of the elements are not
    // lowercased for each lookup
    private readonly Dictionary<string, List<CascadeRule>> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CascadeRule>> _byClass = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CascadeRule>> _byTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CascadeRule> _universal = [];

    /// <summary>The rules added to the lists of several tags (e.g. <c>:is(h1, h2)</c>), enumerated once by <see cref="EnumerateRules"/>.</summary>
    private HashSet<CascadeRule>? _multipleTagRules;

    private static readonly string[] HeadingTags = ["h1", "h2", "h3", "h4", "h5", "h6"];

    /// <summary>The pseudo-elements of the rules of the lists of candidates (most elements have no rule for their pseudo-elements).</summary>
    private readonly HashSet<string> _pseudoElements = new(StringComparer.Ordinal);

    /// <summary>The pseudo-elements of the rules that declare an animation or a transition property, with the host, slotted and part rules (see <see cref="DeclaresAnimationsFor"/>).</summary>
    private readonly HashSet<string> _animatedPseudoElements = new(StringComparer.Ordinal);

    /// <summary>The rules of a pseudo-element that declare an animation or a transition property.</summary>
    private readonly HashSet<CascadeRule> _animatedPseudoElementRules = new(ReferenceEqualityComparer.Instance);

    private StyleInvalidationFeatures? _invalidationFeatures;

    public List<CascadeRule> HostRules { get; } = [];

    public List<CascadeRule> SlottedRules { get; } = [];

    public List<CascadeRule> PartRules { get; } = [];

    public int Count { get; private set; }

    /// <summary>Whether a rule declares a property whose value may reference an image to fetch (see <see cref="Om.CssImageFetching"/>).</summary>
    public bool ReferencesImages { get; private set; }

    /// <summary>Whether a rule declares a property whose value may reference an element of another document to fetch (see <see cref="Om.CssImageFetching.ReferencesExternalResources"/>).</summary>
    public bool ReferencesExternalResources { get; private set; }

    /// <summary>Whether a rule is in a @starting-style rule.</summary>
    public bool HasStartingStyleRules { get; private set; }

    /// <summary>Whether a rule declares an animation or a transition property (the CSS animations and transitions are then updated at each frame).</summary>
    public bool DeclaresAnimations { get; private set; }

    /// <summary>Whether a rule declares <c>content-visibility</c> or <c>contain-intrinsic-*</c> (the document is then laid out by the rendering steps, see <see cref="StyleEngine.MayHaveContentVisibility"/>).</summary>
    public bool DeclaresContentVisibility { get; private set; }

    /// <summary>
    /// Whether a rule declares <c>position-try-fallbacks</c> (the document is then laid out by the rendering steps, which record the
    /// last successful position options, see <see cref="StyleEngine.MayHavePositionTryFallbacks"/>).
    /// </summary>
    public bool DeclaresPositionTryFallbacks { get; private set; }

    /// <summary>Whether a rule is in a @container rule with a container scroll-state query (the document is then laid out by the rendering steps, see <see cref="StyleEngine.MayHaveScrollStateQueries"/>).</summary>
    public bool HasScrollStateQueries { get; private set; }

    /// <summary>
    /// Whether a rule declares <c>scroll-marker-group</c> or <c>scroll-target-group</c>, or styles a <c>::scroll-button()</c> (the
    /// document is then laid out by the rendering steps, see <see cref="StyleEngine.MayHaveScrollNavigationControls"/>).
    /// </summary>
    public bool HasScrollNavigationControls { get; private set; }

    public void Add(CascadeRule rule)
    {
        Count++;
        if (!HasScrollNavigationControls && (rule.PseudoElement == Layout.ScrollControls.ButtonRuleName || rule.Declarations.Exists(static declaration => declaration.Name is "scroll-marker-group" or "scroll-target-group")))
        {
            HasScrollNavigationControls = true;
        }

        if (!ReferencesImages && Om.CssImageFetching.ReferencesImages(rule.Declarations))
        {
            ReferencesImages = true;
        }

        if (!ReferencesExternalResources && Om.CssImageFetching.ReferencesExternalResources(rule.Declarations))
        {
            ReferencesExternalResources = true;
        }

        if ((!DeclaresAnimations || rule.PseudoElement is not null) && rule.Declarations.Exists(static declaration => IsAnimationProperty(declaration.Name)))
        {
            DeclaresAnimations = true;
            if (rule.PseudoElement is { } animatedPseudoElement)
            {
                _animatedPseudoElements.Add(animatedPseudoElement);
                _animatedPseudoElementRules.Add(rule);
            }
        }

        if (!DeclaresContentVisibility && rule.Declarations.Exists(static declaration => IsContentVisibilityProperty(declaration.Name)))
        {
            DeclaresContentVisibility = true;
        }

        if (!DeclaresPositionTryFallbacks && rule.Declarations.Exists(static declaration => declaration.Name == "position-try-fallbacks"))
        {
            DeclaresPositionTryFallbacks = true;
        }

        if (rule.StartingStyle)
        {
            HasStartingStyleRules = true;
        }

        if (!DeclaresTimelines && rule.Declarations.Exists(static declaration => IsTimelineDeclaration(declaration)))
        {
            DeclaresTimelines = true;
        }

        if (!HasScrollStateQueries && rule.Containers is { } containers && HasScrollStateQuery(containers))
        {
            HasScrollStateQueries = true;
        }

        switch (rule.Kind)
        {
            case CascadeRuleKind.Host:
                HostRules.Add(rule);
                return;
            case CascadeRuleKind.Slotted:
                SlottedRules.Add(rule);
                return;
            case CascadeRuleKind.Part:
                PartRules.Add(rule);
                return;
        }

        if (rule.PseudoElement is { } pseudoElement)
        {
            _pseudoElements.Add(pseudoElement);
        }

        string? id = null, className = null, tag = null;
        foreach (var simple in rule.Selector.Compounds[^1].Selectors)
        {
            switch (simple)
            {
                case IdSelector idSelector:
                    id ??= idSelector.Id;
                    break;
                case ClassSelector classSelector:
                    className ??= classSelector.ClassName;
                    break;
                case TypeSelector { LowercaseLocalName: { } localName }:
                    tag ??= localName;
                    break;
            }
        }

        if (id is not null)
        {
            AddTo(_byId, id, rule);
        }
        else if (className is not null)
        {
            AddTo(_byClass, className, rule);
        }
        else if (tag is not null)
        {
            AddTo(_byTag, tag, rule);
        }
        else if (GetSubjectTags(rule.Selector.Compounds[^1]) is { Count: > 0 } tags)
        {
            // The subject can only be one of a few elements (e.g. :is(h1, h2) or :heading): the rule is a candidate of each
            foreach (var subjectTag in tags)
            {
                if (!_byTag.TryGetValue(subjectTag, out var list) || list.Count == 0 || !ReferenceEquals(list[^1], rule))
                {
                    AddTo(_byTag, subjectTag, rule);
                }
            }

            (_multipleTagRules ??= []).Add(rule);
        }
        else
        {
            _universal.Add(rule);
        }
    }

    /// <summary>
    /// Returns the ASCII lowercase local names one of which the subject of a compound selector without type selector must
    /// have, or null when it can be any element: the arguments of a :is() or :where() whose subjects have type selectors,
    /// and the headings of :heading and :heading() (the HTML elements h1 to h6).
    /// </summary>
    private static List<string>? GetSubjectTags(CompoundSelector compound)
    {
        foreach (var simple in compound.Selectors)
        {
            switch (simple)
            {
                case PseudoElementSelector:
                    return null;

                case PseudoClassSelector { Kind: PseudoClass.Heading or PseudoClass.HeadingFunction }:
                    return [.. HeadingTags];

                case PseudoClassSelector { Kind: PseudoClass.Is or PseudoClass.Where, Argument: { Selectors.Length: > 0 } argument }:
                {
                    var tags = new List<string>();
                    foreach (var complex in argument.Selectors)
                    {
                        var subject = complex.Compounds[^1];
                        var subjectTag = GetTypeSelectorTag(subject);
                        if (subjectTag is not null)
                        {
                            tags.Add(subjectTag);
                        }
                        else if (GetSubjectTags(subject) is { } nestedTags)
                        {
                            tags.AddRange(nestedTags);
                        }
                        else
                        {
                            tags = null;
                            break;
                        }
                    }

                    if (tags is not null)
                        return tags;

                    break;
                }
            }
        }

        return null;
    }

    private static string? GetTypeSelectorTag(CompoundSelector compound)
    {
        foreach (var simple in compound.Selectors)
        {
            if (simple is TypeSelector { LowercaseLocalName: { } localName })
                return localName;
        }

        return null;
    }

    /// <summary>Returns whether a candidate rule may apply to a pseudo-element (see <see cref="CascadeRule.PseudoElement"/>), or to an element when the name is null.</summary>
    /// <summary>Returns whether a property is an animation or a transition longhand.</summary>
    /// <summary>Whether a property is <c>content-visibility</c> or one of the <c>contain-intrinsic-*</c> properties (whose <c>auto</c> keyword records the last remembered size).</summary>
    /// <summary>The features of the selectors of the rules, collected on first use (see <see cref="StyleInvalidationFeatures"/>).</summary>
    public StyleInvalidationFeatures InvalidationFeatures
    {
        get
        {
            // (the rule sets of the user agent are shared by the threads: they may collect equivalent features)
            var features = Volatile.Read(ref _invalidationFeatures);
            if (features is null)
            {
                features = StyleInvalidationFeatures.Create(this);
                Volatile.Write(ref _invalidationFeatures, features);
            }

            return features;
        }
    }

    /// <summary>Enumerates all the rules of the set.</summary>
    public IEnumerable<CascadeRule> EnumerateRules()
    {
        foreach (var rules in _byId.Values)
        {
            foreach (var rule in rules)
                yield return rule;
        }

        foreach (var rules in _byClass.Values)
        {
            foreach (var rule in rules)
                yield return rule;
        }

        foreach (var rules in _byTag.Values)
        {
            foreach (var rule in rules)
            {
                if (_multipleTagRules is null || !_multipleTagRules.Contains(rule))
                    yield return rule;
            }
        }

        if (_multipleTagRules is not null)
        {
            foreach (var rule in _multipleTagRules)
                yield return rule;
        }

        foreach (var rule in _universal)
            yield return rule;

        foreach (var rule in HostRules)
            yield return rule;

        foreach (var rule in SlottedRules)
            yield return rule;

        foreach (var rule in PartRules)
            yield return rule;
    }

    /// <summary>
    /// Whether a rule declares a named scroll-driven timeline or the timeline of an animation: the timelines of the animations
    /// are looked up by name in the styles of other elements (https://drafts.csswg.org/scroll-animations-1/#timeline-scoping).
    /// </summary>
    public bool DeclaresTimelines { get; private set; }

    internal static bool IsTimelineDeclaration(Om.CssDeclarationEntry declaration)
    {
        var name = declaration.Name;
        return name.StartsWith("scroll-timeline", StringComparison.Ordinal) || name.StartsWith("view-timeline", StringComparison.Ordinal) || name == "timeline-scope"
            || (name == "animation-timeline" && !declaration.Value.Value.Equals("auto", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsContentVisibilityProperty(string name) => name == "content-visibility" || name.StartsWith("contain-intrinsic-", StringComparison.Ordinal) || name == "all";

    private static bool HasScrollStateQuery(CascadeContainerQuery query)
    {
        for (var current = query; current is not null; current = current.Parent)
        {
            foreach (var condition in current.Conditions)
            {
                if ((condition.Features & ContainerQueryFeatures.ScrollState) != ContainerQueryFeatures.None)
                    return true;
            }
        }

        return false;
    }

    internal static bool IsAnimationProperty(string name) => name.StartsWith("animation-", StringComparison.Ordinal) || name.StartsWith("transition-", StringComparison.Ordinal);

    public bool MayHaveRulesFor(string? pseudoElement) => pseudoElement is null || _pseudoElements.Contains(pseudoElement);

    /// <summary>Returns whether a candidate rule of the element (see <see cref="GetCandidates"/>) has the pseudo-element (its selector is not matched).</summary>
    public bool MayHaveRulesFor(Element element, string pseudoElement)
    {
        if (!_pseudoElements.Contains(pseudoElement))
            return false;

        foreach (var rules in GetCandidates(element))
        {
            foreach (var rule in rules)
            {
                if (string.Equals(rule.PseudoElement, pseudoElement, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether a rule of the set (a host, slotted or part rule included) declares an animation or a transition property for the pseudo-element.</summary>
    public bool DeclaresAnimationsFor(string pseudoElement) => _animatedPseudoElements.Contains(pseudoElement);

    /// <summary>Returns whether a candidate rule of the element (see <see cref="GetCandidates"/>) has the pseudo-element and declares an animation or a transition property (its selector is not matched).</summary>
    public bool MayHaveAnimationRulesFor(Element element, string pseudoElement)
    {
        if (!_animatedPseudoElements.Contains(pseudoElement))
            return false;

        foreach (var rules in GetCandidates(element))
        {
            foreach (var rule in rules)
            {
                if (string.Equals(rule.PseudoElement, pseudoElement, StringComparison.Ordinal) && _animatedPseudoElementRules.Contains(rule))
                    return true;
            }
        }

        return false;
    }

    private static void AddTo(Dictionary<string, List<CascadeRule>> map, string key, CascadeRule rule)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(rule);
    }

    /// <summary>
    /// Returns the lists of the rules that may match the element (the rules whose rightmost compound selector cannot match
    /// it are skipped): the rules are matched from the lists, without being copied.
    /// </summary>
    public CandidateLists GetCandidates(Element element) => new(this, element);

    /// <summary>Enumerates the rules of the id, of the classes and of the tag of an element, then the universal rules.</summary>
    public struct CandidateLists(CascadeRuleSet ruleSet, Element element)
    {
        private int _state;
        private int _classIndex;
        private ClassNames? _classNames;

        public List<CascadeRule> Current { get; private set; } = null!;

        public readonly CandidateLists GetEnumerator() => this;

        public bool MoveNext()
        {
            while (true)
            {
                switch (_state)
                {
                    case 0:
                        _state = 1;
                        if (ruleSet._byId.Count > 0 && element.IdValue is { Length: > 0 } id && ruleSet._byId.TryGetValue(id, out var idRules))
                        {
                            Current = idRules;
                            return true;
                        }

                        break;

                    case 1:
                        if (ruleSet._byClass.Count > 0)
                        {
                            var classNames = _classNames ??= element.GetStyleClassNames();
                            while (_classIndex < classNames.Count)
                            {
                                if (ruleSet._byClass.TryGetValue(classNames[_classIndex++], out var classRules))
                                {
                                    Current = classRules;
                                    return true;
                                }
                            }
                        }

                        _state = 2;
                        break;

                    case 2:
                        _state = 3;
                        if (ruleSet._byTag.TryGetValue(element.LocalName, out var tagRules))
                        {
                            Current = tagRules;
                            return true;
                        }

                        break;

                    case 3:
                        _state = 4;
                        Current = ruleSet._universal;
                        return true;

                    default:
                        return false;
                }
            }
        }
    }
}

/// <summary>The cascade layers of a tree (https://drafts.csswg.org/css-cascade-5/#layer-ordering).</summary>
internal sealed class CascadeLayerTree
{
    public CascadeLayer Root { get; } = new();

    /// <summary>Assigns the ranks: the sublayers of a layer precede its own rules, and the layers precede the unlayered rules.</summary>
    public void AssignRanks()
    {
        var rank = 0;
        Assign(Root, ref rank);
    }

    private static void Assign(CascadeLayer layer, ref int rank)
    {
        foreach (var child in layer.Children)
        {
            Assign(child, ref rank);
        }

        layer.Rank = layer.Parent is null ? CascadeRule.Unlayered : rank++;
    }
}

/// <summary>A cascade layer.</summary>
internal sealed class CascadeLayer
{
    private Dictionary<string, CascadeLayer>? _named;

    public CascadeLayer? Parent { get; private init; }

    public List<CascadeLayer> Children { get; } = [];

    public int Rank { get; set; }

    /// <summary>Returns the sublayer with the (possibly dotted) name, declaring it when needed.</summary>
    public CascadeLayer GetOrAdd(string name)
    {
        var layer = this;
        foreach (var part in name.Split('.'))
        {
            layer._named ??= new(StringComparer.Ordinal);
            if (!layer._named.TryGetValue(part, out var child))
            {
                child = new CascadeLayer { Parent = layer };
                layer._named[part] = child;
                layer.Children.Add(child);
            }

            layer = child;
        }

        return layer;
    }

    /// <summary>Declares an anonymous sublayer.</summary>
    public CascadeLayer AddAnonymous()
    {
        var child = new CascadeLayer { Parent = this };
        Children.Add(child);
        return child;
    }
}
