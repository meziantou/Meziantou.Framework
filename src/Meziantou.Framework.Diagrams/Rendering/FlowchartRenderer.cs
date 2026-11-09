using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal static class FlowchartRenderer
{
    public static SceneBounds Render(FlowchartDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var model = new GraphModel
        {
            Direction = diagram.Direction.ToLayout(),
            NodeSeparation = diagram.NodeSpacing,
            RankSeparation = diagram.RankSpacing,
        };

        // The innermost subgraph of each node, and the parent of each subgraph
        var clusters = new Dictionary<FlowchartSubgraph, GraphCluster>(ReferenceEqualityComparer.Instance);
        var nodeSubgraph = new Dictionary<FlowchartNode, FlowchartSubgraph>(ReferenceEqualityComparer.Instance);
        var allNodes = new List<FlowchartNode>(diagram.Nodes);
        var seen = new HashSet<FlowchartNode>(diagram.Nodes, ReferenceEqualityComparer.Instance);
        foreach (var subgraph in diagram.Subgraphs)
        {
            AddSubgraph(subgraph, parent: null, depth: 0);
        }

        var graphNodes = new Dictionary<FlowchartNode, GraphNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in allNodes)
        {
            if (graphNodes.ContainsKey(node))
                continue;

            graphNodes[node] = CreateNode(node, nodeSubgraph.TryGetValue(node, out var subgraph) ? clusters[subgraph] : null);
        }

        foreach (var edge in diagram.Edges)
        {
            var graphEdge = CreateEdge(edge);
            if (graphEdge is not null)
            {
                model.Edges.Add(graphEdge);
            }
        }

        return diagram.Swimlanes ? SwimlaneRenderer.Render(model, context, group) : GraphRenderer.Render(model, context, group);

        void AddSubgraph(FlowchartSubgraph subgraph, FlowchartSubgraph? parent, int depth)
        {
            // A subgraph listed twice, or nested in itself, is drawn once; very deep nesting is flattened
            if (clusters.ContainsKey(subgraph) || depth > 64)
                return;

            var style = ResolveStyle(subgraph.CssClasses, subgraph.Style, includeDefault: false);
            var title = context.Text(subgraph.Label ?? subgraph.Id, style.FontSize, style.Bold ?? false, style.Italic ?? false, diagram.WrappingWidth);
            var isLane = diagram.Swimlanes && parent is null;
            var cluster = new GraphCluster
            {
                Parent = parent is null ? null : clusters[parent],
                TitleSpace = title.IsEmpty ? 0 : title.Height + 8,
                TitleWidth = title.IsEmpty ? 0 : title.Width + 16,
                Direction = diagram.Swimlanes ? null : subgraph.Direction?.ToLayout(),
            };
            var laneIndex = model.Clusters.Count(other => other.Parent is null);
            cluster.Draw = (target, bounds) =>
            {
                if (isLane)
                {
                    DrawLane(target, subgraph, style, title, bounds, laneIndex, cluster.HeaderSize);
                    return;
                }

                var clusterGroup = target.Add(new SceneGroup
                {
                    CssClass = StyleHelpers.ToCssClass(subgraph.CssClasses, "cluster"),
                    Opacity = style.Opacity ?? 1,
                    Hyperlink = subgraph.Link,
                    Tooltip = subgraph.Tooltip,
                });
                clusterGroup.SetDataAttribute("id", subgraph.Id);
                clusterGroup.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                {
                    Fill = style.Fill ?? theme.ClusterBackground,
                    Stroke = style.Stroke ?? theme.ClusterBorderColor,
                    StrokeWidth = style.StrokeWidth ?? theme.StrokeWidth,
                    StrokeDashArray = style.StrokeDashArray,
                });
                title.Draw(clusterGroup, bounds.CenterX, bounds.Y + 4 + (title.Height / 2), style.TextColor ?? theme.TitleColor);
            };

            clusters[subgraph] = cluster;
            model.Clusters.Add(cluster);
            foreach (var node in subgraph.Nodes)
            {
                nodeSubgraph[node] = subgraph;
                if (seen.Add(node))
                {
                    allNodes.Add(node);
                }
            }

            foreach (var child in subgraph.Subgraphs)
            {
                AddSubgraph(child, subgraph, depth + 1);
            }
        }

        // A lane: a band with its title in a header at the start of the flow, and every other band a little darker
        void DrawLane(SceneGroup target, FlowchartSubgraph subgraph, DiagramStyle style, TextBlock title, SceneBounds bounds, int index, double header)
        {
            var laneGroup = target.Add(new SceneGroup
            {
                CssClass = StyleHelpers.ToCssClass(subgraph.CssClasses, "lane"),
                Opacity = style.Opacity ?? 1,
                Hyperlink = subgraph.Link,
                Tooltip = subgraph.Tooltip,
            });
            laneGroup.SetDataAttribute("id", subgraph.Id);
            var fill = style.Fill ?? (index % 2 == 0 ? theme.ClusterBackground : theme.ClusterBackground.WithOpacity(theme.ClusterBackground.Opacity * 0.5));
            var stroke = style.Stroke ?? theme.ClusterBorderColor;
            laneGroup.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height) { Fill = fill, Stroke = stroke, StrokeWidth = style.StrokeWidth ?? theme.StrokeWidth, StrokeDashArray = style.StrokeDashArray });
            var textColor = style.TextColor ?? theme.TitleColor;
            if (diagram.Direction.IsHorizontal())
            {
                laneGroup.Add(new SceneLine(bounds.X + header, bounds.Y, bounds.X + header, bounds.Bottom) { Stroke = stroke, StrokeWidth = theme.StrokeWidth });
                title.Draw(laneGroup, bounds.X + (header / 2), bounds.CenterY, textColor);
            }
            else
            {
                laneGroup.Add(new SceneLine(bounds.X, bounds.Y + header, bounds.Right, bounds.Y + header) { Stroke = stroke, StrokeWidth = theme.StrokeWidth });
                title.Draw(laneGroup, bounds.CenterX, bounds.Y + (header / 2), textColor);
            }
        }

        GraphNode CreateNode(FlowchartNode node, GraphCluster? cluster)
        {
            var style = ResolveStyle(node.CssClasses, node.Style, includeDefault: true);
            var label = context.Text(node.Label ?? node.Id, style.FontSize, style.Bold ?? false, style.Italic ?? false, diagram.WrappingWidth);
            var shape = Shapes.Create(node.Shape, label.Width, label.Height, diagram.Direction.IsHorizontal());
            var graphNode = model.AddNode(shape.Width, shape.Height, cluster);
            graphNode.Intersect = shape.Intersect;
            graphNode.Draw = (target, x, y) =>
            {
                var nodeGroup = target.Add(new SceneGroup
                {
                    CssClass = StyleHelpers.ToCssClass(node.CssClasses, "node"),
                    Opacity = style.Opacity ?? 1,
                    Hyperlink = node.Link,
                    Tooltip = node.Tooltip,
                });
                nodeGroup.SetDataAttribute("id", node.Id);
                var stroke = style.Stroke ?? (shape.FillWithLineColor || shape.FilledDecorations.Count > 0 ? theme.LineColor : theme.NodeBorderColor);
                StyleHelpers.DrawShape(nodeGroup, shape, x, y, style.Fill ?? theme.NodeBackground, stroke, style.StrokeWidth ?? theme.StrokeWidth, style.StrokeDashArray);
                if (shape.ShowLabel)
                {
                    label.Draw(nodeGroup, x + shape.LabelOffsetX, y + shape.LabelOffsetY, style.TextColor ?? theme.NodeTextColor);
                }
            };

            return graphNode;
        }

        GraphEdge? CreateEdge(FlowchartEdge edge)
        {
            var (source, sourceCluster) = Resolve(edge.Source);
            var (target, targetCluster) = Resolve(edge.Target);
            if ((source is null && sourceCluster is null) || (target is null && targetCluster is null))
                return null;

            var style = ResolveEdgeStyle(edge);
            var label = edge.Label is { IsEmpty: false } text ? context.Text(text, style.FontSize, style.Bold ?? false, style.Italic ?? false, diagram.WrappingWidth) : null;
            var color = style.Stroke ?? theme.LineColor;
            return new GraphEdge
            {
                Source = source,
                Target = target,
                SourceCluster = sourceCluster,
                TargetCluster = targetCluster,
                Label = label,
                MinLength = edge.Length,
                Curve = edge.Curve ?? diagram.Curve,
                Color = color,
                MarkerColor = style.Stroke ?? theme.ArrowheadColor,
                StrokeWidth = style.StrokeWidth ?? (edge.LineStyle is FlowchartLineStyle.Thick ? 3.5 : theme.StrokeWidth),
                StrokeDashArray = style.StrokeDashArray ?? (edge.LineStyle is FlowchartLineStyle.Dotted ? [3, 3] : null),
                StartMarker = ToMarker(edge.StartArrowHead),
                EndMarker = ToMarker(edge.EndArrowHead),
                LabelColor = style.TextColor ?? theme.TextColor,
                LabelBackground = theme.EdgeLabelBackground,
                Invisible = edge.LineStyle is FlowchartLineStyle.Invisible,
                CssClass = StyleHelpers.ToCssClass(edge.CssClasses, edge.LineStyle switch
                {
                    FlowchartLineStyle.Dotted => "dotted",
                    FlowchartLineStyle.Thick => "thick",
                    FlowchartLineStyle.Invisible => "invisible",
                    _ => "solid",
                }),
                Opacity = style.Opacity ?? 1,
            };
        }

        (GraphNode? Node, GraphCluster? Cluster) Resolve(FlowchartElement element)
        {
            return element switch
            {
                FlowchartNode node => (graphNodes.TryGetValue(node, out var graphNode) ? graphNode : AddMissingNode(node), null),
                FlowchartSubgraph subgraph => (null, clusters.GetValueOrDefault(subgraph)),
                _ => (null, null),
            };
        }

        // An edge to a node that is in no list draws the node
        GraphNode AddMissingNode(FlowchartNode node)
        {
            var graphNode = CreateNode(node, cluster: null);
            graphNodes[node] = graphNode;
            return graphNode;
        }

        DiagramStyle ResolveStyle(IList<string> classes, DiagramStyle? own, bool includeDefault)
        {
            var style = new DiagramStyle();
            if (includeDefault && diagram.ClassDefinitions.TryGetValue("default", out var defaultStyle))
            {
                style = style.Merge(defaultStyle);
            }

            foreach (var name in classes)
            {
                if (diagram.ClassDefinitions.TryGetValue(name, out var classStyle))
                {
                    style = style.Merge(classStyle);
                }
            }

            return style.Merge(own);
        }

        DiagramStyle ResolveEdgeStyle(FlowchartEdge edge)
        {
            var style = new DiagramStyle().Merge(diagram.DefaultEdgeStyle);
            foreach (var name in edge.CssClasses)
            {
                if (diagram.ClassDefinitions.TryGetValue(name, out var classStyle))
                {
                    style = style.Merge(classStyle);
                }
            }

            return style.Merge(edge.Style);
        }

        static EdgeMarker ToMarker(FlowchartArrowHead head) => head switch
        {
            FlowchartArrowHead.Arrow => EdgeMarker.Arrow,
            FlowchartArrowHead.Circle => EdgeMarker.Circle,
            FlowchartArrowHead.Cross => EdgeMarker.Cross,
            _ => EdgeMarker.None,
        };
    }
}
