using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the C4 diagrams as Mermaid does: no automatic layout, the elements of each boundary flow in rows in the order
/// they are declared, the nested boundaries after them, and the relationships are straight or slightly curved lines
/// between the borders of the items.
/// </summary>
internal static class C4DiagramRenderer
{
    private const double ElementWidth = 216;
    private const double ElementMinHeight = 60;
    private const double ElementPadding = 20;
    private const double ShapeMargin = 50;
    private const double MarginX = 50;
    private const double MarginY = 10;
    private const double WidthLimit = 1920;
    private const double HeadRadius = 16;

    public static SceneBounds Render(C4Diagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var state = new LayoutState(diagram, context);
        var screen = new RowBounds(diagram.ShapesPerRow);
        screen.SetData(MarginX, MarginX, MarginY, MarginY);
        screen.WidthLimit = WidthLimit;

        // The items in no boundary are in an implicit global boundary, which is not drawn
        var global = new C4Boundary("global", "global");
        foreach (var element in diagram.Elements)
        {
            global.Elements.Add(element);
        }

        foreach (var boundary in diagram.Boundaries)
        {
            global.Boundaries.Add(boundary);
        }

        var boundariesGroup = group.Add(new SceneGroup { CssClass = "boundaries" });
        var elementsGroup = group.Add(new SceneGroup { CssClass = "elements" });
        LayOutBoundaries(state, screen, [global], isGlobal: true, boundariesGroup, elementsGroup, depth: 0);
        var relationshipsGroup = group.Add(new SceneGroup { CssClass = "relationships" });
        DrawRelationships(state, relationshipsGroup);
        return state.Bounds.ToBounds();
    }

    private static void LayOutBoundaries(LayoutState state, RowBounds parent, List<C4Boundary> boundaries, bool isGlobal, SceneGroup boundariesGroup, SceneGroup elementsGroup, int depth)
    {
        var context = state.Context;
        var current = new RowBounds(state.Diagram.ShapesPerRow) { WidthLimit = parent.WidthLimit / Math.Min(state.Diagram.BoundariesPerRow, boundaries.Count) };
        for (var i = 0; i < boundaries.Count; i++)
        {
            var boundary = boundaries[i];
            if (!state.Visited.Add(boundary))
                continue;

            // The header: the name, the type and the description
            var wrapWidth = state.Diagram.Wrap ? current.WidthLimit : (double?)null;
            var label = isGlobal ? null : context.Text(boundary.Label, context.FontSize, bold: true, maxWidth: wrapWidth);
            var typeText = isGlobal ? null : GetBoundaryType(boundary);
            var type = typeText is null ? null : context.Text("[" + typeText + "]", context.FontSize * 0.875, maxWidth: wrapWidth);
            var description = isGlobal || boundary.Description is not { IsEmpty: false } ? null : context.Text(boundary.Description, context.FontSize * 0.75, maxWidth: wrapWidth);
            var y = 0.0;
            var labelY = 0.0;
            var typeY = 0.0;
            var descriptionY = 0.0;
            if (label is not null)
            {
                labelY = y + 8;
                y = labelY + label.Height;
            }

            if (type is not null)
            {
                typeY = y + 5;
                y = typeY + type.Height;
            }

            if (description is not null)
            {
                descriptionY = y + 20;
                y = descriptionY + description.Height;
            }

            if (i == 0 || i % state.Diagram.BoundariesPerRow == 0)
            {
                var x = parent.StartX + MarginX;
                var top = parent.StopY + MarginY + y;
                current.SetData(x, x, top, top);
            }
            else
            {
                var x = current.StopX != current.StartX ? current.StopX + MarginX : current.StartX;
                var top = current.StartY;
                current.SetData(x, x, top, top);
            }

            if (boundary.Elements.Count > 0)
            {
                foreach (var element in boundary.Elements)
                {
                    if (state.Placed.ContainsKey(element))
                        continue;

                    var shape = state.Measure(element);
                    var (shapeX, shapeY) = current.Insert(shape.Width, shape.Height, ShapeMargin);
                    var bounds = new SceneBounds(shapeX, shapeY, shape.Width, shape.Height);
                    state.Placed[element] = bounds;
                    state.Bounds.Add(bounds);
                    DrawElement(elementsGroup, element, shape, bounds);
                }

                current.BumpLastMargin(ShapeMargin);
            }

            if (boundary.Boundaries.Count > 0 && depth < 64)
            {
                LayOutBoundaries(state, current, [.. boundary.Boundaries], isGlobal: false, boundariesGroup, elementsGroup, depth + 1);
            }

            if (!isGlobal)
            {
                var bounds = new SceneBounds(current.StartX, current.StartY, current.StopX - current.StartX, current.StopY - current.StartY);
                state.Placed[boundary] = bounds;
                state.Bounds.Add(bounds);
                DrawBoundary(state, boundariesGroup, boundary, bounds, (label, labelY), (type, typeY), (description, descriptionY));
            }

            parent.StopY = Math.Max(current.StopY + ShapeMargin, parent.StopY);
            parent.StopX = Math.Max(current.StopX + ShapeMargin, parent.StopX);
        }
    }

