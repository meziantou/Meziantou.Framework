namespace Meziantou.Framework.Language.Toml;

/// <summary>Reads the TOML kind of the tokens, trivia, and nodes the shared syntax types hand back.</summary>
public static class TomlExtensions
{
    /// <summary>Gets the TOML kind of a node reached through the shared type rather than the TOML one.</summary>
    public static SyntaxKind Kind(this SyntaxNode? node) => (SyntaxKind)(node?.RawKind ?? 0);

    public static SyntaxKind Kind(this SyntaxToken token) => (SyntaxKind)token.RawKind;
    public static SyntaxKind Kind(this SyntaxTrivia trivia) => (SyntaxKind)trivia.RawKind;
    public static SyntaxKind Kind(this SyntaxNodeOrToken nodeOrToken) => (SyntaxKind)nodeOrToken.RawKind;

    public static bool IsKind(this SyntaxNode? node, SyntaxKind kind) => node?.RawKind == (int)kind;
    public static bool IsKind(this SyntaxToken token, SyntaxKind kind) => token.RawKind == (int)kind;
    public static bool IsKind(this SyntaxTrivia trivia, SyntaxKind kind) => trivia.RawKind == (int)kind;
    public static bool IsKind(this SyntaxNodeOrToken nodeOrToken, SyntaxKind kind) => nodeOrToken.RawKind == (int)kind;

    /// <summary>Carries the annotations of <paramref name="original"/> onto a node built to replace it.</summary>
    internal static TNode WithAnnotationsFrom<TNode>(this TNode node, TomlSyntaxNode original)
        where TNode : TomlSyntaxNode
    {
        var annotations = original.GetAnnotations().ToArray();

        return annotations.Length == 0 ? node : node.WithAdditionalAnnotations(annotations);
    }
}
