using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>
/// Character-level scanner for the PowerShell family. As with the POSIX lexer, the parser chooses the mode: the same
/// characters lex differently in expression position and in command-argument position.
/// </summary>
internal sealed class PowerShellLexer
{
    private readonly List<Diagnostic> _diagnostics;

    public PowerShellLexer(SourceText source, ShellDialect dialect, List<Diagnostic> diagnostics)
    {
        Source = source;
        Text = source.Text;
        Dialect = dialect;
        _diagnostics = diagnostics;
    }

    public SourceText Source { get; }
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

    public GreenNode? ReadInlineTrivia() => ReadTrivia(includeLineBreaks: false);

    public GreenNode? ReadStatementTrivia() => ReadTrivia(includeLineBreaks: true);

    private GreenNode? ReadTrivia(bool includeLineBreaks)
    {
        List<GreenNode?>? trivia = null;
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

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // A backtick immediately before a line break joins two physical lines.
            if (current == '`' && Position + 1 < Text.Length && SourceText.GetLineBreakLength(Text, Position + 1) > 0)
            {
                Position += 1 + SourceText.GetLineBreakLength(Text, Position + 1);
                Add(ref trivia, SyntaxKind.LineContinuationTrivia, start);
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

                Add(ref trivia, SyntaxKind.MultiLineCommentTrivia, start);
                continue;
            }

            if (current == '#')
            {
                while (!IsAtEnd && SourceText.GetLineBreakLength(Text, Position) == 0)
                {
                    Position++;
                }

                Add(ref trivia, SyntaxKind.SingleLineCommentTrivia, start);
                continue;
            }

            if (includeLineBreaks)
            {
                var lineBreakLength = SourceText.GetLineBreakLength(Text, Position);
                if (lineBreakLength > 0)
                {
                    Position += lineBreakLength;
                    Add(ref trivia, SyntaxKind.EndOfLineTrivia, start);
                    continue;
                }
            }

            break;
        }

        return trivia is null ? null : GreenFactory.List(CollectionsMarshal.AsSpan(trivia));
    }

    public ScannedToken CreateToken(SyntaxKind kind, int tokenStart, GreenNode? leadingTrivia, int fullStart, string? valueText = null)
    {
        Position = Math.Clamp(Position, 0, Text.Length);
        tokenStart = Math.Clamp(tokenStart, 0, Position);
        var text = Text[tokenStart..Position];

        return new ScannedToken(kind, text, valueText ?? text, leadingTrivia: leadingTrivia, fullStart: fullStart);
    }

    public void AddDiagnostic(int start, int length, string id, string message)
    {
        _diagnostics.Add(new Diagnostic(id, message, DiagnosticSeverity.Error, new Location(new TextSpan(start, Math.Max(0, length)), Source)));
    }

    private void Add(ref List<GreenNode?>? trivia, SyntaxKind kind, int start)
    {
        trivia ??= [];
        trivia.Add(GreenFactory.Trivia(kind, Text[start..Position]));
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
