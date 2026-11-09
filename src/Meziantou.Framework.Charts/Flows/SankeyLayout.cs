namespace Meziantou.Framework.Charts;

/// <summary>
/// The Sankey layout of d3-sankey (ISC license): nodes in columns by depth, stacked by value, then moved to align with
/// the nodes they link, with the iterative relaxation of d3.
/// </summary>
internal sealed class SankeyLayout
{
    private readonly double _x0;
    private readonly double _y0;
    private readonly double _x1;
    private readonly double _y1;
    private readonly double _nodeWidth;
    private readonly int _iterations;
    private readonly SankeyAlignment _alignment;
    private double _padding;

    private SankeyLayout(double x0, double y0, double x1, double y1, double nodeWidth, double nodePadding, int iterations, SankeyAlignment alignment)
    {
        _x0 = x0;
        _y0 = y0;
        _x1 = x1;
        _y1 = y1;
        _nodeWidth = nodeWidth;
        _padding = nodePadding;
        _iterations = iterations;
        _alignment = alignment;
    }

    public List<SankeyNode> Nodes { get; } = [];

    public List<SankeyLink> Links { get; } = [];

    /// <exception cref="InvalidOperationException">The links have a cycle.</exception>
    public static SankeyLayout Compute(IReadOnlyList<(string Source, string Target, double Value)> links, ChartRectangle bounds, double nodeWidth, double nodePadding, int iterations, SankeyAlignment alignment)
    {
        var layout = new SankeyLayout(bounds.X, bounds.Y, bounds.Right, bounds.Bottom, nodeWidth, nodePadding, iterations, alignment);
        var nodes = new Dictionary<string, SankeyNode>(StringComparer.Ordinal);

        // Missing, infinite and negative values count as zero. The layout works on the values scaled by a power of two,
        // which keeps their ratios exactly, so that the sums of huge values cannot overflow.
        var maxValue = links.Select(link => GetValue(link.Value)).DefaultIfEmpty().Max();
        var exponent = maxValue > 0 ? -Math.ILogB(maxValue) : 0;
        for (var i = 0; i < links.Count; i++)
        {
            var (source, target, value) = links[i];
            value = GetValue(value);
            var link = new SankeyLink(layout.GetNode(nodes, source), layout.GetNode(nodes, target), value, Math.ScaleB(value, exponent), i);
            link.Source.SourceLinks.Add(link);
            link.Target.TargetLinks.Add(link);
            layout.Links.Add(link);
        }

        foreach (var node in layout.Nodes)
        {
            node.Value = Math.Max(node.SourceLinks.Sum(link => link.Value), node.TargetLinks.Sum(link => link.Value));
            node.Weight = Math.Max(node.SourceLinks.Sum(link => link.Weight), node.TargetLinks.Sum(link => link.Weight));
        }

        layout.ComputeDepths();
        layout.ComputeHeights();
        var columns = layout.ComputeLayers();
        layout.ComputeBreadths(columns);
        ComputeLinkBreadths(layout.Nodes);
        return layout;
    }

