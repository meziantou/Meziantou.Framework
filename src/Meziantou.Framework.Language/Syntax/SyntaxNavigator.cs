namespace Meziantou.Framework.Language.Syntax;

/// <summary>Steps from one token to the next across the whole tree.</summary>
/// <remarks>
/// Finding a node's place among its siblings means scanning them, so stepping token by token over a long list costs
/// more than it looks. <see cref="SyntaxNode.DescendantTokens(Func{SyntaxNode, bool})"/> is the cheaper way to visit
/// many tokens in a row.
/// </remarks>
internal static class SyntaxNavigator
{
    public static SyntaxToken GetNextToken(SyntaxToken current)
    {
        var parent = current.Parent;
        if (parent is null)
            return default;

        var index = current.Index;
        while (true)
        {
            var children = parent.ChildNodesAndTokens();
            for (var i = index + 1; i < children.Count; i++)
            {
                if (FirstToken(children[i]) is { } token)
                    return token;
            }

            if (parent.Parent is not { } grandParent)
                return default;

            index = IndexInParent(parent);
            parent = grandParent;
        }
    }

    public static SyntaxToken GetPreviousToken(SyntaxToken current)
    {
        var parent = current.Parent;
        if (parent is null)
            return default;

        var index = current.Index;
        while (true)
        {
            var children = parent.ChildNodesAndTokens();
            for (var i = Math.Min(index, children.Count) - 1; i >= 0; i--)
            {
                if (LastToken(children[i]) is { } token)
                    return token;
            }

            if (parent.Parent is not { } grandParent)
                return default;

            index = IndexInParent(parent);
            parent = grandParent;
        }
    }

    private static SyntaxToken? FirstToken(SyntaxNodeOrToken child)
    {
        if (child.AsToken(out var token))
            return token;

        if (child.AsNode(out var node) && node.GetFirstToken() is { Node: not null } first)
            return first;

        return null;
    }

    private static SyntaxToken? LastToken(SyntaxNodeOrToken child)
    {
        if (child.AsToken(out var token))
            return token;

        if (child.AsNode(out var node) && node.GetLastToken() is { Node: not null } last)
            return last;

        return null;
    }

    private static int IndexInParent(SyntaxNode node)
    {
        if (node.Parent is not { } parent)
            return -1;

        var children = parent.ChildNodesAndTokens();
        var index = 0;
        foreach (var child in children)
        {
            if (child.AsNode(out var childNode) && ReferenceEquals(childNode, node))
                return index;

            index++;
        }

        return -1;
    }
}
