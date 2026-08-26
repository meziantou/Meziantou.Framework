namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Parses an ECMAScript pattern, and the delimiters and flags of a literal when there are any.</summary>
internal sealed class JavaScriptRegexParser : PerlStyleRegexParser
{
    private readonly JavaScriptLiteral? _literal;

    public JavaScriptRegexParser(string text, RegexParseOptions parseOptions, JavaScriptLiteral? literal)
        : base(text, parseOptions)
    {
        _literal = literal;
    }

    /// <summary>
    /// ECMAScript has two grammars, and which one applies is decided by the <c>u</c> and <c>v</c> flags rather than by
    /// the flavor.
    /// </summary>
    /// <remarks>
    /// Without them the web-compatibility grammar applies: an escape that is not well formed stands for its own
    /// letter, so <c>\x4</c> matches <c>x4</c> and <c>\k</c> in a pattern with no named group matches <c>k</c>. With
    /// them the grammar is strict, and only a syntax character or <c>/</c> may follow a backslash and mean itself.
    /// </remarks>
    private bool UsesStrictGrammar => UsesUnicodeMode;

    protected override bool AllowsIdentityEscape(char ch)
    {
        if (!UsesStrictGrammar)
            return true;

        // The strict grammar allows only these, plus "-" inside a character class.
        return ch is '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '/'
            || (ch == '-' && IsInCharacterClass);
    }

    protected override bool AllowsMalformedNumericEscape => !UsesStrictGrammar;

    protected override bool AllowsLoneQuantifierBracket => !UsesStrictGrammar;

    protected override bool AllowsOctalEscape => !UsesStrictGrammar;

    protected override bool AllowsNonLetterControlEscape => !UsesStrictGrammar;

    /// <summary>
    /// An assertion matches nothing, so repeating it means nothing. Lookahead is the one exception, and only in the
    /// web-compatibility grammar; lookbehind is never quantifiable.
    /// </summary>
    protected override bool IsQuantifiable(RegexTermSyntax term) => term switch
    {
        RegexAnchorSyntax => false,
        RegexLookaroundSyntax lookaround => !UsesStrictGrammar && !lookaround.IsLookbehind,
        _ => true,
    };

    protected override bool AllowsShorthandClassInRange => !UsesStrictGrammar;

    protected override bool AllowsUndefinedNamedBackreference => !UsesStrictGrammar;

    protected override RegexSyntaxToken? ReadLiteralPrefix()
    {
        if (_literal is not { HasOpeningSlash: true })
            return null;

        var start = Scanner.Position;
        Scanner.Position = _literal.BodyStart;

        return Scanner.Token(RegexSyntaxKind.SlashToken, start);
    }

    protected override (RegexSyntaxToken? CloseSlash, RegexSyntaxToken? Flags, RegexSyntaxToken? Trailing) ReadLiteralSuffix()
    {
        if (_literal is { LineTerminatorPosition: >= 0 } broken)
        {
            AddDiagnostic(
                new TextSpan(broken.LineTerminatorPosition, 1),
                RegexDiagnosticIds.LineTerminatorInLiteral,
                "A regular-expression literal cannot contain a line terminator.");

            return (null, null, ReadTrailingContent());
        }

        if (_literal is not { HasClosingSlash: true })
        {
            // An opening delimiter with nothing to close it is not a literal at all. The tree still covers the text so
            // it round-trips, but saying nothing about it would be wrong.
            if (_literal is { HasOpeningSlash: true })
            {
                AddDiagnostic(
                    new TextSpan(0, Math.Min(1, Text.Length)),
                    RegexDiagnosticIds.UnterminatedLiteral,
                    "Unterminated regular-expression literal: expected a closing '/'.");
            }

            return (null, null, null);
        }

        var slashStart = Scanner.Position;
        if (slashStart >= Text.Length || Text[slashStart] != '/')
            return (null, null, null);

        Scanner.Position++;
        var closeSlashToken = Scanner.Token(RegexSyntaxKind.SlashToken, slashStart);

        RegexSyntaxToken? flagsToken = null;
        if (Scanner.Position < _literal.FlagsEnd)
        {
            var flagsStart = Scanner.Position;
            Scanner.Position = _literal.FlagsEnd;
            flagsToken = Scanner.Token(RegexSyntaxKind.FlagsToken, flagsStart);
            ReportFlagProblems(flagsToken);
        }

        return (closeSlashToken, flagsToken, ReadTrailingContent());
    }

    /// <summary>Keeps whatever followed the flags, which a well-formed literal has none of.</summary>
    private RegexSyntaxToken? ReadTrailingContent()
    {
        if (Scanner.IsAtEnd)
            return null;

        var start = Scanner.Position;
        Scanner.Position = Text.Length;
        var token = Scanner.Token(RegexSyntaxKind.BadToken, start);
        AddDiagnostic(token.Span, RegexDiagnosticIds.TrailingContent, "Unexpected content after the regular-expression literal.");

        return token;
    }

    /// <summary>Stops the body at the closing delimiter, so the flags are not read as part of the pattern.</summary>
    protected override bool IsAtBodyEnd(int position) =>
        _literal is { HasClosingSlash: true } or { LineTerminatorPosition: >= 0 }
            ? position >= _literal.BodyEnd
            : base.IsAtBodyEnd(position);

    /// <summary>Reports the three ways a flag list can be wrong: unknown, repeated, or <c>u</c> together with <c>v</c>.</summary>
    private void ReportFlagProblems(RegexSyntaxToken flagsToken)
    {
        var text = flagsToken.Text;
        var seen = new HashSet<char>();

        for (var index = 0; index < text.Length; index++)
        {
            var flag = text[index];
            var span = new TextSpan(flagsToken.Span.Start + index, 1);

            if (flag is not ('d' or 'g' or 'i' or 'm' or 's' or 'u' or 'v' or 'y'))
            {
                AddDiagnostic(span, RegexDiagnosticIds.UnknownFlag, $"Unknown regular-expression flag '{flag}'.");
                continue;
            }

            if (!seen.Add(flag))
            {
                AddDiagnostic(span, RegexDiagnosticIds.DuplicateFlag, $"The regular-expression flag '{flag}' is repeated.");
            }
        }

        // The two Unicode modes are alternatives, not a pair: "v" is "u" plus the class set grammar.
        if (seen.Contains('u') && seen.Contains('v'))
        {
            AddDiagnostic(flagsToken.Span, RegexDiagnosticIds.ConflictingFlags, "The 'u' and 'v' flags cannot both be set.");
        }
    }
}
