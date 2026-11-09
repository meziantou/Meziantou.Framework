using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the block diagrams as Mermaid lays them out: in a group, every item gets the width of the widest one per
/// column it spans and the height of the tallest one, and the items flow left to right on the columns of the group.
/// </summary>
internal static class BlockDiagramRenderer
{
    private const double Padding = 8;
    private const double CompositeTitleGap = 4;

    public static SceneBounds Render(BlockDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var state = new State(diagram, context);
        var root = new Node(null);
        foreach (var item in diagram.Items)
        {
            if (state.Measure(item, depth: 0) is { } child)
            {
                root.Children.Add(child);
            }
        }

        root.Columns = diagram.Columns;
        State.Fit(root);
        State.Position(root, 0, 0);
        var frames = group.Add(new SceneGroup { CssClass = "composites" });
        var blocks = group.Add(new SceneGroup { CssClass = "blocks" });
        foreach (var child in root.Children)
        {
            state.Draw(child, frames, blocks);
        }

        var edges = group.Add(new SceneGroup { CssClass = "edges" });
        foreach (var edge in diagram.Edges)
        {
            state.DrawEdge(edge, edges);
        }

        return state.Bounds.ToBounds();
    }

    private sealed class Node(BlockDiagramItem? item)
    {
        public BlockDiagramItem? Item { get; } = item;

        public List<Node> Children { get; } = [];

        public int? Columns { get; set; }

        public int Span => Item?.Span ?? 1;

        public double Width { get; set; }

        public double Height { get; set; }

        public double NaturalWidth { get; set; }

        public double NaturalHeight { get; set; }

        public double Left { get; set; }

        public double Top { get; set; }

        public double TitleSpace { get; set; }

        public TextBlock? Label { get; set; }

        public DiagramStyle Style { get; set; } = new();

        public LayoutPoint Center => new(Left + (Width / 2), Top + (Height / 2));

        public SceneBounds Bounds => new(Left, Top, Width, Height);

        /// <summary>Gets or sets the shape drawn in the final size of the block, centered on the origin.</summary>
        public ShapeDrawing? Shape { get; set; }
    }

    private sealed class State(BlockDiagram diagram, DiagramRenderContext context)
    {
        private readonly Dictionary<BlockDiagramBlock, Node> _nodes = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<BlockDiagramItem> _measured = new(ReferenceEqualityComparer.Instance);

        public BoundsBuilder Bounds { get; } = new();

        public Node? Measure(BlockDiagramItem item, int depth)
        {
            // An item listed twice, or nested in itself, is drawn once
            if (!_measured.Add(item))
                return null;

            var node = new Node(item);
            switch (item)
            {
                case BlockDiagramComposite composite:
                    node.Style = ResolveStyle(composite, includeDefault: false);
                    node.Columns = composite.Columns;
                    if (composite.Label is { IsEmpty: false } title)
                    {
                        node.Label = context.Text(title, node.Style.FontSize, node.Style.Bold ?? false, node.Style.Italic ?? false);
                        node.TitleSpace = node.Label.Height + CompositeTitleGap;
                    }

                    if (depth < 64)
                    {
                        foreach (var child in composite.Items)
                        {
                            if (Measure(child, depth + 1) is { } childNode)
                            {
                                node.Children.Add(childNode);
                            }
                        }
                    }

                    Fit(node);
                    _nodes[composite] = node;
                    break;
                case BlockDiagramArrow arrow:
                    node.Style = ResolveStyle(arrow, includeDefault: true);
                    node.Label = context.Text(arrow.Label ?? arrow.Id, node.Style.FontSize, node.Style.Bold ?? false, node.Style.Italic ?? false);
                    var headLength = (node.Label.Height + (2 * Shapes.PaddingY)) / 2;
                    node.Width = node.Label.Width + (2 * Shapes.PaddingX) + (CountHorizontal(arrow.Directions) * headLength);
                    node.Height = node.Label.Height + (2 * Shapes.PaddingY) + (CountVertical(arrow.Directions) * headLength);
                    node.NaturalWidth = node.Width;
                    node.NaturalHeight = node.Height;
                    _nodes[arrow] = node;
                    break;
                case BlockDiagramBlock block:
                    node.Style = ResolveStyle(block, includeDefault: true);
                    node.Label = context.Text(block.Label ?? block.Id, node.Style.FontSize, node.Style.Bold ?? false, node.Style.Italic ?? false, maxWidth: 200);
                    var shape = Shapes.Create(block.Shape, node.Label.Width, node.Label.Height);
                    node.Width = shape.Width;
                    node.Height = shape.Height;
                    _nodes[block] = node;
                    break;
            }

            return node;
        }

