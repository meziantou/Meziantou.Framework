using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>Turns INI text into tokens, keeping layout and comments as trivia.</summary>
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
                return Punctuation(leading, SyntaxKind.OpenBracketToken);
            case ']':
                return Punctuation(leading, SyntaxKind.CloseBracketToken);
            case '=':
                return Punctuation(leading, SyntaxKind.EqualsToken);
            case ':':
                return Punctuation(leading, SyntaxKind.ColonToken);
            default:
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
        while (!IsAtEnd && Current is not '\r' and not '\n' and not ';' and not '#')
        {
            Position++;
        }

        var text = _text[start..Position];
        var trailing = LexTrivia(includeEndOfLine: true, includeComments: true);

        return SyntaxFactory.Token(leading, SyntaxKind.ValueToken, text, trailing);
    }

    private GreenToken Punctuation(GreenNode? leading, SyntaxKind kind)
    {
        Position++;

        return SyntaxFactory.Token(leading, kind, trailing: null);
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
            else if (includeComments && Current is ';' or '#')
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
        => value is '[' or ']' or '=' or ':' or ';' or '#' or '\r' or '\n' or ' ' or '\t';
}
