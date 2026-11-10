using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal static class UseCaseDiagramRenderer
{
    private const double FigureWidth = 30;
    private const double FigureHeight = 50;
    private const double LabelGap = 4;
    private const double PaddingX = 12;
    private const double PaddingY = 8;
    private const double CellPaddingX = 8;
    private const double CellPaddingY = 4;

    public static SceneBounds Render(UseCaseDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var model = new GraphModel
        {
            Direction = diagram.Direction.ToLayout(),
            NodeSeparation = double.IsFinite(diagram.NodeSpacing) ? Math.Clamp(diagram.NodeSpacing, 0, 1000) : 50,
            RankSeparation = double.IsFinite(diagram.RankSpacing) ? Math.Clamp(diagram.RankSpacing, 0, 1000) : 50,
        };
        var clusters = new Dictionary<UseCaseDiagramElement, GraphCluster>(ReferenceEqualityComparer.Instance);
        var elements = new List<UseCaseDiagramElement>();
        var seen = new HashSet<UseCaseDiagramElement>(ReferenceEqualityComparer.Instance);
        foreach (var element in diagram.Actors.Cast<UseCaseDiagramElement>().Concat(diagram.UseCases).Concat(diagram.Tables))
        {
            if (seen.Add(element))
            {
                elements.Add(element);
            }
        }

        var boundaryIndex = 0;
        foreach (var boundary in diagram.Boundaries)
        {
            AddBoundary(boundary, boundaryIndex++);
        }

        var nodes = new Dictionary<UseCaseDiagramElement, GraphNode>(ReferenceEqualityComparer.Instance);
        foreach (var element in elements)
        {
            nodes.TryAdd(element, CreateNode(element, clusters.GetValueOrDefault(element)));
        }

        foreach (var relationship in diagram.Relationships)
        {
            var style = ResolveStyle(relationship.CssClasses, relationship.Style, includeDefault: false);
            var color = style.Stroke ?? theme.LineColor;
            var isDashed = relationship.Kind is UseCaseRelationshipKind.Include or UseCaseRelationshipKind.Extend;
            var label = relationship.Kind switch
            {
                UseCaseRelationshipKind.Include => new DiagramText("«include»"),
                UseCaseRelationshipKind.Extend => new DiagramText("«extend»"),
                _ => relationship.Label,
            };
            model.Edges.Add(new GraphEdge
            {
                Source = GetNode(relationship.From),
                Target = GetNode(relationship.To),
                Label = label is { IsEmpty: false } ? context.Text(label, style.FontSize, style.Bold ?? false, style.Italic ?? false) : null,
                MinLength = relationship.Kind is UseCaseRelationshipKind.Association ? Math.Clamp(relationship.MinLength, 1, 100) : 1,
                Color = color,
                MarkerColor = color,
                StrokeWidth = style.StrokeWidth ?? theme.StrokeWidth,
                StrokeDashArray = style.StrokeDashArray ?? (isDashed ? [3, 3] : null),
                StartMarker = relationship.Kind is UseCaseRelationshipKind.Association ? ToMarker(relationship.FromArrowHead) : EdgeMarker.None,
                EndMarker = relationship.Kind switch
                {
                    UseCaseRelationshipKind.Association => ToMarker(relationship.ToArrowHead),
                    UseCaseRelationshipKind.Generalization => EdgeMarker.OpenTriangle,
                    _ => EdgeMarker.Arrow,
                },
                LabelColor = style.TextColor ?? theme.TextColor,
                LabelBackground = theme.EdgeLabelBackground,
                CssClass = StyleHelpers.ToCssClass(relationship.CssClasses, relationship.Kind switch
                {
                    UseCaseRelationshipKind.Include => "include",
                    UseCaseRelationshipKind.Extend => "extend",
                    UseCaseRelationshipKind.Generalization => "generalization",
                    _ => "association",
                }),
                Opacity = style.Opacity ?? 1,
            });
        }

        foreach (var note in diagram.Notes)
        {
            var noteNode = NoteRenderer.CreateNote(model, context, note.Text, cluster: null);
            model.Edges.Add(new GraphEdge
            {
                Source = noteNode,
                Target = GetNode(note.Target),
                Color = theme.LineColor,
                StrokeWidth = theme.StrokeWidth,
                StrokeDashArray = [3, 3],
                CssClass = "note-link",
            });
        }

        return GraphRenderer.Render(model, context, group);

        GraphNode GetNode(UseCaseDiagramElement element)
        {
            if (!nodes.TryGetValue(element, out var node))
            {
                node = CreateNode(element, clusters.GetValueOrDefault(element));
                nodes[element] = node;
            }

            return node;
        }

        void AddBoundary(UseCaseBoundary boundary, int index)
        {
            var style = ResolveStyle(boundary.CssClasses, boundary.Style, includeDefault: false);
            var title = context.Text(boundary.Title ?? boundary.Id, style.FontSize, style.Bold ?? true, style.Italic ?? false);
            var isPackage = boundary.Kind is UseCaseBoundaryKind.Package;
            var titleSpace = title.IsEmpty ? 0 : title.Height + 8;
            var cluster = new GraphCluster
            {
                TitleSpace = isPackage ? titleSpace + 4 : titleSpace,
                TitleWidth = title.IsEmpty ? 0 : title.Width + 16,
            };
            cluster.Draw = (target, bounds) =>
            {
                var boundaryGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(boundary.CssClasses, isPackage ? "boundary package" : "boundary"), Opacity = style.Opacity ?? 1 });
                boundaryGroup.SetDataAttribute("id", boundary.Id);
                var fill = style.Fill ?? theme.ClusterBackground;
                var stroke = style.Stroke ?? theme.ClusterBorderColor;
                var strokeWidth = style.StrokeWidth ?? theme.StrokeWidth;
                var textColor = style.TextColor ?? theme.TitleColor;
                if (isPackage && !title.IsEmpty)
                {
                    // The title is in a tab on the top left of the body
                    var tabWidth = Math.Min(title.Width + 16, bounds.Width);
                    boundaryGroup.Add(new SceneRectangle(bounds.X, bounds.Y, tabWidth, titleSpace) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                    boundaryGroup.Add(new SceneRectangle(bounds.X, bounds.Y + titleSpace, bounds.Width, bounds.Height - titleSpace) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                    title.Draw(boundaryGroup, bounds.X + (tabWidth / 2), bounds.Y + (titleSpace / 2), textColor);
                    return;
                }

                boundaryGroup.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                title.Draw(boundaryGroup, bounds.CenterX, bounds.Y + 4 + (title.Height / 2), textColor);
            };
            model.Clusters.Add(cluster);
            foreach (var element in boundary.Elements)
            {
                if (element is UseCaseJsonTable || clusters.ContainsKey(element))
                    continue;

                clusters[element] = cluster;
                if (seen.Add(element))
                {
                    elements.Add(element);
                }
            }
        }

        GraphNode CreateNode(UseCaseDiagramElement element, GraphCluster? cluster) => element switch
        {
            UseCaseActor actor => CreateActorNode(actor, cluster),
            UseCase useCase => CreateUseCaseNode(useCase, cluster),
            UseCaseJsonTable table => CreateTableNode(table, cluster),
            _ => model.AddNode(1, 1, cluster),
        };

        GraphNode CreateActorNode(UseCaseActor actor, GraphCluster? cluster)
        {
            var style = ResolveStyle(actor.CssClasses, actor.Style, includeDefault: true);
            var fontSize = style.FontSize ?? theme.FontSize;
            var stereotype = string.IsNullOrEmpty(actor.Stereotype) ? null : context.Text("«" + actor.Stereotype + "»", fontSize * 0.875);
            var label = context.Text(actor.Label ?? actor.Id, fontSize, style.Bold ?? false, style.Italic ?? false);
            var width = Math.Max(FigureWidth, Math.Max(label.Width, stereotype?.Width ?? 0));
            var height = FigureHeight + LabelGap + (stereotype?.Height ?? 0) + label.Height;
            var node = model.AddNode(width, height, cluster);
            node.Draw = (target, x, y) =>
            {
                var actorGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(actor.CssClasses, "actor"), Opacity = style.Opacity ?? 1 });
                actorGroup.SetDataAttribute("id", actor.Id);
                var top = y - (height / 2);
                var fill = style.Fill ?? theme.NodeBackground;
                var stroke = style.Stroke ?? theme.NodeBorderColor;
                var strokeWidth = style.StrokeWidth ?? Math.Max(theme.StrokeWidth, 1.5);
                var figure = actorGroup.Add(new SceneGroup { Transform = SceneTransform.Translate(x, top) });
                switch (actor.Kind)
                {
                    case UseCaseActorKind.Hollow:
                        figure.Add(new SceneCircle(0, 7, 7) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth });
                        figure.Add(new ScenePath(Geometry.Polygon((-3, 14), (-14, 18), (-14, 23), (-3, 21), (-12, 48), (-7, 50), (0, 36), (7, 50), (12, 48), (3, 21), (14, 23), (14, 18), (3, 14))) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth });
                        break;
                    case UseCaseActorKind.Awesome:
                        figure.Add(new SceneCircle(0, 8, 8) { Fill = stroke });
                        figure.Add(new ScenePath(new PathBuilder().MoveTo(-14, 50).LineTo(-14, 30).CubicTo(-14, 22, -8, 19, 0, 19).CubicTo(8, 19, 14, 22, 14, 30).LineTo(14, 50).Close().Build()) { Fill = stroke });
                        break;
                    default:
                        figure.Add(new SceneCircle(0, 7, 7) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth });
                        figure.Add(new ScenePath(new PathBuilder()
                            .MoveTo(0, 14).LineTo(0, 33)
                            .MoveTo(-14, 20).LineTo(14, 20)
                            .MoveTo(-11, 50).LineTo(0, 33).LineTo(11, 50)
                            .Build()) { Stroke = stroke, StrokeWidth = strokeWidth });
                        break;
                }

                if (actor.IsBusiness && actor.Kind is not UseCaseActorKind.Awesome)
                {
                    figure.Add(new SceneLine(-6, 12, 6, 2) { Stroke = stroke, StrokeWidth = strokeWidth, CssClass = "business" });
                }

                var textColor = style.TextColor ?? theme.TextColor;
                var cursor = top + FigureHeight + LabelGap;
                if (stereotype is not null)
                {
                    stereotype.Draw(actorGroup, x, cursor + (stereotype.Height / 2), textColor);
                    cursor += stereotype.Height;
                }

                label.Draw(actorGroup, x, cursor + (label.Height / 2), textColor);
            };
            return node;
        }

        GraphNode CreateUseCaseNode(UseCase useCase, GraphCluster? cluster)
        {
            var style = ResolveStyle(useCase.CssClasses, useCase.Style, includeDefault: true);
            var fontSize = style.FontSize ?? theme.FontSize;
            var stereotype = string.IsNullOrEmpty(useCase.Stereotype) ? null : context.Text("«" + useCase.Stereotype + "»", fontSize * 0.875);
            var label = context.Text(useCase.Label ?? useCase.Id, fontSize, style.Bold ?? false, style.Italic ?? false, maxWidth: 200);
            var contentWidth = Math.Max(label.Width, stereotype?.Width ?? 0);
            var contentHeight = label.Height + (stereotype?.Height ?? 0);
            var isEllipse = useCase.Shape is UseCaseShape.Ellipse;
            var width = contentWidth + (2 * PaddingX);
            var height = contentHeight + (2 * PaddingY);
            if (isEllipse)
            {
                // The ellipse around the rectangle of the text
                width *= Math.Sqrt(2);
                height *= Math.Sqrt(2);
            }

            var node = model.AddNode(width, height, cluster);
            if (isEllipse)
            {
                node.Intersect = (center, toward) =>
                {
                    var dx = toward.X - center.X;
                    var dy = toward.Y - center.Y;
                    var scale = Math.Sqrt((dx * dx / (width * width / 4)) + (dy * dy / (height * height / 4)));
                    return scale < 1e-9 ? center : new LayoutPoint(center.X + (dx / scale), center.Y + (dy / scale));
                };
            }

            node.Draw = (target, x, y) =>
            {
                var useCaseGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(useCase.CssClasses, isEllipse ? "usecase" : "usecase rectangle"), Opacity = style.Opacity ?? 1 });
                useCaseGroup.SetDataAttribute("id", useCase.Id);
                var fill = style.Fill ?? theme.NodeBackground;
                var stroke = style.Stroke ?? theme.NodeBorderColor;
                var strokeWidth = style.StrokeWidth ?? theme.StrokeWidth;
                if (isEllipse)
                {
                    useCaseGroup.Add(new ScenePath(Geometry.Ellipse(x, y, width / 2, height / 2)) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                    if (useCase.IsBusiness)
                    {
                        // A slash on the bottom left of the ellipse
                        var a = 120 * Math.PI / 180;
                        var b = 160 * Math.PI / 180;
                        useCaseGroup.Add(new SceneLine(x + (width / 2 * Math.Cos(a)), y + (height / 2 * Math.Sin(a)), x + (width / 2 * Math.Cos(b)), y + (height / 2 * Math.Sin(b))) { Stroke = stroke, StrokeWidth = strokeWidth, CssClass = "business" });
                    }
                }
                else
                {
                    useCaseGroup.Add(new SceneRectangle(x - (width / 2), y - (height / 2), width, height) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                }

                var textColor = style.TextColor ?? theme.PrimaryTextColor;
                var cursor = y - (contentHeight / 2);
                if (stereotype is not null)
                {
                    stereotype.Draw(useCaseGroup, x, cursor + (stereotype.Height / 2), textColor);
                    cursor += stereotype.Height;
                }

                label.Draw(useCaseGroup, x, cursor + (label.Height / 2), textColor);
            };
            return node;
        }

        GraphNode CreateTableNode(UseCaseJsonTable table, GraphCluster? cluster)
        {
            var style = ResolveStyle(table.CssClasses, table.Style, includeDefault: true);
            var fontSize = style.FontSize ?? theme.FontSize;
            var title = context.Text(table.Label ?? table.Id, fontSize, bold: true);
            var rows = JsonTableRows.Create(table.Json).Select(row => (Key: context.Text(row.Key, fontSize * 0.875), Value: context.Text(row.Value, fontSize * 0.875, maxWidth: 300))).ToList();
            var keyWidth = rows.Count is 0 ? 0 : rows.Max(row => row.Key.Width) + (2 * CellPaddingX);
            var valueWidth = rows.Count is 0 ? 0 : rows.Max(row => row.Value.Width) + (2 * CellPaddingX);
            var rowHeights = rows.Select(row => Math.Max(row.Key.Height, row.Value.Height) + (2 * CellPaddingY)).ToList();
            var headerHeight = title.Height + (4 * CellPaddingY);
            var width = Math.Max(keyWidth + valueWidth, title.Width + (2 * PaddingX));
            if (keyWidth + valueWidth > 0 && width > keyWidth + valueWidth)
            {
                valueWidth = width - keyWidth;
            }

            var height = headerHeight + rowHeights.Sum();
            var node = model.AddNode(width, height, cluster);
            node.Draw = (target, x, y) =>
            {
                var tableGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(table.CssClasses, "json-table"), Opacity = style.Opacity ?? 1 });
                tableGroup.SetDataAttribute("id", table.Id);
                var left = x - (width / 2);
                var top = y - (height / 2);
                var stroke = style.Stroke ?? theme.NodeBorderColor;
                var strokeWidth = style.StrokeWidth ?? theme.StrokeWidth;
                var textColor = style.TextColor ?? theme.PrimaryTextColor;
                tableGroup.Add(new SceneRectangle(left, top, width, height) { Fill = style.Fill ?? theme.NodeBackground, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                title.Draw(tableGroup, x, top + (headerHeight / 2), textColor);
                var rowTop = top + headerHeight;
                for (var i = 0; i < rows.Count; i++)
                {
                    var fill = style.Fill ?? (i % 2 == 0 ? theme.RowOddBackground : theme.RowEvenBackground);
                    tableGroup.Add(new SceneRectangle(left, rowTop, keyWidth, rowHeights[i]) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth });
                    tableGroup.Add(new SceneRectangle(left + keyWidth, rowTop, valueWidth, rowHeights[i]) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth });
                    rows[i].Key.Draw(tableGroup, left + CellPaddingX + (rows[i].Key.Width / 2), rowTop + (rowHeights[i] / 2), textColor);
                    rows[i].Value.Draw(tableGroup, left + keyWidth + CellPaddingX + (rows[i].Value.Width / 2), rowTop + (rowHeights[i] / 2), textColor);
                    rowTop += rowHeights[i];
                }
            };
            return node;
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

        static EdgeMarker ToMarker(UseCaseArrowHead head) => head switch
        {
            UseCaseArrowHead.Arrow => EdgeMarker.Arrow,
            UseCaseArrowHead.Circle => EdgeMarker.Circle,
            UseCaseArrowHead.Cross => EdgeMarker.Cross,
            _ => EdgeMarker.None,
        };
    }
}
