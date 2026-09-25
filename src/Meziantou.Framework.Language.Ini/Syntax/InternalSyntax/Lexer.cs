using System.Text;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>Turns INI text into tokens, keeping whitespace, line breaks, and comments as trivia.</summary>
/// <remarks>
/// <para>
/// INI is read line by line, so what a run of characters is depends on where it starts; the parser says which through
/// the <see cref="LexerMode"/> it asks for. Keys, section names, and values run up to the character that ends them, so
/// they can hold spaces, and the whitespace around them is trivia.
/// </para>
/// <para>
/// Trivia is split the way it reads: whitespace and a comment up to and including the end of a line belong to the
/// token that ends that line, and everything after belongs to the token that follows. That is what makes a comment on
/// its own line attach to the entry it describes rather than to the one above it.
/// </para>
/// </remarks>
internal sealed class Lexer(SourceText source, IniParseOptions options)
{
    private readonly string _text = source.Text;

    /// <summary>Gets or sets where the next token will be read from.</summary>
    public int Position { get; set; }

    /// <summary>Reads the next token.</summary>
    /// <param name="mode">What the parser expects next.</param>
    /// <param name="indentation">For a value, how indented its key is; a line continues the value only when it is indented more.</param>
    public GreenToken Lex(LexerMode mode, int indentation = 0)
    {
        var leading = mode is LexerMode.Value or LexerMode.RestOfLine ? LexWhitespace() : LexLeadingTrivia();
        if (IsAtEnd && mode != LexerMode.Value)
            return SyntaxFactory.Token(leading, SyntaxKind.EndOfFileToken, trailing: null);

        return mode switch
        {
            LexerMode.LineStart => LexLineStart(leading),
            LexerMode.SectionName => LexSectionName(leading),
            LexerMode.Value => LexValue(leading, indentation),
            _ => LexRestOfLine(leading),
        };
    }

    /// <summary>Gets how many whitespace characters come before <paramref name="position"/> on its line.</summary>
    public int GetIndentation(int position)
    {
        var start = position;
        while (start > 0 && IsWhitespace(_text[start - 1]))
        {
            start--;
        }

        return position - start;
    }

    private GreenToken LexLineStart(GreenNode? leading)
    {
        switch (Current)
        {
            case '[':
                Position++;
                return SyntaxFactory.Token(leading, SyntaxKind.OpenBracketToken, LexTrailingTrivia(alwaysAllowComment: false));

            // A separator never has trailing trivia: the whitespace after it is the leading trivia of the value, which is
            // read next whether or not there is any.
            case '=':
                Position++;
                return SyntaxFactory.Token(leading, SyntaxKind.EqualsToken, trailing: null);

            case ':':
                Position++;
                return SyntaxFactory.Token(leading, SyntaxKind.ColonToken, trailing: null);
        }

        var start = Position;
        while (!IsAtEnd && Current is not ('=' or ':') && !IsEndOfLine(Current) && !IsInlineCommentStart(Position))
        {
            Position++;
        }

        return Text(leading, SyntaxKind.KeyToken, start);
    }

    private GreenToken LexSectionName(GreenNode? leading)
    {
        if (Current == ']')
        {
            Position++;

            // Nothing but a comment can follow a section header, so one is read as a comment whatever the mode.
            return SyntaxFactory.Token(leading, SyntaxKind.CloseBracketToken, LexTrailingTrivia(alwaysAllowComment: true));
        }

        var start = Position;
        while (!IsAtEnd && Current != ']' && !IsEndOfLine(Current) && !IsInlineCommentStart(Position))
        {
            Position++;
        }

        return Text(leading, SyntaxKind.KeyToken, start);
    }

    private GreenToken LexRestOfLine(GreenNode? leading)
    {
        var start = Position;
        while (!IsAtEnd && !IsEndOfLine(Current) && !IsInlineCommentStart(Position))
        {
            Position++;
        }

        return Text(leading, SyntaxKind.BadToken, start);
    }

