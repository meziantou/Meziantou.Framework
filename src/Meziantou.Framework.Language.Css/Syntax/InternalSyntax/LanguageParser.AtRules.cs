using Meziantou.Framework.Language.Css.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>At-rules: their preludes, their blocks, and where they may be.</summary>
internal sealed partial class LanguageParser
{
    private GreenNode ParseAtRule(BlockContext context, bool isTopLevel)
    {
        var start = _index;
        var mark = _pending.Count;
        var atKeyword = EatToken();
        var name = atKeyword.ValueText;
        var kind = GetAtRuleKind(name, context);

        // At the top level of a style sheet, a closing brace that closes nothing is part of the prelude; in a block, it
        // can only be the closing brace of a declaration list, which ends the rule.
        var nested = !isTopLevel || context != BlockContext.TopLevel;
        var end = _index;
        while (end < _limit && KindAt(end) is not (SyntaxKind.SemicolonToken or SyntaxKind.OpenBraceToken) && !(nested && KindAt(end) == SyntaxKind.CloseBraceToken))
        {
            end = SkipComponentValue(end);
        }

        var hasBlock = end < _limit && KindAt(end) == SyntaxKind.OpenBraceToken;
        var prelude = ParseAtRulePrelude(kind, name, end, hasBlock);

        CheckPlacement(start, kind, name, context, isTopLevel, hasBlock, prelude);

        GreenNode? block = null;
        GreenToken? semicolon = null;
        if (hasBlock)
        {
            if (!RequiresBlock(kind) && kind is not (SyntaxKind.LayerRule or SyntaxKind.UnknownAtRule or SyntaxKind.FunctionRule))
            {
                AddError(start, CssDiagnosticDescriptors.BlockNotAllowed, name);
            }

            block = ParseBlock(GetAtRuleBlockContext(kind, context));
        }
        else
        {
            if (RequiresBlock(kind))
            {
                AddMissingToken(CssDiagnosticDescriptors.BlockRequired, name);
            }

            if (_index < _limit && CurrentKind == SyntaxKind.SemicolonToken)
            {
                semicolon = EatToken();
            }
        }

        return Finish(new CssAtRuleSyntax(kind, atKeyword, prelude, block, semicolon), _starts[start], mark);
    }

    private static SyntaxKind GetAtRuleKind(string name, BlockContext context)
    {
        // A margin rule and a feature block are only rules inside the rule they belong to.
        if (context == BlockContext.Page && SyntaxFacts.IsPageMarginRuleName(name))
            return SyntaxKind.PageMarginRule;

        if (context == BlockContext.FontFeatureValues && SyntaxFacts.IsFontFeatureValueBlockName(name))
            return SyntaxKind.FontFeatureValueBlockRule;

        return SyntaxFacts.GetAtRuleKind(name);
    }

    private static bool RequiresBlock(SyntaxKind kind) => kind switch
    {
        SyntaxKind.CharsetRule or SyntaxKind.ImportRule or SyntaxKind.NamespaceRule or SyntaxKind.CustomMediaRule => false,
        SyntaxKind.LayerRule or SyntaxKind.UnknownAtRule => false,
        _ => true,
    };

    private static BlockContext GetAtRuleBlockContext(SyntaxKind kind, BlockContext parent) => kind switch
    {
        SyntaxKind.MediaRule or SyntaxKind.SupportsRule or SyntaxKind.ContainerRule or SyntaxKind.LayerRule or SyntaxKind.StartingStyleRule => parent switch
        {
            BlockContext.StyleRule or BlockContext.DeclarationsOnly => BlockContext.StyleRule,
            BlockContext.Function => BlockContext.Function,
            BlockContext.Scope => BlockContext.Scope,
            BlockContext.Unknown => BlockContext.Unknown,
            _ => BlockContext.Group,
        },
        SyntaxKind.ScopeRule => parent == BlockContext.StyleRule ? BlockContext.StyleRule : BlockContext.Scope,
        SyntaxKind.KeyframesRule => BlockContext.Keyframes,
        SyntaxKind.PageRule => BlockContext.Page,
        SyntaxKind.FontFeatureValuesRule => BlockContext.FontFeatureValues,
        SyntaxKind.FunctionRule => BlockContext.Function,
        SyntaxKind.FontFaceRule or SyntaxKind.PropertyRule or SyntaxKind.CounterStyleRule or SyntaxKind.FontPaletteValuesRule or SyntaxKind.PositionTryRule
            or SyntaxKind.ViewTransitionRule or SyntaxKind.PageMarginRule or SyntaxKind.FontFeatureValueBlockRule => BlockContext.DeclarationsOnly,
        _ => BlockContext.Unknown,
    };

