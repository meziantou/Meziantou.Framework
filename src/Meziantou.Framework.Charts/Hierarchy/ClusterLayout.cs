namespace Meziantou.Framework.Charts;

/// <summary>
/// Lays a tree out as a dendrogram, as d3's <c>cluster</c>: the leaves are evenly spaced at the deepest level, and each
/// parent is centered over its children. Siblings are 1 unit apart, cousins 2.
/// </summary>
public sealed class ClusterLayout
{
    /// <summary>Gets or sets the size of the layout across the depths. The default is 1.</summary>
    public double Width { get; set; } = 1;

    /// <summary>Gets or sets the size of the layout along the depths. The default is 1.</summary>
    public double Height { get; set; } = 1;

    /// <summary>Places the nodes of a tree.</summary>
    /// <typeparam name="T">The type of the data of the nodes.</typeparam>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The point of every node, in pre-order.</returns>
    public IReadOnlyList<HierarchyPoint<T>> Layout<T>(HierarchyNode<T> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var nodes = root.Descendants().ToList();
        var x = new Dictionary<HierarchyNode<T>, double>(nodes.Count);
        var y = new Dictionary<HierarchyNode<T>, double>(nodes.Count);
        HierarchyNode<T>? previous = null;
        var position = 0.0;
        foreach (var node in PostOrder(root))
        {
            if (!node.IsLeaf)
            {
                x[node] = node.Children.Average(child => x[child]);
                y[node] = 1 + node.Children.Max(child => y[child]);
            }
            else
            {
                if (previous is not null)
                {
                    position += TreeLayout.Separation(node, previous);
                }

                x[node] = position;
                y[node] = 0;
                previous = node;
            }
        }

        var leftLeaf = root;
        while (!leftLeaf.IsLeaf)
        {
            leftLeaf = leftLeaf.Children[0];
        }

        var rightLeaf = root;
        while (!rightLeaf.IsLeaf)
        {
            rightLeaf = rightLeaf.Children[^1];
        }

        var x0 = x[leftLeaf] - (TreeLayout.Separation(leftLeaf, rightLeaf) / 2);
        var x1 = x[rightLeaf] + (TreeLayout.Separation(rightLeaf, leftLeaf) / 2);
        var rootY = y[root];
        return [.. nodes.Select(node => new HierarchyPoint<T>(node, (x[node] - x0) / (x1 - x0) * Width, (1 - (rootY > 0 ? y[node] / rootY : 1)) * Height))];
    }

    private static IEnumerable<HierarchyNode<T>> PostOrder<T>(HierarchyNode<T> root)
    {
        var stack = new Stack<(HierarchyNode<T> Node, bool Visited)>();
        stack.Push((root, false));
        while (stack.TryPop(out var item))
        {
            if (item.Visited || item.Node.IsLeaf)
            {
                yield return item.Node;
                continue;
            }

            stack.Push((item.Node, true));
            for (var i = item.Node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((item.Node.Children[i], false));
            }
        }
    }
}