    private GreenToken LexValue(GreenNode? leading, int indentation)
    {
        var start = Position;
        var contentEnd = ScanValueLine(allowQuotes: true, out var isQuoted);
        var valueText = isQuoted ? _text[(start + 1)..(contentEnd - 1)] : null;

        if (options.AllowMultilineValues)
        {
            StringBuilder? lines = null;
            SkipToEndOfLine();
            while (TryStartContinuationLine(indentation))
            {
                lines ??= new StringBuilder().Append(_text, start, contentEnd - start);

                var lineStart = Position;
                var lineEnd = ScanValueLine(allowQuotes: false, out _);
                lines.Append('\n').Append(_text, lineStart, lineEnd - lineStart);
                contentEnd = lineEnd;
                SkipToEndOfLine();
            }

            // The lines of the value are read without their indentation and without the comments between them.
            if (lines is not null)
            {
                valueText = lines.ToString();
            }
        }

        Position = contentEnd;
        var text = _text[start..contentEnd];
        var trailing = LexTrailingTrivia(alwaysAllowComment: false);

        return valueText is null
            ? SyntaxFactory.Token(leading, SyntaxKind.ValueToken, text, trailing)
            : SyntaxFactory.TokenWithValue(leading, SyntaxKind.ValueToken, text, valueText, valueText, trailing);
    }

    /// <summary>Reads one line of a value, and returns where its text ends once the whitespace after it is left out.</summary>
    /// <param name="allowQuotes">Whether a quote at the start of the line opens a quoted value.</param>
    /// <param name="isQuoted">Whether the whole text is one quoted string, such as <c>"a;b"</c>.</param>
    private int ScanValueLine(bool allowQuotes, out bool isQuoted)
    {
        var start = Position;
        var quoteEnd = -1;
        if (allowQuotes && Current is '"' or '\'')
        {
            // A comment cannot start inside quotes, so the quoted part is read as a whole. A quote that is not closed on
            // the line is an ordinary character.
            var close = _text.IndexOfAny([Current, '\r', '\n'], Position + 1);
            if (close >= 0 && _text[close] == Current)
            {
                Position = close + 1;
                quoteEnd = Position;
            }
        }

        while (!IsAtEnd && !IsEndOfLine(Current) && !IsInlineCommentStart(Position))
        {
            Position++;
        }

        var end = Position;
        while (end > start && IsWhitespace(_text[end - 1]))
        {
            end--;
        }

        isQuoted = quoteEnd == end;
        return end;
    }

    /// <summary>
    /// Moves to the start of the text of the next line when that line continues a value: it is indented more than the
    /// key, and it is neither blank nor a comment.
    /// </summary>
    private bool TryStartContinuationLine(int indentation)
    {
        if (IsAtEnd)
            return false;

        var lineStart = Position + (Current == '\r' && LookAhead == '\n' ? 2 : 1);
        var textStart = lineStart;
        while (textStart < _text.Length && IsWhitespace(_text[textStart]))
        {
            textStart++;
        }

        if (textStart - lineStart <= indentation || textStart >= _text.Length || _text[textStart] is '\r' or '\n' or ';' or '#')
            return false;

        Position = textStart;
        return true;
    }

    private void SkipToEndOfLine()
    {
        while (!IsAtEnd && !IsEndOfLine(Current))
        {
            Position++;
        }
    }

    /// <summary>Creates a token for the text from <paramref name="start"/>, leaving the whitespace after it to the trailing trivia.</summary>
    private GreenToken Text(GreenNode? leading, SyntaxKind kind, int start)
    {
        var end = Position;
        while (end > start && IsWhitespace(_text[end - 1]))
        {
            end--;
        }

        Position = end;
        var text = _text[start..end];
        var trailing = LexTrailingTrivia(alwaysAllowComment: false);

        return kind == SyntaxKind.BadToken ? SyntaxFactory.BadToken(leading, text, trailing) : SyntaxFactory.Token(leading, kind, text, trailing);
    }

