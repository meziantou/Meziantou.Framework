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

using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using ScannedToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

internal partial class PerlStyleRegexParser
{
    /// <summary>
    /// Whether <c>\p{…}</c> is recognized. JavaScript reads it as an identity escape unless the pattern opted into
    /// Unicode mode, so the answer depends on the options as well as the dialect.
    /// </summary>
    private bool SupportsUnicodeCategories =>
        Dialect.HasFeature(RegexDialectFeatures.UnicodeCategories) &&
        (!Dialect.HasFeature(RegexDialectFeatures.UnicodeCategoriesRequireUnicodeFlag) ||
            (Options & RegexPatternOptions.Unicode) != RegexPatternOptions.None);

    /// <summary>Parses a backslash escape used as an atom of a sequence.</summary>
    private RegexAtomSyntax ParseBackslashAtom(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        if (Scanner.Position + 1 >= Text.Length)
        {
            Scanner.Position = Text.Length;
            var stray = Scanner.Token(SyntaxKind.BadToken, start, leadingTrivia);
            AddDiagnostic(TextSpan.FromBounds(start, Scanner.Position), RegexDiagnosticIds.UnescapedEndingBackslash, "The pattern ends with an unescaped backslash.");

            return new RegexSkippedTextSyntax(stray, Options);
        }

        switch (Scanner.Peek())
        {
            case 'b':
            case 'B':
                Scanner.Position += 2;
                return new RegexAnchorSyntax(Scanner.Token(SyntaxKind.AnchorToken, start, leadingTrivia), Options);

            // Where the dialect has no such anchor the escape is not an anchor at all: it falls through and stands for
            // the letter, which is what an engine without it does.
            case 'A' or 'G' or 'z' or 'Z' when Dialect.HasFeature(RegexDialectFeatures.AnchorsAZ):
                Scanner.Position += 2;
                return new RegexAnchorSyntax(Scanner.Token(SyntaxKind.AnchorToken, start, leadingTrivia), Options);

            case 'K' when Dialect.HasFeature(RegexDialectFeatures.KeepOut):
                Scanner.Position += 2;
                return new RegexAnchorSyntax(Scanner.Token(SyntaxKind.AnchorToken, start, leadingTrivia), Options);

            case var letter when IsShorthandClassLetter(letter):
                Scanner.Position += 2;
                return new RegexCharacterClassEscapeSyntax(Scanner.Token(SyntaxKind.ClassEscapeToken, start, leadingTrivia), Options);

            case 'p' or 'P' when SupportsUnicodeCategories:
                return ParseUnicodeCategory(leadingTrivia);

            case 'Q' when Dialect.HasFeature(RegexDialectFeatures.QuotedLiterals):
                return ParseQuotedLiteral(leadingTrivia);

            // "\E" with no "\Q" in front of it closes nothing and matches nothing, which the engines simply ignore.
            case 'E' when Dialect.HasFeature(RegexDialectFeatures.QuotedLiterals):
                Scanner.Position += 2;
                return new RegexCharacterEscapeSyntax(Scanner.Token(SyntaxKind.EscapeToken, start, leadingTrivia, string.Empty), Options);

            default:
                return TryParseDialectEscape(leadingTrivia) ?? ParseBackreferenceOrEscape(leadingTrivia);
        }
    }

    /// <summary>Parses a <c>\Q…\E</c> run, in which every character stands for itself.</summary>
    /// <remarks>An unterminated run reaches the end of the pattern, which is what the engines that have it do.</remarks>
    private RegexQuotedLiteralSyntax ParseQuotedLiteral(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var startToken = Scanner.Token(SyntaxKind.QuoteStartToken, start, leadingTrivia);

        var textStart = Scanner.Position;
        var end = Text.AsSpan(textStart).IndexOf("\\E", StringComparison.Ordinal);
        Scanner.Position = end < 0 ? Text.Length : textStart + end;
        var textToken = Scanner.Position > textStart ? Scanner.Token(SyntaxKind.QuoteTextToken, textStart) : default;

        ScannedToken endToken = default;
        if (end >= 0)
        {
            var endStart = Scanner.Position;
            Scanner.Position += 2;
            endToken = Scanner.Token(SyntaxKind.QuoteEndToken, endStart);
        }

        return new RegexQuotedLiteralSyntax(startToken, textToken, endToken, Options);
    }

