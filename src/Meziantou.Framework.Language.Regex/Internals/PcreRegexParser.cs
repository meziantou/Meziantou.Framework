namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Parses a pattern the way PCRE and Perl do.</summary>
/// <remarks>
/// Most of PCRE is the Perl grammar the shared parser already reads, narrowed or widened by flavor features. What is
/// here is what PCRE has and the others do not: the <c>\g</c> reference family, subroutine calls, callouts, the extra
/// shorthand classes, and the two numeric escapes with braces.
/// </remarks>
internal sealed class PcreRegexParser : PerlStyleRegexParser
{
    public PcreRegexParser(string text, RegexParseOptions parseOptions)
        : base(text, parseOptions)
    {
    }

    /// <summary><c>\R</c> and <c>\X</c> stand for a set of characters but may not appear inside a class.</summary>
    /// <remarks>
    /// <c>\N</c> is "any character except a newline" on its own, but <c>\N{…}</c> names a code point. The brace is
    /// what tells them apart, so the shorthand has to decline when one follows.
    /// </remarks>
    protected override bool IsShorthandClassLetter(char letter) =>
        IsShorthandClassLetterInClass(letter) ||
        letter is 'R' or 'X' ||
        (letter == 'N' && Scanner.Peek(2) != '{');

    protected override bool IsShorthandClassLetterInClass(char letter) =>
        base.IsShorthandClassLetterInClass(letter) || letter is 'h' or 'H' or 'v' or 'V';

    /// <summary><c>J</c> and <c>U</c> change matching rather than syntax, but they still have to be accepted.</summary>
    protected override bool TryMapOptionLetter(char letter, out RegexPatternOptions option)
    {
        if (base.TryMapOptionLetter(letter, out option))
            return true;

        return letter is 'J' or 'U' or 'a' or 'u';
    }

    protected override bool AllowsEmptyOptionGroup => true;

    protected override bool AllowsShortHexEscape => true;

    protected override bool AllowsAnyControlEscapeCharacter => true;

    protected override bool AllowsBracelessProperty => true;

    protected override RegexAtomSyntax? TryParseFlavorGroupHeader(RegexSyntaxToken openParenToken, int questionStart)
    {
        // A subroutine call runs another group's pattern at this point. "(?&name)" is the Perl spelling and
        // "(?P>name)" the Python one; both are recursion by name rather than by number.
        if (Scanner.Current == '&' || (Scanner.Current == 'P' && Scanner.Peek() == '>'))
            return ParseNamedRecursion(openParenToken, questionStart);

        // "(?P=name)" is Python's spelling of a named backreference.
        if (Scanner.Current == 'P' && Scanner.Peek() == '=')
            return ParsePythonNamedBackreference(openParenToken, questionStart);

        if (Scanner.Current == 'C')
            return ParseCallout(openParenToken, questionStart);

        return null;
    }

    protected override RegexAtomSyntax? TryParseFlavorEscape(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        return Scanner.Peek() switch
        {
            'g' => ParseGReference(leadingTrivia),
            'o' when Scanner.Peek(2) == '{' => ParseBracedNumericEscape(leadingTrivia, octal: true),
            'x' when Scanner.Peek(2) == '{' => ParseBracedNumericEscape(leadingTrivia, octal: false, hexOnly: true),
            'N' when Scanner.Peek(2) == '{' => ParseBracedNumericEscape(leadingTrivia, octal: false),
            _ => null,
        };
    }

