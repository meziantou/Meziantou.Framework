using System.Collections.Immutable;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Collects the style rules of style sheets into a <see cref="CascadeRuleSet"/>: the conditional rules (@media,
/// @supports) are evaluated, the nested rules are flattened, and the cascade layers and the scopes are resolved. The
/// container queries (@container) are evaluated for each element by the cascade.
/// </summary>
internal sealed class CascadeRuleCollector
{
    // The order of appearance is global, so that the orders of the rules of different trees never collide
    private static int s_order;

    private readonly CascadeRuleSet _rules = new();
    private readonly CascadeLayerTree _layers = new();
    private readonly List<(CascadeRule Rule, CascadeLayer Layer)> _layeredRules = [];
    private readonly List<(CssPropertyRule Rule, CascadeLayer Layer)> _propertyRules = [];
    private readonly List<(CssFunctionRule Rule, CascadeLayer Layer)> _functionRules = [];
    private readonly List<(CssMixinRule Rule, CascadeLayer Layer)> _mixinRules = [];
    private readonly List<(CssFontFaceRule Rule, CascadeLayer Layer)> _fontFaceRules = [];
    private readonly List<(CssFontFeatureValuesRule Rule, CascadeLayer Layer)> _fontFeatureValuesRules = [];
    private readonly List<(CssFontPaletteValuesRule Rule, CascadeLayer Layer)> _fontPaletteValuesRules = [];
    private readonly List<(CssKeyframesRule Rule, CascadeLayer Layer)> _keyframesRules = [];
    private readonly List<(CssViewTransitionRule Rule, CascadeLayer Layer)> _viewTransitionRules = [];
    private readonly List<(CssPositionTryRule Rule, CascadeLayer Layer)> _positionTryRules = [];
    private readonly List<(CssCounterStyleRule Rule, CascadeLayer Layer)> _counterStyleRules = [];
    private readonly List<(CssPageRule Rule, CascadeLayer Layer, string? BaseUrl)> _pageRules = [];
    private bool _inStartingStyle;
    private readonly MediaEnvironment _environment;
    private readonly CascadeOrigin _origin;
    private readonly Node? _treeRoot;

    /// <summary>The maximum depth of the nested applications of mixins.</summary>
    private const int MaxMixinDepth = 32;

    /// <summary>The mixins being applied (a mixin applied by its own result is a cycle: the @apply rule is ignored).</summary>
    private readonly List<CssMixinRule> _appliedMixins = [];

    /// <summary>The application of the mixin whose @result rules are being added, or null.</summary>
    private CssMixinApplication? _currentMixin;

    /// <summary>The namespace prefixes declared by the style sheet whose rules are added, or null.</summary>
    private IReadOnlyDictionary<string, string>? _namespacePrefixes;

    /// <param name="environment">The environment the media queries are evaluated against.</param>
    /// <param name="origin">The origin of the rules.</param>
    /// <param name="treeRoot">The root of the tree of the style sheets (the scoping root of the @scope rules without prelude of the constructed style sheets).</param>
    public CascadeRuleCollector(MediaEnvironment environment, CascadeOrigin origin, Node? treeRoot)
    {
        _environment = environment;
        _origin = origin;
        _treeRoot = treeRoot;
    }

    /// <summary>The specificity of the rules is zero (presentational hints).</summary>
    public bool ZeroSpecificity { get; init; }

    /// <summary>The rules are in the layer of the presentational hints.</summary>
    public bool PresentationalHints { get; init; }

    private bool _onlyIfTableBorderIsNotZero;

    /// <summary>The condition of the rules that are added (the rules of the user agent style sheet of the select element).</summary>
    public SelectRuleCondition SelectCondition { get; set; }

    /// <summary>The registered custom properties (https://drafts.css-houdini.org/css-properties-values-api/#at-property-rule), by name (available after <see cref="Complete"/>).</summary>
    public Dictionary<string, CssPropertyRule> PropertyRules { get; } = new(StringComparer.Ordinal);

    /// <summary>The custom functions (https://drafts.csswg.org/css-mixins-1/#function-rule), by name (available after <see cref="Complete"/>).</summary>
    public Dictionary<string, CssFunctionRule> FunctionRules { get; } = new(StringComparer.Ordinal);

    /// <summary>The mixins (https://drafts.csswg.org/css-mixins-1/#mixin-rule), by name (available after <see cref="Complete"/>).</summary>
    public Dictionary<string, CssMixinRule> MixinRules { get; } = new(StringComparer.Ordinal);

    /// <summary>The @keyframes rules (https://drafts.csswg.org/css-animations-1/#keyframes), by name (available after <see cref="Complete"/>).</summary>
    public Dictionary<string, CssKeyframesRule> KeyframesRules { get; } = new(StringComparer.Ordinal);

    /// <summary>The @view-transition rule that applies (the last one of the strongest layer), or null.</summary>
    public CssViewTransitionRule? ViewTransitionRule { get; private set; }

    /// <summary>The @position-try rules (https://drafts.csswg.org/css-anchor-position-1/#fallback-rule), by name (available after <see cref="Complete"/>).</summary>
    public Dictionary<string, CssPositionTryRule> PositionTryRules { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The @counter-style rules (https://drafts.csswg.org/css-counter-styles-3/#the-counter-style-rule), by name (available
    /// after <see cref="Complete"/>); the predefined names are in lowercase.
    /// </summary>
    public Dictionary<string, CssCounterStyleRule> CounterStyleRules { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The @page rules (https://drafts.csswg.org/css-page-3/#at-page-rule) in the order of appearance, with the rank of their
    /// cascade layer and the base URL of their style sheet (available after <see cref="Complete"/>).
    /// </summary>
    public List<(CssPageRule Rule, int Layer, string? BaseUrl)> PageRules { get; } = [];

    /// <summary>
    /// Returns the mixin with the name, dereferenced as a tree-scoped reference from a tree (the tree of the @apply rule, or the
    /// tree of the mixin whose @result rule contains it), and the root of its tree. The @apply rules are ignored when it is null.
    /// </summary>
    public Func<string, Node?, (CssMixinRule Rule, Node Scope)?>? MixinResolver { get; init; }

    /// <summary>Whether the style sheets contain an @apply rule (they must be collected again with a <see cref="MixinResolver"/>).</summary>
    public bool HasApplyRules { get; private set; }

    /// <summary>
    /// The rank of the cascade layer of the @font-face rules (available after <see cref="Complete"/>): when several rules
    /// define the same font face, the one in the last layer wins (https://drafts.csswg.org/css-cascade-5/#layering).
    /// </summary>
    public Dictionary<CssFontFaceRule, int> FontFaceLayers { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The @font-feature-values rules that apply (available after <see cref="Complete"/>), from the weakest cascade layer to
    /// the strongest one, then in order: when several rules define a feature value name for a family, the last one of the list
    /// wins (https://drafts.csswg.org/css-fonts-4/#font-feature-values, https://drafts.csswg.org/css-cascade-5/#layering).
    /// </summary>
    public List<CssFontFeatureValuesRule> FontFeatureValuesRules { get; } = [];

    /// <summary>
    /// The @font-palette-values rules that apply (available after <see cref="Complete"/>), from the weakest cascade layer to
    /// the strongest one, then in order: when several rules define a palette of the same name for a family, the last one of
    /// the list wins (https://drafts.csswg.org/css-fonts-4/#font-palette-values, https://drafts.csswg.org/css-cascade-5/#layering).
    /// </summary>
    public List<CssFontPaletteValuesRule> FontPaletteValuesRules { get; } = [];

    public void AddStyleSheet(CssStyleSheet sheet, string? baseUrl)
    {
        // The scoping root of the @scope rules without prelude is the parent of the owner node: the host when it is a shadow root
        var ownerParent = (sheet.OwnerNode as Element)?.ParentNode switch
        {
            Element parent => parent,
            ShadowRoot shadowRoot => shadowRoot.Host,
            _ => null,
        };
        _namespacePrefixes = GetNamespacePrefixes(sheet.RuleList);
        AddRules(sheet.RuleList, _layers.Root, scope: null, containers: null, parentSelectors: null, ownerParent, baseUrl);
        _namespacePrefixes = null;
    }

    /// <summary>Returns the prefixes declared by the @namespace rules of a style sheet, or null when there is none.</summary>
    private static Dictionary<string, string>? GetNamespacePrefixes(List<CssRule> rules)
    {
        Dictionary<string, string>? result = null;
        foreach (var rule in rules)
        {
            if (rule is CssNamespaceRule { Prefix.Length: > 0 } namespaceRule)
            {
                (result ??= new(StringComparer.Ordinal))[namespaceRule.Prefix] = namespaceRule.NamespaceUri;
            }
        }

        return result;
    }

    public void AddRules(List<CssRule> rules, string? baseUrl) => AddRules(rules, _layers.Root, scope: null, containers: null, parentSelectors: null, ownerParent: null, baseUrl);

    public CascadeRuleSet Complete()
    {
        _layers.AssignRanks();
        foreach (var (rule, layer) in _layeredRules)
        {
            rule.Layer = layer.Rank;
        }

        // When several @property rules register the same name, the one in the last layer wins (the unlayered rules win
        // over the layered ones), then the last one (https://drafts.csswg.org/css-cascade-5/#layering)
        foreach (var (rule, layer) in _propertyRules.OrderBy(entry => entry.Layer.Rank))
        {
            PropertyRules[rule.Name] = rule;
        }

        // Likewise, the @function rule in the strongest layer wins, then the last one (https://drafts.csswg.org/css-mixins-1/#function-rule)
        foreach (var (rule, layer) in _functionRules.OrderBy(entry => entry.Layer.Rank))
        {
            FunctionRules[rule.Name] = rule;
        }

        // The @mixin rule in the strongest layer wins, then the last one (https://drafts.csswg.org/css-mixins-1/#mixin-rule)
        foreach (var (rule, layer) in _mixinRules.OrderBy(entry => entry.Layer.Rank))
        {
            MixinRules[rule.Name] = rule;
        }

        foreach (var (rule, layer) in _fontFaceRules)
        {
            FontFaceLayers[rule] = layer.Rank;
        }

        foreach (var (rule, _) in _fontFeatureValuesRules.OrderBy(entry => entry.Layer.Rank))
        {
            FontFeatureValuesRules.Add(rule);
        }

        foreach (var (rule, _) in _fontPaletteValuesRules.OrderBy(entry => entry.Layer.Rank))
        {
            FontPaletteValuesRules.Add(rule);
        }

        // The @keyframes rule in the strongest layer wins, then the last one (https://drafts.csswg.org/css-animations-1/#keyframes)
        foreach (var (rule, layer) in _keyframesRules.OrderBy(entry => entry.Layer.Rank))
        {
            KeyframesRules[rule.Name] = rule;
        }

        // https://drafts.csswg.org/css-view-transitions-2/#resolve-view-transition-rule: the last @view-transition rule, in the strongest layer
        foreach (var (rule, _) in _viewTransitionRules.OrderBy(entry => entry.Layer.Rank))
        {
            ViewTransitionRule = rule;
        }

        // "If multiple @position-try rules are declared with the same name, they cascade the same as @keyframes rules do"
        foreach (var (rule, layer) in _positionTryRules.OrderBy(entry => entry.Layer.Rank))
        {
            PositionTryRules[rule.PositionTryName] = rule;
        }

        // "If multiple @counter-style rules are defined with the same name, only one wins, according to standard cascade rules"
        // (the rules that do not define a counter style, e.g. an alphabetic system with a single symbol, are ignored)
        foreach (var (rule, layer) in _counterStyleRules.OrderBy(entry => entry.Layer.Rank))
        {
            if (CounterStyle.Create(rule, scope: null, predefined: true, static _ => CounterStyle.Decimal) is not null)
            {
                CounterStyleRules[CounterStyle.NormalizeName(rule.Name)] = rule;
            }
        }

        PageRules.Clear();
        foreach (var (rule, layer, baseUrl) in _pageRules)
        {
            PageRules.Add((rule, layer.Rank, baseUrl));
        }

        return _rules;
    }

    private void AddRules(List<CssRule> rules, CascadeLayer layer, CascadeScope? scope, CascadeContainerQuery? containers, SelectorList? parentSelectors, Element? ownerParent, string? baseUrl)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case CssStyleRule styleRule:
                {
                    var selectors = styleRule.Selectors;
                    foreach (var selector in selectors.Selectors)
                    {
                        BindNestingSelectors(selector, parentSelectors);
                    }

                    var declarations = styleRule.Style.Declarations;
                    if (declarations.Count > 0)
                    {
                        foreach (var selector in selectors.Selectors)
                        {
                            AddRule(selector, declarations, layer, scope, containers, baseUrl);
                        }
                    }

                    if (styleRule.ChildRules.Count > 0)
                    {
                        AddRules(styleRule.ChildRules, layer, scope, containers, selectors, ownerParent, baseUrl);
                    }

                    break;
                }

                case CssNestedDeclarations nested:
                {
                    // The declarations of a style rule that follow its nested rules (https://drafts.csswg.org/css-nesting/#nested-declarations-rule):
                    // they match like the parent style rule, with the specificity of its matching selectors (not the
                    // :is() specificity of &), including its pseudo-elements; :where(:scope) in a @scope rule
                    var declarations = nested.Style.Declarations;
                    if (declarations.Count > 0)
                    {
                        if (parentSelectors is not null)
                        {
                            foreach (var selector in parentSelectors.Selectors)
                            {
                                AddRule(selector, declarations, layer, scope, containers, baseUrl);
                            }
                        }
                        else
                        {
                            var nesting = new ComplexSelector([new CompoundSelector([new NestingSelector { Parent = parentSelectors }])], [], relativeCombinator: null);
                            AddRule(nesting, declarations, layer, scope, containers, baseUrl);
                        }
                    }

                    break;
                }

                case CssMediaRule mediaRule:
                    if (MediaQueryEvaluator.Matches(mediaRule.Media, _environment))
                    {
                        AddRules(mediaRule.ChildRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    }

                    break;

                case CssSupportsRule supportsRule:
                    if (supportsRule.Matches)
                    {
                        AddRules(supportsRule.ChildRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    }

                    break;

                case CssLayerBlockRule layerBlock:
                {
                    // The presentational hints "only if border is not equivalent to zero" are in a layer of the user agent
                    // style sheet (the layers of the presentational hints do not order their rules)
                    var onlyIfTableBorderIsNotZero = _onlyIfTableBorderIsNotZero;
                    _onlyIfTableBorderIsNotZero |= PresentationalHints && layerBlock.Name == HtmlUserAgentStyleSheet.NonZeroBorderLayer;
                    AddRules(layerBlock.ChildRules, layerBlock.Name.Length == 0 ? layer.AddAnonymous() : layer.GetOrAdd(layerBlock.Name), scope, containers, parentSelectors, ownerParent, baseUrl);
                    _onlyIfTableBorderIsNotZero = onlyIfTableBorderIsNotZero;
                    break;
                }

                case CssLayerStatementRule layerStatement:
                    foreach (var name in layerStatement.NameList)
                    {
                        layer.GetOrAdd(name);
                    }

                    break;

                case CssScopeRule scopeRule:
                {
                    if (scopeRule.StartSelectors is { } start)
                    {
                        foreach (var selector in start.Selectors)
                        {
                            BindNestingSelectors(selector, parentSelectors);
                        }
                    }

                    var implicitRoot = scopeRule.StartSelectors is null ? (Node?)ownerParent ?? (_treeRoot is ShadowRoot shadowRoot ? shadowRoot.Host : _treeRoot) : null;
                    var childScope = new CascadeScope { Start = scopeRule.StartSelectors, End = scopeRule.EndSelectors, ImplicitRoot = implicitRoot, Parent = scope };

                    // In the scoped rules, & represents the scoping root
                    AddRules(scopeRule.ChildRules, layer, childScope, containers, parentSelectors: null, ownerParent, baseUrl);
                    break;
                }

                // https://drafts.csswg.org/css-cascade-5/#at-import: the rules of the imported style sheet (if it was
                // fetched) replace the @import rule, in the layer of the rule, when its conditions match. A layered @import
                // declares its layer even when the style sheet could not be fetched.
                case CssImportRule importRule:
                    if (importRule.SupportsMatches && MediaQueryEvaluator.Matches(importRule.Media, _environment))
                    {
                        var importLayer = importRule.LayerName switch
                        {
                            null => layer,
                            "" => layer.AddAnonymous(),
                            var name => layer.GetOrAdd(name),
                        };

                        if (importRule.StyleSheet is { Disabled: false } importedSheet)
                        {
                            // scope() scopes the imported rules like a @scope rule (https://drafts.csswg.org/css-cascade-6/#at-import)
                            var importScope = scope;
                            var importParentSelectors = parentSelectors;
                            if (importRule.Scoped)
                            {
                                var implicitRoot = importRule.ScopeStartSelectors is null ? (Node?)ownerParent ?? (_treeRoot is ShadowRoot shadowRoot ? shadowRoot.Host : _treeRoot) : null;
                                importScope = new CascadeScope { Start = importRule.ScopeStartSelectors, End = importRule.ScopeEndSelectors, ImplicitRoot = implicitRoot, Parent = scope };
                                importParentSelectors = null;
                            }

                            var namespacePrefixes = _namespacePrefixes;
                            _namespacePrefixes = GetNamespacePrefixes(importedSheet.RuleList);
                            AddRules(importedSheet.RuleList, importLayer, importScope, containers, importParentSelectors, ownerParent, importedSheet.BaseUrl ?? baseUrl);
                            _namespacePrefixes = namespacePrefixes;
                        }
                    }

                    break;

                case CssContainerRule containerRule:
                    // The rules apply when the container query is true for the element (https://drafts.csswg.org/css-conditional-5/#container-rule)
                    AddRules(containerRule.ChildRules, layer, scope, new CascadeContainerQuery { Conditions = containerRule.ParsedConditions, Parent = containers }, parentSelectors, ownerParent, baseUrl);
                    break;

                case CssPropertyRule propertyRule:
                    _propertyRules.Add((propertyRule, layer));
                    break;

                case CssFunctionRule functionRule:
                    _functionRules.Add((functionRule, layer));
                    break;

                case CssMixinRule mixinRule:
                    _mixinRules.Add((mixinRule, layer));
                    break;

                case CssApplyStatementRule applyStatement:
                    Apply(applyStatement.Prelude, contents: null, applyStatement.ParentStyleSheet, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    break;

                case CssApplyBlockRule applyBlock:
                    Apply(applyBlock.Prelude, applyBlock.ChildRules, applyBlock.ParentStyleSheet, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    break;

                // https://drafts.csswg.org/css-mixins-1/#contents-rule: the contents block of the @apply rule (in the calling
                // context), or else the fallback block of the @contents rule
                case CssContentsStatementRule or CssContentsBlockRule when _currentMixin is { } application:
                    if (application.Contents is { } contents)
                    {
                        var contentsRules = new CssRuleParser(application.ContentsSheet).ParseNestedRules(SerializeRules(contents), inMixinResult: true);
                        _currentMixin = application.Parent;
                        try
                        {
                            AddRules(contentsRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                        }
                        finally
                        {
                            _currentMixin = application;
                        }
                    }
                    else if (rule is CssContentsBlockRule fallback)
                    {
                        AddRules(fallback.ChildRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    }

                    break;

                case CssFontFaceRule fontFaceRule:
                    _fontFaceRules.Add((fontFaceRule, layer));
                    break;

                case CssFontFeatureValuesRule fontFeatureValuesRule:
                    _fontFeatureValuesRules.Add((fontFeatureValuesRule, layer));
                    break;

                case CssFontPaletteValuesRule fontPaletteValuesRule:
                    _fontPaletteValuesRules.Add((fontPaletteValuesRule, layer));
                    break;

                case CssKeyframesRule keyframesRule:
                    _keyframesRules.Add((keyframesRule, layer));
                    break;

                case CssPositionTryRule positionTryRule:
                    _positionTryRules.Add((positionTryRule, layer));
                    break;

                case CssViewTransitionRule viewTransitionRule:
                    _viewTransitionRules.Add((viewTransitionRule, layer));
                    break;

                case CssCounterStyleRule counterStyleRule:
                    _counterStyleRules.Add((counterStyleRule, layer));
                    break;

                // https://drafts.csswg.org/css-transitions-2/#defining-before-change-style: the rules of @starting-style only
                // apply to the starting style of the elements
                case CssStartingStyleRule startingStyleRule:
                {
                    var previous = _inStartingStyle;
                    _inStartingStyle = true;
                    try
                    {
                        AddRules(startingStyleRule.ChildRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    }
                    finally
                    {
                        _inStartingStyle = previous;
                    }

                    break;
                }

                // The @page rules do not apply to the elements: they style the pages of the paged media (see StyleEngine.GetPageStyle)
                case CssPageRule pageRule:
                    _pageRules.Add((pageRule, layer, baseUrl));
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Applies a mixin (https://drafts.csswg.org/css-mixins-1/#apply-rule): the rules of its @result rules replace the @apply
    /// rule, in the rule that contains it. The @apply rule does nothing when there is no such mixin, when it has more arguments
    /// than the mixin has parameters, or when the mixin is already being applied (a cycle).
    /// </summary>
    private void Apply(CssApplyPrelude prelude, List<CssRule>? contents, CssStyleSheet? sheet, CascadeLayer layer, CascadeScope? scope, CascadeContainerQuery? containers, SelectorList? parentSelectors, Element? ownerParent, string? baseUrl)
    {
        HasApplyRules = true;
        if (MixinResolver?.Invoke(prelude.Name, _currentMixin?.DefinitionScope ?? _treeRoot) is not { } resolved)
            return;

        var (mixin, definitionScope) = resolved;
        if (prelude.Arguments.Count > mixin.Parameters.Count || _appliedMixins.Contains(mixin) || _appliedMixins.Count >= MaxMixinDepth)
            return;

        var application = new CssMixinApplication
        {
            Mixin = mixin,
            DefinitionScope = definitionScope,
            Arguments = prelude.Arguments,
            TreeScope = _treeRoot,
            Parent = _currentMixin,
            Contents = contents,
            ContentsSheet = sheet,
        };

        var previous = _currentMixin;
        _currentMixin = application;
        _appliedMixins.Add(mixin);
        try
        {
            AddMixinResults(mixin.ChildRules, layer, scope, containers, parentSelectors, ownerParent, mixin.ParentStyleSheet?.BaseUrl ?? baseUrl);
        }
        finally
        {
            _appliedMixins.RemoveAt(_appliedMixins.Count - 1);
            _currentMixin = previous;
        }
    }

    /// <summary>
    /// Adds the rules of the @result rules of a mixin body, whose conditional group rules apply when their condition is true
    /// (the container queries are evaluated for each element). The rules are parsed again for each application, as their
    /// nesting selectors refer to the rule the mixin is applied in.
    /// </summary>
    private void AddMixinResults(List<CssRule> rules, CascadeLayer layer, CascadeScope? scope, CascadeContainerQuery? containers, SelectorList? parentSelectors, Element? ownerParent, string? baseUrl)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case CssMixinResultRule result:
                {
                    var resultRules = new CssRuleParser(result.ParentStyleSheet).ParseNestedRules(SerializeRules(result.ChildRules), inMixinResult: true);
                    AddRules(resultRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    break;
                }

                case CssMediaRule mediaRule when MediaQueryEvaluator.Matches(mediaRule.Media, _environment):
                    AddMixinResults(mediaRule.ChildRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    break;

                case CssSupportsRule { Matches: true } supportsRule:
                    AddMixinResults(supportsRule.ChildRules, layer, scope, containers, parentSelectors, ownerParent, baseUrl);
                    break;

                case CssContainerRule containerRule:
                    AddMixinResults(containerRule.ChildRules, layer, scope, new CascadeContainerQuery { Conditions = containerRule.ParsedConditions, Parent = containers }, parentSelectors, ownerParent, baseUrl);
                    break;
            }
        }
    }

    private static string SerializeRules(List<CssRule> rules) => string.Join("\n", rules.Select(rule => rule.CssText));

    private void AddRule(ComplexSelector selector, List<CssDeclarationEntry> declarations, CascadeLayer layer, CascadeScope? scope, CascadeContainerQuery? containers, string? baseUrl)
    {
        var kind = CascadeRuleKind.Element;
        string? pseudoElement = null;
        string? pseudoElementArgument = null;
        var pseudoElementOnlyChild = false;
        var pseudoElementNotOnlyChild = false;
        SelectorList? slotted = null;
        ImmutableArray<string> partNames = [];
        foreach (var simple in selector.Compounds[^1].Selectors)
        {
            switch (simple)
            {
                case PseudoElementSelector { Name: "slotted" } slottedSelector:
                    if (kind is CascadeRuleKind.Slotted or CascadeRuleKind.Part || pseudoElement is not null)
                        return;

                    kind = CascadeRuleKind.Slotted;
                    slotted = slottedSelector.Argument;
                    break;

                case PseudoElementSelector { Name: "part" } part:
                    // The originating element can be the shadow host (:host::part()); a pseudo-element can follow ::part()
                    // (e.g. ::part(label)::before)
                    if (kind is CascadeRuleKind.Slotted or CascadeRuleKind.Part || pseudoElement is not null)
                        return;

                    kind = CascadeRuleKind.Part;
                    partNames = part.PartNames;
                    break;

                case PseudoElementSelector pseudo:
                    // https://drafts.csswg.org/css-multicol-2/#column-pseudo: the scroll markers of the columns
                    if (pseudoElement == Layout.ScrollControls.Column && pseudo.Name == Layout.ScrollControls.Marker)
                    {
                        pseudoElement = Layout.ScrollControls.ColumnMarkerRuleName;
                        break;
                    }

                    // The other sub-pseudo-elements (e.g. ::before::marker) are not supported, but the ones of the element-backed
                    // ::details-content (e.g. "details-content::before", see StyleEngine.DetailsContentPrefix); the
                    // pseudo-elements of the slotted elements and of the parts are (e.g. ::slotted(*)::before, ::part(label)::before)
                    if (pseudoElement == StyleEngine.DetailsContent && pseudo.Name is "before" or "after" or "first-line" or "first-letter")
                    {
                        pseudoElement = StyleEngine.DetailsContentPrefix + pseudo.Name;
                        break;
                    }

                    // https://drafts.csswg.org/css-pseudo-4/#marker-pseudo: the ::marker of a ::before or ::after that is a list item
                    // ("before::marker", see StyleEngine.IsMarker)
                    if (pseudoElement is "before" or "after" && pseudo.Name == "marker")
                    {
                        pseudoElement += "::marker";
                        break;
                    }

                    // The pseudo-elements of the element-backed ::picker(select) (e.g. "picker::before" with the argument of the
                    // picker, see StyleEngine.SelectPickerPrefix); the highlight pseudo-elements keep their own argument (e.g.
                    // "picker::highlight" with the name of the highlight)
                    if (pseudoElement == "picker" && pseudo.Name is "before" or "after" or "backdrop" or "first-line" or "first-letter")
                    {
                        pseudoElement = "picker::" + pseudo.Name;
                        break;
                    }

                    if (pseudoElement == "picker" && StyleEngine.IsHighlightPseudoElement(pseudo.Name))
                    {
                        pseudoElement = "picker::" + pseudo.Name;
                        pseudoElementArgument = pseudo.ArgumentText ?? pseudoElementArgument;
                        break;
                    }

                    if (pseudoElement is not null)
                        return;

                    pseudoElement = pseudo.Name;
                    pseudoElementArgument = pseudo.ArgumentText;
                    break;

                case PseudoClassSelector { Kind: PseudoClass.OnlyChild } when pseudoElement is not null && pseudoElement.StartsWith("view-transition", StringComparison.Ordinal):
                    pseudoElementOnlyChild = true;
                    break;

                case PseudoClassSelector negated when pseudoElement is not null && pseudoElement.StartsWith("view-transition", StringComparison.Ordinal) && SelectorParser.IsNegatedOnlyChild(negated):
                    pseudoElementNotOnlyChild = true;
                    break;

                case PseudoClassSelector { Kind: PseudoClass.Host or PseudoClass.HostFunction or PseudoClass.HostContext } when kind == CascadeRuleKind.Element:
                    kind = CascadeRuleKind.Host;
                    break;
            }
        }

        var hasImportant = false;
        var hasNormal = false;
        foreach (var declaration in declarations)
        {
            if (declaration.Important)
            {
                hasImportant = true;
            }
            else
            {
                hasNormal = true;
            }
        }

        var rule = new CascadeRule
        {
            Selector = selector,
            Declarations = declarations,
            Origin = _origin,
            Specificity = ZeroSpecificity ? default : selector.Specificity,
            Order = Interlocked.Increment(ref s_order),
            Scope = scope,
            Containers = containers,
            ScopeRelative = scope is not null && !ContainsScopeOrNesting(selector),
            Kind = kind,
            PseudoElement = pseudoElement,
            PseudoElementArgument = pseudoElementArgument,
            PseudoElementOnlyChild = pseudoElementOnlyChild,
            PseudoElementNotOnlyChild = pseudoElementNotOnlyChild,
            SlottedSelector = slotted,
            PartNames = partNames,
            BaseUrl = baseUrl,
            TreeScope = _treeRoot,
            Mixin = _currentMixin,
            NamespacePrefixes = _namespacePrefixes,
            HasImportant = hasImportant,
            HasNormal = hasNormal,
            StartingStyle = _inStartingStyle,
            OnlyIfTableBorderIsNotZero = _onlyIfTableBorderIsNotZero,
            SelectCondition = SelectCondition,
        };

        if (PresentationalHints)
        {
            rule.Layer = CascadeRule.PresentationalHints;
        }
        else if (layer != _layers.Root)
        {
            _layeredRules.Add((rule, layer));
        }

        _rules.Add(rule);

        // In a shadow tree, the scoping root can be the shadow host (e.g. @scope (:host)): a scoped rule whose subject is the
        // scoping root (:scope or &) can match the host, like the :host rules. So can a rule whose subject matches the host
        // through a logical combination or the nesting selector (:is(:host), :not(:not(:host)), :host { &::before { } })
        if (kind == CascadeRuleKind.Element && _treeRoot is ShadowRoot
            && ((scope is not null && pseudoElement is null && CompoundContainsScopeOrNesting(selector.Compounds[^1])) || CompoundMayMatchHost(selector.Compounds[^1])))
        {
            _rules.Add(new CascadeRule
            {
                Selector = rule.Selector,
                Declarations = rule.Declarations,
                Origin = rule.Origin,
                Specificity = rule.Specificity,
                Order = rule.Order,
                Scope = rule.Scope,
                Containers = rule.Containers,
                ScopeRelative = rule.ScopeRelative,
                Kind = CascadeRuleKind.Host,
                PseudoElement = rule.PseudoElement,
                PseudoElementArgument = rule.PseudoElementArgument,
                BaseUrl = rule.BaseUrl,
                TreeScope = rule.TreeScope,
                Mixin = rule.Mixin,
                NamespacePrefixes = rule.NamespacePrefixes,
                HasImportant = rule.HasImportant,
                HasNormal = rule.HasNormal,
                StartingStyle = rule.StartingStyle,
                OnlyIfTableBorderIsNotZero = rule.OnlyIfTableBorderIsNotZero,
                SelectCondition = rule.SelectCondition,
                Layer = rule.Layer,
            });
            if (layer != _layers.Root && !PresentationalHints)
            {
                _layeredRules.Add((_rules.HostRules[^1], layer));
            }
        }
    }

    /// <summary>
    /// Returns whether a compound selector can match the featureless shadow host through a logical combination or the
    /// nesting selector: :is(:host), :where(div, :host), :not(:not(:host)), &amp; in a rule nested in a :host rule.
    /// </summary>
    private static bool CompoundMayMatchHost(CompoundSelector compound)
    {
        foreach (var simple in compound.Selectors)
        {
            switch (simple)
            {
                case NestingSelector { Parent: { } parent } when parent.Selectors.Any(SubjectMayMatchHost):
                    return true;

                case PseudoClassSelector { Kind: PseudoClass.Is or PseudoClass.Where or PseudoClass.Not, Argument: { } argument } when argument.Selectors.Any(SubjectMayMatchHost):
                    return true;
            }
        }

        return false;

        static bool SubjectMayMatchHost(ComplexSelector selector)
        {
            var subject = selector.Compounds[^1];
            return subject.Selectors.Any(static simple => simple is PseudoClassSelector { Kind: PseudoClass.Host or PseudoClass.HostFunction or PseudoClass.HostContext }) || CompoundMayMatchHost(subject);
        }
    }

    /// <summary>Returns whether a compound selector contains :scope or the nesting selector (possibly in a nested selector or in the parent rule of &amp;).</summary>
    private static bool CompoundContainsScopeOrNesting(CompoundSelector compound)
    {
        foreach (var simple in compound.Selectors)
        {
            switch (simple)
            {
                case PseudoClassSelector { Kind: PseudoClass.Scope }:
                    return true;

                case NestingSelector nesting:
                    if (nesting.Parent is null || nesting.Parent.Selectors.Any(parent => CompoundContainsScopeOrNesting(parent.Compounds[^1])))
                        return true;

                    break;

                case PseudoClassSelector { Argument: { } argument } when argument.Selectors.Any(nested => CompoundContainsScopeOrNesting(nested.Compounds[^1])):
                    return true;
            }
        }

        return false;
    }

    /// <summary>Sets the parent of the nesting selectors of a selector (https://drafts.csswg.org/css-nesting/#nest-selector).</summary>
    private static void BindNestingSelectors(ComplexSelector selector, SelectorList? parent)
    {
        foreach (var compound in selector.Compounds)
        {
            foreach (var simple in compound.Selectors)
            {
                switch (simple)
                {
                    case NestingSelector nesting:
                        nesting.Parent = parent;
                        break;
                    case PseudoClassSelector { Argument: { } argument }:
                        foreach (var nested in argument.Selectors)
                        {
                            BindNestingSelectors(nested, parent);
                        }

                        break;
                }
            }
        }
    }

    private static bool ContainsScopeOrNesting(ComplexSelector selector)
    {
        foreach (var compound in selector.Compounds)
        {
            foreach (var simple in compound.Selectors)
            {
                switch (simple)
                {
                    case NestingSelector:
                    case PseudoClassSelector { Kind: PseudoClass.Scope }:
                        return true;
                    case PseudoClassSelector { Argument: { } argument }:
                        foreach (var nested in argument.Selectors)
                        {
                            if (ContainsScopeOrNesting(nested))
                                return true;
                        }

                        break;
                }
            }
        }

        return false;
    }
}
