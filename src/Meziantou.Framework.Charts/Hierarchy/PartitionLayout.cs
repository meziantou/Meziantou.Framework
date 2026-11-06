namespace Meziantou.Framework.Charts;

/// <summary>
/// Lays a tree out as adjacent rectangles, one layer per depth, as d3's <c>partition</c>: an icicle chart, or a
/// sunburst once mapped to polar coordinates. The width of a node is proportional to its value.
/// </summary>
public sealed class PartitionLayout
{
    /// <summary>Gets or sets the size of the layout along the values (the width of an icicle). The default is 1.</summary>
    public double Width { get; set; } = 1;

    /// <summary>Gets or sets the size of the layout along the depth (the height of an icicle). The default is 1.</summary>
    public double Height { get; set; } = 1;

    /// <summary>Gets or sets the space removed from the right and bottom edges of each rectangle, to separate them. The default is 0.</summary>
    public double Padding { get; set; }

    /// <summary>Places the nodes of a tree. The values of the nodes must be computed first (<see cref="HierarchyNode{T}.Sum"/>).</summary>
    /// <typeparam name="T">The type of the data of the nodes.</typeparam>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The rectangle of every node, in pre-order. The root takes the first layer.</returns>
    public IReadOnlyList<HierarchyRectangle<T>> Layout<T>(HierarchyNode<T> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var layers = root.Height + 1;
        var boxes = new Dictionary<HierarchyNode<T>, TreemapLayout.Box> { [root] = new TreemapLayout.Box(0, 0, Width, Height / layers) };
        var result = new List<HierarchyRectangle<T>>();
        foreach (var node in root.Descendants())
        {
            var box = boxes[node];
            if (!node.IsLeaf)
            {
                var children = TreemapLayout.Dice([.. node.Children.Select(child => child.Value)], node.Value, new TreemapLayout.Box(box.X0, Height * (node.Depth + 1) / layers, box.X1, Height * (node.Depth + 2) / layers));
                for (var i = 0; i < children.Length; i++)
                {
                    boxes[node.Children[i]] = children[i];
                }
            }

            var shrunk = TreemapLayout.Shrink(box, 0, Padding, Padding, 0);
            result.Add(new HierarchyRectangle<T>(node, shrunk.X0, shrunk.Y0, shrunk.X1, shrunk.Y1));
        }

        return result;
    }
}
