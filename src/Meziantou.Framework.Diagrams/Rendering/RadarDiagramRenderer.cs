namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the radar charts as Mermaid does: the axes clockwise from the top over a scale of circles or polygons, the
/// curves rounded through their points (or straight with the polygons), and the legend in the top right corner.
/// </summary>
internal static class RadarDiagramRenderer
{
    private const double LabelPadding = 4;
    private const double LegendBoxSize = 12;
    private const double LegendLineHeight = 20;

    public static SceneBounds Render(RadarDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var bounds = new BoundsBuilder();
        var totalWidth = diagram.Width + diagram.MarginLeft + diagram.MarginRight;
        var totalHeight = diagram.Height + diagram.MarginTop + diagram.MarginBottom;
        bounds.Add(new SceneBounds(0, 0, totalWidth, totalHeight));
        var centerX = diagram.MarginLeft + (diagram.Width / 2);
        var centerY = diagram.MarginTop + (diagram.Height / 2);
        var radius = Math.Min(diagram.Width, diagram.Height) / 2;
        var axes = diagram.Axes;
        var axisColor = diagram.AxisColor ?? theme.LineColor;
        var graticuleColor = diagram.GraticuleColor ?? SceneColor.Parse("#dedede");
        var curveOpacity = diagram.CurveOpacity ?? 0.5;

        // The scale
        var graticule = group.Add(new SceneGroup { CssClass = "radarGraticules" });
        for (var i = 0; i < diagram.Ticks; i++)
        {
            var r = radius * (i + 1) / diagram.Ticks;
            SceneShape shape;
            if (diagram.Graticule is RadarGraticule.Circle)
            {
                shape = new SceneCircle(centerX, centerY, r);
            }
            else
            {
                if (axes.Count is 0)
                    break;

                var builder = new PathBuilder();
                for (var j = 0; j < axes.Count; j++)
                {
                    var (x, y) = Point(centerX, centerY, r, j, axes.Count);
                    if (j is 0)
                    {
                        builder.MoveTo(x, y);
                    }
                    else
                    {
                        builder.LineTo(x, y);
                    }
                }

                shape = new ScenePath(builder.Close().Build());
            }

            shape.Fill = graticuleColor.WithOpacity((diagram.GraticuleOpacity ?? 0.3) * graticuleColor.Opacity);
            shape.Stroke = graticuleColor;
            shape.StrokeWidth = diagram.GraticuleStrokeWidth ?? 1;
            shape.CssClass = "radarGraticule";
            graticule.Add(shape);
        }

        // The axes and their labels, anchored away from the center
        var labelFont = new SceneFont(diagram.AxisLabelFontSize ?? 12);
        var axesGroup = group.Add(new SceneGroup { CssClass = "radarAxes" });
        for (var i = 0; i < axes.Count; i++)
        {
            var angle = (2 * i * Math.PI / axes.Count) - (Math.PI / 2);
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            axesGroup.Add(new SceneLine(centerX, centerY, centerX + (radius * diagram.AxisScaleFactor * cos), centerY + (radius * diagram.AxisScaleFactor * sin))
            {
                Stroke = axisColor,
                StrokeWidth = diagram.AxisStrokeWidth ?? 2,
                CssClass = "radarAxisLine",
            });

            var label = SingleLine(axes[i].Label ?? axes[i].Name);
            var metrics = context.Measurer.Measure(label, labelFont);
            var x = centerX + (radius * diagram.AxisLabelFactor * cos) + (LabelPadding * cos);
            var y = centerY + (radius * diagram.AxisLabelFactor * sin) + (LabelPadding * sin);
            var anchor = cos > 0.01 ? SceneTextAnchor.Start : cos < -0.01 ? SceneTextAnchor.End : SceneTextAnchor.Middle;

            // The dominant baselines of Mermaid: hanging below the center, alphabetic above it, central on its sides
            var baseline = sin > 0.01 ? y + metrics.Ascent : sin < -0.01 ? y : y + ((metrics.Ascent - metrics.Descent) / 2);
            axesGroup.Add(new SceneText(x, baseline, label, labelFont, theme.TextColor) { Anchor = anchor, CssClass = "radarAxisLabel", MeasuredWidth = metrics.Width });
            var left = anchor switch
            {
                SceneTextAnchor.Start => x,
                SceneTextAnchor.End => x - metrics.Width,
                _ => x - (metrics.Width / 2),
            };
            bounds.Add(new SceneBounds(left, baseline - metrics.Ascent, metrics.Width, metrics.Height));
        }

        // The curves: the values are clipped between the minimum and the maximum
        var maximum = diagram.Maximum ?? MaximumValue(diagram.Curves);
        var minimum = diagram.Minimum;
        var curves = group.Add(new SceneGroup { CssClass = "radarCurves" });
        for (var index = 0; index < diagram.Curves.Count; index++)
        {
            var curve = diagram.Curves[index];
            if (curve.Values.Count != axes.Count || axes.Count is 0 || !(maximum > minimum) || !double.IsFinite(maximum))
                continue;

            var points = new (double X, double Y)[axes.Count];
            for (var i = 0; i < axes.Count; i++)
            {
                var value = Math.Min(Math.Max(curve.Values[i], minimum), maximum);
                var r = radius * (value - minimum) / (maximum - minimum);
                if (!double.IsFinite(r))
                {
                    r = 0;
                }

                points[i] = Point(centerX, centerY, r, i, axes.Count);
            }

            var path = diagram.Graticule is RadarGraticule.Circle ? ClosedRoundCurve(points, diagram.CurveTension) : Polygon(points);
            var color = theme.GetPaletteColor(index);
            curves.Add(new ScenePath(path)
            {
                Fill = color.WithOpacity(curveOpacity * color.Opacity),
                Stroke = color,
                StrokeWidth = diagram.CurveStrokeWidth ?? 2,
                CssClass = "radarCurve-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        // The legend, in the top right corner, may go out of the frame as in Mermaid
        if (diagram.ShowLegend)
        {
            var legendFont = new SceneFont(diagram.LegendFontSize ?? 12);
            var legendX = centerX + ((((diagram.Width / 2) + diagram.MarginRight) * 3) / 4);
            var legendY = centerY - ((((diagram.Height / 2) + diagram.MarginTop) * 3) / 4);
            var legend = group.Add(new SceneGroup { CssClass = "radarLegend" });
            for (var index = 0; index < diagram.Curves.Count; index++)
            {
                var curve = diagram.Curves[index];
                var y = legendY + (index * LegendLineHeight);
                var color = theme.GetPaletteColor(index);
                legend.Add(new SceneRectangle(legendX, y, LegendBoxSize, LegendBoxSize)
                {
                    Fill = color.WithOpacity(curveOpacity * color.Opacity),
                    Stroke = color,
                    StrokeWidth = 1,
                    CssClass = "radarLegendBox-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });

                var text = SingleLine(curve.Label ?? curve.Name);
                var metrics = context.Measurer.Measure(text, legendFont);
                legend.Add(new SceneText(legendX + 16, y + metrics.Ascent, text, legendFont, theme.TextColor) { CssClass = "radarLegendText", MeasuredWidth = metrics.Width });
                bounds.Add(new SceneBounds(legendX, y, 16 + metrics.Width, Math.Max(LegendBoxSize, metrics.Height)));
            }
        }

        return bounds.ToBounds();
    }

    private static (double X, double Y) Point(double centerX, double centerY, double radius, int index, int count)
    {
        var angle = (2 * Math.PI * index / count) - (Math.PI / 2);
        return (centerX + (radius * Math.Cos(angle)), centerY + (radius * Math.Sin(angle)));
    }

    private static double MaximumValue(IList<RadarCurve> curves)
    {
        var maximum = double.NegativeInfinity;
        foreach (var curve in curves)
        {
            foreach (var value in curve.Values)
            {
                maximum = Math.Max(maximum, value);
            }
        }

        return maximum;
    }

    private static PathData Polygon((double X, double Y)[] points)
    {
        var builder = new PathBuilder().MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++)
        {
            builder.LineTo(points[i].X, points[i].Y);
        }

        return builder.Close().Build();
    }

    /// <summary>Joins the points with the cubic Bézier curves of a closed Catmull-Rom spline, as Mermaid.</summary>
    private static PathData ClosedRoundCurve((double X, double Y)[] points, double tension)
    {
        var count = points.Length;
        var builder = new PathBuilder().MoveTo(points[0].X, points[0].Y);
        for (var i = 0; i < count; i++)
        {
            var p0 = points[(i - 1 + count) % count];
            var p1 = points[i];
            var p2 = points[(i + 1) % count];
            var p3 = points[(i + 2) % count];
            builder.CubicTo(
                p1.X + ((p2.X - p0.X) * tension),
                p1.Y + ((p2.Y - p0.Y) * tension),
                p2.X - ((p3.X - p1.X) * tension),
                p2.Y - ((p3.Y - p1.Y) * tension),
                p2.X,
                p2.Y);
        }

        return builder.Close().Build();
    }

    // An SVG text is one line: the line breaks are spaces
    private static string SingleLine(string text) => text.ReplaceLineEndings(" ");
}
