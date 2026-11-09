using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the pie charts as Mermaid does: a pie of 185 pixels of radius in a square of 450 pixels, its slices clockwise
/// from the top, their percentages at <see cref="PieDiagram.TextPosition"/> of the radius, and a legend of 18 pixels
/// squares.
/// </summary>
internal static class PieDiagramRenderer
{
    private const double Margin = 40;
    private const double LegendRectSize = 18;
    private const double LegendSpacing = 4;
    private const double LegendItemHeight = LegendRectSize + LegendSpacing;
    private const double Size = 450;
    private const double Radius = (Size / 2) - Margin;

    public static SceneBounds Render(PieDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var colors = new PieColors(diagram, context.Theme);
        var bounds = new BoundsBuilder();
        var outerStrokeWidth = diagram.OuterStrokeWidth ?? 2;
        var strokeWidth = diagram.StrokeWidth ?? 2;
        var strokeColor = diagram.StrokeColor ?? SceneColor.Black;
        var opacity = diagram.Opacity ?? 0.7;
        var innerRadius = diagram.DonutHole is > 0 and <= 0.9 ? diagram.DonutHole * Radius : 0;
        var labelRadius = Radius * diagram.TextPosition;
        var sectionFont = new SceneFont(diagram.SectionTextSize ?? 17);
        var legendFont = new SceneFont(diagram.LegendTextSize ?? 17);

        // As the map of Mermaid, a label keeps the value of its first slice
        var sections = new List<PieSlice>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slice in diagram.Slices)
        {
            if (labels.Add(slice.Label))
            {
                sections.Add(slice);
            }
        }

        var sum = 0.0;
        foreach (var section in sections)
        {
            sum += section.Value;
        }

        var legendTexts = new List<string>(sections.Count);
        var longestTextWidth = 0.0;
        foreach (var section in sections)
        {
            var text = SingleLine(diagram.ShowData ? section.Label + " [" + FormatNumber(section.Value) + "]" : section.Label);
            legendTexts.Add(text);
            longestTextWidth = Math.Max(longestTextWidth, context.Measurer.Measure(text, legendFont).Width);
        }

        // The pie is centered in its square; the legend moves it, and makes the drawing larger
        var width = Size + Margin;
        var height = Size;
        var pieOffsetX = 0.0;
        var pieOffsetY = 0.0;
        var totalLegendHeight = sections.Count * LegendItemHeight;
        switch (diagram.LegendPosition)
        {
            case PieLegendPosition.Top:
                height += totalLegendHeight;
                pieOffsetY = totalLegendHeight + LegendItemHeight;
                break;
            case PieLegendPosition.Bottom:
                height += totalLegendHeight;
                break;
            case PieLegendPosition.Left:
                width += LegendRectSize + LegendSpacing + longestTextWidth;
                pieOffsetX = longestTextWidth + LegendRectSize + LegendSpacing;
                break;
            case PieLegendPosition.Right:
                width += LegendRectSize + LegendSpacing + longestTextWidth;
                break;
        }

        bounds.Add(new SceneBounds(0, 0, width, height));
        var centerX = Size / 2;
        var centerY = Size / 2;
        var pieX = centerX + pieOffsetX;
        var pieY = centerY + pieOffsetY;
        var pie = group.Add(new SceneGroup { CssClass = "pie" });
        var outerRadius = Radius + (outerStrokeWidth / 2);
        pie.Add(new SceneCircle(pieX, pieY, outerRadius) { Stroke = diagram.OuterStrokeColor ?? SceneColor.Black, StrokeWidth = outerStrokeWidth, CssClass = "pieOuterCircle" });
        bounds.Add(new SceneBounds(pieX - outerRadius - (outerStrokeWidth / 2), pieY - outerRadius - (outerStrokeWidth / 2), (2 * outerRadius) + outerStrokeWidth, (2 * outerRadius) + outerStrokeWidth));

