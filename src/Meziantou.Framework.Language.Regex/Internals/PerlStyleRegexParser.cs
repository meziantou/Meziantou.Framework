// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
//
// Changes: ScanRegex and ScanGroupOpen build a round-trippable concrete syntax tree instead of a RegexNode tree, they
// record diagnostics instead of throwing, and they perform no reductions, no case folding, and no set construction.

using System.Globalization;

namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The Perl-derived grammar, parameterized by what the flavor supports.</summary>
/// <remarks>
/// Ported from the .NET engine, which is the most complete of the Perl-derived grammars, and then narrowed by feature
/// flags for the flavors that have less. The alternative, a parser per flavor, would have four copies of the same
/// escape and character-class handling and four places for them to drift apart.
/// </remarks>
internal abstract partial class PerlStyleRegexParser : RegexParser
{
    /// <summary>Set while reading the condition of a conditional, so its parentheses do not take a capture number.</summary>
    private bool _ignoreNextParen;

    /// <summary>
    /// Set while reading the group that is the test of an expression conditional, where inline options are not
    /// recognized.
    /// </summary>
    /// <remarks>
    /// The engine expresses this as "the group is an expression conditional that has no children yet", which is true
    /// of exactly one construct: the parenthesis the conditional rewound to. So <c>(?(?n)a|b)</c> is an invalid
    /// grouping construct rather than an option setter, while <c>(?(name)(?n))</c>, whose <c>(?n)</c> comes after the
    /// test, is fine.
    /// </remarks>
    private bool _inConditionalTest;

    private int _autocap = 1;

    /// <summary>Takes the next capture number without noting it.</summary>
    protected override int NextAutoCapture() => _autocap++;

    protected PerlStyleRegexParser(string text, RegexParseOptions parseOptions)
        : base(text, parseOptions)
    {
    }

    protected override RegexAtomSyntax ParseAtom(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;

        // The group opener is asked for by length rather than matched as a character, because a POSIX basic
        // expression spells it "\(".
        var groupOpen = GroupOpenLength(start);
        if (groupOpen > 0)
            return ParseGroup(leadingTrivia, groupOpen);

        switch (Scanner.Current)
        {
            case '[':
                return ParseCharacterClass(leadingTrivia);

            case '\\':
                return ParseBackslashAtom(leadingTrivia);

            case '^':
            case '$':
                Scanner.Position++;
                return WithOptions(new RegexAnchorSyntax(Scanner.Token(RegexSyntaxKind.AnchorToken, start, leadingTrivia)));

            case '.':
                Scanner.Position++;
                return WithOptions(new RegexAnyCharacterSyntax(Scanner.Token(RegexSyntaxKind.DotToken, start, leadingTrivia)));

            case ')' when GroupCloseLength(start) > 0:
                return SkipOneCharacter(leadingTrivia, RegexDiagnosticIds.InsufficientOpeningParentheses, "Unmatched ')'.");

            // Only a character that is a quantifier in this flavor can be one with nothing to repeat. In a basic
            // expression "+" and "?" are ordinary characters, so reporting them here would invent an error.
            case '*':
            case '+' or '?' when Flavor.HasFeature(RegexFlavorFeatures.PlusAndQuestionQuantifiers):
                return SkipOneCharacter(leadingTrivia, RegexDiagnosticIds.QuantifierAfterNothing, $"Quantifier '{Scanner.Current}' has nothing to repeat.");

            case '{' when IsQuantifierAt(Scanner.Position):
                return SkipOneCharacter(leadingTrivia, RegexDiagnosticIds.QuantifierAfterNothing, "Quantifier '{' has nothing to repeat.");

            default:
                Scanner.Position++;

                // In Unicode mode a pattern is a sequence of code points, so a surrogate pair is one atom and a
                // quantifier after it repeats the whole character rather than half of one.
                if (UsesUnicodeMode && char.IsHighSurrogate(Text[start]) && char.IsLowSurrogate(Scanner.Current))
                {
                    Scanner.Position++;
                }

                return WithOptions(new RegexLiteralSyntax(Scanner.Token(RegexSyntaxKind.LiteralToken, start, leadingTrivia)));
        }
    }