    /// <summary>Parses <c>\p{Name}</c> or <c>\P{Name}</c>.</summary>
    /// <remarks>
    /// Ported from <c>ParseProperty</c>. The engine's first guard is a length check that rejects <c>\p</c> near the end
    /// of the pattern before it looks at anything, which is why an incomplete escape is reported as invalid rather than
    /// malformed.
    /// </remarks>
    private RegexUnicodeCategorySyntax ParseUnicodeCategory(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position += 2;
        var categoryStartToken = Scanner.Token(SyntaxKind.CategoryStartToken, start, leadingTrivia);

        if (Scanner.Position + 2 > Text.Length && !(AllowsBracelessProperty && !Scanner.IsAtEnd && char.IsAsciiLetter(Scanner.Current)))
        {
            AddDiagnostic(categoryStartToken.Span, RegexDiagnosticIds.InvalidUnicodePropertyEscape, "Incomplete '\\p{...}' character escape.");

            return new RegexUnicodeCategorySyntax(categoryStartToken, null, null, null, Options);
        }

        if (Scanner.Current != '{')
        {
            // PCRE lets a single-letter property stand without braces.
            if (AllowsBracelessProperty && char.IsAsciiLetter(Scanner.Current))
            {
                var letterStart = Scanner.Position;
                Scanner.Position++;

                return new RegexUnicodeCategorySyntax(categoryStartToken, null, Scanner.Token(SyntaxKind.CategoryNameToken, letterStart), null, Options);
            }

            AddDiagnostic(categoryStartToken.Span, RegexDiagnosticIds.MalformedUnicodePropertyEscape, "Malformed '\\p{...}' character escape.");

            return new RegexUnicodeCategorySyntax(categoryStartToken, null, null, null, Options);
        }

        var braceStart = Scanner.Position;
        Scanner.Position++;
        var openBraceToken = Scanner.Token(SyntaxKind.OpenBraceToken, braceStart);

        // Dialects that name a property as well as a value accept "Script=Greek", so the separator has to be part of
        // the name rather than the character that ends it.
        var namesProperties = Dialect.HasFeature(RegexDialectFeatures.UnicodePropertyNames);
        var nameStart = Scanner.Position;

        // "\p{^L}" is the other way of writing "\P{L}" where the dialect has it.
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
        var nameToken = Scanner.Token(SyntaxKind.CategoryNameToken, nameStart);

        ScannedToken closeBraceToken = default;
        if (Scanner.Current == '}')
        {
            var closeStart = Scanner.Position;
            Scanner.Position++;
            closeBraceToken = Scanner.Token(SyntaxKind.CloseBraceToken, closeStart);

            // An empty name is wrong in every dialect, whatever set of names it recognizes.
            if (name.Length == 0)
            {
                AddDiagnostic(nameToken.Span, RegexDiagnosticIds.UnrecognizedUnicodeProperty, "The property name is empty.");
            }

            // The known-name set is .NET's own. Another dialect has a different and larger one, so checking a name
            // against this table there would reject properties that dialect really does have.
            else if (!namesProperties && !NetUnicodeCategoryNames.IsDefined(name))
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

        return new RegexUnicodeCategorySyntax(categoryStartToken, openBraceToken, nameToken, closeBraceToken, Options);
    }

    /// <summary>Parses a backreference, or falls back to a character escape.</summary>
    /// <remarks>
    /// Ported from <c>ScanBasicBackslash</c>, including the asymmetry that makes <c>\10</c> the octal escape for a
    /// backspace when the pattern has fewer than ten groups while <c>\5</c> with two groups is an undefined reference.
    /// </remarks>
    private RegexAtomSyntax ParseBackreferenceOrEscape(GreenNode? leadingTrivia)
    {
        var backpos = Scanner.Position;
        Scanner.Position++;

        var angled = false;
        var close = '\0';
        var ch = Scanner.Current;
        ScannedToken startToken = default;
        ScannedToken openNameToken = default;

        // "\k" introduces a named backreference only where the dialect has named groups at all.
        if (ch == 'k' && Dialect.HasFeature(RegexDialectFeatures.NamedGroups))
        {
            if (Scanner.Position + 1 < Text.Length)
            {
                Scanner.Position++;
                startToken = Scanner.Token(SyntaxKind.NamedBackreferenceStartToken, backpos, leadingTrivia);

                var openStart = Scanner.Position;
                ch = Text[Scanner.Position++];
                if (ch is '<' or '\'')
                {
                    angled = true;
                    close = ch == '\'' ? '\'' : '>';
                    openNameToken = Scanner.Token(SyntaxKind.OpenNameToken, openStart);
                }
                else
                {
                    Scanner.Position = openStart;
                }
            }

            if ((!angled || Scanner.IsAtEnd) && AllowsUndefinedNamedBackreference && !HasAnyGroupName)
            {
                // With no named group anywhere in the pattern there is nothing "\k" could refer to, so it is the
                // letter rather than a malformed reference.
                Scanner.Position = backpos + 1;
                var identity = ScanCharEscape();

                return new RegexCharacterEscapeSyntax(Scanner.Token(SyntaxKind.EscapeToken, backpos, leadingTrivia, identity), Options);
            }

            if (!angled || Scanner.IsAtEnd)
            {
                Scanner.Position = backpos;
                Scanner.Position += Math.Min(2, Text.Length - backpos);
                var malformed = Scanner.Token(SyntaxKind.NamedBackreferenceStartToken, backpos, leadingTrivia);
                AddDiagnostic(malformed.Span, RegexDiagnosticIds.MalformedNamedReference, "Malformed '\\k<...>' named backreference.");

                return new RegexNamedBackreferenceSyntax(malformed, null, null, null, Options);
            }

            ch = Scanner.Current;
        }
        else if (ch is '<' or '\'' && Scanner.Position + 1 < Text.Length && AllowsBareAngleBackreference)
        {
            angled = true;
            close = ch == '\'' ? '\'' : '>';
            startToken = Scanner.Token(SyntaxKind.NamedBackreferenceStartToken, backpos, leadingTrivia);

            var openStart = Scanner.Position;
            Scanner.Position++;
            openNameToken = Scanner.Token(SyntaxKind.OpenNameToken, openStart);
            ch = Scanner.Current;
        }

        if (angled && char.IsAsciiDigit(ch))
        {
            var nameStart = Scanner.Position;
            var number = ReadDecimal(out _);
            var nameToken = Scanner.Token(SyntaxKind.NameToken, nameStart);
            if (!Scanner.IsAtEnd && Text[Scanner.Position] == close)
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeNameToken = Scanner.Token(SyntaxKind.CloseNameToken, closeStart);
                if (!CaptureTable.ContainsNumber(number))
                {
                    AddDiagnostic(nameToken.Span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {FormatNumber(number)}.");
                }

                return new RegexNamedBackreferenceSyntax(startToken, openNameToken, nameToken, closeNameToken, Options);
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
            var nameToken = Scanner.Token(SyntaxKind.NameToken, nameStart);
            if (!Scanner.IsAtEnd && Text[Scanner.Position] == close)
            {
                var closeStart = Scanner.Position;
                Scanner.Position++;
                var closeNameToken = Scanner.Token(SyntaxKind.CloseNameToken, closeStart);
                if (!CaptureTable.TryGetNumber(name, out _) && !(AllowsUndefinedNamedBackreference && !HasAnyGroupName))
                {
                    AddDiagnostic(nameToken.Span, RegexDiagnosticIds.UndefinedNamedReference, $"Reference to undefined group name '{name}'.");
                }

                return new RegexNamedBackreferenceSyntax(startToken, openNameToken, nameToken, closeNameToken, Options);
            }
        }

        // Not a backreference after all: rewind and read the whole thing as a character escape.
        Scanner.Position = backpos + 1;
        var value = ScanCharEscape();

        return new RegexCharacterEscapeSyntax(Scanner.Token(SyntaxKind.EscapeToken, backpos, leadingTrivia, value), Options);
    }

    /// <summary>Reads <c>\1</c>-style backreferences, which are octal escapes when no such group exists.</summary>
    private bool TryParseUnangledBackreference(int backpos, GreenNode? leadingTrivia, out RegexAtomSyntax result)
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
                result = new RegexBackreferenceSyntax(Scanner.Token(SyntaxKind.BackreferenceToken, backpos, leadingTrivia, FormatNumber(capnum)), Options);

                return true;
            }

            // Nothing to refer back to. Where octal is not a fallback the reference is simply undefined.
            if (!AllowsOctalEscape)
            {
                var undefined = Scanner.Token(SyntaxKind.BackreferenceToken, backpos, leadingTrivia, "0");
                AddDiagnostic(undefined.Span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {undefined.Text[1..]}.");
                result = new RegexBackreferenceSyntax(undefined, Options);

                return true;
            }
        }
        else
        {
            var number = ReadDecimal(out _);
            if (CaptureTable.ContainsNumber(number))
            {
                result = new RegexBackreferenceSyntax(Scanner.Token(SyntaxKind.BackreferenceToken, backpos, leadingTrivia, FormatNumber(number)), Options);

                return true;
            }

            if (number <= 9)
            {
                var token = Scanner.Token(SyntaxKind.BackreferenceToken, backpos, leadingTrivia, FormatNumber(number));
                AddDiagnostic(token.Span, RegexDiagnosticIds.UndefinedNumberedReference, $"Reference to undefined group number {FormatNumber(number)}.");
                result = new RegexBackreferenceSyntax(token, Options);

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

        // Where none of these escapes exist, a backslash before a character simply means that character.
        if (!RecognizesPerlCharacterEscapes)
            return ch.ToString();

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
                if (!AllowsIdentityEscape(ch))
                {
                    AddDiagnostic(TextSpan.FromBounds(escapeStart, Scanner.Position), RegexDiagnosticIds.UnrecognizedEscape, $"Unrecognized escape sequence '\\{ch}'.");
                }

                return ch.ToString();
        }
    }

    /// <summary>Reads up to three octal digits, stopping before the value exceeds 0377.</summary>
    private string ScanOctal()
    {
        var octalStart = Scanner.Position;
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

        // A lone "\0" is the null character everywhere; it is the digits after it that make it an octal escape.
        if (!AllowsOctalEscape && (Scanner.Position - octalStart > 1 || Text[octalStart] != '0'))
        {
            AddDiagnostic(
                TextSpan.FromBounds(Math.Max(0, octalStart - 1), Scanner.Position),
                RegexDiagnosticIds.UnrecognizedEscape,
                "Octal escapes are not allowed in this mode.");
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

        if (Scanner.Position + count <= Text.Length || AllowsShortHexEscape)
        {
            for (; remaining > 0; remaining--)
            {
                if (Scanner.IsAtEnd)
                    break;

                var digit = FromHexChar(Text[Scanner.Position]);
                if (digit < 0)
                    break;

                Scanner.Position++;
                value = (value * 0x10) + digit;
            }
        }

        if (remaining > 0 && remaining < count && AllowsShortHexEscape)
        {
            // PCRE accepts "\xh" as well as "\xhh".
            return ((char)value).ToString();
        }

        if (remaining > 0)
        {
            // Where the dialect allows it, an escape that is not well formed is not an error: it stands for its own
            // letter, and the characters it failed to consume are read again as ordinary text.
            if (AllowsMalformedNumericEscape)
            {
                Scanner.Position = escapeStart + 2;

                return Text[escapeStart + 1].ToString();
            }

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
        if (Scanner.IsAtEnd || (AllowsMalformedNumericEscape && !char.IsAsciiLetter(Scanner.Current)))
        {
            if (AllowsMalformedNumericEscape)
                return Text[escapeStart..Scanner.Position];

            AddDiagnostic(TextSpan.FromBounds(escapeStart, Scanner.Position), RegexDiagnosticIds.MissingControlCharacter, "The '\\c' escape is missing its control character.");

            return string.Empty;
        }

        if (!AllowsNonLetterControlEscape && !char.IsAsciiLetter(Scanner.Current))
        {
            Scanner.Position++;
            AddDiagnostic(TextSpan.FromBounds(escapeStart, Scanner.Position), RegexDiagnosticIds.UnrecognizedControlCharacter, "The '\\c' escape must be followed by a letter.");

            return string.Empty;
        }

        var ch = Text[Scanner.Position++];

        // \ca is read as \cA.
        if ((uint)(ch - 'a') <= 'z' - 'a')
        {
            ch = (char)(ch - ('a' - 'A'));
        }

        ch = (char)(ch - '@');
        if (ch < ' ' || AllowsAnyControlEscapeCharacter)
            return ch.ToString();

        if (AllowsMalformedNumericEscape)
        {
            Scanner.Position = escapeStart + 2;

            return Text[escapeStart..Scanner.Position];
        }

        AddDiagnostic(TextSpan.FromBounds(escapeStart, Scanner.Position), RegexDiagnosticIds.UnrecognizedControlCharacter, "Unrecognized control character in '\\c' escape.");

        return string.Empty;
    }
}
