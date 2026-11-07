using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>Builds the immutable tokens and trivia a pattern is made of.</summary>
internal static class SyntaxFactory
{
    public static GreenToken Token(SyntaxKind kind, string text, GreenNode? leadingTrivia = null, string? valueText = null)
    {
        if (valueText is null || string.Equals(valueText, text, StringComparison.Ordinal))
            return new GreenToken((int)kind, text, leadingTrivia, trailingTrivia: null, isMissing: false);

        return new SyntaxTokenWithValue<string>((int)kind, text, valueText, valueText, leadingTrivia, trailingTrivia: null, isMissing: false);
    }

    /// <summary>Creates a zero-width token standing in for one the pattern does not have.</summary>
    public static GreenToken MissingToken(SyntaxKind kind, GreenNode? leadingTrivia = null)
        => new((int)kind, "", leadingTrivia, trailingTrivia: null, isMissing: true);

    public static GreenTrivia Trivia(SyntaxKind kind, string text) => new((int)kind, text);

    public static GreenNode? List(ReadOnlySpan<GreenNode?> items) => Meziantou.Framework.Language.InternalSyntax.SyntaxList.List(items);

    /// <summary>Builds a list that can be projected into a node even when it holds a single token.</summary>
    public static GreenNode? ListNode(ReadOnlySpan<GreenNode?> items) => Meziantou.Framework.Language.InternalSyntax.SyntaxList.ListNode(items);
}
