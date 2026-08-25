namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Builds the source text of a list of nodes interleaved with separator tokens, where <c>separators[i]</c> follows
/// <c>nodes[i]</c>. The separator list is either the same length as the node list, or one shorter when the last
/// node has no trailing separator.
/// </summary>
internal static class SeparatedNodes
{
    public static string BuildText<TNode>(IReadOnlyList<TNode>? nodes, IReadOnlyList<ShellSyntaxToken>? separators)
        where TNode : ShellSyntaxNode
    {
        nodes ??= [];
        separators ??= [];

        var builder = new StringBuilder();
        for (var index = 0; index < nodes.Count; index++)
        {
            builder.Append(nodes[index].ToFullString());
            if (index < separators.Count)
            {
                builder.Append(separators[index].ToFullString());
            }
        }

        for (var index = nodes.Count; index < separators.Count; index++)
        {
            builder.Append(separators[index].ToFullString());
        }

        return builder.ToString();
    }

    public static int GetFullStart<TNode>(IReadOnlyList<TNode>? nodes, IReadOnlyList<ShellSyntaxToken>? separators)
        where TNode : ShellSyntaxNode
    {
        if (nodes is { Count: > 0 })
            return nodes[0].FullSpan.Start;

        if (separators is { Count: > 0 })
            return separators[0].FullSpan.Start;

        return 0;
    }
}
