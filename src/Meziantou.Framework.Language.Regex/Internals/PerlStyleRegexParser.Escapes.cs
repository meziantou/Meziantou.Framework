// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
//
// Changes: ScanBackslash, ScanBasicBackslash, ScanCharEscape, and their helpers produce tokens and diagnostics rather
// than RegexNode instances and exceptions. What each of them consumes is unchanged, which is what the differential
// test against the runtime checks.

namespace Meziantou.Framework.Language.Regex.Internals;

internal partial class PerlStyleRegexParser
{
    /// <summary>
    /// Whether <c>\p{…}</c> is recognized. JavaScript reads it as an identity escape unless the pattern opted into
    /// Unicode mode, so the answer depends on the options as well as the flavor.
    /// </summary>
    private bool SupportsUnicodeCategories =>
        Flavor.HasFeature(RegexFlavorFeatures.UnicodeCategories) &&
        (!Flavor.HasFeature(RegexFlavorFeatures.UnicodeCategoriesRequireUnicodeFlag) ||
            (Options & RegexPatternOptions.Unicode) != RegexPatternOptions.None);

    /// <summary>Parses a backslash escape used as an atom of a sequence.</summary>
    private RegexAtomSyntax ParseBackslashAtom(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;
        if (Scanner.Position + 1 >= Text.Length)
        {
            Scanner.Position = Text.Length;
            var stray = Scanner.Token(RegexSyntaxKind.BadToken, start, leadingTrivia);
            AddDiagnostic(TextSpan.FromBounds(start, Scanner.Position), RegexDiagnosticIds.UnescapedEndingBackslash, "The pattern ends with an unescaped backslash.");

            return WithOptions(new RegexSkippedTextSyntax([stray], stray.FullSpan.Start));
        }

        switch (Scanner.Peek())
        {
            case 'b':
            case 'B':
                Scanner.Position += 2;
                return WithOptions(new RegexAnchorSyntax(Scanner.Token(RegexSyntaxKind.AnchorToken, start, leadingTrivia)));

            // Where the flavor has no such anchor the escape is not an anchor at all: it falls through and stands for
            // the letter, which is what an engine without it does.
            case 'A' or 'G' or 'z' or 'Z' when Flavor.HasFeature(RegexFlavorFeatures.AnchorsAZ):
                Scanner.Position += 2;
                return WithOptions(new RegexAnchorSyntax(Scanner.Token(RegexSyntaxKind.AnchorToken, start, leadingTrivia)));

            case 'K' when Flavor.HasFeature(RegexFlavorFeatures.KeepOut):
                Scanner.Position += 2;
                return WithOptions(new RegexAnchorSyntax(Scanner.Token(RegexSyntaxKind.AnchorToken, start, leadingTrivia)));

            case var letter when IsShorthandClassLetter(letter):
                Scanner.Position += 2;
                return WithOptions(new RegexCharacterClassEscapeSyntax(Scanner.Token(RegexSyntaxKind.ClassEscapeToken, start, leadingTrivia)));

            case 'p' or 'P' when SupportsUnicodeCategories:
                return ParseUnicodeCategory(leadingTrivia);

            case 'Q' when Flavor.HasFeature(RegexFlavorFeatures.QuotedLiterals):
                return ParseQuotedLiteral(leadingTrivia);

            default:
                return TryParseFlavorEscape(leadingTrivia) ?? ParseBackreferenceOrEscape(leadingTrivia);
        }
    }