    private static string? GetBoundaryType(C4Boundary boundary) => boundary.Type ?? boundary.Kind switch
    {
        C4BoundaryKind.Enterprise => "ENTERPRISE",
        C4BoundaryKind.System => "SYSTEM",
        C4BoundaryKind.Container => "CONTAINER",
        _ => null,
    };

    private static void DrawBoundary(LayoutState state, SceneGroup target, C4Boundary boundary, SceneBounds bounds, (TextBlock? Text, double Y) label, (TextBlock? Text, double Y) type, (TextBlock? Text, double Y) description)
    {
        var theme = state.Context.Theme;
        var isNode = boundary.Kind is C4BoundaryKind.DeploymentNode or C4BoundaryKind.DeploymentNodeLeft or C4BoundaryKind.DeploymentNodeRight;
        var boundaryGroup = target.Add(new SceneGroup { CssClass = isNode ? "boundary node" : "boundary", Hyperlink = boundary.Link });
        boundaryGroup.SetDataAttribute("id", boundary.Id);
        boundaryGroup.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height)
        {
            Fill = boundary.Style?.Fill,
            Stroke = boundary.Style?.Stroke ?? theme.LineColor,
            StrokeWidth = 1,
            StrokeDashArray = isNode ? null : [7, 7],
        });
        var color = boundary.Style?.TextColor ?? theme.TextColor;
        foreach (var (text, y) in new[] { label, type, description })
        {
            if (text is null)
                continue;

            var x = boundary.Kind is C4BoundaryKind.DeploymentNodeRight ? bounds.Right - 10 - (text.Width / 2) : bounds.X + 10 + (text.Width / 2);
            if (boundary.Kind is C4BoundaryKind.DeploymentNode)
            {
                x = bounds.CenterX;
            }

            text.Draw(boundaryGroup, x, bounds.Y + y + (text.Height / 2), color);
        }
    }

    private static void DrawElement(SceneGroup target, C4Element element, ElementShape shape, SceneBounds bounds)
    {
        var (fill, stroke) = GetColors(element);
        fill = element.Style?.Fill ?? fill;
        stroke = element.Style?.Stroke ?? stroke;
        var textColor = element.Style?.TextColor ?? SceneColor.White;
        var elementGroup = target.Add(new SceneGroup { CssClass = "c4-" + GetCssKind(element) + (element.IsExternal ? " c4-external" : string.Empty), Hyperlink = element.Link });
        elementGroup.SetDataAttribute("id", element.Id);
        var centerX = bounds.CenterX;
        var bodyTop = bounds.Y + shape.HeadHeight;
        var bodyHeight = bounds.Height - shape.HeadHeight;
        var drawing = element.Kind is C4ElementKind.Person ? null : element.Shape switch
        {
            C4ElementShape.Database => Shapes.Create(FlowchartNodeShape.Cylinder, bounds.Width - (2 * Shapes.PaddingX), bodyHeight - (2 * Shapes.PaddingY) - (2 * shape.CapSize)),
            C4ElementShape.Queue => Shapes.Create(FlowchartNodeShape.HorizontalCylinder, bounds.Width - (2 * Shapes.PaddingX) - (2 * shape.CapSize), bodyHeight - (2 * Shapes.PaddingY)),
            _ => null,
        };
        if (drawing is not null)
        {
            StyleHelpers.DrawShape(elementGroup, drawing, centerX, bodyTop + (bodyHeight / 2), fill, stroke, 1, dashes: null);
        }
        else
        {
            var body = elementGroup.Add(new SceneGroup { Transform = SceneTransform.Translate(centerX, bodyTop + (bodyHeight / 2)) });
            body.Add(new ScenePath(Geometry.RoundedRectangle(bounds.Width, bodyHeight, 12)) { Fill = fill, Stroke = stroke, StrokeWidth = 1 });
            if (element.Kind is C4ElementKind.Person)
            {
                elementGroup.Add(new SceneCircle(centerX, bounds.Y + HeadRadius, HeadRadius) { Fill = fill, Stroke = stroke, StrokeWidth = 1 });
            }
        }

        var cursor = bodyTop + ((bodyHeight - shape.ContentHeight) / 2) + (element.Shape is C4ElementShape.Database && element.Kind is not C4ElementKind.Person ? shape.CapSize : 0);
        foreach (var (text, gap) in shape.Lines)
        {
            cursor += gap;
            text.Draw(elementGroup, centerX, cursor + (text.Height / 2), textColor);
            cursor += text.Height;
        }
    }

    private static string GetCssKind(C4Element element) => element.Kind switch
    {
        C4ElementKind.Person => "person",
        C4ElementKind.Container => "container",
        C4ElementKind.Component => "component",
        _ => "system",
    } + element.Shape switch
    {
        C4ElementShape.Database => "_db",
        C4ElementShape.Queue => "_queue",
        _ => string.Empty,
    };

    /// <summary>Returns the colors of C4 for an element: blue for the system being described, gray for the external ones.</summary>
    internal static (SceneColor Fill, SceneColor Stroke) GetColors(C4Element element) => (element.Kind, element.IsExternal) switch
    {
        (C4ElementKind.Person, false) => ((SceneColor)"#08427B", (SceneColor)"#073B6F"),
        (C4ElementKind.Person, true) => ((SceneColor)"#686868", (SceneColor)"#8A8A8A"),
        (C4ElementKind.Container, false) => ((SceneColor)"#438DD5", (SceneColor)"#3C7FC0"),
        (C4ElementKind.Container, true) => ((SceneColor)"#B3B3B3", (SceneColor)"#A6A6A6"),
        (C4ElementKind.Component, false) => ((SceneColor)"#85BBF0", (SceneColor)"#78A8D8"),
        (C4ElementKind.Component, true) => ((SceneColor)"#CCCCCC", (SceneColor)"#BFBFBF"),
        (_, true) => ((SceneColor)"#999999", (SceneColor)"#8A8A8A"),
        _ => ((SceneColor)"#1168BD", (SceneColor)"#3C7FC0"),
    };

    private static void DrawRelationships(LayoutState state, SceneGroup target)
    {
        var context = state.Context;
        var theme = context.Theme;
        var index = 0;
        foreach (var relationship in state.Diagram.Relationships)
        {
            index++;
            if (!state.Placed.TryGetValue(relationship.From, out var from) || !state.Placed.TryGetValue(relationship.To, out var to))
                continue;

            var start = Geometry.IntersectRectangle(from, new LayoutPoint(to.CenterX, to.CenterY));
            var end = Geometry.IntersectRectangle(to, new LayoutPoint(from.CenterX, from.CenterY));
            if (!Geometry.IsFinite(start) || !Geometry.IsFinite(end))
                continue;

            var color = relationship.Style?.Stroke ?? theme.LineColor;
            var textColor = relationship.Style?.TextColor ?? theme.TextColor;
            var relationshipGroup = target.Add(new SceneGroup { CssClass = "relationship" });
            var startMarker = relationship.Kind is C4RelationshipKind.Bidirectional or C4RelationshipKind.Back ? EdgeMarker.Arrow : EdgeMarker.None;
            var endMarker = relationship.Kind is C4RelationshipKind.Back ? EdgeMarker.None : EdgeMarker.Arrow;

            // As Mermaid, the first relationship is straight and the others are slightly curved
            var control = index == 1 ? new LayoutPoint((start.X + end.X) / 2, (start.Y + end.Y) / 2) : new LayoutPoint(start.X + ((end.X - start.X) / 4), start.Y + ((end.Y - start.Y) / 2));
            var lineStart = Geometry.Distance(start, control) > EdgeMarkers.GetSetback(startMarker) ? Geometry.MoveToward(start, control, EdgeMarkers.GetSetback(startMarker)) : start;
            var lineEnd = Geometry.Distance(end, control) > EdgeMarkers.GetSetback(endMarker) ? Geometry.MoveToward(end, control, EdgeMarkers.GetSetback(endMarker)) : end;
            var path = index == 1
                ? new PathBuilder().MoveTo(lineStart.X, lineStart.Y).LineTo(lineEnd.X, lineEnd.Y).Build()
                : new PathBuilder().MoveTo(lineStart.X, lineStart.Y).QuadraticTo(control.X, control.Y, lineEnd.X, lineEnd.Y).Build();
            relationshipGroup.Add(new ScenePath(path) { Stroke = color, StrokeWidth = 1 });
            EdgeMarkers.Draw(relationshipGroup, startMarker, start, control, color, context.Background, 1);
            EdgeMarkers.Draw(relationshipGroup, endMarker, end, control, color, context.Background, 1);
            state.Bounds.Add(start);
            state.Bounds.Add(end);

            var labelText = state.Diagram.Kind is C4DiagramKind.Dynamic ? new DiagramText(index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + relationship.Label.Text, relationship.Label.Format) : relationship.Label;
            var offsetX = double.IsFinite(relationship.LabelOffsetX) ? Math.Clamp(relationship.LabelOffsetX, -10_000, 10_000) : 0;
            var offsetY = double.IsFinite(relationship.LabelOffsetY) ? Math.Clamp(relationship.LabelOffsetY, -10_000, 10_000) : 0;
            var labelX = Math.Min(start.X, end.X) + (Math.Abs(end.X - start.X) / 2) + offsetX;
            var labelY = Math.Min(start.Y, end.Y) + (Math.Abs(end.Y - start.Y) / 2) + offsetY;
            var label = context.Text(labelText, context.FontSize * 0.75, maxWidth: state.Diagram.Wrap ? ElementWidth : null);
            label.Draw(relationshipGroup, labelX, labelY, textColor);
            state.Bounds.Add(new SceneBounds(labelX - (label.Width / 2), labelY - (label.Height / 2), label.Width, label.Height));
            if (!string.IsNullOrEmpty(relationship.Technology))
            {
                var technology = context.Text("[" + relationship.Technology + "]", context.FontSize * 0.75, italic: true, maxWidth: state.Diagram.Wrap ? ElementWidth : null);
                var technologyY = labelY + (label.Height / 2) + 5 + (technology.Height / 2);
                technology.Draw(relationshipGroup, labelX, technologyY, textColor);
                state.Bounds.Add(new SceneBounds(labelX - (technology.Width / 2), technologyY - (technology.Height / 2), technology.Width, technology.Height));
            }
        }
    }

    private sealed class LayoutState(C4Diagram diagram, DiagramRenderContext context)
    {
        public C4Diagram Diagram { get; } = diagram;

        public DiagramRenderContext Context { get; } = context;

        public BoundsBuilder Bounds { get; } = new();

        public Dictionary<C4Item, SceneBounds> Placed { get; } = new(ReferenceEqualityComparer.Instance);

        public HashSet<C4Boundary> Visited { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Measures an element: its kind and technology, its name and its description, in a box of 216 pixels at least.</summary>
        public ElementShape Measure(C4Element element)
        {
            var fontSize = Context.FontSize;
            var kind = element.Kind switch
            {
                C4ElementKind.Person => "Person",
                C4ElementKind.Container => "Container",
                C4ElementKind.Component => "Component",
                _ => "Software System",
            };
            var textWidth = ElementWidth - (2 * ElementPadding);
            double? wrap = Diagram.Wrap ? textWidth : null;
            var lines = new List<(TextBlock Text, double Gap)>
            {
                (Context.Text("[" + kind + (string.IsNullOrEmpty(element.Technology) ? string.Empty : ": " + element.Technology) + "]", fontSize * 0.75, maxWidth: wrap), 0),
                (Context.Text(element.Label, fontSize * 0.875, bold: true, maxWidth: wrap), 4),
            };
            if (element.Description is { IsEmpty: false } description)
            {
                lines.Add((Context.Text(description, fontSize * 0.75, maxWidth: wrap), 8));
            }

            var contentWidth = lines.Max(line => line.Text.Width);
            var contentHeight = lines.Sum(line => line.Text.Height + line.Gap);
            var isPerson = element.Kind is C4ElementKind.Person;
            var capSize = isPerson ? 0 : element.Shape switch
            {
                C4ElementShape.Database or C4ElementShape.Queue => 10,
                _ => 0,
            };
            var width = Math.Max(ElementWidth, contentWidth + (2 * ElementPadding)) + (element.Shape is C4ElementShape.Queue ? 2 * capSize : 0);
            var bodyHeight = Math.Max(ElementMinHeight, contentHeight + (2 * ElementPadding)) + (element.Shape is C4ElementShape.Database ? 2 * capSize : 0);
            var headHeight = isPerson ? HeadRadius * 1.5 : 0;
            return new ElementShape(width, bodyHeight + headHeight, headHeight, capSize, contentHeight, lines);
        }
    }

    private sealed record ElementShape(double Width, double Height, double HeadHeight, double CapSize, double ContentHeight, List<(TextBlock Text, double Gap)> Lines);

    /// <summary>The rows of a boundary, as the <c>Bounds</c> of the C4 renderer of Mermaid.</summary>
    private sealed class RowBounds(int shapesPerRow)
    {
        private double _nextStartX;
        private double _nextStopX;
        private double _nextStartY;
        private double _nextStopY;
        private int _count;

        public double StartX { get; private set; }

        public double StopX { get; set; }

        public double StartY { get; private set; }

        public double StopY { get; set; }

        public double WidthLimit { get; set; }

        public void SetData(double startX, double stopX, double startY, double stopY)
        {
            _nextStartX = StartX = startX;
            _nextStopX = StopX = stopX;
            _nextStartY = StartY = startY;
            _nextStopY = StopY = stopY;
            _count = 0;
        }

        public (double X, double Y) Insert(double width, double height, double margin)
        {
            _count++;
            var startX = _nextStartX == _nextStopX ? _nextStopX + margin : _nextStopX + (margin * 2);
            var stopX = startX + width;
            var startY = _nextStartY + (margin * 2);
            var stopY = startY + height;
            if (startX >= WidthLimit || stopX >= WidthLimit || _count > shapesPerRow)
            {
                startX = _nextStartX + margin;
                startY = _nextStopY + (margin * 2);
                _nextStopX = stopX = startX + width;
                _nextStartY = _nextStopY;
                _nextStopY = stopY = startY + height;
                _count = 1;
            }

            StartX = Math.Min(StartX, startX);
            StartY = Math.Min(StartY, startY);
            StopX = Math.Max(StopX, stopX);
            StopY = Math.Max(StopY, stopY);
            _nextStartX = Math.Min(_nextStartX, startX);
            _nextStartY = Math.Min(_nextStartY, startY);
            _nextStopX = Math.Max(_nextStopX, stopX);
            _nextStopY = Math.Max(_nextStopY, stopY);
            return (startX, startY);
        }

        public void BumpLastMargin(double margin)
        {
            StopX += margin;
            StopY += margin;
        }
    }
}