    /// <summary>
    /// Parses the <c>\g</c> family: <c>\g1</c>, <c>\g{1}</c>, <c>\g{-1}</c>, and <c>\g{name}</c> are backreferences,
    /// while <c>\g&lt;name&gt;</c> and <c>\g'name'</c> are subroutine calls.
    /// </summary>
    private RegexAtomSyntax ParseGReference(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var startToken = Scanner.Token(RegexSyntaxKind.NamedBackreferenceStartToken, start, leadingTrivia);

        // The angled and quoted spellings call a group rather than referring back to what it matched.
        if (Scanner.Current is '<' or '\'')
        {
            var close = Scanner.Current == '\'' ? '\'' : '>';
            var openStart = Scanner.Position;
            Scanner.Position++;
            var openToken = Scanner.Token(RegexSyntaxKind.OpenNameToken, openStart);
            var target = ReadUntil(close, RegexSyntaxKind.RecursionToken);
            ReportUnknownRecursionTarget(target);
            var closeToken = ReadExpected(close, RegexSyntaxKind.CloseNameToken, startToken)
                ?? Scanner.MissingToken(RegexSyntaxKind.CloseNameToken);

            return WithOptions(new RegexRecursionSyntax(startToken, openToken, target, closeToken));
        }

        if (Scanner.Current == '{')
        {
            var openStart = Scanner.Position;
            Scanner.Position++;
            var openToken = Scanner.Token(RegexSyntaxKind.OpenNameToken, openStart);
            var nameStart = Scanner.Position;
            var name = ReadUntil('}', RegexSyntaxKind.NameToken);
            var closeToken = ReadExpected('}', RegexSyntaxKind.CloseNameToken, startToken);
            ReportUnknownReference(name, nameStart);

            return WithOptions(new RegexNamedBackreferenceSyntax(startToken, openToken, name, closeToken));
        }

        // "\g1" and "\g-1": a bare number, possibly relative.
        var numberStart = Scanner.Position;
        if (Scanner.Current == '-')
        {
            Scanner.Position++;
        }

        while (char.IsAsciiDigit(Scanner.Current))
        {
            Scanner.Position++;
        }

        if (Scanner.Position == numberStart)
        {
            AddDiagnostic(startToken.Span, RegexDiagnosticIds.MalformedNamedReference, "Malformed '\\g' reference.");

            return WithOptions(new RegexNamedBackreferenceSyntax(startToken, null, null, null));
        }

        var numberToken = Scanner.Token(RegexSyntaxKind.NameToken, numberStart);
        ReportUnknownReference(numberToken, numberStart);

        return WithOptions(new RegexNamedBackreferenceSyntax(startToken, null, numberToken, null));
    }

    /// <summary>Parses <c>(?&amp;name)</c> and <c>(?P&gt;name)</c>.</summary>
    private RegexRecursionSyntax ParseNamedRecursion(RegexSyntaxToken openParenToken, int questionStart)
    {
        Scanner.Position += Scanner.Current == '&' ? 1 : 2;
        var questionToken = Scanner.Token(RegexSyntaxKind.QuestionToken, questionStart);

        var target = ReadUntil(')', RegexSyntaxKind.RecursionToken);
        ReportUnknownRecursionTarget(target);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return WithOptions(new RegexRecursionSyntax(openParenToken, questionToken, target, closeParenToken));
    }

    /// <summary>Parses <c>(?P=name)</c>.</summary>
    private RegexNamedBackreferenceSyntax ParsePythonNamedBackreference(RegexSyntaxToken openParenToken, int questionStart)
    {
        Scanner.Position += 2;
        var markerToken = Scanner.Token(RegexSyntaxKind.NamedBackreferenceStartToken, questionStart);

        var nameStart = Scanner.Position;
        var name = ReadUntil(')', RegexSyntaxKind.NameToken);
        var closeToken = ReadCloseParen(openParenToken);
        RestoreOptions();
        ReportUnknownReference(name, nameStart);

        return WithOptions(new RegexNamedBackreferenceSyntax(openParenToken, markerToken, name, closeToken));
    }

    /// <summary>Parses <c>(?C)</c>, <c>(?C1)</c>, and <c>(?C"text")</c>.</summary>
    private RegexCalloutSyntax ParseCallout(RegexSyntaxToken openParenToken, int questionStart)
    {
        Scanner.Position++;
        var questionToken = Scanner.Token(RegexSyntaxKind.QuestionToken, questionStart);

        var body = Scanner.Current == ')' ? null : ReadUntil(')', RegexSyntaxKind.CalloutToken);
        var closeParenToken = ReadCloseParen(openParenToken);
        RestoreOptions();

        return WithOptions(new RegexCalloutSyntax(openParenToken, questionToken, body, closeParenToken));
    }