    /// <summary>Parses a <c>\Q…\E</c> run, in which every character stands for itself.</summary>
    /// <remarks>An unterminated run reaches the end of the pattern, which is what the engines that have it do.</remarks>
    private RegexQuotedLiteralSyntax ParseQuotedLiteral(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var startToken = Scanner.Token(RegexSyntaxKind.QuoteStartToken, start, leadingTrivia);

        var textStart = Scanner.Position;
        var end = Text.AsSpan(textStart).IndexOf("\\E", StringComparison.Ordinal);
        Scanner.Position = end < 0 ? Text.Length : textStart + end;
        var textToken = Scanner.Position > textStart ? Scanner.Token(RegexSyntaxKind.QuoteTextToken, textStart) : null;

        RegexSyntaxToken? endToken = null;
        if (end >= 0)
        {
            var endStart = Scanner.Position;
            Scanner.Position += 2;
            endToken = Scanner.Token(RegexSyntaxKind.QuoteEndToken, endStart);
        }

        return WithOptions(new RegexQuotedLiteralSyntax(startToken, textToken, endToken));
    }

    /// <summary>Parses <c>\p{Name}</c> or <c>\P{Name}</c>.</summary>
    /// <remarks>
    /// Ported from <c>ParseProperty</c>. The engine's first guard is a length check that rejects <c>\p</c> near the end
    /// of the pattern before it looks at anything, which is why an incomplete escape is reported as invalid rather than
    /// malformed.
    /// </remarks>
    private RegexUnicodeCategorySyntax ParseUnicodeCategory(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var categoryStartToken = Scanner.Token(RegexSyntaxKind.CategoryStartToken, start, leadingTrivia);

        if (Scanner.Position + 2 > Text.Length)
        {
            AddDiagnostic(categoryStartToken.Span, RegexDiagnosticIds.InvalidUnicodePropertyEscape, "Incomplete '\\p{...}' character escape.");

            return WithOptions(new RegexUnicodeCategorySyntax(categoryStartToken, null, null, null));
        }

        if (Scanner.Current != '{')
        {
            AddDiagnostic(categoryStartToken.Span, RegexDiagnosticIds.MalformedUnicodePropertyEscape, "Malformed '\\p{...}' character escape.");

            return WithOptions(new RegexUnicodeCategorySyntax(categoryStartToken, null, null, null));
        }

        var braceStart = Scanner.Position;
        Scanner.Position++;
        var openBraceToken = Scanner.Token(RegexSyntaxKind.OpenBraceToken, braceStart);

        // Flavors that name a property as well as a value accept "Script=Greek", so the separator has to be part of
        // the name rather than the character that ends it.
        var namesProperties = Flavor.HasFeature(RegexFlavorFeatures.UnicodePropertyNames);
        var nameStart = Scanner.Position;

        // "\p{^L}" is the other way of writing "\P{L}" where the flavor has it.
        if (namesProperties && Scanner.Current == '^')
        {
            Scanner.Position++;
        }

        while (!Scanner.IsAtEnd &&
            (RegexCharacterTables.IsBoundaryWordChar(Scanner.Current) || Scanner.Current == '-' || (namesProperties && Scanner.Current == '=')))
        {
            Scanner.Position++;
        }

        var name = Text[nameStart..Scanner.Position];
        var nameToken = Scanner.Token(RegexSyntaxKind.CategoryNameToken, nameStart);

        RegexSyntaxToken? closeBraceToken = null;
        if (Scanner.Current == '}')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            closeBraceToken = Scanner.Token(RegexSyntaxKind.CloseBraceToken, closeStart);

            // The known-name set is .NET's own. Another flavor has a different and larger one, so checking a name
            // against this table there would reject properties that flavor really does have.
            if (!namesProperties && !NetUnicodeCategoryNames.IsDefined(name))
            {
                AddDiagnostic(nameToken.Span, RegexDiagnosticIds.UnrecognizedUnicodeProperty, $"Unknown Unicode property or block name '{name}'.");
            }
        }
        else
        {
            AddDiagnostic(
                TextSpan.FromBounds(categoryStartToken.Span.Start, Math.Max(categoryStartToken.Span.Start, Scanner.Position)),
                RegexDiagnosticIds.InvalidUnicodePropertyEscape,
                "Incomplete '\\p{...}' character escape.");
        }

