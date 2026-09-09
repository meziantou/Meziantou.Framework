using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Syntax;

namespace Meziantou.Framework.Language;

/// <summary>Operations that return the same node type they were given.</summary>
/// <remarks>
/// These are extensions rather than members so that the result keeps its type: editing a document returns a document,
/// not the abstract node type, and no cast is needed at the call site.
/// </remarks>
public static class SyntaxNodeExtensions
{
    /// <summary>Returns <paramref name="root"/> with <paramref name="newNode"/> in place of <paramref name="oldNode"/>.</summary>
    /// <remarks>
    /// The result is a new tree that shares every part of the old one that did not change, and it is the same type as
    /// <paramref name="root"/>, so editing a document gives back a document. Nothing is re-parsed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="oldNode"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot ReplaceNode<TRoot>(this TRoot root, SyntaxNode oldNode, SyntaxNode newNode)
        where TRoot : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(newNode);

        return root.ReplaceNodes([oldNode], (_, _) => newNode);
    }

    /// <summary>Returns <paramref name="root"/> with each of <paramref name="nodes"/> replaced by what <paramref name="computeReplacement"/> returns for it.</summary>
    /// <param name="root">The tree to rebuild.</param>
    /// <param name="nodes">The nodes to replace.</param>
    /// <param name="computeReplacement">Given the original node twice, returns what to put in its place.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of <paramref name="nodes"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot ReplaceNodes<TRoot, TNode>(this TRoot root, IEnumerable<TNode> nodes, Func<TNode, TNode, SyntaxNode> computeReplacement)
        where TRoot : SyntaxNode
        where TNode : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(computeReplacement);

        var replacer = new SyntaxReplacer();
        foreach (var node in nodes)
        {
            ArgumentNullException.ThrowIfNull(node, nameof(nodes));
            replacer.ReplaceNode(node, computeReplacement(node, node).Green);
        }

        return Rebuild(root, replacer, "One of the nodes is not part of this tree.", nameof(nodes));
    }

    /// <summary>Returns <paramref name="root"/> with <paramref name="newToken"/> in place of <paramref name="oldToken"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="oldToken"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot ReplaceToken<TRoot>(this TRoot root, SyntaxToken oldToken, SyntaxToken newToken)
        where TRoot : SyntaxNode
        => root.ReplaceTokens([oldToken], (_, _) => newToken);

    /// <summary>Returns <paramref name="root"/> with each of <paramref name="tokens"/> replaced by what <paramref name="computeReplacement"/> returns for it.</summary>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of <paramref name="tokens"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot ReplaceTokens<TRoot>(this TRoot root, IEnumerable<SyntaxToken> tokens, Func<SyntaxToken, SyntaxToken, SyntaxToken> computeReplacement)
        where TRoot : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(computeReplacement);

        var replacer = new SyntaxReplacer();
        foreach (var token in tokens)
        {
            replacer.ReplaceToken(token, computeReplacement(token, token).Node);
        }

        return Rebuild(root, replacer, "One of the tokens is not part of this tree.", nameof(tokens));
    }

    /// <summary>Returns <paramref name="root"/> with <paramref name="newTrivia"/> in place of <paramref name="oldTrivia"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="oldTrivia"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot ReplaceTrivia<TRoot>(this TRoot root, SyntaxTrivia oldTrivia, SyntaxTrivia newTrivia)
        where TRoot : SyntaxNode
        => root.ReplaceTrivia([oldTrivia], (_, _) => newTrivia);

    /// <summary>Returns <paramref name="root"/> with each of <paramref name="trivia"/> replaced by what <paramref name="computeReplacement"/> returns for it.</summary>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of <paramref name="trivia"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot ReplaceTrivia<TRoot>(this TRoot root, IEnumerable<SyntaxTrivia> trivia, Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia> computeReplacement)
        where TRoot : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(trivia);
        ArgumentNullException.ThrowIfNull(computeReplacement);

        var replacer = new SyntaxReplacer();
        foreach (var item in trivia)
        {
            replacer.ReplaceTrivia(item, computeReplacement(item, item).UnderlyingNode);
        }

        return Rebuild(root, replacer, "One of the trivia is not part of this tree.", nameof(trivia));
    }

    /// <summary>Returns <paramref name="root"/> with <paramref name="newNodes"/> in place of <paramref name="oldNode"/>.</summary>
    /// <remarks>Replacing one node with several requires that it sits in a list, or is the only thing in its place.</remarks>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="oldNode"/> is not part of <paramref name="root"/>, or is not somewhere several nodes can go.</exception>
    public static TRoot ReplaceNode<TRoot>(this TRoot root, SyntaxNode oldNode, IEnumerable<SyntaxNode> newNodes)
        where TRoot : SyntaxNode
        => root.SpliceIntoList(oldNode, newNodes, removeOriginal: true, insertBefore: true);

    /// <summary>Returns <paramref name="root"/> with <paramref name="newNodes"/> added in front of <paramref name="nodeInList"/>.</summary>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeInList"/> is not part of <paramref name="root"/>, or is not in a list.</exception>
    public static TRoot InsertNodesBefore<TRoot>(this TRoot root, SyntaxNode nodeInList, IEnumerable<SyntaxNode> newNodes)
        where TRoot : SyntaxNode
        => root.SpliceIntoList(nodeInList, newNodes, removeOriginal: false, insertBefore: true);

    /// <summary>Returns <paramref name="root"/> with <paramref name="newNodes"/> added after <paramref name="nodeInList"/>.</summary>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeInList"/> is not part of <paramref name="root"/>, or is not in a list.</exception>
    public static TRoot InsertNodesAfter<TRoot>(this TRoot root, SyntaxNode nodeInList, IEnumerable<SyntaxNode> newNodes)
        where TRoot : SyntaxNode
        => root.SpliceIntoList(nodeInList, newNodes, removeOriginal: false, insertBefore: false);

    /// <summary>Returns <paramref name="root"/> without <paramref name="node"/>.</summary>
    /// <param name="root">The tree to rebuild.</param>
    /// <param name="node">The node to take out. Its separator goes with it when it sits in a separated list.</param>
    /// <param name="options">What to keep of the trivia around it. Anything kept moves onto what now stands in its place.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot RemoveNode<TRoot>(this TRoot root, SyntaxNode node, SyntaxRemoveOptions options)
        where TRoot : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);

        return root.RemoveNodes([node], options);
    }

    /// <summary>Returns <paramref name="root"/> without <paramref name="nodes"/>.</summary>
    /// <param name="root">The tree to rebuild.</param>
    /// <param name="nodes">The nodes to take out. Naming both a node and something inside it removes the node, once.</param>
    /// <param name="options">What to keep of the trivia around them.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of <paramref name="nodes"/> is not part of <paramref name="root"/>.</exception>
    public static TRoot RemoveNodes<TRoot>(this TRoot root, IEnumerable<SyntaxNode> nodes, SyntaxRemoveOptions options)
        where TRoot : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(nodes);

        var targets = nodes.ToArray();
        foreach (var target in targets)
        {
            ArgumentNullException.ThrowIfNull(target, nameof(nodes));
        }

        // Asking for a node and for something inside it is asking for the node.
        var pending = new HashSet<SyntaxNode>(targets);
        var replacer = new SyntaxReplacer();
        var groups = new Dictionary<(SyntaxNode Parent, int Slot), List<int>>();

        foreach (var target in targets)
        {
            if (target.Ancestors().Any(pending.Contains))
                continue;

            if (!TryLocate(target, out var parent, out var slot, out var indexInList))
                throw new ArgumentException("The node is not part of this tree.", nameof(nodes));

            if (!groups.TryGetValue((parent, slot), out var indices))
            {
                groups[(parent, slot)] = indices = [];
            }

            indices.Add(indexInList);
        }

        if (groups.Count == 0)
            throw new ArgumentException("There was nothing to remove.", nameof(nodes));

        foreach (var ((parent, slot), indices) in groups)
        {
            var owner = parent;
            var slotIndex = slot;
            var removedIndices = indices;
            replacer.EditSlots(parent, slots => RemoveFromSlot(owner, slots, slotIndex, removedIndices, options));
        }

        return Rebuild(root, replacer, "The node is not part of this tree.", nameof(nodes));
    }

    /// <summary>Takes the named items out of one slot and finds a home for whatever trivia is being kept.</summary>
    private static GreenNode?[] RemoveFromSlot(SyntaxNode parent, GreenNode?[] slots, int slot, List<int> indices, SyntaxRemoveOptions options)
    {
        if (indices.Contains(-1))
        {
            // The node filled the slot on its own rather than sitting in a list.
            var removedAlone = slots[slot] is { } only ? new[] { only } : [];
            slots[slot] = null;

            return AttachToNeighbouringSlot(slots, slot, SyntaxNodeRemover.ResidualTrivia(removedAlone, options, parent.Green));
        }

        var items = GreenNodeList.ToArray(slots[slot]);
        var removing = new HashSet<int>();
        foreach (var index in indices)
        {
            removing.Add(index);

            // A separated list has to keep alternating, so the separator that went with the node goes too: the one
            // after it, or the one before it when nothing follows.
            if (index + 1 < items.Length && !removing.Contains(index + 1))
            {
                removing.Add(index + 1);
            }
            else if (index > 0)
            {
                removing.Add(index - 1);
            }
        }

        var removed = new List<GreenNode>();
        var kept = new List<GreenNode?>();
        var insertionIndex = -1;
        for (var i = 0; i < items.Length; i++)
        {
            if (removing.Contains(i))
            {
                if (items[i] is { } item)
                {
                    removed.Add(item);
                }

                insertionIndex = insertionIndex < 0 ? kept.Count : insertionIndex;
                continue;
            }

            kept.Add(items[i]);
        }

        var residual = SyntaxNodeRemover.ResidualTrivia(removed, options, parent.Green);
        if (residual is not null && insertionIndex >= 0)
        {
            if (insertionIndex < kept.Count)
            {
                kept[insertionIndex] = SyntaxNodeRemover.PrependLeadingTrivia(kept[insertionIndex]!, residual);
                residual = null;
            }
            else if (kept.Count > 0)
            {
                kept[^1] = SyntaxNodeRemover.AppendTrailingTrivia(kept[^1]!, residual);
                residual = null;
            }
        }

        slots[slot] = InternalSyntax.SyntaxList.ListNode([.. kept]);

        return residual is null ? slots : AttachToNeighbouringSlot(slots, slot, residual);
    }

    /// <summary>Puts kept trivia on the nearest thing left in the parent once its own slot has emptied.</summary>
    private static GreenNode?[] AttachToNeighbouringSlot(GreenNode?[] slots, int slot, GreenNode? residual)
    {
        if (residual is null)
            return slots;

        for (var i = slot + 1; i < slots.Length; i++)
        {
            if (slots[i] is { } following)
            {
                slots[i] = SyntaxNodeRemover.PrependLeadingTrivia(following, residual);

                return slots;
            }
        }

        for (var i = slot - 1; i >= 0; i--)
        {
            if (slots[i] is { } preceding)
            {
                slots[i] = SyntaxNodeRemover.AppendTrailingTrivia(preceding, residual);

                return slots;
            }
        }

        return slots;
    }

    /// <summary>Returns <paramref name="node"/> carrying <paramref name="annotations"/> as well as the ones it already has.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="annotations"/> is <see langword="null"/>.</exception>
    public static TNode WithAdditionalAnnotations<TNode>(this TNode node, params SyntaxAnnotation[] annotations)
        where TNode : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(annotations);

        return (TNode)node.Green.WithAdditionalAnnotations(annotations).CreateRed();
    }

    /// <summary>Returns <paramref name="node"/> without <paramref name="annotations"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="annotations"/> is <see langword="null"/>.</exception>
    public static TNode WithoutAnnotations<TNode>(this TNode node, params SyntaxAnnotation[] annotations)
        where TNode : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(annotations);

        return (TNode)node.Green.WithoutAnnotations(annotations).CreateRed();
    }

    /// <summary>Returns <paramref name="node"/> without any annotation of the given kind.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="annotationKind"/> is <see langword="null"/>.</exception>
    public static TNode WithoutAnnotations<TNode>(this TNode node, string annotationKind)
        where TNode : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(annotationKind);

        return node.WithoutAnnotations([.. node.GetAnnotations(annotationKind)]);
    }

    /// <summary>Returns <paramref name="to"/> carrying the annotations of <paramref name="from"/> as well as its own.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="from"/> or <paramref name="to"/> is <see langword="null"/>.</exception>
    public static TNode CopyAnnotationsTo<TNode>(this SyntaxNode from, TNode to)
        where TNode : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return to.WithAdditionalAnnotations([.. from.GetAnnotations()]);
    }

    private static TRoot SpliceIntoList<TRoot>(this TRoot root, SyntaxNode nodeInList, IEnumerable<SyntaxNode> newNodes, bool removeOriginal, bool insertBefore)
        where TRoot : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(nodeInList);
        ArgumentNullException.ThrowIfNull(newNodes);

        if (!TryLocate(nodeInList, out var parent, out var slot, out var indexInList))
            throw new ArgumentException("The node is not part of this tree.", nameof(nodeInList));

        var replacements = newNodes.Select(node => (GreenNode?)node.Green).ToArray();
        var slotGreen = parent.Green.GetSlot(slot);

        GreenNode?[] items;
        int start;
        if (indexInList < 0)
        {
            // The node fills the slot on its own rather than sitting in a list.
            items = [slotGreen];
            start = 0;
        }
        else
        {
            items = GreenNodeList.ToArray(slotGreen);
            start = indexInList;
        }

        var spliced = Splice(items, start, replacements, removeOriginal, insertBefore, nodeInList.Green);

        var newSlots = new GreenNode?[parent.Green.SlotCount];
        for (var i = 0; i < newSlots.Length; i++)
        {
            newSlots[i] = parent.Green.GetSlot(i);
        }

        newSlots[slot] = InternalSyntax.SyntaxList.ListNode(spliced);

        var replacer = new SyntaxReplacer();
        replacer.ReplaceNode(parent, parent.Green.WithSlots(newSlots));

        return Rebuild(root, replacer, "The node is not part of this tree.", nameof(nodeInList));
    }


    /// <summary>
    /// Splices nodes into the contents of a slot, adding the separators a separated list needs to stay alternating.
    /// </summary>
    /// <remarks>
    /// A list is separated when it holds tokens between its nodes. Adding a node to one means adding a separator with
    /// it, and taking a node out means taking its separator too, or the list stops alternating and can no longer be
    /// read.
    /// </remarks>
    private static GreenNode?[] Splice(GreenNode?[] items, int index, GreenNode?[] replacements, bool removeOriginal, bool insertBefore, GreenNode original)
    {
        var isSeparated = Array.Exists(items, item => item is { IsToken: true });
        var result = new List<GreenNode?>(items);

        if (!isSeparated)
        {
            if (removeOriginal)
            {
                result.RemoveAt(index);
            }

            result.InsertRange(removeOriginal || insertBefore ? index : index + 1, replacements);

            return [.. result];
        }

        var separator = FindSeparator(items) ?? original.CreateSeparator()
            ?? throw new InvalidOperationException("The language does not define a separator for its lists, so nodes cannot be spliced into one.");

        if (removeOriginal)
        {
            result.RemoveAt(index);
            if (replacements.Length == 0)
            {
                // Take the separator that went with the node: the one after it, or the one before it when it was last.
                if (index < result.Count)
                {
                    result.RemoveAt(index);
                }
                else if (index > 0)
                {
                    result.RemoveAt(index - 1);
                }

                return [.. result];
            }

            result.InsertRange(index, Interleave(replacements, separator, separatorAtEnd: false));

            return [.. result];
        }

        if (insertBefore)
        {
            result.InsertRange(index, Interleave(replacements, separator, separatorAtEnd: true));

            return [.. result];
        }

        result.InsertRange(index + 1, [separator, .. Interleave(replacements, separator, separatorAtEnd: false)]);

        return [.. result];
    }

    private static GreenNode? FindSeparator(GreenNode?[] items) => Array.Find(items, item => item is { IsToken: true });

    private static List<GreenNode?> Interleave(GreenNode?[] nodes, GreenNode separator, bool separatorAtEnd)
    {
        var result = new List<GreenNode?>((nodes.Length * 2) - 1);
        for (var i = 0; i < nodes.Length; i++)
        {
            if (i > 0)
            {
                result.Add(separator);
            }

            result.Add(nodes[i]);
        }

        if (separatorAtEnd && nodes.Length > 0)
        {
            result.Add(separator);
        }

        return result;
    }

    /// <summary>Finds where <paramref name="node"/> sits in its parent: which slot, and where in that slot's list.</summary>
    private static bool TryLocate(SyntaxNode node, [NotNullWhen(true)] out SyntaxNode? parent, out int slot, out int indexInList)
    {
        slot = -1;
        indexInList = -1;
        parent = node.Parent;
        if (parent is null)
            return false;

        for (var i = 0; i < parent.Green.SlotCount; i++)
        {
            var childGreen = parent.Green.GetSlot(i);
            if (childGreen is null || childGreen.IsToken)
                continue;

            if (!childGreen.IsList)
            {
                if (ReferenceEquals(parent.GetNodeSlot(i), node))
                {
                    slot = i;

                    return true;
                }

                continue;
            }

            if (parent.GetNodeSlot(i) is not { } listRed)
                continue;

            for (var j = 0; j < childGreen.SlotCount; j++)
            {
                if (ReferenceEquals(listRed.GetNodeSlot(j), node))
                {
                    slot = i;
                    indexInList = j;

                    return true;
                }
            }
        }

        return false;
    }

    private static TRoot Rebuild<TRoot>(TRoot root, SyntaxReplacer replacer, string message, string parameterName)
        where TRoot : SyntaxNode
    {
        var green = replacer.Rebuild(root, out var replacedAnything);
        if (!replacedAnything)
            throw new ArgumentException(message, parameterName);

        return (TRoot)green.CreateRed();
    }
}
