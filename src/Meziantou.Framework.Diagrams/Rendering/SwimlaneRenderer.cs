using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Lays a graph out in swimlanes: the top-level clusters are bands across the diagram, in their order, and the nodes of a
/// lane stay in its band. The layered layout gives the position of the nodes along the flow; each lane then stacks its
/// nodes of a rank, and the edges go between the bands.
/// </summary>
internal static class SwimlaneRenderer
{
    private const double LanePadding = 20;

    public static SceneBounds Render(GraphModel model, DiagramRenderContext context, SceneGroup group)
    {
        var horizontal = model.Direction is LayoutDirection.LeftToRight or LayoutDirection.RightToLeft;
        var lanes = model.Clusters.Where(cluster => cluster.Parent is null).ToList();
        var laneOf = new Dictionary<GraphNode, int>();
        foreach (var node in model.Nodes)
        {
            var cluster = node.Cluster;
            while (cluster?.Parent is not null)
            {
                cluster = cluster.Parent;
            }

            laneOf[node] = cluster is null ? lanes.Count : lanes.IndexOf(cluster);
        }

        // The position of the nodes along the flow, from the layered layout without the clusters
        var graph = new LayoutGraph
        {
            Direction = model.Direction,
            NodeSeparation = model.NodeSeparation,
            RankSeparation = model.RankSeparation,
            EdgeSeparation = model.EdgeSeparation,
        };
        var layoutNodes = new Dictionary<GraphNode, LayoutNode>();
        foreach (var node in model.Nodes)
        {
            layoutNodes[node] = graph.AddNode(node.Width, node.Height, node);
        }

        foreach (var edge in model.Edges)
        {
            var source = Resolve(edge.Source, edge.SourceCluster);
            var target = Resolve(edge.Target, edge.TargetCluster);
            if (source is null || target is null)
                continue;

            var layoutEdge = graph.AddEdge(layoutNodes[source], layoutNodes[target]);
            layoutEdge.MinLength = Math.Max(1, edge.MinLength);
            if (edge.Label is { IsEmpty: false } label)
            {
                layoutEdge.LabelWidth = label.Width + 8;
                layoutEdge.LabelHeight = label.Height + 8;
            }
        }

        LayeredLayout.Run(graph);

        double Rank(GraphNode node) => horizontal ? layoutNodes[node].X : layoutNodes[node].Y;
        double Cross(GraphNode node) => horizontal ? layoutNodes[node].Y : layoutNodes[node].X;
        double RankSize(GraphNode node) => horizontal ? node.Width : node.Height;
        double CrossSize(GraphNode node) => horizontal ? node.Height : node.Width;

        // The header of the lanes takes the start of the flow
        var header = lanes.Count is 0 ? 0 : lanes.Max(lane => horizontal ? lane.TitleWidth : lane.TitleSpace) + LanePadding;
        var minRank = model.Nodes.Count is 0 ? 0 : model.Nodes.Min(node => Rank(node) - (RankSize(node) / 2));
        var maxRank = model.Nodes.Count is 0 ? 0 : model.Nodes.Max(node => Rank(node) + (RankSize(node) / 2));
        var rankExtent = header + (maxRank - minRank) + (2 * LanePadding);

        // Each lane stacks the nodes of a rank across the flow, in the order of the layout
        var laneCount = lanes.Count + (laneOf.Values.Any(index => index == lanes.Count) ? 1 : 0);
        var crossStart = 0.0;
        var laneBounds = new SceneBounds[laneCount];
        for (var lane = 0; lane < laneCount; lane++)
        {
            var nodes = model.Nodes.Where(node => laneOf[node] == lane).ToList();
            var stacks = nodes.GroupBy(node => Math.Round(Rank(node), 3, MidpointRounding.AwayFromZero)).Select(stack => stack.OrderBy(Cross).ToList()).ToList();
            var thickness = stacks.Count is 0 ? 0 : stacks.Max(stack => stack.Sum(CrossSize) + ((stack.Count - 1) * model.NodeSeparation / 2));
            var titleCross = lane < lanes.Count ? (horizontal ? lanes[lane].TitleSpace : lanes[lane].TitleWidth) : 0;
            var laneThickness = Math.Max(thickness, titleCross) + (2 * LanePadding);
            foreach (var stack in stacks)
            {
                var size = stack.Sum(CrossSize) + ((stack.Count - 1) * model.NodeSeparation / 2);
                var position = crossStart + ((laneThickness - size) / 2);
                foreach (var node in stack)
                {
                    var rank = Rank(node) - minRank + header + LanePadding;
                    var cross = position + (CrossSize(node) / 2);
                    (node.X, node.Y) = horizontal ? (rank, cross) : (cross, rank);
                    position += CrossSize(node) + (model.NodeSeparation / 2);
                }
            }

            laneBounds[lane] = horizontal ? new SceneBounds(0, crossStart, rankExtent, laneThickness) : new SceneBounds(crossStart, 0, laneThickness, rankExtent);
            crossStart += laneThickness;
        }

        for (var i = 0; i < lanes.Count; i++)
        {
            lanes[i].Bounds = laneBounds[i];
            lanes[i].HeaderSize = header;
        }

        // The nested clusters are boxes around their nodes
        foreach (var cluster in model.Clusters.Where(cluster => cluster.Parent is not null).OrderByDescending(GraphRenderer.Depth))
        {
            var builder = new BoundsBuilder();
            foreach (var node in model.Nodes.Where(node => node.Cluster is not null && node.Cluster.IsDescendantOf(cluster)))
            {
                builder.Add(node.Bounds);
            }

            foreach (var child in model.Clusters.Where(child => child.Parent == cluster))
            {
                builder.Add(child.Bounds);
            }

            var box = builder.ToBounds();
            cluster.Bounds = new SceneBounds(box.X - model.ClusterPadding, box.Y - model.ClusterPadding - cluster.TitleSpace, box.Width + (2 * model.ClusterPadding), box.Height + (2 * model.ClusterPadding) + cluster.TitleSpace);
        }

        foreach (var edge in model.Edges)
        {
            edge.Points = Route(edge, horizontal, model.RankSeparation);
            edge.LabelPosition = edge.Points.Count > 1 ? GraphRenderer.Midpoint(edge.Points) : default;
        }

        var bounds = new BoundsBuilder();
        var clusterGroup = group.Add(new SceneGroup { CssClass = "lanes" });
        foreach (var cluster in model.Clusters.OrderBy(GraphRenderer.Depth))
        {
            cluster.Draw?.Invoke(clusterGroup, cluster.Bounds);
            bounds.Add(cluster.Bounds);
        }

        for (var i = lanes.Count; i < laneCount; i++)
        {
            bounds.Add(laneBounds[i]);
        }

        GraphRenderer.DrawEdgesAndNodes(model, context, group, bounds);
        return bounds.ToBounds();

        GraphNode? Resolve(GraphNode? node, GraphCluster? cluster)
        {
            if (node is not null)
                return node;

            return cluster is null ? null : model.Nodes.FirstOrDefault(candidate => candidate.Cluster is not null && candidate.Cluster.IsDescendantOf(cluster));
        }
    }