        return WithOptions(new RegexUnicodeCategorySyntax(categoryStartToken, openBraceToken, nameToken, closeBraceToken));
    }

    /// <summary>Parses a backreference, or falls back to a character escape.</summary>
    /// <remarks>
    /// Ported from <c>ScanBasicBackslash</c>, including the asymmetry that makes <c>\10</c> the octal escape for a
    /// backspace when the pattern has fewer than ten groups while <c>\5</c> with two groups is an undefined reference.
    /// </remarks>
    private RegexAtomSyntax ParseBackreferenceOrEscape(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var backpos = Scanner.Position;
        Scanner.Position++;

        var angled = false;
        var close = '\0';
        var ch = Scanner.Current;
        RegexSyntaxToken? startToken = null;
        RegexSyntaxToken? openNameToken = null;

        if (ch == 'k')
        {
            if (Scanner.Position + 1 < Text.Length)
            {
                Scanner.Position++;
                startToken = Scanner.Token(RegexSyntaxKind.NamedBackreferenceStartToken, backpos, leadingTrivia);

                var openStart = Scanner.Position;
                ch = Text[Scanner.Position++];
                if (ch is '<' or '\'')
                {
                    angled = true;
                    close = ch == '\'' ? '\'' : '>';
                    openNameToken = Scanner.Token(RegexSyntaxKind.OpenNameToken, openStart);
                }
                else
                {
                    Scanner.Position = openStart;
                }
            }

            if (!angled || Scanner.IsAtEnd)
            {
                Scanner.Position = backpos;
                Scanner.Position += Math.Min(2, Text.Length - backpos);
                var malformed = Scanner.Token(RegexSyntaxKind.NamedBackreferenceStartToken, backpos, leadingTrivia);
                AddDiagnostic(malformed.Span, RegexDiagnosticIds.MalformedNamedReference, "Malformed '\\k<...>' named backreference.");

                return WithOptions(new RegexNamedBackreferenceSyntax(malformed, null, null, null));
            }

            ch = Scanner.Current;
        }
        else if (ch is '<' or '\'' && Scanner.Position + 1 < Text.Length)
        {
            angled = true;
            close = ch == '\'' ? '\'' : '>';
            startToken = Scanner.Token(RegexSyntaxKind.NamedBackreferenceStartToken, backpos, leadingTrivia);

            var openStart = Scanner.Position;
            Scanner.Position++;
            openNameToken = Scanner.Token(RegexSyntaxKind.OpenNameToken, openStart);
            ch = Scanner.Current;
        }

        if (angled && char.IsAsciiDigit(ch))
        {
            var nameStart = Scanner.Position;
            var number = ReadDecimal(out _);
            var nameToken = Scanner.Token(RegexSyntaxKind.NameToken, nameStart);
            if (!Scanner.IsAtEnd && Text[Scanner.Position] == close)
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeNameToken = Scanner.Token(RegexSyntaxKind.CloseNameToken, closeStart);
                if (!CaptureTable.ContainsNumber(number))
                {
                    AddDiagnostic(nameToken.Span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {FormatNumber(number)}.");
                }

                return WithOptions(new RegexNamedBackreferenceSyntax(startToken!, openNameToken, nameToken, closeNameToken));
            }
        }
        else if (!angled && ch is >= '1' and <= '9')
        {
            if (TryParseUnangledBackreference(backpos, leadingTrivia, out var backreference))
                return backreference;
        }
        else if (angled && RegexCharacterTables.IsBoundaryWordChar(ch))
        {
            var nameStart = Scanner.Position;
            var name = ReadCaptureName();
            var nameToken = Scanner.Token(RegexSyntaxKind.NameToken, nameStart);
            if (!Scanner.IsAtEnd && Text[Scanner.Position] == close)
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeNameToken = Scanner.Token(RegexSyntaxKind.CloseNameToken, closeStart);
                if (!CaptureTable.TryGetNumber(name, out _))
                {
                    AddDiagnostic(nameToken.Span, RegexDiagnosticIds.UndefinedNamedReference, $"Reference to undefined group name '{name}'.");
                }

                return WithOptions(new RegexNamedBackreferenceSyntax(startToken!, openNameToken, nameToken, closeNameToken));
            }
        }

        // Not a backreference after all: rewind and read the whole thing as a character escape.
        Scanner.Position = backpos + 1;
        var value = ScanCharEscape();

        return WithOptions(new RegexCharacterEscapeSyntax(Scanner.Token(RegexSyntaxKind.EscapeToken, backpos, leadingTrivia, value)));
    }

    /// <summary>Reads <c>\1</c>-style backreferences, which are octal escapes when no such group exists.</summary>
    private bool TryParseUnangledBackreference(int backpos, IReadOnlyList<RegexSyntaxTrivia> leadingTrivia, out RegexAtomSyntax result)
    {
        if (UsesEcmaScriptBehavior)
        {
            // ECMAScript takes the longest prefix of the digits that names a group declared before this point.
            var capnum = -1;
            var newcapnum = Scanner.Current - '0';
            var pos = Scanner.Position;
            while (true)
            {
                if (CaptureTable.ContainsNumber(newcapnum) && CaptureTable.GetPosition(newcapnum) < pos)
                {
                    capnum = newcapnum;
                }

                Scanner.Position++;
                if (Scanner.IsAtEnd || !char.IsAsciiDigit(Scanner.Current))
                    break;

                newcapnum = (newcapnum * 10) + (Scanner.Current - '0');
            }

            if (capnum >= 0)
            {
                result = WithOptions(new RegexBackreferenceSyntax(Scanner.Token(RegexSyntaxKind.BackreferenceToken, backpos, leadingTrivia, FormatNumber(capnum))));

                return true;
            }
        }
        else
        {
            var number = ReadDecimal(out _);
            if (CaptureTable.ContainsNumber(number))
            {
                result = WithOptions(new RegexBackreferenceSyntax(Scanner.Token(RegexSyntaxKind.BackreferenceToken, backpos, leadingTrivia, FormatNumber(number))));

                return true;
            }

            if (number <= 9)
            {
                var token = Scanner.Token(RegexSyntaxKind.BackreferenceToken, backpos, leadingTrivia, FormatNumber(number));
                AddDiagnostic(token.Span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {FormatNumber(number)}.");
                result = WithOptions(new RegexBackreferenceSyntax(token));

                return true;
            }
        }

        result = null!;

        return false;
    }

    /// <summary>Reads the body of an escape that stands for a single character, and returns that character.</summary>
    /// <remarks>The reading position is on the character after the backslash.</remarks>
    private string ScanCharEscape()
    {
        // The backslash sits one before the reading position, so a diagnostic can point at the whole escape.
        var escapeStart = Scanner.Position - 1;
        var ch = Text[Scanner.Position++];

        if (ch is >= '0' and <= '7')
        {
            Scanner.Position--;

            return ScanOctal();
        }

        switch (ch)
        {
            case 'x':
                return ScanHex(2, escapeStart);

            // In Unicode mode "\u{10FFFF}" names a code point directly, so the braces are part of the escape rather
            // than a bound applied to the letter.
            case 'u' when UsesUnicodeMode && Scanner.Current == '{':
                return ScanBracedCodePoint(escapeStart);

            case 'u':
                return ScanHex(4, escapeStart);

            case 'a':
                return "\a";

            case 'b':
                return "\b";

            case 'e':
                return "\u001b";

            case 'f':
                return "\f";

            case 'n':
                return "\n";

            case 'r':
                return "\r";

            case 't':
                return "\t";

            case 'v':
                return "\v";

            case 'c':
                return ScanControl(escapeStart);

            default:
                if (Flavor.HasFeature(RegexFlavorFeatures.StrictEscapes) &&
                    !UsesEcmaScriptBehavior &&
                    RegexCharacterTables.IsBoundaryWordChar(ch))
                {
                    AddDiagnostic(TextSpan.FromBounds(escapeStart, Scanner.Position), RegexDiagnosticIds.UnrecognizedEscape, $"Unrecognized escape sequence '\\{ch}'.");
                }

                return ch.ToString();
        }
    }

    /// <summary>Reads up to three octal digits, stopping before the value exceeds 0377.</summary>
    private string ScanOctal()
    {
        var count = Math.Min(3, Text.Length - Scanner.Position);
        var value = 0;
        while (count > 0 && (uint)(Scanner.Current - '0') <= 7)
        {
            var digit = Scanner.Current - '0';
            Scanner.Position++;
            count--;
            value = (value * 8) + digit;

            // ECMAScript stops as soon as the value could no longer be a control character.
            if (UsesEcmaScriptBehavior && value >= 0x20)
                break;
        }

        // Octal codes only go up to 255; Perl truncates the high bits and so does the engine.
        return ((char)(value & 0xFF)).ToString();
    }

    /// <summary>Reads <c>{HHHH}</c> after <c>\u</c> and returns the code point it names.</summary>
    private string ScanBracedCodePoint(int escapeStart)
    {
        Scanner.Position++;

        var digitsStart = Scanner.Position;
        var value = 0;
        var overflowed = false;
        while (!Scanner.IsAtEnd && FromHexChar(Scanner.Current) >= 0)
        {
            value = (value * 0x10) + FromHexChar(Scanner.Current);
            if (value > 0x10FFFF)
            {
                overflowed = true;
                value = 0x10FFFF;
            }

            Scanner.Position++;
        }

        var hasDigits = Scanner.Position > digitsStart;
        var closed = Scanner.Current == '}';
        if (closed)
        {
            Scanner.Position++;
        }

        if (!hasDigits || !closed || overflowed)
        {
            AddDiagnostic(
                TextSpan.FromBounds(escapeStart, Scanner.Position),
                RegexDiagnosticIds.InsufficientOrInvalidHexDigits,
                "The code point escape is not a well-formed '\\u{...}' value.");

            return string.Empty;
        }

        return char.ConvertFromUtf32(value);
    }

    /// <summary>Reads exactly <paramref name="count"/> hexadecimal digits.</summary>
    private string ScanHex(int count, int escapeStart)
    {
        var value = 0;
        var remaining = count;

        if (Scanner.Position + count <= Text.Length)
        {
            for (; remaining > 0; remaining--)
            {
                var digit = FromHexChar(Text[Scanner.Position]);
                if (digit < 0)
                    break;

                Scanner.Position++;
                value = (value * 0x10) + digit;
            }
        }

        if (remaining > 0)
        {
            AddDiagnostic(
                TextSpan.FromBounds(escapeStart, Scanner.Position),
                RegexDiagnosticIds.InsufficientOrInvalidHexDigits,
                "The hexadecimal escape does not have enough valid digits.");
        }

        return ((char)value).ToString();
    }

    private static int FromHexChar(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'a' and <= 'f' => ch - 'a' + 10,
        >= 'A' and <= 'F' => ch - 'A' + 10,
        _ => -1,
    };

    /// <summary>Reads the character of a <c>\c</c> control escape and converts it.</summary>
    private string ScanControl(int escapeStart)
    {
        if (Scanner.IsAtEnd)
        {
            AddDiagnostic(TextSpan.FromBounds(escapeStart, Scanner.Position), RegexDiagnosticIds.MissingControlCharacter, "The '\\c' escape is missing its control character.");

            return string.Empty;
        }

        var ch = Text[Scanner.Position++];

        // \ca is read as \cA.
        if ((uint)(ch - 'a') <= 'z' - 'a')
        {
            ch = (char)(ch - ('a' - 'A'));
        }

        ch = (char)(ch - '@');
        if (ch < ' ')
            return ch.ToString();

        AddDiagnostic(TextSpan.FromBounds(escapeStart, Scanner.Position), RegexDiagnosticIds.UnrecognizedControlCharacter, "Unrecognized control character in '\\c' escape.");

        return string.Empty;
    }
}
