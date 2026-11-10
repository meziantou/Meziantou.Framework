using System.Text;
using Meziantou.Framework.Language.Ini.Internals;
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
/// <para>
/// In the git dialect, a value runs on the lines below it while a line ends with <c>\</c>, so its token holds those line
/// breaks.
/// </para>
/// </remarks>
internal sealed class Lexer(SourceText source, IniParseOptions options)
{
    private readonly string _text = source.Text;
    private readonly List<LexerDiagnostic> _diagnostics = [];

    /// <summary>Gets or sets where the next token will be read from.</summary>
    public int Position { get; set; }

    /// <summary>Gets the errors found in the last token read, at their position in the text.</summary>
    public IReadOnlyList<LexerDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Reads the next token.</summary>
    /// <param name="mode">What the parser expects next.</param>
    public GreenToken Lex(LexerMode mode)
    {
        _diagnostics.Clear();
        var leading = mode is LexerMode.Value or LexerMode.RestOfLine ? LexWhitespace() : LexLeadingTrivia();
        if (IsAtEnd && mode != LexerMode.Value)
            return SyntaxFactory.Token(leading, SyntaxKind.EndOfFileToken, trailing: null);

        return mode switch
        {
            LexerMode.LineStart => LexLineStart(leading),
            LexerMode.SectionName => options.IsGit ? LexGitSectionName(leading) : LexSectionName(leading),
            LexerMode.Value or LexerMode.ValueContinuation => options.IsGit ? LexGitValue(leading) : LexValue(leading),
            _ => LexRestOfLine(leading),
        };
    }

    /// <summary>
    /// Determines whether the line starting at <paramref name="position"/> continues a value, once the blank lines and
    /// comment lines in front of it are passed over: it is indented more than the key of the value.
    /// </summary>
    /// <param name="position">The start of a line.</param>
    /// <param name="indentation">How indented the key of the value is.</param>
    /// <param name="textStart">Where the text of that line starts.</param>
    public bool IsContinuation(int position, int indentation, out int textStart)
    {
        while (true)
        {
            textStart = position;
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
            return LexCloseBracket(leading);

        var start = Position;
        while (!IsAtEnd && Current != ']' && !IsEndOfLine(Current) && !IsInlineCommentStart(Position))
        {
            Position++;
        }

        return Text(leading, SyntaxKind.KeyToken, start);
    }

    private GreenToken LexCloseBracket(GreenNode? leading)
    {
        Position++;

        // Nothing but a comment can follow a section header, so one is read as a comment whatever the mode.
        return SyntaxFactory.Token(leading, SyntaxKind.CloseBracketToken, LexTrailingTrivia(alwaysAllowComment: true));
    }

    /// <summary>Reads a git section name, such as <c>core</c> or <c>remote "origin"</c>, whose quotes may hold <c>]</c>.</summary>
    /// <remarks>
    /// The name of <c>remote "origin"</c> is <c>remote.origin</c>, as git names it: the value text of the token. In the
    /// subsection, a backslash escapes the character after it. A name that is not a section name followed by a quoted
    /// subsection is its text.
    /// </remarks>
    private GreenToken LexGitSectionName(GreenNode? leading)
    {
        if (Current == ']')
            return LexCloseBracket(leading);

        var start = Position;
        var quoteStart = -1;
        while (!IsAtEnd && !IsEndOfLine(Current))
        {
            if (quoteStart >= 0)
            {
                if (Current == '\\' && Position + 1 < _text.Length && !IsEndOfLine(_text[Position + 1]))
                {
                    Position += 2;
                    continue;
                }

                if (Current == '"')
                {
                    quoteStart = -1;
                }

                Position++;
                continue;
            }

            if (Current == ']' || IsInlineCommentStart(Position))
                break;

            if (Current == '"')
            {
                quoteStart = Position;
            }

            Position++;
        }

        if (quoteStart >= 0)
        {
            AddDiagnostic(quoteStart, 1, IniDiagnosticDescriptors.UnclosedQuote);
        }

        var end = TrimEnd(start, Position);
        Position = end;
        var text = _text[start..end];
        var trailing = LexTrailingTrivia(alwaysAllowComment: false);
        var name = GetGitSectionName(text);
        if (name is null || string.Equals(name, text, StringComparison.Ordinal))
            return SyntaxFactory.Token(leading, SyntaxKind.KeyToken, text, trailing);

        return SyntaxFactory.TokenWithValue(leading, SyntaxKind.KeyToken, text, name, name, trailing);
    }

    /// <summary>Gets the name git gives the header text <c>section "subsection"</c>, <c>section.subsection</c>, or <see langword="null"/> when the text is not one.</summary>
    internal static string? GetGitSectionName(string text)
    {
        var index = 0;
        while (index < text.Length && !IsWhitespace(text[index]) && text[index] != '"')
        {
            index++;
        }

        var sectionEnd = index;
        while (index < text.Length && IsWhitespace(text[index]))
        {
            index++;
        }

        if (sectionEnd == 0 || index == sectionEnd || index >= text.Length || text[index] != '"')
            return null;

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, sectionEnd).Append('.');
        for (index++; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '"')
                return index == text.Length - 1 ? builder.ToString() : null;

            if (current == '\\' && index + 1 < text.Length)
            {
                index++;
                current = text[index];
            }

            builder.Append(current);
        }