    private static bool AllowsDeclarations(BlockContext context)
        => context is BlockContext.Scope or BlockContext.StyleRule or BlockContext.DeclarationsOnly or BlockContext.Page or BlockContext.FontFeatureValues or BlockContext.Function or BlockContext.Unknown;

    private static bool AllowsQualifiedRules(BlockContext context)
        => context is BlockContext.TopLevel or BlockContext.Group or BlockContext.Scope or BlockContext.StyleRule or BlockContext.Keyframes or BlockContext.Unknown;

    private static bool AllowsAtRule(BlockContext context, SyntaxKind kind) => context switch
    {
        BlockContext.TopLevel => true,
        BlockContext.Group or BlockContext.Scope => kind is not (SyntaxKind.CharsetRule or SyntaxKind.ImportRule or SyntaxKind.NamespaceRule),
        BlockContext.StyleRule => kind is SyntaxKind.MediaRule or SyntaxKind.SupportsRule or SyntaxKind.ContainerRule or SyntaxKind.LayerRule or SyntaxKind.ScopeRule
            or SyntaxKind.StartingStyleRule or SyntaxKind.UnknownAtRule,
        BlockContext.Function => kind is SyntaxKind.MediaRule or SyntaxKind.SupportsRule or SyntaxKind.ContainerRule or SyntaxKind.UnknownAtRule,
        BlockContext.Page => kind is SyntaxKind.PageMarginRule or SyntaxKind.UnknownAtRule,
        BlockContext.FontFeatureValues => kind is SyntaxKind.FontFeatureValueBlockRule or SyntaxKind.UnknownAtRule,
        BlockContext.Unknown => true,
        _ => kind == SyntaxKind.UnknownAtRule,
    };

    private static string DescribeContext(BlockContext context) => context switch
    {
        BlockContext.TopLevel => "at the top level of a style sheet",
        BlockContext.Group => "in a conditional group rule",
        BlockContext.Scope => "in '@scope'",
        BlockContext.StyleRule => "in a style rule",
        BlockContext.Keyframes => "in '@keyframes'",
        BlockContext.Page => "in '@page'",
        BlockContext.FontFeatureValues => "in '@font-feature-values'",
        BlockContext.Function => "in '@function'",
        BlockContext.DeclarationsOnly => "in a block that only holds declarations",
        _ => "here",
    };

