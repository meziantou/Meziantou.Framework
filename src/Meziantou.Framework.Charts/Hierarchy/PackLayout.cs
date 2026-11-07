namespace Meziantou.Framework.Charts;

/// <summary>
/// Lays a tree out as nested circles: the area of each leaf is proportional to its value, and the children of a node
/// are packed in its circle, as d3's <c>pack</c>. The result is deterministic.
/// </summary>
public sealed class PackLayout
{
    private static readonly double MaxUnscaledValue = Math.ScaleB(1, 128);

    /// <summary>Gets or sets the width of the layout. The default is 1.</summary>
    public double Width { get; set; } = 1;

    /// <summary>Gets or sets the height of the layout. The default is 1.</summary>
    public double Height { get; set; } = 1;

    /// <summary>Gets or sets the space between sibling circles, and between a parent and its children. The default is 0.</summary>
    public double Padding { get; set; }

    /// <summary>Places the nodes of a tree. The values of the nodes must be computed first (<see cref="HierarchyNode{T}.Sum"/>).</summary>
    /// <typeparam name="T">The type of the data of the nodes.</typeparam>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The circle of every node, in pre-order.</returns>
    public IReadOnlyList<HierarchyCircle<T>> Layout<T>(HierarchyNode<T> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var nodes = root.Descendants().ToList();
        // The layout does not depend on the scale of the values, but the packing squares and multiplies the radii, which
        // would overflow for huge values: scale them by a power of four, so the radii are scaled exactly by a power of two
        var maxValue = nodes.Where(node => node.IsLeaf).Select(node => node.Value).DefaultIfEmpty().Max();
        var exponent = maxValue > MaxUnscaledValue ? -2 * (Math.ILogB(maxValue) / 2) : 0;
        var circles = nodes.ToDictionary(node => node, node => new PackCircle(0, 0, node.IsLeaf ? Math.Sqrt(Math.Max(0, Math.ScaleB(node.Value, exponent))) : 0));
        var random = CirclePacking.CreateRandom();

        // As d3: pack without padding to find the scale of the layout, then pack again with the padding scaled to it
        PackChildren(nodes, circles, padding: 0, random);
        var size = Math.Min(Width, Height);
        var scale = circles[root].Radius / size;
        PackChildren(nodes, circles, Padding * scale, random);

        var k = size / (2 * circles[root].Radius);
        if (!double.IsFinite(k))
        {
            k = 0;
        }

        var rootCircle = circles[root];
        rootCircle.X = Width / 2;
        rootCircle.Y = Height / 2;
        rootCircle.Radius *= k;
        var result = new List<HierarchyCircle<T>>(nodes.Count);
        foreach (var node in nodes)
        {
            var circle = circles[node];
            if (node.Parent is { } parent)
            {
                var parentCircle = circles[parent];
                circle.Radius *= k;
                circle.X = parentCircle.X + (k * circle.X);
                circle.Y = parentCircle.Y + (k * circle.Y);
            }

            result.Add(new HierarchyCircle<T>(node, circle.X, circle.Y, circle.Radius));
        }

        return result;
    }

    private static void PackChildren<T>(List<HierarchyNode<T>> nodes, Dictionary<HierarchyNode<T>, PackCircle> circles, double padding, Func<double> random)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            if (node.IsLeaf)
                continue;

            var children = node.Children.Select(child => circles[child]).ToList();
            if (padding > 0)
            {
                children.ForEach(child => child.Radius += padding);
            }

            var radius = CirclePacking.PackSiblings(children, random);
            if (padding > 0)
            {
                children.ForEach(child => child.Radius -= padding);
            }

            circles[node].Radius = radius + padding;
        }
    }
}
