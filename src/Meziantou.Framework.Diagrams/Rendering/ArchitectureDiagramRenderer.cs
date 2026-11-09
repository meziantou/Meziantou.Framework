using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the architecture diagrams on a grid, as the spatial map of Mermaid: a breadth-first walk of the edges places
/// each node next to the one it links, on the side the edge says; the groups frame their members, and the edges are
/// lines with right angles between the sides of the nodes.
/// </summary>
internal static class ArchitectureDiagramRenderer
{
    public static SceneBounds Render(ArchitectureDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var iconSize = diagram.IconSize;
        var padding = diagram.GroupPadding;
        var nodes = diagram.EnumerateNodes().ToList();
        foreach (var edge in diagram.Edges)
        {
            foreach (var node in new[] { edge.From, edge.To })
            {
                if (!nodes.Contains(node))
                {
                    nodes.Add(node);
                }
            }
        }

        var titles = new Dictionary<ArchitectureNode, TextBlock>(ReferenceEqualityComparer.Instance);
        var labelHeight = 0.0;
        var labelWidth = 0.0;
        foreach (var node in nodes)
        {
            if (node is ArchitectureService { Title: { IsEmpty: false } title })
            {
                var text = context.Text(title, maxWidth: iconSize * 1.5);
                titles[node] = text;
                labelHeight = Math.Max(labelHeight, text.Height + 4);
                labelWidth = Math.Max(labelWidth, text.Width);
            }
        }

        var cells = PlaceOnGrid(diagram, nodes);
        var gap = diagram.Groups.Count > 0 ? (2 * padding) + 20 : iconSize * 0.75;
        var cellWidth = Math.Max(iconSize, labelWidth) + gap;
        var cellHeight = iconSize + labelHeight + gap;
        var centers = new Dictionary<ArchitectureNode, LayoutPoint>(ReferenceEqualityComparer.Instance);
        foreach (var (node, (x, y)) in cells)
        {
            centers[node] = new LayoutPoint(x * cellWidth, -y * cellHeight);
        }

        var bounds = new BoundsBuilder();

        // The frames of the groups: their members, and their nested groups, with the padding and the title
        var groupBounds = new Dictionary<ArchitectureGroup, SceneBounds>(ReferenceEqualityComparer.Instance);
        var nodeGroup = new Dictionary<ArchitectureNode, ArchitectureGroup>(ReferenceEqualityComparer.Instance);
        var groupTitles = new Dictionary<ArchitectureGroup, TextBlock>(ReferenceEqualityComparer.Instance);
        var groupsLayer = group.Add(new SceneGroup { CssClass = "groups" });
        foreach (var architectureGroup in diagram.EnumerateGroups().Reverse())
        {
            var builder = new BoundsBuilder();
            var hasContent = false;
            foreach (var node in architectureGroup.Nodes)
            {
                nodeGroup.TryAdd(node, architectureGroup);
                if (centers.TryGetValue(node, out var center))
                {
                    builder.Add(NodeBounds(node, center));
                    hasContent = true;
                }
            }

            foreach (var child in architectureGroup.Groups)
            {
                if (groupBounds.TryGetValue(child, out var childBounds))
                {
                    builder.Add(childBounds);
                    hasContent = true;
                }
            }

            if (!hasContent)
                continue;

            var content = builder.ToBounds();
            var title = architectureGroup.Title is { IsEmpty: false } groupTitle ? context.Text(groupTitle, bold: true) : null;
            var titleHeight = Math.Max(title?.Height ?? 0, architectureGroup.Icon is null ? 0 : padding * 0.75);
            var frame = new SceneBounds(content.X - padding, content.Y - padding - titleHeight, content.Width + (2 * padding), content.Height + (2 * padding) + titleHeight);
            groupBounds[architectureGroup] = frame;
            if (title is not null)
            {
                groupTitles[architectureGroup] = title;
            }
        }

        foreach (var architectureGroup in diagram.EnumerateGroups())
        {
            if (!groupBounds.TryGetValue(architectureGroup, out var frame))
                continue;

            var frameGroup = groupsLayer.Add(new SceneGroup { CssClass = "group" });
            frameGroup.SetDataAttribute("id", architectureGroup.Id);
            frameGroup.Add(new SceneRectangle(frame.X, frame.Y, frame.Width, frame.Height) { Fill = theme.ClusterBackground, Stroke = theme.ClusterBorderColor, StrokeWidth = 2, StrokeDashArray = [8, 4] });
            var titleX = frame.X + 4;
            if (architectureGroup.Icon is not null)
            {
                var size = padding * 0.75;
                ArchitectureIcons.Draw(frameGroup, architectureGroup.Icon, frame.X + 4, frame.Y + 4, size, context.Measurer);
                titleX += size + 4;
            }

            if (groupTitles.TryGetValue(architectureGroup, out var title))
            {
                title.Draw(frameGroup, titleX + (title.Width / 2), frame.Y + 4 + (Math.Max(title.Height, architectureGroup.Icon is null ? 0 : padding * 0.75) / 2), theme.TitleColor);
            }

            bounds.Add(frame);
        }

        var nodesLayer = group.Add(new SceneGroup { CssClass = "services" });
        foreach (var node in nodes)
        {
            if (node is not ArchitectureService service || !centers.TryGetValue(node, out var center))
                continue;

            var serviceGroup = nodesLayer.Add(new SceneGroup { CssClass = "service" });
            serviceGroup.SetDataAttribute("id", service.Id);
            var left = center.X - (iconSize / 2);
            var top = center.Y - (iconSize / 2);
            if (service.IconText is { } iconText)
            {
                serviceGroup.Add(new SceneRectangle(left, top, iconSize, iconSize) { Fill = ArchitectureIcons.Background });
                context.Text(iconText, maxWidth: iconSize - 8).Draw(serviceGroup, center.X, center.Y, SceneColor.White);
            }
            else
            {
                ArchitectureIcons.Draw(serviceGroup, service.Icon, left, top, iconSize, context.Measurer);
            }

            if (titles.TryGetValue(service, out var title))
            {
                title.Draw(serviceGroup, center.X, top + iconSize + 4 + (title.Height / 2), theme.TextColor);
            }

            bounds.Add(NodeBounds(service, center));
        }

        var edgesLayer = group.Add(new SceneGroup { CssClass = "edges" });
        foreach (var edge in diagram.Edges)
        {
            if (!centers.TryGetValue(edge.From, out var fromCenter) || !centers.TryGetValue(edge.To, out var toCenter))
                continue;

            var start = Port(edge.From, fromCenter, edge.FromSide, edge.FromGroup);
            var end = Port(edge.To, toCenter, edge.ToSide, edge.ToGroup);
            var points = Route(start, edge.FromSide, end, edge.ToSide);
            var edgeGroup = edgesLayer.Add(new SceneGroup { CssClass = "edge" });
            var builder = new PathBuilder().MoveTo(points[0].X, points[0].Y);
            var arrowLength = iconSize / 8;
            for (var i = 1; i < points.Count; i++)
            {
                var point = points[i];
                if (i == points.Count - 1 && edge.ToArrow && Geometry.Distance(points[i - 1], point) > arrowLength)
                {
                    point = Geometry.MoveToward(point, points[i - 1], EdgeMarkers.GetSetback(EdgeMarker.Arrow));
                }

                builder.LineTo(point.X, point.Y);
            }

            edgeGroup.Add(new ScenePath(builder.Build()) { Stroke = theme.LineColor, StrokeWidth = 2 });
            if (edge.ToArrow && points.Count > 1)
            {
                EdgeMarkers.Draw(edgeGroup, EdgeMarker.Arrow, points[^1], points[^2], theme.LineColor, context.Background, 2);
            }

            if (edge.FromArrow && points.Count > 1)
            {
                EdgeMarkers.Draw(edgeGroup, EdgeMarker.Arrow, points[0], points[1], theme.LineColor, context.Background, 2);
            }

            foreach (var point in points)
            {
                bounds.Add(point);
            }

            if (edge.Label is { IsEmpty: false } text && points.Count > 1)
            {
                var label = context.Text(text);
                var middle = GraphRenderer.Midpoint(points);
                edgeGroup.Add(new SceneRectangle(middle.X - (label.Width / 2) - 2, middle.Y - (label.Height / 2), label.Width + 4, label.Height) { Fill = theme.EdgeLabelBackground });
                label.Draw(edgeGroup, middle.X, middle.Y, theme.TextColor);
                bounds.Add(new SceneBounds(middle.X - (label.Width / 2) - 2, middle.Y - (label.Height / 2), label.Width + 4, label.Height));
            }
        }

        return bounds.ToBounds();

        SceneBounds NodeBounds(ArchitectureNode node, LayoutPoint center)
        {
            if (node is ArchitectureJunction)
                return new SceneBounds(center.X, center.Y, 0, 0);

            var width = Math.Max(iconSize, titles.TryGetValue(node, out var title) ? title.Width : 0);
            var height = iconSize + (titles.TryGetValue(node, out var text) ? text.Height + 4 : 0);
            return new SceneBounds(center.X - (width / 2), center.Y - (iconSize / 2), width, height);
        }

        // The point of a side of a node: the middle of the side of the icon, below the title for the bottom
        LayoutPoint Port(ArchitectureNode node, LayoutPoint center, ArchitectureSide side, bool atGroup)
        {
            if (atGroup && nodeGroup.TryGetValue(node, out var architectureGroup) && groupBounds.TryGetValue(architectureGroup, out var frame))
            {
                return side switch
                {
                    ArchitectureSide.Left => new LayoutPoint(frame.X, center.Y),
                    ArchitectureSide.Right => new LayoutPoint(frame.Right, center.Y),
                    ArchitectureSide.Top => new LayoutPoint(center.X, frame.Y),
                    _ => new LayoutPoint(center.X, frame.Bottom),
                };
            }

            if (node is ArchitectureJunction)
                return center;

            var half = iconSize / 2;
            return side switch
            {
                ArchitectureSide.Left => new LayoutPoint(center.X - half, center.Y),
                ArchitectureSide.Right => new LayoutPoint(center.X + half, center.Y),
                ArchitectureSide.Top => new LayoutPoint(center.X, center.Y - half),
                _ => new LayoutPoint(center.X, center.Y + half + (titles.TryGetValue(node, out var title) ? title.Height + 4 : 0)),
            };
        }
    }

