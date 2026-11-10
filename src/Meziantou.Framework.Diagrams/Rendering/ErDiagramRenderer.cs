namespace Meziantou.Framework.Diagrams.Rendering;

internal static class ErDiagramRenderer
{
    private const double NamePadding = 15;
    private const double CellPaddingX = 8;
    private const double CellPaddingY = 4;
    private const double MinEntityWidth = 100;
    private const double MinEntityHeight = 75;

    public static SceneBounds Render(ErDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var model = new GraphModel
        {
            Direction = diagram.Direction.ToLayout(),
            NodeSeparation = double.IsFinite(diagram.NodeSpacing) ? Math.Clamp(diagram.NodeSpacing, 0, 1000) : 140,
            RankSeparation = double.IsFinite(diagram.RankSpacing) ? Math.Clamp(diagram.RankSpacing, 0, 1000) : 80,
        };
        var clusters = new Dictionary<ErSubgraph, GraphCluster>(ReferenceEqualityComparer.Instance);
        var entitySubgraph = new Dictionary<ErEntity, ErSubgraph>(ReferenceEqualityComparer.Instance);
        var entities = new List<ErEntity>(diagram.Entities);
        var seen = new HashSet<ErEntity>(diagram.Entities, ReferenceEqualityComparer.Instance);
        foreach (var subgraph in diagram.Subgraphs)
        {
            AddSubgraph(subgraph, parent: null, depth: 0);
        }

        var nodes = new Dictionary<ErEntity, GraphNode>(ReferenceEqualityComparer.Instance);
        foreach (var entity in entities)
        {
            if (!nodes.ContainsKey(entity))
            {
                nodes[entity] = CreateEntityNode(entity, entitySubgraph.TryGetValue(entity, out var subgraph) ? clusters[subgraph] : null);
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            var (source, sourceCluster) = Resolve(relationship.From);
            var (target, targetCluster) = Resolve(relationship.To);
            if ((source is null && sourceCluster is null) || (target is null && targetCluster is null))
                continue;

            model.Edges.Add(new GraphEdge
            {
                Source = source,
                Target = target,
                SourceCluster = sourceCluster,
                TargetCluster = targetCluster,
                Label = relationship.Label is { IsEmpty: false } text ? context.Text(text) : null,
                Color = theme.LineColor,
                MarkerColor = theme.LineColor,
                StrokeWidth = theme.StrokeWidth,
                StrokeDashArray = relationship.IsIdentifying ? null : [8, 8],
                StartMarker = ToMarker(relationship.FromCardinality),
                EndMarker = ToMarker(relationship.ToCardinality),
                LabelColor = theme.TextColor,
                LabelBackground = theme.EdgeLabelBackground,
                CssClass = relationship.IsIdentifying ? "relationship" : "relationship non-identifying",
            });
        }

        return GraphRenderer.Render(model, context, group);

        (GraphNode? Node, GraphCluster? Cluster) Resolve(ErElement element)
        {
            switch (element)
            {
                case ErSubgraph subgraph:
                    return (null, clusters.GetValueOrDefault(subgraph));
                case ErEntity entity:
                    if (!nodes.TryGetValue(entity, out var node))
                    {
                        node = CreateEntityNode(entity, cluster: null);
                        nodes[entity] = node;
                    }

                    return (node, null);
                default:
                    return (null, null);
            }
        }

        void AddSubgraph(ErSubgraph subgraph, ErSubgraph? parent, int depth)
        {
            if (clusters.ContainsKey(subgraph) || depth > 64)
                return;

            var style = ResolveStyle(subgraph, includeDefault: false);
            var title = context.Text(subgraph.Label ?? subgraph.Id, style.FontSize, style.Bold ?? false, style.Italic ?? false);
            var cluster = new GraphCluster
            {
                Parent = parent is null ? null : clusters[parent],
                TitleSpace = title.IsEmpty ? 0 : title.Height + 8,
                TitleWidth = title.IsEmpty ? 0 : title.Width + 16,
                Direction = subgraph.Direction?.ToLayout(),
            };
            cluster.Draw = (target, bounds) =>
            {
                var clusterGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(subgraph.CssClasses, "cluster"), Opacity = style.Opacity ?? 1 });
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
            foreach (var entity in subgraph.Entities)
            {
                entitySubgraph[entity] = subgraph;
                if (seen.Add(entity))
                {
                    entities.Add(entity);
                }
            }

            foreach (var child in subgraph.Subgraphs)
            {
                AddSubgraph(child, subgraph, depth + 1);
            }
        }

        GraphNode CreateEntityNode(ErEntity entity, GraphCluster? cluster)
        {
            var style = ResolveStyle(entity, includeDefault: true);
            var fontSize = style.FontSize ?? theme.FontSize;
            var name = context.Text(entity.Label ?? entity.Id, fontSize, style.Bold ?? false, style.Italic ?? false);
            var cellFontSize = fontSize * 0.875;
            var hasKeys = entity.Attributes.Any(attribute => attribute.Keys is not ErAttributeKeys.None);
            var hasComments = entity.Attributes.Any(attribute => !string.IsNullOrEmpty(attribute.Comment));
            var rows = entity.Attributes.Select(attribute => new[]
            {
                context.Text(ClassDiagram.FormatGenerics(attribute.Type), cellFontSize),
                context.Text(attribute.Name, cellFontSize),
                hasKeys ? context.Text(FormatKeys(attribute.Keys), cellFontSize) : null,
                hasComments ? context.Text(attribute.Comment, cellFontSize) : null,
            }).ToList();

            var columnWidths = new double[4];
            var rowHeights = new double[rows.Count];
            for (var i = 0; i < rows.Count; i++)
            {
                for (var column = 0; column < 4; column++)
                {
                    if (rows[i][column] is { } cell)
                    {
                        columnWidths[column] = Math.Max(columnWidths[column], cell.Width + (2 * CellPaddingX));
                        rowHeights[i] = Math.Max(rowHeights[i], cell.Height + (2 * CellPaddingY));
                    }
                }
            }

            double width;
            double height;
            var headerHeight = name.Height + (2 * NamePadding);
            if (rows.Count is 0)
            {
                width = Math.Max(name.Width + (2 * NamePadding), MinEntityWidth);
                height = Math.Max(headerHeight, MinEntityHeight);
            }
            else
            {
                var tableWidth = columnWidths.Sum();
                width = Math.Max(Math.Max(tableWidth, name.Width + (2 * NamePadding)), MinEntityWidth);

                // The extra width goes to the columns, in proportion
                if (tableWidth > 0 && width > tableWidth)
                {
                    var factor = width / tableWidth;
                    for (var column = 0; column < 4; column++)
                    {
                        columnWidths[column] *= factor;
                    }
                }

                headerHeight = name.Height + (2 * CellPaddingY * 2);
                height = headerHeight + rowHeights.Sum();
            }

            var node = model.AddNode(width, height, cluster);
            node.Draw = (target, x, y) =>
            {
                var entityGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(entity.CssClasses, "entity"), Opacity = style.Opacity ?? 1 });
                entityGroup.SetDataAttribute("id", entity.Id);
                var left = x - (width / 2);
                var top = y - (height / 2);
                var stroke = style.Stroke ?? theme.NodeBorderColor;
                var strokeWidth = style.StrokeWidth ?? theme.StrokeWidth;
                var textColor = style.TextColor ?? theme.PrimaryTextColor;
                entityGroup.Add(new SceneRectangle(left, top, width, height) { Fill = style.Fill ?? theme.NodeBackground, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                if (rows.Count is 0)
                {
                    name.Draw(entityGroup, x, y, textColor);
                    return;
                }

                name.Draw(entityGroup, x, top + (headerHeight / 2), textColor);
                var rowTop = top + headerHeight;
                for (var i = 0; i < rows.Count; i++)
                {
                    var fill = style.Fill ?? (i % 2 == 0 ? theme.RowOddBackground : theme.RowEvenBackground);
                    var cellLeft = left;
                    for (var column = 0; column < 4; column++)
                    {
                        if (columnWidths[column] <= 0)
                            continue;

                        entityGroup.Add(new SceneRectangle(cellLeft, rowTop, columnWidths[column], rowHeights[i]) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, CssClass = "attribute" });
                        if (rows[i][column] is { IsEmpty: false } cell)
                        {
                            cell.Draw(entityGroup, cellLeft + CellPaddingX + (cell.Width / 2), rowTop + (rowHeights[i] / 2), textColor);
                        }

                        cellLeft += columnWidths[column];
                    }

                    rowTop += rowHeights[i];
                }
            };
            return node;
        }

