using System.Globalization;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the Wardley maps as Mermaid does: the axes with the stages of evolution, the pipelines, the links, the
/// movements, the components with their sourcing and inertia, the annotations, the notes and the forces, on a canvas of
/// the size of the map. The elements are merged by name as in Mermaid: an anchor and a component of the same name are
/// one node, the last one gives its position.
/// </summary>
internal static class WardleyDiagramRenderer
{
    private static readonly string[] DefaultStages = ["Genesis", "Custom Built", "Product", "Commodity"];

    public static SceneBounds Render(WardleyDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var width = diagram.Width;
        var height = diagram.Height;
        var padding = diagram.MapPadding;
        var radius = diagram.NodeRadius;
        var squareSize = radius * 1.6;
        var colors = new Colors(context);
        var chartWidth = width - (padding * 2);
        var chartHeight = height - (padding * 2);
        double ProjectX(double value) => padding + (value / 100 * chartWidth);
        double ProjectY(double value) => height - padding - (value / 100 * chartHeight);

        var root = group.Add(new SceneGroup { CssClass = "wardley-map", Clip = new SceneBounds(0, 0, width, height) });
        root.Add(new SceneRectangle(0, 0, width, height) { Fill = colors.Background, CssClass = "wardley-background" });

        DrawAxes(diagram, context, root, colors, chartWidth, chartHeight);

        var model = WardleyRenderModel.Create(diagram);
        var positions = new Dictionary<string, Position>(StringComparer.Ordinal);
        foreach (var node in model.Nodes)
        {
            positions[node.Id] = new Position(ProjectX(node.X), ProjectY(node.Y));
        }

        DrawPipelines(model, root, colors, positions, radius, squareSize);
        DrawLinks(diagram, context, model, root, colors, positions, radius, squareSize);

        var trendGroup = root.Add(new SceneGroup { CssClass = "wardley-trends" });
        foreach (var trend in model.Trends)
        {
            var origin = positions[trend.NodeId];
            var targetX = ProjectX(trend.TargetX);
            var targetY = ProjectY(trend.TargetY);
            var dx = targetX - origin.X;
            var dy = targetY - origin.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            var shortenBy = radius + 2;
            var endX = distance > shortenBy ? targetX - (dx / distance * shortenBy) : targetX;
            var endY = distance > shortenBy ? targetY - (dy / distance * shortenBy) : targetY;
            trendGroup.Add(new SceneLine(origin.X, origin.Y, endX, endY) { Stroke = colors.Evolution, StrokeWidth = 1, StrokeDashArray = [4, 4], CssClass = "wardley-trend" });
            AddArrowhead(trendGroup, origin.X, origin.Y, endX, endY, scale: 0.6, colors.Evolution);
        }

        DrawNodes(diagram, context, model, root, colors, positions, radius, squareSize);
        DrawAnnotations(diagram, context, root, colors, ProjectX, ProjectY);

        if (diagram.Notes.Count > 0)
        {
            var notesGroup = root.Add(new SceneGroup { CssClass = "wardley-notes" });
            foreach (var note in diagram.Notes)
            {
                DrawText(context, notesGroup, note.Text, ProjectX(note.Evolution * 100), ProjectY(note.Visibility * 100), 11, bold: true, colors.AxisText, SceneTextAnchor.Start, middle: false);
            }
        }

        DrawForces(context, root, colors, diagram.Accelerators, "wardley-accelerators", pointsRight: true, ProjectX, ProjectY);
        DrawForces(context, root, colors, diagram.Deaccelerators, "wardley-deaccelerators", pointsRight: false, ProjectX, ProjectY);
        return new SceneBounds(0, 0, width, height);
    }

