using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Syntax;

/// <summary>Rebuilds a tree with some of its parts replaced.</summary>
/// <remarks>
/// <para>
/// Only the spine from a replaced part up to the root is rebuilt. Every subtree that contains nothing to replace is
/// carried over by reference, so an edit costs the depth of the tree rather than its size, and the parts that did not
/// change stay the very same immutable nodes -- which is what makes it possible to ask, afterwards, what actually
/// changed.
/// </para>
/// <para>
/// Nodes are matched by identity, and tokens and trivia by the immutable node behind them together with their
/// position, because a token is a view rather than an object and two views of the same token compare equal only when
/// they agree on both.
/// </para>
/// </remarks>
internal sealed class SyntaxReplacer
{
    private readonly Dictionary<SyntaxNode, GreenNode?> _nodes = [];
    private readonly Dictionary<(GreenNode Green, int Position), GreenNode?> _tokens = [];
    private readonly Dictionary<(GreenNode Green, int Position), GreenNode?> _trivia = [];
    private TextSpan? _bounds;
    private bool _replacedAnything;

    public void ReplaceNode(SyntaxNode oldNode, GreenNode? newGreen)
    {
        _nodes[oldNode] = newGreen;
        Extend(oldNode.FullSpan);
    }

    public void ReplaceToken(SyntaxToken oldToken, GreenNode? newGreen)
    {
        if (oldToken.Node is not { } green)
            return;

        _tokens[(green, oldToken.FullSpan.Start)] = newGreen;
        Extend(oldToken.FullSpan);
    }

    public void ReplaceTrivia(SyntaxTrivia oldTrivia, GreenNode? newGreen)
    {
        if (oldTrivia.UnderlyingNode is not { } green)
            return;

        _trivia[(green, oldTrivia.FullSpan.Start)] = newGreen;
        Extend(oldTrivia.FullSpan);
    }

    /// <summary>Rebuilds <paramref name="root"/> and reports whether anything was actually replaced.</summary>
    public GreenNode Rebuild(SyntaxNode root, out bool replacedAnything)
    {
        _replacedAnything = false;
        var result = RebuildNode(root) ?? throw new InvalidOperationException("The root of a tree cannot be removed.");
        replacedAnything = _replacedAnything;

        return result;
    }

    private void Extend(TextSpan span) => _bounds = _bounds is { } bounds ? TextSpan.FromBounds(Math.Min(bounds.Start, span.Start), Math.Max(bounds.End, span.End)) : span;

    private bool CouldContainATarget(TextSpan span) => _bounds is not { } bounds || span.IntersectsWith(bounds);

    private GreenNode? RebuildNode(SyntaxNode node)
    {
        if (_nodes.TryGetValue(node, out var replacement))
        {
            _replacedAnything = true;

            return replacement;
        }

        var green = node.Green;
        if (!CouldContainATarget(node.FullSpan))
            return green;

        GreenNode?[]? newSlots = null;
        for (var i = 0; i < green.SlotCount; i++)
        {
            var childGreen = green.GetSlot(i);
            if (childGreen is null)
                continue;

            var newChild = childGreen.IsToken
                ? RebuildToken(node, i, childGreen)
                : node.GetNodeSlot(i) is { } childRed ? RebuildNode(childRed) : childGreen;

            if (ReferenceEquals(newChild, childGreen))
                continue;

            newSlots ??= CopySlots(green);
            newSlots[i] = newChild;
        }

        return newSlots is null ? green : green.WithSlots(newSlots);
    }

    private GreenNode? RebuildToken(SyntaxNode parent, int slot, GreenNode green)
    {
        var position = parent.GetChildPosition(slot);
        if (_tokens.TryGetValue((green, position), out var replacement))
        {
            _replacedAnything = true;

            return replacement;
        }

        if (_trivia.Count == 0 || green is not GreenToken token || !CouldContainATarget(new TextSpan(position, green.FullWidth)))
            return green;

        var leading = RebuildTrivia(token.LeadingTrivia, position);
        var trailing = RebuildTrivia(token.TrailingTrivia, position + token.GetLeadingTriviaWidth() + token.Text.Length);
        if (ReferenceEquals(leading, token.LeadingTrivia) && ReferenceEquals(trailing, token.TrailingTrivia))
            return green;

        return token.WithTrivia(leading, trailing);
    }

    private GreenNode? RebuildTrivia(GreenNode? trivia, int position)
    {
        if (trivia is null)
            return null;

        var count = GreenNodeList.Count(trivia);
        GreenNode?[]? replaced = null;
        for (var i = 0; i < count; i++)
        {
            var item = GreenNodeList.ElementAt(trivia, i)!;
            if (!_trivia.TryGetValue((item, position + GreenNodeList.OffsetAt(trivia, i)), out var replacement))
                continue;

            replaced ??= GreenNodeList.ToArray(trivia);
            replaced[i] = replacement;
            _replacedAnything = true;
        }

        return replaced is null ? trivia : InternalSyntax.SyntaxList.List(replaced);
    }

    private static GreenNode?[] CopySlots(GreenNode green)
    {
        var slots = new GreenNode?[green.SlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = green.GetSlot(i);
        }

        return slots;
    }
}
