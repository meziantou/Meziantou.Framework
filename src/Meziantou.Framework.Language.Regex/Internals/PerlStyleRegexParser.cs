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

using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using ScannedToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>The Perl-derived grammar, parameterized by what the dialect supports.</summary>
/// <remarks>
/// Ported from the .NET engine, which is the most complete of the Perl-derived grammars, and then narrowed by feature
/// flags for the dialects that have less. The alternative, a parser per dialect, would have four copies of the same
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

    protected PerlStyleRegexParser(SourceText source, RegexParseOptions parseOptions)
        : base(source, parseOptions)
    {
    }

    protected override RegexAtomSyntax ParseAtom(GreenNode? leadingTrivia)
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
                return new RegexAnchorSyntax(Scanner.Token(SyntaxKind.AnchorToken, start, leadingTrivia), Options);

            case '.':
                Scanner.Position++;
                return new RegexAnyCharacterSyntax(Scanner.Token(SyntaxKind.DotToken, start, leadingTrivia), Options);

            case ')' when GroupCloseLength(start) > 0:
                return SkipOneCharacter(leadingTrivia, RegexDiagnosticIds.InsufficientOpeningParentheses, "Unmatched ')'.");

            // Only a character that is a quantifier in this dialect can be one with nothing to repeat. In a basic
            // expression "+" and "?" are ordinary characters, so reporting them here would invent an error.
            case '*':
            case '+' or '?' when Dialect.HasFeature(RegexDialectFeatures.PlusAndQuestionQuantifiers):
                return SkipOneCharacter(leadingTrivia, RegexDiagnosticIds.QuantifierAfterNothing, $"Quantifier '{Scanner.Current}' has nothing to repeat.");

            case '{' when IsQuantifierAt(Scanner.Position):
                return SkipOneCharacter(leadingTrivia, RegexDiagnosticIds.QuantifierAfterNothing, "Quantifier '{' has nothing to repeat.");

            case '{' or '}' or ']' when !AllowsLoneQuantifierBracket:
                return SkipOneCharacter(leadingTrivia, RegexDiagnosticIds.QuantifierAfterNothing, $"'{Scanner.Current}' must be escaped to match itself.");

            default:
                Scanner.Position++;

                // In Unicode mode a pattern is a sequence of code points, so a surrogate pair is one atom and a
                // quantifier after it repeats the whole character rather than half of one.
                if (ReadsCodePoints && char.IsHighSurrogate(Text[start]) && char.IsLowSurrogate(Scanner.Current))
                {
                    Scanner.Position++;
                }

                return new RegexLiteralSyntax(Scanner.Token(SyntaxKind.LiteralToken, start, leadingTrivia), Options);
        }
    }

    /// <summary>Parses a parenthesized construct, from <c>(</c> through the matching <c>)</c>.</summary>
    /// <remarks>
    /// Ported from <c>ScanGroupOpen</c>. Every path that construct reached by <c>goto BreakRecognize</c> becomes a
    /// diagnostic plus a node that still covers the text, and every character it consumed in one step is kept as its
    /// own token so the parts of a header are addressable.
    /// </remarks>
    private RegexAtomSyntax ParseGroup(GreenNode? leadingTrivia, int openLength)
    {
        var start = Scanner.Position;
        if (!TryEnterRecursion(new TextSpan(start, 1)))
            return ConsumeRestAsText(start, leadingTrivia);

        try
        {
            Scanner.Position += openLength;
            var openParenToken = Scanner.Token(SyntaxKind.OpenParenToken, start, leadingTrivia);
            OptionsStack.Push((Options, DuplicateNamesAllowed));

            // The flag applies to this parenthesis only, never to anything nested inside it.
            var inConditionalTest = _inConditionalTest;
            _inConditionalTest = false;

            // A backtracking verb is "(*NAME)", so it is decided before the "(?" headers are looked at.
            if (Scanner.Current == '*' && Dialect.HasFeature(RegexDialectFeatures.BacktrackingVerbs))
                return ParseBacktrackingVerb(openParenToken);

            // "(" at the end, "(x" where x is not "?", and "(?)" are all plain groups. The "?" of "(?)" is left where
            // it is on purpose: the engine leaves it too, and the body parse then reports it as a quantifier with
            // nothing to repeat. A dialect without the "(?…)" family treats every one of them the same way.
            if (Scanner.IsAtEnd || Scanner.Current != '?' ||
                (Scanner.Peek() == ')' && !AllowsEmptyOptionGroup) ||
                !Dialect.HasFeature(RegexDialectFeatures.ExtendedGroupSyntax))
            {
                return ParsePlainGroup(openParenToken);
            }

            var questionStart = Scanner.Position;
            Scanner.Position++;

            switch (Scanner.Current)
            {
                case ':':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, SyntaxKind.NonCapturingGroup, RegexDialectFeatures.NonCapturingGroups);

                case '=':
                case '!':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, SyntaxKind.Lookaround, RegexDialectFeatures.Lookahead);

                case '>':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, SyntaxKind.AtomicGroup, RegexDialectFeatures.AtomicGroups);

                case '|':
                    return ParseSimpleHeaderGroup(openParenToken, questionStart, SyntaxKind.BranchResetGroup, RegexDialectFeatures.BranchReset);

                case '<':
                case '\'':
                    return ParseAngledGroup(openParenToken, questionStart);

                case 'P' when Dialect.HasFeature(RegexDialectFeatures.PythonNamedGroups) && Scanner.Peek() == '<':
                    return ParseAngledGroup(openParenToken, questionStart);

                case '(':
                    return ParseConditional(openParenToken, questionStart);

                case 'R':
                case >= '0' and <= '9' when Dialect.HasFeature(RegexDialectFeatures.Recursion):
                case '-' or '+' when Dialect.HasFeature(RegexDialectFeatures.Recursion) && char.IsAsciiDigit(Scanner.Peek()):
                    if (Dialect.HasFeature(RegexDialectFeatures.Recursion))
                        return ParseRecursion(openParenToken, questionStart);

                    goto default;

                default:
                    return TryParseDialectGroupHeader(openParenToken, questionStart)
                        ?? ParseOptionsConstruct(openParenToken, questionStart, inConditionalTest);
            }
        }
        finally
        {
            ExitRecursion();
        }
    }

    /// <summary>How many lookarounds, of either direction, enclose the reading position.</summary>
    private protected int LookaroundDepth { get; set; }

    /// <summary>How many lookbehinds enclose the reading position.</summary>
    private protected int LookbehindDepth { get; set; }

    /// <summary>Called once the body of a lookbehind has been read, for the dialects that restrict what it may match.</summary>
    private protected virtual void OnLookbehindParsed(RegexAlternationSyntax body, TextSpan span, int bodyStart)
    {
    }

    /// <summary>Called once a capturing group has been read, with the number it took.</summary>
    private protected virtual void OnCaptureGroupParsed(int number, RegexGroupSyntax group)
    {
    }

    /// <summary>Parses a <c>(?…</c> header that only some dialects have, or returns null to fall through.</summary>
    protected virtual RegexAtomSyntax? TryParseDialectGroupHeader(ScannedToken openParenToken, int questionStart) => null;

    /// <summary>Parses a backslash escape that only some dialects have, or returns null to fall through.</summary>
    /// <remarks>The reading position is on the backslash.</remarks>
    protected virtual RegexAtomSyntax? TryParseDialectEscape(GreenNode? leadingTrivia) => null;

    /// <summary>Whether the reading position is inside a character class, where a few escapes differ.</summary>
    protected bool IsInCharacterClass { get; private set; }

    /// <summary>Whether <c>\</c> followed by <paramref name="ch"/> may simply stand for that character.</summary>
    protected virtual bool AllowsIdentityEscape(char ch) =>
        !Dialect.HasFeature(RegexDialectFeatures.StrictEscapes) ||
        UsesEcmaScriptBehavior ||
        !RegexCharacterTables.IsBoundaryWordChar(ch);

    /// <summary>
    /// Whether a <c>\x</c>, <c>\u</c>, or <c>\c</c> escape without the digits it needs falls back to standing for
    /// its own letter instead of being reported.
    /// </summary>
    protected virtual bool AllowsMalformedNumericEscape => false;

    /// <summary>Whether a shorthand class may be an endpoint of a range, which makes the dash an ordinary character.</summary>
    protected virtual bool AllowsShorthandClassInRange => false;

    /// <summary>
    /// Whether a bare <c>{</c>, <c>}</c>, or <c>]</c> is an ordinary character. The strict ECMAScript grammar calls it
    /// a lone quantifier bracket and rejects it.
    /// </summary>
    protected virtual bool AllowsLoneQuantifierBracket => true;

    /// <summary>
    /// Whether <c>\c</c> may be followed by something other than an ASCII letter. The .NET engine subtracts <c>@</c>
    /// from whatever is there and takes it if the result is a control character; the strict ECMAScript grammar wants
    /// a letter.
    /// </summary>
    protected virtual bool AllowsNonLetterControlEscape => true;

    /// <summary>
    /// Whether the Perl character escapes -- <c>\a</c>, <c>\e</c>, <c>\f</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>,
    /// <c>\v</c>, <c>\x</c>, <c>\u</c>, <c>\c</c>, and octal -- mean anything.
    /// </summary>
    /// <remarks>
    /// POSIX has none of them. There a backslash before an ordinary character just means that character, so reading
    /// <c>\x41</c> as an "A" would describe a pattern the engine does not see.
    /// </remarks>
    protected virtual bool RecognizesPerlCharacterEscapes => true;

    /// <summary>Whether <c>(?)</c> is an option group that sets nothing rather than a malformed construct.</summary>
    protected virtual bool AllowsEmptyOptionGroup => false;

    /// <summary>
    /// Whether the pattern is a sequence of code points rather than of UTF-16 code units, so that a surrogate pair is
    /// one character wherever it appears, a character class and its ranges included.
    /// </summary>
    protected virtual bool ReadsCodePoints => UsesUnicodeMode;

    /// <summary>Whether <c>\a</c> (bell) and <c>\e</c> (escape) are character escapes.</summary>
    protected virtual bool HasBellAndEscapeEscapes => true;

    /// <summary>Whether <c>\u</c> introduces a character escape at all.</summary>
    protected virtual bool SupportsUnicodeEscape => true;

    /// <summary>
    /// Whether a backslash followed by digits follows the ECMAScript grammar: the digits name a group when the pattern
    /// has that many, and are otherwise an error in Unicode mode or a legacy octal escape outside it.
    /// </summary>
    protected virtual bool UsesJavaScriptDecimalEscapes => false;

    /// <summary>
    /// Whether a shorthand class followed by a dash is reported as the start of a range. .NET makes that dash an
    /// ordinary character; PCRE and the strict ECMAScript grammar reject it.
    /// </summary>
    protected virtual bool ReportsShorthandClassAsRangeStart => false;

    /// <summary>
    /// Whether any character may follow <c>\c</c>. PCRE exclusive-ors whatever is there with <c>0x40</c> and accepts
    /// the result; .NET only takes it when it lands on a control character.
    /// </summary>
    protected virtual bool AllowsAnyControlEscapeCharacter => false;

    /// <summary>Whether <c>\xh</c> with a single hexadecimal digit is well formed, as it is in PCRE.</summary>
    protected virtual bool AllowsShortHexEscape => false;

    /// <summary>Whether a property may be written without braces, as <c>\pL</c>.</summary>
    protected virtual bool AllowsBracelessProperty => false;

    /// <summary>
    /// Whether <c>\&lt;name&gt;</c> is a backreference. Only .NET spells one that way; in POSIX <c>\&lt;</c> asserts a
    /// word boundary and in PCRE it is an ordinary escape.
    /// </summary>
    protected virtual bool AllowsBareAngleBackreference => false;

    /// <summary>Whether a backslash followed by digits may fall back to an octal escape.</summary>
    protected virtual bool AllowsOctalEscape => true;

    /// <summary>Whether <c>\k</c> naming nothing falls back to standing for its own letter.</summary>
    protected virtual bool AllowsUndefinedNamedBackreference => false;

    /// <summary>Whether the pattern declares any named group, which decides what a bare <c>\k</c> means.</summary>
    protected bool HasAnyGroupName => CaptureTable.HasNames;

    /// <summary>Whether the letter after a backslash names a shorthand character class at the atom level.</summary>
    protected virtual bool IsShorthandClassLetter(char letter) => IsCoreShorthandClassLetter(letter);

    /// <summary>Whether the letter after a backslash names a shorthand character class inside a class.</summary>
    /// <remarks>
    /// Deliberately not delegating to <see cref="IsShorthandClassLetter"/>: an override that widens one by calling the
    /// other would then call back into itself.
    /// </remarks>
    protected virtual bool IsShorthandClassLetterInClass(char letter) => IsCoreShorthandClassLetter(letter);

    /// <summary>The six shorthand classes every dialect has.</summary>
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

    private RegexCapturingGroupSyntax ParsePlainGroup(ScannedToken openParenToken)
    {
        // ExplicitCapture and the condition of a conditional both suppress the capture, but the group is still spelled
        // with a bare "(", so it stays a capturing-group node with number 0.
        var capturing = (Options & RegexPatternOptions.ExplicitCapture) == RegexPatternOptions.None && !_ignoreNextParen;
        var number = capturing ? NoteAutoCapture(openParenToken.Span.Start) : 0;
        _ignoreNextParen = false;

        var alternation = ParseAlternation(insideGroup: true);
        var closeParenToken = ReadCloseParen(openParenToken);
        var group = new RegexCapturingGroupSyntax(openParenToken, alternation, closeParenToken, Options, alternation.Options, number);
        RestoreOptions();
        NoteCaptureSpan(number, TextSpan.FromBounds(openParenToken.Span.Start, closeParenToken.End));
        OnCaptureGroupParsed(number, group);

        return group;
    }

    /// <summary>Parses a group whose header is <c>(?</c> plus one character.</summary>
    /// <remarks>
    /// A header the dialect does not have is reported and then read as a non-capturing group, so the body is still
    /// parsed and every character is still accounted for.
    /// </remarks>
    private RegexGroupSyntax ParseSimpleHeaderGroup(ScannedToken openParenToken, int questionStart, SyntaxKind kind, RegexDialectFeatures required)
    {
        Scanner.Position++;
        var groupKindToken = Scanner.Token(SyntaxKind.GroupKindToken, questionStart);
        _ignoreNextParen = false;

        if (!Dialect.HasFeature(required))
        {
            AddDiagnostic(groupKindToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"The '{groupKindToken.Text}' grouping construct is not supported by the {Dialect.Name} dialect.");
            kind = SyntaxKind.NonCapturingGroup;
        }

        LookaroundDepth += kind == SyntaxKind.Lookaround ? 1 : 0;
        var alternation = ParseAlternation(insideGroup: true, resetsCaptureNumbers: kind == SyntaxKind.BranchResetGroup);
        LookaroundDepth -= kind == SyntaxKind.Lookaround ? 1 : 0;
        var closeParenToken = ReadCloseParen(openParenToken);
        RegexGroupSyntax group = kind switch
        {
            SyntaxKind.AtomicGroup => new RegexAtomicGroupSyntax(openParenToken, groupKindToken, alternation, closeParenToken, Options, alternation.Options),
            SyntaxKind.Lookaround => new RegexLookaroundSyntax(openParenToken, groupKindToken, alternation, closeParenToken, Options, alternation.Options),
            SyntaxKind.BranchResetGroup => new RegexBranchResetGroupSyntax(openParenToken, groupKindToken, alternation, closeParenToken, Options, alternation.Options),
            _ => new RegexNonCapturingGroupSyntax(openParenToken, groupKindToken, alternation, closeParenToken, Options, alternation.Options),
        };

        RestoreOptions();

        return group;
    }

    /// <summary>Parses <c>(?R)</c> and <c>(?1)</c>, which restart the pattern or one of its groups.</summary>
    private RegexRecursionSyntax ParseRecursion(ScannedToken openParenToken, int questionStart)
    {
        var questionToken = Scanner.Token(SyntaxKind.QuestionToken, questionStart);
        _ignoreNextParen = false;

        var targetStart = Scanner.Position;
        if (Scanner.Current == 'R')
        {
            Scanner.Position++;
        }
        else
        {
            if (Scanner.Current is '-' or '+')
            {
                Scanner.Position++;
            }

            ReadDecimal(out _);
        }

        var targetToken = Scanner.Token(SyntaxKind.RecursionToken, targetStart);
        ReportUnknownRecursionTarget(targetToken);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return new RegexRecursionSyntax(openParenToken, questionToken, targetToken, closeParenToken, Options);
    }

    /// <summary>Reports a recursion into a group that does not exist. <c>R</c> means the whole pattern, so it always does.</summary>
    private protected void ReportUnknownRecursionTarget(ScannedToken targetToken)
    {
        if (!targetToken.IsPresent || targetToken.Text is "R" or "")
            return;

        var target = targetToken.Text;
        if (target is ['-' or '+', _, ..] && int.TryParse(target.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var relative))
        {
            ReportUnknownRelativeReference(targetToken.Span, target[0] == '-' ? -relative : relative);

            return;
        }

        if (int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            // Group 0 is the whole pattern, which is always there to recurse into.
            if (number != 0 && !CaptureTable.ContainsNumber(number))
            {
                AddDiagnostic(targetToken.Span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {target}.");
            }

            return;
        }

        if (!CaptureTable.TryGetNumber(target, out _))
        {
            AddDiagnostic(targetToken.Span, RegexDiagnosticIds.UndefinedNamedReference, $"Reference to undefined group name '{target}'.");
        }
    }

    /// <summary>
    /// Reports a relative reference, <c>-1</c> for the group opened last or <c>+1</c> for the one opened next, that
    /// names no group.
    /// </summary>
    private protected void ReportUnknownRelativeReference(TextSpan span, int offset)
    {
        // The reading position is past the reference, and the groups opened so far have taken every number below the
        // next one to be assigned.
        var number = offset switch
        {
            0 => 0,
            < 0 => AutoCaptureNumber + offset,
            _ => AutoCaptureNumber + offset - 1,
        };

        if (offset == 0)
        {
            AddDiagnostic(span, RegexDiagnosticIds.UndefinedNumberedReference, "A relative reference cannot be zero.");
        }
        else if (number < 1 || !CaptureTable.ContainsNumber(number))
        {
            AddDiagnostic(span, RegexDiagnosticIds.UndefinedNumberedReference, FormattableString.Invariant($"Reference to undefined group, {offset:+0;-0} from here."));
        }
    }

    /// <summary>Parses a backtracking control verb such as <c>(*SKIP)</c>.</summary>
    private protected virtual RegexAtomSyntax ParseBacktrackingVerb(ScannedToken openParenToken)
    {
        var verbStart = Scanner.Position;
        Scanner.Position++;
        while (!Scanner.IsAtEnd && Scanner.Current is not (')' or '('))
        {
            Scanner.Position++;
        }

        var verbToken = Scanner.Token(SyntaxKind.VerbToken, verbStart);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return new RegexBacktrackingVerbSyntax(openParenToken, verbToken, closeParenToken, Options);
    }

    /// <summary>Parses <c>(?&lt;…</c>, <c>(?'…</c>, and <c>(?P&lt;…</c>: lookbehind, a named group, or a balancing group.</summary>
    private RegexAtomSyntax ParseAngledGroup(ScannedToken openParenToken, int questionStart)
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
            var lookbehindKindToken = Scanner.Token(SyntaxKind.GroupKindToken, questionStart);
            _ignoreNextParen = false;

            if (!Dialect.HasFeature(RegexDialectFeatures.Lookbehind))
            {
                AddDiagnostic(lookbehindKindToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"The '{lookbehindKindToken.Text}' grouping construct is not supported by the {Dialect.Name} dialect.");
            }

            LookaroundDepth++;
            LookbehindDepth++;
            var lookbehindBody = ParseAlternation(insideGroup: true);
            LookaroundDepth--;
            LookbehindDepth--;
            var lookbehindClose = ReadCloseParen(openParenToken);
            var lookbehind = new RegexLookaroundSyntax(openParenToken, lookbehindKindToken, lookbehindBody, lookbehindClose, Options, lookbehindBody.Options);
            OnLookbehindParsed(lookbehindBody, TextSpan.FromBounds(openParenToken.Span.Start, lookbehindClose.End), lookbehindKindToken.End);
            RestoreOptions();

            return lookbehind;
        }

        var groupKindToken = Scanner.Token(SyntaxKind.GroupKindToken, questionStart);
        _ignoreNextParen = false;

        var namedSpelling = close == '\'' ? RegexDialectFeatures.QuoteNamedGroups : RegexDialectFeatures.AngleNamedGroups;
        if (!Dialect.HasFeature(namedSpelling))
        {
            AddDiagnostic(groupKindToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"The '{groupKindToken.Text}' grouping construct is not supported by the {Dialect.Name} dialect.");
        }

        var nameToken = ReadGroupNameOrNumber(close, openParenToken.Span.Start, out var capnum, out var startsWithHyphen);
        ScannedToken hyphenToken = default;
        ScannedToken previousNameToken = default;

        // A balancing group may name only the group it pops, as "(?<-1>x)" does, so a leading hyphen is enough on
        // its own to make the rest of the header a pop target.
        if ((capnum != -1 || startsWithHyphen) && Scanner.Position + 1 < Text.Length && Scanner.Current == '-')
        {
            var hyphenStart = Scanner.Position;
            Scanner.Position++;
            hyphenToken = Scanner.Token(SyntaxKind.HyphenToken, hyphenStart);
            if (!Dialect.HasFeature(RegexDialectFeatures.BalancingGroups))
            {
                AddDiagnostic(hyphenToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"Balancing groups are not supported by the {Dialect.Name} dialect.");
            }

            previousNameToken = ReadBalancingTarget(close);
        }

        // The engine accepts the header only when it named something: a group to push, a group to pop, or both.
        if (capnum == -1 && !previousNameToken.IsPresent)
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
        if (hyphenToken.IsPresent)
        {
            var number = capnum > 0 ? capnum : ResolveDeclaredNumber(nameToken);
            result = new RegexBalancingGroupSyntax(openParenToken, groupKindToken, nameToken, hyphenToken, previousNameToken, closeNameToken, alternationBody, closeParenToken, Options, alternationBody.Options, number);
            NoteCaptureSpan(number, TextSpan.FromBounds(openParenToken.Span.Start, closeParenToken.Span.End));
        }
        else
        {
            var number = capnum > 0 ? capnum : ResolveDeclaredNumber(nameToken);
            result = new RegexNamedGroupSyntax(openParenToken, groupKindToken, nameToken, closeNameToken, alternationBody, closeParenToken, Options, alternationBody.Options, number);
            NoteCaptureSpan(number, TextSpan.FromBounds(openParenToken.Span.Start, closeParenToken.Span.End));
            OnCaptureGroupParsed(number, result);
        }

        RestoreOptions();

        return result;
    }

    /// <summary>Reads the name or number a named group declares, reporting what the engine reports about it.</summary>
    private ScannedToken ReadGroupNameOrNumber(char close, int groupStart, out int capnum, out bool startsWithHyphen)
    {
        capnum = -1;
        startsWithHyphen = false;

        var start = Scanner.Position;
        var ch = Scanner.Current;

        if (char.IsAsciiDigit(ch) && AllowsNumberedGroups)
        {
            capnum = ReadDecimal(out _);
            var token = Scanner.Token(SyntaxKind.NameToken, start);

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

        if (IsGroupNameStartAt(Scanner.Position))
        {
            var name = ReadGroupName();
            var token = Scanner.Token(SyntaxKind.NameToken, start, leadingTrivia: null, ValueIfDifferent(name, start));
            if (!Scanner.IsAtEnd && Scanner.Current != close && Scanner.Current != '-')
            {
                AddDiagnostic(token.Span, RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");
            }

            if (NamedGroupsTakeNumbersInOrder)
            {
                capnum = NoteNumberedCaptureName(name, groupStart);
                if (!IsNumberingPass)
                {
                    CheckGroupNameDeclaration(name, capnum, token.Span);
                }
            }
            else
            {
                NoteCaptureName(name, groupStart);
                capnum = CaptureTable.TryGetNumber(name, out var declared) ? declared : -1;
            }

            return token;
        }

        if (ch == '-')
        {
            startsWithHyphen = true;

            return default;
        }

        AddDiagnostic(new TextSpan(start, Math.Min(1, Text.Length - start)), RegexDiagnosticIds.CaptureGroupNameInvalid, "Invalid capture group name.");

        return default;
    }

    /// <summary>Reads the group a balancing group pops, which must already exist.</summary>
    private ScannedToken ReadBalancingTarget(char close)
    {
        var start = Scanner.Position;
        var ch = Scanner.Current;

        if (char.IsAsciiDigit(ch))
        {
            var number = ReadDecimal(out _);
            var token = Scanner.Token(SyntaxKind.NameToken, start);
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
            var token = Scanner.Token(SyntaxKind.NameToken, start);
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

        return default;
    }

    private ScannedToken ReadNameTerminator(char close)
    {
        if (Scanner.Current != close)
        {
            AddDiagnostic(new TextSpan(Scanner.Position, 0), RegexDiagnosticIds.InvalidGroupingConstruct, "Invalid grouping construct.");

            return default;
        }

        var start = Scanner.Position;
        Scanner.Position++;

        return Scanner.Token(SyntaxKind.CloseNameToken, start);
    }

    private int ResolveDeclaredNumber(ScannedToken nameToken) =>
        nameToken.IsPresent && CaptureTable.TryGetNumber(nameToken.ValueText, out var number) ? number : 0;

    /// <summary>
    /// Whether a named group takes the next number where it stands, as in JavaScript and PCRE, rather than one after
    /// every unnamed group, as in .NET.
    /// </summary>
    protected virtual bool NamedGroupsTakeNumbersInOrder => false;

    /// <summary>Whether a group may be given an explicit number, as <c>(?&lt;3&gt;x)</c> does in .NET.</summary>
    protected virtual bool AllowsNumberedGroups => true;

    /// <summary>Whether a group name starts at <paramref name="position"/>.</summary>
    protected virtual bool IsGroupNameStartAt(int position) => RegexCharacterTables.IsBoundaryWordChar(Scanner.CharAt(position));

    /// <summary>Reads a group name at the reading position and returns the name it spells.</summary>
    /// <remarks>The name may differ from its text where the dialect lets a name contain escapes.</remarks>
    protected virtual string ReadGroupName() => ReadCaptureName();

    /// <summary>Checks a group name against the groups already declared, in the pass that reports diagnostics.</summary>
    protected virtual void CheckGroupNameDeclaration(string name, int number, TextSpan span)
    {
    }

    /// <summary>Parses <c>(?(…)yes|no)</c>.</summary>
    private RegexConditionalSyntax ParseConditional(ScannedToken openParenToken, int questionStart)
    {
        var questionToken = Scanner.Token(SyntaxKind.QuestionToken, questionStart);
        if (!Dialect.HasFeature(RegexDialectFeatures.Conditionals))
        {
            AddDiagnostic(questionToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"Conditional alternations are not supported by the {Dialect.Name} dialect.");
        }

        var conditionStart = Scanner.Position;
        ConditionAllowsOneBranchOnly = false;

        RegexSyntaxNode? condition = ReadConditionalReference(conditionStart);
        var maxBranches = ConditionAllowsOneBranchOnly ? 1 : 2;
        if (condition is null)
        {
            // Not a reference, so the condition is an expression. The engine rewinds to the parenthesis and lets the
            // ordinary group parser read it, with the capture suppressed.
            Scanner.Position = conditionStart;
            ReportIllegalConditionHeader(conditionStart);
            _ignoreNextParen = true;
            _inConditionalTest = true;
            condition = ParseAtom(ConditionMayStartWithComment ? TakeTrivia() : null);
            _inConditionalTest = false;
        }

        var alternation = ParseAlternation(insideGroup: true);
        if (alternation.BranchCount > maxBranches)
        {
            AddDiagnostic(JustParsedSpan(alternation), RegexDiagnosticIds.AlternationHasTooManyConditions, "A conditional alternation has too many branches.");
        }

        var closeParenToken = ReadCloseParen(openParenToken);
        var conditional = new RegexConditionalSyntax(openParenToken, questionToken, condition, alternation, closeParenToken, Options, alternation.Options);
        RestoreOptions();

        return conditional;
    }

    /// <summary>Set by <see cref="ReadConditionalReference"/> when the condition is one, like PCRE's <c>DEFINE</c>, that takes a single branch.</summary>
    private protected bool ConditionAllowsOneBranchOnly { get; set; }

    /// <summary>Whether a comment may stand in front of an expression condition, as PCRE allows.</summary>
    private protected virtual bool ConditionMayStartWithComment => false;

    /// <summary>Reads <c>(1)</c> or <c>(name)</c>, or reports that the condition is an expression by returning null.</summary>
    private protected virtual RegexConditionalReferenceSyntax? ReadConditionalReference(int conditionStart)
    {
        Scanner.Position++;
        var openParenToken = Scanner.Token(SyntaxKind.OpenParenToken, conditionStart);

        var nameStart = Scanner.Position;
        if (char.IsAsciiDigit(Scanner.Current))
        {
            var number = ReadDecimal(out _);
            var nameToken = Scanner.Token(SyntaxKind.NameToken, nameStart);
            if (Scanner.Current != ')')
            {
                AddDiagnostic(nameToken.Span, RegexDiagnosticIds.AlternationHasMalformedReference, FormattableString.Invariant($"Malformed conditional alternation reference '{number}'."));
            }
            else
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeToken = Scanner.Token(SyntaxKind.CloseParenToken, closeStart);
                if (!CaptureTable.ContainsNumber(number))
                {
                    AddDiagnostic(nameToken.Span, RegexDiagnosticIds.AlternationHasUndefinedReference, FormattableString.Invariant($"Conditional alternation refers to undefined group number {number}."));
                }

                return new RegexConditionalReferenceSyntax(openParenToken, nameToken, closeToken, Options);
            }

            Scanner.Position = conditionStart;

            return null;
        }

        if (RegexCharacterTables.IsBoundaryWordChar(Scanner.Current))
        {
            var name = ReadCaptureName();
            if (CaptureTable.TryGetNumber(name, out _) && Scanner.Current == ')')
            {
                var nameToken = Scanner.Token(SyntaxKind.NameToken, nameStart);
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeToken = Scanner.Token(SyntaxKind.CloseParenToken, closeStart);

                return new RegexConditionalReferenceSyntax(openParenToken, nameToken, closeToken, Options);
            }
        }

        Scanner.Position = conditionStart;

        return null;
    }

    /// <summary>Reports the two headers a conditional's expression condition may not have.</summary>
    private protected virtual void ReportIllegalConditionHeader(int conditionStart)
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
    private RegexAtomSyntax ParseOptionsConstruct(ScannedToken openParenToken, int questionStart, bool inConditionalTest)
    {
        var questionToken = Scanner.Token(SyntaxKind.QuestionToken, questionStart);
        if (!Dialect.HasFeature(RegexDialectFeatures.InlineOptions))
        {
            AddDiagnostic(questionToken.Span, RegexDiagnosticIds.InvalidGroupingConstruct, $"Inline options are not supported by the {Dialect.Name} dialect.");
        }

        var optionsStart = Scanner.Position;
        var optionsToken = inConditionalTest || !Dialect.HasFeature(RegexDialectFeatures.InlineOptions) ? default : ScanInlineOptions(optionsStart);

        if (Scanner.Current == ')')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            var closeToken = Scanner.Token(SyntaxKind.CloseParenToken, closeStart);
            _ = OptionsStack.Pop();
            _ignoreNextParen = false;

            return new RegexInlineOptionsSyntax(openParenToken, questionToken, optionsToken, closeToken, Options, Options);
        }

        if (Scanner.Current != ':')
        {
            AddDiagnostic(
                TextSpan.FromBounds(openParenToken.Span.Start, Math.Max(openParenToken.Span.Start, Scanner.Position)),
                RegexDiagnosticIds.InvalidGroupingConstruct,
                "Invalid grouping construct.");

            var recoveredBody = ParseAlternation(insideGroup: true);
            var recoveredClose = ReadCloseParen(openParenToken);
            var recovered = new RegexOptionsGroupSyntax(openParenToken, questionToken, optionsToken, default, recoveredBody, recoveredClose, Options, recoveredBody.Options);
            RestoreOptions();

            return recovered;
        }

        var colonStart = Scanner.Position;
        Scanner.Position++;
        var colonToken = Scanner.Token(SyntaxKind.ColonToken, colonStart);
        _ignoreNextParen = false;

        var body = ParseAlternation(insideGroup: true);
        var closeParenToken = ReadCloseParen(openParenToken);
        var group = new RegexOptionsGroupSyntax(openParenToken, questionToken, optionsToken, colonToken, body, closeParenToken, Options, body.Options);
        RestoreOptions();

        return group;
    }

    /// <summary>Reads an <c>imnsx-imnsx</c> run and applies it, stopping at the first character it does not know.</summary>
    private protected virtual ScannedToken ScanInlineOptions(int start)
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

        return Scanner.Position > start ? Scanner.Token(SyntaxKind.OptionsToken, start) : default;
    }

    private protected ScannedToken ReadCloseParen(ScannedToken openParenToken)
    {
        var trivia = TakeTrivia();
        var closeLength = GroupCloseLength(Scanner.Position);
        if (closeLength > 0)
        {
            var start = Scanner.Position;
            Scanner.Position += closeLength;

            return Scanner.Token(SyntaxKind.CloseParenToken, start, trivia);
        }

        AddDiagnostic(
            TextSpan.FromBounds(openParenToken.Span.Start, Math.Max(openParenToken.Span.Start, Scanner.Position)),
            RegexDiagnosticIds.InsufficientClosingParentheses,
            "Unterminated group: expected ')'.");

        return Scanner.MissingToken(SyntaxKind.CloseParenToken, trivia);
    }

    private protected void RestoreOptions()
    {
        if (OptionsStack.Count > 0)
        {
            (Options, DuplicateNamesAllowed) = OptionsStack.Pop();
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
