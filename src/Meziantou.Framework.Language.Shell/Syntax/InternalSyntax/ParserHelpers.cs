using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>The list-building the three dialect parsers share.</summary>
internal static class ParserHelpers
{
    /// <summary>Builds the list a node's list slot holds.</summary>
    public static GreenNode? List<TNode>(IReadOnlyList<TNode>? nodes)
        where TNode : GreenNode
    {
        if (nodes is null || nodes.Count == 0)
            return null;

        var items = new GreenNode?[nodes.Count];
        for (var index = 0; index < nodes.Count; index++)
        {
            items[index] = nodes[index];
        }

        return SyntaxFactory.List(items);
    }

    /// <summary>
    /// Builds the token list of a skipped-text node, marking each token so the flag reaches every node above it.
    /// </summary>
    /// <remarks>
    /// A node reports ContainsSkippedText from a flag its children pass upward, and only a token can set it. The
    /// tokens here are ordinary ones the parser could make no use of, so this is where they are marked.
    /// </remarks>
    public static GreenNode? SkippedTokens(ReadOnlySpan<GreenNode?> tokens)
    {
        var marked = new GreenNode?[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            marked[index] = tokens[index] is GreenToken token ? token.AsSkippedText() : tokens[index];
        }

        return SyntaxFactory.ListNode(marked);
    }

    /// <summary>Builds the list a token-list slot holds.</summary>
    public static GreenNode? TokenList(IReadOnlyList<ScannedToken>? tokens)
    {
        if (tokens is null || tokens.Count == 0)
            return null;

        var items = new GreenNode?[tokens.Count];
        for (var index = 0; index < tokens.Count; index++)
        {
            items[index] = tokens[index].Green;
        }

        return SyntaxFactory.ListNode(items);
    }

    /// <summary>
    /// Weaves nodes and the separators that follow them into the one sequence a separated list holds.
    /// </summary>
    /// <remarks>
    /// The separator list is either the same length as the node list, or one shorter when the last node has no
    /// trailing separator -- which is what the parsers produce.
    /// </remarks>
    /// <remarks>
    /// A separated list has to alternate, so a gap between two nodes -- which happens when a line break rather than a
    /// separator ended a statement, or when a here-document body follows the line that introduced it -- is filled
    /// with a zero-width separator of <paramref name="missingSeparatorKind"/>.
    /// </remarks>
    public static GreenNode? Separated<TNode>(IReadOnlyList<TNode>? nodes, IReadOnlyList<ScannedToken>? separators, SyntaxKind missingSeparatorKind = SyntaxKind.SemicolonToken)
        where TNode : GreenNode
    {
        nodes ??= [];
        separators ??= [];
        if (nodes.Count == 0 && separators.Count == 0)
            return null;

        var items = new List<GreenNode?>(nodes.Count + separators.Count);
        for (var index = 0; index < nodes.Count; index++)
        {
            items.Add(nodes[index]);
            if (index < separators.Count)
            {
                items.Add(separators[index].Green);
            }
            else if (index < nodes.Count - 1)
            {
                items.Add(SyntaxFactory.MissingToken(missingSeparatorKind));
            }
        }

        for (var index = nodes.Count; index < separators.Count; index++)
        {
            items.Add(separators[index].Green);
        }

        return SyntaxFactory.List(CollectionsMarshal.AsSpan(items));
    }
}