    /// <summary>Reads the whitespace, line breaks, and whole-line comments in front of a token.</summary>
    private GreenNode? LexLeadingTrivia()
    {
        List<GreenNode?>? trivia = null;
        while (!IsAtEnd)
        {
            var start = Position;
            if (TryLexWhitespace() || TryLexEndOfLine())
            {
                Add(ref trivia, start);
                continue;
            }

            if (Current is ';' or '#' && (IsAtLineStart(Position) || IsInlineCommentStart(Position)))
            {
                LexComment();
                Add(ref trivia, start);
                continue;
            }

            break;
        }

        return ToList(trivia);
    }

    /// <summary>Reads what follows a token on its line: whitespace, then a comment, then the end of the line.</summary>
    /// <param name="alwaysAllowComment">Whether <c>;</c> or <c>#</c> starts a comment even where the mode says it does not.</param>
    private GreenNode? LexTrailingTrivia(bool alwaysAllowComment)
    {
        List<GreenNode?>? trivia = null;
        var start = Position;
        if (TryLexWhitespace())
        {
            Add(ref trivia, start);
        }

        start = Position;
        if (Current is ';' or '#' && (alwaysAllowComment || IsInlineCommentStart(Position)))
        {
            LexComment();
            Add(ref trivia, start);
        }

        start = Position;
        if (TryLexEndOfLine())
        {
            Add(ref trivia, start);
        }

        return ToList(trivia);
    }

    private GreenNode? LexWhitespace()
    {
        var start = Position;
        if (!TryLexWhitespace())
            return null;

        List<GreenNode?>? trivia = null;
        Add(ref trivia, start);
        return ToList(trivia);
    }

    private bool TryLexWhitespace()
    {
        var start = Position;

        // A byte order mark is not part of the document it starts.
        if (Position == 0 && Current == '﻿')
        {
            Position++;
        }

        while (!IsAtEnd && IsWhitespace(Current))
        {
            Position++;
        }

        return Position > start;
    }

    private bool TryLexEndOfLine()
    {
        if (IsAtEnd || !IsEndOfLine(Current))
            return false;

        Position += Current == '\r' && LookAhead == '\n' ? 2 : 1;
        return true;
    }

    private void LexComment()
    {
        Position++;
        SkipToEndOfLine();
    }

    private void Add(ref List<GreenNode?>? trivia, int start)
    {
        var text = _text[start..Position];
        var kind = text[0] switch
        {
            ';' or '#' => SyntaxKind.CommentTrivia,
            '\r' or '\n' => SyntaxKind.EndOfLineTrivia,
            _ => SyntaxKind.WhitespaceTrivia,
        };

        (trivia ??= []).Add(SyntaxFactory.Trivia(kind, text));
    }

    private static GreenNode? ToList(List<GreenNode?>? trivia) => trivia is null ? null : SyntaxFactory.List(trivia.ToArray());

    /// <summary>Determines whether the <c>;</c> or <c>#</c> at <paramref name="position"/> starts a comment in the middle of a line.</summary>
    private bool IsInlineCommentStart(int position)
    {
        if (_text[position] is not (';' or '#'))
            return false;

        return options.InlineComments switch
        {
            IniInlineCommentMode.Anywhere => true,
            IniInlineCommentMode.AfterWhitespace => position > 0 && IsWhitespace(_text[position - 1]),
            _ => false,
        };
    }

    /// <summary>Determines whether only whitespace comes before <paramref name="position"/> on its line.</summary>
    private bool IsAtLineStart(int position)
    {
        var start = position - GetIndentation(position);

        return start == 0 || IsEndOfLine(_text[start - 1]) || (start == 1 && _text[0] == '﻿');
    }

    private bool IsAtEnd => Position >= _text.Length;
    private char Current => Position < _text.Length ? _text[Position] : '\0';
    private char LookAhead => Position + 1 < _text.Length ? _text[Position + 1] : '\0';

    private static bool IsEndOfLine(char value) => value is '\r' or '\n';

    /// <summary>Gets whether a character is whitespace other than a line break.</summary>
    internal static bool IsWhitespace(char value) => value is ' ' or '\t' || (!IsEndOfLine(value) && char.IsWhiteSpace(value));
}
