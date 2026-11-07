using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Json.Internals;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>Turns JSON text into tokens, keeping everything it cannot give meaning to as trivia.</summary>
/// <remarks>
/// Trivia is split the way it reads: whitespace and comments up to and including the end of a line belong to the
/// token that ends that line, and everything after belongs to the token that follows. That is what makes a comment on
/// its own line attach to the thing it describes rather than to the thing above it.
/// </remarks>
internal sealed class Lexer(SourceText source)
{
    private readonly string _text = source.Text;
    private List<SyntaxDiagnosticInfo>? _tokenDiagnostics;
    private int _tokenBodyStart;

    /// <summary>Gets or sets where the next token will be read from.</summary>
    public int Position { get; set; }

    public GreenToken Lex()
    {
        _tokenDiagnostics = null;

        var fullStart = Position;
        var leading = LexTrivia(isTrailing: false);
        _tokenBodyStart = Position;

        var kind = ScanTokenBody(out var text, out var valueText);
        var trailing = LexTrivia(isTrailing: true);

        var token = kind switch
        {
            SyntaxKind.StringToken => SyntaxFactory.TokenWithValue(leading, kind, text, valueText!, trailing),
            SyntaxKind.BadToken => SyntaxFactory.BadToken(leading, text, trailing),
            SyntaxKind.EndOfFileToken => SyntaxFactory.Token(leading, kind, text, trailing),
            _ when SyntaxFacts.GetText(kind).Length > 0 => SyntaxFactory.Token(leading, kind, trailing),
            _ => SyntaxFactory.Token(leading, kind, text, trailing),
        };

        if (_tokenDiagnostics is not { Count: > 0 })
            return token;

        // Diagnostics are recorded against the text, and stored against the start of the token, trivia included.
        var leadingWidth = _tokenBodyStart - fullStart;
        var moved = _tokenDiagnostics.Select(info => info.WithOffset(info.Offset + leadingWidth)).ToArray();

        return (GreenToken)token.WithAdditionalDiagnostics(moved);
    }

    private SyntaxKind ScanTokenBody(out string text, out string? valueText)
    {
        valueText = null;
        if (IsAtEnd)
        {
            text = "";

            return SyntaxKind.EndOfFileToken;
        }

        var start = Position;
        switch (Current)
        {
            case '{':
                return Punctuation(SyntaxKind.OpenBraceToken, out text);
            case '}':
                return Punctuation(SyntaxKind.CloseBraceToken, out text);
            case '[':
                return Punctuation(SyntaxKind.OpenBracketToken, out text);
            case ']':
                return Punctuation(SyntaxKind.CloseBracketToken, out text);
            case ':':
                return Punctuation(SyntaxKind.ColonToken, out text);
            case ',':
                return Punctuation(SyntaxKind.CommaToken, out text);
            case '"' or '\'':
                return ScanString(out text, out valueText);
            case '-' or '+' or (>= '0' and <= '9'):
            case '.' when IsDigit(LookAhead):
                return ScanNumber(out text);
            default:
                while (!IsAtTokenEnd)
                {
                    Position++;
                }

                if (Position == start)
                {
                    // A character that ends nothing and starts nothing still has to be consumed, or lexing stalls.
                    Position++;
                }

                text = _text[start..Position];
                var keyword = SyntaxFacts.GetKeywordKind(text);

                return keyword == SyntaxKind.None ? SyntaxKind.BadToken : keyword;
        }
    }

    private SyntaxKind Punctuation(SyntaxKind kind, out string text)
    {
        text = SyntaxFacts.GetText(kind);
        Position++;

        return kind;
    }