    /// <summary>Parses <c>\o{101}</c> and <c>\N{U+0041}</c>, both of which name a code point in braces.</summary>
    private RegexCharacterEscapeSyntax ParseBracedNumericEscape(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia, bool octal, bool hexOnly = false)
    {
        var start = Scanner.Position;
        Scanner.Position += 3;

        var digitsStart = Scanner.Position;
        while (!Scanner.IsAtEnd && Scanner.Current != '}')
        {
            Scanner.Position++;
        }

        var digits = Text[digitsStart..Scanner.Position];
        var closed = Scanner.Current == '}';
        if (closed)
        {
            Scanner.Position++;
        }

        var value = TryReadCodePoint(hexOnly ? "U+" + digits : digits, octal, out var codePoint) && closed
            ? char.ConvertFromUtf32(codePoint)
            : string.Empty;

        if (value.Length == 0)
        {
            AddDiagnostic(
                TextSpan.FromBounds(start, Scanner.Position),
                RegexDiagnosticIds.InsufficientOrInvalidHexDigits,
                octal ? "The '\\o{...}' escape is not a well-formed octal value." : "The '\\N{U+...}' escape is not a well-formed code point.");
        }

        return WithOptions(new RegexCharacterEscapeSyntax(Scanner.Token(RegexSyntaxKind.EscapeToken, start, leadingTrivia, value)));
    }

    private static bool TryReadCodePoint(string digits, bool octal, out int codePoint)
    {
        codePoint = 0;
        var span = digits.AsSpan();
        if (!octal)
        {
            // "\N{U+xxxx}" names a code point in hexadecimal; the "U+" is part of the syntax.
            if (!span.StartsWith("U+", StringComparison.Ordinal))
                return false;

            span = span[2..];
        }

        if (span.Length == 0)
            return false;

        var radix = octal ? 8 : 16;
        foreach (var ch in span)
        {
            var digit = octal
                ? (ch is >= '0' and <= '7' ? ch - '0' : -1)
                : Uri.IsHexDigit(ch) ? Uri.FromHex(ch) : -1;

            if (digit < 0)
                return false;

            codePoint = (codePoint * radix) + digit;
            if (codePoint > 0x10FFFF)
                return false;
        }

        // Surrogates are not scalar values, so they cannot be written as a code point.
        return codePoint is < 0xD800 or > 0xDFFF;
    }

    /// <summary>Reads everything up to <paramref name="terminator"/>, without consuming it.</summary>
    private RegexSyntaxToken? ReadUntil(char terminator, RegexSyntaxKind kind)
    {
        var start = Scanner.Position;
        while (!Scanner.IsAtEnd && Scanner.Current != terminator)
        {
            Scanner.Position++;
        }

        return Scanner.Position > start ? Scanner.Token(kind, start) : null;
    }

    private RegexSyntaxToken? ReadExpected(char expected, RegexSyntaxKind kind, RegexSyntaxToken owner)
    {
        if (Scanner.Current != expected)
        {
            AddDiagnostic(owner.Span, RegexDiagnosticIds.MalformedNamedReference, $"Expected '{expected}' to close the reference.");

            return null;
        }

        var start = Scanner.Position;
        Scanner.Position++;

        return Scanner.Token(kind, start);
    }

    /// <summary>Reports a reference that names neither an existing group number nor an existing group name.</summary>
    /// <remarks>A relative reference such as <c>\g{-1}</c> is always in range here, so only absolute ones are checked.</remarks>
    private void ReportUnknownReference(RegexSyntaxToken? nameToken, int nameStart)
    {
        if (nameToken is null)
            return;

        var name = nameToken.Text;

        // "\g{-2}" counts back from here, so what it needs is two groups already declared rather than a group of
        // that number.
        if (name is ['-', ..] &&
            int.TryParse(name.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var back))
        {
            var declared = 0;
            foreach (var declaredNumber in CaptureTable.Numbers)
            {
                if (CaptureTable.GetPosition(declaredNumber) < nameStart)
                {
                    declared++;
                }
            }

            if (back == 0 || back > declared)
            {
                AddDiagnostic(new TextSpan(nameStart, name.Length), RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {name}.");
            }

            return;
        }

        if (name is ['-', ..])
            return;

        if (int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            if (!CaptureTable.ContainsNumber(number))
            {
                AddDiagnostic(new TextSpan(nameStart, name.Length), RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {name}.");
            }

            return;
        }

        if (!CaptureTable.TryGetNumber(name, out _))
        {
            AddDiagnostic(new TextSpan(nameStart, name.Length), RegexDiagnosticIds.UndefinedNamedReference, $"Reference to undefined group name '{name}'.");
        }
    }
}
