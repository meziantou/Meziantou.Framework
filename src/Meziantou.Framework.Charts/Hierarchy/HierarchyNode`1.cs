namespace Meziantou.Framework.Charts;

/// <summary>A node of a tree, for the hierarchical charts (<see cref="PackMark{T}"/>, <see cref="TreemapMark{T}"/>, <see cref="PartitionMark{T}"/>). Create a tree with <see cref="Hierarchy"/>.</summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
public sealed class HierarchyNode<T>
{
    private readonly List<HierarchyNode<T>> _children = [];

    internal HierarchyNode(T data, string name, HierarchyNode<T>? parent)
    {
        Data = data;
        Name = name;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        parent?._children.Add(this);
    }

    /// <summary>Gets the data of the node.</summary>
    public T Data { get; }

    /// <summary>Gets the name of the node, the default label of the charts.</summary>
    public string Name { get; }

    /// <summary>Gets the parent of the node, or <see langword="null"/> for the root.</summary>
    public HierarchyNode<T>? Parent { get; }

    /// <summary>Gets the children of the node.</summary>
    public IReadOnlyList<HierarchyNode<T>> Children => _children;

    /// <summary>Gets the value of the node: its own value plus the values of its descendants, once <see cref="Sum"/> or <see cref="Count"/> is called.</summary>
    public double Value { get; private set; }

    /// <summary>Gets the number of ancestors of the node: 0 for the root.</summary>
    public int Depth { get; }

    /// <summary>Gets the length of the longest path from the node to a leaf: 0 for a leaf.</summary>
    public int Height => _children.Count is 0 ? 0 : _children.Max(child => child.Height) + 1;

    /// <summary>Gets a value that indicates whether the node has no children.</summary>
    public bool IsLeaf => _children.Count is 0;

    /// <summary>Gets the names of the ancestors of the node and of the node, from the root, joined by <c>/</c>.</summary>
    public string Path => string.Join('/', Ancestors().Reverse().Select(node => node.Name));

    /// <summary>Returns the node and its descendants, in pre-order: a node comes before its children.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<HierarchyNode<T>> Descendants()
    {
        var stack = new Stack<HierarchyNode<T>>();
        stack.Push(this);
        while (stack.TryPop(out var node))
        {
            yield return node;
            for (var i = node._children.Count - 1; i >= 0; i--)
            {
                stack.Push(node._children[i]);
            }
        }
    }

    /// <summary>Returns the leaves under the node, in pre-order. The leaf of a leaf is itself.</summary>
    /// <returns>The leaves.</returns>
    public IEnumerable<HierarchyNode<T>> Leaves() => Descendants().Where(node => node.IsLeaf);

    /// <summary>Returns the node and its ancestors, from the node to the root.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<HierarchyNode<T>> Ancestors()
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }

    /// <summary>Sets the value of every node under this node to its own value plus the values of its descendants.</summary>
    /// <param name="value">The own value of a node. NaN, infinite and negative values count as zero, and a sum larger than <see cref="double.MaxValue"/> is <see cref="double.MaxValue"/>.</param>
    /// <returns>The node, to chain calls.</returns>
    public HierarchyNode<T> Sum(Func<T, double> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SumNodes(node => value(node.Data));
    }

    internal HierarchyNode<T> SumNodes(Func<HierarchyNode<T>, double> value)
    {
        foreach (var node in Descendants().Reverse())
        {
            var total = GetOwnValue(value(node));
            foreach (var child in node._children)
            {
                total += child.Value;
            }

            // The terms are finite and positive, so the sum is only infinite when it overflows
            node.Value = Math.Min(total, double.MaxValue);
        }

        return this;
    }

    internal static double GetOwnValue(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    /// <summary>Sets the value of every node under this node to its number of leaves (1 for a leaf).</summary>
    /// <returns>The node, to chain calls.</returns>
    public HierarchyNode<T> Count()
    {
        foreach (var node in Descendants().Reverse())
        {
            node.Value = node.IsLeaf ? 1 : node._children.Sum(child => child.Value);
        }

        return this;
    }

    /// <summary>Sorts the children of every node under this node.</summary>
    /// <param name="comparison">The comparison of two siblings.</param>
    /// <returns>The node, to chain calls.</returns>
    public HierarchyNode<T> Sort(Comparison<HierarchyNode<T>> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        foreach (var node in Descendants())
        {
            // A stable sort, so equal siblings keep their order
            var sorted = node._children.Order(Comparer<HierarchyNode<T>>.Create(comparison)).ToList();
            node._children.Clear();
            node._children.AddRange(sorted);
        }

        return this;
    }

    /// <summary>Sorts the children of every node under this node by decreasing value, the usual order of treemaps and packed circles.</summary>
    /// <returns>The node, to chain calls.</returns>
    public HierarchyNode<T> SortByValue() => Sort((a, b) => b.Value.CompareTo(a.Value));

    /// <inheritdoc/>
    public override string ToString() => Path;
}