    /// <summary>Reads a string, which ends at its closing quote or at the end of its line, whichever comes first.</summary>
    /// <remarks>
    /// JSON strings cannot contain a line break, so one that reaches the end of its line was never closed. Stopping
    /// there keeps the lines below out of it: otherwise a single missing quote would turn the rest of the document into
    /// one string, and every quote after it would open or close the wrong one.
    /// </remarks>
    private SyntaxKind ScanString(out string text, out string valueText)
    {
        var start = Position;
        var quote = Current;
        var builder = new StringBuilder();
        Position++;

        var terminated = false;
        while (!IsAtEnd)
        {
            var current = Current;
            if (current == quote)
            {
                Position++;
                terminated = true;
                break;
            }

            if (current is '\r' or '\n')
                break;

            if (current == '\\')
            {
                ScanEscapeSequence(builder, quote);
                continue;
            }

            if (current < ' ')
            {
                AddDiagnostic(Position, 1, JsonDiagnosticDescriptors.ControlCharacterInString, ToCodePoint(current));
            }

            builder.Append(current);
            Position++;
        }

        if (!terminated)
        {
            AddDiagnostic(start, Position - start, JsonDiagnosticDescriptors.UnterminatedString);
        }

        // Read as a string all the same, so a value written with the wrong quotes is still where it belongs in the tree.
        if (quote == '\'')
        {
            AddDiagnostic(start, Position - start, JsonDiagnosticDescriptors.SingleQuotedString);
        }

        text = _text[start..Position];
        valueText = builder.ToString();

        return SyntaxKind.StringToken;
    }

    private void ScanEscapeSequence(StringBuilder builder, char quote)
    {
        var escapeStart = Position;
        Position++;

        // A backslash that ends the text or the line has nothing to escape; the string is reported as unterminated.
        if (IsAtEnd || Current is '\r' or '\n')
            return;

        switch (Current)
        {
            case '"':
            case '\\':
            case '/':
                builder.Append(Current);
                Position++;
                break;
            case '\'' when quote == '\'':
                // The quotes are already reported; escaping the one that delimits the string is what such a string does.
                builder.Append(Current);
                Position++;
                break;
            case 'b':
                builder.Append('\b');
                Position++;
                break;
            case 'f':
                builder.Append('\f');
                Position++;
                break;
            case 'n':
                builder.Append('\n');
                Position++;
                break;
            case 'r':
                builder.Append('\r');
                Position++;
                break;
            case 't':
                builder.Append('\t');
                Position++;
                break;
            case 'u':
                ScanUnicodeEscape(builder, escapeStart);
                break;
            default:
                AddDiagnostic(escapeStart, 2, JsonDiagnosticDescriptors.InvalidEscapeSequence);
                builder.Append(Current);
                Position++;
                break;
        }
    }

    /// <summary>Reads the four hex digits after <c>\u</c>, stopping at the first character that is not one.</summary>
    /// <remarks>Stopping there matters: that character may be the closing quote, which must still end the string.</remarks>
    private void ScanUnicodeEscape(StringBuilder builder, int escapeStart)
    {
        Position++;

        var value = 0;
        for (var digits = 0; digits < 4; digits++)
        {
            var digit = IsAtEnd ? -1 : GetHexValue(Current);
            if (digit < 0)
            {
                AddDiagnostic(escapeStart, Position - escapeStart, JsonDiagnosticDescriptors.InvalidUnicodeEscapeSequence);
                return;
            }

            value = (value * 16) + digit;
            Position++;
        }

        builder.Append((char)value);
    }

    /// <summary>Reads a number, and anything glued to it.</summary>
    /// <remarks>
    /// Whatever runs on up to the next token boundary is part of the same mistake -- <c>0x1F</c>, <c>1.2.3</c>,
    /// <c>-Infinity</c>, <c>+1</c> -- so it is kept in the one token and reported once, rather than split into a number
    /// followed by a stray word that would also be reported as a missing comma.
    /// </remarks>
    private SyntaxKind ScanNumber(out string text)
    {
        var start = Position;
        var malformed = false;
        if (Current is '-' or '+')
        {
            malformed = Current == '+';
            Position++;
        }

        var integerStart = Position;
        SkipDigits();
        if (Position == integerStart)
        {
            malformed = true;
        }
        else if (Position - integerStart > 1 && _text[integerStart] == '0')
        {
            AddDiagnostic(start, Position - start, JsonDiagnosticDescriptors.LeadingZero);
        }

        if (Current == '.')
        {
            Position++;
            var fractionStart = Position;
            SkipDigits();
            if (Position == fractionStart)
            {
                AddDiagnostic(fractionStart, 0, JsonDiagnosticDescriptors.ExpectedFractionDigit);
            }
        }

        if (Current is 'e' or 'E')
        {
            Position++;
            if (Current is '+' or '-')
            {
                Position++;
            }

            var exponentStart = Position;
            SkipDigits();
            if (Position == exponentStart)
            {
                AddDiagnostic(exponentStart, 0, JsonDiagnosticDescriptors.ExpectedExponentDigit);
            }
        }

        if (!IsAtTokenEnd)
        {
            malformed = true;
            while (!IsAtTokenEnd)
            {
                Position++;
            }
        }

        if (malformed)
        {
            _tokenDiagnostics = null;
            AddDiagnostic(start, Position - start, JsonDiagnosticDescriptors.InvalidNumber);
        }

        text = _text[start..Position];

        return SyntaxKind.NumberToken;
    }