    /// <summary>Routes an edge along the flow, with a turn halfway between its ends when they are not aligned.</summary>
    private static List<LayoutPoint> Route(GraphEdge edge, bool horizontal, double rankSeparation)
    {
        var source = edge.Source is { } sourceNode ? (Center: sourceNode.Center, Node: sourceNode, Cluster: (GraphCluster?)null) : (Center: Center(edge.SourceCluster!.Bounds), Node: null, Cluster: edge.SourceCluster);
        var target = edge.Target is { } targetNode ? (Center: targetNode.Center, Node: targetNode, Cluster: (GraphCluster?)null) : (Center: Center(edge.TargetCluster!.Bounds), Node: null, Cluster: edge.TargetCluster);
        if (edge.Source is not null && edge.Source == edge.Target)
        {
            var node = edge.Source;
            var x = node.X + (node.Width / 2);
            var dy = node.Height / 2;
            const double Dx = 30;
            return [node.IntersectToward(new(x, node.Y - dy)), new(x + (Dx / 2), node.Y - dy), new(x + Dx, node.Y), new(x + (Dx / 2), node.Y + dy), node.IntersectToward(new(x, node.Y + dy))];
        }

        LayoutPoint P(double rank, double cross) => horizontal ? new(rank, cross) : new(cross, rank);
        double Rank(LayoutPoint point) => horizontal ? point.X : point.Y;
        double Cross(LayoutPoint point) => horizontal ? point.Y : point.X;

        var sourceRank = Rank(source.Center);
        var targetRank = Rank(target.Center);
        var sourceCross = Cross(source.Center);
        var targetCross = Cross(target.Center);
        List<LayoutPoint> inner;
        if (Math.Abs(targetRank - sourceRank) < 1e-6 || Math.Abs(targetCross - sourceCross) < 1e-6)
        {
            inner = [];
        }
        else if (targetRank > sourceRank)
        {
            var middle = (sourceRank + targetRank) / 2;
            inner = [P(middle, sourceCross), P(middle, targetCross)];
        }
        else
        {
            // Backward: out of the side, around the far side of both nodes, and in from the side
            var sourceHalf = source.Node is { } s ? (horizontal ? s.Height : s.Width) / 2 : 0;
            var targetHalf = target.Node is { } t ? (horizontal ? t.Height : t.Width) / 2 : 0;
            var detour = Math.Max(sourceCross + sourceHalf, targetCross + targetHalf) + Math.Min(15, rankSeparation / 2);
            inner = [P(sourceRank, detour), P(targetRank, detour)];
        }

        var first = inner.Count > 0 ? inner[0] : target.Center;
        var last = inner.Count > 0 ? inner[^1] : source.Center;
        var start = source.Node?.IntersectToward(first) ?? Geometry.IntersectRectangle(source.Cluster!.Bounds, first);
        var end = target.Node?.IntersectToward(last) ?? Geometry.IntersectRectangle(target.Cluster!.Bounds, last);
        return [start, .. inner, end];

        static LayoutPoint Center(SceneBounds bounds) => new(bounds.CenterX, bounds.CenterY);
    }
}
