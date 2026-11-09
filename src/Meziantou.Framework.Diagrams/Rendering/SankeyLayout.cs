namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Lays a sankey diagram out as d3-sankey 0.12 does (a port of its algorithm, see THIRD-PARTY-NOTICES.md): the nodes in
/// columns by their depth and the alignment, stacked in each column with heights proportional to their value, then moved
/// six times toward the links of their neighbors.
/// </summary>
/// <remarks>
/// The links that close a cycle, which d3-sankey rejects, are left out. The tiny and the huge values are divided by the
/// largest node value, which does not change the layout but keeps it finite.
/// </remarks>
internal sealed class SankeyLayout
{
    private const int Iterations = 6;

    private readonly double _x0;
    private readonly double _y0;
    private readonly double _x1;
    private readonly double _y1;
    private readonly double _dx;
    private readonly double _dy;
    private readonly SankeyNodeAlignment _alignment;
    private double _py;

    private SankeyLayout(double width, double height, double nodeWidth, double nodePadding, SankeyNodeAlignment alignment)
    {
        _x0 = 0;
        _y0 = 0;
        _x1 = width;
        _y1 = height;
        _dx = nodeWidth;
        _dy = nodePadding;
        _alignment = alignment;
    }

    public List<SankeyLayoutNode> Nodes { get; } = [];

    /// <summary>Gets the links that are drawn: every link but the ones that close a cycle.</summary>
    public List<SankeyLayoutLink> Links { get; } = [];

    public static SankeyLayout Compute(SankeyDiagram diagram)
    {
        var layout = new SankeyLayout(diagram.Width, diagram.Height, diagram.NodeWidth, diagram.NodePadding + (diagram.ShowValues ? 15 : 0), diagram.NodeAlignment);
        layout.Run(diagram);
        return layout;
    }

    private void Run(SankeyDiagram diagram)
    {
        var nodesByName = new Dictionary<string, SankeyLayoutNode>(StringComparer.Ordinal);
        SankeyLayoutNode GetNode(string name)
        {
            if (!nodesByName.TryGetValue(name, out var node))
            {
                node = new SankeyLayoutNode(name, Nodes.Count);
                nodesByName.Add(name, node);
                Nodes.Add(node);
            }

            return node;
        }

        var links = new List<(SankeyLayoutNode Source, SankeyLayoutNode Target, double Value)>();
        foreach (var link in diagram.Links)
        {
            var source = GetNode(link.Source);
            var target = GetNode(link.Target);
            links.Add((source, target, link.Value));
        }

        var excluded = FindCycleLinks(links, Nodes.Count);

        // The values of the nodes, and the scale that makes the largest one 1
        var outgoing = new double[Nodes.Count];
        var incoming = new double[Nodes.Count];
        for (var i = 0; i < links.Count; i++)
        {
            if (excluded[i])
                continue;

            outgoing[links[i].Source.Index] += links[i].Value;
            incoming[links[i].Target.Index] += links[i].Value;
        }

        var largest = 0.0;
        foreach (var node in Nodes)
        {
            node.DisplayValue = Math.Max(outgoing[node.Index], incoming[node.Index]);
            largest = Math.Max(largest, node.DisplayValue);
        }

        // The values are used as they are, as d3-sankey does, unless they are so small or so large that the heights would not be finite
        var scale = largest is > 0 and (< 1e-200 or > 1e200) ? 1 / largest : 1;
        for (var i = 0; i < links.Count; i++)
        {
            if (excluded[i])
                continue;

            var (source, target, value) = links[i];
            var link = new SankeyLayoutLink(source, target, value * scale, Links.Count);
            Links.Add(link);
            source.SourceLinks.Add(link);
            target.TargetLinks.Add(link);
        }

        foreach (var node in Nodes)
        {
            node.Value = Math.Max(Sum(node.SourceLinks), Sum(node.TargetLinks));
        }

        ComputeNodeDepths();
        ComputeNodeHeights();
        ComputeNodeBreadths();
        ComputeLinkBreadths();
    }

