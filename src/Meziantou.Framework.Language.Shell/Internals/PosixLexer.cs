using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenFactory = Meziantou.Framework.Language.Shell.Syntax.InternalSyntax.SyntaxFactory;
using Red = Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>
/// Character-level scanner for the POSIX shell family. The parser drives it and chooses the lexical mode, because
/// the same characters mean different things in command position, in argument position, and inside quotes.
/// </summary>
internal sealed class PosixLexer
{
    private readonly List<Diagnostic> _diagnostics;

    public PosixLexer(SourceText source, ShellDialect dialect, List<Diagnostic> diagnostics)
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

    /// <summary>Reads blanks, line continuations, and a trailing comment. Stops at a line break.</summary>
    public GreenNode? ReadInlineTrivia() => ReadTrivia(includeLineBreaks: false);

    /// <summary>Reads blanks, line continuations, comments, and line breaks.</summary>
    public GreenNode? ReadStatementTrivia() => ReadTrivia(includeLineBreaks: true);

    private GreenNode? ReadTrivia(bool includeLineBreaks)
    {
        List<GreenNode?>? trivia = null;
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

                Add(ref trivia, SyntaxKind.WhitespaceTrivia, start);
                continue;
            }

            // A backslash immediately before a line break joins two physical lines; it is never part of a word.
            if (current == '\\' && SourceText.GetLineBreakLength(Text, Math.Min(Position + 1, Text.Length - 1)) > 0 && Position + 1 < Text.Length)
            {
                Position += 1 + SourceText.GetLineBreakLength(Text, Position + 1);
                Add(ref trivia, SyntaxKind.LineContinuationTrivia, start);
                continue;
            }

            if (current == '#' && IsCommentStart(start))
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

    /// <summary>A <c>#</c> only starts a comment at the beginning of a word, not in the middle of one.</summary>
    private bool IsCommentStart(int position)
    {
        if (position == 0)
            return true;

        var previous = Text[position - 1];

        return previous is ' ' or '\t' or '\n' or '\r' or ';' or '&' or '|' or '(' or ')';
    }

    public ScannedToken CreateToken(SyntaxKind kind, int tokenStart, GreenNode? leadingTrivia, int fullStart, string? valueText = null)
    {
        // Clamp defensively: a scan that runs off the end must still produce a valid token rather than throw.
        Position = Math.Clamp(Position, 0, Text.Length);
        tokenStart = Math.Clamp(tokenStart, 0, Position);
        var text = Text[tokenStart..Position];

        _ = fullStart;

        return new ScannedToken(GreenFactory.TokenWithValue(leadingTrivia, kind, text, valueText ?? text, trailing: null), tokenStart, Position);
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

    /// <summary>Returns <see langword="true"/> for characters that cannot appear unquoted inside a word.</summary>
    public static bool IsWordBoundary(char value) =>
        value is '\0' or ' ' or '\t' or '\r' or '\n' or ';' or '&' or '|' or '<' or '>' or '(' or ')';

    public static bool IsNameStart(char value) => char.IsAsciiLetter(value) || value == '_';
    public static bool IsNameCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';

    /// <summary>The single-character special parameters: <c>$?</c>, <c>$@</c>, <c>$1</c>, and friends.</summary>
    public static bool IsSpecialParameter(char value) => value is '?' or '@' or '*' or '#' or '$' or '!' or '-' or '0' or (>= '1' and <= '9');
}
