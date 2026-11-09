using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal static class ClassDiagramRenderer
{
    private const double PaddingX = 10;
    private const double PaddingY = 6;
    private const double EmptyCompartment = 12;
    private const double LollipopRadius = 7;

    public static SceneBounds Render(ClassDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var model = new GraphModel { Direction = diagram.Direction.ToLayout() };
        var clusters = new Dictionary<ClassDiagramNamespace, GraphCluster>(ReferenceEqualityComparer.Instance);
        var classNamespace = new Dictionary<ClassDiagramClass, ClassDiagramNamespace>(ReferenceEqualityComparer.Instance);
        var classes = new List<ClassDiagramClass>(diagram.Classes);
        var seen = new HashSet<ClassDiagramClass>(diagram.Classes, ReferenceEqualityComparer.Instance);
        foreach (var ns in diagram.Namespaces)
        {
            AddNamespace(ns, parent: null, depth: 0);
        }

        var nodes = new Dictionary<ClassDiagramElement, GraphNode>(ReferenceEqualityComparer.Instance);
        foreach (var item in classes)
        {
            if (!nodes.ContainsKey(item))
            {
                nodes[item] = CreateClassNode(item, classNamespace.TryGetValue(item, out var ns) ? clusters[ns] : null);
            }
        }

        foreach (var item in diagram.Interfaces)
        {
            if (!nodes.ContainsKey(item))
            {
                nodes[item] = CreateInterfaceNode(item);
            }
        }

        foreach (var relation in diagram.Relations)
        {
            var from = GetNode(relation.From);
            var to = GetNode(relation.To);
            var label = relation.Label is { IsEmpty: false } text ? context.Text(text) : null;
            model.Edges.Add(new GraphEdge
            {
                Source = from,
                Target = to,
                Label = label,
                Color = theme.LineColor,
                MarkerColor = theme.LineColor,
                StrokeWidth = theme.StrokeWidth,
                StrokeDashArray = relation.IsDashed ? [3, 3] : null,
                StartMarker = ToMarker(relation.FromEnd, relation.From),
                EndMarker = ToMarker(relation.ToEnd, relation.To),
                StartLabel = string.IsNullOrEmpty(relation.FromCardinality) ? null : context.Text(relation.FromCardinality, theme.FontSize * 0.85),
                EndLabel = string.IsNullOrEmpty(relation.ToCardinality) ? null : context.Text(relation.ToCardinality, theme.FontSize * 0.85),
                LabelColor = theme.TextColor,
                LabelBackground = theme.EdgeLabelBackground,
                CssClass = "relation",
            });
        }

        foreach (var note in diagram.Notes)
        {
            var noteNode = NoteRenderer.CreateNote(model, context, note.Text, cluster: note.Class is not null && classNamespace.TryGetValue(note.Class, out var ns) ? clusters[ns] : null);
            if (note.Class is not null)
            {
                model.Edges.Add(new GraphEdge
                {
                    Source = noteNode,
                    Target = GetNode(note.Class),
                    Color = theme.LineColor,
                    StrokeWidth = theme.StrokeWidth,
                    StrokeDashArray = [3, 3],
                    CssClass = "note-link",
                });
            }
        }

        return GraphRenderer.Render(model, context, group);

        GraphNode GetNode(ClassDiagramElement element)
        {
            if (!nodes.TryGetValue(element, out var node))
            {
                node = element is ClassDiagramClass item ? CreateClassNode(item, cluster: null) : CreateInterfaceNode((ClassDiagramInterface)element);
                nodes[element] = node;
            }

            return node;
        }

        void AddNamespace(ClassDiagramNamespace ns, ClassDiagramNamespace? parent, int depth)
        {
            if (clusters.ContainsKey(ns) || depth > 64)
                return;

            var title = context.Text(ns.Label ?? ns.Id, bold: true);
            var cluster = new GraphCluster { Parent = parent is null ? null : clusters[parent], TitleSpace = title.Height + 8 };
            cluster.Draw = (target, bounds) =>
            {
                var clusterGroup = target.Add(new SceneGroup { CssClass = "namespace" });
                clusterGroup.SetDataAttribute("id", ns.Id);
                clusterGroup.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height) { Fill = theme.ClusterBackground, Stroke = theme.ClusterBorderColor, StrokeWidth = theme.StrokeWidth });
                title.Draw(clusterGroup, bounds.CenterX, bounds.Y + 4 + (title.Height / 2), theme.TitleColor);
            };
            clusters[ns] = cluster;
            model.Clusters.Add(cluster);
            foreach (var item in ns.Classes)
            {
                classNamespace[item] = ns;
                if (seen.Add(item))
                {
                    classes.Add(item);
                }
            }

            foreach (var child in ns.Namespaces)
            {
                AddNamespace(child, ns, depth + 1);
            }
        }

        GraphNode CreateClassNode(ClassDiagramClass item, GraphCluster? cluster)
        {
            var style = ResolveStyle(item);
            var fontSize = style.FontSize ?? theme.FontSize;
            var annotations = item.Annotations.Select(annotation => context.Text("«" + annotation + "»", fontSize)).ToList();
            var nameText = item.Label ?? item.Id;
            if (item.GenericType is { Length: > 0 } generic)
            {
                nameText = new DiagramText(nameText.Text + "<" + ClassDiagram.FormatGenerics(generic) + ">", nameText.Format);
            }

            var name = context.Text(nameText, fontSize, bold: true, italic: style.Italic ?? false);
            var attributes = item.Members.Where(member => !member.IsMethod).Select(member => (Member: member, Text: context.Text(member.GetDisplayText(), fontSize, italic: member.IsAbstract))).ToList();
            var methods = item.Members.Where(member => member.IsMethod).Select(member => (Member: member, Text: context.Text(member.GetDisplayText(), fontSize, italic: member.IsAbstract))).ToList();
            var hideMembers = diagram.HideEmptyMembersBox && item.Members.Count is 0;
            var width = new[] { name.Width }.Concat(annotations.Select(text => text.Width)).Concat(attributes.Select(text => text.Text.Width)).Concat(methods.Select(text => text.Text.Width)).Max() + (2 * PaddingX);
            width = Math.Max(width, 60);
            var headerHeight = annotations.Sum(text => text.Height) + name.Height + (2 * PaddingY);
            var attributesHeight = hideMembers ? 0 : attributes.Count is 0 ? EmptyCompartment : attributes.Sum(text => text.Text.Height) + (2 * PaddingY);
            var methodsHeight = hideMembers ? 0 : methods.Count is 0 ? EmptyCompartment : methods.Sum(text => text.Text.Height) + (2 * PaddingY);
            var height = headerHeight + attributesHeight + methodsHeight;
            var node = model.AddNode(width, height, cluster);
            node.Draw = (target, x, y) =>
            {
                var classGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(item.CssClasses, "class"), Opacity = style.Opacity ?? 1, Hyperlink = item.Link, Tooltip = item.Tooltip });
                classGroup.SetDataAttribute("id", item.Id);
                var left = x - (width / 2);
                var top = y - (height / 2);
                var stroke = style.Stroke ?? theme.NodeBorderColor;
                var strokeWidth = style.StrokeWidth ?? theme.StrokeWidth;
                classGroup.Add(new SceneRectangle(left, top, width, height) { Fill = style.Fill ?? theme.NodeBackground, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = style.StrokeDashArray });
                var textColor = style.TextColor ?? theme.PrimaryTextColor;
                var cursor = top + PaddingY;
                foreach (var annotation in annotations)
                {
                    annotation.Draw(classGroup, x, cursor + (annotation.Height / 2), textColor);
                    cursor += annotation.Height;
                }

                name.Draw(classGroup, x, cursor + (name.Height / 2), textColor);
                if (hideMembers)
                    return;

                var attributesTop = top + headerHeight;
                classGroup.Add(new SceneLine(left, attributesTop, left + width, attributesTop) { Stroke = stroke, StrokeWidth = strokeWidth });
                DrawMembers(classGroup, attributes, left, attributesTop + PaddingY, width, textColor);
                var methodsTop = attributesTop + attributesHeight;
                classGroup.Add(new SceneLine(left, methodsTop, left + width, methodsTop) { Stroke = stroke, StrokeWidth = strokeWidth });
                DrawMembers(classGroup, methods, left, methodsTop + PaddingY, width, textColor);
            };
            return node;
        }

        void DrawMembers(SceneGroup target, List<(ClassMember Member, TextBlock Text)> members, double left, double top, double width, SceneColor color)
        {
            var cursor = top;
            foreach (var (member, text) in members)
            {
                var centerX = left + PaddingX + (text.Width / 2);
                text.Draw(target, centerX, cursor + (text.Height / 2), color);
                if (member.IsStatic)
                {
                    // A static member is underlined
                    var underline = cursor + ((text.Height - text.Ascent - text.Descent) / 2) + text.Ascent + 2;
                    target.Add(new SceneLine(left + PaddingX, underline, left + PaddingX + text.Width, underline) { Stroke = color, StrokeWidth = 1 });
                }

                cursor += text.Height;
            }
        }

        GraphNode CreateInterfaceNode(ClassDiagramInterface item)
        {
            var style = ResolveStyle(item);
            var label = context.Text(item.Label ?? item.Id, style.FontSize);
            // The label is on the right of the circle, away from the lines that reach it
            var width = (2 * LollipopRadius) + 4 + label.Width;
            var height = Math.Max(2 * LollipopRadius, label.Height);
            var node = model.AddNode(width, height);
            node.Intersect = (center, toward) =>
            {
                var circle = new LayoutPoint(center.X - (width / 2) + LollipopRadius, center.Y);
                var distance = Geometry.Distance(circle, toward);
                return distance < 1e-9 ? circle : Geometry.MoveToward(circle, toward, LollipopRadius);
            };
            node.Draw = (target, x, y) =>
            {
                var interfaceGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(item.CssClasses, "interface"), Opacity = style.Opacity ?? 1, Hyperlink = item.Link, Tooltip = item.Tooltip });
                interfaceGroup.SetDataAttribute("id", item.Id);
                var left = x - (width / 2);
                interfaceGroup.Add(new SceneCircle(left + LollipopRadius, y, LollipopRadius) { Fill = style.Fill ?? theme.NodeBackground, Stroke = style.Stroke ?? theme.NodeBorderColor, StrokeWidth = style.StrokeWidth ?? theme.StrokeWidth });
                label.Draw(interfaceGroup, left + (2 * LollipopRadius) + 4 + (label.Width / 2), y, style.TextColor ?? theme.TextColor);
            };
            return node;
        }

        DiagramStyle ResolveStyle(ClassDiagramElement element)
        {
            var style = new DiagramStyle();
            if (element is ClassDiagramClass && diagram.ClassDefinitions.TryGetValue("default", out var defaultStyle))
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

        static EdgeMarker ToMarker(ClassRelationEnd end, ClassDiagramElement element) => end switch
        {
            ClassRelationEnd.Arrow => EdgeMarker.Arrow,
            ClassRelationEnd.Inheritance => EdgeMarker.OpenTriangle,
            ClassRelationEnd.Composition => EdgeMarker.FilledDiamond,
            ClassRelationEnd.Aggregation => EdgeMarker.OpenDiamond,
            ClassRelationEnd.Lollipop when element is not ClassDiagramInterface => EdgeMarker.OpenCircle,
            _ => EdgeMarker.None,
        };
    }
}