    /// <summary>Finds the links that close a cycle, with a depth-first search from the nodes in their order.</summary>
    private static bool[] FindCycleLinks(List<(SankeyLayoutNode Source, SankeyLayoutNode Target, double Value)> links, int nodeCount)
    {
        var excluded = new bool[links.Count];
        var outgoing = new List<int>[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            outgoing[i] = [];
        }

        for (var i = 0; i < links.Count; i++)
        {
            outgoing[links[i].Source.Index].Add(i);
        }

        // 0: not visited, 1: on the path being explored, 2: done
        var state = new byte[nodeCount];
        var stack = new Stack<(int Node, int Next)>();
        for (var start = 0; start < nodeCount; start++)
        {
            if (state[start] != 0)
                continue;

            state[start] = 1;
            stack.Push((start, 0));
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                if (next >= outgoing[node].Count)
                {
                    state[node] = 2;
                    continue;
                }

                stack.Push((node, next + 1));
                var linkIndex = outgoing[node][next];
                var target = links[linkIndex].Target.Index;
                if (state[target] == 1)
                {
                    excluded[linkIndex] = true;
                }
                else if (state[target] == 0)
                {
                    state[target] = 1;
                    stack.Push((target, 0));
                }
            }
        }

        return excluded;
    }

    private static double Sum(List<SankeyLayoutLink> links)
    {
        var sum = 0.0;
        foreach (var link in links)
        {
            sum += link.Value;
        }

        return sum;
    }

    private void ComputeNodeDepths()
    {
        var current = new List<SankeyLayoutNode>(Nodes);
        var depth = 0;
        while (current.Count > 0 && depth <= Nodes.Count)
        {
            var next = new List<SankeyLayoutNode>();
            var seen = new HashSet<SankeyLayoutNode>();
            foreach (var node in current)
            {
                node.Depth = depth;
                foreach (var link in node.SourceLinks)
                {
                    if (seen.Add(link.Target))
                    {
                        next.Add(link.Target);
                    }
                }
            }

            depth++;
            current = next;
        }
    }

    private void ComputeNodeHeights()
    {
        var current = new List<SankeyLayoutNode>(Nodes);
        var height = 0;
        while (current.Count > 0 && height <= Nodes.Count)
        {
            var next = new List<SankeyLayoutNode>();
            var seen = new HashSet<SankeyLayoutNode>();
            foreach (var node in current)
            {
                node.Height = height;
                foreach (var link in node.TargetLinks)
                {
                    if (seen.Add(link.Source))
                    {
                        next.Add(link.Source);
                    }
                }
            }

            height++;
            current = next;
        }
    }

    private int Align(SankeyLayoutNode node, int columns) => _alignment switch
    {
        SankeyNodeAlignment.Left => node.Depth,
        SankeyNodeAlignment.Right => columns - 1 - node.Height,
        SankeyNodeAlignment.Center => node.TargetLinks.Count > 0 ? node.Depth : node.SourceLinks.Count > 0 ? node.SourceLinks.Min(link => link.Target.Depth) - 1 : 0,
        _ => node.SourceLinks.Count > 0 ? node.Depth : columns - 1,
    };

    private List<SankeyLayoutNode>[] ComputeNodeLayers()
    {
        var count = Nodes.Count is 0 ? 0 : Nodes.Max(node => node.Depth) + 1;
        var kx = count > 1 ? (_x1 - _x0 - _dx) / (count - 1) : 0;
        var columns = new List<SankeyLayoutNode>[count];
        for (var i = 0; i < count; i++)
        {
            columns[i] = [];
        }

        foreach (var node in Nodes)
        {
            var layer = Math.Max(0, Math.Min(count - 1, Align(node, count)));
            node.Layer = layer;
            node.X0 = _x0 + (layer * kx);
            node.X1 = node.X0 + _dx;
            columns[layer].Add(node);
        }

        return columns;
    }

    private void ComputeNodeBreadths()
    {
        var columns = ComputeNodeLayers();
        var largestColumn = columns.Length is 0 ? 0 : columns.Max(column => column.Count);
        _py = Math.Min(_dy, largestColumn > 1 ? (_y1 - _y0) / (largestColumn - 1) : double.PositiveInfinity);
        InitializeNodeBreadths(columns);
        for (var i = 0; i < Iterations; i++)
        {
            var alpha = Math.Pow(0.99, i);
            var beta = Math.Max(1 - alpha, (i + 1) / (double)Iterations);
            RelaxRightToLeft(columns, alpha, beta);
            RelaxLeftToRight(columns, alpha, beta);
        }
    }

    private void InitializeNodeBreadths(List<SankeyLayoutNode>[] columns)
    {
        var ky = double.PositiveInfinity;
        foreach (var column in columns)
        {
            var sum = 0.0;
            foreach (var node in column)
            {
                sum += node.Value;
            }

            var value = (_y1 - _y0 - ((column.Count - 1) * _py)) / sum;
            if (!double.IsNaN(value))
            {
                ky = Math.Min(ky, value);
            }
        }

        // d3-sankey gives infinite or negative heights when every value is 0 or the nodes do not fit
        if (!double.IsFinite(ky) || ky < 0)
        {
            ky = 0;
        }

        foreach (var column in columns)
        {
            var y = _y0;
            foreach (var node in column)
            {
                node.Y0 = y;
                node.Y1 = y + (node.Value * ky);
                y = node.Y1 + _py;
                foreach (var link in node.SourceLinks)
                {
                    link.Width = link.Value * ky;
                }
            }

            y = (_y1 - y + _py) / (column.Count + 1);
            for (var i = 0; i < column.Count; i++)
            {
                column[i].Y0 += y * (i + 1);
                column[i].Y1 += y * (i + 1);
            }

            ReorderLinks(column);
        }
    }

    // Reposition each node based on its incoming (target) links.
    private void RelaxLeftToRight(List<SankeyLayoutNode>[] columns, double alpha, double beta)
    {
        for (var i = 1; i < columns.Length; i++)
        {
            var column = columns[i];
            foreach (var target in column)
            {
                var y = 0.0;
                var w = 0.0;
                foreach (var link in target.TargetLinks)
                {
                    var v = link.Value * (target.Layer - link.Source.Layer);
                    y += TargetTop(link.Source, target) * v;
                    w += v;
                }

                if (!(w > 0))
                    continue;

                var dy = ((y / w) - target.Y0) * alpha;
                target.Y0 += dy;
                target.Y1 += dy;
                ReorderNodeLinks(target);
            }

            SortByBreadth(column);
            ResolveCollisions(column, beta);
        }
    }

    // Reposition each node based on its outgoing (source) links.
    private void RelaxRightToLeft(List<SankeyLayoutNode>[] columns, double alpha, double beta)
    {
        for (var i = columns.Length - 2; i >= 0; i--)
        {
            var column = columns[i];
            foreach (var source in column)
            {
                var y = 0.0;
                var w = 0.0;
                foreach (var link in source.SourceLinks)
                {
                    var v = link.Value * (link.Target.Layer - source.Layer);
                    y += SourceTop(source, link.Target) * v;
                    w += v;
                }

                if (!(w > 0))
                    continue;

                var dy = ((y / w) - source.Y0) * alpha;
                source.Y0 += dy;
                source.Y1 += dy;
                ReorderNodeLinks(source);
            }

            SortByBreadth(column);
            ResolveCollisions(column, beta);
        }
    }

    /// <summary>Sorts a column by the top of its nodes; the sort is stable, as the sort of JavaScript.</summary>
    private static void SortByBreadth(List<SankeyLayoutNode> column)
    {
        var sorted = column.OrderBy(node => node.Y0).ToArray();
        column.Clear();
        column.AddRange(sorted);
    }

    private void ResolveCollisions(List<SankeyLayoutNode> nodes, double alpha)
    {
        if (nodes.Count is 0)
            return;

        var i = nodes.Count >> 1;
        var subject = nodes[i];
        ResolveCollisionsBottomToTop(nodes, subject.Y0 - _py, i - 1, alpha);
        ResolveCollisionsTopToBottom(nodes, subject.Y1 + _py, i + 1, alpha);
        ResolveCollisionsBottomToTop(nodes, _y1, nodes.Count - 1, alpha);
        ResolveCollisionsTopToBottom(nodes, _y0, 0, alpha);
    }

    // Push any overlapping nodes down.
    private void ResolveCollisionsTopToBottom(List<SankeyLayoutNode> nodes, double y, int i, double alpha)
    {
        for (; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var dy = (y - node.Y0) * alpha;
            if (dy > 1e-6)
            {
                node.Y0 += dy;
                node.Y1 += dy;
            }

            y = node.Y1 + _py;
        }
    }

    // Push any overlapping nodes up.
    private void ResolveCollisionsBottomToTop(List<SankeyLayoutNode> nodes, double y, int i, double alpha)
    {
        for (; i >= 0; i--)
        {
            var node = nodes[i];
            var dy = (node.Y1 - y) * alpha;
            if (dy > 1e-6)
            {
                node.Y0 -= dy;
                node.Y1 -= dy;
            }

            y = node.Y0 - _py;
        }
    }

    private static void ReorderNodeLinks(SankeyLayoutNode node)
    {
        foreach (var link in node.TargetLinks)
        {
            Sort(link.Source.SourceLinks, CompareTargetBreadth);
        }

        foreach (var link in node.SourceLinks)
        {
            Sort(link.Target.TargetLinks, CompareSourceBreadth);
        }
    }

    private static void ReorderLinks(List<SankeyLayoutNode> nodes)
    {
        foreach (var node in nodes)
        {
            Sort(node.SourceLinks, CompareTargetBreadth);
            Sort(node.TargetLinks, CompareSourceBreadth);
        }
    }

    /// <summary>
    /// Sorts links by insertion: the order is total, so the result is the one of any sort, and the lists are sorted again
    /// after a single node moved, so they are almost sorted and this is linear, where a node with many links is quadratic
    /// with a general sort.
    /// </summary>
    private static void Sort(List<SankeyLayoutLink> links, Comparison<SankeyLayoutLink> comparison)
    {
        for (var i = 1; i < links.Count; i++)
        {
            var link = links[i];
            var j = i - 1;
            while (j >= 0 && comparison(links[j], link) > 0)
            {
                links[j + 1] = links[j];
                j--;
            }

            links[j + 1] = link;
        }
    }

    private static int CompareSourceBreadth(SankeyLayoutLink a, SankeyLayoutLink b)
    {
        var result = a.Source.Y0.CompareTo(b.Source.Y0);
        return result != 0 ? result : a.Index.CompareTo(b.Index);
    }

    private static int CompareTargetBreadth(SankeyLayoutLink a, SankeyLayoutLink b)
    {
        var result = a.Target.Y0.CompareTo(b.Target.Y0);
        return result != 0 ? result : a.Index.CompareTo(b.Index);
    }

    // Returns the target.y0 that would produce an ideal link from source to target.
    private double TargetTop(SankeyLayoutNode source, SankeyLayoutNode target)
    {
        var y = source.Y0 - ((source.SourceLinks.Count - 1) * _py / 2);
        foreach (var link in source.SourceLinks)
        {
            if (link.Target == target)
                break;

            y += link.Width + _py;
        }

        foreach (var link in target.TargetLinks)
        {
            if (link.Source == source)
                break;

            y -= link.Width;
        }

        return y;
    }

    // Returns the source.y0 that would produce an ideal link from source to target.
    private double SourceTop(SankeyLayoutNode source, SankeyLayoutNode target)
    {
        var y = target.Y0 - ((target.TargetLinks.Count - 1) * _py / 2);
        foreach (var link in target.TargetLinks)
        {
            if (link.Source == source)
                break;

            y += link.Width + _py;
        }

        foreach (var link in source.SourceLinks)
        {
            if (link.Target == target)
                break;

            y -= link.Width;
        }

        return y;
    }

    private void ComputeLinkBreadths()
    {
        foreach (var node in Nodes)
        {
            var y0 = node.Y0;
            var y1 = y0;
            foreach (var link in node.SourceLinks)
            {
                link.Y0 = y0 + (link.Width / 2);
                y0 += link.Width;
            }

            foreach (var link in node.TargetLinks)
            {
                link.Y1 = y1 + (link.Width / 2);
                y1 += link.Width;
            }
        }
    }
}
