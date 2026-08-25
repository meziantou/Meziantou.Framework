namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>
/// Character-level scanner for the PowerShell family. As with the POSIX lexer, the parser chooses the mode: the same
/// characters lex differently in expression position and in command-argument position.
/// </summary>
internal sealed class PowerShellLexer
{
    private readonly List<ShellDiagnostic> _diagnostics;

    public PowerShellLexer(string text, ShellDialect dialect, List<ShellDiagnostic> diagnostics)
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

    public IReadOnlyList<ShellSyntaxTrivia> ReadInlineTrivia() => ReadTrivia(includeLineBreaks: false);

    public IReadOnlyList<ShellSyntaxTrivia> ReadStatementTrivia() => ReadTrivia(includeLineBreaks: true);

    private List<ShellSyntaxTrivia> ReadTrivia(bool includeLineBreaks)
    {
        List<ShellSyntaxTrivia>? trivia = null;
        while (!IsAtEnd)
        {
            var start = Position;
            var current = Current;

            if (current is ' ' or '\t' or '\f' or '\v')
            {
                while (!IsAtEnd && Current is ' ' or '\t' or '\f' or '\v')
                {
                    Position++;
                }

                Add(ref trivia, ShellSyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // A backtick immediately before a line break joins two physical lines.
            if (current == '`' && Position + 1 < Text.Length && SourceText.GetLineBreakLength(Text, Position + 1) > 0)
            {
                Position += 1 + SourceText.GetLineBreakLength(Text, Position + 1);
                Add(ref trivia, ShellSyntaxKind.LineContinuationTrivia, start);
                continue;
            }

            if (current == '<' && Peek(1) == '#')
            {
                Position += 2;
                while (!IsAtEnd && !(Current == '#' && Peek(1) == '>'))
                {
                    Position++;
                }

                if (IsAtEnd)
                {
                    AddDiagnostic(start, Text.Length - start, "SHELL0020", "Unterminated block comment.");
                }
                else
                {
                    Position += 2;
                }

                Add(ref trivia, ShellSyntaxKind.MultiLineCommentTrivia, start);
                continue;
            }

            if (current == '#')
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

    public ShellSyntaxToken CreateToken(ShellSyntaxKind kind, int tokenStart, IReadOnlyList<ShellSyntaxTrivia> leadingTrivia, int fullStart, string? valueText = null)
    {
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

    /// <summary>
    /// Characters that end a bare command argument. Note that <c>&gt;</c> and <c>&lt;</c> are not among them: they
    /// only start a redirection when whitespace precedes them, so <c>in&gt;</c> is a single word.
    /// </summary>
    public static bool IsArgumentBoundary(char value) =>
        value is '\0' or ' ' or '\t' or '\f' or '\v' or '\r' or '\n' or '|' or ';' or '&' or ',' or ')' or '}';

    public static bool IsNameStart(char value) => char.IsLetter(value) || value == '_';
    public static bool IsNameCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    /// <summary>Variable names may carry a scope or provider prefix, as in <c>$env:PATH</c> or <c>$script:x</c>.</summary>
    public static bool IsVariableNameCharacter(char value) => IsNameCharacter(value) || value == ':';
}