    private static double GetValue(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private SankeyNode GetNode(Dictionary<string, SankeyNode> nodes, string name)
    {
        if (!nodes.TryGetValue(name, out var node))
        {
            node = new SankeyNode(name, Nodes.Count);
            nodes.Add(name, node);
            Nodes.Add(node);
        }

        return node;
    }

    private void ComputeDepths()
    {
        var current = new HashSet<SankeyNode>(Nodes);
        var depth = 0;
        while (current.Count > 0)
        {
            var next = new HashSet<SankeyNode>();
            foreach (var node in current)
            {
                node.Depth = depth;
                foreach (var link in node.SourceLinks)
                {
                    next.Add(link.Target);
                }
            }

            if (++depth > Nodes.Count)
                throw new InvalidOperationException("The links of the Sankey diagram have a cycle.");

            current = next;
        }
    }

    private void ComputeHeights()
    {
        var current = new HashSet<SankeyNode>(Nodes);
        var height = 0;
        while (current.Count > 0)
        {
            var next = new HashSet<SankeyNode>();
            foreach (var node in current)
            {
                node.Height = height;
                foreach (var link in node.TargetLinks)
                {
                    next.Add(link.Source);
                }
            }

            ++height;
            current = next;
        }
    }

    private List<List<SankeyNode>> ComputeLayers()
    {
        var count = Nodes.Count is 0 ? 1 : Nodes.Max(node => node.Depth) + 1;
        var step = count > 1 ? (_x1 - _x0 - _nodeWidth) / (count - 1) : 0;
        var columns = new List<List<SankeyNode>>();
        for (var i = 0; i < count; i++)
        {
            columns.Add([]);
        }

        foreach (var node in Nodes)
        {
            var layer = Math.Max(0, Math.Min(count - 1, (int)Math.Floor(GetLayer(node, count))));
            node.Layer = layer;
            node.X0 = _x0 + (layer * step);
            node.X1 = node.X0 + _nodeWidth;
            columns[layer].Add(node);
        }

        return columns;
    }

    private double GetLayer(SankeyNode node, int count)
    {
        return _alignment switch
        {
            SankeyAlignment.Left => node.Depth,
            SankeyAlignment.Right => count - 1 - node.Height,
            SankeyAlignment.Center => node.TargetLinks.Count > 0 ? node.Depth : node.SourceLinks.Count > 0 ? node.SourceLinks.Min(link => link.Target.Depth) - 1 : 0,
            _ => node.SourceLinks.Count > 0 ? node.Depth : count - 1,
        };
    }

    private void ComputeBreadths(List<List<SankeyNode>> columns)
    {
        var maxColumn = columns.Max(column => column.Count);
        if (maxColumn > 1)
        {
            _padding = Math.Min(_padding, (_y1 - _y0) / (maxColumn - 1));
        }

        InitializeBreadths(columns);
        for (var i = 0; i < _iterations; i++)
        {
            var alpha = Math.Pow(0.99, i);
            var beta = Math.Max(1 - alpha, (i + 1.0) / _iterations);
            RelaxRightToLeft(columns, alpha, beta);
            RelaxLeftToRight(columns, alpha, beta);
        }
    }

    private void InitializeBreadths(List<List<SankeyNode>> columns)
    {
        var scale = double.PositiveInfinity;
        foreach (var column in columns)
        {
            var sum = column.Sum(node => node.Weight);
            if (sum > 0)
            {
                scale = Math.Min(scale, (_y1 - _y0 - ((column.Count - 1) * _padding)) / sum);
            }
        }

        if (!double.IsFinite(scale))
        {
            scale = 0;
        }

        foreach (var column in columns)
        {
            var y = _y0;
            foreach (var node in column)
            {
                node.Y0 = y;
                node.Y1 = y + (node.Weight * scale);
                y = node.Y1 + _padding;
                foreach (var link in node.SourceLinks)
                {
                    link.Width = link.Weight * scale;
                }
            }

            // The remaining space is spread around the nodes
            var spacing = (_y1 - y + _padding) / (column.Count + 1);
            for (var i = 0; i < column.Count; i++)
            {
                column[i].Y0 += spacing * (i + 1);
                column[i].Y1 += spacing * (i + 1);
            }

            ReorderLinks(column);
        }
    }

    private void RelaxLeftToRight(List<List<SankeyNode>> columns, double alpha, double beta)
    {
        for (var i = 1; i < columns.Count; i++)
        {
            var column = columns[i];
            foreach (var target in column)
            {
                var y = 0.0;
                var weight = 0.0;
                foreach (var link in target.TargetLinks)
                {
                    var v = link.Weight * (target.Layer - link.Source.Layer);
                    y += TargetTop(link.Source, target) * v;
                    weight += v;
                }

                if (!(weight > 0))
                    continue;

                var dy = ((y / weight) - target.Y0) * alpha;
                target.Y0 += dy;
                target.Y1 += dy;
                ReorderNodeLinks(target);
            }

            column.Sort(CompareBreadth);
            ResolveCollisions(column, beta);
        }
    }

    private void RelaxRightToLeft(List<List<SankeyNode>> columns, double alpha, double beta)
    {
        for (var i = columns.Count - 2; i >= 0; i--)
        {
            var column = columns[i];
            foreach (var source in column)
            {
                var y = 0.0;
                var weight = 0.0;
                foreach (var link in source.SourceLinks)
                {
                    var v = link.Weight * (link.Target.Layer - source.Layer);
                    y += SourceTop(source, link.Target) * v;
                    weight += v;
                }

                if (!(weight > 0))
                    continue;

                var dy = ((y / weight) - source.Y0) * alpha;
                source.Y0 += dy;
                source.Y1 += dy;
                ReorderNodeLinks(source);
            }

            column.Sort(CompareBreadth);
            ResolveCollisions(column, beta);
        }
    }

    private void ResolveCollisions(List<SankeyNode> nodes, double alpha)
    {
        if (nodes.Count is 0)
            return;

        var i = nodes.Count >> 1;
        var subject = nodes[i];
        ResolveCollisionsBottomToTop(nodes, subject.Y0 - _padding, i - 1, alpha);
        ResolveCollisionsTopToBottom(nodes, subject.Y1 + _padding, i + 1, alpha);
        ResolveCollisionsBottomToTop(nodes, _y1, nodes.Count - 1, alpha);
        ResolveCollisionsTopToBottom(nodes, _y0, 0, alpha);
    }

    private void ResolveCollisionsTopToBottom(List<SankeyNode> nodes, double y, int i, double alpha)
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

            y = node.Y1 + _padding;
        }
    }

    private void ResolveCollisionsBottomToTop(List<SankeyNode> nodes, double y, int i, double alpha)
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

            y = node.Y0 - _padding;
        }
    }

    // The y coordinate of the top of the link from source to target, if it were centered on its source
    private double TargetTop(SankeyNode source, SankeyNode target)
    {
        var y = source.Y0 - ((source.SourceLinks.Count - 1) * _padding / 2);
        foreach (var link in source.SourceLinks)
        {
            if (link.Target == target)
                break;

            y += link.Width + _padding;
        }

        foreach (var link in target.TargetLinks)
        {
            if (link.Source == source)
                break;

            y -= link.Width;
        }

        return y;
    }

    private double SourceTop(SankeyNode source, SankeyNode target)
    {
        var y = target.Y0 - ((target.TargetLinks.Count - 1) * _padding / 2);
        foreach (var link in target.TargetLinks)
        {
            if (link.Source == source)
                break;

            y += link.Width + _padding;
        }

        foreach (var link in source.SourceLinks)
        {
            if (link.Target == target)
                break;

            y -= link.Width;
        }

        return y;
    }

    private static void ReorderLinks(List<SankeyNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.SourceLinks.Sort(CompareTargetBreadth);
            node.TargetLinks.Sort(CompareSourceBreadth);
        }
    }

    private static void ReorderNodeLinks(SankeyNode node)
    {
        foreach (var link in node.TargetLinks)
        {
            link.Source.SourceLinks.Sort(CompareTargetBreadth);
        }

        foreach (var link in node.SourceLinks)
        {
            link.Target.TargetLinks.Sort(CompareSourceBreadth);
        }
    }

    private static void ComputeLinkBreadths(List<SankeyNode> nodes)
    {
        foreach (var node in nodes)
        {
            var y0 = node.Y0;
            var y1 = node.Y0;
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

    private static int CompareBreadth(SankeyNode a, SankeyNode b) => a.Y0.CompareTo(b.Y0) is var result and not 0 ? result : a.Index.CompareTo(b.Index);

    private static int CompareSourceBreadth(SankeyLink a, SankeyLink b) => a.Source.Y0.CompareTo(b.Source.Y0) is var result and not 0 ? result : a.Index.CompareTo(b.Index);

    private static int CompareTargetBreadth(SankeyLink a, SankeyLink b) => a.Target.Y0.CompareTo(b.Target.Y0) is var result and not 0 ? result : a.Index.CompareTo(b.Index);
}
