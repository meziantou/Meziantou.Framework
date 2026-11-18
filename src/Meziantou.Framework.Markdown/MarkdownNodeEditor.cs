using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Syntax.InternalSyntax;
using Green = Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// Builds the nodes that the <c>With</c> methods of the nodes return: a copy of a node whose children are changed, which is in no
/// tree. The children are green nodes, so that the tokens of the copy keep the trivia of the ones they replace.
/// </summary>
internal static class MarkdownNodeEditor
{
    public static List<GreenNode?> GetChildren(MarkdownSyntaxNode node)
    {
        var children = new List<GreenNode?>();
        foreach (var child in node.ChildNodesAndTokens())
        {
            children.Add(child.AsNode(out var childNode) ? childNode.Green : child.AsToken().Node);
        }

        return children;
    }

    public static TNode WithChildren<TNode>(TNode node, List<GreenNode?> children)
        where TNode : MarkdownSyntaxNode
    {
        GreenNode green = new MarkdownNode(node.Kind(), CollectionsMarshal.AsSpan(children));
        if (node.Green.GetAnnotations() is { Length: > 0 } annotations)
        {
            green = green.SetAnnotations(annotations);
        }

        return (TNode)green.CreateRed(parent: null, position: 0);
    }

    /// <summary>Gets the index of the first child token of <paramref name="kind"/>, or -1.</summary>
    public static int IndexOfToken(List<GreenNode?> children, SyntaxKind kind, int start = 0)
    {
        for (var i = start; i < children.Count; i++)
        {
            if (children[i] is { IsToken: true } token && token.RawKind == (int)kind)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Gets the index of the last child token of <paramref name="kind"/>, or -1.</summary>
    public static int LastIndexOfToken(List<GreenNode?> children, SyntaxKind kind)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i] is { IsToken: true } token && token.RawKind == (int)kind)
            {
                return i;
            }
        }

        return -1;
    }

    public static Green.SyntaxToken Token(SyntaxKind kind, string text, GreenNode? leadingTrivia, GreenNode? trailingTrivia)
        => new((int)kind, text, leadingTrivia, trailingTrivia, isMissing: false);

    /// <summary>Gets the same token with another text.</summary>
    public static Green.SyntaxToken WithText(GreenNode token, string text)
    {
        var greenToken = (Green.SyntaxToken)token;
        return Token((SyntaxKind)greenToken.RawKind, text, greenToken.LeadingTrivia, greenToken.TrailingTrivia);
    }

    /// <summary>Gets the trailing trivia of the last token of a child, a token or a node.</summary>
    public static GreenNode? GetTrailingTrivia(GreenNode child) => GetLastToken(child)?.TrailingTrivia;

    /// <summary>Gets a child, a token or a node, whose last token has the trailing trivia <paramref name="trailingTrivia"/>.</summary>
    public static GreenNode WithTrailingTrivia(GreenNode child, GreenNode? trailingTrivia)
    {
        if (child is Green.SyntaxToken token)
        {
            return token.WithTrivia(token.LeadingTrivia, trailingTrivia);
        }

        var node = child.CreateRed(parent: null, position: 0);
        var last = node.GetLastToken();
        if (last.Node is not Green.SyntaxToken lastToken)
        {
            return child;
        }

        var replacement = new SyntaxToken(parent: null, lastToken.WithTrivia(lastToken.LeadingTrivia, trailingTrivia), position: 0, index: 0);
        return node.ReplaceToken(last, replacement).Green;
    }

    /// <summary>
    /// Inserts <paramref name="inserted"/>, a token or a node, after the child at <paramref name="index"/>, on its line: the whitespace
    /// after the child separates them, a space when there is none and <paramref name="space"/> is <see langword="true"/>, and the
    /// rest of its trailing trivia, such as the line ending, goes after the inserted child.
    /// </summary>
    public static void InsertOnLineAfter(List<GreenNode?> children, int index, GreenNode inserted, bool space = true)
    {
        var previous = children[index]!;
        var (whitespace, rest) = SplitWhitespace(GetTrailingTrivia(previous));
        children[index] = WithTrailingTrivia(previous, whitespace ?? (space ? new Green.SyntaxTrivia((int)SyntaxKind.WhitespaceTrivia, " ") : null));
        children.Insert(index + 1, WithTrailingTrivia(inserted, rest));
    }

    /// <summary>
    /// Removes the children from <paramref name="start"/> to <paramref name="end"/>, and the whitespace that separates them from the
    /// child before them: this child gets the trailing trivia of the last removed child, such as the line ending.
    /// </summary>
    public static void RemoveOnLine(List<GreenNode?> children, int start, int end)
    {
        var trailing = GetTrailingTrivia(children[end]!);
        children.RemoveRange(start, end - start + 1);
        children[start - 1] = WithTrailingTrivia(children[start - 1]!, trailing);
    }

    // The whitespace at the start of trivia, and the rest of it
    private static (GreenNode? Whitespace, GreenNode? Remaining) SplitWhitespace(GreenNode? trivia)
    {
        var items = new List<GreenNode?>();
        if (trivia is { IsTrivia: true })
        {
            items.Add(trivia);
        }
        else if (trivia is not null)
        {
            for (var i = 0; i < trivia.SlotCount; i++)
            {
                items.Add(trivia.GetSlot(i));
            }
        }

        var count = 0;
        while (count < items.Count && items[count]?.RawKind == (int)SyntaxKind.WhitespaceTrivia)
        {
            count++;
        }

        return (Green.SyntaxList.List(CollectionsMarshal.AsSpan(items)[..count]), Green.SyntaxList.List(CollectionsMarshal.AsSpan(items)[count..]));
    }

    private static Green.SyntaxToken? GetLastToken(GreenNode node)
    {
        if (node is Green.SyntaxToken token)
        {
            return token;
        }

        for (var i = node.SlotCount - 1; i >= 0; i--)
        {
            if (node.GetSlot(i) is { } slot && GetLastToken(slot) is { } last)
            {
                return last;
            }
        }

        return null;
    }
}
