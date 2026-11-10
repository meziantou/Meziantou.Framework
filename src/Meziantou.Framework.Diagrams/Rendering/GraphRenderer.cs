using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Lays a <see cref="GraphModel"/> out with the layered layout and draws it.</summary>
internal static class GraphRenderer
{
    private const double LabelPadding = 4;

    /// <summary>Lays the graph out and draws it into a group.</summary>
    /// <returns>The rectangle the drawing covers.</returns>
    public static SceneBounds Render(GraphModel model, DiagramRenderContext context, SceneGroup group)
    {
        CollapseDirectedClusters(model, context);
        Layout(model);
        return Draw(model, context, group);
    }

    /// <summary>
    /// Lays out separately the clusters that have their own direction and no edge across their border, as Mermaid does:
    /// they become a node of the graph that contains them.
    /// </summary>
    private static void CollapseDirectedClusters(GraphModel model, DiagramRenderContext context)
    {
        foreach (var cluster in model.Clusters.ToList())
        {
            if (cluster.Direction is null || !model.Clusters.Contains(cluster))
                continue;

            // An outer cluster that is collapsed takes this one with it
            if (HasCollapsibleAncestor(cluster))
                continue;

            if (model.Edges.Any(edge => Crosses(edge, cluster)))
                continue;

            var inner = new GraphModel
            {
                Direction = cluster.Direction.Value,
                NodeSeparation = model.NodeSeparation,
                RankSeparation = model.RankSeparation,
                EdgeSeparation = model.EdgeSeparation,
                ClusterPadding = model.ClusterPadding,
            };

            foreach (var node in model.Nodes.Where(node => node.Cluster is not null && node.Cluster.IsDescendantOf(cluster)).ToList())
            {
                model.Nodes.Remove(node);
                if (node.Cluster == cluster)
                {
                    node.Cluster = null;
                }

                inner.Nodes.Add(node);
            }

            foreach (var descendant in model.Clusters.Where(other => other != cluster && other.IsDescendantOf(cluster)).ToList())
            {
                model.Clusters.Remove(descendant);
                if (descendant.Parent == cluster)
                {
                    descendant.Parent = null;
                }

                inner.Clusters.Add(descendant);
            }

            foreach (var edge in model.Edges.Where(edge => IsInside(edge, inner)).ToList())
            {
                model.Edges.Remove(edge);
                inner.Edges.Add(edge);
            }

            var content = new SceneGroup();
            var bounds = Render(inner, context, content);
            var padding = model.ClusterPadding * 2;
            var compound = new GraphNode
            {
                Width = bounds.Width + (2 * padding),
                Height = bounds.Height + (2 * padding) + cluster.TitleSpace,
                Cluster = cluster.Parent,
            };
            compound.Draw = (target, x, y) =>
            {
                var rectangle = new SceneBounds(x - (compound.Width / 2), y - (compound.Height / 2), compound.Width, compound.Height);
                cluster.Bounds = rectangle;
                cluster.Draw?.Invoke(target, rectangle);
                content.Transform = SceneTransform.Translate(rectangle.X + padding - bounds.X, rectangle.Y + padding + cluster.TitleSpace - bounds.Y);
                target.Add(content);
            };

            model.Clusters.Remove(cluster);
            model.Nodes.Add(compound);
            foreach (var edge in model.Edges)
            {
                if (edge.SourceCluster == cluster)
                {
                    edge.Source = compound;
                    edge.SourceCluster = null;
                }

                if (edge.TargetCluster == cluster)
                {
                    edge.Target = compound;
                    edge.TargetCluster = null;
                }
            }
        }

        bool HasCollapsibleAncestor(GraphCluster cluster)
        {
            for (var parent = cluster.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent.Direction is not null && !model.Edges.Any(edge => Crosses(edge, parent)))
                    return true;
            }

            return false;
        }

