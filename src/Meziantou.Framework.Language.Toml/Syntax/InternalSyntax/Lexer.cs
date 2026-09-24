using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Turns TOML text into tokens, keeping layout and comments as trivia.</summary>
internal sealed class Lexer(SourceText source)
{
    private readonly string _text = source.Text;

    /// <summary>Gets or sets where the next token will be read from.</summary>
    public int Position { get; set; }

    public GreenToken Lex()
    {
        var leading = LexTrivia(includeEndOfLine: true, includeComments: true);

        if (IsAtEnd)
            return SyntaxFactory.Token(leading, SyntaxKind.EndOfFileToken, trailing: null);

        var start = Position;
        switch (Current)
        {
            case '[':
                return Punctuation(leading, SyntaxKind.OpenBracketToken, Current == '[' && LookAhead == '[' ? 2 : 1);
            case ']':
                return Punctuation(leading, SyntaxKind.CloseBracketToken, Current == ']' && LookAhead == ']' ? 2 : 1);
            case '=':
                return Punctuation(leading, SyntaxKind.EqualsToken);
            default:
                if (Current is '"' or '\'')
                {
                    var quote = Current;
                    Position++;
                    while (!IsAtEnd)
                    {
                        var escaped = Position > start && _text[Position - 1] == '\\';
                        if (Current == quote && !escaped)
                        {
                            Position++;
                            break;
                        }

                        Position++;
                    }

                    return SyntaxFactory.Token(leading, SyntaxKind.KeyToken, _text[start..Position], trailing: null);
                }

                while (!IsAtEnd && !IsTokenDelimiter(Current))
                {
                    Position++;
                }

                if (Position == start)
                {
                    Position++;

                    return SyntaxFactory.BadToken(leading, _text[start..Position], trailing: null);
                }

                return SyntaxFactory.Token(leading, SyntaxKind.KeyToken, _text[start..Position], trailing: null);
        }
    }

    public GreenToken LexValue()
    {
        var leading = LexTrivia(includeEndOfLine: false, includeComments: false);
        var start = Position;
        var depth = 0;
        var quote = '\0';
        while (!IsAtEnd)
        {
            if (quote is not '\0')
            {
                if (Current == quote && (Position == start || _text[Position - 1] != '\\'))
                    quote = '\0';
                Position++;
                continue;
            }

            if (Current is '"' or '\'')
            {
                quote = Current;
                Position++;
                continue;
            }

            if (Current is '[' or '{')
                depth++;
            else if (Current is ']' or '}')
                depth--;
            else if (depth <= 0 && Current is '\r' or '\n')
                break;
            else if (depth <= 0 && Current == '#')
                break;

            Position++;
        }

        var text = _text[start..Position];
        var trailing = LexTrivia(includeEndOfLine: true, includeComments: true);

        return SyntaxFactory.Token(leading, SyntaxKind.ValueToken, text, trailing);
    }

    private GreenToken Punctuation(GreenNode? leading, SyntaxKind kind, int width = 1)
    {
        var text = _text[Position..(Position + width)];
        Position += width;

        return SyntaxFactory.Token(leading, kind, text, trailing: null);
    }

    private GreenNode? LexTrivia(bool includeEndOfLine, bool includeComments)
    {
        List<GreenNode?>? trivia = null;
        while (!IsAtEnd)
        {
            var start = Position;
            SyntaxKind kind;
            if (Current is ' ' or '\t')
            {
                Position++;
                while (!IsAtEnd && Current is ' ' or '\t')
                {
                    Position++;
                }

                kind = SyntaxKind.WhitespaceTrivia;
            }
            else if (includeEndOfLine && Current is '\r' or '\n')
            {
                if (Current == '\r' && LookAhead == '\n')
                {
                    Position += 2;
                }
                else
                {
                    Position++;
                }

                kind = SyntaxKind.EndOfLineTrivia;
            }
            else if (includeComments && Current == '#')
            {
                Position++;
                while (!IsAtEnd && Current is not '\r' and not '\n')
                {
                    Position++;
                }

                kind = SyntaxKind.CommentTrivia;
            }
            else
            {
                break;
            }

            trivia ??= [];
            trivia.Add(SyntaxFactory.Trivia(kind, _text[start..Position]));
        }

        return trivia is null ? null : SyntaxFactory.List(trivia.ToArray());
    }

    private bool IsAtEnd => Position >= _text.Length;
    private char Current => Position < _text.Length ? _text[Position] : '\0';
    private char LookAhead => Position + 1 < _text.Length ? _text[Position + 1] : '\0';

    private static bool IsTokenDelimiter(char value)
        => value is '[' or ']' or '=' or '#' or '\r' or '\n' or ' ' or '\t';
}
