using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The context in which a rule is parsed, which determines the valid rules.</summary>
internal enum CssRuleContext
{
    /// <summary>The top level of a style sheet.</summary>
    TopLevel,

    /// <summary>The block of a conditional group rule (@media, @supports...) that is not nested in a style rule.</summary>
    Grouping,

    /// <summary>The block of a style rule, or of a rule nested in a style rule (https://drafts.csswg.org/css-nesting/#nested-group-rules).</summary>
    Nested,

    /// <summary>The block of a @page rule (margin rules).</summary>
    Page,

    /// <summary>
    /// The body of a @function rule, or of a conditional group rule in it (https://drafts.csswg.org/css-mixins-1/#function-body):
    /// the descriptors and the conditional group rules.
    /// </summary>
    Function,

    /// <summary>
    /// The body of a @mixin rule, or of a conditional group rule in it (https://drafts.csswg.org/css-mixins-1/#mixin-body):
    /// the local variables, the @result rules and the conditional group rules.
    /// </summary>
    Mixin,
}

/// <summary>Creates the CSSOM rules from the rules of the CSS Syntax parser (https://drafts.csswg.org/cssom/#parse-a-css-rule).</summary>
internal sealed partial class CssRuleParser
{
    private static readonly string[] MarginRuleNames =
    [
        "top-left-corner", "top-left", "top-center", "top-right", "top-right-corner",
        "bottom-left-corner", "bottom-left", "bottom-center", "bottom-right", "bottom-right-corner",
        "left-top", "left-middle", "left-bottom", "right-top", "right-middle", "right-bottom",
    ];

    private readonly Dictionary<string, string> _namespacePrefixes = new(StringComparer.Ordinal);
    private string? _defaultNamespace;
    private SelectorNamespaceContext? _namespaceContext;

    /// <summary>The preprocessed source of the parsed text (the positions of the component values are relative to it).</summary>
    private string _source = "";

    public CssRuleParser(CssStyleSheet? sheet)
    {
        if (sheet is null)
            return;

        _quirks = sheet.IsQuirksMode;
        foreach (var rule in sheet.RuleList)
        {
            if (rule is CssNamespaceRule namespaceRule)
            {
                AddNamespace(namespaceRule);
            }
        }
    }

    public static bool IsMarginRuleName(string name) => MarginRuleNames.Contains(name);

    private void AddNamespace(CssNamespaceRule rule)
    {
        if (rule.Prefix.Length == 0)
        {
            _defaultNamespace = rule.NamespaceUri;
        }
        else
        {
            _namespacePrefixes[rule.Prefix] = rule.NamespaceUri;
        }

        _namespaceContext = null;
    }

    /// <summary>
    /// Whether CSS is parsed in quirks mode: the quirky lengths (https://drafts.csswg.org/css-values-4/#deprecated-quirky-length)
    /// and the quirky colors (https://drafts.csswg.org/css-color-4/#quirky-color) are valid in the declarations and in the
    /// supports conditions.
    /// </summary>
    private bool _quirks;

    /// <summary>Whether CSS is parsed in quirks mode (the style sheet of a document in quirks mode).</summary>
    public bool Quirks
    {
        init => _quirks = value;
    }

    /// <summary>Whether the rules being parsed are the scoped style rules of a @scope rule (https://drafts.csswg.org/css-cascade-6/#scoped-rules).</summary>
    private bool _inScopeRule;

    /// <summary>Whether the parsed rules are in a @scope rule: their style rules are scoped style rules.</summary>
    public bool InScopeRule
    {
        init => _inScopeRule = value;
    }

    /// <summary>Whether the rules being parsed are in a @result rule of a mixin: @contents is valid, @layer is not.</summary>
    private bool _inMixinResult;

    /// <summary>Whether the parsed rules are in a @result rule of a mixin.</summary>
    public bool InMixinResult
    {
        init => _inMixinResult = value;
    }

    private SelectorNamespaceContext Namespaces => _namespaceContext ??= _defaultNamespace is null && _namespacePrefixes.Count == 0
        ? SelectorNamespaceContext.Empty
        : new SelectorNamespaceContext
        {
            DefaultNamespace = _defaultNamespace,
            ResolvePrefix = prefix => _namespacePrefixes.GetValueOrDefault(prefix),
        };

    // ----------------------------------------------------------------------------------------------------------------
    // Entry points
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Parses the rules of a style sheet (https://drafts.csswg.org/cssom/#parse-a-css-stylesheet).</summary>
    public List<CssRule> ParseStylesheet(string text, bool allowImport = true)
    {
        var values = CssParser.ParseListOfComponentValues(text, out _source);
        var nodes = CssParser.ConsumeStylesheetContents(new CssComponentValueStream(values), reportErrors: false);
        var rules = new List<CssRule>();
        foreach (var node in nodes)
        {
            var rule = ConvertRule(node, CssRuleContext.TopLevel);
            if (rule is null)
                continue;

            // The @import and @namespace rules after other rules are ignored (https://drafts.csswg.org/css-cascade-5/#at-import)
            if (rule is CssImportRule && (!allowImport || !CanAppend(rules, rule)))
                continue;

            if (rule is CssNamespaceRule namespaceRule)
            {
                if (!CanAppend(rules, rule))
                    continue;

                AddNamespace(namespaceRule);
            }

            rules.Add(rule);
        }

        return rules;
    }

    /// <summary>Returns whether the rule can be appended at the end of the rules of a style sheet.</summary>
    private static bool CanAppend(List<CssRule> rules, CssRule rule)
    {
        var copy = new List<CssRule>(rules) { rule };
        return IsValidOrder(copy);
    }

    /// <summary>
    /// Returns whether the order of the rules is valid: the @import rules (and @layer statements) first, then the
    /// @namespace rules, then the other rules.
    /// </summary>
    private static bool IsValidOrder(List<CssRule> rules)
    {
        var state = 0;
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case CssImportRule:
                    if (state > 0)
                        return false;

                    break;
                case CssLayerStatementRule when state == 0:
                    break;
                case CssNamespaceRule:
                    if (state > 1)
                        return false;

                    state = 1;
                    break;
                default:
                    state = 2;
                    break;
            }
        }

