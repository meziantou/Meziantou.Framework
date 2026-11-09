namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the quadrant charts as Mermaid does: the four quadrants filled with shades of the primary color of the theme,
/// their texts, the borders, the points with their texts below them, and the texts of the axes. Without points, the
/// texts of the quadrants are in their middle; with points, at their top, and the texts of the x axis at the bottom.
/// </summary>
internal static class QuadrantChartDiagramRenderer
{
    public static SceneBounds Render(QuadrantChartDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var colors = diagram.Colors;
        var width = diagram.Width;
        var height = diagram.Height;

        // The circles of large radius do not go out of the chart, as in the SVG of Mermaid
        var chart = group.Add(new SceneGroup { CssClass = "quadrant-chart", Clip = new SceneBounds(0, 0, width, height) });

        var xAxisLeft = context.Text(diagram.XAxisLeftLabel, diagram.XAxisLabelFontSize);
        var xAxisRight = context.Text(diagram.XAxisRightLabel, diagram.XAxisLabelFontSize);
        var yAxisBottom = context.Text(diagram.YAxisBottomLabel, diagram.YAxisLabelFontSize);
        var yAxisTop = context.Text(diagram.YAxisTopLabel, diagram.YAxisLabelFontSize);
        var showXAxis = !xAxisLeft.IsEmpty || !xAxisRight.IsEmpty;
        var showYAxis = !yAxisBottom.IsEmpty || !yAxisTop.IsEmpty;
        var hasPoints = diagram.Points.Count > 0;
        var xAxisAtBottom = hasPoints || diagram.XAxisPosition is QuadrantChartXAxisPosition.Bottom;
        var yAxisAtRight = diagram.YAxisPosition is QuadrantChartYAxisPosition.Right;

        var xAxisSpace = showXAxis ? (diagram.XAxisLabelPadding * 2) + diagram.XAxisLabelFontSize : 0;
        var yAxisSpace = showYAxis ? (diagram.YAxisLabelPadding * 2) + diagram.YAxisLabelFontSize : 0;
        var padding = diagram.QuadrantPadding;
        var left = padding + (yAxisAtRight ? 0 : yAxisSpace);
        var top = padding + (xAxisAtBottom ? 0 : xAxisSpace);
        var quadrantWidth = Math.Max(0, width - (padding * 2) - yAxisSpace);
        var quadrantHeight = Math.Max(0, height - (padding * 2) - xAxisSpace);
        var halfWidth = quadrantWidth / 2;
        var halfHeight = quadrantHeight / 2;

        // The quadrants, numbered as in mathematics: 1 top right, 2 top left, 3 bottom left, 4 bottom right
        var primary = theme.PrimaryColor;
        var primaryText = theme.PrimaryTextColor;
        var quadrants = new (double X, double Y, DiagramText? Label, SceneColor Fill, SceneColor TextFill)[]
        {
            (left + halfWidth, top, diagram.Quadrant1Label, colors.Quadrant1Fill ?? primary, colors.Quadrant1TextFill ?? primaryText),
            (left, top, diagram.Quadrant2Label, colors.Quadrant2Fill ?? Adjust(primary, 5), colors.Quadrant2TextFill ?? Adjust(primaryText, -5)),
            (left, top + halfHeight, diagram.Quadrant3Label, colors.Quadrant3Fill ?? Adjust(primary, 10), colors.Quadrant3TextFill ?? Adjust(primaryText, -10)),
            (left + halfWidth, top + halfHeight, diagram.Quadrant4Label, colors.Quadrant4Fill ?? Adjust(primary, 15), colors.Quadrant4TextFill ?? Adjust(primaryText, -15)),
        };

        var quadrantGroup = chart.Add(new SceneGroup { CssClass = "quadrants" });
        foreach (var quadrant in quadrants)
        {
            var quadrantElement = quadrantGroup.Add(new SceneGroup { CssClass = "quadrant" });
            quadrantElement.Add(new SceneRectangle(quadrant.X, quadrant.Y, halfWidth, halfHeight) { Fill = quadrant.Fill, Stroke = null, StrokeWidth = 0 });
            var text = context.Text(quadrant.Label, diagram.QuadrantLabelFontSize);
            var textY = hasPoints ? quadrant.Y + diagram.QuadrantTextTopPadding : quadrant.Y + (halfHeight / 2);
            DrawText(quadrantElement, text, quadrant.X + (halfWidth / 2), textY, quadrant.TextFill, start: false, hanging: hasPoints, rotation: 0);
        }

        // The borders: the outside one is wider, and goes past the corners by half its width
        var externalColor = colors.ExternalBorderStrokeFill ?? theme.PrimaryBorderColor;
        var internalColor = colors.InternalBorderStrokeFill ?? theme.PrimaryBorderColor;
        var external = diagram.ExternalBorderWidth;
        var half = external / 2;
        var borderGroup = chart.Add(new SceneGroup { CssClass = "border" });
        AddLine(borderGroup, left - half, top, left + quadrantWidth + half, top, externalColor, external);
        AddLine(borderGroup, left + quadrantWidth, top + half, left + quadrantWidth, top + quadrantHeight - half, externalColor, external);
        AddLine(borderGroup, left - half, top + quadrantHeight, left + quadrantWidth + half, top + quadrantHeight, externalColor, external);
        AddLine(borderGroup, left, top + half, left, top + quadrantHeight - half, externalColor, external);
        AddLine(borderGroup, left + halfWidth, top + half, left + halfWidth, top + quadrantHeight - half, internalColor, diagram.InternalBorderWidth);
        AddLine(borderGroup, left + half, top + halfHeight, left + quadrantWidth - half, top + halfHeight, internalColor, diagram.InternalBorderWidth);

        // Mermaid draws the last point first: the first points are on top
        var pointFill = colors.PointFill ?? DefaultPointFill(theme);
        var pointTextFill = colors.PointTextFill ?? primaryText;
        var pointGroup = chart.Add(new SceneGroup { CssClass = "data-points" });
        for (var i = diagram.Points.Count - 1; i >= 0; i--)
        {
            var point = diagram.Points[i];
            var classStyle = point.ClassName is not null && diagram.ClassDefinitions.TryGetValue(point.ClassName, out var style) ? style : null;
            var radius = point.Style.Radius ?? classStyle?.Radius ?? diagram.PointRadius;
            var fill = point.Style.Color ?? classStyle?.Color ?? pointFill;
            var stroke = point.Style.StrokeColor ?? classStyle?.StrokeColor ?? pointFill;
            var strokeWidth = point.Style.StrokeWidth ?? classStyle?.StrokeWidth ?? 0;
            var x = left + (point.X * quadrantWidth);
            var y = top + ((1 - point.Y) * quadrantHeight);
            var pointElement = pointGroup.Add(new SceneGroup { CssClass = "data-point" });
            pointElement.Add(new SceneCircle(x, y, radius) { Fill = fill, Stroke = strokeWidth > 0 ? (ScenePaint)stroke : null, StrokeWidth = strokeWidth });
            DrawText(pointElement, context.Text(point.Label, diagram.PointLabelFontSize), x, y + diagram.PointTextPadding, pointTextFill, start: false, hanging: true, rotation: 0);
        }

        // The texts of the axes: in the middle of the halves when the axis has two texts, at the start of the axis otherwise
        var labelGroup = chart.Add(new SceneGroup { CssClass = "labels" });
        if (showXAxis)
        {
            var xAxisColor = colors.XAxisTextFill ?? primaryText;
            var inMiddle = !xAxisRight.IsEmpty;
            var y = xAxisAtBottom ? diagram.XAxisLabelPadding + top + quadrantHeight + padding : diagram.XAxisLabelPadding;
            DrawText(labelGroup, xAxisLeft, left + (inMiddle ? halfWidth / 2 : 0), y, xAxisColor, start: !inMiddle, hanging: true, rotation: 0);
            DrawText(labelGroup, xAxisRight, left + halfWidth + (inMiddle ? halfWidth / 2 : 0), y, xAxisColor, start: !inMiddle, hanging: true, rotation: 0);
        }

        if (showYAxis)
        {
            var yAxisColor = colors.YAxisTextFill ?? primaryText;
            var inMiddle = !yAxisTop.IsEmpty;
            var x = yAxisAtRight ? diagram.YAxisLabelPadding + left + quadrantWidth + padding : diagram.YAxisLabelPadding;
            DrawText(labelGroup, yAxisBottom, x, top + quadrantHeight - (inMiddle ? halfHeight / 2 : 0), yAxisColor, start: !inMiddle, hanging: true, rotation: -90);
            DrawText(labelGroup, yAxisTop, x, top + halfHeight - (inMiddle ? halfHeight / 2 : 0), yAxisColor, start: !inMiddle, hanging: true, rotation: -90);
        }

        return new SceneBounds(0, 0, width, height);
    }