    /// <summary>Reports an at-rule that is not where it may be, or that a browser ignores where it is.</summary>
    private void CheckPlacement(int start, SyntaxKind kind, string name, BlockContext context, bool isTopLevel, bool hasBlock, GreenNode? prelude)
    {
        if (kind == SyntaxKind.UnknownAtRule)
        {
            if (context != BlockContext.Unknown && !SyntaxFacts.IsVendorPrefixed(name))
            {
                AddError(start, CssDiagnosticDescriptors.UnknownAtRule, name);
            }

            return;
        }

        if (!AllowsAtRule(context, kind))
        {
            AddError(start, CssDiagnosticDescriptors.RuleNotAllowed, "'@" + name + "'", DescribeContext(context));
            return;
        }

        if (!isTopLevel || context != BlockContext.TopLevel)
            return;

        switch (kind)
        {
            case SyntaxKind.CharsetRule:
                // "@charset" is not a rule at all, but the bytes the encoding is sniffed from: it must be exactly that.
                if (start != 0 || _tokens[0].LeadingTrivia is not null || !string.Equals(_tokens[0].Text, "@charset", StringComparison.Ordinal)
                    || _tokens[0].TrailingTrivia?.ToFullString() != " " || KindAt(1) != SyntaxKind.StringToken || _tokens[1].Text[0] != '"'
                    || _tokens[1].TrailingTrivia is not null || KindAt(2) != SyntaxKind.SemicolonToken || _tokens[2].LeadingTrivia is not null)
                {
                    AddError(start, CssDiagnosticDescriptors.InvalidCharset);
                }

                break;

            case SyntaxKind.ImportRule:
                if (_topLevelState != TopLevelState.Imports)
                {
                    AddError(start, CssDiagnosticDescriptors.MisplacedRule, name, "'@charset' and '@layer' statements");
                }

                break;

            case SyntaxKind.NamespaceRule:
                if (_topLevelState == TopLevelState.Rules)
                {
                    AddError(start, CssDiagnosticDescriptors.MisplacedRule, name, "'@charset', '@import', and '@layer' statements");
                }
                else
                {
                    // Only a rule a browser keeps declares its prefix: one after other rules is ignored.
                    _topLevelState = TopLevelState.Namespaces;
                    if (prelude is CssNamespacePreludeSyntax && prelude.GetSlot(0) is GreenToken prefix)
                    {
                        _namespacePrefixes.Add(prefix.ValueText);
                    }
                }

                break;

            case SyntaxKind.LayerRule when !hasBlock:
                break;

            default:
                _topLevelState = TopLevelState.Rules;
                break;
        }
    }

    private GreenNode? ParseAtRulePrelude(SyntaxKind kind, string name, int end, bool hasBlock)
    {
        switch (kind)
        {
            case SyntaxKind.CharsetRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => ParseNamePrelude(kind, name), required: true, name, "expected a string");

            case SyntaxKind.ImportRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => ParseImportPrelude(name), required: true, name, "expected a URL or a string");

