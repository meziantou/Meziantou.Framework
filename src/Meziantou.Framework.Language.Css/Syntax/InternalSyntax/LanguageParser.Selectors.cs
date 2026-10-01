using Meziantou.Framework.Language.Css.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>Selectors Level 4 and CSS Nesting.</summary>
/// <remarks>
/// Whitespace is trivia, so the descendant combinator is the absence of any other combinator between two compound
/// selectors that whitespace separates, and a compound selector ends at the first token whitespace precedes. A comment
/// is not whitespace: <c>a/**/.b</c> is one compound selector, as it is for a browser.
/// </remarks>
internal sealed partial class LanguageParser
{
    private enum SelectorListMode
    {
        /// <summary>A list of complex selectors, any one of which failing fails the list.</summary>
        Complex,

        /// <summary>A list of relative selectors, which may start with a combinator: a nested style rule, or <c>:has()</c>.</summary>
        Relative,

        /// <summary>A forgiving list, such as the argument of <c>:is()</c>, which keeps the selectors that do not parse as matching nothing.</summary>
        Forgiving,
    }

    private CssSelectorListSyntax? ParseSelectorList(SelectorListMode mode)
    {
        var items = new List<GreenNode?>();
        while (true)
        {
            if (mode == SelectorListMode.Forgiving)
            {
                items.Add(ParseForgivingSelector());
            }
            else
            {
                if (AtEnd || CurrentKind == SyntaxKind.CommaToken)
                    return Fail<CssSelectorListSyntax>(_index, CssDiagnosticDescriptors.ExpectedSelector);

                var selector = ParseComplexSelector(relative: mode == SelectorListMode.Relative);
                if (selector is null)
                    return null;

                items.Add(selector);
            }

            if (AtEnd)
                break;

            if (CurrentKind != SyntaxKind.CommaToken)
                return Fail<CssSelectorListSyntax>(_index, CssDiagnosticDescriptors.UnexpectedSelectorToken, Current.Text);

            items.Add(EatToken());
        }

        return new CssSelectorListSyntax(SyntaxFactory.List(items));
    }

    /// <summary>Parses an item of a forgiving selector list, keeping it as an invalid selector when it does not parse.</summary>
    private GreenNode ParseForgivingSelector()
    {
        var start = _index;
        var end = start;
        while (end < _limit && KindAt(end) != SyntaxKind.CommaToken)
        {
            end = SkipComponentValue(end);
        }

        if (start == end)
            return new CssInvalidSelectorSyntax(values: null);

        var point = Mark();
        _failure = null;
        _limit = end;
        var selector = ParseComplexSelector(relative: false);
        if (selector is not null && _index == end)
        {
            _limit = point.Limit;
            _failure = point.Failure;
            return selector;
        }

        var failure = _failure;
        Reset(point);

        var mark = _pending.Count;
        MarkSkipped(start);
        var reason = failure is { } pending ? Diagnostic.FormatMessage(pending.Descriptor.MessageFormat, pending.Arguments) : "Unexpected '" + _tokens[_index].Text + "'.";
        AddErrorSpan(start, end, CssDiagnosticDescriptors.IgnoredForgivingSelector, reason);
        var values = ParseComponentValues(end);
        return Finish(new CssInvalidSelectorSyntax(SyntaxFactory.List(values)), _starts[start], mark);
    }

    private CssComplexSelectorSyntax? ParseComplexSelector(bool relative)
    {
        var parts = new List<GreenNode?>();
        GreenNode? combinator = null;
        if (IsCombinatorStart())
        {
            if (!relative)
                return Fail<CssComplexSelectorSyntax>(_index, CssDiagnosticDescriptors.LeadingCombinator);

            combinator = ParseCombinator();
        }

        var compound = ParseCompoundSelector();
        if (compound is null)
            return null;

        parts.Add(new CssComplexSelectorPartSyntax(combinator, compound));
        while (!AtEnd && CurrentKind != SyntaxKind.CommaToken)
        {
            combinator = null;
            if (IsCombinatorStart())
            {
                combinator = ParseCombinator();
            }
            else if (!HasWhitespaceBefore(_index))
            {
                return Fail<CssComplexSelectorSyntax>(_index, CssDiagnosticDescriptors.UnexpectedSelectorToken, Current.Text);
            }

            compound = ParseCompoundSelector();
            if (compound is null)
                return null;

            parts.Add(new CssComplexSelectorPartSyntax(combinator, compound));
        }

        return new CssComplexSelectorSyntax(SyntaxFactory.List(parts));
    }

