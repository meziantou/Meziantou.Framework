using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal static class StateDiagramRenderer
{
    private const double NoteGap = 16;

    public static SceneBounds Render(StateDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var horizontal = diagram.Direction.IsHorizontal();
        var model = new GraphModel { Direction = diagram.Direction.ToLayout() };
        var clusters = new Dictionary<StateDiagramState, GraphCluster>(ReferenceEqualityComparer.Instance);
        var nodes = new Dictionary<StateDiagramState, GraphNode>(ReferenceEqualityComparer.Instance);
        var notesOf = new Dictionary<StateDiagramState, List<StateNote>>(ReferenceEqualityComparer.Instance);
        foreach (var note in diagram.Notes)
        {
            if (!notesOf.TryGetValue(note.State, out var list))
            {
                notesOf[note.State] = list = [];
            }

            list.Add(note);
        }
        foreach (var state in diagram.States)
        {
            AddState(state, parent: null, depth: 0);
        }

        foreach (var transition in diagram.Transitions)
        {
            var (from, fromCluster) = Resolve(transition.From);
            var (to, toCluster) = Resolve(transition.To);
            model.Edges.Add(new GraphEdge
            {
                Source = from,
                SourceCluster = fromCluster,
                Target = to,
                TargetCluster = toCluster,
                Label = transition.Label is { IsEmpty: false } label ? context.Text(label) : null,
                Color = theme.LineColor,
                MarkerColor = theme.LineColor,
                StrokeWidth = theme.StrokeWidth,
                EndMarker = EdgeMarker.Arrow,
                LabelColor = theme.TextColor,
                LabelBackground = theme.EdgeLabelBackground,
                CssClass = "transition",
            });
        }

        // The notes of a composite state are nodes next to it
        foreach (var (state, notes) in notesOf)
        {
            if (!state.IsComposite || !clusters.TryGetValue(state, out var cluster))
                continue;

            foreach (var note in notes)
            {
                var noteNode = NoteRenderer.CreateNote(model, context, note.Text, cluster.Parent);
                model.Edges.Add(new GraphEdge { Source = noteNode, TargetCluster = cluster, Color = theme.LineColor, StrokeWidth = theme.StrokeWidth, StrokeDashArray = [3, 3], CssClass = "note-link" });
            }
        }

        return GraphRenderer.Render(model, context, group);

        (GraphNode? Node, GraphCluster? Cluster) Resolve(StateDiagramState state)
        {
            if (clusters.TryGetValue(state, out var cluster))
                return (null, cluster);

            if (!nodes.TryGetValue(state, out var node))
            {
                node = CreateNode(state, cluster: null);
                nodes[state] = node;
            }

            return (node, null);
        }

        void AddState(StateDiagramState state, GraphCluster? parent, int depth)
        {
            if (nodes.ContainsKey(state) || clusters.ContainsKey(state))
                return;

            if (!state.IsComposite || depth > 64)
            {
                nodes[state] = CreateNode(state, parent);
                return;
            }

            var style = ResolveStyle(state);
            var title = context.Text(state.Label ?? state.Id, style.FontSize, style.Bold ?? false, style.Italic ?? false);
            var header = title.Height + 12;
            var cluster = new GraphCluster { Parent = parent, TitleSpace = header, Direction = state.Direction?.ToLayout() };
            cluster.Draw = (target, bounds) =>
            {
                var stateGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(state.CssClasses, "composite-state"), Opacity = style.Opacity ?? 1 });
                stateGroup.SetDataAttribute("id", state.Id);
                var stroke = style.Stroke ?? theme.NodeBorderColor;
                stateGroup.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height) { Fill = style.Fill ?? theme.CompositeTitleBackground, Stroke = stroke, StrokeWidth = style.StrokeWidth ?? theme.StrokeWidth, CornerRadius = 5 });
                stateGroup.Add(new SceneRectangle(bounds.X + 1, bounds.Y + header, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - header - 1)) { Fill = theme.CompositeBackground });
                stateGroup.Add(new SceneLine(bounds.X, bounds.Y + header, bounds.Right, bounds.Y + header) { Stroke = stroke, StrokeWidth = theme.StrokeWidth });
                title.Draw(stateGroup, bounds.CenterX, bounds.Y + (header / 2), style.TextColor ?? theme.NodeTextColor);
            };
            clusters[state] = cluster;
            model.Clusters.Add(cluster);
            if (state.Regions.Count == 1)
            {
                foreach (var child in state.Regions[0].States)
                {
                    AddState(child, cluster, depth + 1);
                }

                return;
            }

            // Concurrent regions: dashed frames side by side
            foreach (var region in state.Regions)
            {
                var regionCluster = new GraphCluster { Parent = cluster, TitleSpace = 0 };
                regionCluster.Draw = (target, bounds) => target.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height) { Stroke = theme.NodeBorderColor, StrokeWidth = theme.StrokeWidth, StrokeDashArray = [5, 5], CssClass = "region" });
                model.Clusters.Add(regionCluster);
                foreach (var child in region.States)
                {
                    AddState(child, regionCluster, depth + 1);
                }
            }
        }

        GraphNode CreateNode(StateDiagramState state, GraphCluster? cluster)
        {
            var style = ResolveStyle(state);
            var label = context.Text(state.Label ?? state.Id, style.FontSize, style.Bold ?? false, style.Italic ?? false, maxWidth: 200);
            var shape = state.Kind switch
            {
                StateKind.Start => new ShapeDrawing { Width = 14, Height = 14, Outline = Geometry.Ellipse(0, 0, 7, 7), ShowLabel = false },
                StateKind.End => new ShapeDrawing { Width = 14, Height = 14, Outline = Geometry.Ellipse(0, 0, 7, 7), ShowLabel = false },
                StateKind.Choice => new ShapeDrawing { Width = 28, Height = 28, Outline = Geometry.Polygon((0, -14), (14, 0), (0, 14), (-14, 0)), ShowLabel = false },
                StateKind.Fork or StateKind.Join => horizontal
                    ? new ShapeDrawing { Width = 10, Height = 70, Outline = Shapes.Rectangle(10, 70), ShowLabel = false }
                    : new ShapeDrawing { Width = 70, Height = 10, Outline = Shapes.Rectangle(70, 10), ShowLabel = false },
                _ => Shapes.Create(FlowchartNodeShape.RoundedRectangle, Math.Max(label.Width, 30), label.Height),
            };

            var notes = notesOf.TryGetValue(state, out var stateNotes) ? stateNotes : [];
            var noteBlocks = notes.Select(note => (Note: note, Text: context.Text(note.Text, maxWidth: 200))).ToList();
            var leftWidth = noteBlocks.Where(item => item.Note.Position is StateNotePosition.Left).Select(item => NoteRenderer.Measure(item.Text).Width + NoteGap).DefaultIfEmpty(0).Max();
            var rightWidth = noteBlocks.Where(item => item.Note.Position is StateNotePosition.Right).Select(item => NoteRenderer.Measure(item.Text).Width + NoteGap).DefaultIfEmpty(0).Max();
            var height = noteBlocks.Select(item => NoteRenderer.Measure(item.Text).Height).Append(shape.Height).Max();
            var width = leftWidth + shape.Width + rightWidth;
            var node = model.AddNode(width, height, cluster);

            // The state is offset from the center of the node by its notes
            var offset = (leftWidth - rightWidth) / 2;
            node.Intersect = (center, toward) => shape.Intersect(new LayoutPoint(center.X + offset, center.Y), toward);
            node.Draw = (target, x, y) =>
            {
                var stateGroup = target.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(state.CssClasses, "state"), Opacity = style.Opacity ?? 1 });
                stateGroup.SetDataAttribute("id", state.Id);
                var stateX = x + offset;
                switch (state.Kind)
                {
                    case StateKind.Start:
                        stateGroup.Add(new SceneCircle(stateX, y, 7) { Fill = style.Fill ?? theme.SpecialStateColor, Stroke = style.Stroke ?? theme.SpecialStateColor });
                        break;
                    case StateKind.End:
                        stateGroup.Add(new SceneCircle(stateX, y, 7) { Fill = style.Fill ?? theme.InnerEndBackground, Stroke = style.Stroke ?? theme.SpecialStateColor, StrokeWidth = 1.5 });
                        stateGroup.Add(new SceneCircle(stateX, y, 4.5) { Fill = theme.SpecialStateColor });
                        break;
                    case StateKind.Fork or StateKind.Join:
                        StyleHelpers.DrawShape(stateGroup, shape, stateX, y, style.Fill ?? theme.SpecialStateColor, style.Stroke ?? theme.SpecialStateColor, theme.StrokeWidth, dashes: null);
                        break;
                    default:
                        StyleHelpers.DrawShape(stateGroup, shape, stateX, y, style.Fill ?? theme.NodeBackground, style.Stroke ?? theme.NodeBorderColor, style.StrokeWidth ?? theme.StrokeWidth, style.StrokeDashArray);
                        if (state.Kind is StateKind.Normal)
                        {
                            label.Draw(stateGroup, stateX, y, style.TextColor ?? theme.NodeTextColor);
                        }

                        break;
                }

                foreach (var (note, text) in noteBlocks)
                {
                    var (noteWidth, _) = NoteRenderer.Measure(text);
                    var noteX = note.Position is StateNotePosition.Left ? stateX - (shape.Width / 2) - NoteGap - (noteWidth / 2) : stateX + (shape.Width / 2) + NoteGap + (noteWidth / 2);
                    var edgeX = note.Position is StateNotePosition.Left ? stateX - (shape.Width / 2) : stateX + (shape.Width / 2);
                    var noteEdgeX = note.Position is StateNotePosition.Left ? noteX + (noteWidth / 2) : noteX - (noteWidth / 2);
                    stateGroup.Add(new SceneLine(edgeX, y, noteEdgeX, y) { Stroke = theme.LineColor, StrokeWidth = theme.StrokeWidth, StrokeDashArray = [3, 3] });
                    NoteRenderer.Draw(stateGroup, context, text, noteX, y);
                }
            };
            return node;
        }

        DiagramStyle ResolveStyle(StateDiagramState state)
        {
            var style = new DiagramStyle();
            if (state.Kind is StateKind.Normal && diagram.ClassDefinitions.TryGetValue("default", out var defaultStyle))
            {
                style = style.Merge(defaultStyle);
            }

            foreach (var name in state.CssClasses)
            {
                if (diagram.ClassDefinitions.TryGetValue(name, out var classStyle))
                {
                    style = style.Merge(classStyle);
                }
            }

            return style.Merge(state.Style);
        }
    }
}