    private static void DrawAxes(WardleyDiagram diagram, DiagramRenderContext context, SceneGroup root, Colors colors, double chartWidth, double chartHeight)
    {
        var width = diagram.Width;
        var height = diagram.Height;
        var padding = diagram.MapPadding;
        var axisGroup = root.Add(new SceneGroup { CssClass = "wardley-axes" });
        axisGroup.Add(new SceneLine(padding, height - padding, width - padding, height - padding) { Stroke = colors.Axis, StrokeWidth = 1 });
        axisGroup.Add(new SceneLine(padding, padding, padding, height - padding) { Stroke = colors.Axis, StrokeWidth = 1 });
        DrawText(context, axisGroup, "Evolution", padding + (chartWidth / 2), height - (padding / 4), diagram.AxisFontSize, bold: true, colors.AxisText, SceneTextAnchor.Middle, middle: false);
        var yLabelX = padding / 3;
        var yLabelY = padding + (chartHeight / 2);
        var rotated = axisGroup.Add(new SceneGroup { Transform = SceneTransform.Rotate(-90, yLabelX, yLabelY) });
        DrawText(context, rotated, "Visibility", yLabelX, yLabelY, diagram.AxisFontSize, bold: true, colors.AxisText, SceneTextAnchor.Middle, middle: false);

        var stages = diagram.Stages.Count > 0 ? diagram.Stages.Select(stage => stage.Label).ToArray() : DefaultStages;
        var boundaries = diagram.Stages.Where(stage => stage.Boundary is not null).Select(stage => stage.Boundary!.Value).ToArray();
        var stageGroup = root.Add(new SceneGroup { CssClass = "wardley-stages" });
        var stageFontSize = Math.Max(1, diagram.AxisFontSize - 2);
        var previous = 0.0;
        for (var i = 0; i < stages.Length; i++)
        {
            // The boundaries are used when every stage has one; otherwise the stages share the axis
            double start;
            double end;
            if (boundaries.Length == stages.Length)
            {
                start = previous;
                end = boundaries[i];
                previous = end;
            }
            else
            {
                start = i / (double)stages.Length;
                end = (i + 1) / (double)stages.Length;
            }

            var startX = padding + (start * chartWidth);
            var endX = padding + (end * chartWidth);
            if (!double.IsFinite(startX) || !double.IsFinite(endX))
                continue;

            if (i > 0)
            {
                stageGroup.Add(new SceneLine(startX, padding, startX, height - padding) { Stroke = colors.StageDivider, StrokeWidth = 1, StrokeDashArray = [5, 5], Opacity = 0.8 });
            }

            DrawText(context, stageGroup, stages[i], (startX + endX) / 2, height - (padding / 1.5), stageFontSize, bold: false, colors.AxisText, SceneTextAnchor.Middle, middle: false);
        }

        if (diagram.ShowGrid)
        {
            var gridGroup = root.Add(new SceneGroup { CssClass = "wardley-grid" });
            for (var i = 1; i < 4; i++)
            {
                var ratio = i / 4.0;
                var x = padding + (chartWidth * ratio);
                var y = height - padding - (chartHeight * ratio);
                gridGroup.Add(new SceneLine(x, padding, x, height - padding) { Stroke = colors.Grid, StrokeWidth = 1, StrokeDashArray = [2, 6] });
                gridGroup.Add(new SceneLine(padding, y, width - padding, y) { Stroke = colors.Grid, StrokeWidth = 1, StrokeDashArray = [2, 6] });
            }
        }
    }