    /// <summary>Routes an edge with right angles: straight when its ends face each other, a bend between a horizontal and a vertical side, two bends otherwise.</summary>
    private static List<LayoutPoint> Route(LayoutPoint start, ArchitectureSide startSide, LayoutPoint end, ArchitectureSide endSide)
    {
        var startHorizontal = startSide is ArchitectureSide.Left or ArchitectureSide.Right;
        var endHorizontal = endSide is ArchitectureSide.Left or ArchitectureSide.Right;
        var points = new List<LayoutPoint> { start };
        if (startHorizontal && endHorizontal)
        {
            if (Math.Abs(start.Y - end.Y) > 1e-6)
            {
                var middle = (start.X + end.X) / 2;
                points.Add(new LayoutPoint(middle, start.Y));
                points.Add(new LayoutPoint(middle, end.Y));
            }
        }
        else if (!startHorizontal && !endHorizontal)
        {
            if (Math.Abs(start.X - end.X) > 1e-6)
            {
                var middle = (start.Y + end.Y) / 2;
                points.Add(new LayoutPoint(start.X, middle));
                points.Add(new LayoutPoint(end.X, middle));
            }
        }
        else if (startHorizontal)
        {
            points.Add(new LayoutPoint(end.X, start.Y));
        }
        else
        {
            points.Add(new LayoutPoint(start.X, end.Y));
        }

        points.Add(end);
        return points;
    }

