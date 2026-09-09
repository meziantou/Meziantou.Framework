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
            case '"':
                return ScanString(out text, out valueText);
            case '-' or (>= '0' and <= '9'):
                return ScanNumber(out text);
            default:
                while (!IsAtEnd && !IsTokenBoundary(Current))
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

    private SyntaxKind ScanString(out string text, out string valueText)
    {
        var start = Position;
        var builder = new StringBuilder();
        Position++;

        var terminated = false;
        while (!IsAtEnd)
        {
            var current = Current;
            if (current == '"')
            {
                Position++;
                terminated = true;
                break;
            }

            if (current == '\\')
            {
                ScanEscapeSequence(builder, start);
                continue;
            }

            if (current is '\r' or '\n')
            {
                AddDiagnostic(Position, SourceText.GetLineBreakLength(_text, Position), JsonDiagnosticDescriptors.LineBreakInString);
            }

            builder.Append(current);
            Position++;
        }

        if (!terminated)
        {
            AddDiagnostic(start, _text.Length - start, JsonDiagnosticDescriptors.UnterminatedString);
        }

        text = _text[start..Position];
        valueText = builder.ToString();

        return SyntaxKind.StringToken;
    }

    private void ScanEscapeSequence(StringBuilder builder, int stringStart)
    {
        var escapeStart = Position;
        Position++;
        if (IsAtEnd)
        {
            AddDiagnostic(stringStart, _text.Length - stringStart, JsonDiagnosticDescriptors.UnterminatedString);
            return;
        }

        switch (Current)
        {
            case '"':
            case '\\':
            case '/':
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
                AddDiagnostic(escapeStart, Math.Min(2, _text.Length - escapeStart), JsonDiagnosticDescriptors.InvalidEscapeSequence);
                builder.Append(Current);
                Position++;
                break;
        }
    }

    private void ScanUnicodeEscape(StringBuilder builder, int escapeStart)
    {
        if (Position + 4 >= _text.Length)
        {
            AddDiagnostic(escapeStart, _text.Length - escapeStart, JsonDiagnosticDescriptors.InvalidUnicodeEscapeSequence);
            Position++;
            return;
        }

        var value = 0;
        for (var index = 1; index <= 4; index++)
        {
            var digit = GetHexValue(_text[Position + index]);
            if (digit < 0)
            {
                AddDiagnostic(escapeStart, 6, JsonDiagnosticDescriptors.InvalidUnicodeEscapeSequence);
                Position++;
                return;
            }

            value = (value * 16) + digit;
        }

        builder.Append((char)value);
        Position += 5;
    }

    private SyntaxKind ScanNumber(out string text)
    {
        var start = Position;
        var hasDigits = false;
        if (Current == '-')
        {
            Position++;
        }

        if (Current == '0')
        {
            hasDigits = true;
            Position++;
            if (Current is >= '0' and <= '9')
            {
                AddDiagnostic(start, Position - start + 1, JsonDiagnosticDescriptors.LeadingZero);
                while (Current is >= '0' and <= '9')
                {
                    Position++;
                }
            }
        }
        else
        {
            while (Current is >= '0' and <= '9')
            {
                hasDigits = true;
                Position++;
            }
        }

        if (!hasDigits)
        {
            AddDiagnostic(start, Position - start, JsonDiagnosticDescriptors.InvalidNumber);
        }

        if (Current == '.')
        {
            Position++;
            var fractionStart = Position;
            while (Current is >= '0' and <= '9')
            {
                Position++;
            }

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
            while (Current is >= '0' and <= '9')
            {
                Position++;
            }

            if (Position == exponentStart)
            {
                AddDiagnostic(exponentStart, 0, JsonDiagnosticDescriptors.ExpectedExponentDigit);
            }
        }

        text = _text[start..Position];

        return SyntaxKind.NumberToken;
    }

    private GreenNode? LexTrivia(bool isTrailing)
    {
        List<GreenNode?>? trivia = null;
        while (!IsAtEnd)
        {
            var start = Position;
            if (Current is ' ' or '\t' or '\f' or '\v')
            {
                while (!IsAtEnd && Current is ' ' or '\t' or '\f' or '\v')
                {
                    Position++;
                }

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
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

    private void Add(ref List<GreenNode?>? trivia, SyntaxKind kind, int start, DiagnosticDescriptor? descriptor = null)
    {
        GreenNode item = SyntaxFactory.Trivia(kind, _text[start..Position]);
        if (descriptor is not null)
        {
            item = item.WithAdditionalDiagnostics(new SyntaxDiagnosticInfo(0, Position - start, descriptor));
        }

        (trivia ??= []).Add(item);
    }

    private void AddDiagnostic(int start, int width, DiagnosticDescriptor descriptor, params object?[]? arguments)
        => (_tokenDiagnostics ??= []).Add(new SyntaxDiagnosticInfo(start - _tokenBodyStart, Math.Max(0, width), descriptor, arguments));

    private bool IsAtEnd => Position >= _text.Length;
    private char Current => Position < _text.Length ? _text[Position] : '\0';
    private char LookAhead => Position + 1 < _text.Length ? _text[Position + 1] : '\0';

    private static bool IsTokenBoundary(char value)
        => value is '\0' or ' ' or '\t' or '\f' or '\v' or '\r' or '\n' or '{' or '}' or '[' or ']' or ':' or ',' or '"';

    private static int GetHexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1,
    };
}