    private void SkipDigits()
    {
        while (IsDigit(Current))
        {
            Position++;
        }
    }

    private GreenNode? LexTrivia(bool isTrailing)
    {
        List<GreenNode?>? trivia = null;
        while (!IsAtEnd)
        {
            var start = Position;
            if (Current is ' ' or '\t')
            {
                while (!IsAtEnd && Current is ' ' or '\t')
                {
                    Position++;
                }

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // RFC 8259 lets a parser ignore a byte order mark at the start of the text.
            if (Position == 0 && Current == '\uFEFF')
            {
                Position++;
                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // Anything else that looks like whitespace is kept as whitespace, so the tokens around it still read the
            // way they were meant to, but JSON only has four whitespace characters.
            if (IsNonJsonWhitespace(Current))
            {
                while (!IsAtEnd && IsNonJsonWhitespace(Current))
                {
                    Position++;
                }

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start, JsonDiagnosticDescriptors.InvalidWhitespace, ToCodePoint(_text[start]));
                continue;
            }

            if (Current is '\r' or '\n')
            {
                Position += SourceText.GetLineBreakLength(_text, Position);
                Add(ref trivia, SyntaxKind.EndOfLineTrivia, start);

                // The line is over, so whatever comes next belongs to the next token.
                if (isTrailing)
                    break;

                continue;
            }

            if (Current == '/' && LookAhead == '/')
            {
                Position += 2;
                while (!IsAtEnd && Current is not '\r' and not '\n')
                {
                    Position++;
                }

                Add(ref trivia, SyntaxKind.SingleLineCommentTrivia, start);
                continue;
            }

            if (Current == '/' && LookAhead == '*')
            {
                Position += 2;
                while (!IsAtEnd && !(Current == '*' && LookAhead == '/'))
                {
                    Position++;
                }

                if (IsAtEnd)
                {
                    Add(ref trivia, SyntaxKind.MultiLineCommentTrivia, start, JsonDiagnosticDescriptors.UnterminatedBlockComment);
                    continue;
                }

                Position += 2;
                Add(ref trivia, SyntaxKind.MultiLineCommentTrivia, start);
                continue;
            }

            break;
        }

        return trivia is null ? null : SyntaxFactory.List(trivia.ToArray());
    }

    private void Add(ref List<GreenNode?>? trivia, SyntaxKind kind, int start, DiagnosticDescriptor? descriptor = null, params object?[]? arguments)
    {
        GreenNode item = SyntaxFactory.Trivia(kind, _text[start..Position]);
        if (descriptor is not null)
        {
            item = item.WithAdditionalDiagnostics(new SyntaxDiagnosticInfo(0, Position - start, descriptor, arguments));
        }

        (trivia ??= []).Add(item);
    }

    private void AddDiagnostic(int start, int width, DiagnosticDescriptor descriptor, params object?[]? arguments)
        => (_tokenDiagnostics ??= []).Add(new SyntaxDiagnosticInfo(start - _tokenBodyStart, Math.Max(0, width), descriptor, arguments));

    private bool IsAtEnd => Position >= _text.Length;
    private char Current => Position < _text.Length ? _text[Position] : '\0';
    private char LookAhead => Position + 1 < _text.Length ? _text[Position + 1] : '\0';

    /// <summary>Gets whether the current character cannot continue a number or a bare word.</summary>
    private bool IsAtTokenEnd => IsAtEnd || Current switch
    {
        ' ' or '\t' or '\r' or '\n' or '{' or '}' or '[' or ']' or ':' or ',' or '"' => true,
        '/' => LookAhead is '/' or '*',
        var value => IsNonJsonWhitespace(value),
    };

    private static bool IsDigit(char value) => value is >= '0' and <= '9';

    private static bool IsNonJsonWhitespace(char value)
        => value is not (' ' or '\t' or '\r' or '\n') && (char.IsWhiteSpace(value) || value == '\uFEFF');

    private static string ToCodePoint(char value) => ((int)value).ToString("X4", CultureInfo.InvariantCulture);

    private static int GetHexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };
}