        return null;
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
        {
            var unescaped = options.EffectiveAllowEscapes ? Unescape(text) : text;
            if (string.Equals(unescaped, text, StringComparison.Ordinal))
                return SyntaxFactory.Token(leading, SyntaxKind.ValueToken, text, trailing);

            return SyntaxFactory.TokenWithValue(leading, SyntaxKind.ValueToken, text, unescaped, unescaped, trailing);
        }

        var valueText = text[1..^1];
        if (options.EffectiveAllowEscapes)
        {
            valueText = Unescape(valueText);
        }

        return SyntaxFactory.TokenWithValue(leading, SyntaxKind.ValueToken, text, valueText, valueText, trailing);
    }

    /// <summary>Reads the escapes of a value of the generic dialect: a backslash in front of a character it does not escape is kept.</summary>
    private static string Unescape(string text)
    {
        var index = text.IndexOf('\\', StringComparison.Ordinal);
        if (index < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, index);
        for (; index < text.Length; index++)
        {
            var current = text[index];
            if (current != '\\' || index + 1 >= text.Length)
            {
                builder.Append(current);
                continue;
            }

            var next = text[index + 1];
            char? unescaped = next switch
            {
                '\\' or '\'' or '"' or ';' or '#' or '=' or ':' => next,
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                't' => '\t',
                'r' => '\r',
                'n' => '\n',
                _ => null,
            };

            if (unescaped is { } value)
            {
                builder.Append(value);
                index++;
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    /// <summary>Reads a value of the git dialect, which runs on the next line when its line ends with a backslash.</summary>
    /// <remarks>
    /// This follows git: double quotes may open and close anywhere, and are left out; outside them, <c>;</c> and <c>#</c>
    /// start a comment, and the whitespace at the end of the value is left out. <c>\"</c>, <c>\\</c>, <c>\n</c>, <c>\t</c>,
    /// and <c>\b</c> are the only escapes. The whitespace after the value, its comment, and the end of its last line are
    /// trivia.
    /// </remarks>
    private GreenToken LexGitValue(GreenNode? leading)
    {
        var start = Position;
        var builder = new StringBuilder();
        var trimLength = 0;
        var quoteStart = -1;
        var contentEnd = start;
        while (!IsAtEnd && !IsEndOfLine(Current))
        {
            var current = Current;
            if (quoteStart < 0 && IsWhitespace(current))
            {
                // Whitespace is part of the value only when something other than whitespace follows it.
                if (trimLength == 0)
                {
                    trimLength = builder.Length;
                }

                if (builder.Length > 0)
                {
                    builder.Append(current);
                }

                Position++;
                continue;
            }

            if (quoteStart < 0 && current is ';' or '#')
                break;

            trimLength = 0;
            if (current == '\\')
            {
                var escapeStart = Position;
                Position++;
                if (!IsAtEnd)
                {
                    var next = Current;
                    if (IsEndOfLine(next))
                    {
                        // The value continues on the next line.
                        TryLexEndOfLine();
                    }
                    else
                    {
                        Position++;
                        switch (next)
                        {
                            case 'n':
                                builder.Append('\n');
                                break;
                            case 't':
                                builder.Append('\t');
                                break;
                            case 'b':
                                builder.Append('\b');
                                break;
                            case '\\' or '"':
                                builder.Append(next);
                                break;
                            default:
                                AddDiagnostic(escapeStart, 2, IniDiagnosticDescriptors.InvalidEscapeSequence, next);
                                builder.Append(current).Append(next);
                                break;
                        }
                    }
                }

                contentEnd = Position;
                continue;
            }

            if (current == '"')
            {
                quoteStart = quoteStart < 0 ? Position : -1;
            }
            else
            {
                builder.Append(current);
            }

            Position++;
            contentEnd = Position;
        }

        if (quoteStart >= 0)
        {
            AddDiagnostic(quoteStart, 1, IniDiagnosticDescriptors.UnclosedQuote);
        }

        if (trimLength > 0)
        {
            builder.Length = trimLength;
        }

        Position = contentEnd;
        var text = _text[start..contentEnd];
        var valueText = builder.ToString();
        var trailing = LexTrailingTrivia(alwaysAllowComment: false);
        if (string.Equals(text, valueText, StringComparison.Ordinal))
            return SyntaxFactory.Token(leading, SyntaxKind.ValueToken, text, trailing);

        return SyntaxFactory.TokenWithValue(leading, SyntaxKind.ValueToken, text, valueText, valueText, trailing);
    }

    /// <summary>
    /// Determines whether the text of a git value ends with a backslash that continues it on the next line, which the end of
    /// the text ends instead: a line break written after it would continue the value on the line that follows.
    /// </summary>
    internal static bool EndsWithLineContinuation(string text)
    {
        var count = 0;
        for (var i = text.Length - 1; i >= 0 && text[i] == '\\'; i--)
        {
            count++;
        }

        return count % 2 == 1;
    }

    private void AddDiagnostic(int position, int width, DiagnosticDescriptor descriptor, params object?[] arguments)
        => _diagnostics.Add(new LexerDiagnostic(position, width, descriptor, arguments));

    /// <summary>Reads one line of a value, and returns where its text ends once the whitespace after it is left out.</summary>
    /// <param name="isQuoted">Whether the whole text is one quoted string, such as <c>"a;b"</c>, whose quotes are left out.</param>
    /// <remarks>
    /// Only a value wholly in quotes is read as quoted: a comment cannot start inside its quotes. When anything but a
    /// comment follows the closing quote on the line, or the quote is not closed, the quotes are ordinary characters and
    /// the line is read as if there were none, so whether <c>;</c> or <c>#</c> starts a comment never depends on them.
    /// </remarks>
    private int ScanValueLine(out bool isQuoted)
    {
        var start = Position;
        var escapes = options.EffectiveAllowEscapes;
        if (options.EffectiveAllowQuotedValues && Current is '"' or '\'')
        {
            var close = FindClosingQuote(Position + 1, Current, escapes);
            if (close >= 0)
            {
                var after = close + 1;
                while (after < _text.Length && IsWhitespace(_text[after]))
                {
                    after++;
                }

                if (after >= _text.Length || IsEndOfLine(_text[after]) || IsInlineCommentStart(after))
                {
                    isQuoted = true;
                    return close + 1;
                }
            }
        }

        // The whitespace that ends the line is left out, but not a character a backslash escapes.
        var end = start;
        while (!IsAtEnd && !IsEndOfLine(Current) && !IsInlineCommentStart(Position))
        {
            var isEscape = escapes && Current == '\\' && Position + 1 < _text.Length && !IsEndOfLine(_text[Position + 1]);
            Position += isEscape ? 2 : 1;
            if (isEscape || !IsWhitespace(_text[Position - 1]))
            {
                end = Position;
            }
        }

        isQuoted = false;
        return end;
    }

    /// <summary>Finds the quote that closes the one before <paramref name="position"/> on its line, or returns -1.</summary>
    private int FindClosingQuote(int position, char quote, bool escapes)
    {
        for (var i = position; i < _text.Length; i++)
        {
            var current = _text[i];
            if (current == quote)
                return i;

            if (IsEndOfLine(current))
                return -1;

            if (escapes && current == '\\' && i + 1 < _text.Length && !IsEndOfLine(_text[i + 1]))
            {
                i++;
            }
        }

        return -1;
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
        var end = TrimEnd(start, Position);
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
        if (Position == 0 && Current == '\uFEFF')
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

    /// <summary>Gets where the text from <paramref name="start"/> to <paramref name="end"/> ends once the whitespace after it is left out.</summary>
    private int TrimEnd(int start, int end)
    {
        while (end > start && IsWhitespace(_text[end - 1]))
        {
            end--;
        }

        return end;
    }

    private static GreenNode? ToList(List<GreenNode?>? trivia) => trivia is null ? null : SyntaxFactory.List(trivia.ToArray());

    /// <summary>Determines whether the <c>;</c> or <c>#</c> at <paramref name="position"/> starts a comment in the middle of a line.</summary>
    private bool IsInlineCommentStart(int position)
    {
        if (_text[position] is not (';' or '#'))
            return false;

        return options.EffectiveInlineComments switch
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

        return start == 0 || IsEndOfLine(_text[start - 1]) || (start == 1 && _text[0] == '\uFEFF');
    }

    private bool IsAtEnd => Position >= _text.Length;
    private char Current => Position < _text.Length ? _text[Position] : '\0';
    private char LookAhead => Position + 1 < _text.Length ? _text[Position + 1] : '\0';

    private static bool IsEndOfLine(char value) => value is '\r' or '\n';

    /// <summary>Gets whether a character is whitespace other than a line break.</summary>
    internal static bool IsWhitespace(char value) => value is ' ' or '\t' || (!IsEndOfLine(value) && char.IsWhiteSpace(value));
}