    private static void DrawPipelines(WardleyRenderModel model, SceneGroup root, Colors colors, Dictionary<string, Position> positions, double radius, double squareSize)
    {
        if (model.Pipelines.Count is 0)
            return;

        var boxGroup = root.Add(new SceneGroup { CssClass = "wardley-pipelines" });
        var linkGroup = root.Add(new SceneGroup { CssClass = "wardley-pipeline-links" });
        foreach (var pipeline in model.Pipelines)
        {
            if (pipeline.ComponentIds.Count is 0)
                continue;

            // The components are joined from the least to the most evolved
            var sorted = pipeline.ComponentIds
                .Where(positions.ContainsKey)
                .Select(id => (Position: positions[id], Node: model.NodesById[id]))
                .OrderBy(component => component.Node.X)
                .ToList();
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                linkGroup.Add(new SceneLine(sorted[i].Position.X, sorted[i].Position.Y, sorted[i + 1].Position.X, sorted[i + 1].Position.Y) { Stroke = colors.Link, StrokeWidth = 1, StrokeDashArray = [4, 4], CssClass = "wardley-pipeline-evolution-link" });
            }

            var minX = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var y = 0.0;
            foreach (var id in pipeline.ComponentIds)
            {
                if (positions.TryGetValue(id, out var position))
                {
                    minX = Math.Min(minX, position.X);
                    maxX = Math.Max(maxX, position.X);
                    y = position.Y;
                }
            }

            if (double.IsInfinity(minX))
                continue;

            const double BoxPadding = 15;
            var boxHeight = radius * 4;
            var boxTop = y - (boxHeight / 2);

            // The parent moves above the box: two thirds of its square outside, one third inside
            if (positions.TryGetValue(pipeline.ParentId, out var parent))
            {
                parent.X = (minX + maxX) / 2;
                parent.Y = boxTop - (squareSize / 6);
            }

            boxGroup.Add(new SceneRectangle(minX - BoxPadding, boxTop, maxX - minX + (BoxPadding * 2), boxHeight) { Stroke = colors.Axis, StrokeWidth = 1.5, CornerRadius = 4, CssClass = "wardley-pipeline-box" });
        }
    }

    private static void DrawLinks(WardleyDiagram diagram, DiagramRenderContext context, WardleyRenderModel model, SceneGroup root, Colors colors, Dictionary<string, Position> positions, double radius, double squareSize)
    {
        var linksGroup = root.Add(new SceneGroup { CssClass = "wardley-links" });
        var drawn = new List<(WardleyRenderLink Link, Position Source, Position Target)>();
        foreach (var link in model.Links)
        {
            if (!positions.TryGetValue(link.SourceId, out var source) || !positions.TryGetValue(link.TargetId, out var target))
                continue;

            // A link from a component of a pipeline to its parent is the pipeline itself
            if (model.PipelinesByParent.TryGetValue(link.TargetId, out var pipeline) && pipeline.ComponentIds.Contains(link.SourceId, StringComparer.Ordinal))
                continue;

            var dx = target.X - source.X;
            var dy = target.Y - source.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            // Mermaid draws no line between two nodes at the same place
            if (!(distance > 0))
                continue;

            var sourceRadius = model.NodesById[link.SourceId].IsPipelineParent ? squareSize / Math.Sqrt(2) : radius;
            var targetRadius = model.NodesById[link.TargetId].IsPipelineParent ? squareSize / Math.Sqrt(2) : radius;
            var x1 = source.X + (dx / distance * sourceRadius);
            var y1 = source.Y + (dy / distance * sourceRadius);
            var x2 = target.X - (dx / distance * targetRadius);
            var y2 = target.Y - (dy / distance * targetRadius);
            linksGroup.Add(new SceneLine(x1, y1, x2, y2)
            {
                Stroke = colors.Link,
                StrokeWidth = 1,
                StrokeDashArray = link.IsDashed ? [4, 4] : null,
                CssClass = link.IsDashed ? "wardley-link wardley-link--dashed" : "wardley-link",
            });
            if (link.Flow is WardleyFlow.Forward or WardleyFlow.Bidirectional)
            {
                AddArrowhead(linksGroup, x1, y1, x2, y2, scale: 0.5, colors.Link);
            }

            if (link.Flow is WardleyFlow.Backward or WardleyFlow.Bidirectional)
            {
                AddArrowhead(linksGroup, x2, y2, x1, y1, scale: 0.5, colors.Link);
            }

            drawn.Add((link, source, target));
        }

        foreach (var (link, source, target) in drawn)
        {
            if (string.IsNullOrEmpty(link.Label))
                continue;

            // The label is above the middle of the line, along the line, never upside down
            var dx = target.X - source.X;
            var dy = target.Y - source.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            const double Offset = 8;
            var labelX = ((source.X + target.X) / 2) + (dy / distance * Offset);
            var labelY = ((source.Y + target.Y) / 2) - (dx / distance * Offset);
            var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angle is > 90 or < -90)
            {
                angle += 180;
            }

            var labelGroup = linksGroup.Add(new SceneGroup { CssClass = "wardley-link-label", Transform = SceneTransform.Rotate(angle, labelX, labelY) });
            DrawText(context, labelGroup, link.Label, labelX, labelY, diagram.LabelFontSize, bold: false, colors.AxisText, SceneTextAnchor.Middle, middle: true);
        }
    }

    private static void DrawNodes(WardleyDiagram diagram, DiagramRenderContext context, WardleyRenderModel model, SceneGroup root, Colors colors, Dictionary<string, Position> positions, double radius, double squareSize)
    {
        var nodesGroup = root.Add(new SceneGroup { CssClass = "wardley-nodes" });
        foreach (var node in model.Nodes)
        {
            var position = positions[node.Id];
            var x = position.X;
            var y = position.Y;
            var nodeGroup = nodesGroup.Add(new SceneGroup { CssClass = "wardley-node wardley-node--" + node.ClassName });

            // The circle of the sourcing is behind the component
            switch (node.Sourcing)
            {
                case WardleySourcing.Outsource:
                    nodeGroup.Add(new SceneCircle(x, y, radius * 2) { Fill = SceneColor.Parse("#666666"), Stroke = colors.ComponentStroke, StrokeWidth = 1, CssClass = "wardley-outsource-overlay" });
                    break;
                case WardleySourcing.Buy:
                    nodeGroup.Add(new SceneCircle(x, y, radius * 2) { Fill = SceneColor.Parse("#cccccc"), Stroke = colors.ComponentStroke, StrokeWidth = 1, CssClass = "wardley-buy-overlay" });
                    break;
                case WardleySourcing.Build:
                    nodeGroup.Add(new SceneCircle(x, y, radius * 2) { Fill = SceneColor.Parse("#eeeeee"), Stroke = SceneColor.Black, StrokeWidth = 1, CssClass = "wardley-build-overlay" });
                    break;
                case WardleySourcing.Market:
                    nodeGroup.Add(new SceneCircle(x, y, radius * 2) { Fill = colors.ComponentFill, Stroke = colors.ComponentStroke, StrokeWidth = 1, CssClass = "wardley-market-overlay" });
                    break;
            }

            if (!node.IsPipelineParent && node.Sourcing is not WardleySourcing.Market && node.ClassName is not "anchor")
            {
                nodeGroup.Add(new SceneCircle(x, y, radius) { Fill = colors.ComponentFill, Stroke = colors.ComponentStroke, StrokeWidth = 1 });
            }

            if (node.Sourcing is WardleySourcing.Market)
            {
                // Three small circles in a triangle, joined by lines
                var triangleRadius = radius * 1.2;
                var cos = triangleRadius * Math.Cos(Math.PI / 6);
                var sin = triangleRadius * Math.Sin(Math.PI / 6);
                (double X, double Y)[] points = [(x, y - triangleRadius), (x - cos, y + sin), (x + cos, y + sin)];
                for (var i = 0; i < 3; i++)
                {
                    var next = points[(i + 1) % 3];
                    nodeGroup.Add(new SceneLine(points[i].X, points[i].Y, next.X, next.Y) { Stroke = colors.ComponentStroke, StrokeWidth = 1, CssClass = "wardley-market-line" });
                }

                foreach (var point in points)
                {
                    nodeGroup.Add(new SceneCircle(point.X, point.Y, radius * 0.7) { Fill = colors.ComponentFill, Stroke = colors.ComponentStroke, StrokeWidth = 2, CssClass = "wardley-market-dot" });
                }
            }

            if (node.IsPipelineParent)
            {
                nodeGroup.Add(new SceneRectangle(x - (squareSize / 2), y - (squareSize / 2), squareSize, squareSize) { Fill = colors.ComponentFill, Stroke = colors.ComponentStroke, StrokeWidth = 1 });
            }

            if (node.Inertia)
            {
                var offset = node.IsPipelineParent ? (squareSize / 2) + 15 : radius + 15;
                if (node.Sourcing is not WardleySourcing.None)
                {
                    offset += radius + 10;
                }

                var lineHeight = node.IsPipelineParent ? squareSize : radius * 2;
                nodeGroup.Add(new SceneLine(x + offset, y - (lineHeight / 2), x + offset, y + (lineHeight / 2)) { Stroke = colors.ComponentStroke, StrokeWidth = 6, CssClass = "wardley-inertia" });
            }

            if (node.ClassName is "anchor")
            {
                // An anchor is its name, in bold, centered on its position
                var labelX = x + (node.LabelOffset?.X ?? 0);
                var labelY = y + (node.LabelOffset?.Y ?? -3);
                DrawText(context, nodeGroup, node.Label, labelX, labelY, diagram.LabelFontSize, bold: true, colors.ComponentLabel, SceneTextAnchor.Middle, middle: true);
            }
            else
            {
                var defaultOffset = diagram.NodeLabelOffset + (node.Sourcing is not WardleySourcing.None ? 10 : 0);
                var labelX = x + (node.LabelOffset?.X ?? defaultOffset);
                var labelY = y + (node.LabelOffset?.Y ?? -defaultOffset);
                DrawText(context, nodeGroup, node.Label, labelX, labelY, diagram.LabelFontSize, bold: false, colors.ComponentLabel, SceneTextAnchor.Start, middle: false);
            }
        }
    }

    private static void DrawAnnotations(WardleyDiagram diagram, DiagramRenderContext context, SceneGroup root, Colors colors, Func<double, double> projectX, Func<double, double> projectY)
    {
        if (diagram.Annotations.Count is 0)
            return;

        var annotationsGroup = root.Add(new SceneGroup { CssClass = "wardley-annotations" });
        foreach (var annotation in diagram.Annotations)
        {
            var x = projectX(annotation.Evolution * 100);
            var y = projectY(annotation.Visibility * 100);
            var annotationGroup = annotationsGroup.Add(new SceneGroup { CssClass = "wardley-annotation" });
            annotationGroup.Add(new SceneCircle(x, y, 10) { Fill = colors.AnnotationFill, Stroke = colors.Axis, StrokeWidth = 1.5 });
            DrawText(context, annotationGroup, annotation.Number.ToString(CultureInfo.InvariantCulture), x, y, 10, bold: true, colors.AxisText, SceneTextAnchor.Middle, middle: true);
        }

        if (diagram.AnnotationsPosition is not { } box)
            return;

        // The texts are listed by number, in a box that stays inside the map
        var texts = diagram.Annotations
            .Where(annotation => annotation.Text.Length > 0)
            .OrderBy(annotation => annotation.Number)
            .Select(annotation => context.Text(annotation.Number.ToString(CultureInfo.InvariantCulture) + ". " + annotation.Text, 11))
            .ToList();
        if (texts.Count is 0)
            return;

        const double BoxPadding = 10;
        const double LineHeight = 16;
        var boxWidth = texts.Max(text => text.Width) + (BoxPadding * 2) + 105;
        var boxHeight = (texts.Count * LineHeight) + (BoxPadding * 2) + (texts.Max(text => text.Height) / 2);
        var padding = diagram.MapPadding;
        var boxX = Math.Max(padding, Math.Min(projectX(box.Evolution * 100), diagram.Width - padding - boxWidth));
        var boxY = Math.Max(padding, Math.Min(projectY(box.Visibility * 100), diagram.Height - padding - boxHeight));
        var boxGroup = annotationsGroup.Add(new SceneGroup { CssClass = "wardley-annotations-box" });
        boxGroup.Add(new SceneRectangle(boxX, boxY, boxWidth, boxHeight) { Fill = colors.AnnotationFill, Stroke = colors.Axis, StrokeWidth = 1.5, CornerRadius = 4 });
        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            text.Draw(boxGroup, boxX + BoxPadding + (text.Width / 2), boxY + BoxPadding + ((i + 1) * LineHeight), colors.AxisText, SceneTextAnchor.Start);
        }
    }

    private static void DrawForces(DiagramRenderContext context, SceneGroup root, Colors colors, IList<WardleyAccelerator> forces, string cssClass, bool pointsRight, Func<double, double> projectX, Func<double, double> projectY)
    {
        if (forces.Count is 0)
            return;

        const double ArrowWidth = 60;
        const double ArrowHeight = 30;
        const double HeadWidth = 20;
        var forcesGroup = root.Add(new SceneGroup { CssClass = cssClass });
        foreach (var force in forces)
        {
            var x = projectX(force.Evolution * 100);
            var y = projectY(force.Visibility * 100);

            // The shaft and the head of a large arrow, pointing right or left
            var path = new PathBuilder();
            if (pointsRight)
            {
                path.MoveTo(x, y - (ArrowHeight / 2))
                    .LineTo(x + ArrowWidth - HeadWidth, y - (ArrowHeight / 2))
                    .LineTo(x + ArrowWidth - HeadWidth, y - (ArrowHeight / 2) - 8)
                    .LineTo(x + ArrowWidth, y)
                    .LineTo(x + ArrowWidth - HeadWidth, y + (ArrowHeight / 2) + 8)
                    .LineTo(x + ArrowWidth - HeadWidth, y + (ArrowHeight / 2))
                    .LineTo(x, y + (ArrowHeight / 2))
                    .Close();
            }
            else
            {
                path.MoveTo(x + ArrowWidth, y - (ArrowHeight / 2))
                    .LineTo(x + HeadWidth, y - (ArrowHeight / 2))
                    .LineTo(x + HeadWidth, y - (ArrowHeight / 2) - 8)
                    .LineTo(x, y)
                    .LineTo(x + HeadWidth, y + (ArrowHeight / 2) + 8)
                    .LineTo(x + HeadWidth, y + (ArrowHeight / 2))
                    .LineTo(x + ArrowWidth, y + (ArrowHeight / 2))
                    .Close();
            }

            forcesGroup.Add(new ScenePath(path.Build()) { Fill = colors.ComponentFill, Stroke = colors.ComponentStroke, StrokeWidth = 1 });
            DrawText(context, forcesGroup, force.Name, x + (ArrowWidth / 2), y + (ArrowHeight / 2) + 15, 10, bold: true, colors.AxisText, SceneTextAnchor.Middle, middle: false);
        }
    }

    /// <summary>Adds the arrowhead of a marker of Mermaid at the end of a line: a triangle of 10 by 10 units, its tip one unit past the end.</summary>
    private static void AddArrowhead(SceneGroup group, double fromX, double fromY, double toX, double toY, double scale, SceneColor color)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (!(length > 0))
            return;

        var ux = dx / length;
        var uy = dy / length;
        (double X, double Y) Point(double along, double across) => (toX + (((along * ux) - (across * uy)) * scale), toY + (((along * uy) + (across * ux)) * scale));
        var tip = Point(1, 0);
        var left = Point(-9, -5);
        var right = Point(-9, 5);
        var path = new PathBuilder().MoveTo(left.X, left.Y).LineTo(tip.X, tip.Y).LineTo(right.X, right.Y).Close();
        group.Add(new ScenePath(path.Build()) { Fill = color });
    }

    /// <summary>Draws a text as Mermaid positions it: on its baseline, or centered vertically on <paramref name="y"/>.</summary>
    private static void DrawText(DiagramRenderContext context, SceneGroup group, string text, double x, double y, double fontSize, bool bold, SceneColor color, SceneTextAnchor anchor, bool middle)
    {
        var block = context.Text(text, fontSize, bold);
        if (block.IsEmpty)
            return;

        var centerY = middle ? y : y + (block.Height / 2) - ((block.LineHeight - block.Ascent - block.Descent) / 2) - block.Ascent;
        var centerX = anchor switch
        {
            SceneTextAnchor.Start => x + (block.Width / 2),
            SceneTextAnchor.End => x - (block.Width / 2),
            _ => x,
        };
        block.Draw(group, centerX, centerY, color, anchor);
    }

    private sealed class Position(double x, double y)
    {
        public double X { get; set; } = x;

        public double Y { get; set; } = y;
    }

    private sealed class Colors
    {
        public Colors(DiagramRenderContext context)
        {
            var theme = context.Theme;
            Background = context.Background;
            Axis = theme.LineColor;
            AxisText = theme.PrimaryTextColor;
            ComponentFill = theme.IsDark ? theme.NodeBackground : Background;
            ComponentStroke = theme.LineColor;
            ComponentLabel = theme.PrimaryTextColor;
            Link = theme.LineColor;
            Evolution = SceneColor.Parse(theme.IsDark ? "#ff6b6b" : "#dc3545");
            AnnotationFill = ComponentFill;
            Grid = theme.IsDark ? theme.LineColor.WithOpacity(0.3) : SceneColor.Parse("lightgrey");
            StageDivider = theme.IsDark ? theme.LineColor : SceneColor.Black;
        }

        public SceneColor Background { get; }

        public SceneColor Axis { get; }

        public SceneColor AxisText { get; }

        public SceneColor ComponentFill { get; }

        public SceneColor ComponentStroke { get; }

        public SceneColor ComponentLabel { get; }

        public SceneColor Link { get; }

        public SceneColor Evolution { get; }

        public SceneColor AnnotationFill { get; }

        public SceneColor Grid { get; }

        public SceneColor StageDivider { get; }
    }
}
