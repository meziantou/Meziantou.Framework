using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The helpers shared by the CSS animations and the CSS transitions.</summary>
internal static class DeclarativeAnimations
{
    /// <summary>
    /// Compares the owning elements of two declarative animations in the composite order: the shadow-including tree order of
    /// the elements, then the element before its pseudo-elements (::marker, ::before, the others, then ::after)
    /// (https://drafts.csswg.org/css-animations-2/#animation-composite-order).
    /// </summary>
    public static int CompareTreeOrder(Element a, string? pseudoA, Element b, string? pseudoB)
    {
        if (ReferenceEquals(a, b))
        {
            // The pseudo-elements of a view transition are in the order of their tree
            // (https://drafts.csswg.org/css-view-transitions-1/#pseudo-element-tree: "the pseudo-element tree defines the document
            // order of its descendant tree-abiding pseudo-elements")
            if (pseudoA is not null && pseudoB is not null && !string.Equals(pseudoA, pseudoB, StringComparison.Ordinal) && a.NodeDocument.GetViewTransitionPseudoTree(a) is { } tree
                && tree.GetOrder(pseudoA) is { } orderA && tree.GetOrder(pseudoB) is { } orderB)
            {
                return orderA.CompareTo(orderB);
            }

            return GetPseudoElementRank(pseudoA).CompareTo(GetPseudoElementRank(pseudoB));
        }

        var pathA = GetShadowIncludingPath(a);
        var pathB = GetShadowIncludingPath(b);
        if (!ReferenceEquals(pathA[0], pathB[0]))
            return 0;

        var length = Math.Min(pathA.Count, pathB.Count);
        for (var i = 1; i < length; i++)
        {
            var nodeA = pathA[i];
            var nodeB = pathB[i];
            if (ReferenceEquals(nodeA, nodeB))
                continue;

            // The shadow root of a host precedes its children
            if (nodeA is ShadowRoot)
                return -1;

            if (nodeB is ShadowRoot)
                return 1;

            return nodeA.IsPreceding(nodeB) ? -1 : 1;
        }

        return pathA.Count.CompareTo(pathB.Count);
    }

    private static List<Node> GetShadowIncludingPath(Node node)
    {
        var path = new List<Node>();
        for (Node? current = node; current is not null; current = current.ParentNode ?? (current as ShadowRoot)?.Host)
        {
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private static int GetPseudoElementRank(string? pseudoElement) => pseudoElement switch
    {
        null => 0,
        "marker" => 1,
        "before" => 2,
        "after" => 4,
        _ => 3,
    };
}
