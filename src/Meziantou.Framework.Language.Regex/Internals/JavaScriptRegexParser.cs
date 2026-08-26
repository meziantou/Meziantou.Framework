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
        if (_literal is not { HasClosingSlash: true })
            return (null, null, null);

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
            ReportUnknownFlags(flagsToken);
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
    protected override bool IsAtBodyEnd(int position) => _literal is { HasClosingSlash: true } literal
        ? position >= literal.BodyEnd
        : base.IsAtBodyEnd(position);

    private void ReportUnknownFlags(RegexSyntaxToken flagsToken)
    {
        var seen = 0;
        foreach (var flag in flagsToken.Text)
        {
            if (flag is 'd' or 'g' or 'i' or 'm' or 's' or 'u' or 'v' or 'y')
            {
                seen++;
                continue;
            }

            AddDiagnostic(new TextSpan(flagsToken.Span.Start + seen, 1), RegexDiagnosticIds.UnknownFlag, $"Unknown regular-expression flag '{flag}'.");
            seen++;
        }
    }
}