    private bool IsCombinatorStart() => CurrentKind switch
    {
        SyntaxKind.GreaterThanToken or SyntaxKind.PlusToken or SyntaxKind.TildeToken => true,
        SyntaxKind.BarToken => KindAt(_index + 1) == SyntaxKind.BarToken && !HasWhitespaceBefore(_index + 1),
        _ => false,
    };

    private CssCombinatorSyntax ParseCombinator()
    {
        var token = EatToken();
        return (SyntaxKind)token.RawKind switch
        {
            SyntaxKind.GreaterThanToken => new CssCombinatorSyntax(SyntaxKind.ChildCombinator, token, secondToken: null),
            SyntaxKind.PlusToken => new CssCombinatorSyntax(SyntaxKind.NextSiblingCombinator, token, secondToken: null),
            SyntaxKind.TildeToken => new CssCombinatorSyntax(SyntaxKind.SubsequentSiblingCombinator, token, secondToken: null),
            _ => new CssCombinatorSyntax(SyntaxKind.ColumnCombinator, token, EatToken()),
        };
    }

    private CssCompoundSelectorSyntax? ParseCompoundSelector()
    {
        var selectors = new List<GreenNode?>();
        var afterPseudoElement = false;
        while (!AtEnd && (selectors.Count == 0 || !HasWhitespaceBefore(_index)))
        {
            var kind = CurrentKind;
            if (kind == SyntaxKind.BarToken && KindAt(_index + 1) == SyntaxKind.BarToken && !HasWhitespaceBefore(_index + 1))
                break;

            if (afterPseudoElement && kind is SyntaxKind.IdentToken or SyntaxKind.AsteriskToken or SyntaxKind.BarToken or SyntaxKind.HashToken or SyntaxKind.DotToken or SyntaxKind.OpenBracketToken or SyntaxKind.AmpersandToken)
                return Fail<CssCompoundSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidCompoundOrder, "Only pseudo-classes and pseudo-elements can follow a pseudo-element.");

            GreenNode? selector;
            switch (kind)
            {
                case SyntaxKind.IdentToken or SyntaxKind.AsteriskToken or SyntaxKind.BarToken:
                    if (selectors.Count > 0)
                        return Fail<CssCompoundSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidCompoundOrder, "A type selector must come first in a compound selector.");

                    selector = ParseTypeSelector();
                    break;

                case SyntaxKind.HashToken:
                    if (!CssIdentifier.WouldStartIdentSequence(Current.Text, 1))
                        return Fail<CssCompoundSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidIdSelector, Current.Text);

                    selector = new CssIdSelectorSyntax(EatToken());
                    break;

                case SyntaxKind.DotToken:
                    if (KindAt(_index + 1) != SyntaxKind.IdentToken)
                        return Fail<CssCompoundSelectorSyntax>(_index + 1, CssDiagnosticDescriptors.ExpectedToken, "a class name");

                    if (HasWhitespaceBefore(_index + 1))
                        return Fail<CssCompoundSelectorSyntax>(_index + 1, CssDiagnosticDescriptors.WhitespaceNotAllowed, "after '.'");

                    selector = new CssClassSelectorSyntax(EatToken(), EatToken());
                    break;

                case SyntaxKind.OpenBracketToken:
                    selector = ParseAttributeSelector();
                    break;

                case SyntaxKind.ColonToken:
                    selector = ParsePseudoSelector(out var isPseudoElement);
                    afterPseudoElement |= isPseudoElement;
                    break;

                case SyntaxKind.AmpersandToken:
                    selector = new CssNestingSelectorSyntax(EatToken());
                    break;