        return true;
    }

    /// <summary>Parses a rule (https://drafts.csswg.org/css-syntax-3/#parse-a-rule) and keeps the source for the original text.</summary>
    private CssSyntaxNode ParseRuleText(string text, bool nested)
    {
        var values = CssParser.ParseListOfComponentValues(text, out _source);
        var stream = new CssComponentValueStream(values);
        stream.DiscardWhitespace();
        if (stream.IsEmpty)
            return new CssSyntaxError("empty");

        CssSyntaxNode? rule = stream.NextIs(CssTokenType.AtKeyword) ? CssParser.ConsumeAtRule(stream, nested) : CssParser.ConsumeQualifiedRule(stream, stopToken: null, nested);
        if (rule is null)
            return new CssSyntaxError("invalid");

        stream.DiscardWhitespace();
        return stream.IsEmpty ? rule : new CssSyntaxError("extra-input");
    }

    /// <summary>Parses a keyframe rule (https://drafts.csswg.org/css-animations/#dom-csskeyframesrule-appendrule).</summary>
    public CssKeyframeRule? ParseKeyframeText(string text) => ParseRuleText(text, nested: false) is CssQualifiedRule rule ? ParseKeyframe(rule) : null;

    /// <summary>https://drafts.csswg.org/cssom/#insert-a-css-rule</summary>
    public uint InsertRule(List<CssRule> list, string text, uint index, CssRuleContext context, CssRule? parentRule, CssStyleSheet? sheet, bool constructed = false)
    {
        // The index is validated before the rule (as browsers do)
        if (index > (uint)list.Count)
            throw DomException.IndexSize("The index is greater than the number of rules");

        var node = ParseRuleText(text, nested: context == CssRuleContext.Nested);
        var rule = node is CssSyntaxError ? null : ConvertRule(node, context);

        // Nested declarations (https://drafts.csswg.org/cssom/#insert-a-css-rule step 2)
        if (rule is null && node is not CssAtRule && context == CssRuleContext.Nested)
        {
            var declarations = new List<CssDeclarationEntry>();
            CssStyleDeclaration.ParseDeclarationBlock(text, declarations, atRule: null, _quirks);
            if (declarations.Count > 0)
            {
                rule = new CssNestedDeclarations(declarations);
            }
        }
        else if (rule is null && node is not CssAtRule && context == CssRuleContext.Mixin)
        {
            // The local variables of a mixin body are wrapped in nested declarations (https://drafts.csswg.org/css-mixins-1/#cssmixinrule)
            var declarations = new List<CssDeclarationEntry>();
            CssStyleDeclaration.ParseDeclarationBlock(text, declarations, atRule: null, _quirks);
            declarations.RemoveAll(d => d.Important);
            if (declarations.Count > 0)
            {
                rule = new CssNestedDeclarations(declarations);
            }
        }
        else if (rule is null && node is not CssAtRule && context == CssRuleContext.Function)
        {
            // The declarations of a @function rule (https://drafts.csswg.org/css-mixins-1/#cssfunctiondeclarations)
            var declarations = new List<CssDeclarationEntry>();
            CssStyleDeclaration.ParseDeclarationBlock(text, declarations, CssStyleDeclaration.FunctionContext);
            if (declarations.Count > 0)
            {
                rule = new CssFunctionDeclarations(declarations);
            }
        }

        if (rule is null)
        {
            // Rules that are valid at the top level but not in this context cannot be inserted
            if (node is CssAtRule atRule && context != CssRuleContext.TopLevel && (InfraAscii.ToAsciiLowercase(atRule.Name) is "import" or "namespace" || ConvertRule(node, CssRuleContext.TopLevel) is not null))
            {
                if (index > (uint)list.Count)
                    throw DomException.IndexSize("The index is greater than the number of rules");

                throw DomException.HierarchyRequest("The rule cannot be inserted in this context");
            }

            throw DomException.Syntax("The rule is invalid");
        }

        if (rule is CssImportRule && constructed)
            throw DomException.Syntax("@import rules are not allowed in constructed style sheets");

        if (index > (uint)list.Count)
            throw DomException.IndexSize("The index is greater than the number of rules");

        if (context != CssRuleContext.TopLevel && rule is CssImportRule or CssNamespaceRule)
            throw DomException.HierarchyRequest("The rule cannot be inserted in this context");

        if (context == CssRuleContext.TopLevel)
        {
            var copy = new List<CssRule>(list);
            copy.Insert((int)index, rule);
            if (!IsValidOrder(copy))
            {
                if (rule is CssNamespaceRule && list.Exists(r => r is not CssImportRule and not CssNamespaceRule))
                    throw DomException.InvalidState("@namespace rules cannot be inserted after other rules");

                throw DomException.HierarchyRequest("The rule cannot be inserted at this position");
            }

            if (rule is CssNamespaceRule && list.Exists(r => r is not CssImportRule and not CssNamespaceRule))
                throw DomException.InvalidState("@namespace rules cannot be inserted after other rules");
        }

        list.Insert((int)index, rule);
        rule.ParentRule = parentRule;
        rule.SetParentStyleSheet(sheet);
        CssStyleVersion.Increment(sheet);
        sheet?.RequestCssResourcesUpdate();
        return index;
    }

    /// <summary>https://drafts.csswg.org/cssom/#remove-a-css-rule</summary>
    public static void RemoveRule(List<CssRule> list, uint index, CssStyleSheet? sheet)
    {
        if (index >= (uint)list.Count)
            throw DomException.IndexSize("The index is greater than the number of rules");

        var rule = list[(int)index];
        if (rule is CssNamespaceRule && list.Exists(r => r is not CssImportRule and not CssNamespaceRule))
            throw DomException.InvalidState("@namespace rules cannot be removed when there are other rules");

        list.RemoveAt((int)index);
        rule.ParentRule = null;
        rule.SetParentStyleSheet(null);

        // The style sheet of a removed @import rule is no longer imported by the style sheet, as in browsers
        if (rule is CssImportRule { StyleSheet: { } importedSheet })
        {
            importedSheet.ParentStyleSheet = null;
        }

        CssStyleVersion.Increment(sheet);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Rules
    // ----------------------------------------------------------------------------------------------------------------

    private CssRule? ConvertRule(CssSyntaxNode node, CssRuleContext context) => node switch
    {
        CssQualifiedRule qualified => context is CssRuleContext.Page or CssRuleContext.Function or CssRuleContext.Mixin ? null : ParseStyleRule(qualified, nested: context == CssRuleContext.Nested),
        CssAtRule atRule => ParseAtRule(atRule, context),
        _ => null,
    };

    private CssStyleRule? ParseStyleRule(CssQualifiedRule rule, bool nested)
    {
        var scoped = _inScopeRule;
        var selectors = ParseStyleRuleSelector(rule.Prelude, nested, scoped);
        if (selectors is null)
            return null;

        var declarations = new List<CssDeclarationEntry>();
        var styleRule = new CssStyleRule(selectors, declarations);

        // The rules nested in a style rule are relative to the style rule, not to the scoping root
        _inScopeRule = false;
        try
        {
            ParseNestedBlock(rule.Block.Value, styleRule, declarations);
        }
        finally
        {
            _inScopeRule = scoped;
        }

        return styleRule;
    }

    /// <summary>Parses the selector of a style rule; the nested style rules have relative selectors. The source is the preprocessed text of the prelude (the text of the style sheet by default).</summary>
    public SelectorList? ParseStyleRuleSelector(List<CssComponentValue> prelude, bool nested, bool scoped = false, string? source = null)
    {
        // An empty selector or a selector containing a {} block is invalid
        if (ValueMatcher.RemoveWhitespace(prelude).Count == 0)
            return null;

        if (!nested)
            return SelectorParser.ParseSelector(prelude, Namespaces, source ?? _source);

        return scoped ? SelectorParser.ParseScopedSelector(prelude, Namespaces, source ?? _source) : SelectorParser.ParseNestedSelector(prelude, Namespaces, source ?? _source);
    }

    /// <summary>
    /// Parses the contents of a block that accepts declarations and nested rules: the declarations before the first rule
    /// are the declarations of the rule, the next ones are CSSNestedDeclarations rules.
    /// </summary>
    private void ParseNestedBlock(List<CssComponentValue> block, CssGroupingRule parent, List<CssDeclarationEntry>? declarations)
    {
        var nodes = CssParser.ConsumeBlockContents(new CssComponentValueStream(block));
        List<CssDeclarationEntry>? nested = null;
        foreach (var node in nodes)
        {
            if (node is CssDeclaration declaration)
            {
                if (declarations is not null && parent.ChildRules.Count == 0)
                {
                    CssStyleDeclaration.AppendDeclaration(declaration, _source, declarations, atRule: null, _quirks);
                }
                else
                {
                    if (nested is null)
                    {
                        nested = [];
                        parent.AddChildRule(new CssNestedDeclarations(nested));
                    }

                    CssStyleDeclaration.AppendDeclaration(declaration, _source, nested, atRule: null, _quirks);
                }

                continue;
            }

            if (ConvertRule(node, CssRuleContext.Nested) is { } child)
            {
                // A CSSNestedDeclarations rule is created for the declarations after this rule
                nested = null;
                parent.AddChildRule(child);
            }
        }

        // Remove the empty nested declarations (e.g. only invalid declarations)
        parent.ChildRules.RemoveAll(r => r is CssNestedDeclarations { Style.Length: 0 });
    }

    /// <summary>
    /// Parses the body of a @function rule, or of a conditional group rule in it: the runs of consecutive declarations are
    /// CSSFunctionDeclarations rules (https://drafts.csswg.org/css-mixins-1/#cssfunctiondeclarations).
    /// </summary>
    private void ParseFunctionBlock(List<CssComponentValue> block, CssGroupingRule parent)
    {
        List<CssDeclarationEntry>? declarations = null;
        foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(block)))
        {
            if (node is CssDeclaration declaration)
            {
                if (declarations is null)
                {
                    declarations = [];
                    parent.AddChildRule(new CssFunctionDeclarations(declarations));
                }

                CssStyleDeclaration.AppendDeclaration(declaration, _source, declarations, CssStyleDeclaration.FunctionContext);
                continue;
            }

            if (ConvertRule(node, CssRuleContext.Function) is { } child)
            {
                declarations = null;
                parent.AddChildRule(child);
            }
        }

        parent.ChildRules.RemoveAll(r => r is CssFunctionDeclarations { Style.Length: 0 });
    }

    /// <summary>
    /// Parses the body of a @mixin rule, or of a conditional group rule in it: the runs of consecutive declarations (the local
    /// variables) are CSSNestedDeclarations rules (https://drafts.csswg.org/css-mixins-1/#cssmixinrule). The important
    /// declarations are invalid.
    /// </summary>
    private void ParseMixinBlock(List<CssComponentValue> block, CssGroupingRule parent)
    {
        List<CssDeclarationEntry>? declarations = null;
        foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(block)))
        {
            if (node is CssDeclaration declaration)
            {
                if (declaration.Important)
                    continue;

                if (declarations is null)
                {
                    declarations = [];
                    parent.AddChildRule(new CssNestedDeclarations(declarations));
                }

                CssStyleDeclaration.AppendDeclaration(declaration, _source, declarations, atRule: null, _quirks);
                continue;
            }

            if (ConvertRule(node, CssRuleContext.Mixin) is { } child)
            {
                declarations = null;
                parent.AddChildRule(child);
            }
        }

        parent.ChildRules.RemoveAll(r => r is CssNestedDeclarations { Style.Length: 0 });
    }

    /// <summary>Parses the contents of a block in the context of a @result rule of a mixin (where @contents is valid and @layer is not).</summary>
    private void ParseMixinResultBlock(List<CssComponentValue> block, CssGroupingRule parent)
    {
        var inMixinResult = _inMixinResult;
        var inScopeRule = _inScopeRule;
        _inMixinResult = true;
        _inScopeRule = false;
        try
        {
            ParseNestedBlock(block, parent, declarations: null);
        }
        finally
        {
            _inMixinResult = inMixinResult;
            _inScopeRule = inScopeRule;
        }
    }

    /// <summary>
    /// Parses rules nested in a style rule, from their serialization: the rules of a @result rule (or of the contents block of
    /// an @apply rule) are parsed again for each application of the mixin, as their nesting selectors refer to the rule they
    /// are applied in.
    /// </summary>
    public List<CssRule> ParseNestedRules(string text, bool inMixinResult)
    {
        var values = CssParser.ParseListOfComponentValues(text, out _source);
        var container = new CssMixinResultRule();
        var previous = _inMixinResult;
        _inMixinResult = inMixinResult;
        try
        {
            ParseNestedBlock(values, container, declarations: null);
        }
        finally
        {
            _inMixinResult = previous;
        }

        var rules = new List<CssRule>(container.ChildRules);
        foreach (var rule in rules)
        {
            rule.ParentRule = null;
        }

        return rules;
    }

    /// <summary>Parses the child rules of a grouping rule.</summary>
    private void ParseChildRules(CssSimpleBlock block, CssGroupingRule parent, CssRuleContext context)
    {
        if (context == CssRuleContext.Nested)
        {
            ParseNestedBlock(block.Value, parent, declarations: null);
            return;
        }

        if (context == CssRuleContext.Function)
        {
            ParseFunctionBlock(block.Value, parent);
            return;
        }

        if (context == CssRuleContext.Mixin)
        {
            ParseMixinBlock(block.Value, parent);
            return;
        }

        var nodes = CssParser.ConsumeStylesheetContents(new CssComponentValueStream(block.Value), reportErrors: false, topLevel: false);
        foreach (var node in nodes)
        {
            if (ConvertRule(node, CssRuleContext.Grouping) is { } child)
            {
                parent.AddChildRule(child);
            }
        }
    }

    private List<CssDeclarationEntry> ParseDescriptors(CssSimpleBlock block, string atRule)
    {
        var declarations = new List<CssDeclarationEntry>();
        foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(block.Value)))
        {
            if (node is CssDeclaration declaration)
            {
                CssStyleDeclaration.AppendDeclaration(declaration, _source, declarations, atRule);
            }
        }

        return declarations;
    }

    private CssRule? ParseAtRule(CssAtRule rule, CssRuleContext context)
    {
        var name = InfraAscii.ToAsciiLowercase(rule.Name);
        var prelude = ValueMatcher.RemoveWhitespace(rule.Prelude);
        var nested = context == CssRuleContext.Nested;

        // The context of the child rules of a conditional group rule
        var childContext = context switch
        {
            CssRuleContext.Nested or CssRuleContext.Function or CssRuleContext.Mixin => context,
            _ => CssRuleContext.Grouping,
        };

        // The body of a mixin contains the conditional group rules and the @result rules
        if (context == CssRuleContext.Mixin && name is not ("media" or "supports" or "container" or "result"))
            return null;

        switch (name)
        {
            case "import" when context == CssRuleContext.TopLevel && rule.Block is null:
                return ParseImport(rule.Prelude);

            case "namespace" when context == CssRuleContext.TopLevel && rule.Block is null:
                return ParseNamespace(prelude);

            case "custom-media" when rule.Block is null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
                return ParseCustomMedia(rule.Prelude);

            case "media" when rule.Block is not null && context != CssRuleContext.Page:
            {
                var mediaRule = new CssMediaRule(new MediaList(rule.Prelude));
                ParseChildRules(rule.Block, mediaRule, childContext);
                return mediaRule;
            }

            case "supports" when rule.Block is not null && context != CssRuleContext.Page:
            {
                var matches = EvaluateSupportsCondition(prelude, 0, out var end, _quirks, Namespaces);
                if (matches is null || end != prelude.Count)
                    return null;

                var supportsRule = new CssSupportsRule(NormalizeSupportsConditionText(GetOriginalText(rule.Prelude)), matches.Value);
                ParseChildRules(rule.Block, supportsRule, childContext);
                return supportsRule;
            }

            case "container" when rule.Block is not null && context != CssRuleContext.Page:
            {
                var conditions = ParseContainerConditions(rule.Prelude);
                if (conditions is null)
                    return null;

                var containerRule = new CssContainerRule(conditions);
                ParseChildRules(rule.Block, containerRule, childContext);
                return containerRule;
            }

            // @layer is invalid in a mixin (https://github.com/w3c/csswg-drafts/issues/12417)
            case "layer" when context is not (CssRuleContext.Page or CssRuleContext.Function) && !_inMixinResult:
            {
                var names = ParseLayerNames(rule.Prelude);
                if (names is null)
                    return null;

                // The @layer statements are not nested group rules: they are invalid in a style rule (but valid in @scope)
                // (https://drafts.csswg.org/css-nesting/#nested-group-rules)
                if (rule.Block is null)
                    return names.Count == 0 || (context == CssRuleContext.Nested && !_inScopeRule) ? null : new CssLayerStatementRule(names);

                if (names.Count > 1)
                    return null;

                var layerRule = new CssLayerBlockRule(names.Count == 0 ? "" : names[0]);
                ParseChildRules(rule.Block, layerRule, nested ? CssRuleContext.Nested : CssRuleContext.Grouping);
                return layerRule;
            }

            case "scope" when rule.Block is not null && context is not (CssRuleContext.Page or CssRuleContext.Function):
            {
                if (!TryParseScopePrelude(rule.Prelude, nested, out var start, out var scopeEnd, out var startSelectors, out var endSelectors))
                    return null;

                var scopeRule = new CssScopeRule(start, scopeEnd) { StartSelectors = startSelectors, EndSelectors = endSelectors };
                var inScopeRule = _inScopeRule;
                _inScopeRule = true;
                try
                {
                    ParseChildRules(rule.Block, scopeRule, CssRuleContext.Nested);
                }
                finally
                {
                    _inScopeRule = inScopeRule;
                }

                return scopeRule;
            }

            case "starting-style" when rule.Block is not null && prelude.Count == 0 && context is not (CssRuleContext.Page or CssRuleContext.Function):
            {
                var startingStyleRule = new CssStartingStyleRule();
                ParseChildRules(rule.Block, startingStyleRule, nested ? CssRuleContext.Nested : CssRuleContext.Grouping);
                return startingStyleRule;
            }

            case "font-face" when rule.Block is not null && prelude.Count == 0 && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
                return new CssFontFaceRule(ParseDescriptors(rule.Block, "@font-face"));

            case "keyframes" or "-webkit-keyframes" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
            {
                var keyframesName = ParseKeyframesName(prelude);
                if (keyframesName is null)
                    return null;

                var keyframesRule = new CssKeyframesRule(keyframesName);
                foreach (var node in CssParser.ConsumeStylesheetContents(new CssComponentValueStream(rule.Block.Value), reportErrors: false, topLevel: false))
                {
                    if (node is CssQualifiedRule qualified && ParseKeyframe(qualified) is { } keyframe)
                    {
                        keyframesRule.AddKeyframe(keyframe);
                    }
                }

                return keyframesRule;
            }

            case "page" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
            {
                var selector = ParsePageSelector(rule.Prelude);
                if (selector is null)
                    return null;

                var declarations = new List<CssDeclarationEntry>();
                var pageRule = new CssPageRule(selector, declarations);
                foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(rule.Block.Value)))
                {
                    if (node is CssDeclaration declaration)
                    {
                        CssStyleDeclaration.AppendDeclaration(declaration, _source, declarations, "@page");
                    }
                    else if (node is CssAtRule { Block: not null } marginRule && IsMarginRuleName(InfraAscii.ToAsciiLowercase(marginRule.Name)) && ValueMatcher.RemoveWhitespace(marginRule.Prelude).Count == 0)
                    {
                        var marginName = InfraAscii.ToAsciiLowercase(marginRule.Name);
                        pageRule.AddChildRule(new CssMarginRule(marginName, ParseDescriptors(marginRule.Block, "@" + marginName)));
                    }
                }

                return pageRule;
            }

            case var margin when context == CssRuleContext.Page && rule.Block is not null && IsMarginRuleName(margin) && prelude.Count == 0:
                return new CssMarginRule(margin, ParseDescriptors(rule.Block, "@" + margin));

            case "property" when rule.Block is not null && (context is CssRuleContext.TopLevel or CssRuleContext.Grouping || (context == CssRuleContext.Nested && _inScopeRule)):
                return ParsePropertyRule(prelude, rule.Block);

            case "view-transition" when rule.Block is not null && prelude.Count == 0 && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
                return ParseViewTransitionRule(rule.Block);

            case "counter-style" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
            {
                var counterName = ParseCounterStyleName(rule.Prelude, AllowsReservedCounterStyleNames);
                if (counterName is null)
                    return null;

                // The component values of the descriptors are kept: a symbol that is an identifier written with escapes may
                // not be an identifier once serialized (e.g. \2022, which is not an ident code point)
                var descriptors = new Dictionary<string, string>(StringComparer.Ordinal);
                var descriptorValues = new Dictionary<string, List<CssComponentValue>>(StringComparer.Ordinal);
                var entries = new List<CssDeclarationEntry>();
                foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(rule.Block.Value)))
                {
                    if (node is not CssDeclaration declaration)
                        continue;

                    entries.Clear();
                    CssStyleDeclaration.AppendDeclaration(declaration, _source, entries, "@counter-style");
                    foreach (var entry in entries)
                    {
                        descriptors[entry.Name] = entry.Value.Value;
                        descriptorValues[entry.Name] = declaration.Value;
                    }
                }

                return new CssCounterStyleRule(counterName, descriptors, descriptorValues);
            }

            case "function" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
            {
                var functionRule = ParseFunctionPrelude(rule.Prelude);
                if (functionRule is null)
                    return null;

                ParseChildRules(rule.Block, functionRule, CssRuleContext.Function);
                return functionRule;
            }

            case "mixin" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
            {
                var mixinRule = ParseMixinPrelude(rule.Prelude);
                if (mixinRule is null)
                    return null;

                ParseChildRules(rule.Block, mixinRule, CssRuleContext.Mixin);
                return mixinRule;
            }

            case "result" when rule.Block is not null && prelude.Count == 0 && context == CssRuleContext.Mixin:
            {
                var resultRule = new CssMixinResultRule();
                ParseMixinResultBlock(rule.Block.Value, resultRule);
                return resultRule;
            }

            // @apply is valid in the body of a style rule or of a nested group rule (https://drafts.csswg.org/css-mixins-1/#apply-rule)
            case "apply" when context == CssRuleContext.Nested:
            {
                if (ParseApplyPrelude(rule.Prelude) is not { } applyPrelude)
                    return null;

                if (rule.Block is null)
                    return new CssApplyStatementRule(applyPrelude);

                var applyRule = new CssApplyBlockRule(applyPrelude);
                ParseNestedBlock(rule.Block.Value, applyRule, declarations: null);
                return applyRule;
            }

            // @contents is only valid in a mixin (https://drafts.csswg.org/css-mixins-1/#contents-rule)
            case "contents" when context == CssRuleContext.Nested && _inMixinResult && prelude.Count == 0:
            {
                if (rule.Block is null)
                    return new CssContentsStatementRule();

                var contentsRule = new CssContentsBlockRule();
                ParseNestedBlock(rule.Block.Value, contentsRule, declarations: null);
                return contentsRule;
            }

            // @private is only valid in a style rule or a nested group rule (https://drafts.csswg.org/css-mixins-1/#private)
            case "private" when rule.Block is not null && prelude.Count == 0 && context == CssRuleContext.Nested:
                return new CssPrivateRule(ParsePrivateDeclarations(rule.Block.Value));

            case "font-feature-values" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
                return ParseFontFeatureValuesRule(rule.Prelude, rule.Block);

            // https://drafts.csswg.org/css-anchor-position-1/#fallback-rule: @position-try <dashed-ident> { <declaration-list> }; the
            // other contents of the block (rules, at-rules) are ignored
            case "position-try" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
            {
                if (prelude is not [CssPreservedToken { Type: CssTokenType.Ident } positionTryName] || !positionTryName.Token.Value.StartsWith("--", StringComparison.Ordinal))
                    return null;

                var declarations = new List<CssDeclarationEntry>();
                foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(rule.Block.Value)))
                {
                    if (node is CssDeclaration declaration)
                    {
                        CssStyleDeclaration.AppendDeclaration(declaration, _source, declarations, CssStyleDeclaration.PositionTryContext);
                    }
                }

                return new CssPositionTryRule(positionTryName.Token.Value, declarations);
            }

            case "font-palette-values" when rule.Block is not null && context is CssRuleContext.TopLevel or CssRuleContext.Grouping:
            {
                if (prelude is not [CssPreservedToken { Type: CssTokenType.Ident } paletteName] || !paletteName.Token.Value.StartsWith("--", StringComparison.Ordinal))
                    return null;

                return new CssFontPaletteValuesRule(paletteName.Token.Value, ParseDescriptors(rule.Block, "@font-palette-values").ToDictionary(d => d.Name, d => d.Value.Value, StringComparer.Ordinal));
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#font-feature-values: the feature value blocks contain the declarations of the
    /// feature value names, whose value is one or more non-negative integers (the declarations whose number of values is not
    /// valid for the type of the block are ignored). The blocks of the same type are merged.
    /// </summary>
    private CssFontFeatureValuesRule? ParseFontFeatureValuesRule(List<CssComponentValue> prelude, CssSimpleBlock block)
    {
        if (CssFontFeatureValuesRule.ParseFamilyNames(prelude) is not { } families)
            return null;

        var descriptors = new List<CssDeclarationEntry>();
        var rule = new CssFontFeatureValuesRule(families, descriptors);
        foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(block.Value)))
        {
            if (node is CssDeclaration declaration)
            {
                CssStyleDeclaration.AppendDeclaration(declaration, _source, descriptors, "@font-feature-values");
            }
            else if (node is CssAtRule { Block: { } featureBlock } atRule && ValueMatcher.RemoveWhitespace(atRule.Prelude).Count == 0 && CssFontFeatureValuesRule.GetFeatureType(atRule.Name) is { } featureType)
            {
                var map = rule.GetMapForBlock(featureType);
                foreach (var item in CssParser.ConsumeBlockContents(new CssComponentValueStream(featureBlock.Value)))
                {
                    if (item is not CssDeclaration { Important: false } featureValue)
                        continue;

                    var values = new List<uint>();
                    foreach (var component in ValueMatcher.RemoveWhitespace(featureValue.Value))
                    {
                        if (component is not CssPreservedToken { Type: CssTokenType.Number, Token: { IsInteger: true, NumericValue: >= 0 and <= uint.MaxValue } number })
                        {
                            values = null;
                            break;
                        }

                        values.Add((uint)number.NumericValue);
                    }

                    if (values is not null && CssFontFeatureValuesMap.IsValidCount(featureType, values.Count))
                    {
                        map.SetValue(featureValue.Name, [.. values]);
                    }
                }
            }
        }

        return rule;
    }

    /// <summary>Returns the original text of component values, trimmed, with the whitespace collapsed.</summary>
    private string GetOriginalText(List<CssComponentValue> values)
    {
        var start = values.FindIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        var end = values.FindLastIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        if (start < 0)
            return "";

        var from = values[start].Start;
        var to = values[end].End;
        if (from < 0 || to > _source.Length || from > to)
            return CssSerializer.SerializeValue(values, property: null);

        var builder = new StringBuilder(to - from);
        var previousWhitespace = false;
        foreach (var c in _source.AsSpan(from, to - from))
        {
            if (c is ' ' or '\t' or '\n')
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }

                previousWhitespace = true;
            }
            else
            {
                builder.Append(c);
                previousWhitespace = false;
            }
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/css-cascade-5/#at-import</summary>
    private CssImportRule? ParseImport(List<CssComponentValue> prelude)
    {
        var values = ValueMatcher.RemoveWhitespace(prelude);
        if (values.Count == 0)
            return null;

        // The url() can have request URL modifiers (https://drafts.csswg.org/css-values-5/#request-url-modifiers)
        string? modifiers = null;
        string? href = values[0] switch
        {
            CssPreservedToken { Type: CssTokenType.Url or CssTokenType.String } token => token.Token.Value,
            CssFunction { Name: var functionName } function when InfraAscii.EqualsIgnoreAsciiCase(functionName, "url") && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } url, .. var urlModifiers]
                && (modifiers = CssUrlModifiers.Canonicalize(urlModifiers)) is not null => url.Token.Value,
            _ => null,
        };

        if (href is null)
            return null;

        var index = 1;
        string? layerName = null;
        if (index < values.Count && values[index] is CssPreservedToken { Type: CssTokenType.Ident } layerKeyword && layerKeyword.Token.IsIdent("layer"))
        {
            layerName = "";
            index++;
        }
        else if (index < values.Count && values[index] is CssFunction { Name: var layerFunction } layer && InfraAscii.EqualsIgnoreAsciiCase(layerFunction, "layer"))
        {
            // An invalid layer() is part of the media query list (a <general-enclosed>, https://drafts.csswg.org/css-cascade-5/#at-import)
            if (ParseLayerNames(layer.Value) is [var layerValue])
            {
                layerName = layerValue;
                index++;
            }
        }

        // https://drafts.csswg.org/css-cascade-6/#at-import: scope() scopes the rules of the imported style sheet, like @scope
        // (an invalid scope() is part of the media query list)
        var scoped = false;
        string? scopeStart = null;
        string? scopeEnd = null;
        SelectorList? scopeStartSelectors = null;
        SelectorList? scopeEndSelectors = null;
        if (index < values.Count && values[index] is CssFunction { Name: var scopeFunction } scope && InfraAscii.EqualsIgnoreAsciiCase(scopeFunction, "scope")
            && TryParseScopePrelude(scope.Value, nested: false, out scopeStart, out scopeEnd, out scopeStartSelectors, out scopeEndSelectors))
        {
            scoped = true;
            index++;
        }

        string? supportsText = null;
        var supportsMatches = true;
        if (index < values.Count && values[index] is CssFunction { Name: var supportsFunction } supports && InfraAscii.EqualsIgnoreAsciiCase(supportsFunction, "supports"))
        {
            var condition = ValueMatcher.RemoveWhitespace(supports.Value);
            if (EvaluateSupportsCondition(condition, 0, out var end, _quirks, Namespaces) is { } matches && end == condition.Count)
            {
                supportsMatches = matches;
            }
            else
            {
                // supports(<declaration>)
                var stream = new CssComponentValueStream(supports.Value);
                stream.DiscardWhitespace();
                if (CssParser.ConsumeDeclaration(stream, nested: false) is not { } declaration)
                    return null;

                stream.DiscardWhitespace();
                if (!stream.IsEmpty)
                    return null;

                supportsMatches = IsSupportedDeclaration(declaration.Name, declaration.Value, _quirks);
            }

            supportsText = GetOriginalText(supports.Value);
            index++;
        }

        // The rest is a media query list
        var mediaStart = index < values.Count ? prelude.IndexOf(values[index]) : prelude.Count;
        var media = new MediaList(prelude[mediaStart..]);
        return new CssImportRule(href, media, layerName, supportsText, supportsMatches)
        {
            UrlModifiers = modifiers is { Length: > 0 } ? modifiers : null,
            Scoped = scoped,
            ScopeStart = scopeStart,
            ScopeEnd = scopeEnd,
            ScopeStartSelectors = scopeStartSelectors,
            ScopeEndSelectors = scopeEndSelectors,
        };
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#custom-mq: @custom-media &lt;extension-name&gt; [ &lt;media-query-list&gt; | true | false ]</summary>
    private static CssCustomMediaRule? ParseCustomMedia(List<CssComponentValue> prelude)
    {
        var start = prelude.FindIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        if (start < 0 || prelude[start] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken || !nameToken.Token.Value.StartsWith("--", StringComparison.Ordinal))
            return null;

        var query = prelude[(start + 1)..];
        if (ValueMatcher.RemoveWhitespace(query) is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && InfraAscii.ToAsciiLowercase(keyword.Token.Value) is "true" or "false")
            return new CssCustomMediaRule(nameToken.Token.Value, InfraAscii.EqualsIgnoreAsciiCase(keyword.Token.Value, "true"));

        // The "true" and "false" keywords can only be the whole query
        foreach (var value in query)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.ToAsciiLowercase(ident.Token.Value) is "true" or "false")
                return null;
        }

        return new CssCustomMediaRule(nameToken.Token.Value, new MediaList(query));
    }

    /// <summary>https://drafts.csswg.org/css-namespaces/#syntax</summary>
    private static CssNamespaceRule? ParseNamespace(List<CssComponentValue> prelude)
    {
        var prefix = "";
        var index = 0;
        if (prelude.Count == 2 && prelude[0] is CssPreservedToken { Type: CssTokenType.Ident } prefixToken)
        {
            prefix = prefixToken.Token.Value;
            index = 1;
        }

        if (prelude.Count != index + 1)
            return null;

        string? uri = prelude[index] switch
        {
            CssPreservedToken { Type: CssTokenType.Url or CssTokenType.String } token => token.Token.Value,
            CssFunction { Name: var functionName } function when InfraAscii.EqualsIgnoreAsciiCase(functionName, "url") && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } url] => url.Token.Value,
            _ => null,
        };

        return uri is null ? null : new CssNamespaceRule(prefix, uri);
    }

    /// <summary>https://drafts.csswg.org/css-cascade-5/#typedef-layer-name: a comma-separated list of layer names.</summary>
    private static List<string>? ParseLayerNames(List<CssComponentValue> values)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var expectIdent = true;
        var sawWhitespaceInName = false;
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                    if (current.Length > 0)
                    {
                        sawWhitespaceInName = true;
                    }

                    break;
                case CssPreservedToken { Type: CssTokenType.Ident } ident when expectIdent && !sawWhitespaceInName:
                    if (ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(ident.Token.Value)))
                        return null;

                    current.Append(CssSerializer.SerializeIdentifier(ident.Token.Value));
                    expectIdent = false;
                    break;
                case CssPreservedToken dot when dot.Token.IsDelim('.') && !expectIdent && !sawWhitespaceInName:
                    current.Append('.');
                    expectIdent = true;
                    break;
                case CssPreservedToken { Type: CssTokenType.Comma } when !expectIdent:
                    result.Add(current.ToString());
                    current.Clear();
                    expectIdent = true;
                    sawWhitespaceInName = false;
                    break;
                default:
                    return null;
            }
        }

        if (current.Length > 0)
        {
            if (expectIdent)
                return null;

            result.Add(current.ToString());
        }
        else if (result.Count > 0)
        {
            return null; // Trailing comma
        }

        return result;
    }

    /// <summary>https://drafts.csswg.org/css-cascade-6/#scope-syntax: [ ( &lt;scope-start&gt; ) ]? [ to ( &lt;scope-end&gt; ) ]?</summary>
    private bool TryParseScopePrelude(List<CssComponentValue> prelude, bool nested, out string? start, out string? end, out SelectorList? startSelectors, out SelectorList? endSelectors)
    {
        start = null;
        end = null;
        startSelectors = null;
        endSelectors = null;
        var values = ValueMatcher.RemoveWhitespace(prelude);
        var index = 0;
        if (index < values.Count && values[index] is CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } startBlock)
        {
            // The <scope-start> and <scope-end> selectors cannot contain pseudo-elements
            // In a style rule or in a @scope rule, <scope-start> is relative to the parent style rule or to the scoping root
            var selectors = nested ? SelectorParser.ParseNestedScopeStart(startBlock.Value, scoped: _inScopeRule, Namespaces, _source) : SelectorParser.ParseSelector(startBlock.Value, Namespaces, _source);
            if (selectors is null || ContainsPseudoElement(selectors))
                return false;

            start = SelectorSerializer.Serialize(selectors);
            startSelectors = selectors;
            index++;
        }

        if (index < values.Count && values[index] is CssPreservedToken { Type: CssTokenType.Ident } to && to.Token.IsIdent("to"))
        {
            if (index + 1 >= values.Count || values[index + 1] is not CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } endBlock)
                return false;

            var selectors = SelectorParser.ParseScopedSelector(endBlock.Value, Namespaces, _source);
            if (selectors is null || ContainsPseudoElement(selectors))
                return false;

            end = SelectorSerializer.Serialize(selectors);
            endSelectors = selectors;
            index += 2;
        }

        return index == values.Count;
    }

    private static bool ContainsPseudoElement(SelectorList selectors)
        => selectors.Selectors.Any(selector => selector.Compounds.Any(compound => compound.Selectors.Any(simple => simple is PseudoElementSelector)));

    /// <summary>https://drafts.csswg.org/css-conditional-5/#container-rule: &lt;container-condition&gt;#</summary>
    private List<ContainerCondition>? ParseContainerConditions(List<CssComponentValue> prelude)
    {
        var result = new List<ContainerCondition>();
        var current = new List<CssComponentValue>();
        foreach (var value in prelude.Append(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma })))
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Comma })
            {
                current.Add(value);
                continue;
            }

            if (ContainerQueryParser.ParseCondition(current, _source) is not { } condition)
                return null;

            result.Add(condition);
            current = [];
        }

        return result;
    }

    /// <summary>https://drafts.css-houdini.org/css-properties-values-api/#at-property-rule</summary>
    private CssPropertyRule? ParsePropertyRule(List<CssComponentValue> prelude, CssSimpleBlock block)
    {
        if (prelude is not [CssPreservedToken { Type: CssTokenType.Ident } nameToken] || !CssPropertyRegistry.IsCustomProperty(nameToken.Token.Value))
            return null;

        string? syntax = null;
        bool? inherits = null;
        string? initialValue = null;
        foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(block.Value)))
        {
            if (node is not CssDeclaration declaration)
                continue;

            var values = ValueMatcher.RemoveWhitespace(declaration.Value);
            switch (InfraAscii.ToAsciiLowercase(declaration.Name))
            {
                case "syntax" when values is [CssPreservedToken { Type: CssTokenType.String } syntaxToken]:
                    syntax = syntaxToken.Token.Value;
                    break;
                case "inherits" when values is [CssPreservedToken { Type: CssTokenType.Ident } inheritsToken] && (inheritsToken.Token.IsIdent("true") || inheritsToken.Token.IsIdent("false")):
                    inherits = inheritsToken.Token.IsIdent("true");
                    break;
                case "initial-value":
                    initialValue = declaration.Value.Count == 0 ? "" : CssSerializer.SerializeValue(declaration.Value, property: null);
                    break;
            }
        }

        // The syntax and inherits descriptors are required; the initial value is required unless the syntax is universal, and
        // it must match the syntax and be computationally independent
        if (syntax is null || inherits is null || CustomPropertySyntax.Parse(syntax) is not { } syntaxDefinition)
            return null;

        if (initialValue is null ? !syntaxDefinition.IsUniversal : !syntaxDefinition.IsValidInitialValue(initialValue))
            return null;

        return new CssPropertyRule(nameToken.Token.Value, syntax, syntaxDefinition, inherits.Value, initialValue);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-mixins-1/#function-rule: &lt;function-token&gt; &lt;function-parameter&gt;#? ) [ returns &lt;css-type&gt; ]?.
    /// The name of the function starts with two dashes.
    /// </summary>
    private CssFunctionRule? ParseFunctionPrelude(List<CssComponentValue> prelude)
    {
        var index = prelude.FindIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        if (index < 0 || prelude[index] is not CssFunction function || !function.Name.StartsWith("--", StringComparison.Ordinal))
            return null;

        var parameters = ParseFunctionParameters(function.Value);
        if (parameters is null)
            return null;

        var returnType = CssSyntaxDefinition.Universal;
        var rest = prelude[(index + 1)..];
        var significant = ValueMatcher.RemoveWhitespace(rest);
        if (significant.Count > 0)
        {
            if (significant[0] is not CssPreservedToken { Type: CssTokenType.Ident } returns || !returns.Token.IsIdent("returns"))
                return null;

            var type = ParseCssType(rest[(rest.IndexOf(significant[0]) + 1)..]);
            if (type is null)
                return null;

            returnType = type;
        }

        return new CssFunctionRule(function.Name, parameters, returnType);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-mixins-1/#mixin-rule: [ &lt;function-token&gt; &lt;function-parameter&gt;#? ) | &lt;ident-token&gt; ].
    /// The name of the mixin starts with two dashes.
    /// </summary>
    private CssMixinRule? ParseMixinPrelude(List<CssComponentValue> prelude)
    {
        switch (ValueMatcher.RemoveWhitespace(prelude))
        {
            case [CssPreservedToken { Type: CssTokenType.Ident } ident] when ident.Token.Value.StartsWith("--", StringComparison.Ordinal):
                return new CssMixinRule(ident.Token.Value, []);

            case [CssFunction function] when function.Name.StartsWith("--", StringComparison.Ordinal):
                return ParseFunctionParameters(function.Value) is { } parameters ? new CssMixinRule(function.Name, parameters) : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-mixins-1/#apply-rule: [ &lt;dashed-ident&gt; | &lt;dashed-function&gt; ]. Each argument is
    /// a &lt;declaration-value&gt;, possibly in a {} block (https://drafts.csswg.org/css-values-5/#component-function-commas):
    /// a top-level "!" (e.g. !important) makes the rule invalid.
    /// </summary>
    private CssApplyPrelude? ParseApplyPrelude(List<CssComponentValue> prelude)
    {
        switch (ValueMatcher.RemoveWhitespace(prelude))
        {
            case [CssPreservedToken { Type: CssTokenType.Ident } ident] when ident.Token.Value.StartsWith("--", StringComparison.Ordinal):
                return new CssApplyPrelude(ident.Token.Value, []);

            case [CssFunction function] when function.Name.StartsWith("--", StringComparison.Ordinal):
            {
                var arguments = new List<string>();
                if (ValueMatcher.RemoveWhitespace(function.Value).Count > 0)
                {
                    foreach (var argument in ValueMatcher.SplitAtCommas(function.Value))
                    {
                        var values = ValueMatcher.TrimWhitespace(argument);
                        if (values is [CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } block])
                        {
                            values = ValueMatcher.TrimWhitespace(block.Value);
                        }

                        if (!CssShorthands.IsValidDeclarationValue(values, topLevel: true))
                            return null;

                        if (ValueMatcher.ContainsSubstitutionFunction(values) && !ValueMatcher.AreSubstitutionFunctionsValid(values))
                            return null;

                        arguments.Add(GetOriginalText(values));
                    }
                }

                return new CssApplyPrelude(function.Name, arguments);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses the declarations of a @private rule: &lt;custom-property-name&gt; &lt;css-type&gt;? : &lt;declaration-value&gt;.
    /// The other declarations (the properties, the variables without value, the important declarations) are ignored.
    /// </summary>
    private List<CssPrivateDeclaration> ParsePrivateDeclarations(List<CssComponentValue> block)
    {
        var result = new List<CssPrivateDeclaration>();
        var start = 0;
        for (var i = 0; i <= block.Count; i++)
        {
            if (i < block.Count && block[i] is not CssPreservedToken { Type: CssTokenType.Semicolon })
                continue;

            if (ParsePrivateDeclaration(block[start..i]) is { } declaration)
            {
                result.Add(declaration);
            }

            start = i + 1;
        }

        return result;
    }

    private CssPrivateDeclaration? ParsePrivateDeclaration(List<CssComponentValue> values)
    {
        var declaration = ValueMatcher.TrimWhitespace(values);
        if (declaration is not [CssPreservedToken { Type: CssTokenType.Ident } nameToken, ..] || !CssPropertyRegistry.IsCustomProperty(nameToken.Token.Value))
            return null;

        var colon = declaration.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Colon });
        if (colon < 0)
            return null;

        var type = CssSyntaxDefinition.Universal;
        var typeValues = declaration[1..colon];
        if (ValueMatcher.RemoveWhitespace(typeValues).Count > 0)
        {
            if (ParseCssType(typeValues) is not { } parsedType)
                return null;

            type = parsedType;
        }

        var value = declaration[(colon + 1)..];
        if (ValueMatcher.RemoveWhitespace(value).Count == 0 || !CssShorthands.IsValidDeclarationValue(value, topLevel: true))
            return null;

        if (ValueMatcher.ContainsSubstitutionFunction(value))
        {
            if (!ValueMatcher.AreSubstitutionFunctionsValid(value))
                return null;
        }
        else if (!CustomPropertySyntax.Matches(type, value))
        {
            return null;
        }

        return new CssPrivateDeclaration(nameToken.Token.Value, type, GetOriginalText(value));
    }

    /// <summary>
    /// Parses the parameters of a @function or @mixin rule: &lt;function-parameter&gt;#?, where &lt;function-parameter&gt; is
    /// &lt;custom-property-name&gt; &lt;css-type&gt;? [ : &lt;default-value&gt; ]? (https://drafts.csswg.org/css-mixins-1/#typedef-function-parameter).
    /// </summary>
    private List<CssFunctionParameterDefinition>? ParseFunctionParameters(List<CssComponentValue> values)
    {
        var result = new List<CssFunctionParameterDefinition>();
        if (ValueMatcher.RemoveWhitespace(values).Count == 0)
            return result;

        foreach (var argument in ValueMatcher.SplitAtCommas(values))
        {
            var parameter = ValueMatcher.TrimWhitespace(argument);
            if (parameter is not [CssPreservedToken { Type: CssTokenType.Ident } nameToken, ..] || !CssPropertyRegistry.IsCustomProperty(nameToken.Token.Value))
                return null;

            // The same name more than once makes the rule invalid
            var name = nameToken.Token.Value;
            if (result.Exists(p => p.Name == name))
                return null;

            var colon = parameter.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Colon });
            var typeValues = parameter[1..(colon < 0 ? parameter.Count : colon)];
            var type = CssSyntaxDefinition.Universal;
            if (ValueMatcher.RemoveWhitespace(typeValues).Count > 0)
            {
                if (ParseCssType(typeValues) is not { } parsedType)
                    return null;

                type = parsedType;
            }

            string? defaultValue = null;
            if (colon >= 0)
            {
                // The default value is a <declaration-value> that matches the type (unless it contains arbitrary substitution functions)
                var defaultValues = parameter[(colon + 1)..];
                if (ValueMatcher.RemoveWhitespace(defaultValues).Count == 0 || !CssShorthands.IsValidDeclarationValue(defaultValues, topLevel: true))
                    return null;

                if (ValueMatcher.ContainsSubstitutionFunction(defaultValues))
                {
                    if (!ValueMatcher.AreSubstitutionFunctionsValid(defaultValues))
                        return null;
                }
                else if (!CustomPropertySyntax.Matches(type, defaultValues))
                {
                    return null;
                }

                defaultValue = GetOriginalText(defaultValues);
            }

            result.Add(new CssFunctionParameterDefinition(name, type, defaultValue));
        }

        return result;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-mixins-1/#typedef-css-type: &lt;syntax-component&gt; | type( &lt;syntax&gt; ). A syntax
    /// with several components or the universal syntax must be written in type().
    /// </summary>
    private static CssSyntaxDefinition? ParseCssType(List<CssComponentValue> values)
    {
        var significant = ValueMatcher.RemoveWhitespace(values);
        if (significant is [CssFunction { Name: var functionName } typeFunction] && InfraAscii.EqualsIgnoreAsciiCase(functionName, "type"))
            return CustomPropertySyntax.ParseSyntax(typeFunction.Value);

        return CustomPropertySyntax.ParseSyntax(values) is { ComponentCount: 1 } component ? component : null;
    }

    /// <summary>
    /// Whether the @counter-style rules may have the names that the style sheets cannot define (decimal, disc...): the style
    /// sheet of the predefined counter styles (https://drafts.csswg.org/css-counter-styles-3/#predefined-counters).
    /// </summary>
    public bool AllowsReservedCounterStyleNames { get; init; }

    /// <summary>https://drafts.csswg.org/css-counter-styles/#typedef-counter-style-name</summary>
    public static string? ParseCounterStyleName(List<CssComponentValue> values, bool allowReserved = false)
    {
        if (ValueMatcher.RemoveWhitespace(values) is not [CssPreservedToken { Type: CssTokenType.Ident } ident])
            return null;

        var lower = InfraAscii.ToAsciiLowercase(ident.Token.Value);
        if (lower is "none" or "default" || ValueMatcher.CssWideKeywords.Contains(lower))
            return null;

        // The predefined counter styles are case-insensitive and serialized in lowercase
        return !allowReserved && Cascade.CounterStyle.IsNonOverridableName(lower) ? null : ident.Token.Value;
    }

    /// <summary>https://drafts.csswg.org/css-animations/#typedef-keyframes-name</summary>
    private static string? ParseKeyframesName(List<CssComponentValue> prelude)
    {
        return prelude switch
        {
            // The empty string is not a valid name (like the empty string of animation-name)
            [CssPreservedToken { Type: CssTokenType.String } str] when str.Token.Value.Length > 0 => str.Token.Value,
            [CssPreservedToken { Type: CssTokenType.Ident } ident] when InfraAscii.ToAsciiLowercase(ident.Token.Value) is not ("none" or "default") && !ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(ident.Token.Value)) => ident.Token.Value,
            _ => null,
        };
    }

    public CssKeyframeRule? ParseKeyframe(CssQualifiedRule rule)
    {
        var keys = ParseKeyframeSelector(rule.Prelude);
        if (keys is null)
            return null;

        var declarations = new List<CssDeclarationEntry>();
        foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(rule.Block.Value)))
        {
            // !important declarations and the animation properties are ignored in keyframes (https://drafts.csswg.org/css-animations/#keyframes)
            if (node is CssDeclaration { Important: false } declaration && CssStyleDeclaration.IsAllowedInKeyframe(declaration.Name))
            {
                CssStyleDeclaration.AppendDeclaration(declaration, _source, declarations, atRule: null, _quirks);
            }
        }

        return new CssKeyframeRule(keys, declarations);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-animations/#typedef-keyframe-selector: from | to | &lt;percentage [0,100]&gt;, or a
    /// timeline range name with a percentage (https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes),
    /// comma-separated. Returns the serialized keys (from is 0%, to is 100%), or null when the selector is invalid.
    /// </summary>
    public static List<string>? ParseKeyframeSelector(List<CssComponentValue> values)
    {
        var grammar = CssPropertyRegistry.GetTypeGrammar("<keyframe-selector>");
        var result = new List<string>();
        foreach (var key in ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(values)))
        {
            if (grammar is null || !ValueMatcher.Matches(grammar, key))
                return null;

            result.Add(key switch
            {
                [var from] when from is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("from") => "0%",
                [var to] when to is CssPreservedToken { Type: CssTokenType.Ident } => "100%",
                [CssPreservedToken { Type: CssTokenType.Percentage } percentage] => CssSerializer.SerializeNumber(percentage.Token.NumericValue) + "%",
                [CssPreservedToken { Type: CssTokenType.Ident } name, var offset] => InfraAscii.ToAsciiLowercase(name.Token.Value) + " " + CssSerializer.SerializeValue([offset], property: null),
                _ => CssSerializer.SerializeValue(key, property: null),
            });
        }

        return result;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-page-3/#syntax-page-selector: returns the serialized page selector list (empty when
    /// there is no selector), or null when it is invalid.
    /// </summary>
    public static string? ParsePageSelector(List<CssComponentValue> values)
    {
        var selectors = new List<string>();
        var current = new StringBuilder();
        var hasContent = false;
        var index = 0;
        var significant = values;
        while (index < significant.Count)
        {
            var value = significant[index];
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                    // Whitespace is only allowed around the commas
                    if (hasContent && index + 1 < significant.Count && significant[index + 1] is not CssPreservedToken { Type: CssTokenType.Whitespace or CssTokenType.Comma })
                    {
                        var next = significant.Skip(index + 1).FirstOrDefault(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
                        if (next is not null and not CssPreservedToken { Type: CssTokenType.Comma })
                            return null;
                    }

                    index++;
                    continue;
                case CssPreservedToken { Type: CssTokenType.Ident } ident when !hasContent:
                    current.Append(CssSerializer.SerializeIdentifier(ident.Token.Value));
                    hasContent = true;
                    index++;
                    continue;
                case CssPreservedToken { Type: CssTokenType.Colon }:
                    if (index + 1 < significant.Count && significant[index + 1] is CssPreservedToken { Type: CssTokenType.Ident } pseudo
                        && InfraAscii.ToAsciiLowercase(pseudo.Token.Value) is "left" or "right" or "first" or "blank")
                    {
                        current.Append(':').Append(InfraAscii.ToAsciiLowercase(pseudo.Token.Value));
                        hasContent = true;
                        index += 2;
                        continue;
                    }

                    return null;
                case CssPreservedToken { Type: CssTokenType.Comma }:
                    if (!hasContent)
                        return null;

                    selectors.Add(current.ToString());
                    current.Clear();
                    hasContent = false;
                    index++;
                    continue;
                default:
                    return null;
            }
        }

        if (hasContent)
        {
            selectors.Add(current.ToString());
        }
        else if (selectors.Count > 0)
        {
            return null;
        }

        return string.Join(", ", selectors);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // @supports (https://drafts.csswg.org/css-conditional-3/#at-supports)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses and evaluates a &lt;supports-condition&gt; starting at the index of the component values (whitespace
    /// removed); returns null when the syntax is invalid.
    /// </summary>
    /// <param name="values">The component values.</param>
    /// <param name="index">The index of the condition.</param>
    /// <param name="end">The index after the condition.</param>
    /// <param name="quirks">Whether CSS is parsed in quirks mode: the quirky lengths and colors are valid in the declarations.</param>
    /// <param name="namespaces">The namespaces declared by the style sheet, for the prefixes of selector().</param>
    public static bool? EvaluateSupportsCondition(List<CssComponentValue> values, int index, out int end, bool quirks = false, SelectorNamespaceContext? namespaces = null)
    {
        end = index;
        if (index >= values.Count)
            return null;

        if (values[index] is CssPreservedToken { Type: CssTokenType.Ident } not && not.Token.IsIdent("not"))
        {
            var inner = EvaluateSupportsInParens(values, index + 1, quirks, namespaces);
            if (inner is null)
                return null;

            end = index + 2;
            return !inner.Value;
        }

        var result = EvaluateSupportsInParens(values, index, quirks, namespaces);
        if (result is null)
            return null;

        index++;
        string? combinator = null;
        while (index < values.Count && values[index] is CssPreservedToken { Type: CssTokenType.Ident } word && InfraAscii.ToAsciiLowercase(word.Token.Value) is "and" or "or")
        {
            var keyword = InfraAscii.ToAsciiLowercase(word.Token.Value);
            if (combinator is not null && combinator != keyword)
                return null;

            combinator = keyword;
            var next = EvaluateSupportsInParens(values, index + 1, quirks, namespaces);
            if (next is null)
                return null;

            result = keyword == "and" ? result.Value && next.Value : result.Value || next.Value;
            index += 2;
        }

        end = index;
        return result;
    }

    private static bool? EvaluateSupportsInParens(List<CssComponentValue> values, int index, bool quirks, SelectorNamespaceContext? namespaces)
    {
        if (index >= values.Count)
            return null;

        switch (values[index])
        {
            // A block or a function that contains a bad string, a bad URL or an unmatched closing bracket is not a
            // <general-enclosed> (<any-value>, https://drafts.csswg.org/css-syntax-3/#typedef-any-value): it is invalid
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block when !MediaQueryEvaluator.IsAnyValue(block.Value):
                return null;

            case CssFunction function when !MediaQueryEvaluator.IsAnyValue(function.Value):
                return null;

            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block:
            {
                var inner = ValueMatcher.RemoveWhitespace(block.Value);
                if (EvaluateSupportsCondition(inner, 0, out var end, quirks, namespaces) is { } condition && end == inner.Count)
                    return condition;

                // <supports-decl>
                var stream = new CssComponentValueStream(block.Value);
                stream.DiscardWhitespace();
                if (stream.NextIs(CssTokenType.Ident) && CssParser.ConsumeDeclaration(stream, nested: false) is { } declaration)
                {
                    stream.DiscardWhitespace();
                    if (stream.IsEmpty)
                        return IsSupportedDeclaration(declaration.Name, declaration.Value, quirks);
                }

                // <general-enclosed>
                return false;
            }

            case CssFunction function:
            {
                var name = InfraAscii.ToAsciiLowercase(function.Name);
                if (name == "selector")
                    return SelectorParser.ParseSupportsSelector(function.Value, namespaces) is { Selectors: [var selector] } && !selector.Compounds.SelectMany(c => c.Selectors).Any(s => s is PseudoElementSelector { Name: var pseudo } && pseudo.StartsWith("-webkit-", StringComparison.Ordinal));

                // https://drafts.csswg.org/css-conditional-5/#support-definition-at-rules: an at-rule recognized by the parser
                if (name == "at-rule")
                    return ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.AtKeyword } atKeyword] && IsSupportedAtRule(atKeyword.Token.Value);

                // https://drafts.csswg.org/css-conditional-5/#typedef-supports-font-format-fn: the font formats that can be loaded
                // (https://drafts.csswg.org/css-fonts-4/#font-format-definitions)
                if (name == "font-format")
                    return ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.Ident } format] && InfraAscii.ToAsciiLowercase(format.Token.Value) is "collection" or "opentype" or "truetype" or "woff" or "woff2";

                // https://drafts.csswg.org/css-conditional-5/#typedef-supports-font-tech-fn: the font technologies of the
                // browsers (https://drafts.csswg.org/css-fonts-4/#font-tech-definitions)
                if (name == "font-tech")
                    return ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.Ident } tech] && InfraAscii.ToAsciiLowercase(tech.Token.Value) is "features-opentype" or "color-colrv0" or "color-colrv1" or "color-sbix" or "color-cbdt" or "variations" or "palettes";

                // https://drafts.csswg.org/css-conditional-5/#support-definition-named-features: the anchor positioning takes the
                // transforms of the anchors into account (https://drafts.csswg.org/css-anchor-position-1/#determining)
                if (name == "named-feature")
                    return ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.Ident } feature] && InfraAscii.ToAsciiLowercase(feature.Token.Value) is "anchor-position-follows-transforms";

                // <general-enclosed>
                return false;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses a @view-transition rule (https://drafts.csswg.org/css-view-transitions-2/#view-transition-rule): the
    /// <c>navigation</c> descriptor (<c>auto | none</c>) and the <c>types</c> descriptor (<c>none | &lt;custom-ident&gt;+</c>, where
    /// <c>none</c> and the names that start with <c>-ua-</c> are not valid types); the invalid descriptors are ignored.
    /// </summary>
    private static CssViewTransitionRule ParseViewTransitionRule(CssSimpleBlock block)
    {
        var navigation = "";
        List<string> types = [];
        var hasTypes = false;
        foreach (var node in CssParser.ConsumeBlockContents(new CssComponentValueStream(block.Value)))
        {
            if (node is not CssDeclaration { Important: false } declaration)
                continue;

            var values = ValueMatcher.RemoveWhitespace(declaration.Value);
            switch (InfraAscii.ToAsciiLowercase(declaration.Name))
            {
                case "navigation":
                    if (values is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && InfraAscii.ToAsciiLowercase(keyword.Token.Value) is ("auto" or "none") and var value)
                    {
                        navigation = value;
                    }

                    break;

                case "types":
                {
                    if (values is [CssPreservedToken { Type: CssTokenType.Ident } none] && InfraAscii.EqualsIgnoreAsciiCase(none.Token.Value, "none"))
                    {
                        types = [];
                        hasTypes = true;
                        break;
                    }

                    var parsed = new List<string>();
                    foreach (var item in values)
                    {
                        if (item is not CssPreservedToken { Type: CssTokenType.Ident } type || InfraAscii.EqualsIgnoreAsciiCase(type.Token.Value, "none")
                            || type.Token.Value.StartsWith("-ua-", StringComparison.Ordinal) || InfraAscii.EqualsIgnoreAsciiCase(type.Token.Value, "default")
                            || ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(type.Token.Value)))
                        {
                            parsed = null;
                            break;
                        }

                        parsed.Add(type.Token.Value);
                    }

                    if (parsed is { Count: > 0 })
                    {
                        types = parsed;
                        hasTypes = true;
                    }

                    break;
                }
            }
        }

        return new CssViewTransitionRule(navigation, types, hasTypes);
    }

    /// <summary>
    /// The at-rules that the parser recognizes, in any context (https://drafts.csswg.org/css-conditional-5/#support-definition-at-rules):
    /// the margin rules of @page and the feature value blocks of @font-feature-values (e.g. @swash) included.
    /// </summary>
    private static bool IsSupportedAtRule(string name) => InfraAscii.ToAsciiLowercase(name) is "import" or "namespace" or "media" or "supports" or "container" or "layer"
        or "custom-media" or "scope" or "starting-style" or "font-face" or "keyframes" or "-webkit-keyframes" or "page" or "property" or "counter-style" or "font-feature-values" or "font-palette-values" or "function" or "position-try" or "view-transition"
        or "stylistic" or "historical-forms" or "styleset" or "character-variant" or "swash" or "ornaments" or "annotation"
        or "top-left-corner" or "top-left" or "top-center" or "top-right" or "top-right-corner" or "bottom-left-corner" or "bottom-left" or "bottom-center"
        or "bottom-right" or "bottom-right-corner" or "left-top" or "left-middle" or "left-bottom" or "right-top" or "right-middle" or "right-bottom";

    /// <summary>
    /// Serializes the at-rule() functions of a supports condition (https://drafts.csswg.org/css-conditional-5/#typedef-supports-at-rule-fn):
    /// "at-rule( @supports )" is "at-rule(@supports)", and the name of the at-keyword is serialized as an identifier.
    /// </summary>
    internal static string NormalizeSupportsConditionText(string text)
    {
        if (!text.Contains("at-rule(", StringComparison.OrdinalIgnoreCase))
            return text;

        return AtRuleFunctionRegex().Replace(text, match =>
        {
            var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(match.Groups["value"].Value, out _));
            return values is [CssPreservedToken { Type: CssTokenType.AtKeyword } atKeyword] ? match.Groups["name"].Value + "(@" + CssSerializer.SerializeIdentifier(atKeyword.Token.Value) + ")" : match.Value;
        });
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<name>at-rule)\((?<value>[^()]*)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial System.Text.RegularExpressions.Regex AtRuleFunctionRegex();

    /// <summary>
    /// Returns whether a declaration is supported (https://drafts.csswg.org/css-conditional-3/#dfn-support). In quirks mode, the
    /// quirky lengths and colors are valid (https://drafts.csswg.org/css-values-4/#deprecated-quirky-length).
    /// </summary>
    public static bool IsSupportedDeclaration(string property, List<CssComponentValue> values, bool quirks = false)
    {
        var name = CssPropertyRegistry.NormalizeName(property);
        if (CssShorthands.ParseValue(name, values, originalText: "") is not null)
            return true;

        return quirks && CssQuirks.RewriteQuirkyValues(name, values) is { } rewritten && CssShorthands.ParseValue(name, rewritten, originalText: "") is not null;
    }
}
