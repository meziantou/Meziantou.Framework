namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Lays a treemap out as Mermaid does with d3-hierarchy 3.1 (a port of its algorithms, see THIRD-PARTY-NOTICES.md): the
/// values summed up the hierarchy, the children sorted by decreasing value, and the squarified tiling with the paddings
/// of Mermaid, rounded to whole pixels.
/// </summary>
internal static class TreemapLayout
{
    public const double SectionHeaderHeight = 25;
    private const double SectionInnerPadding = 10;

    // The golden ratio, the aspect ratio the squarified tiling aims at
    private static readonly double Phi = (1 + Math.Sqrt(5)) / 2;

    /// <summary>Builds the hierarchy and lays it out.</summary>
    /// <returns>The root, whose children are the top-level nodes of the diagram.</returns>
    public static TreemapLayoutNode Compute(TreemapDiagram diagram)
    {
        var root = BuildHierarchy(diagram);
        var nodes = PreOrder(root);

        // node.sum(d => d.value ?? 0), then node.sort((a, b) => b.value - a.value)
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            // d3-hierarchy adds the values of the children from the last one
            var sum = node.Data?.Value ?? 0;
            for (var j = node.Children.Count - 1; j >= 0; j--)
            {
                sum += node.Children[j].Value;
            }

            node.Value = sum;
        }

        foreach (var node in nodes)
        {
            if (node.Children.Count > 1)
            {
                var sorted = node.Children.OrderByDescending(child => child.Value).ToArray();
                node.Children.Clear();
                node.Children.AddRange(sorted);
            }
        }

        root.X0 = 0;
        root.Y0 = 0;
        root.X1 = diagram.NodeWidth * 10;
        root.Y1 = diagram.NodeHeight * 10;
        foreach (var node in PreOrder(root))
        {
            PositionNode(node, diagram.InnerPadding);
        }

        foreach (var node in nodes)
        {
            node.X0 = Round(node.X0);
            node.Y0 = Round(node.Y0);
            node.X1 = Round(node.X1);
            node.Y1 = Round(node.Y1);
        }

        return root;
    }

    /// <summary>Enumerates the nodes breadth first, as the iterator of d3-hierarchy.</summary>
    public static List<TreemapLayoutNode> BreadthFirst(TreemapLayoutNode root)
    {
        var result = new List<TreemapLayoutNode>();
        var queue = new Queue<TreemapLayoutNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            foreach (var child in node.Children)
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>Enumerates the nodes depth first, a parent before its children.</summary>
    public static List<TreemapLayoutNode> PreOrder(TreemapLayoutNode root)
    {
        var result = new List<TreemapLayoutNode>();
        var stack = new Stack<TreemapLayoutNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            result.Add(node);
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }

        return result;
    }

    private static TreemapLayoutNode BuildHierarchy(TreemapDiagram diagram)
    {
        var root = new TreemapLayoutNode(data: null, string.Empty, 0, parent: null);
        var visited = new HashSet<TreemapNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(TreemapLayoutNode Parent, IList<TreemapNode> Children)>();
        stack.Push((root, diagram.Nodes));
        while (stack.Count > 0)
        {
            var (parent, children) = stack.Pop();
            foreach (var child in children)
            {
                // A node that is twice in the diagram is drawn once
                if (!visited.Add(child))
                    continue;

                var node = new TreemapLayoutNode(child, child.Name, parent.Depth + 1, parent);
                parent.Children.Add(node);
                if (child.Children.Count > 0)
                {
                    stack.Push((node, child.Children));
                }
            }
        }

        return root;
    }

    private static void PositionNode(TreemapLayoutNode node, double innerPadding)
    {
        var p = node.Depth is 0 ? 0 : innerPadding / 2;
        var x0 = node.X0 + p;
        var y0 = node.Y0 + p;
        var x1 = node.X1 - p;
        var y1 = node.Y1 - p;
        if (x1 < x0)
        {
            x0 = x1 = (x0 + x1) / 2;
        }

        if (y1 < y0)
        {
            y0 = y1 = (y0 + y1) / 2;
        }

        node.X0 = x0;
        node.Y0 = y0;
        node.X1 = x1;
        node.Y1 = y1;
        if (!node.HasChildren)
            return;

        // The paddings of the sections of Mermaid: a header above the children, and 10 pixels on the other sides
        p = innerPadding / 2;
        x0 += SectionInnerPadding - p;
        y0 += SectionHeaderHeight + SectionInnerPadding - p;
        x1 -= SectionInnerPadding - p;
        y1 -= SectionInnerPadding - p;
        if (x1 < x0)
        {
            x0 = x1 = (x0 + x1) / 2;
        }

        if (y1 < y0)
        {
            y0 = y1 = (y0 + y1) / 2;
        }

        Squarify(node, x0, y0, x1, y1);
    }

    private static void Squarify(TreemapLayoutNode parent, double x0, double y0, double x1, double y1)
    {
        var nodes = parent.Children;
        var value = parent.Value;
        var i0 = 0;
        var i1 = 0;
        var n = nodes.Count;
        while (i0 < n)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;

            // Find the next non-empty node.
            double sumValue;
            do
            {
                sumValue = nodes[i1++].Value;
            }
            while (sumValue == 0 && i1 < n);

            var minValue = sumValue;
            var maxValue = sumValue;
            var alpha = Math.Max(dy / dx, dx / dy) / (value * Phi);
            var beta = sumValue * sumValue * alpha;
            var minRatio = Math.Max(maxValue / beta, beta / minValue);

            // Keep adding nodes while the aspect ratio maintains or improves.
            for (; i1 < n; i1++)
            {
                var nodeValue = nodes[i1].Value;
                sumValue += nodeValue;
                if (nodeValue < minValue)
                {
                    minValue = nodeValue;
                }

                if (nodeValue > maxValue)
                {
                    maxValue = nodeValue;
                }

                beta = sumValue * sumValue * alpha;
                var newRatio = Math.Max(maxValue / beta, beta / minValue);
                if (newRatio > minRatio)
                {
                    sumValue -= nodeValue;
                    break;
                }

                minRatio = newRatio;
            }

            // Position and record the row orientation.
            if (dx < dy)
            {
                var top = y0;
                var bottom = value != 0 ? y0 += dy * sumValue / value : y1;
                Dice(nodes, i0, i1, sumValue, x0, top, x1, bottom);
            }
            else
            {
                var left = x0;
                var right = value != 0 ? x0 += dx * sumValue / value : x1;
                Slice(nodes, i0, i1, sumValue, left, y0, right, y1);
            }

            value -= sumValue;
            i0 = i1;
        }
    }

    // treemapDice: the nodes side by side horizontally
    private static void Dice(List<TreemapLayoutNode> nodes, int start, int end, double value, double x0, double y0, double x1, double y1)
    {
        var k = value != 0 ? (x1 - x0) / value : 0;
        for (var i = start; i < end; i++)
        {
            var node = nodes[i];
            node.Y0 = y0;
            node.Y1 = y1;
            node.X0 = x0;
            node.X1 = x0 += node.Value * k;
        }
    }

    // treemapSlice: the nodes stacked vertically
    private static void Slice(List<TreemapLayoutNode> nodes, int start, int end, double value, double x0, double y0, double x1, double y1)
    {
        var k = value != 0 ? (y1 - y0) / value : 0;
        for (var i = start; i < end; i++)
        {
            var node = nodes[i];
            node.X0 = x0;
            node.X1 = x1;
            node.Y0 = y0;
            node.Y1 = y0 += node.Value * k;
        }
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