                default:
                    goto Done;
            }

            if (selector is null)
                return null;

            selectors.Add(selector);
        }

    Done:
        if (selectors.Count == 0)
            return AtEnd ? Fail<CssCompoundSelectorSyntax>(_index, CssDiagnosticDescriptors.ExpectedSelector) : Fail<CssCompoundSelectorSyntax>(_index, CssDiagnosticDescriptors.UnexpectedSelectorToken, Current.Text);

        return new CssCompoundSelectorSyntax(SyntaxFactory.List(selectors));
    }

    /// <summary>Parses a type selector or the universal selector, with its namespace prefix: <c>a</c>, <c>*</c>, <c>svg|a</c>, <c>*|*</c>, <c>|a</c>.</summary>
    private GreenNode? ParseTypeSelector()
    {
        GreenNode? prefix = null;
        if (CurrentKind is SyntaxKind.IdentToken or SyntaxKind.AsteriskToken && IsAdjacentNamespaceBar(_index + 1))
        {
            if (!IsDeclaredNamespacePrefix(_index))
                return Fail<GreenNode>(_index, CssDiagnosticDescriptors.UndeclaredNamespacePrefix, Current.ValueText);

            prefix = new CssNamespacePrefixSyntax(EatToken(), EatToken());
        }
        else if (CurrentKind == SyntaxKind.BarToken)
        {
            if (!IsAdjacentName(_index + 1))
                return Fail<GreenNode>(_index, CssDiagnosticDescriptors.UnexpectedSelectorToken, "|");

            prefix = new CssNamespacePrefixSyntax(prefixToken: null, EatToken());
        }

        return CurrentKind == SyntaxKind.AsteriskToken ? new CssUniversalSelectorSyntax(prefix, EatToken()) : new CssTypeSelectorSyntax(prefix, EatToken());
    }

    /// <summary>Determines whether the namespace prefix at <paramref name="index"/> is <c>*</c>, or one a <c>@namespace</c> rule declares.</summary>
    private bool IsDeclaredNamespacePrefix(int index) => KindAt(index) == SyntaxKind.AsteriskToken || _namespacePrefixes.Contains(_tokens[index].ValueText);

    /// <summary>Determines whether a <c>|</c> at <paramref name="index"/> touches a name on both sides, making it a namespace separator.</summary>
    private bool IsAdjacentNamespaceBar(int index) => KindAt(index) == SyntaxKind.BarToken && !HasWhitespaceBefore(index) && IsAdjacentName(index + 1);

    private bool IsAdjacentName(int index) => KindAt(index) is SyntaxKind.IdentToken or SyntaxKind.AsteriskToken && !HasWhitespaceBefore(index);

    private CssAttributeSelectorSyntax? ParseAttributeSelector()
    {
        var openIndex = _index;
        var close = _match[openIndex];
        if (close < 0 || close > _limit)
            return Fail<CssAttributeSelectorSyntax>(openIndex, CssDiagnosticDescriptors.ExpectedToken, "]");

        var open = EatToken();
        var savedLimit = _limit;
        _limit = close;

        GreenNode? prefix = null;
        if (CurrentKind is SyntaxKind.IdentToken or SyntaxKind.AsteriskToken && KindAt(_index + 1) == SyntaxKind.BarToken && !HasWhitespaceBefore(_index + 1)
            && KindAt(_index + 2) == SyntaxKind.IdentToken && !HasWhitespaceBefore(_index + 2))
        {
            if (!IsDeclaredNamespacePrefix(_index))
                return Fail<CssAttributeSelectorSyntax>(_index, CssDiagnosticDescriptors.UndeclaredNamespacePrefix, Current.ValueText);

            prefix = new CssNamespacePrefixSyntax(EatToken(), EatToken());
        }
        else if (CurrentKind == SyntaxKind.BarToken && KindAt(_index + 1) == SyntaxKind.IdentToken && !HasWhitespaceBefore(_index + 1))
        {
            prefix = new CssNamespacePrefixSyntax(prefixToken: null, EatToken());
        }

        if (CurrentKind != SyntaxKind.IdentToken)
            return Fail<CssAttributeSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidAttributeSelector, "expected an attribute name");

        var name = EatToken();
        GreenToken? operatorPrefix = null;
        GreenToken? equals = null;
        GreenToken? value = null;
        GreenToken? modifier = null;
        if (!AtEnd)
        {
            if (CurrentKind is SyntaxKind.TildeToken or SyntaxKind.BarToken or SyntaxKind.CaretToken or SyntaxKind.DollarToken or SyntaxKind.AsteriskToken
                && KindAt(_index + 1) == SyntaxKind.EqualsToken && !HasWhitespaceBefore(_index + 1))
            {
                operatorPrefix = EatToken();
            }

            if (CurrentKind != SyntaxKind.EqualsToken)
                return Fail<CssAttributeSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidAttributeSelector, "expected '=', '~=', '|=', '^=', '$=', or '*='");

            equals = EatToken();
            if (CurrentKind is not (SyntaxKind.IdentToken or SyntaxKind.StringToken))
                return Fail<CssAttributeSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidAttributeSelector, "expected an identifier or a string");

            value = EatToken();
            if (!AtEnd)
            {
                if (CurrentKind != SyntaxKind.IdentToken || !(CssIdentifier.EqualsIgnoreAsciiCase(Current.ValueText, "i") || CssIdentifier.EqualsIgnoreAsciiCase(Current.ValueText, "s")))
                    return Fail<CssAttributeSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidAttributeSelector, "expected 'i', 's', or ']'");

                modifier = EatToken();
            }
        }

        if (!AtEnd)
            return Fail<CssAttributeSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidAttributeSelector, "expected ']'");

        _limit = savedLimit;
        return new CssAttributeSelectorSyntax(open, prefix, name, operatorPrefix, equals, value, modifier, EatToken());
    }

    private GreenNode? ParsePseudoSelector(out bool isPseudoElement)
    {
        isPseudoElement = false;
        var colon = EatToken();
        GreenToken? secondColon = null;
        if (CurrentKind == SyntaxKind.ColonToken && !HasWhitespaceBefore(_index))
        {
            secondColon = EatToken();
        }

        if (AtEnd)
            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.ExpectedToken, "a pseudo-class or pseudo-element name");

        if (HasWhitespaceBefore(_index))
            return Fail<GreenNode>(_index, CssDiagnosticDescriptors.WhitespaceNotAllowed, secondColon is null ? "after ':'" : "after '::'");

        var nameIndex = _index;
        switch (CurrentKind)
        {
            case SyntaxKind.IdentToken:
                var name = Current.ValueText;
                isPseudoElement = secondColon is not null || SyntaxFacts.IsLegacyPseudoElement(name);
                if (!SyntaxFacts.IsVendorPrefixed(name))
                {
                    if (secondColon is not null ? !SyntaxFacts.IsKnownPseudoElement(name) : !SyntaxFacts.IsKnownPseudoClass(name) && !SyntaxFacts.IsLegacyPseudoElement(name))
                    {
                        AddError(nameIndex, secondColon is not null ? CssDiagnosticDescriptors.UnknownPseudoElement : CssDiagnosticDescriptors.UnknownPseudoClass, name);
                    }
                }

                var kind = isPseudoElement ? SyntaxKind.PseudoElementSelector : SyntaxKind.PseudoClassSelector;
                return new CssPseudoSelectorSyntax(kind, colon, secondColon, EatToken());

            case SyntaxKind.FunctionToken:
                isPseudoElement = secondColon is not null;
                return ParseFunctionalPseudoSelector(colon, secondColon);

            default:
                return Fail<GreenNode>(_index, CssDiagnosticDescriptors.UnexpectedSelectorToken, Current.Text);
        }
    }

    private CssFunctionalPseudoSelectorSyntax? ParseFunctionalPseudoSelector(GreenToken colon, GreenToken? secondColon)
    {
        var functionIndex = _index;
        var close = _match[functionIndex];
        if (close < 0 || close > _limit)
            return Fail<CssFunctionalPseudoSelectorSyntax>(functionIndex, CssDiagnosticDescriptors.ExpectedToken, ")");

        if (IsTooDeep())
            return Fail<CssFunctionalPseudoSelectorSyntax>(functionIndex, CssDiagnosticDescriptors.NestingTooDeep, _depth);

        var function = EatToken();
        var name = CssIdentifier.ToAsciiLowerCase(function.ValueText);
        var isPseudoElement = secondColon is not null;
        var displayName = (isPseudoElement ? "::" : ":") + function.ValueText + "()";
        var savedLimit = _limit;
        _limit = close;
        _depth++;

        GreenNode? argument;
        if (isPseudoElement)
        {
            argument = name switch
            {
                "part" => ParseNameList(displayName, IsIdent, separator: SyntaxKind.None, minCount: 1, maxCount: int.MaxValue),
                "slotted" => ParseCompoundArgument(),
                "highlight" or "picker" => ParseNameList(displayName, IsIdent, separator: SyntaxKind.None, minCount: 1, maxCount: 1),
                "scroll-button" => ParseNameList(displayName, kind => kind is SyntaxKind.IdentToken or SyntaxKind.AsteriskToken, separator: SyntaxKind.None, minCount: 1, maxCount: 1),
                "cue" or "cue-region" => ParseSelectorList(SelectorListMode.Complex),
                "view-transition-group" or "view-transition-image-pair" or "view-transition-old" or "view-transition-new" => ParseViewTransitionPartSelector(displayName),
                _ => ParseUnknownPseudoArgument(functionIndex, isPseudoElement),
            };
        }
        else
        {
            argument = name switch
            {
                "is" or "where" => AtEnd ? null : ParseSelectorList(SelectorListMode.Forgiving),
                "not" or "current" or "past" or "future" => ParseSelectorList(SelectorListMode.Complex),
                "has" => ParseSelectorList(SelectorListMode.Relative),
                "nth-child" or "nth-last-child" => ParseNthArgument(displayName, allowOf: true),
                "nth-of-type" or "nth-last-of-type" or "nth-col" or "nth-last-col" => ParseNthArgument(displayName, allowOf: false),
                "host" or "host-context" => ParseCompoundArgument(),
                "lang" => ParseNameList(displayName, kind => kind is SyntaxKind.IdentToken or SyntaxKind.StringToken, SyntaxKind.CommaToken, minCount: 1, maxCount: int.MaxValue),
                "dir" or "state" => ParseNameList(displayName, IsIdent, separator: SyntaxKind.None, minCount: 1, maxCount: 1),
                "active-view-transition-type" => ParseNameList(displayName, IsIdent, SyntaxKind.CommaToken, minCount: 1, maxCount: int.MaxValue),
                "heading" => ParseNameList(displayName, kind => kind == SyntaxKind.NumberToken, SyntaxKind.CommaToken, minCount: 1, maxCount: int.MaxValue),
                _ => ParseUnknownPseudoArgument(functionIndex, isPseudoElement),
            };
        }

        if (_failure is not null && argument is null)
            return null;

        if (!AtEnd)
            return Fail<CssFunctionalPseudoSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "unexpected '" + Current.Text + "'");

        _depth--;
        _limit = savedLimit;
        var kind = isPseudoElement ? SyntaxKind.FunctionalPseudoElementSelector : SyntaxKind.FunctionalPseudoClassSelector;
        return new CssFunctionalPseudoSelectorSyntax(kind, colon, secondColon, function, argument, EatToken());

        static bool IsIdent(SyntaxKind kind) => kind == SyntaxKind.IdentToken;
    }

    private CssCompoundSelectorSyntax? ParseCompoundArgument()
    {
        if (AtEnd)
            return Fail<CssCompoundSelectorSyntax>(_index, CssDiagnosticDescriptors.ExpectedSelector);

        return ParseCompoundSelector();
    }

    /// <summary>Keeps the argument of a pseudo-class or pseudo-element this parser does not know as plain values, and reports the name.</summary>
    private CssGenericPreludeSyntax? ParseUnknownPseudoArgument(int functionIndex, bool isPseudoElement)
    {
        var name = _tokens[functionIndex].ValueText;
        if (!SyntaxFacts.IsVendorPrefixed(name))
        {
            AddError(functionIndex, isPseudoElement ? CssDiagnosticDescriptors.UnknownPseudoElement : CssDiagnosticDescriptors.UnknownPseudoClass, name + "()");
        }

        return ParseGenericPrelude(_limit);
    }

    /// <summary>Parses a list of single tokens, such as the idents of <c>::part(a b)</c> or the languages of <c>:lang(en, "fr")</c>.</summary>
    /// <param name="displayName">How to name the construct in a diagnostic.</param>
    /// <param name="isName">Which tokens the list holds.</param>
    /// <param name="separator">What separates them, or <see cref="SyntaxKind.None"/> for whitespace.</param>
    /// <param name="minCount">How many there must be at least.</param>
    /// <param name="maxCount">How many there may be at most.</param>
    private CssNameListSyntax? ParseNameList(string displayName, Func<SyntaxKind, bool> isName, SyntaxKind separator, int minCount, int maxCount)
    {
        var tokens = new List<GreenNode?>();
        var count = 0;
        while (!AtEnd)
        {
            if (count > 0 && separator != SyntaxKind.None)
            {
                if (CurrentKind != separator)
                    return Fail<CssNameListSyntax>(_index, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "expected '" + SyntaxFacts.GetText(separator) + "'");

                tokens.Add(EatToken());
            }

            if (!isName(CurrentKind))
                return Fail<CssNameListSyntax>(_index, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, AtEnd ? "expected a name" : "unexpected '" + Current.Text + "'");

            if (count > 0 && separator == SyntaxKind.None && !HasWhitespaceBefore(_index))
                return Fail<CssNameListSyntax>(_index, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "expected whitespace between names");

            tokens.Add(EatToken());
            count++;
            if (count > maxCount)
                return Fail<CssNameListSyntax>(_index - 1, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "too many names");
        }

        if (count < minCount)
            return Fail<CssNameListSyntax>(_index, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "expected a name");

        return new CssNameListSyntax(SyntaxFactory.TokenList(tokens));
    }

    /// <summary>Parses the argument of <c>:nth-child()</c> and the like: An+B, followed by <c>of</c> and a selector list where <paramref name="allowOf"/> says.</summary>
    private CssNthArgumentSyntax? ParseNthArgument(string displayName, bool allowOf)
    {
        var start = _index;
        var anPlusBEnd = _limit;
        for (var i = start; i < _limit; i = SkipComponentValue(i))
        {
            if (KindAt(i) == SyntaxKind.IdentToken && CssIdentifier.EqualsIgnoreAsciiCase(_tokens[i].ValueText, "of"))
            {
                anPlusBEnd = i;
                break;
            }
        }

        if (!TryReadAnPlusB(start, anPlusBEnd, out _, out _))
            return Fail<CssNthArgumentSyntax>(start, CssDiagnosticDescriptors.InvalidAnPlusB, TextOf(start, anPlusBEnd));

        var tokens = new List<GreenNode?>();
        while (_index < anPlusBEnd)
        {
            tokens.Add(EatToken());
        }

        var anPlusB = new CssAnPlusBSyntax(SyntaxFactory.TokenList(tokens));
        if (AtEnd)
            return new CssNthArgumentSyntax(anPlusB, ofKeyword: null, selectors: null);

        if (!allowOf)
            return Fail<CssNthArgumentSyntax>(_index, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "'of' is only allowed in ':nth-child()' and ':nth-last-child()'");

        var of = EatToken();
        var selectors = ParseSelectorList(SelectorListMode.Complex);
        return selectors is null ? null : new CssNthArgumentSyntax(anPlusB, of, selectors);
    }

    /// <summary>Reads the tokens from <paramref name="start"/> up to <paramref name="end"/> as An+B.</summary>
    private bool TryReadAnPlusB(int start, int end, out int a, out int b)
    {
        var tokens = new AnPlusB.Token[end - start];
        for (var i = start; i < end; i++)
        {
            var token = _tokens[i];
            var kind = KindAt(i);
            if (SyntaxFacts.GetClosingKind(kind) != SyntaxKind.None)
            {
                a = 0;
                b = 0;
                return false;
            }

            tokens[i - start] = AnPlusB.CreateToken(kind, token.Text, token.ValueText, token.GetValue(), HasWhitespaceBefore(i) && i > start);
        }

        return AnPlusB.TryParse(tokens, out a, out b);
    }

    /// <summary>Parses the argument of <c>::view-transition-group()</c> and the like: <c>*</c>, a name, and classes, as in <c>card.large</c>.</summary>
    private CssViewTransitionPartSelectorSyntax? ParseViewTransitionPartSelector(string displayName)
    {
        GreenToken? name = null;
        if (CurrentKind is SyntaxKind.IdentToken or SyntaxKind.AsteriskToken)
        {
            name = EatToken();
        }

        var classes = new List<GreenNode?>();
        while (!AtEnd && CurrentKind == SyntaxKind.DotToken && (name is null && classes.Count == 0 || !HasWhitespaceBefore(_index)))
        {
            if (KindAt(_index + 1) != SyntaxKind.IdentToken || HasWhitespaceBefore(_index + 1))
                return Fail<CssViewTransitionPartSelectorSyntax>(_index + 1, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "expected a class name");

            classes.Add(new CssClassSelectorSyntax(EatToken(), EatToken()));
        }

        if (name is null && classes.Count == 0)
            return Fail<CssViewTransitionPartSelectorSyntax>(_index, CssDiagnosticDescriptors.InvalidPseudoArgument, displayName, "expected '*', a name, or a class");

        return new CssViewTransitionPartSelectorSyntax(name, SyntaxFactory.List(classes));
    }
}
