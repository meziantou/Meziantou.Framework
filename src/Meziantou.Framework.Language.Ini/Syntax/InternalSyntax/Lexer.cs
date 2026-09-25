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
    public GreenToken Lex(LexerMode mode)
    {
        var leading = mode is LexerMode.Value or LexerMode.RestOfLine ? LexWhitespace() : LexLeadingTrivia();
        if (IsAtEnd && mode != LexerMode.Value)
            return SyntaxFactory.Token(leading, SyntaxKind.EndOfFileToken, trailing: null);

        return mode switch
        {
            LexerMode.LineStart => LexLineStart(leading),
            LexerMode.SectionName => LexSectionName(leading),
            LexerMode.Value or LexerMode.ValueContinuation => LexValue(leading),
            _ => LexRestOfLine(leading),
        };
    }

    /// <summary>
    /// Determines whether the line starting at <paramref name="position"/> continues a value, once the blank lines and
    /// comment lines in front of it are passed over: it is indented more than the key of the value.
    /// </summary>
    /// <param name="position">The start of a line.</param>
    /// <param name="indentation">How indented the key of the value is.</param>
    public bool IsContinuation(int position, int indentation)
    {
        while (true)
        {
            var textStart = position;
            while (textStart < _text.Length && IsWhitespace(_text[textStart]))
            {
                textStart++;
            }

            if (textStart >= _text.Length)
                return false;

            if (_text[textStart] is not ('\r' or '\n' or ';' or '#'))
                return textStart - position > indentation;

            // A blank line or a comment line does not end the value: the line after it may still continue it.
            position = _text.IndexOfAny(['\r', '\n'], textStart);
            if (position < 0)
                return false;

            position += _text[position] == '\r' && position + 1 < _text.Length && _text[position + 1] == '\n' ? 2 : 1;
        }
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

    /// <summary>Reads one line of a value: the first one, after the separator, or one it continues on.</summary>
    private GreenToken LexValue(GreenNode? leading)
    {
        var start = Position;
        var end = ScanValueLine(out var isQuoted);
        Position = end;
        var text = _text[start..end];
        var trailing = LexTrailingTrivia(alwaysAllowComment: false);
        if (!isQuoted)
            return SyntaxFactory.Token(leading, SyntaxKind.ValueToken, text, trailing);

        var valueText = text[1..^1];
        return SyntaxFactory.TokenWithValue(leading, SyntaxKind.ValueToken, text, valueText, valueText, trailing);
    }

    /// <summary>Reads one line of a value, and returns where its text ends once the whitespace after it is left out.</summary>
    /// <param name="isQuoted">Whether the whole text is one quoted string, such as <c>"a;b"</c>, whose quotes are left out.</param>
    private int ScanValueLine(out bool isQuoted)
    {
        var start = Position;
        var quoteEnd = -1;
        if (options.AllowQuotedValues && Current is '"' or '\'')
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

        // A byte order mark is not part of the document it starts. It is trivia of its own, so that the whitespace after
        // it can be edited like any other.
        if (Position == 0 && Current == '﻿')
        {
            Position++;
            return true;
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
