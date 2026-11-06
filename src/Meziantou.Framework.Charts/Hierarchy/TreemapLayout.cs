using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>Lays a tree out as nested rectangles whose areas are proportional to the values of the nodes, as d3's <c>treemap</c>.</summary>
public sealed class TreemapLayout
{
    private static readonly double GoldenRatio = (1 + Math.Sqrt(5)) / 2;

    /// <summary>Gets or sets the width of the layout. The default is 1.</summary>
    public double Width { get; set; } = 1;

    /// <summary>Gets or sets the height of the layout. The default is 1.</summary>
    public double Height { get; set; } = 1;

    /// <summary>Gets or sets how the rectangle of a node is divided among its children. The default is <see cref="TreemapTiling.Squarified"/>.</summary>
    public TreemapTiling Tiling { get; set; }

    /// <summary>Gets or sets the target aspect ratio of <see cref="TreemapTiling.Squarified"/>. The default is the golden ratio.</summary>
    public double Ratio { get; set; } = GoldenRatio;

    /// <summary>Gets or sets the space between siblings. The default is 0.</summary>
    public double PaddingInner { get; set; }

    /// <summary>Gets or sets the space between the edges of a node and its children. The default is 0.</summary>
    public double PaddingOuter { get; set; }

    /// <summary>Gets or sets the space between the top edge of a node and its children, to make room for a label, or <see langword="null"/> for <see cref="PaddingOuter"/>.</summary>
    public double? PaddingTop { get; set; }

    /// <summary>Places the nodes of a tree. The values of the nodes must be computed first (<see cref="HierarchyNode{T}.Sum"/>).</summary>
    /// <typeparam name="T">The type of the data of the nodes.</typeparam>
    /// <param name="root">The root of the tree.</param>
    /// <returns>The rectangle of every node, in pre-order.</returns>
    public IReadOnlyList<HierarchyRectangle<T>> Layout<T>(HierarchyNode<T> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var boxes = new Dictionary<HierarchyNode<T>, Box> { [root] = new Box(0, 0, Width, Height) };
        var paddingStack = new Dictionary<int, double> { [0] = 0 };
        var result = new List<HierarchyRectangle<T>>();
        foreach (var node in root.Descendants())
        {
            var box = boxes[node];
            var p = paddingStack.GetValueOrDefault(node.Depth);
            box = Shrink(box, p, p, p, p);
            boxes[node] = box;
            result.Add(new HierarchyRectangle<T>(node, box.X0, box.Y0, box.X1, box.Y1));
            if (node.IsLeaf)
                continue;

            p = PaddingInner / 2;
            paddingStack[node.Depth + 1] = p;
            var inner = Shrink(box, (PaddingTop ?? PaddingOuter) - p, PaddingOuter - p, PaddingOuter - p, PaddingOuter - p);
            foreach (var (child, childBox) in Tile(node, inner))
            {
                boxes[child] = childBox;
            }
        }

        return result;
    }

    internal static Box Shrink(Box box, double top, double right, double bottom, double left)
    {
        var x0 = box.X0 + left;
        var y0 = box.Y0 + top;
        var x1 = box.X1 - right;
        var y1 = box.Y1 - bottom;
        if (x1 < x0)
        {
            x0 = x1 = (x0 + x1) / 2;
        }

        if (y1 < y0)
        {
            y0 = y1 = (y0 + y1) / 2;
        }

        return new Box(x0, y0, x1, y1);
    }

    private List<(HierarchyNode<T> Node, Box Box)> Tile<T>(HierarchyNode<T> node, Box box)
    {
        var children = node.Children;
        var values = children.Select(child => child.Value).ToArray();
        var boxes = Tiling switch
        {
            TreemapTiling.Slice => Slice(values, node.Value, box),
            TreemapTiling.Dice => Dice(values, node.Value, box),
            TreemapTiling.SliceDice => (node.Depth & 1) is 1 ? Slice(values, node.Value, box) : Dice(values, node.Value, box),
            TreemapTiling.Binary => Binary(values, node.Value, box),
            _ => Squarify(values, node.Value, box, Ratio),
        };

        return [.. children.Zip(boxes)];
    }

    internal static Box[] Dice(IReadOnlyList<double> values, double total, Box box)
    {
        var result = new Box[values.Count];
        var k = total > 0 ? (box.X1 - box.X0) / total : 0;
        var x = box.X0;
        for (var i = 0; i < values.Count; i++)
        {
            var next = x + (values[i] * k);
            result[i] = new Box(x, box.Y0, next, box.Y1);
            x = next;
        }

        return result;
    }

