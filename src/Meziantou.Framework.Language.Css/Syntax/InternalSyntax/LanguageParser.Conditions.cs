using Meziantou.Framework.Language.Css.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>Media queries, and the boolean conditions media, supports, and container queries share.</summary>
/// <remarks>
/// Each grammar ends a parenthesized condition with <c>&lt;general-enclosed&gt;</c>: anything in parentheses, or any
/// function, is valid and evaluates to false. So a condition that does not parse as a feature is not an error that
/// drops the rule, but it is reported as a warning, since it can never be true.
/// </remarks>
internal sealed partial class LanguageParser
{
    private enum ConditionFlavor
    {
        Media,
        Supports,
        Container,
        Style,
        ScrollState,
    }

    private CssMediaQueryListSyntax ParseMediaQueryList()
    {
        var items = new List<GreenNode?>();
        while (true)
        {
            var start = _index;
            var end = start;
            while (end < _limit && KindAt(end) != SyntaxKind.CommaToken)
            {
                end = SkipComponentValue(end);
            }

            var point = Mark();
            _failure = null;
            _limit = end;
            var query = start == end ? null : ParseMediaQuery();
            if (query is not null && _index == end)
            {
                _limit = point.Limit;
                _failure = point.Failure;
                items.Add(query);
            }
            else
            {
                // An invalid query matches nothing, but leaves the other queries of the list, and the rule, alone.
                var failure = _failure;
                Reset(point);
                var mark = _pending.Count;
                var reason = failure is { } pending ? Diagnostic.FormatMessage(pending.Descriptor.MessageFormat, pending.Arguments).TrimEnd('.') : "expected a media query";
                MarkSkipped(start);
                AddErrorSpan(start, end, CssDiagnosticDescriptors.InvalidMediaQuery, reason);
                var values = ParseComponentValues(end);

                // An empty query has no text to carry the diagnostic, which the list carries instead.
                GreenNode invalid = new CssInvalidMediaQuerySyntax(SyntaxFactory.List(values));
                items.Add(values.Count == 0 ? invalid : Finish(invalid, _starts[start], mark));
            }

            if (AtEnd)
                break;

            items.Add(EatToken());
        }

        return new CssMediaQueryListSyntax(SyntaxFactory.List(items));
    }

    private GreenNode? ParseMediaQuery()
    {
        if (CurrentKind is SyntaxKind.OpenParenToken or SyntaxKind.FunctionToken
            || (IsKeyword(_index, "not") && KindAt(_index + 1) is SyntaxKind.OpenParenToken or SyntaxKind.FunctionToken))
        {
            var condition = ParseCondition(ConditionFlavor.Media, allowOr: true);
            return condition is null ? null : new CssMediaConditionQuerySyntax(condition);
        }

        GreenToken? modifier = null;
        if (IsKeyword(_index, "not") || IsKeyword(_index, "only"))
        {
            modifier = EatToken();
        }

        if (CurrentKind != SyntaxKind.IdentToken)
            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.ExpectedToken, "a media type");

        if (IsKeyword(_index, "only") || IsKeyword(_index, "not") || IsKeyword(_index, "and") || IsKeyword(_index, "or") || IsKeyword(_index, "layer"))
            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

        var type = EatToken();
        if (AtEnd)
            return new CssMediaTypeQuerySyntax(modifier, type, andKeyword: null, condition: null);

        if (!IsKeyword(_index, "and"))
            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