    /// <summary>
    /// Places the nodes on a grid, y upwards: a breadth-first walk from the first node of each connected part puts a
    /// neighbor one cell away, on the side its edge says; the parts are side by side, then the alignments apply.
    /// </summary>
    internal static Dictionary<ArchitectureNode, (int X, int Y)> PlaceOnGrid(ArchitectureDiagram diagram, List<ArchitectureNode> nodes)
    {
        var adjacency = new Dictionary<ArchitectureNode, List<((ArchitectureSide, ArchitectureSide) Pair, ArchitectureNode Neighbor)>>(ReferenceEqualityComparer.Instance);
        foreach (var node in nodes)
        {
            adjacency[node] = [];
        }

        foreach (var node in nodes)
        {
            var list = adjacency[node];
            foreach (var edge in diagram.Edges)
            {
                (ArchitectureSide, ArchitectureSide) pair;
                ArchitectureNode neighbor;
                if (edge.From == node)
                {
                    pair = (edge.FromSide, edge.ToSide);
                    neighbor = edge.To;
                }
                else if (edge.To == node)
                {
                    pair = (edge.ToSide, edge.FromSide);
                    neighbor = edge.From;
                }
                else
                {
                    continue;
                }

                // A pair of the same sides places nothing; a later edge with the same pair replaces the neighbor
                if (pair.Item1 == pair.Item2 || neighbor == node)
                    continue;

                var index = list.FindIndex(item => item.Pair == pair);
                if (index >= 0)
                {
                    list[index] = (pair, neighbor);
                }
                else
                {
                    list.Add((pair, neighbor));
                }
            }
        }

        var result = new Dictionary<ArchitectureNode, (int X, int Y)>(ReferenceEqualityComparer.Instance);
        var occupied = new HashSet<(int, int)>();
        var offsetX = 0;
        foreach (var start in nodes)
        {
            if (result.ContainsKey(start))
                continue;

            // A connected part, placed on the right of the previous ones
            var part = new Dictionary<ArchitectureNode, (int X, int Y)>(ReferenceEqualityComparer.Instance) { [start] = (0, 0) };
            var partCells = new HashSet<(int, int)> { (0, 0) };
            var queue = new Queue<ArchitectureNode>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                var (x, y) = part[node];
                foreach (var (pair, neighbor) in adjacency[node])
                {
                    if (part.ContainsKey(neighbor) || result.ContainsKey(neighbor))
                        continue;

                    var (dx, dy) = Shift(pair);
                    var cell = (x + dx, y + dy);
                    for (var i = 0; partCells.Contains(cell) && i < 1000; i++)
                    {
                        cell = (cell.Item1 + dx, cell.Item2 + dy);
                    }

                    part[neighbor] = cell;
                    partCells.Add(cell);
                    queue.Enqueue(neighbor);
                }
            }

            var minX = part.Values.Min(cell => cell.X);
            var maxX = part.Values.Max(cell => cell.X);
            var maxY = part.Values.Max(cell => cell.Y);
            foreach (var (node, (x, y)) in part)
            {
                result[node] = (x - minX + offsetX, y - maxY);
            }

            offsetX += maxX - minX + 1;
        }