    /// <summary>Parses a parenthesized construct, from <c>(</c> through the matching <c>)</c>.</summary>
    /// <remarks>
    /// Ported from <c>ScanGroupOpen</c>. Every path that construct reached by <c>goto BreakRecognize</c> becomes a
    /// diagnostic plus a node that still covers the text, and every character it consumed in one step is kept as its
    /// own token so the parts of a header are addressable.
    /// </remarks>
    private RegexAtomSyntax ParseGroup(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia, int openLength)
    {
        var start = Scanner.Position;
        if (!TryEnterRecursion(new TextSpan(start, 1)))
            return ConsumeRestAsText(start, leadingTrivia);

        try
        {
            Scanner.Position += openLength;
            var openParenToken = Scanner.Token(RegexSyntaxKind.OpenParenToken, start, leadingTrivia);
            OptionsStack.Push(Options);

            // The flag applies to this parenthesis only, never to anything nested inside it.
            var inConditionalTest = _inConditionalTest;
            _inConditionalTest = false;

            // A backtracking verb is "(*NAME)", so it is decided before the "(?" headers are looked at.
            if (Scanner.Current == '*' && Flavor.HasFeature(RegexFlavorFeatures.BacktrackingVerbs))
                return ParseBacktrackingVerb(openParenToken);

            // "(" at the end, "(x" where x is not "?", and "(?)" are all plain groups. The "?" of "(?)" is left where
            // it is on purpose: the engine leaves it too, and the body parse then reports it as a quantifier with
            // nothing to repeat. A flavor without the "(?…)" family treats every one of them the same way.
            if (Scanner.IsAtEnd || Scanner.Current != '?' || Scanner.Peek() == ')' ||
                !Flavor.HasFeature(RegexFlavorFeatures.ExtendedGroupSyntax))
            {
                return ParsePlainGroup(openParenToken);
            }

            var questionStart = Scanner.Position;
            Scanner.Position++;

            switch (Scanner.Current)
            {
                case ':':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, RegexSyntaxKind.NonCapturingGroup, RegexFlavorFeatures.NonCapturingGroups);

                case '=':
                case '!':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, RegexSyntaxKind.Lookaround, RegexFlavorFeatures.Lookahead);

                case '>':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, RegexSyntaxKind.AtomicGroup, RegexFlavorFeatures.AtomicGroups);