    internal static Box[] Slice(IReadOnlyList<double> values, double total, Box box)
    {
        var result = new Box[values.Count];
        var k = total > 0 ? (box.Y1 - box.Y0) / total : 0;
        var y = box.Y0;
        for (var i = 0; i < values.Count; i++)
        {
            var next = y + (values[i] * k);
            result[i] = new Box(box.X0, y, box.X1, next);
            y = next;
        }

        return result;
    }

    internal static Box[] Squarify(IReadOnlyList<double> values, double total, Box box, double ratio)
    {
        var result = new Box[values.Count];
        var n = values.Count;
        var i0 = 0;
        var i1 = 0;
        var value = total;
        var (x0, y0, x1, y1) = (box.X0, box.Y0, box.X1, box.Y1);
        while (i0 < n)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;

            // The next node with a value
            double sumValue;
            do
            {
                sumValue = values[i1++];
            }
            while (sumValue == 0 && i1 < n);

            var minValue = sumValue;
            var maxValue = sumValue;
            var alpha = Math.Max(dy / dx, dx / dy) / (value * ratio);
            var beta = sumValue * sumValue * alpha;
            var minRatio = Math.Max(maxValue / beta, beta / minValue);

            // Add nodes to the row while its worst aspect ratio does not get worse
            for (; i1 < n; i1++)
            {
                var nodeValue = values[i1];
                sumValue += nodeValue;
                minValue = Math.Min(minValue, nodeValue);
                maxValue = Math.Max(maxValue, nodeValue);
                beta = sumValue * sumValue * alpha;
                var newRatio = Math.Max(maxValue / beta, beta / minValue);
                if (newRatio > minRatio)
                {
                    sumValue -= nodeValue;
                    break;
                }

                minRatio = newRatio;
            }

            var rowValues = values.Skip(i0).Take(i1 - i0).ToArray();
            Box[] row;
            if (dx < dy)
            {
                var yEnd = value > 0 ? y0 + (dy * sumValue / value) : y1;
                row = Dice(rowValues, sumValue, new Box(x0, y0, x1, yEnd));
                y0 = yEnd;
            }
            else
            {
                var xEnd = value > 0 ? x0 + (dx * sumValue / value) : x1;
                row = Slice(rowValues, sumValue, new Box(x0, y0, xEnd, y1));
                x0 = xEnd;
            }

            row.CopyTo(result, i0);
            value -= sumValue;
            i0 = i1;
        }

        return result;
    }

    internal static Box[] Binary(IReadOnlyList<double> values, double total, Box box)
    {
        var result = new Box[values.Count];
        if (values.Count is 0)
            return result;

        var sums = new double[values.Count + 1];
        for (var i = 0; i < values.Count; i++)
        {
            sums[i + 1] = sums[i] + values[i];
        }

        Partition(0, values.Count, total, box.X0, box.Y0, box.X1, box.Y1);
        return result;

        void Partition(int i, int j, double value, double x0, double y0, double x1, double y1)
        {
            if (i >= j - 1)
            {
                result[i] = new Box(x0, y0, x1, y1);
                return;
            }

            var valueOffset = sums[i];
            var valueTarget = (value / 2) + valueOffset;
            var k = i + 1;
            var hi = j - 1;
            while (k < hi)
            {
                var middle = (k + hi) >>> 1;
                if (sums[middle] < valueTarget)
                {
                    k = middle + 1;
                }
                else
                {
                    hi = middle;
                }
            }

            if (valueTarget - sums[k - 1] < sums[k] - valueTarget && i + 1 < k)
            {
                k--;
            }

            var valueLeft = sums[k] - valueOffset;
            var valueRight = value - valueLeft;
            if (x1 - x0 > y1 - y0)
            {
                var xk = value > 0 ? ((x0 * valueRight) + (x1 * valueLeft)) / value : x1;
                Partition(i, k, valueLeft, x0, y0, xk, y1);
                Partition(k, j, valueRight, xk, y0, x1, y1);
            }
            else
            {
                var yk = value > 0 ? ((y0 * valueRight) + (y1 * valueLeft)) / value : y1;
                Partition(i, k, valueLeft, x0, y0, x1, yk);
                Partition(k, j, valueRight, x0, yk, x1, y1);
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Box(double X0, double Y0, double X1, double Y1);
}
