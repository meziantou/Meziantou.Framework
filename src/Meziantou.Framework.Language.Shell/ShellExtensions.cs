namespace Meziantou.Framework.Language.Shell;

/// <summary>Reads the shell kind of the tokens, trivia, and nodes the shared syntax types hand back.</summary>
/// <remarks>
/// The shared types carry a kind as a plain number because they are used by every language. These turn that number
/// back into the shell kind it stands for.
/// </remarks>
public static class ShellExtensions
{
    /// <summary>Gets the shell kind of a node reached through the shared type rather than the shell one.</summary>
    public static SyntaxKind Kind(this SyntaxNode? node) => (SyntaxKind)(node?.RawKind ?? 0);

    public static SyntaxKind Kind(this SyntaxToken token) => (SyntaxKind)token.RawKind;
    public static SyntaxKind Kind(this SyntaxTrivia trivia) => (SyntaxKind)trivia.RawKind;
    public static SyntaxKind Kind(this SyntaxNodeOrToken nodeOrToken) => (SyntaxKind)nodeOrToken.RawKind;

    public static bool IsKind(this SyntaxNode? node, SyntaxKind kind) => node?.RawKind == (int)kind;
    public static bool IsKind(this SyntaxToken token, SyntaxKind kind) => token.RawKind == (int)kind;
    public static bool IsKind(this SyntaxTrivia trivia, SyntaxKind kind) => trivia.RawKind == (int)kind;
    public static bool IsKind(this SyntaxNodeOrToken nodeOrToken, SyntaxKind kind) => nodeOrToken.RawKind == (int)kind;

    /// <summary>Determines whether the source actually had this optional token.</summary>
    /// <remarks>
    /// An optional slot the source left empty holds the default token, whose kind is <see cref="SyntaxKind.None"/>.
    /// That is not the same as a missing token, which the parser puts in a required slot to stand in for text that
    /// should have been there.
    /// </remarks>
    public static bool IsPresent(this SyntaxToken token) => token.RawKind != (int)SyntaxKind.None;

    /// <summary>Determines whether <paramref name="trivia"/> is a comment in any of the dialects.</summary>
    public static bool IsComment(this SyntaxTrivia trivia) => SyntaxFacts.IsComment(trivia.Kind());

    /// <summary>Carries the annotations of <paramref name="original"/> onto a node built to replace it.</summary>
    /// <remarks>
    /// This is what makes an annotation survive an edit: every <c>Update</c> builds a fresh node and then copies the
    /// marks the old one carried.
    /// </remarks>
    internal static TNode WithAnnotationsFrom<TNode>(this TNode node, ShellSyntaxNode original)
        where TNode : ShellSyntaxNode
    {
        var annotations = original.GetAnnotations().ToArray();

        return annotations.Length == 0 ? node : node.WithAdditionalAnnotations(annotations);
    }
}