                case '|':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, RegexSyntaxKind.BranchResetGroup, RegexFlavorFeatures.BranchReset);

                case '<':
                case '\'':
                    return ParseAngledGroup(openParenToken, questionStart);

                case 'P' when Flavor.HasFeature(RegexFlavorFeatures.PythonNamedGroups) && Scanner.Peek() == '<':
                    return ParseAngledGroup(openParenToken, questionStart);

                case '(':
                    return ParseConditional(openParenToken, questionStart);

                case 'R':
                case >= '0' and <= '9' when Flavor.HasFeature(RegexFlavorFeatures.Recursion):
                    if (Flavor.HasFeature(RegexFlavorFeatures.Recursion))
                        return ParseRecursion(openParenToken, questionStart);

                    goto default;

                default:
                    return TryParseFlavorGroupHeader(openParenToken, questionStart)
                        ?? ParseOptionsConstruct(openParenToken, questionStart, inConditionalTest);
            }
        }
        finally
        {
            ExitRecursion();
        }
    }

    /// <summary>Parses a <c>(?…</c> header that only some flavors have, or returns null to fall through.</summary>
    protected virtual RegexAtomSyntax? TryParseFlavorGroupHeader(RegexSyntaxToken openParenToken, int questionStart) => null;

    /// <summary>Parses a backslash escape that only some flavors have, or returns null to fall through.</summary>
    /// <remarks>The reading position is on the backslash.</remarks>
    protected virtual RegexAtomSyntax? TryParseFlavorEscape(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia) => null;

    /// <summary>Whether the letter after a backslash names a shorthand character class at the atom level.</summary>
    protected virtual bool IsShorthandClassLetter(char letter) => IsCoreShorthandClassLetter(letter);

    /// <summary>Whether the letter after a backslash names a shorthand character class inside a class.</summary>
    /// <remarks>
    /// Deliberately not delegating to <see cref="IsShorthandClassLetter"/>: an override that widens one by calling the
    /// other would then call back into itself.
    /// </remarks>
    protected virtual bool IsShorthandClassLetterInClass(char letter) => IsCoreShorthandClassLetter(letter);

    /// <summary>The six shorthand classes every flavor has.</summary>
    private static bool IsCoreShorthandClassLetter(char letter) => letter is 'd' or 'D' or 's' or 'S' or 'w' or 'W';

    /// <summary>Maps an inline option letter onto the options it sets, reporting whether the letter is one at all.</summary>
    /// <remarks>
    /// A letter can be recognized without changing anything: PCRE's <c>J</c> and <c>U</c> alter matching rather than
    /// syntax, but they still have to be accepted or the construct around them looks malformed.
    /// </remarks>
    protected virtual bool TryMapOptionLetter(char letter, out RegexPatternOptions option)
    {
        option = (char)(letter | 0x20) switch
        {
            'i' => RegexPatternOptions.IgnoreCase,
            'm' => RegexPatternOptions.Multiline,
            'n' => RegexPatternOptions.ExplicitCapture,
            's' => RegexPatternOptions.Singleline,
            'x' => RegexPatternOptions.IgnorePatternWhitespace,
            _ => RegexPatternOptions.None,
        };

        return option != RegexPatternOptions.None;
    }

    private RegexCapturingGroupSyntax ParsePlainGroup(RegexSyntaxToken openParenToken)
    {
        // ExplicitCapture and the condition of a conditional both suppress the capture, but the group is still spelled
        // with a bare "(", so it stays a capturing-group node with number 0.
        var capturing = (Options & RegexPatternOptions.ExplicitCapture) == RegexPatternOptions.None && !_ignoreNextParen;
        var number = capturing ? NoteAutoCapture(openParenToken.Span.Start) : 0;
        _ignoreNextParen = false;

        var alternation = ParseAlternation(insideGroup: true);
        var closeParenToken = ReadCloseParen(openParenToken);
        var group = WithOptions(new RegexCapturingGroupSyntax(openParenToken, alternation, closeParenToken, number));
        group.InnerOptions = alternation.Options;
        RestoreOptions();
        NoteCaptureSpan(number, group.Span);

        return group;
    }

    /// <summary>Parses a group whose header is <c>(?</c> plus one character.</summary>
    /// <remarks>
    /// A header the flavor does not have is reported and then read as a non-capturing group, so the body is still
    /// parsed and every character is still accounted for.
    /// </remarks>
    private RegexGroupSyntax ParseSimpleHeaderGroup(RegexSyntaxToken openParenToken, int questionStart, RegexSyntaxKind kind, RegexFlavorFeatures required)
    {
        Scanner.Position++;
        var groupKindToken = Scanner.Token(RegexSyntaxKind.GroupKindToken, questionStart);
        _ignoreNextParen = false;

        if (!Flavor.HasFeature(required))
        {
            AddDiagnostic(groupKindToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"The '{groupKindToken.Text}' grouping construct is not supported by the {Flavor.Name} flavor.");
            kind = RegexSyntaxKind.NonCapturingGroup;
        }

        var alternation = ParseAlternation(insideGroup: true);
        var closeParenToken = ReadCloseParen(openParenToken);
        RegexGroupSyntax group = kind switch
        {
            RegexSyntaxKind.AtomicGroup => new RegexAtomicGroupSyntax(openParenToken, groupKindToken, alternation, closeParenToken),
            RegexSyntaxKind.Lookaround => new RegexLookaroundSyntax(openParenToken, groupKindToken, alternation, closeParenToken),
            RegexSyntaxKind.BranchResetGroup => new RegexBranchResetGroupSyntax(openParenToken, groupKindToken, alternation, closeParenToken),
            _ => new RegexNonCapturingGroupSyntax(openParenToken, groupKindToken, alternation, closeParenToken),
        };

        WithOptions(group);
        group.InnerOptions = alternation.Options;
        RestoreOptions();

        return group;
    }

    /// <summary>Parses <c>(?R)</c> and <c>(?1)</c>, which restart the pattern or one of its groups.</summary>
    private RegexRecursionSyntax ParseRecursion(RegexSyntaxToken openParenToken, int questionStart)
    {
        var questionToken = Scanner.Token(RegexSyntaxKind.QuestionToken, questionStart);
        _ignoreNextParen = false;

        var targetStart = Scanner.Position;
        if (Scanner.Current == 'R')
        {
            Scanner.Position++;
        }
        else
        {
            ReadDecimal(out _);
        }

        var targetToken = Scanner.Token(RegexSyntaxKind.RecursionToken, targetStart);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return WithOptions(new RegexRecursionSyntax(openParenToken, questionToken, targetToken, closeParenToken));
    }

    /// <summary>Parses a backtracking control verb such as <c>(*SKIP)</c>.</summary>
    private RegexBacktrackingVerbSyntax ParseBacktrackingVerb(RegexSyntaxToken openParenToken)
    {
        var verbStart = Scanner.Position;
        Scanner.Position++;
        while (!Scanner.IsAtEnd && Scanner.Current is not (')' or '('))
        {
            Scanner.Position++;
        }

        var verbToken = Scanner.Token(RegexSyntaxKind.VerbToken, verbStart);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return WithOptions(new RegexBacktrackingVerbSyntax(openParenToken, verbToken, closeParenToken));
    }

    /// <summary>Parses <c>(?&lt;…</c>, <c>(?'…</c>, and <c>(?P&lt;…</c>: lookbehind, a named group, or a balancing group.</summary>
    private RegexAtomSyntax ParseAngledGroup(RegexSyntaxToken openParenToken, int questionStart)
    {
        // "(?P<" is the Python spelling of "(?<"; the extra letter is part of the header and nothing else.
        if (Scanner.Current == 'P')
        {
            Scanner.Position++;
        }

        var close = Scanner.Current == '\'' ? '\'' : '>';
        Scanner.Position++;

        // "(?<=" and "(?<!" are lookbehind; the single-quoted spelling has no lookbehind form.
        if (close == '>' && Scanner.Current is '=' or '!')
        {
            Scanner.Position++;
            var lookbehindKindToken = Scanner.Token(RegexSyntaxKind.GroupKindToken, questionStart);
            _ignoreNextParen = false;

            if (!Flavor.HasFeature(RegexFlavorFeatures.Lookbehind))
            {
                AddDiagnostic(lookbehindKindToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"The '{lookbehindKindToken.Text}' grouping construct is not supported by the {Flavor.Name} flavor.");
            }

            var lookbehindBody = ParseAlternation(insideGroup: true);
            var lookbehindClose = ReadCloseParen(openParenToken);
            var lookbehind = WithOptions(new RegexLookaroundSyntax(openParenToken, lookbehindKindToken, lookbehindBody, lookbehindClose));
            lookbehind.InnerOptions = lookbehindBody.Options;
            RestoreOptions();

            return lookbehind;
        }

        var groupKindToken = Scanner.Token(RegexSyntaxKind.GroupKindToken, questionStart);
        _ignoreNextParen = false;

        var namedSpelling = close == '\'' ? RegexFlavorFeatures.QuoteNamedGroups : RegexFlavorFeatures.AngleNamedGroups;
        if (!Flavor.HasFeature(namedSpelling))
        {
            AddDiagnostic(groupKindToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"The '{groupKindToken.Text}' grouping construct is not supported by the {Flavor.Name} flavor.");
        }

        var nameToken = ReadGroupNameOrNumber(close, openParenToken.Span.Start, out var capnum, out var startsWithHyphen);
        RegexSyntaxToken? hyphenToken = null;
        RegexSyntaxToken? previousNameToken = null;

        // A balancing group may name only the group it pops, as "(?<-1>x)" does, so a leading hyphen is enough on
        // its own to make the rest of the header a pop target.
        if ((capnum != -1 || startsWithHyphen) && Scanner.Position + 1 < Text.Length && Scanner.Current == '-')
        {
            var hyphenStart = Scanner.Position;
            Scanner.Position++;
            hyphenToken = Scanner.Token(RegexSyntaxKind.HyphenToken, hyphenStart);
            if (!Flavor.HasFeature(RegexFlavorFeatures.BalancingGroups))
            {
                AddDiagnostic(hyphenToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"Balancing groups are not supported by the {Flavor.Name} flavor.");
            }

            previousNameToken = ReadBalancingTarget(close);
        }

        // The engine accepts the header only when it named something: a group to push, a group to pop, or both.
        if (capnum == -1 && previousNameToken is null)
        {
            AddDiagnostic(
                TextSpan.FromBounds(openParenToken.Span.Start, Math.Max(openParenToken.Span.Start, Scanner.Position)),
                RegexDiagnosticIds.InvalidGroupingConstruct,
                "Invalid grouping construct.");
        }

        var closeNameToken = ReadNameTerminator(close);

        var alternationBody = ParseAlternation(insideGroup: true);
        var closeParenToken = ReadCloseParen(openParenToken);

        RegexGroupSyntax result;
        if (hyphenToken is not null)
        {
            var number = capnum > 0 ? capnum : ResolveDeclaredNumber(nameToken);
            result = new RegexBalancingGroupSyntax(openParenToken, groupKindToken, nameToken, hyphenToken, previousNameToken, closeNameToken, alternationBody, closeParenToken, number);
            NoteCaptureSpan(number, TextSpan.FromBounds(openParenToken.Span.Start, closeParenToken.Span.End));
        }
        else
        {
            var number = capnum > 0 ? capnum : ResolveDeclaredNumber(nameToken);
            result = new RegexNamedGroupSyntax(openParenToken, groupKindToken, nameToken, closeNameToken, alternationBody, closeParenToken, number);
            NoteCaptureSpan(number, TextSpan.FromBounds(openParenToken.Span.Start, closeParenToken.Span.End));
        }

        WithOptions(result);
        result.InnerOptions = alternationBody.Options;
        RestoreOptions();

        return result;
    }

    /// <summary>Reads the name or number a named group declares, reporting what the engine reports about it.</summary>
    private RegexSyntaxToken? ReadGroupNameOrNumber(char close, int groupStart, out int capnum, out bool startsWithHyphen)
    {
        capnum = -1;
        startsWithHyphen = false;

        var start = Scanner.Position;
        var ch = Scanner.Current;

        if (char.IsAsciiDigit(ch))
        {
            capnum = ReadDecimal(out _);
            var token = Scanner.Token(RegexSyntaxKind.NameToken, start);

            // Group zero is the whole match and cannot be declared, so the engine does not note it either.
            if (ch != '0')
            {
                NoteCaptureNumber(capnum, groupStart);
            }

            if (!Scanner.IsAtEnd && Scanner.Current != close && Scanner.Current != '-')
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");
            }
            else if (capnum == 0)
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.CaptureGroupOfZero, "Capture group numbers must be greater than zero.");
            }
            else if (!CaptureTable.ContainsNumber(capnum))
            {
                capnum = -1;
            }

            return token;
        }

        if (RegexCharacterTables.IsBoundaryWordChar(ch))
        {
            var name = ReadCaptureName();
            NoteCaptureName(name, groupStart);
            var token = Scanner.Token(RegexSyntaxKind.NameToken, start);
            if (!Scanner.IsAtEnd && Scanner.Current != close && Scanner.Current != '-')
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");
            }

            capnum = CaptureTable.TryGetNumber(name, out var declared) ? declared : -1;

            return token;
        }

        if (ch == '-')
        {
            startsWithHyphen = true;

            return null;
        }

        AddDiagnostic(new TextSpan(start, Math.Min(1, Text.Length - start)), RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");

        return null;
    }

    /// <summary>Reads the group a balancing group pops, which must already exist.</summary>
    private RegexSyntaxToken? ReadBalancingTarget(char close)
    {
        var start = Scanner.Position;
        var ch = Scanner.Current;

        if (char.IsAsciiDigit(ch))
        {
            var number = ReadDecimal(out _);
            var token = Scanner.Token(RegexSyntaxKind.NameToken, start);
            if (!CaptureTable.ContainsNumber(number))
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.UndefinedNumberedReference, FormattableString.Invariant($"Reference to undefined group number {number}."));
            }
            else if (!Scanner.IsAtEnd && Scanner.Current != close)
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");
            }

            return token;
        }

        if (RegexCharacterTables.IsBoundaryWordChar(ch))
        {
            var name = ReadCaptureName();
            var token = Scanner.Token(RegexSyntaxKind.NameToken, start);
            if (!CaptureTable.TryGetNumber(name, out _))
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.UndefinedNamedReference, $"Reference to undefined group name '{name}'.");
            }
            else if (!Scanner.IsAtEnd && Scanner.Current != close)
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");
            }

            return token;
        }

        AddDiagnostic(new TextSpan(start, Math.Min(1, Math.Max(0, Text.Length - start))), RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");

        return null;
    }

    private RegexSyntaxToken? ReadNameTerminator(char close)
    {
        if (Scanner.Current != close)
        {
            AddDiagnostic(new TextSpan(Scanner.Position, 0), RegexDiagnosticIds.InvalidGroupingConstruct, "Invalid grouping construct.");

            return null;
        }

        var start = Scanner.Position;
        Scanner.Position++;

        return Scanner.Token(RegexSyntaxKind.CloseNameToken, start);
    }

    private int ResolveDeclaredNumber(RegexSyntaxToken? nameToken) =>
        nameToken is not null && CaptureTable.TryGetNumber(nameToken.Text, out var number) ? number : 0;

    /// <summary>Parses <c>(?(…)yes|no)</c>.</summary>
    private RegexConditionalSyntax ParseConditional(RegexSyntaxToken openParenToken, int questionStart)
    {
        var questionToken = Scanner.Token(RegexSyntaxKind.QuestionToken, questionStart);
        if (!Flavor.HasFeature(RegexFlavorFeatures.Conditionals))
        {
            AddDiagnostic(questionToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"Conditional alternations are not supported by the {Flavor.Name} flavor.");
        }

        var conditionStart = Scanner.Position;

        RegexSyntaxNode? condition = ReadConditionalReference(conditionStart);
        if (condition is null)
        {
            // Not a reference, so the condition is an expression. The engine rewinds to the parenthesis and lets the
            // ordinary group parser read it, with the capture suppressed.
            Scanner.Position = conditionStart;
            ReportIllegalConditionHeader(conditionStart);
            _ignoreNextParen = true;
            _inConditionalTest = true;
            condition = ParseAtom([]);
            _inConditionalTest = false;
        }

        var alternation = ParseAlternation(insideGroup: true);
        if (alternation.Branches.Count > 2)
        {
            AddDiagnostic(alternation.Span, RegexDiagnosticIds.AlternationHasTooManyConditions, "A conditional alternation has too many branches.");
        }

        var closeParenToken = ReadCloseParen(openParenToken);
        var conditional = WithOptions(new RegexConditionalSyntax(openParenToken, questionToken, condition, alternation, closeParenToken));
        conditional.InnerOptions = alternation.Options;
        RestoreOptions();

        return conditional;
    }

    /// <summary>Reads <c>(1)</c> or <c>(name)</c>, or reports that the condition is an expression by returning null.</summary>
    private RegexConditionalReferenceSyntax? ReadConditionalReference(int conditionStart)
    {
        Scanner.Position++;
        var openParenToken = Scanner.Token(RegexSyntaxKind.OpenParenToken, conditionStart);

        var nameStart = Scanner.Position;
        if (char.IsAsciiDigit(Scanner.Current))
        {
            var number = ReadDecimal(out _);
            var nameToken = Scanner.Token(RegexSyntaxKind.NameToken, nameStart);
            if (Scanner.Current != ')')
            {
                AddDiagnostic(nameToken.Span, RegexDiagnosticIds.AlternationHasMalformedReference, FormattableString.Invariant($"Malformed conditional alternation reference '{number}'."));
            }
            else
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeToken = Scanner.Token(RegexSyntaxKind.CloseParenToken, closeStart);
                if (!CaptureTable.ContainsNumber(number))
                {
                    AddDiagnostic(nameToken.Span, RegexDiagnosticIds.AlternationHasUndefinedReference, FormattableString.Invariant($"Conditional alternation refers to undefined group number {number}."));
                }

                return WithOptions(new RegexConditionalReferenceSyntax(openParenToken, nameToken, closeToken));
            }

            Scanner.Position = conditionStart;

            return null;
        }

        if (RegexCharacterTables.IsBoundaryWordChar(Scanner.Current))
        {
            var name = ReadCaptureName();
            if (CaptureTable.TryGetNumber(name, out _) && Scanner.Current == ')')
            {
                var nameToken = Scanner.Token(RegexSyntaxKind.NameToken, nameStart);
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeToken = Scanner.Token(RegexSyntaxKind.CloseParenToken, closeStart);

                return WithOptions(new RegexConditionalReferenceSyntax(openParenToken, nameToken, closeToken));
            }
        }

        Scanner.Position = conditionStart;

        return null;
    }

    /// <summary>Reports the two headers a conditional's expression condition may not have.</summary>
    private void ReportIllegalConditionHeader(int conditionStart)
    {
        if (conditionStart + 2 >= Text.Length || Text[conditionStart + 1] != '?')
            return;

        if (Text[conditionStart + 2] == '#')
        {
            AddDiagnostic(new TextSpan(conditionStart, 3), RegexDiagnosticIds.AlternationHasComment, "A conditional alternation condition cannot contain a comment.");
        }
        else if (Text[conditionStart + 2] == '\'' ||
            (conditionStart + 3 < Text.Length && Text[conditionStart + 2] == '<' && Text[conditionStart + 3] is not '!' and not '='))
        {
            AddDiagnostic(new TextSpan(conditionStart, 3), RegexDiagnosticIds.AlternationHasNamedCapture, "A conditional alternation condition cannot be a named capture group.");
        }
    }

    /// <summary>Parses <c>(?i)</c> and <c>(?i:…)</c>.</summary>
    /// <remarks>
    /// An option setter with no body ends at its own <c>)</c>, and the options it set stay in effect until the
    /// enclosing group closes, so the entry this construct pushed is discarded rather than restored.
    /// </remarks>
    private RegexAtomSyntax ParseOptionsConstruct(RegexSyntaxToken openParenToken, int questionStart, bool inConditionalTest)
    {
        var questionToken = Scanner.Token(RegexSyntaxKind.QuestionToken, questionStart);
        if (!Flavor.HasFeature(RegexFlavorFeatures.InlineOptions))
        {
            AddDiagnostic(questionToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"Inline options are not supported by the {Flavor.Name} flavor.");
        }

        var optionsStart = Scanner.Position;
        var optionsToken = inConditionalTest || !Flavor.HasFeature(RegexFlavorFeatures.InlineOptions) ? null : ScanInlineOptions(optionsStart);

        if (Scanner.Current == ')')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            var closeToken = Scanner.Token(RegexSyntaxKind.CloseParenToken, closeStart);
            OptionsStack.Pop();
            _ignoreNextParen = false;

            return WithOptions(new RegexInlineOptionsSyntax(openParenToken, questionToken, optionsToken, closeToken) { AppliedOptions = Options });
        }

        if (Scanner.Current != ':')
        {
            AddDiagnostic(
                TextSpan.FromBounds(openParenToken.Span.Start, Math.Max(openParenToken.Span.Start, Scanner.Position)),
                RegexDiagnosticIds.InvalidGroupingConstruct,
                "Invalid grouping construct.");

            var recoveredBody = ParseAlternation(insideGroup: true);
            var recoveredClose = ReadCloseParen(openParenToken);
            var recovered = WithOptions(new RegexOptionsGroupSyntax(openParenToken, questionToken, optionsToken, null, recoveredBody, recoveredClose));
            recovered.InnerOptions = recoveredBody.Options;
            RestoreOptions();

            return recovered;
        }

        var colonStart = Scanner.Position;
        Scanner.Position++;
        var colonToken = Scanner.Token(RegexSyntaxKind.ColonToken, colonStart);
        _ignoreNextParen = false;

        var body = ParseAlternation(insideGroup: true);
        var closeParenToken = ReadCloseParen(openParenToken);
        var group = WithOptions(new RegexOptionsGroupSyntax(openParenToken, questionToken, optionsToken, colonToken, body, closeParenToken));
        group.InnerOptions = body.Options;
        RestoreOptions();

        return group;
    }

    /// <summary>Reads an <c>imnsx-imnsx</c> run and applies it, stopping at the first character it does not know.</summary>
    private RegexSyntaxToken? ScanInlineOptions(int start)
    {
        var off = false;
        while (!Scanner.IsAtEnd)
        {
            var ch = Scanner.Current;
            if (ch == '-')
            {
                off = true;
            }
            else if (ch == '+')
            {
                off = false;
            }
            else
            {
                if (!TryMapOptionLetter(ch, out var option))
                    break;

                Options = off ? Options & ~option : Options | option;
            }

            Scanner.Position++;
        }

        return Scanner.Position > start ? Scanner.Token(RegexSyntaxKind.OptionsToken, start) : null;
    }

    private protected RegexSyntaxToken ReadCloseParen(RegexSyntaxToken openParenToken)
    {
        var trivia = TakeTrivia();
        var closeLength = GroupCloseLength(Scanner.Position);
        if (closeLength > 0)
        {
            var start = Scanner.Position;
            Scanner.Position += closeLength;

            return Scanner.Token(RegexSyntaxKind.CloseParenToken, start, trivia);
        }

        AddDiagnostic(
            TextSpan.FromBounds(openParenToken.Span.Start, Math.Max(openParenToken.Span.Start, Scanner.Position)),
            RegexDiagnosticIds.InsufficientClosingParentheses,
            "Unterminated group: expected ')'.");

        return Scanner.MissingToken(RegexSyntaxKind.CloseParenToken, trivia);
    }

    private protected void RestoreOptions()
    {
        if (OptionsStack.Count > 0)
        {
            Options = OptionsStack.Pop();
        }
    }

    /// <summary>Reads a run of digits, clamping a value that does not fit rather than throwing.</summary>
    private int ReadDecimal(out bool overflowed)
    {
        overflowed = false;
        long value = 0;
        var start = Scanner.Position;
        while (char.IsAsciiDigit(Scanner.Current))
        {
            if (!overflowed)
            {
                value = (value * 10) + (Scanner.Current - '0');
                if (value > int.MaxValue)
                {
                    overflowed = true;
                    value = int.MaxValue;
                }
            }

            Scanner.Position++;
        }

        if (overflowed)
        {
            AddDiagnostic(
                TextSpan.FromBounds(start, Scanner.Position),
                RegexDiagnosticIds.QuantifierOrCaptureGroupOutOfRange,
                "The quantifier or capture group number is larger than Int32.MaxValue.");
        }

        return (int)value;
    }

    /// <summary>Reads a capture-group name and returns it.</summary>
    private string ReadCaptureName()
    {
        var start = Scanner.Position;
        while (!Scanner.IsAtEnd && RegexCharacterTables.IsBoundaryWordChar(Scanner.Current))
        {
            Scanner.Position++;
        }

        return Text[start..Scanner.Position];
    }

    private static string FormatNumber(int value) => value.ToString(CultureInfo.InvariantCulture);
}