    /// <summary>
    /// The default fill of the points. Mermaid computes an invalid color for it (<c>darken</c> without an amount), which
    /// the browsers draw black; on a dark theme, the text color is used instead, as black points would not show.
    /// </summary>
    private static SceneColor DefaultPointFill(DiagramTheme theme) => theme.IsDark ? theme.PrimaryTextColor : SceneColor.Black;

    /// <summary>Adds an amount to the red, green and blue channels of a color, as the <c>adjust</c> function of Mermaid.</summary>
    internal static SceneColor Adjust(SceneColor color, int amount)
    {
        return new SceneColor(Clamp(color.R + amount), Clamp(color.G + amount), Clamp(color.B + amount), color.A);

        static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }

    private static void AddLine(SceneGroup group, double x1, double y1, double x2, double y2, SceneColor color, double width)
    {
        if (width > 0)
        {
            group.Add(new SceneLine(x1, y1, x2, y2) { Stroke = color, StrokeWidth = width });
        }
    }

    /// <summary>Draws a text whose top (<paramref name="hanging"/>) or middle is at <paramref name="y"/>, and whose start or middle is at <paramref name="x"/>, rotated around that point.</summary>
    private static void DrawText(SceneGroup group, TextBlock text, double x, double y, SceneColor color, bool start, bool hanging, double rotation)
    {
        if (text.IsEmpty)
            return;

        var target = rotation is 0 ? group : group.Add(new SceneGroup { Transform = SceneTransform.Rotate(rotation, x, y) });
        text.Draw(target, start ? x + (text.Width / 2) : x, hanging ? y + (text.Height / 2) : y, color, start ? SceneTextAnchor.Start : SceneTextAnchor.Middle);
    }
}