        foreach (var (node, cell) in result)
        {
            occupied.Add(cell);
        }

        foreach (var alignment in diagram.Alignments)
        {
            ArchitectureNode? previous = null;
            foreach (var member in alignment.Members)
            {
                if (!result.TryGetValue(member, out var cell))
                    continue;

                if (previous is not null)
                {
                    var (previousX, previousY) = result[previous];
                    var target = alignment.IsRow ? (Math.Max(cell.X, previousX + 1), previousY) : (previousX, Math.Min(cell.Y, previousY - 1));
                    occupied.Remove(cell);
                    for (var i = 0; occupied.Contains(target) && i < 1000; i++)
                    {
                        target = alignment.IsRow ? (target.Item1 + 1, target.Item2) : (target.Item1, target.Item2 - 1);
                    }

                    result[member] = target;
                    occupied.Add(target);
                }

                previous = member;
            }
        }

        return result;
    }

    /// <summary>Returns where a neighbor goes, from the side of the node and the side of the neighbor its edge links, y upwards.</summary>
    private static (int X, int Y) Shift((ArchitectureSide Node, ArchitectureSide Neighbor) pair)
    {
        static bool IsHorizontal(ArchitectureSide side) => side is ArchitectureSide.Left or ArchitectureSide.Right;
        var (lhs, rhs) = pair;
        if (IsHorizontal(lhs))
        {
            var x = lhs is ArchitectureSide.Left ? -1 : 1;
            return IsHorizontal(rhs) ? (x, 0) : (x, rhs is ArchitectureSide.Bottom ? 1 : -1);
        }

        var y = lhs is ArchitectureSide.Top ? 1 : -1;
        return IsHorizontal(rhs) ? (rhs is ArchitectureSide.Left ? 1 : -1, y) : (0, y);
    }
}
