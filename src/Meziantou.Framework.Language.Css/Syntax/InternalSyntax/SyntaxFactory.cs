using System.Collections.Frozen;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>Builds the immutable CSS tokens and trivia, reusing the ones whose text never varies.</summary>
/// <remarks>
/// A green node is position-independent, so a token like <c>{</c> with no trivia is the same node wherever it appears
/// and can be shared by every tree in the process. Interning the punctuation and the handful of whitespace runs that
/// make up almost all indentation is what keeps parsing from allocating a node per character of layout.
/// </remarks>
internal static class SyntaxFactory
{
    private static readonly FrozenDictionary<SyntaxKind, GreenToken> FixedTokens = BuildFixedTokens();

    private static readonly FrozenDictionary<string, GreenTrivia> CommonWhitespace = new[]
    {
        " ", "  ", "    ", "      ", "        ", "\t", "\t\t",
    }.ToFrozenDictionary(text => text, text => new GreenTrivia((int)SyntaxKind.WhitespaceTrivia, text), StringComparer.Ordinal);

    private static readonly GreenTrivia LineFeedTrivia = new((int)SyntaxKind.EndOfLineTrivia, "\n");
    private static readonly GreenTrivia CarriageReturnLineFeedTrivia = new((int)SyntaxKind.EndOfLineTrivia, "\r\n");

    /// <summary>Returns the shared token for a kind whose text never varies.</summary>
    public static GreenToken Token(SyntaxKind kind)
        => FixedTokens.TryGetValue(kind, out var token) ? token : new GreenToken((int)kind, SyntaxFacts.GetText(kind), leadingTrivia: null, trailingTrivia: null, isMissing: false);

    public static GreenToken Token(GreenNode? leading, SyntaxKind kind, GreenNode? trailing)
        => leading is null && trailing is null ? Token(kind) : new GreenToken((int)kind, SyntaxFacts.GetText(kind), leading, trailing, isMissing: false);

    /// <summary>Creates a token whose text is not fixed by its kind, such as an identifier.</summary>
    public static GreenToken Token(GreenNode? leading, SyntaxKind kind, string text, GreenNode? trailing)
        => new((int)kind, text, leading, trailing, isMissing: false);

    /// <summary>Creates a token whose text and meaning differ, such as a quoted string or a number.</summary>
    public static GreenToken TokenWithValue<TValue>(GreenNode? leading, SyntaxKind kind, string text, TValue value, string valueText, GreenNode? trailing)
        => new SyntaxTokenWithValue<TValue>((int)kind, text, value, valueText, leading, trailing, isMissing: false);

    /// <summary>Creates a zero-width token standing in for one the source does not have.</summary>
    public static GreenToken MissingToken(SyntaxKind kind) => new((int)kind, "", leadingTrivia: null, trailingTrivia: null, isMissing: true);

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

    /// <summary>Builds the list a node's list slot holds, or nothing for an empty list.</summary>
    public static GreenNode? List(List<GreenNode?> items) => items.Count == 0 ? null : List(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items));

    /// <summary>Builds the list a token-list slot holds, or nothing for an empty list.</summary>
    public static GreenNode? TokenList(List<GreenNode?> items) => items.Count == 0 ? null : ListNode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items));

    /// <summary>Returns a copy of <paramref name="token"/> marked as text a browser drops, leaving the original, which may be shared, untouched.</summary>
    public static GreenToken AsSkippedText(GreenToken token) => token.ContainsSkippedText ? token : token.WithTrivia(token.LeadingTrivia, token.TrailingTrivia).AsSkippedText();

    private static FrozenDictionary<SyntaxKind, GreenToken> BuildFixedTokens()
    {
        var tokens = new Dictionary<SyntaxKind, GreenToken>();
        foreach (var kind in Enum.GetValues<SyntaxKind>())
        {
            var text = SyntaxFacts.GetText(kind);
            if (text.Length > 0)
            {
                tokens[kind] = new GreenToken((int)kind, text, leadingTrivia: null, trailingTrivia: null, isMissing: false);
            }
        }

        tokens[SyntaxKind.EndOfFileToken] = new GreenToken((int)SyntaxKind.EndOfFileToken, "", leadingTrivia: null, trailingTrivia: null, isMissing: false);

        return tokens.ToFrozenDictionary();
    }
}