        // The slices smaller than 1% are not drawn; the others share the whole circle, as the pie layout of d3
        var drawn = new List<int>();
        var drawnSum = 0.0;
        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].Value / sum * 100 >= 1)
            {
                drawn.Add(i);
                drawnSum += sections[i].Value;
            }
        }

        var factor = drawnSum > 0 ? 2 * Math.PI / drawnSum : 0;
        var angle = 0.0;
        var arcs = new List<(int Index, double Start, double End)>(drawn.Count);
        foreach (var index in drawn)
        {
            var end = angle + (sections[index].Value * factor);
            arcs.Add((index, angle, end));
            angle = end;
        }

        foreach (var (index, start, end) in arcs)
        {
            var section = sections[index];
            var highlighted = diagram.HighlightedSlice is { Length: > 0 } highlight && string.Equals(highlight, section.Label, StringComparison.Ordinal);

            // A highlighted slice is scaled by 1.05 from the center of the pie, and opaque
            var scale = highlighted ? 1.05 : 1;
            var path = CreateArc(pieX, pieY, innerRadius * scale, Radius * scale, start, end);
            pie.Add(new ScenePath(path)
            {
                Fill = colors.Slice(index),
                Stroke = strokeColor,
                StrokeWidth = strokeWidth * scale,
                Opacity = highlighted ? 1 : opacity,
                CssClass = highlighted ? "pieCircle highlighted" : "pieCircle",
            });
        }

        foreach (var (index, start, end) in arcs)
        {
            var middle = (start + end) / 2;
            var x = pieX + (labelRadius * Math.Sin(middle));
            var y = pieY - (labelRadius * Math.Cos(middle));
            var percent = Math.Round(sections[index].Value / sum * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
            var textWidth = context.Measurer.Measure(percent, sectionFont).Width;
            pie.Add(new SceneText(x, y, percent, sectionFont, colors.SectionText) { Anchor = SceneTextAnchor.Middle, CssClass = "slice", MeasuredWidth = textWidth });
            bounds.Add(new SceneBounds(x - (textWidth / 2), y - sectionFont.Size, textWidth, sectionFont.Size * 1.2));
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var (x, y) = diagram.LegendPosition switch
            {
                PieLegendPosition.Center => ((-longestTextWidth / 2) - LegendRectSize - LegendSpacing, (i * LegendItemHeight) - (totalLegendHeight / 2)),
                PieLegendPosition.Top => ((-longestTextWidth / 2) - LegendRectSize - LegendSpacing, (i * LegendItemHeight) - Radius),
                PieLegendPosition.Bottom => ((-longestTextWidth / 2) - LegendRectSize - LegendSpacing, (i * LegendItemHeight) + Radius + LegendItemHeight),
                PieLegendPosition.Left => (-Radius - LegendRectSize - LegendSpacing, (i * LegendItemHeight) - (totalLegendHeight / 2)),
                _ => (12 * LegendRectSize, (i * LegendItemHeight) - (totalLegendHeight / 2)),
            };

            x += centerX;
            y += centerY;
            var color = colors.Slice(i);
            var legend = group.Add(new SceneGroup { CssClass = "legend" });
            legend.Add(new SceneRectangle(x, y, LegendRectSize, LegendRectSize) { Fill = color, Stroke = color, StrokeWidth = 1 });
            var textWidth = context.Measurer.Measure(legendTexts[i], legendFont).Width;
            legend.Add(new SceneText(x + LegendRectSize + LegendSpacing, y + LegendRectSize - LegendSpacing, legendTexts[i], legendFont, colors.LegendText) { MeasuredWidth = textWidth });
            bounds.Add(new SceneBounds(x, y, LegendRectSize + LegendSpacing + textWidth, Math.Max(LegendRectSize, legendFont.Size * 1.2)));
        }

        return bounds.ToBounds();
    }

    /// <summary>Builds a slice as the arc generator of d3: the angles go clockwise from the top.</summary>
    private static PathData CreateArc(double centerX, double centerY, double innerRadius, double outerRadius, double start, double end)
    {
        var builder = new PathBuilder();
        if (end - start >= (2 * Math.PI) - 1e-12)
        {
            // A full circle: two half circles, and the hole of a donut in the other direction
            builder.MoveTo(centerX, centerY - outerRadius)
                .ArcTo(outerRadius, outerRadius, 0, largeArc: false, sweep: true, centerX, centerY + outerRadius)
                .ArcTo(outerRadius, outerRadius, 0, largeArc: false, sweep: true, centerX, centerY - outerRadius)
                .Close();
            if (innerRadius > 0)
            {
                builder.MoveTo(centerX, centerY - innerRadius)
                    .ArcTo(innerRadius, innerRadius, 0, largeArc: false, sweep: false, centerX, centerY + innerRadius)
                    .ArcTo(innerRadius, innerRadius, 0, largeArc: false, sweep: false, centerX, centerY - innerRadius)
                    .Close();
            }

            return builder.Build();
        }

        var largeArc = end - start > Math.PI;
        builder.MoveTo(centerX + (outerRadius * Math.Sin(start)), centerY - (outerRadius * Math.Cos(start)))
            .ArcTo(outerRadius, outerRadius, 0, largeArc, sweep: true, centerX + (outerRadius * Math.Sin(end)), centerY - (outerRadius * Math.Cos(end)));
        if (innerRadius > 0)
        {
            builder.LineTo(centerX + (innerRadius * Math.Sin(end)), centerY - (innerRadius * Math.Cos(end)))
                .ArcTo(innerRadius, innerRadius, 0, largeArc, sweep: false, centerX + (innerRadius * Math.Sin(start)), centerY - (innerRadius * Math.Cos(start)));
        }
        else
        {
            builder.LineTo(centerX, centerY);
        }

        return builder.Close().Build();
    }

    // An SVG text is one line: the line breaks are spaces
    private static string SingleLine(string text) => text.ReplaceLineEndings(" ");

    /// <summary>Formats a number as JavaScript does: the shortest digits that read back to the number, and an exponent from 1e21.</summary>
    internal static string FormatNumber(double value)
    {
        if (value == 0)
            return "0";

        var negative = value < 0;
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? text : text[..dot];
        var all = dot < 0 ? text : integer + text[(dot + 1)..];
        var leadingZeros = all.Length - all.TrimStart('0').Length;
        var digits = all.Trim('0');

        // The number is 0.digits × 10^n
        var n = integer.Length - leadingZeros + exponent;
        var k = digits.Length;
        var builder = new StringBuilder();
        if (negative)
        {
            builder.Append('-');
        }

        if (k <= n && n <= 21)
        {
            builder.Append(digits).Append('0', n - k);
        }
        else if (n is > 0 and <= 21)
        {
            builder.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
        }
        else if (n is > -6 and <= 0)
        {
            builder.Append("0.").Append('0', -n).Append(digits);
        }
        else
        {
            builder.Append(digits[0]);
            if (k > 1)
            {
                builder.Append('.').Append(digits, 1, k - 1);
            }

            builder.Append('e').Append(n - 1 >= 0 ? "+" : "-").Append(Math.Abs(n - 1).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
