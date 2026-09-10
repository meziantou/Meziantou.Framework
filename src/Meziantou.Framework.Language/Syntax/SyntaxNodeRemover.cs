using Meziantou.Framework.Language.InternalSyntax;
using GreenList = Meziantou.Framework.Language.InternalSyntax.SyntaxList;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Syntax;

/// <summary>Works out what to carry over from a node that is being removed, and where to put it.</summary>
/// <remarks>
/// Only the trivia at the outer edges of what is removed is considered, which is what the options are named after:
/// the comment in front of a node and whatever followed it. Trivia in the middle belonged to the node's own tokens and
/// goes with them.
/// </remarks>
internal static class SyntaxNodeRemover
{
    /// <summary>Collects the trivia that should outlive <paramref name="removed"/>.</summary>
    /// <param name="removed">The green nodes and tokens being taken out, in source order.</param>
    /// <param name="options">Which of their trivia to keep.</param>
    /// <param name="language">A node of the language, asked which of the trivia end a line.</param>
    public static GreenNode? ResidualTrivia(IReadOnlyList<GreenNode> removed, SyntaxRemoveOptions options, GreenNode language)
    {
        if (options == SyntaxRemoveOptions.KeepNoTrivia)
            return null;

        var kept = new List<GreenNode?>();
        GreenNode? lastEndOfLine = null;

        foreach (var item in removed)
        {
            var leading = (item.GetFirstTerminal() as GreenToken)?.LeadingTrivia;
            var trailing = (item.GetLastTerminal() as GreenToken)?.TrailingTrivia;

            if ((options & SyntaxRemoveOptions.KeepLeadingTrivia) != SyntaxRemoveOptions.KeepNoTrivia)
            {
                AddAll(kept, leading);
            }

            if ((options & SyntaxRemoveOptions.KeepTrailingTrivia) != SyntaxRemoveOptions.KeepNoTrivia)
            {
                AddAll(kept, trailing);
            }

            lastEndOfLine = LastEndOfLine(trailing, language) ?? LastEndOfLine(leading, language) ?? lastEndOfLine;
        }

        if ((options & SyntaxRemoveOptions.KeepEndOfLine) != SyntaxRemoveOptions.KeepNoTrivia
            && lastEndOfLine is not null
            && !kept.Exists(trivia => trivia is not null && language.IsEndOfLineTrivia(trivia)))
        {
            kept.Add(lastEndOfLine);
        }

        return GreenList.List(kept.ToArray());
    }

    /// <summary>Returns a copy of <paramref name="node"/> whose first token carries <paramref name="trivia"/> in front of it.</summary>
    public static GreenNode PrependLeadingTrivia(GreenNode node, GreenNode? trivia)
        => trivia is null ? node : RebuildEdge(node, trivia, leading: true);

    /// <summary>Returns a copy of <paramref name="node"/> whose last token carries <paramref name="trivia"/> after it.</summary>
    public static GreenNode AppendTrailingTrivia(GreenNode node, GreenNode? trivia)
        => trivia is null ? node : RebuildEdge(node, trivia, leading: false);

    /// <summary>
    /// Rebuilds the path down to the first or last token of <paramref name="node"/> with extra trivia on it.
    /// </summary>
    /// <remarks>
    /// The descent is written out rather than recursed so a deeply nested document does not become a deep call stack.
    /// </remarks>
    private static GreenNode RebuildEdge(GreenNode node, GreenNode trivia, bool leading)
    {
        var path = new List<(GreenNode Node, int Slot)>();
        var current = node;

        while (current is not GreenToken)
        {
            var slot = leading ? FirstOccupiedSlot(current) : LastOccupiedSlot(current);
            if (slot < 0)
                return node;

            path.Add((current, slot));
            current = current.GetSlot(slot)!;
        }

        var token = (GreenToken)current;
        GreenNode rebuilt = leading
            ? token.WithTrivia(GreenList.Concat(trivia, token.LeadingTrivia), token.TrailingTrivia)
            : token.WithTrivia(token.LeadingTrivia, GreenList.Concat(token.TrailingTrivia, trivia));

        for (var i = path.Count - 1; i >= 0; i--)
        {
            var (parent, slot) = path[i];
            var slots = new GreenNode?[parent.SlotCount];
            for (var s = 0; s < slots.Length; s++)
            {
                slots[s] = parent.GetSlot(s);
            }

            slots[slot] = rebuilt;
            rebuilt = parent.WithSlots(slots)!;
        }

        return rebuilt;
    }

    private static int FirstOccupiedSlot(GreenNode node)
    {
        for (var i = 0; i < node.SlotCount; i++)
        {
            if (node.GetSlot(i) is not null)
                return i;
        }

        return -1;
    }

    private static int LastOccupiedSlot(GreenNode node)
    {
        for (var i = node.SlotCount - 1; i >= 0; i--)
        {
            if (node.GetSlot(i) is not null)
                return i;
        }

        return -1;
    }

    private static void AddAll(List<GreenNode?> kept, GreenNode? trivia)
    {
        var count = GreenNodeList.Count(trivia);
        for (var i = 0; i < count; i++)
        {
            kept.Add(GreenNodeList.ElementAt(trivia, i));
        }
    }

    private static GreenNode? LastEndOfLine(GreenNode? trivia, GreenNode language)
    {
        GreenNode? found = null;
        var count = GreenNodeList.Count(trivia);
        for (var i = 0; i < count; i++)
        {
            var item = GreenNodeList.ElementAt(trivia, i);
            if (item is not null && language.IsEndOfLineTrivia(item))
            {
                found = item;
            }
        }

        return found;
    }
}
