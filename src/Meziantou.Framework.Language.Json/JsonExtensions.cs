namespace Meziantou.Framework.Language.Json;

/// <summary>Reads the JSON kind of the tokens, trivia, and nodes the shared syntax types hand back.</summary>
/// <remarks>
/// The shared types carry a kind as a plain number because they are used by every language. These turn that number
/// back into the JSON kind it stands for.
/// </remarks>
public static class JsonExtensions
{
    /// <summary>Gets the JSON kind of a node reached through the shared type rather than the JSON one.</summary>
    public static SyntaxKind Kind(this SyntaxNode? node) => (SyntaxKind)(node?.RawKind ?? 0);

    public static SyntaxKind Kind(this SyntaxToken token) => (SyntaxKind)token.RawKind;
    public static SyntaxKind Kind(this SyntaxTrivia trivia) => (SyntaxKind)trivia.RawKind;
    public static SyntaxKind Kind(this SyntaxNodeOrToken nodeOrToken) => (SyntaxKind)nodeOrToken.RawKind;

    public static bool IsKind(this SyntaxNode? node, SyntaxKind kind) => node?.RawKind == (int)kind;
    public static bool IsKind(this SyntaxToken token, SyntaxKind kind) => token.RawKind == (int)kind;
    public static bool IsKind(this SyntaxTrivia trivia, SyntaxKind kind) => trivia.RawKind == (int)kind;
    public static bool IsKind(this SyntaxNodeOrToken nodeOrToken, SyntaxKind kind) => nodeOrToken.RawKind == (int)kind;

    /// <summary>Carries the annotations of <paramref name="original"/> onto a node built to replace it.</summary>
    /// <remarks>
    /// This is what makes an annotation survive an edit: every <c>Update</c> builds a fresh node and then copies the
    /// marks the old one carried.
    /// </remarks>
    internal static TNode WithAnnotationsFrom<TNode>(this TNode node, JsonSyntaxNode original)
        where TNode : JsonSyntaxNode
    {
        var annotations = original.GetAnnotations().ToArray();

        return annotations.Length == 0 ? node : node.WithAdditionalAnnotations(annotations);
    }
}
