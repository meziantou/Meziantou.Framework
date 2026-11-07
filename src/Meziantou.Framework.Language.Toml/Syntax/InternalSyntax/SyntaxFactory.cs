using System.Collections.Frozen;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Builds the immutable TOML nodes, reusing the ones whose text never varies.</summary>
internal static class SyntaxFactory
{
    private static readonly FrozenDictionary<SyntaxKind, GreenToken> FixedTokens = new[]
    {
        SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken, SyntaxKind.OpenBracketOpenBracketToken, SyntaxKind.CloseBracketCloseBracketToken,
        SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken, SyntaxKind.EqualsToken, SyntaxKind.CommaToken, SyntaxKind.DotToken,
        SyntaxKind.TrueKeyword, SyntaxKind.FalseKeyword,
    }.ToFrozenDictionary(kind => kind, kind => new GreenToken((int)kind, SyntaxFacts.GetText(kind), leadingTrivia: null, trailingTrivia: null, isMissing: false));

    private static readonly GreenToken EndOfFile = new((int)SyntaxKind.EndOfFileToken, "", leadingTrivia: null, trailingTrivia: null, isMissing: false);

    private static readonly FrozenDictionary<string, GreenTrivia> CommonWhitespace = new[]
    {
        " ", "  ", "    ", "      ", "        ", "\t", "\t\t",
    }.ToFrozenDictionary(text => text, text => new GreenTrivia((int)SyntaxKind.WhitespaceTrivia, text), StringComparer.Ordinal);

    private static readonly GreenTrivia LineFeedTrivia = new((int)SyntaxKind.EndOfLineTrivia, "\n");
    private static readonly GreenTrivia CarriageReturnLineFeedTrivia = new((int)SyntaxKind.EndOfLineTrivia, "\r\n");

    /// <summary>Returns the shared token for a kind whose text never varies.</summary>
    public static GreenToken Token(SyntaxKind kind)
    {
        if (FixedTokens.TryGetValue(kind, out var token))
            return token;

        if (kind == SyntaxKind.EndOfFileToken)
            return EndOfFile;

        return new GreenToken((int)kind, SyntaxFacts.GetText(kind), leadingTrivia: null, trailingTrivia: null, isMissing: false);
    }

    public static GreenToken Token(GreenNode? leading, SyntaxKind kind, GreenNode? trailing)
    {
        if (leading is null && trailing is null)
            return Token(kind);

        return new GreenToken((int)kind, SyntaxFacts.GetText(kind), leading, trailing, isMissing: false);
    }

    public static GreenToken Token(GreenNode? leading, SyntaxKind kind, string text, GreenNode? trailing)
        => new((int)kind, text, leading, trailing, isMissing: false);

    /// <summary>Creates a token whose text and meaning differ, such as a quoted string or a number.</summary>
    public static GreenToken TokenWithValue<TValue>(GreenNode? leading, SyntaxKind kind, string text, TValue value, string valueText, GreenNode? trailing)
        => new SyntaxTokenWithValue<TValue>((int)kind, text, value, valueText, leading, trailing, isMissing: false);

    public static GreenToken MissingToken(SyntaxKind kind) => new((int)kind, "", leadingTrivia: null, trailingTrivia: null, isMissing: true);

    public static GreenToken BadToken(GreenNode? leading, string text, GreenNode? trailing)
        => (GreenToken)new GreenToken((int)SyntaxKind.BadToken, text, leading, trailing, isMissing: false).AsSkippedText();

    public static GreenTrivia Trivia(SyntaxKind kind, string text)
    {
        if (kind == SyntaxKind.WhitespaceTrivia && CommonWhitespace.TryGetValue(text, out var whitespace))
            return whitespace;

        if (kind == SyntaxKind.EndOfLineTrivia)
        {
            if (string.Equals(text, "\n", StringComparison.Ordinal))
                return LineFeedTrivia;

            if (string.Equals(text, "\r\n", StringComparison.Ordinal))
                return CarriageReturnLineFeedTrivia;
        }

        return new GreenTrivia((int)kind, text);
    }

    public static GreenNode? List(ReadOnlySpan<GreenNode?> items) => Meziantou.Framework.Language.InternalSyntax.SyntaxList.List(items);

    /// <summary>Builds a list that can be projected into a red node even when it holds a single token.</summary>
    public static GreenNode? ListNode(ReadOnlySpan<GreenNode?> items) => Meziantou.Framework.Language.InternalSyntax.SyntaxList.ListNode(items);

    /// <summary>Appends one sequence of nodes, such as trivia, to another.</summary>
    public static GreenNode? Concat(GreenNode? left, GreenNode? right) => Meziantou.Framework.Language.InternalSyntax.SyntaxList.Concat(left, right);
}