        /// <summary>Sizes the items of a group to the same unit width and height, and the group around them.</summary>
        public static void Fit(Node group)
        {
            var unitWidth = 0.0;
            var height = 0.0;
            var items = 0;
            foreach (var child in group.Children)
            {
                items += child.Span;
                if (child.Item is BlockDiagramSpace)
                    continue;

                unitWidth = Math.Max(unitWidth, child.Width / child.Span);
                height = Math.Max(height, child.Height);
            }

            foreach (var child in group.Children)
            {
                Resize(child, (unitWidth * child.Span) + (Padding * (child.Span - 1)), height);
            }

            var (columns, rows) = GetGrid(group);
            group.Width = Math.Max((columns * (unitWidth + Padding)) + Padding, group.Label is null ? 0 : group.Label.Width + (2 * Padding));
            group.Height = (rows * (height + Padding)) + Padding + group.TitleSpace;
        }

        /// <summary>Gives an item its final size; a composite block stretches its items to fill it.</summary>
        private static void Resize(Node node, double width, double height)
        {
            if (node.Item is not BlockDiagramComposite)
            {
                node.Width = Math.Max(node.Width, width);
                node.Height = Math.Max(node.Height, height);
                return;
            }

            width = Math.Max(node.Width, width);
            height = Math.Max(node.Height, height);
            if (node.Children.Count > 0 && (width > node.Width || height > node.Height))
            {
                var (columns, rows) = GetGrid(node);
                var unitWidth = (width - (columns * Padding) - Padding) / columns;
                var rowHeight = (height - node.TitleSpace - (rows * Padding) - Padding) / rows;
                foreach (var child in node.Children)
                {
                    Resize(child, (unitWidth * child.Span) + (Padding * (child.Span - 1)), rowHeight);
                }
            }

            node.Width = width;
            node.Height = height;
        }

        private static (int Columns, int Rows) GetGrid(Node group)
        {
            var items = Math.Max(1, group.Children.Sum(child => child.Span));
            var columns = group.Columns is > 0 and var value && value < items ? value : items;
            return (columns, (int)Math.Ceiling(items / (double)columns));
        }

        /// <summary>Places the items of a group, left to right on its columns, a row after the other.</summary>
        public static void Position(Node group, double left, double top)
        {
            group.Left = left;
            group.Top = top;
            var (columns, _) = GetGrid(group);
            var rowHeight = group.Children.Count is 0 ? 0 : group.Children.Max(child => child.Height);
            var position = 0;
            var row = -1;
            var cursor = left + Padding;
            foreach (var child in group.Children)
            {
                var childRow = position / columns;
                if (childRow != row)
                {
                    row = childRow;
                    cursor = left + Padding;
                }

                Position(child, cursor, top + group.TitleSpace + Padding + (row * (rowHeight + Padding)) + ((rowHeight - child.Height) / 2));
                cursor += child.Width + Padding;
                position += Math.Min(child.Span, columns - (position % columns));
            }
        }

        public void Draw(Node node, SceneGroup frames, SceneGroup blocks)
        {
            var theme = context.Theme;
            var style = node.Style;
            switch (node.Item)
            {
                case BlockDiagramComposite composite:
                    {
                        var frame = frames.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(composite.CssClasses, "composite"), Opacity = style.Opacity ?? 1 });
                        frame.SetDataAttribute("id", composite.Id);
                        frame.Add(new SceneRectangle(node.Left, node.Top, node.Width, node.Height) { Fill = style.Fill ?? theme.ClusterBackground, Stroke = style.Stroke ?? theme.ClusterBorderColor, StrokeWidth = style.StrokeWidth ?? theme.StrokeWidth, StrokeDashArray = style.StrokeDashArray });
                        node.Label?.Draw(frame, node.Left + (node.Width / 2), node.Top + CompositeTitleGap + (node.Label.Height / 2), style.TextColor ?? theme.TitleColor);
                        Bounds.Add(node.Bounds);
                        foreach (var child in node.Children)
                        {
                            Draw(child, frames, blocks);
                        }

                        break;
                    }