        var and = EatToken();
        var typeCondition = ParseCondition(ConditionFlavor.Media, allowOr: false);
        return typeCondition is null ? null : new CssMediaTypeQuerySyntax(modifier, type, and, typeCondition);
    }

    /// <summary>Parses a condition: <c>not</c> a condition in parentheses, or conditions in parentheses joined by <c>and</c> or by <c>or</c>.</summary>
    /// <param name="flavor">Which grammar the conditions in parentheses follow.</param>
    /// <param name="allowOr">Whether <c>or</c> may join the conditions, which it may not after a media type.</param>
    private GreenNode? ParseCondition(ConditionFlavor flavor, bool allowOr)
    {
        if (IsKeyword(_index, "not"))
        {
            var not = EatToken();
            var operand = ParseConditionInParens(flavor);
            return operand is null ? null : new CssNotConditionSyntax(not, operand);
        }

        var first = ParseConditionInParens(flavor);
        if (first is null || AtEnd)
            return first;

        var keyword = IsKeyword(_index, "and") ? "and" : IsKeyword(_index, "or") ? "or" : null;
        if (keyword is null)
            return first;

        if (keyword == "or" && !allowOr)
            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.InvalidCondition, "'or' cannot follow a media type");

        var other = keyword == "and" ? "or" : "and";
        var items = new List<GreenNode?> { first };
        while (!AtEnd)
        {
            if (IsKeyword(_index, other))
                return Fail<GreenNode>(_index, CssDiagnosticDescriptors.MixedAndOr);

            if (!IsKeyword(_index, keyword))
                break;

            items.Add(EatToken());
            var operand = ParseConditionInParens(flavor);
            if (operand is null)
                return null;

            items.Add(operand);
        }

        return new CssConditionChainSyntax(keyword == "and" ? SyntaxKind.AndCondition : SyntaxKind.OrCondition, SyntaxFactory.List(items));
    }

    private GreenNode? ParseConditionInParens(ConditionFlavor flavor)
    {
        var openIndex = _index;
        switch (CurrentKind)
        {
            case SyntaxKind.OpenParenToken:
            {
                var close = _match[openIndex];
                if (close < 0 || close > _limit)
                    return Fail<GreenNode>(openIndex, CssDiagnosticDescriptors.ExpectedToken, ")");

                if (IsTooDeep())
                    return Fail<GreenNode>(openIndex, CssDiagnosticDescriptors.NestingTooDeep, _depth);

                // A nested condition, as in "((a) and (b))", or "(not (a))".
                PendingDiagnostic? nestedFailure = null;
                if (KindAt(openIndex + 1) is SyntaxKind.OpenParenToken or SyntaxKind.FunctionToken || IsKeyword(openIndex + 1, "not"))
                {
                    var point = Mark();
                    _failure = null;
                    var open = EatToken();
                    _limit = close;
                    _depth++;
                    var condition = ParseCondition(flavor, allowOr: true);
                    if (condition is not null && AtEnd)
                    {
                        _depth--;
                        _limit = point.Limit;
                        _failure = point.Failure;
                        return new CssParenthesizedConditionSyntax(open, condition, EatToken());
                    }

                    nestedFailure = _failure;
                    Reset(point);
                }

                // A feature, as in "(min-width: 600px)" or "(display: grid)".
                var featurePoint = Mark();
                _failure = null;
                var feature = ParseFeatureInParens(flavor, close);
                if (feature is not null)
                {
                    _failure = featurePoint.Failure;
                    return feature;
                }

                var featureFailure = _failure;
                Reset(featurePoint);
                return ParseGeneralEnclosed(nestedFailure ?? featureFailure, reportFailure: true);
            }

            case SyntaxKind.FunctionToken:
            {
                var close = _match[openIndex];
                if (close < 0 || close > _limit)
                    return Fail<GreenNode>(openIndex, CssDiagnosticDescriptors.ExpectedToken, ")");

                if (IsTooDeep())
                    return Fail<GreenNode>(openIndex, CssDiagnosticDescriptors.NestingTooDeep, _depth);

                var kind = GetConditionFunctionKind(flavor, Current.ValueText);
                if (kind == SyntaxKind.None)
                    return ParseGeneralEnclosed(failure: null, reportFailure: false);

                var point = Mark();
                _failure = null;
                var function = EatToken();
                _limit = close;
                _depth++;
                var argument = ParseConditionFunctionArgument(kind);
                if (argument is not null && AtEnd)
                {
                    _depth--;
                    _limit = point.Limit;
                    _failure = point.Failure;
                    return new CssConditionFunctionSyntax(kind, function, argument, EatToken());
                }

                var failure = _failure ?? (AtEnd ? null : new PendingDiagnostic(TextStart(_index), Current.Text.Length, CssDiagnosticDescriptors.UnexpectedToken, [Current.Text]));
                Reset(point);
                return ParseGeneralEnclosed(failure, reportFailure: true);
            }

            default:
                return AtEnd
                    ? Fail<GreenNode>(_index, CssDiagnosticDescriptors.InvalidCondition, "expected a condition in parentheses")
                    : Fail<GreenNode>(_index, CssDiagnosticDescriptors.InvalidCondition, "expected '(' rather than '" + Current.Text + "'");
        }
    }

    /// <summary>Keeps a parenthesized condition or a function as <c>&lt;general-enclosed&gt;</c>, which is always false.</summary>
    /// <param name="failure">Why it did not parse as anything more specific.</param>
    /// <param name="reportFailure">Whether to report it: whether it looks like something more specific that went wrong.</param>
    private CssGeneralEnclosedSyntax ParseGeneralEnclosed(PendingDiagnostic? failure, bool reportFailure)
    {
        var start = _index;
        var mark = _pending.Count;
        if (reportFailure && failure is { } pending)
        {
            var reason = Diagnostic.FormatMessage(pending.Descriptor.MessageFormat, pending.Arguments);
            _pending.Add(new PendingDiagnostic(TextStart(start), TextEnd(SkipComponentValue(start) - 1) - TextStart(start), CssDiagnosticDescriptors.GeneralEnclosedCondition, [reason]));
        }

        var value = ParseComponentValue();
        return (CssGeneralEnclosedSyntax)Finish(new CssGeneralEnclosedSyntax(value), _starts[start], mark);
    }

    private static SyntaxKind GetConditionFunctionKind(ConditionFlavor flavor, string name)
    {
        var lower = CssIdentifier.ToAsciiLowerCase(name);
        return flavor switch
        {
            ConditionFlavor.Supports => lower switch
            {
                "selector" => SyntaxKind.SelectorFunction,
                "font-tech" => SyntaxKind.FontTechFunction,
                "font-format" => SyntaxKind.FontFormatFunction,
                "at-rule" => SyntaxKind.AtRuleFunction,
                _ => SyntaxKind.None,
            },
            ConditionFlavor.Container => lower switch
            {
                "style" => SyntaxKind.StyleQuery,
                "scroll-state" => SyntaxKind.ScrollStateQuery,
                _ => SyntaxKind.None,
            },
            _ => SyntaxKind.None,
        };
    }

    private GreenNode? ParseConditionFunctionArgument(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.SelectorFunction:
                if (AtEnd)
                    return Fail<GreenNode>(_index, CssDiagnosticDescriptors.ExpectedSelector);

                return ParseComplexSelector(relative: false);

            case SyntaxKind.FontTechFunction:
                return ParseNameList("font-tech()", kind => kind == SyntaxKind.IdentToken, SyntaxKind.None, minCount: 1, maxCount: 1);

            case SyntaxKind.FontFormatFunction:
                return ParseNameList("font-format()", kind => kind is SyntaxKind.IdentToken or SyntaxKind.StringToken, SyntaxKind.None, minCount: 1, maxCount: 1);

            case SyntaxKind.AtRuleFunction:
                return ParseNameList("at-rule()", kind => kind == SyntaxKind.AtKeywordToken, SyntaxKind.None, minCount: 1, maxCount: 1);

            case SyntaxKind.StyleQuery:
                return ParseFeatureOrCondition(ConditionFlavor.Style);

            default:
                return ParseFeatureOrCondition(ConditionFlavor.ScrollState);
        }
    }

    /// <summary>Parses the argument of <c>style()</c> or <c>scroll-state()</c>: a single feature without parentheses, or a condition.</summary>
    private GreenNode? ParseFeatureOrCondition(ConditionFlavor flavor)
    {
        if (CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.ColonToken)
        {
            if (flavor == ConditionFlavor.Style)
            {
                var declaration = ParseDeclaration(BlockContext.Unknown, FindDeclarationEnd(_index));
                return AtEnd ? new CssDeclarationConditionSyntax(openParenToken: null, declaration, closeParenToken: null) : null;
            }

            var name = EatToken();
            var colon = EatToken();
            var value = ParseFeatureValue();
            return value is null ? null : new CssPlainFeatureSyntax(openParenToken: null, name, colon, value, closeParenToken: null);
        }

        if (CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.EndOfFileToken && !IsKeyword(_index, "not"))
            return new CssBooleanFeatureSyntax(openParenToken: null, EatToken(), closeParenToken: null);

        if (AtEnd)
            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.InvalidCondition, "expected a feature or a condition");

        return ParseCondition(flavor, allowOr: true);
    }

    /// <summary>Parses what a parenthesized condition holds when it is not a nested condition: a feature, or a declaration.</summary>
    private GreenNode? ParseFeatureInParens(ConditionFlavor flavor, int close)
    {
        var open = EatToken();
        var savedLimit = _limit;
        _limit = close;
        if (flavor is ConditionFlavor.Supports or ConditionFlavor.Style)
        {
            if (CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.ColonToken)
            {
                var declaration = ParseDeclaration(BlockContext.Unknown, FindDeclarationEnd(_index));
                if (!AtEnd)
                    return Fail<GreenNode>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

                _limit = savedLimit;
                return new CssDeclarationConditionSyntax(open, declaration, EatToken());
            }

            if (flavor == ConditionFlavor.Style && CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.EndOfFileToken)
            {
                var name = EatToken();
                _limit = savedLimit;
                return new CssBooleanFeatureSyntax(open, name, EatToken());
            }

            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.InvalidCondition, "expected a declaration");
        }

        if (CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.ColonToken)
        {
            var name = EatToken();
            var colon = EatToken();
            var value = ParseFeatureValue();
            if (value is null)
                return null;

            _limit = savedLimit;
            return new CssPlainFeatureSyntax(open, name, colon, value, EatToken());
        }

        if (CurrentKind == SyntaxKind.IdentToken && KindAt(_index + 1) == SyntaxKind.EndOfFileToken)
        {
            var name = EatToken();
            _limit = savedLimit;
            return new CssBooleanFeatureSyntax(open, name, EatToken());
        }

        return ParseRangeFeature(open, savedLimit);
    }

    /// <summary>Parses a range feature, such as <c>(width &gt;= 600px)</c> or <c>(400px &lt;= width &lt; 800px)</c>, whose opening parenthesis is <paramref name="open"/>.</summary>
    /// <param name="open">The opening parenthesis.</param>
    /// <param name="outerLimit">The end of the range outside the parentheses, which the closing parenthesis is read with.</param>
    private CssRangeFeatureSyntax? ParseRangeFeature(GreenToken open, int outerLimit)
    {
        var leftIndex = _index;
        var left = ParseFeatureValue();
        if (left is null)
            return null;

        var leftComparison = ParseComparison();
        if (leftComparison is null)
            return null;

        var middleIndex = _index;
        var middle = ParseFeatureValue();
        if (middle is null)
            return null;

        GreenNode? rightComparison = null;
        GreenNode? right = null;
        if (!AtEnd)
        {
            rightComparison = ParseComparison();
            if (rightComparison is null)
                return null;

            right = ParseFeatureValue();
            if (right is null)
                return null;

            if (!AtEnd)
                return Fail<CssRangeFeatureSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

            // "a < name < b" and "a > name > b" are the only ranges with two comparisons.
            if (!IsSingleIdent(middleIndex, middle))
                return Fail<CssRangeFeatureSyntax>(middleIndex, CssDiagnosticDescriptors.InvalidRange, "expected a feature name between the two comparisons");

            var first = leftComparison.Kind;
            var second = ((CssComparisonSyntax)rightComparison).Kind;
            var bothLess = first is SyntaxKind.LessThanComparison or SyntaxKind.LessThanOrEqualComparison && second is SyntaxKind.LessThanComparison or SyntaxKind.LessThanOrEqualComparison;
            var bothGreater = first is SyntaxKind.GreaterThanComparison or SyntaxKind.GreaterThanOrEqualComparison && second is SyntaxKind.GreaterThanComparison or SyntaxKind.GreaterThanOrEqualComparison;
            if (!bothLess && !bothGreater)
                return Fail<CssRangeFeatureSyntax>(middleIndex, CssDiagnosticDescriptors.InvalidRange, "both comparisons must point the same way, and neither can be '='");
        }
        else if (!IsSingleIdent(leftIndex, left) && !IsSingleIdent(middleIndex, middle))
        {
            return Fail<CssRangeFeatureSyntax>(leftIndex, CssDiagnosticDescriptors.InvalidRange, "expected a feature name");
        }

        _limit = outerLimit;
        return new CssRangeFeatureSyntax(open, left, leftComparison, middle, rightComparison, right, EatToken());
    }

    private bool IsSingleIdent(int index, CssFeatureValueSyntax value) => KindAt(index) == SyntaxKind.IdentToken && value.FullWidth == _tokens[index].FullWidth;

    /// <summary>Parses the value of a feature: a single component value, or a ratio such as <c>16 / 9</c>.</summary>
    private CssFeatureValueSyntax? ParseFeatureValue()
    {
        if (AtEnd || IsComparisonStart())
            return Fail<CssFeatureValueSyntax>(_index, CssDiagnosticDescriptors.InvalidCondition, "expected a value");

        var values = new List<GreenNode?> { ParseComponentValue() };
        if (CurrentKind == SyntaxKind.SlashToken)
        {
            values.Add(new CssTokenValueSyntax(EatToken()));
            if (AtEnd || IsComparisonStart())
                return Fail<CssFeatureValueSyntax>(_index, CssDiagnosticDescriptors.InvalidCondition, "expected the second number of a ratio");

            values.Add(ParseComponentValue());
        }

        if (!AtEnd && !IsComparisonStart())
            return Fail<CssFeatureValueSyntax>(_index, CssDiagnosticDescriptors.UnexpectedToken, Current.Text);

        return new CssFeatureValueSyntax(SyntaxFactory.List(values));
    }

    private bool IsComparisonStart() => CurrentKind is SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken or SyntaxKind.EqualsToken;

    private CssComparisonSyntax? ParseComparison()
    {
        var kind = CurrentKind;
        if (kind == SyntaxKind.EqualsToken)
            return new CssComparisonSyntax(SyntaxKind.EqualComparison, EatToken(), equalsToken: null);

        if (kind is not (SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken))
            return Fail<CssComparisonSyntax>(_index, CssDiagnosticDescriptors.InvalidRange, AtEnd ? "expected '<', '>', or '='" : "expected '<', '>', or '=' rather than '" + Current.Text + "'");

        var operatorToken = EatToken();
        GreenToken? equals = null;
        if (CurrentKind == SyntaxKind.EqualsToken && !HasWhitespaceBefore(_index))
        {
            equals = EatToken();
        }

        var comparison = (kind, equals is not null) switch
        {
            (SyntaxKind.LessThanToken, false) => SyntaxKind.LessThanComparison,
            (SyntaxKind.LessThanToken, true) => SyntaxKind.LessThanOrEqualComparison,
            (_, false) => SyntaxKind.GreaterThanComparison,
            _ => SyntaxKind.GreaterThanOrEqualComparison,
        };

        return new CssComparisonSyntax(comparison, operatorToken, equals);
    }

    /// <summary>Determines whether the token at <paramref name="index"/> is the identifier <paramref name="keyword"/>, ignoring ASCII case.</summary>
    private bool IsKeyword(int index, string keyword) => KindAt(index) == SyntaxKind.IdentToken && CssIdentifier.EqualsIgnoreAsciiCase(_tokens[index].ValueText, keyword);
}
