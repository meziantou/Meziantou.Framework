namespace Meziantou.Framework.Language.Syntax;

/// <summary>Works out what changed between two trees by comparing the immutable nodes behind them.</summary>
/// <remarks>
/// Two subtrees that are the same instance are equal without looking inside, which is what makes this proportional to
/// what changed rather than to the size of the document -- provided the new tree was derived from the old one and so
/// shares its untouched parts.
/// </remarks>
internal static class SyntaxDiffer
{
    public static IReadOnlyList<TextChange> GetChanges(SyntaxNode oldRoot, SyntaxNode newRoot)
    {
        var changes = new List<TextChange>();
        Compare(oldRoot, newRoot, changes);

        return Coalesce(changes);
    }

    private static void Compare(SyntaxNodeOrToken oldItem, SyntaxNodeOrToken newItem, List<TextChange> changes)
    {
        if (ReferenceEquals(oldItem.UnderlyingNode, newItem.UnderlyingNode))
            return;

        var oldChildren = oldItem.ChildNodesAndTokens();
        var newChildren = newItem.ChildNodesAndTokens();

        // Once the shapes differ there is no useful pairing left, so report the whole thing as replaced.
        if (oldItem.IsToken || newItem.IsToken || oldItem.RawKind != newItem.RawKind || oldChildren.Count != newChildren.Count)
        {
            changes.Add(new TextChange(oldItem.FullSpan, newItem.ToFullString()));
            return;
        }

        for (var i = 0; i < oldChildren.Count; i++)
        {
            Compare(oldChildren[i], newChildren[i], changes);
        }
    }

    private static List<TextChange> Coalesce(List<TextChange> changes)
    {
        if (changes.Count < 2)
            return changes;

        var result = new List<TextChange>(changes.Count);
        var current = changes[0];
        for (var i = 1; i < changes.Count; i++)
        {
            var next = changes[i];
            if (current.Span.End == next.Span.Start)
            {
                current = new TextChange(TextSpan.FromBounds(current.Span.Start, next.Span.End), current.NewText + next.NewText);
                continue;
            }

            result.Add(current);
            current = next;
        }

        result.Add(current);

        return result;
    }
}
