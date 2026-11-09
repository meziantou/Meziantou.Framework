namespace Meziantou.Framework.Diagrams.Rendering;

internal static class RequirementDiagramRenderer
{
    private const double Padding = 10;
    private const double Gap = 10;

    public static SceneBounds Render(RequirementDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var model = new GraphModel { Direction = diagram.Direction.ToLayout() };
        var nodes = new Dictionary<RequirementDiagramNode, GraphNode>(ReferenceEqualityComparer.Instance);
        foreach (var requirement in diagram.Requirements)
        {
            nodes.TryAdd(requirement, CreateNode(requirement));
        }

        foreach (var element in diagram.Elements)
        {
            nodes.TryAdd(element, CreateNode(element));
        }

        foreach (var relationship in diagram.Relationships)
        {
            var isContains = relationship.Kind is RequirementRelationshipKind.Contains;
            model.Edges.Add(new GraphEdge
            {
                Source = GetNode(relationship.Source),
                Target = GetNode(relationship.Target),
                Label = context.Text("<<" + FormatKind(relationship.Kind) + ">>"),
                Color = theme.LineColor,
                MarkerColor = theme.LineColor,
                StrokeWidth = theme.StrokeWidth,
                StrokeDashArray = isContains ? null : [10, 7],
                StartMarker = isContains ? EdgeMarker.ContainsCircle : EdgeMarker.None,
                EndMarker = isContains ? EdgeMarker.None : EdgeMarker.OpenArrow,
                LabelColor = theme.TextColor,
                LabelBackground = theme.EdgeLabelBackground,
                CssClass = "relationship " + FormatKind(relationship.Kind),
            });
        }

        return GraphRenderer.Render(model, context, group);

        GraphNode GetNode(RequirementDiagramNode node)
        {
            if (!nodes.TryGetValue(node, out var graphNode))
            {
                graphNode = CreateNode(node);
                nodes[node] = graphNode;
            }

            return graphNode;
        }

        GraphNode CreateNode(RequirementDiagramNode item)
        {
            var style = ResolveStyle(item);
            var fontSize = style.FontSize ?? theme.FontSize;
            var kind = context.Text(item is RequirementDiagramRequirement requirement ? "<<" + FormatKind(requirement.Kind) + ">>" : "<<Element>>", fontSize);
            var name = context.Text(item.Name, fontSize, bold: true);
            var body = new List<TextBlock>();
            if (item is RequirementDiagramRequirement req)
            {
                AddLine("ID: ", req.RequirementId is null ? null : new DiagramText(req.RequirementId));
                AddLine("Text: ", req.Text);
                AddLine("Risk: ", req.Risk is { } risk ? new DiagramText(risk.ToString()) : null);
                AddLine("Verification: ", req.VerificationMethod is { } method ? new DiagramText(method.ToString()) : null);
            }
            else if (item is RequirementDiagramElement element)
            {
                AddLine("Type: ", element.Type);
                AddLine("Doc Ref: ", element.DocumentReference);
            }

            var headerHeight = kind.Height + name.Height + (2 * Padding);
            var bodyHeight = body.Count is 0 ? 0 : body.Sum(line => line.Height) + (2 * Padding);
            var width = new[] { kind.Width, name.Width }.Concat(body.Select(line => line.Width)).Max() + (4 * Padding);
            var height = headerHeight + Math.Max(bodyHeight, Gap);
            var node = model.AddNode(width, height);
            node.Draw = (target, x, y) =>
            {
                var nodeGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(item.CssClasses, item is RequirementDiagramRequirement ? "requirement" : "element"), Opacity = style.Opacity ?? 1 });
                nodeGroup.SetDataAttribute("id", item.Name);
                var left = x - (width / 2);
                var top = y - (height / 2);
                var stroke = style.Stroke ?? theme.NodeBorderColor;
                var strokeWidth = style.StrokeWidth ?? theme.StrokeWidth;
                var textColor = style.TextColor ?? theme.PrimaryTextColor;
                nodeGroup.Add(new SceneRectangle(left, top, width, height) { Fill = style.Fill ?? theme.NodeBackground, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                var cursor = top + Padding;
                kind.Draw(nodeGroup, x, cursor + (kind.Height / 2), textColor);
                cursor += kind.Height;
                name.Draw(nodeGroup, x, cursor + (name.Height / 2), textColor);
                cursor = top + headerHeight;
                nodeGroup.Add(new SceneLine(left, cursor, left + width, cursor) { Stroke = stroke, StrokeWidth = strokeWidth });
                cursor += Padding;
                foreach (var line in body)
                {
                    line.Draw(nodeGroup, x, cursor + (line.Height / 2), textColor);
                    cursor += line.Height;
                }
            };
            return node;

            void AddLine(string prefix, DiagramText? value)
            {
                if (value is null || value.IsEmpty)
                    return;

                var text = value.Format is DiagramTextFormat.Markdown ? DiagramText.Markdown(EscapeMarkdown(prefix) + value.Text) : new DiagramText(prefix + value.Text);
                body.Add(context.Text(text, fontSize * 0.875, maxWidth: 300));
            }
        }

        DiagramStyle ResolveStyle(RequirementDiagramNode node)
        {
            var style = new DiagramStyle();
            if (diagram.ClassDefinitions.TryGetValue("default", out var defaultStyle))
            {
                style = style.Merge(defaultStyle);
            }

            foreach (var name in node.CssClasses)
            {
                if (diagram.ClassDefinitions.TryGetValue(name, out var classStyle))
                {
                    style = style.Merge(classStyle);
                }
            }

            return style.Merge(node.Style);
        }
    }

    private static string EscapeMarkdown(string text) => text.Replace("*", "\\*", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    internal static string FormatKind(RequirementKind kind) => kind switch
    {
        RequirementKind.FunctionalRequirement => "Functional Requirement",
        RequirementKind.InterfaceRequirement => "Interface Requirement",
        RequirementKind.PerformanceRequirement => "Performance Requirement",
        RequirementKind.PhysicalRequirement => "Physical Requirement",
        RequirementKind.DesignConstraint => "Design Constraint",
        _ => "Requirement",
    };

    internal static string FormatKind(RequirementRelationshipKind kind) => kind switch
    {
        RequirementRelationshipKind.Copies => "copies",
        RequirementRelationshipKind.Derives => "derives",
        RequirementRelationshipKind.Satisfies => "satisfies",
        RequirementRelationshipKind.Verifies => "verifies",
        RequirementRelationshipKind.Refines => "refines",
        RequirementRelationshipKind.Traces => "traces",
        _ => "contains",
    };
}
