using System.Collections.Frozen;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Builds the immutable XML tokens and trivia, reusing the ones whose text never varies.</summary>
/// <remarks>
/// A green node is position-independent, so a token like <c>&gt;</c> with no trivia is the same node wherever it
/// appears and can be shared by every tree in the process. Punctuation is most of what a document is made of, so
/// interning it is what keeps parsing from allocating a node per angle bracket.
/// </remarks>
internal static class SyntaxFactory
{
    private static readonly FrozenDictionary<SyntaxKind, GreenToken> FixedTokens = new[]
    {
        SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken, SyntaxKind.LessThanSlashToken, SyntaxKind.SlashGreaterThanToken,
        SyntaxKind.EqualsToken, SyntaxKind.SingleQuoteToken, SyntaxKind.DoubleQuoteToken, SyntaxKind.LessThanQuestionToken,
        SyntaxKind.QuestionGreaterThanToken, SyntaxKind.XmlCommentStartToken, SyntaxKind.XmlCommentEndToken,
        SyntaxKind.CDataStartToken, SyntaxKind.CDataEndToken,
    }.ToFrozenDictionary(kind => kind, kind => new GreenToken((int)kind, SyntaxFacts.GetText(kind), leadingTrivia: null, trailingTrivia: null, isMissing: false));

    private static readonly GreenToken EndOfFile = new((int)SyntaxKind.EndOfFileToken, "", leadingTrivia: null, trailingTrivia: null, isMissing: false);

    private static readonly FrozenDictionary<string, GreenTrivia> CommonTrivia = new[]
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

    public static GreenToken Token(GreenNode? leading, SyntaxKind kind)
        => leading is null ? Token(kind) : new GreenToken((int)kind, SyntaxFacts.GetText(kind), leading, trailingTrivia: null, isMissing: false);

    /// <summary>Creates a token whose text is not fixed by its kind, such as a name or a run of content.</summary>
    public static GreenToken Token(GreenNode? leading, SyntaxKind kind, string text)
        => new((int)kind, text, leading, trailingTrivia: null, isMissing: false);

    /// <summary>Creates a token whose text and meaning differ, such as an attribute value that escapes a character.</summary>
    public static GreenToken TokenWithValue(GreenNode? leading, SyntaxKind kind, string text, string valueText)
        => string.Equals(text, valueText, StringComparison.Ordinal)
            ? Token(leading, kind, text)
            : new SyntaxTokenWithValue<string>((int)kind, text, valueText, valueText, leading, trailingTrivia: null, isMissing: false);

    /// <summary>Creates a zero-width token standing in for one the text does not have.</summary>
    public static GreenToken MissingToken(SyntaxKind kind) => new((int)kind, "", leadingTrivia: null, trailingTrivia: null, isMissing: true);

    public static GreenToken BadToken(GreenNode? leading, string text)
        => (GreenToken)new GreenToken((int)SyntaxKind.BadToken, text, leading, trailingTrivia: null, isMissing: false).AsSkippedText();

    public static GreenTrivia Trivia(SyntaxKind kind, string text)
    {
        if (kind == SyntaxKind.WhitespaceTrivia && CommonTrivia.TryGetValue(text, out var whitespace))
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
}