        DiagramStyle ResolveStyle(ErElement element, bool includeDefault)
        {
            var style = new DiagramStyle();
            if (includeDefault && diagram.ClassDefinitions.TryGetValue("default", out var defaultStyle))
            {
                style = style.Merge(defaultStyle);
            }

            foreach (var name in element.CssClasses)
            {
                if (diagram.ClassDefinitions.TryGetValue(name, out var classStyle))
                {
                    style = style.Merge(classStyle);
                }
            }

            return style.Merge(element.Style);
        }

        static EdgeMarker ToMarker(ErCardinality cardinality) => cardinality switch
        {
            ErCardinality.ZeroOrOne => EdgeMarker.ErZeroOrOne,
            ErCardinality.OneOrMore => EdgeMarker.ErOneOrMore,
            ErCardinality.ZeroOrMore => EdgeMarker.ErZeroOrMore,
            ErCardinality.Parent => EdgeMarker.OpenDiamond,
            _ => EdgeMarker.ErOnlyOne,
        };
    }

    internal static string FormatKeys(ErAttributeKeys keys)
    {
        var parts = new List<string>(3);
        if (keys.HasFlag(ErAttributeKeys.PrimaryKey))
        {
            parts.Add("PK");
        }

        if (keys.HasFlag(ErAttributeKeys.ForeignKey))
        {
            parts.Add("FK");
        }

        if (keys.HasFlag(ErAttributeKeys.UniqueKey))
        {
            parts.Add("UK");
        }

        return string.Join(", ", parts);
    }
}