        static bool IsInside(GraphEdge edge, GraphModel inner) => (edge.Source is null || inner.Nodes.Contains(edge.Source)) && (edge.Target is null || inner.Nodes.Contains(edge.Target)) && (edge.SourceCluster is null || inner.Clusters.Contains(edge.SourceCluster)) && (edge.TargetCluster is null || inner.Clusters.Contains(edge.TargetCluster));
    }

    private static bool Crosses(GraphEdge edge, GraphCluster cluster)
    {
        var sourceInside = IsInCluster(edge.Source?.Cluster, edge.SourceCluster, cluster, isAttachedToCluster: edge.Source is null);
        var targetInside = IsInCluster(edge.Target?.Cluster, edge.TargetCluster, cluster, isAttachedToCluster: edge.Target is null);
        return sourceInside != targetInside;

        static bool IsInCluster(GraphCluster? nodeCluster, GraphCluster? attached, GraphCluster cluster, bool isAttachedToCluster)
        {
            if (isAttachedToCluster)

                // An edge attached to the cluster itself is outside of it: it attaches to its border
                return attached is not null && attached != cluster && attached.IsDescendantOf(cluster);

            return nodeCluster is not null && nodeCluster.IsDescendantOf(cluster);
        }
    }

    private static void Layout(GraphModel model)
    {
        var graph = new LayoutGraph
        {
            Direction = model.Direction,
            NodeSeparation = model.NodeSeparation,
            RankSeparation = model.RankSeparation,
            EdgeSeparation = model.EdgeSeparation,
            ClusterPadding = model.ClusterPadding,
        };

        var clusters = new Dictionary<GraphCluster, LayoutNode>();
        foreach (var cluster in model.Clusters)
        {
            var node = graph.AddCluster(cluster);
            node.TitleSpace = cluster.TitleSpace;
            clusters[cluster] = node;
        }

        foreach (var cluster in model.Clusters)
        {
            if (cluster.Parent is not null && clusters.TryGetValue(cluster.Parent, out var parent))
            {
                LayoutGraph.SetParent(clusters[cluster], parent);
            }
        }

        var nodes = new Dictionary<GraphNode, LayoutNode>();
        foreach (var node in model.Nodes)
        {
            var layoutNode = graph.AddNode(node.Width, node.Height, node);
            nodes[node] = layoutNode;
            if (node.Cluster is not null && clusters.TryGetValue(node.Cluster, out var parent))
            {
                LayoutGraph.SetParent(layoutNode, parent);
            }
        }

        // A cluster without nodes gets an invisible one, so it takes some space and its edges have an end
        var placeholders = new Dictionary<GraphCluster, LayoutNode>();
        foreach (var cluster in model.Clusters)
        {
            if (!HasLeaf(clusters[cluster]))
            {
                var placeholder = graph.AddNode(1, 1);
                LayoutGraph.SetParent(placeholder, clusters[cluster]);
                placeholders[cluster] = placeholder;
            }
        }

        var edges = new Dictionary<GraphEdge, LayoutEdge>();
        foreach (var edge in model.Edges)
        {
            var source = ResolveEnd(edge.Source, edge.SourceCluster);
            var target = ResolveEnd(edge.Target, edge.TargetCluster);
            if (source is null || target is null)
                continue;

            var layoutEdge = graph.AddEdge(source, target, edge);
            layoutEdge.MinLength = Math.Max(1, edge.MinLength);
            layoutEdge.Weight = edge.Weight;
            if (edge.Label is { IsEmpty: false } label)
            {
                layoutEdge.LabelWidth = label.Width + (2 * LabelPadding);
                layoutEdge.LabelHeight = label.Height + (2 * LabelPadding);
            }

            edges[edge] = layoutEdge;
        }

        LayeredLayout.Run(graph);

        foreach (var (node, layoutNode) in nodes)
        {
            node.X = layoutNode.X;
            node.Y = layoutNode.Y;
        }

        foreach (var (cluster, layoutNode) in clusters)
        {
            cluster.Bounds = new SceneBounds(layoutNode.X - (layoutNode.Width / 2), layoutNode.Y - (layoutNode.Height / 2), layoutNode.Width, layoutNode.Height);
        }

        foreach (var (edge, layoutEdge) in edges)
        {
            edge.Points = [.. layoutEdge.Points];
            edge.LabelPosition = new LayoutPoint(layoutEdge.LabelX, layoutEdge.LabelY);
            edge.Points = ComputeEndpoints(edge, layoutEdge.V, layoutEdge.W);

            // An edge attached to a cluster is laid out from a node inside it, then cut at its border: a label left
            // inside the cluster goes to the middle of what remains
            if ((edge.SourceCluster is { } sourceCluster && Geometry.Contains(sourceCluster.Bounds, edge.LabelPosition)) || (edge.TargetCluster is { } targetCluster && Geometry.Contains(targetCluster.Bounds, edge.LabelPosition)))
            {
                edge.LabelPosition = edge.Points.Count > 1 ? Midpoint(edge.Points) : edge.LabelPosition;
            }
        }

        LayoutNode? ResolveEnd(GraphNode? node, GraphCluster? cluster)
        {
            if (node is not null)
                return nodes.GetValueOrDefault(node);

            if (cluster is null || !clusters.TryGetValue(cluster, out var layoutCluster))
                return null;

            return FirstLeaf(layoutCluster) ?? placeholders.GetValueOrDefault(cluster);
        }

        static bool HasLeaf(LayoutNode cluster) => FirstLeaf(cluster) is not null;

        static LayoutNode? FirstLeaf(LayoutNode cluster)
        {
            foreach (var child in cluster.Children)
            {
                if (!child.IsCluster)
                    return child;

                if (FirstLeaf(child) is { } leaf)
                    return leaf;
            }

            return null;
        }
    }

    /// <summary>Adds the ends of an edge: where it leaves its source and reaches its target, or the border of their cluster.</summary>
    private static List<LayoutPoint> ComputeEndpoints(GraphEdge edge, LayoutNode layoutSource, LayoutNode layoutTarget)
    {
        var sourceCenter = new LayoutPoint(layoutSource.X, layoutSource.Y);
        var targetCenter = new LayoutPoint(layoutTarget.X, layoutTarget.Y);
        var inner = edge.Points;
        var first = inner.Count > 0 ? inner[0] : targetCenter;
        var last = inner.Count > 0 ? inner[^1] : sourceCenter;

        var points = new List<LayoutPoint>(inner.Count + 2);
        if (edge.SourceCluster is { } sourceCluster)
        {
            points.Add(sourceCenter);
            points.AddRange(inner);
            points.Add(targetCenter);
            points = CutAtCluster(points, sourceCluster.Bounds, fromStart: true);
        }
        else
        {
            points.Add(edge.Source is { } source ? source.IntersectToward(first) : sourceCenter);
            points.AddRange(inner);
            points.Add(targetCenter);
        }

        if (edge.TargetCluster is { } targetCluster)
        {
            points = CutAtCluster(points, targetCluster.Bounds, fromStart: false);
        }
        else
        {
            points[^1] = edge.Target is { } target ? target.IntersectToward(points.Count > 1 ? points[^2] : last) : targetCenter;
        }

        // Duplicated points have no direction for the markers
        var result = new List<LayoutPoint>(points.Count);
        foreach (var point in points)
        {
            if (!Geometry.IsFinite(point))
                continue;

            if (result.Count is 0 || Geometry.Distance(result[^1], point) > 1e-6)
            {
                result.Add(point);
            }
        }

        return result;
    }

    /// <summary>Removes the part of a path inside a cluster, from its start or to its end, and ends it on the border.</summary>
    private static List<LayoutPoint> CutAtCluster(List<LayoutPoint> points, SceneBounds bounds, bool fromStart)
    {
        if (points.Count < 2)
            return points;

        if (!fromStart)
        {
            points.Reverse();
        }

        var index = 0;
        while (index < points.Count - 1 && Geometry.Contains(bounds, points[index + 1]))
        {
            index++;
        }

        List<LayoutPoint> result;
        if (index >= points.Count - 1)
        {
            result = points;
        }
        else
        {
            var inside = points[index];
            var outside = points[index + 1];
            var border = IntersectSegmentWithRectangle(bounds, inside, outside);
            result = [border, .. points.Skip(index + 1)];
        }

        if (!fromStart)
        {
            result.Reverse();
        }

        return result;
    }

    private static LayoutPoint IntersectSegmentWithRectangle(SceneBounds bounds, LayoutPoint inside, LayoutPoint outside)
    {
        var polygon = new List<List<LayoutPoint>>
        {
            new() { new(bounds.X, bounds.Y), new(bounds.Right, bounds.Y), new(bounds.Right, bounds.Bottom), new(bounds.X, bounds.Bottom), new(bounds.X, bounds.Y) },
        };
        return Geometry.Intersect(polygon, inside, outside);
    }

    private static SceneBounds Draw(GraphModel model, DiagramRenderContext context, SceneGroup group)
    {
        var bounds = new BoundsBuilder();
        var clusterGroup = group.Add(new SceneGroup { CssClass = "clusters" });
        foreach (var cluster in model.Clusters.OrderBy(Depth))
        {
            cluster.Draw?.Invoke(clusterGroup, cluster.Bounds);
            bounds.Add(cluster.Bounds);
        }

        DrawEdgesAndNodes(model, context, group, bounds);
        return bounds.ToBounds();
    }

    /// <summary>Draws the edges, their labels and the nodes, whose positions are known.</summary>
    internal static void DrawEdgesAndNodes(GraphModel model, DiagramRenderContext context, SceneGroup group, BoundsBuilder bounds)
    {
        var edgeGroup = group.Add(new SceneGroup { CssClass = "edges" });
        var labelGroup = group.Add(new SceneGroup { CssClass = "edge-labels" });
        foreach (var edge in model.Edges)
        {
            if (edge.Points.Count < 2 || edge.Invisible)
            {
                foreach (var point in edge.Points)
                {
                    bounds.Add(point);
                }

                continue;
            }

            DrawEdge(edge, context, edgeGroup, labelGroup, bounds);
        }

        var nodeGroup = group.Add(new SceneGroup { CssClass = "nodes" });
        foreach (var node in model.Nodes)
        {
            node.Draw?.Invoke(nodeGroup, node.X, node.Y);
            bounds.Add(node.Bounds);
        }
    }

    internal static int Depth(GraphCluster cluster)
    {
        var depth = 0;
        for (var parent = cluster.Parent; parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        return depth;
    }

    private static void DrawEdge(GraphEdge edge, DiagramRenderContext context, SceneGroup edgeGroup, SceneGroup labelGroup, BoundsBuilder bounds)
    {
        var points = edge.Points;
        var start = points[0];
        var end = points[^1];

        // The line stops at the base of the markers
        var line = new List<LayoutPoint>(points);
        var startSetback = EdgeMarkers.GetSetback(edge.StartMarker);
        if (startSetback > 0 && Geometry.Distance(line[0], line[1]) > startSetback)
        {
            line[0] = Geometry.MoveToward(line[0], line[1], startSetback);
        }

        var endSetback = EdgeMarkers.GetSetback(edge.EndMarker);
        if (endSetback > 0 && Geometry.Distance(line[^1], line[^2]) > endSetback)
        {
            line[^1] = Geometry.MoveToward(line[^1], line[^2], endSetback);
        }

        var target = edgeGroup.Add(new SceneGroup { CssClass = edge.CssClass is null ? "edge" : "edge " + edge.CssClass, Opacity = edge.Opacity });
        target.Add(new ScenePath(Curves.Build(line, edge.Curve))
        {
            Stroke = edge.Color,
            StrokeWidth = edge.StrokeWidth,
            StrokeDashArray = edge.StrokeDashArray,
            CssClass = "edge-path",
        });

        var markerColor = edge.MarkerColor ?? edge.Color;
        EdgeMarkers.Draw(target, edge.StartMarker, start, points[1], markerColor, context.Background, edge.StrokeWidth);
        EdgeMarkers.Draw(target, edge.EndMarker, end, points[^2], markerColor, context.Background, edge.StrokeWidth);
        foreach (var point in points)
        {
            bounds.Add(point);
        }

        if (edge.Label is { IsEmpty: false } label)
        {
            var position = edge.LabelPosition;
            if (!Geometry.IsFinite(position) || (position.X == 0 && position.Y == 0))
            {
                position = Midpoint(points);
            }

            DrawLabel(labelGroup, label, position, edge.LabelColor, edge.LabelBackground, bounds);
        }

        if (edge.StartLabel is { IsEmpty: false } startLabel)
        {
            DrawLabel(labelGroup, startLabel, EndLabelPosition(points, startLabel, fromStart: true), edge.LabelColor, background: null, bounds);
        }

        if (edge.EndLabel is { IsEmpty: false } endLabel)
        {
            DrawLabel(labelGroup, endLabel, EndLabelPosition(points, endLabel, fromStart: false), edge.LabelColor, background: null, bounds);
        }
    }

    private static void DrawLabel(SceneGroup group, TextBlock label, LayoutPoint position, SceneColor color, SceneColor? background, BoundsBuilder bounds)
    {
        var rectangle = new SceneBounds(position.X - (label.Width / 2) - LabelPadding, position.Y - (label.Height / 2) - LabelPadding, label.Width + (2 * LabelPadding), label.Height + (2 * LabelPadding));
        var labelGroup = group.Add(new SceneGroup { CssClass = "edge-label" });
        if (background is { } fill)
        {
            labelGroup.Add(new SceneRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) { Fill = fill });
        }

        label.Draw(labelGroup, position.X, position.Y, color);
        bounds.Add(rectangle);
    }

    /// <summary>Places a label next to an end of an edge, beside the line.</summary>
    private static LayoutPoint EndLabelPosition(List<LayoutPoint> points, TextBlock label, bool fromStart)
    {
        var tip = fromStart ? points[0] : points[^1];
        var next = fromStart ? points[1] : points[^2];
        var length = Geometry.Distance(tip, next);
        if (length < 1e-9)
            return tip;

        var dx = (next.X - tip.X) / length;
        var dy = (next.Y - tip.Y) / length;
        var along = Math.Min(length / 2, 14 + (Math.Abs(dx) * label.Width / 2) + (Math.Abs(dy) * label.Height / 2));
        var side = 6 + (Math.Abs(dy) * label.Width / 2) + (Math.Abs(dx) * label.Height / 2);
        return new LayoutPoint(tip.X + (dx * along) + (dy * side), tip.Y + (dy * along) - (dx * side));
    }

    internal static LayoutPoint Midpoint(List<LayoutPoint> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            total += Geometry.Distance(points[i - 1], points[i]);
        }

        var half = total / 2;
        for (var i = 1; i < points.Count; i++)
        {
            var length = Geometry.Distance(points[i - 1], points[i]);
            if (half <= length)
                return Geometry.MoveToward(points[i - 1], points[i], half);

            half -= length;
        }

        return points[^1];
    }
}