                case BlockDiagramArrow arrow:
                    {
                        var arrowGroup = blocks.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(arrow.CssClasses, "block-arrow"), Opacity = style.Opacity ?? 1 });
                        arrowGroup.SetDataAttribute("id", arrow.Id);
                        // An arrow stretches along the axes it points to only
                        var horizontal = CountHorizontal(arrow.Directions) > 0;
                        var vertical = CountVertical(arrow.Directions) > 0;
                        var arrowWidth = horizontal || !vertical ? node.Width : node.NaturalWidth;
                        var arrowHeight = vertical || !horizontal ? node.Height : node.NaturalHeight;
                        var shapeGroup = arrowGroup.Add(new SceneGroup { Transform = SceneTransform.Translate(node.Center.X, node.Center.Y) });
                        foreach (var path in CreateArrow(arrow.Directions, arrowWidth, arrowHeight))
                        {
                            shapeGroup.Add(new ScenePath(path) { Fill = style.Fill ?? theme.NodeBackground, Stroke = style.Stroke ?? theme.NodeBorderColor, StrokeWidth = style.StrokeWidth ?? theme.StrokeWidth, StrokeDashArray = style.StrokeDashArray });
                        }

                        node.Label!.Draw(arrowGroup, node.Center.X, node.Center.Y, style.TextColor ?? theme.NodeTextColor);
                        Bounds.Add(node.Bounds);
                        break;
                    }

                case BlockDiagramBlock block:
                    {
                        var label = node.Label!;
                        var natural = Shapes.Create(block.Shape, label.Width, label.Height);
                        // A circle keeps its size: stretched, it would cover its neighbors
                        var shape = block.Shape is FlowchartNodeShape.Circle or FlowchartNodeShape.DoubleCircle
                            ? natural
                            : Shapes.Create(block.Shape, label.Width + Math.Max(0, node.Width - natural.Width), label.Height + Math.Max(0, node.Height - natural.Height));
                        node.Shape = shape;
                        var blockGroup = blocks.Add(new SceneGroup { CssClass = StyleHelpers.ToCssClass(block.CssClasses, "block"), Opacity = style.Opacity ?? 1 });
                        blockGroup.SetDataAttribute("id", block.Id);
                        var stroke = style.Stroke ?? (shape.FillWithLineColor || shape.FilledDecorations.Count > 0 ? theme.LineColor : theme.NodeBorderColor);
                        StyleHelpers.DrawShape(blockGroup, shape, node.Center.X, node.Center.Y, style.Fill ?? theme.NodeBackground, stroke, style.StrokeWidth ?? theme.StrokeWidth, style.StrokeDashArray);
                        if (shape.ShowLabel)
                        {
                            label.Draw(blockGroup, node.Center.X + shape.LabelOffsetX, node.Center.Y + shape.LabelOffsetY, style.TextColor ?? theme.NodeTextColor);
                        }

                        Bounds.Add(new SceneBounds(node.Center.X - (shape.Width / 2), node.Center.Y - (shape.Height / 2), shape.Width, shape.Height));
                        break;
                    }
            }
        }

        public void DrawEdge(BlockDiagramEdge edge, SceneGroup target)
        {
            if (!_nodes.TryGetValue(edge.From, out var from) || !_nodes.TryGetValue(edge.To, out var to) || edge.LineStyle is FlowchartLineStyle.Invisible)
                return;

            var theme = context.Theme;
            var start = Intersect(from, to.Center);
            var end = Intersect(to, from.Center);
            if (!Geometry.IsFinite(start) || !Geometry.IsFinite(end) || Geometry.Distance(start, end) < 1e-6)
                return;

            var startMarker = ToMarker(edge.StartArrowHead);
            var endMarker = ToMarker(edge.EndArrowHead);
            var length = Geometry.Distance(start, end);
            var lineStart = EdgeMarkers.GetSetback(startMarker) < length ? Geometry.MoveToward(start, end, EdgeMarkers.GetSetback(startMarker)) : start;
            var lineEnd = EdgeMarkers.GetSetback(endMarker) < length ? Geometry.MoveToward(end, start, EdgeMarkers.GetSetback(endMarker)) : end;
            var edgeGroup = target.Add(new SceneGroup { CssClass = "edge" });
            edgeGroup.Add(new SceneLine(lineStart.X, lineStart.Y, lineEnd.X, lineEnd.Y)
            {
                Stroke = theme.LineColor,
                StrokeWidth = edge.LineStyle is FlowchartLineStyle.Thick ? 3.5 : theme.StrokeWidth,
                StrokeDashArray = edge.LineStyle is FlowchartLineStyle.Dotted ? [3, 3] : null,
            });
            EdgeMarkers.Draw(edgeGroup, startMarker, start, end, theme.ArrowheadColor, context.Background, theme.StrokeWidth);
            EdgeMarkers.Draw(edgeGroup, endMarker, end, start, theme.ArrowheadColor, context.Background, theme.StrokeWidth);
            Bounds.Add(start);
            Bounds.Add(end);
            if (edge.Label is { IsEmpty: false } text)
            {
                var label = context.Text(text);
                var x = (start.X + end.X) / 2;
                var y = (start.Y + end.Y) / 2;
                edgeGroup.Add(new SceneRectangle(x - (label.Width / 2) - 2, y - (label.Height / 2), label.Width + 4, label.Height) { Fill = theme.EdgeLabelBackground });
                label.Draw(edgeGroup, x, y, theme.TextColor);
                Bounds.Add(new SceneBounds(x - (label.Width / 2), y - (label.Height / 2), label.Width, label.Height));
            }
        }

        private static LayoutPoint Intersect(Node node, LayoutPoint toward) => node.Shape is { } shape && node.Item is not (BlockDiagramComposite or BlockDiagramArrow)
            ? shape.Intersect(node.Center, toward)
            : Geometry.IntersectRectangle(node.Bounds, toward);

        private static EdgeMarker ToMarker(FlowchartArrowHead head) => head switch
        {
            FlowchartArrowHead.Arrow => EdgeMarker.Arrow,
            FlowchartArrowHead.Circle => EdgeMarker.Circle,
            FlowchartArrowHead.Cross => EdgeMarker.Cross,
            _ => EdgeMarker.None,
        };

        private DiagramStyle ResolveStyle(BlockDiagramBlock block, bool includeDefault)
        {
            var style = new DiagramStyle();
            if (includeDefault && diagram.ClassDefinitions.TryGetValue("default", out var defaultStyle))
            {
                style = style.Merge(defaultStyle);
            }

            foreach (var name in block.CssClasses)
            {
                if (diagram.ClassDefinitions.TryGetValue(name, out var classStyle))
                {
                    style = style.Merge(classStyle);
                }
            }

            return style.Merge(block.Style);
        }
    }

    private static int CountHorizontal(BlockArrowDirections directions) => (directions.HasFlag(BlockArrowDirections.Left) ? 1 : 0) + (directions.HasFlag(BlockArrowDirections.Right) ? 1 : 0);

    private static int CountVertical(BlockArrowDirections directions) => (directions.HasFlag(BlockArrowDirections.Up) ? 1 : 0) + (directions.HasFlag(BlockArrowDirections.Down) ? 1 : 0);

    /// <summary>Returns the outline of a block arrow centered on the origin: a shaft with a head at each end it points to.</summary>
    internal static List<PathData> CreateArrow(BlockArrowDirections directions, double width, double height)
    {
        var horizontal = directions & (BlockArrowDirections.Left | BlockArrowDirections.Right);
        var vertical = directions & (BlockArrowDirections.Up | BlockArrowDirections.Down);
        var result = new List<PathData>();
        if (horizontal is BlockArrowDirections.None && vertical is BlockArrowDirections.None)
        {
            result.Add(Geometry.Polygon((-width / 2, -height / 2), (width / 2, -height / 2), (width / 2, height / 2), (-width / 2, height / 2)));
            return result;
        }

        if (horizontal is not BlockArrowDirections.None)
        {
            result.Add(Arrow(width, vertical is BlockArrowDirections.None ? height : height / 2, horizontal.HasFlag(BlockArrowDirections.Left), horizontal.HasFlag(BlockArrowDirections.Right), swap: false));
        }

        if (vertical is not BlockArrowDirections.None)
        {
            result.Add(Arrow(height, horizontal is BlockArrowDirections.None ? width : width / 2, vertical.HasFlag(BlockArrowDirections.Up), vertical.HasFlag(BlockArrowDirections.Down), swap: true));
        }

        return result;

        // An arrow along x, of a length and a thickness, with heads at its start and its end; swapped, it goes along y
        static PathData Arrow(double length, double thickness, bool atStart, bool atEnd, bool swap)
        {
            var half = length / 2;
            var outer = thickness / 2;
            var shaft = outer * 0.6;
            var head = Math.Min(outer, length / 4);
            var points = new List<(double X, double Y)>();
            points.Add(atStart ? (-half + head, -shaft) : (-half, -shaft));
            if (atEnd)
            {
                points.AddRange([(half - head, -shaft), (half - head, -outer), (half, 0), (half - head, outer), (half - head, shaft)]);
            }
            else
            {
                points.AddRange([(half, -shaft), (half, shaft)]);
            }

            if (atStart)
            {
                points.AddRange([(-half + head, shaft), (-half + head, outer), (-half, 0), (-half + head, -outer)]);
            }
            else
            {
                points.Add((-half, shaft));
            }

            return Geometry.Polygon([.. points.Select(point => swap ? (point.Y, point.X) : point)]);
        }
    }
}
