namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>
/// Character-level scanner for the POSIX shell family. The parser drives it and chooses the lexical mode, because
/// the same characters mean different things in command position, in argument position, and inside quotes.
/// </summary>
internal sealed class PosixLexer
{
    private readonly List<ShellDiagnostic> _diagnostics;

    public PosixLexer(string text, ShellDialect dialect, List<ShellDiagnostic> diagnostics)
    {
        Text = text;
        Dialect = dialect;
        _diagnostics = diagnostics;
    }

    public string Text { get; }
    public ShellDialect Dialect { get; }
    public int Position { get; set; }
    public bool IsAtEnd => Position >= Text.Length;
    public char Current => Position < Text.Length ? Text[Position] : '\0';

    public char Peek(int offset)
    {
        var index = Position + offset;

        return index >= 0 && index < Text.Length ? Text[index] : '\0';
    }

    /// <summary>Reads blanks, line continuations, and a trailing comment. Stops at a line break.</summary>
    public IReadOnlyList<ShellSyntaxTrivia> ReadInlineTrivia() => ReadTrivia(includeLineBreaks: false);

    /// <summary>Reads blanks, line continuations, comments, and line breaks.</summary>
    public IReadOnlyList<ShellSyntaxTrivia> ReadStatementTrivia() => ReadTrivia(includeLineBreaks: true);

    private List<ShellSyntaxTrivia> ReadTrivia(bool includeLineBreaks)
    {
        List<ShellSyntaxTrivia>? trivia = null;
        while (!IsAtEnd)
        {
            var start = Position;
            var current = Current;

            if (current is ' ' or '\t')
            {
                while (!IsAtEnd && Current is ' ' or '\t')
                {
                    Position++;
                }

                Add(ref trivia, ShellSyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // A backslash immediately before a line break joins two physical lines; it is never part of a word.
            if (current == '\\' && SourceText.GetLineBreakLength(Text, Math.Min(Position + 1, Text.Length - 1)) > 0 && Position + 1 < Text.Length)
            {
                Position += 1 + SourceText.GetLineBreakLength(Text, Position + 1);
                Add(ref trivia, ShellSyntaxKind.LineContinuationTrivia, start);
                continue;
            }

            if (current == '#' && IsCommentStart(start))
            {
                while (!IsAtEnd && SourceText.GetLineBreakLength(Text, Position) == 0)
                {
                    Position++;
                }

                Add(ref trivia, ShellSyntaxKind.SingleLineCommentTrivia, start);
                continue;
            }

            if (includeLineBreaks)
            {
                var lineBreakLength = SourceText.GetLineBreakLength(Text, Position);
                if (lineBreakLength > 0)
                {
                    Position += lineBreakLength;
                    Add(ref trivia, ShellSyntaxKind.EndOfLineTrivia, start);
                    continue;
                }
            }

            break;
        }

        return trivia ?? [];
    }

    /// <summary>A <c>#</c> only starts a comment at the beginning of a word, not in the middle of one.</summary>
    private bool IsCommentStart(int position)
    {
        if (position == 0)
            return true;

        var previous = Text[position - 1];

        return previous is ' ' or '\t' or '\n' or '\r' or ';' or '&' or '|' or '(' or ')';
    }

    public ShellSyntaxToken CreateToken(ShellSyntaxKind kind, int tokenStart, IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart, string? valueText = null)
    {
        // Clamp defensively: a scan that runs off the end must still produce a valid token rather than throw.
        Position = Math.Clamp(Position, 0, Text.Length);
        tokenStart = Math.Clamp(tokenStart, 0, Position);
        var text = Text[tokenStart..Position];

        return new ShellSyntaxToken(kind, text, valueText ?? text, leadingTrivia: leadingTrivia, fullStart: fullStart);
    }

    public void AddDiagnostic(int start, int length, string id, string message)
    {
        _diagnostics.Add(new ShellDiagnostic(id, message, ShellDiagnosticSeverity.Error, new TextSpan(start, Math.Max(0, length))));
    }

    private void Add(ref List<ShellSyntaxTrivia>? trivia, ShellSyntaxKind kind, int start)
    {
        trivia ??= [];
        trivia.Add(new ShellSyntaxTrivia(kind, Text[start..Position], start));
    }

    /// <summary>Returns <see langword="true"/> for characters that cannot appear unquoted inside a word.</summary>
    public static bool IsWordBoundary(char value) =>
        value is '\0' or ' ' or '\t' or '\r' or '\n' or ';' or '&' or '|' or '<' or '>' or '(' or ')';

    public static bool IsNameStart(char value) => char.IsAsciiLetter(value) || value == '_';
    public static bool IsNameCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';

    /// <summary>The single-character special parameters: <c>$?</c>, <c>$@</c>, <c>$1</c>, and friends.</summary>
    public static bool IsSpecialParameter(char value) => value is '?' or '@' or '*' or '#' or '$' or '!' or '-' or '0' or (>= '1' and <= '9');
}