            case SyntaxKind.NamespaceRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => ParseNamespacePrelude(name), required: true, name, "expected a URL or a string");

            case SyntaxKind.MediaRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, ParseMediaQueryList, required: false, name, "");

            case SyntaxKind.CustomMediaRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => ParseCustomMediaPrelude(name), required: true, name, "expected a name starting with '--'");

            case SyntaxKind.SupportsRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, ParseSupportsPrelude, required: true, name, "expected a condition");

            case SyntaxKind.ContainerRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, ParseContainerPrelude, required: true, name, "expected a container name or query");

            case SyntaxKind.LayerRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => ParseLayerPrelude(name, hasBlock), required: !hasBlock, name, "expected a layer name");

            case SyntaxKind.ScopeRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, ParseScopePrelude, required: false, name, "");

            case SyntaxKind.KeyframesRule or SyntaxKind.PropertyRule or SyntaxKind.CounterStyleRule or SyntaxKind.FontPaletteValuesRule or SyntaxKind.PositionTryRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => ParseNamePrelude(kind, name), required: true, name, "expected a name");

            case SyntaxKind.PageRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, ParsePageSelectorList, required: false, name, "");

            case SyntaxKind.FontFeatureValuesRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => ParseFamilyNameList(name), required: true, name, "expected a font family name");

            case SyntaxKind.FontFaceRule or SyntaxKind.StartingStyleRule or SyntaxKind.ViewTransitionRule or SyntaxKind.PageMarginRule or SyntaxKind.FontFeatureValueBlockRule:
                return ParseTypedPrelude(end, CssDiagnosticDescriptors.InvalidPrelude, () => Fail<GreenNode>(_index, CssDiagnosticDescriptors.InvalidPrelude, name, "it takes no prelude"), required: false, name, "");

            default:
                return _index == end ? null : ParseGenericPrelude(end);
        }
    }

    /// <summary>Parses the name of <c>@keyframes</c>, <c>@property</c>, <c>@counter-style</c>, and the like.</summary>
    private CssNamePreludeSyntax? ParseNamePrelude(SyntaxKind kind, string ruleName)
    {
        var nameIndex = _index;
        var token = Current;
        var tokenKind = CurrentKind;
        var value = token.ValueText;
        string? reason = kind switch
        {
            SyntaxKind.CharsetRule => tokenKind == SyntaxKind.StringToken ? null : "expected a string",
            SyntaxKind.KeyframesRule => tokenKind switch
            {
                SyntaxKind.StringToken => null,
                SyntaxKind.IdentToken when SyntaxFacts.IsCssWideKeyword(value) || CssIdentifier.EqualsIgnoreAsciiCase(value, "none") => "it is a reserved keyword",
                SyntaxKind.IdentToken => null,
                _ => "expected an identifier or a string",
            },
            SyntaxKind.CounterStyleRule => tokenKind switch
            {
                SyntaxKind.IdentToken when SyntaxFacts.IsCssWideKeyword(value) || CssIdentifier.ToAsciiLowerCase(value) is "none" or "decimal" or "disc" or "square" or "circle" or "disclosure-open" or "disclosure-closed" => "it is a reserved keyword",
                SyntaxKind.IdentToken => null,
                _ => "expected an identifier",
            },
            _ => tokenKind == SyntaxKind.IdentToken && SyntaxFacts.IsCustomPropertyName(value) ? null : "expected a name starting with '--'",
        };

        if (reason is not null)
            return Fail<CssNamePreludeSyntax>(nameIndex, CssDiagnosticDescriptors.InvalidName, token.Text, ruleName, reason);

        EatToken();
        if (!AtEnd)
            return Fail<CssNamePreludeSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

        return new CssNamePreludeSyntax(token);
    }

    private CssImportPreludeSyntax? ParseImportPrelude(string ruleName)
    {
        var url = ParseUrl(ruleName);
        if (url is null)
            return null;

        GreenNode? layer = null;
        if (IsKeyword(_index, "layer"))
        {
            layer = new CssImportLayerSyntax(EatToken(), name: null, closeParenToken: null);
        }
        else if (CurrentKind == SyntaxKind.FunctionToken && CssIdentifier.EqualsIgnoreAsciiCase(Current.ValueText, "layer"))
        {
            var functionIndex = _index;
            var close = _match[functionIndex];
            if (close < 0 || close > _limit)
                return Fail<CssImportPreludeSyntax>(functionIndex, CssDiagnosticDescriptors.ExpectedToken, ")");

            var function = EatToken();
            var savedLimit = _limit;
            _limit = close;
            var name = ParseLayerName();
            if (name is null)
                return null;

            if (!AtEnd)
                return Fail<CssImportPreludeSyntax>(_index, CssDiagnosticDescriptors.InvalidLayerName, "unexpected '" + Current.Text + "'");

            _limit = savedLimit;
            layer = new CssImportLayerSyntax(function, name, EatToken());
        }

        GreenNode? supports = null;
        if (CurrentKind == SyntaxKind.FunctionToken && CssIdentifier.EqualsIgnoreAsciiCase(Current.ValueText, "supports"))
        {
            var functionIndex = _index;
            var close = _match[functionIndex];
            if (close < 0 || close > _limit)
                return Fail<CssImportPreludeSyntax>(functionIndex, CssDiagnosticDescriptors.ExpectedToken, ")");

            var function = EatToken();
            var savedLimit = _limit;
            _limit = close;
            GreenNode? condition;
            if (CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.ColonToken)
            {
                var declaration = ParseDeclaration(BlockContext.Unknown, FindDeclarationEnd(_index));
                condition = new CssDeclarationConditionSyntax(openParenToken: null, declaration, closeParenToken: null);
            }
            else
            {
                condition = ParseCondition(ConditionFlavor.Supports, allowOr: true);
            }

            if (condition is null)
                return null;

            if (!AtEnd)
                return Fail<CssImportPreludeSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

            _limit = savedLimit;
            supports = new CssImportSupportsSyntax(function, condition, EatToken());
        }

        var media = AtEnd ? null : ParseMediaQueryList();
        return new CssImportPreludeSyntax(url, layer, supports, media);
    }

    /// <summary>Parses the URL of <c>@import</c> or <c>@namespace</c>: a string, <c>url(...)</c>, or <c>src(...)</c>.</summary>
    private GreenNode? ParseUrl(string ruleName)
    {
        switch (CurrentKind)
        {
            case SyntaxKind.StringToken or SyntaxKind.UrlToken:
                return new CssTokenValueSyntax(EatToken());

            case SyntaxKind.FunctionToken when CssIdentifier.EqualsIgnoreAsciiCase(Current.ValueText, "url") || CssIdentifier.EqualsIgnoreAsciiCase(Current.ValueText, "src"):
                var close = _match[_index];
                if (close < 0 || close > _limit)
                    return Fail<GreenNode>(_index, CssDiagnosticDescriptors.ExpectedToken, ")");

                return ParseComponentValue();

            default:
                return Fail<GreenNode>(_index, CssDiagnosticDescriptors.InvalidPrelude, ruleName, "expected a URL or a string");
        }
    }

    private CssNamespacePreludeSyntax? ParseNamespacePrelude(string ruleName)
    {
        GreenToken? prefix = null;
        if (CurrentKind == SyntaxKind.IdentToken)
        {
            prefix = EatToken();
        }

        var url = ParseUrl(ruleName);
        if (url is null)
            return null;

        if (!AtEnd)
            return Fail<CssNamespacePreludeSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

        return new CssNamespacePreludeSyntax(prefix, url);
    }

    private CssCustomMediaPreludeSyntax? ParseCustomMediaPrelude(string ruleName)
    {
        if (CurrentKind != SyntaxKind.IdentToken || !SyntaxFacts.IsCustomPropertyName(Current.ValueText))
            return Fail<CssCustomMediaPreludeSyntax>(_index, CssDiagnosticDescriptors.InvalidName, Current.Text, ruleName, "expected a name starting with '--'");

        var name = EatToken();
        if (AtEnd)
            return Fail<CssCustomMediaPreludeSyntax>(_index, CssDiagnosticDescriptors.InvalidPrelude, ruleName, "expected a media query list, 'true', or 'false'");

        return new CssCustomMediaPreludeSyntax(name, ParseMediaQueryList());
    }

    private CssConditionPreludeSyntax? ParseSupportsPrelude()
    {
        var condition = ParseCondition(ConditionFlavor.Supports, allowOr: true);
        if (condition is null)
            return null;

        if (!AtEnd)
            return Fail<CssConditionPreludeSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

        return new CssConditionPreludeSyntax(condition);
    }

    private CssContainerPreludeSyntax? ParseContainerPrelude()
    {
        var items = new List<GreenNode?>();
        while (true)
        {
            GreenToken? name = null;
            if (CurrentKind == SyntaxKind.IdentToken && !IsKeyword(_index, "not"))
            {
                var value = Current.ValueText;
                if (SyntaxFacts.IsCssWideKeyword(value) || CssIdentifier.ToAsciiLowerCase(value) is "none" or "and" or "or")
                    return Fail<CssContainerPreludeSyntax>(_index, CssDiagnosticDescriptors.InvalidName, Current.Text, "container", "it is a reserved keyword");

                name = EatToken();
            }

            GreenNode? query = null;
            if (!AtEnd && CurrentKind != SyntaxKind.CommaToken)
            {
                query = ParseCondition(ConditionFlavor.Container, allowOr: true);
                if (query is null)
                    return null;
            }

            if (name is null && query is null)
                return Fail<CssContainerPreludeSyntax>(_index, CssDiagnosticDescriptors.InvalidPrelude, "container", "expected a container name or query");

            items.Add(new CssContainerConditionSyntax(name, query));
            if (AtEnd)
                break;

            if (CurrentKind != SyntaxKind.CommaToken)
                return Fail<CssContainerPreludeSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

            items.Add(EatToken());
        }

        return new CssContainerPreludeSyntax(SyntaxFactory.List(items));
    }

    private CssLayerPreludeSyntax? ParseLayerPrelude(string ruleName, bool hasBlock)
    {
        var items = new List<GreenNode?>();
        while (true)
        {
            var name = ParseLayerName();
            if (name is null)
                return null;

            items.Add(name);
            if (AtEnd)
                break;

            if (CurrentKind != SyntaxKind.CommaToken)
                return Fail<CssLayerPreludeSyntax>(_index, CssDiagnosticDescriptors.InvalidLayerName, "unexpected '" + Current.Text + "'");

            if (hasBlock)
                return Fail<CssLayerPreludeSyntax>(_index, CssDiagnosticDescriptors.InvalidPrelude, ruleName, "a layer block takes a single name");

            items.Add(EatToken());
        }

        return new CssLayerPreludeSyntax(SyntaxFactory.List(items));
    }

    /// <summary>Parses a layer name: identifiers joined by dots, with no whitespace between them.</summary>
    private CssLayerNameSyntax? ParseLayerName()
    {
        if (CurrentKind != SyntaxKind.IdentToken)
            return Fail<CssLayerNameSyntax>(_index, CssDiagnosticDescriptors.InvalidLayerName, AtEnd ? "expected a name" : "unexpected '" + Current.Text + "'");

        if (SyntaxFacts.IsCssWideKeyword(Current.ValueText))
            return Fail<CssLayerNameSyntax>(_index, CssDiagnosticDescriptors.InvalidLayerName, "'" + Current.Text + "' is a reserved keyword");

        var tokens = new List<GreenNode?> { EatToken() };
        while (CurrentKind == SyntaxKind.DotToken && !HasWhitespaceBefore(_index))
        {
            if (KindAt(_index + 1) != SyntaxKind.IdentToken || HasWhitespaceBefore(_index + 1))
                return Fail<CssLayerNameSyntax>(_index + 1, CssDiagnosticDescriptors.InvalidLayerName, "expected a name after '.'");

            tokens.Add(EatToken());
            tokens.Add(EatToken());
        }

        return new CssLayerNameSyntax(SyntaxFactory.TokenList(tokens));
    }

    private CssScopePreludeSyntax? ParseScopePrelude()
    {
        GreenNode? scopeStart = null;
        if (CurrentKind == SyntaxKind.OpenParenToken)
        {
            scopeStart = ParseScopeBoundary(relative: false);
            if (scopeStart is null)
                return null;
        }

        GreenToken? to = null;
        GreenNode? scopeEnd = null;
        if (IsKeyword(_index, "to"))
        {
            to = EatToken();
            if (CurrentKind != SyntaxKind.OpenParenToken)
                return Fail<CssScopePreludeSyntax>(_index, CssDiagnosticDescriptors.ExpectedToken, "(");

            scopeEnd = ParseScopeBoundary(relative: true);
            if (scopeEnd is null)
                return null;
        }

        if (!AtEnd)
            return Fail<CssScopePreludeSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

        return new CssScopePreludeSyntax(scopeStart, to, scopeEnd);
    }

    private CssScopeBoundarySyntax? ParseScopeBoundary(bool relative)
    {
        var openIndex = _index;
        var close = _match[openIndex];
        if (close < 0 || close > _limit)
            return Fail<CssScopeBoundarySyntax>(openIndex, CssDiagnosticDescriptors.ExpectedToken, ")");

        var open = EatToken();
        var savedLimit = _limit;
        _limit = close;
        var selectors = ParseSelectorList(relative ? SelectorListMode.Relative : SelectorListMode.Complex);
        if (selectors is null)
            return null;

        _limit = savedLimit;
        return new CssScopeBoundarySyntax(open, selectors, EatToken());
    }

    private CssKeyframeSelectorListSyntax? ParseKeyframeSelectorList()
    {
        var items = new List<GreenNode?>();
        while (true)
        {
            GreenToken? rangeName = null;
            var offsetIndex = _index;
            if (CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.PercentageToken)
            {
                if (CssIdentifier.ToAsciiLowerCase(Current.ValueText) is not ("cover" or "contain" or "entry" or "exit" or "entry-crossing" or "exit-crossing"))
                    return Fail<CssKeyframeSelectorListSyntax>(_index, CssDiagnosticDescriptors.InvalidKeyframeSelector, Current.Text);

                rangeName = EatToken();
                offsetIndex = _index;
            }

            var valid = CurrentKind switch
            {
                SyntaxKind.IdentToken => rangeName is null && CssIdentifier.ToAsciiLowerCase(Current.ValueText) is "from" or "to",
                SyntaxKind.PercentageToken => Current.GetValue() is double value && value is >= 0 and <= 100,
                _ => false,
            };

            if (!valid)
                return Fail<CssKeyframeSelectorListSyntax>(offsetIndex, CssDiagnosticDescriptors.InvalidKeyframeSelector, AtEnd ? "" : Current.Text);

            items.Add(new CssKeyframeSelectorSyntax(rangeName, EatToken()));
            if (AtEnd)
                break;

            if (CurrentKind != SyntaxKind.CommaToken)
                return Fail<CssKeyframeSelectorListSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

            items.Add(EatToken());
        }

        return new CssKeyframeSelectorListSyntax(SyntaxFactory.List(items));
    }

    private CssPageSelectorListSyntax? ParsePageSelectorList()
    {
        var items = new List<GreenNode?>();
        while (true)
        {
            GreenToken? name = null;
            if (CurrentKind == SyntaxKind.IdentToken)
            {
                name = EatToken();
            }

            var pseudoPages = new List<GreenNode?>();
            while (CurrentKind == SyntaxKind.ColonToken && (name is null && pseudoPages.Count == 0 || !HasWhitespaceBefore(_index)))
            {
                if (KindAt(_index + 1) != SyntaxKind.IdentToken || HasWhitespaceBefore(_index + 1))
                    return Fail<CssPageSelectorListSyntax>(_index + 1, CssDiagnosticDescriptors.InvalidPageSelector, "expected a pseudo-page name after ':'");

                if (CssIdentifier.ToAsciiLowerCase(_tokens[_index + 1].ValueText) is not ("first" or "left" or "right" or "blank"))
                    return Fail<CssPageSelectorListSyntax>(_index + 1, CssDiagnosticDescriptors.InvalidPageSelector, "unknown pseudo-page ':" + _tokens[_index + 1].Text + "'");

                pseudoPages.Add(new CssPseudoPageSyntax(EatToken(), EatToken()));
            }

            if (name is null && pseudoPages.Count == 0)
                return Fail<CssPageSelectorListSyntax>(_index, CssDiagnosticDescriptors.InvalidPageSelector, AtEnd ? "expected a page name or a pseudo-page" : "unexpected '" + Current.Text + "'");

            items.Add(new CssPageSelectorSyntax(name, SyntaxFactory.List(pseudoPages)));
            if (AtEnd)
                break;

            if (CurrentKind != SyntaxKind.CommaToken)
                return Fail<CssPageSelectorListSyntax>(_index, CssDiagnosticDescriptors.InvalidPageSelector, "unexpected '" + Current.Text + "'");

            items.Add(EatToken());
        }

        return new CssPageSelectorListSyntax(SyntaxFactory.List(items));
    }

    /// <summary>Parses the font families of <c>@font-feature-values</c>: strings, or identifiers that whitespace separates, between commas.</summary>
    private CssNameListSyntax? ParseFamilyNameList(string ruleName)
    {
        var tokens = new List<GreenNode?>();
        while (true)
        {
            if (CurrentKind == SyntaxKind.StringToken)
            {
                tokens.Add(EatToken());
            }
            else if (CurrentKind == SyntaxKind.IdentToken)
            {
                while (CurrentKind == SyntaxKind.IdentToken)
                {
                    tokens.Add(EatToken());
                }
            }
            else
            {
                return Fail<CssNameListSyntax>(_index, CssDiagnosticDescriptors.InvalidPrelude, ruleName, "expected a font family name");
            }

            if (AtEnd)
                break;

            if (CurrentKind != SyntaxKind.CommaToken)
                return Fail<CssNameListSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

            tokens.Add(EatToken());
        }

        return new CssNameListSyntax(SyntaxFactory.TokenList(tokens));
    }
}
