namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>Turns sequences of red nodes back into the green node that holds them.</summary>
internal static class SyntaxListBuilder
{
    public static GreenNode?[] ToGreenArray<TNode>(IEnumerable<TNode> nodes)
        where TNode : SyntaxNode
        => [.. nodes.Select(node => (GreenNode?)node.Green)];

    public static SyntaxNode? CreateNode<TNode>(IEnumerable<TNode> nodes)
        where TNode : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return SyntaxList.List(ToGreenArray(nodes))?.CreateRed();
    }
}
