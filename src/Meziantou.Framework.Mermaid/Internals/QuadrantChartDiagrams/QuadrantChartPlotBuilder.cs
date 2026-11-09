using System.Text;
using Meziantou.Framework.Charts;

namespace Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;

/// <summary>
/// Draws a <see cref="QuadrantChart"/> with the marks of a <see cref="Plot"/>, with the layout of Mermaid: the quadrants
/// fill a square between the texts of the axes, on linear scales from 0 to 1 whose axes are hidden. Without points, the
/// texts of the quadrants are in their middle; with points, at their top, and the texts of the x axis at the bottom.
/// </summary>
internal static class QuadrantChartPlotBuilder
{
    // The space the plot gives the title: its padding above, the gap below, and the gap before the plot area
    private const double TitlePadding = 10;
    private const double TitleGap = 2 + 6;
    private const double LineHeightFactor = 1.2;

    public static void Configure(QuadrantChart chart, Plot plot)
    {
        var measurer = chart.TextMeasurer ?? AfmTextMeasurer.Default;
        var theme = chart.Theme;
        var colors = Resolve(chart.Colors, theme);

        // As in Mermaid, the title is drawn with the primary text color (quadrantTitleFill)
        plot.Theme = plot.Theme with { Foreground = theme.PrimaryTextColor };

        var titleSpace = string.IsNullOrEmpty(plot.Title) ? 0 : TitlePadding + measurer.Measure(plot.Title, new SceneFont(plot.Theme.TitleFontSize, Bold: true)).Height + TitleGap;

        var xAxisLeft = ToPlainText(chart.XAxisLeftLabel);
        var xAxisRight = ToPlainText(chart.XAxisRightLabel);
        var yAxisBottom = ToPlainText(chart.YAxisBottomLabel);
        var yAxisTop = ToPlainText(chart.YAxisTopLabel);
        var showXAxis = xAxisLeft.Length > 0 || xAxisRight.Length > 0;
        var showYAxis = yAxisBottom.Length > 0 || yAxisTop.Length > 0;
        var hasPoints = chart.Points.Count > 0;
        var xAxisAtBottom = hasPoints || chart.XAxisPosition is QuadrantChartXAxisPosition.Bottom;
        var yAxisAtRight = chart.YAxisPosition is QuadrantChartYAxisPosition.Right;

        // The layout of Mermaid, moved down by the space of the title
        var xAxisSpace = showXAxis ? (chart.XAxisLabelPadding * 2) + chart.XAxisLabelFontSize : 0;
        var yAxisSpace = showYAxis ? (chart.YAxisLabelPadding * 2) + chart.YAxisLabelFontSize : 0;
        var padding = chart.QuadrantPadding;
        var width = Math.Max(1, chart.Width);
        var height = Math.Max(1, chart.Height + titleSpace);
        var left = padding + (yAxisAtRight ? 0 : yAxisSpace);
        var top = titleSpace + padding + (xAxisAtBottom ? 0 : xAxisSpace);
        var quadrantWidth = Math.Max(0, chart.Width - (padding * 2) - yAxisSpace);
        var quadrantHeight = Math.Max(0, chart.Height - (padding * 2) - xAxisSpace);

        plot.Width = width;
        plot.Height = height;
        plot.Margin = new ChartMargin(top, width - left - quadrantWidth, height - top - quadrantHeight, left);
        plot.XScale = new LinearScale { Minimum = 0, Maximum = 1, Nice = false, Zero = false };
        plot.YScale = new LinearScale { Minimum = 0, Maximum = 1, Nice = false, Zero = false };
        plot.XAxis.Visible = false;
        plot.YAxis.Visible = false;

        // The quadrants, numbered as in mathematics: 1 top right, 2 top left, 3 bottom left, 4 bottom right
        var quadrants = new (double X, double Y, DiagramText? Label, SceneColor Fill, SceneColor TextFill)[]
        {
            (0.5, 0.5, chart.Quadrant1Label, colors.Quadrant1Fill, colors.Quadrant1TextFill),
            (0, 0.5, chart.Quadrant2Label, colors.Quadrant2Fill, colors.Quadrant2TextFill),
            (0, 0, chart.Quadrant3Label, colors.Quadrant3Fill, colors.Quadrant3TextFill),
            (0.5, 0, chart.Quadrant4Label, colors.Quadrant4Fill, colors.Quadrant4TextFill),
        };

        foreach (var quadrant in quadrants)
        {
            plot.Add(new RectMark<(double X, double Y)>([(quadrant.X, quadrant.Y)])
            {
                X1 = row => row.X,
                X2 = row => row.X + 0.5,
                Y1 = row => row.Y,
                Y2 = row => row.Y + 0.5,
                Inset = 0,
                FillColor = quadrant.Fill,
                CssClass = "quadrant",
            });
        }

        foreach (var quadrant in quadrants)
        {
            var label = ToPlainText(quadrant.Label);
            if (hasPoints)
            {
                AddText(plot, measurer, label, quadrant.X + 0.25, quadrant.Y + 0.5, new TextStyle(chart.QuadrantLabelFontSize, quadrant.TextFill, SceneTextAnchor.Middle, Hanging: true, Rotated: false, OffsetX: 0, OffsetY: chart.QuadrantTextTopPadding, "quadrant-label"));
            }
            else
            {
                AddText(plot, measurer, label, quadrant.X + 0.25, quadrant.Y + 0.25, new TextStyle(chart.QuadrantLabelFontSize, quadrant.TextFill, SceneTextAnchor.Middle, Hanging: false, Rotated: false, OffsetX: 0, OffsetY: 0, "quadrant-label"));
            }
        }

        // The borders: the lines between the quadrants, then the border around them
        if (chart.InternalBorderWidth > 0)
        {
            plot.Add(new RuleXMark(0.5) { StrokeColor = colors.InternalBorderStrokeFill, StrokeWidth = chart.InternalBorderWidth, CssClass = "border" });
            plot.Add(new RuleYMark(0.5) { StrokeColor = colors.InternalBorderStrokeFill, StrokeWidth = chart.InternalBorderWidth, CssClass = "border" });
        }

        if (chart.ExternalBorderWidth > 0)
        {
            plot.Add(new RectMark<int>([0]) { Inset = 0, FillColor = SceneColor.Transparent, StrokeColor = colors.ExternalBorderStrokeFill, StrokeWidth = chart.ExternalBorderWidth, CssClass = "border" });
        }

        // Mermaid draws the last point first: the first points are on top
        for (var i = chart.Points.Count - 1; i >= 0; i--)
        {
            var point = chart.Points[i];
            var classStyle = point.ClassName is not null && chart.ClassDefinitions.TryGetValue(point.ClassName, out var style) ? style : null;
            var radius = point.Style.Radius ?? classStyle?.Radius ?? chart.PointRadius;
            var fill = point.Style.Color ?? classStyle?.Color ?? colors.PointFill;
            var strokeWidth = point.Style.StrokeWidth ?? classStyle?.StrokeWidth ?? 0;
            plot.Add(new DotMark<QuadrantChartPoint>([point], row => row.X, row => row.Y)
            {
                Radius = radius,
                FillColor = fill,
                StrokeColor = strokeWidth > 0 ? point.Style.StrokeColor ?? classStyle?.StrokeColor ?? colors.PointFill : (SceneColor?)null,
                StrokeWidth = strokeWidth > 0 ? strokeWidth : null,
                CssClass = "data-point",
            });
        }

        // The texts of the points, below their center
        if (hasPoints)
        {
            AddText(plot, measurer, [.. chart.Points], point => ToPlainText(point.Label), point => point.X, point => point.Y, new TextStyle(chart.PointLabelFontSize, colors.PointTextFill, SceneTextAnchor.Middle, Hanging: true, Rotated: false, OffsetX: 0, OffsetY: chart.PointTextPadding, "data-point-label"));
        }

        // The texts of the axes: in the middle of the halves when the axis has two texts, at the start of the axis otherwise
        if (showXAxis)
        {
            var inMiddle = xAxisRight.Length > 0;
            var anchor = inMiddle ? SceneTextAnchor.Middle : SceneTextAnchor.Start;
            var y = xAxisAtBottom ? 0 : 1;
            var offsetY = xAxisAtBottom ? padding + chart.XAxisLabelPadding : chart.XAxisLabelPadding - padding - xAxisSpace;
            var textStyle = new TextStyle(chart.XAxisLabelFontSize, colors.XAxisTextFill, anchor, Hanging: true, Rotated: false, OffsetX: 0, offsetY, "axis-label");
            AddText(plot, measurer, xAxisLeft, inMiddle ? 0.25 : 0, y, textStyle);
            AddText(plot, measurer, xAxisRight, 0.75, y, textStyle);
        }

        if (showYAxis)
        {
            var inMiddle = yAxisTop.Length > 0;
            var anchor = inMiddle ? SceneTextAnchor.Middle : SceneTextAnchor.Start;
            var x = yAxisAtRight ? 1 : 0;
            var offsetX = yAxisAtRight ? padding + chart.YAxisLabelPadding : chart.YAxisLabelPadding - left;
            var textStyle = new TextStyle(chart.YAxisLabelFontSize, colors.YAxisTextFill, anchor, Hanging: true, Rotated: true, offsetX, OffsetY: 0, "axis-label");
            AddText(plot, measurer, yAxisBottom, x, inMiddle ? 0.25 : 0, textStyle);
            AddText(plot, measurer, yAxisTop, x, 0.75, textStyle);
        }
    }

    /// <summary>The colors of the chart: the ones of <see cref="QuadrantChart.Colors"/>, or the ones Mermaid derives from the theme.</summary>
    public static QuadrantChartResolvedColors Resolve(QuadrantChartColors colors, DiagramTheme theme)
    {
        var primary = theme.PrimaryColor;
        var primaryText = theme.PrimaryTextColor;
        return new QuadrantChartResolvedColors(
            Quadrant1Fill: colors.Quadrant1Fill ?? primary,
            Quadrant2Fill: colors.Quadrant2Fill ?? Adjust(primary, 5),
            Quadrant3Fill: colors.Quadrant3Fill ?? Adjust(primary, 10),
            Quadrant4Fill: colors.Quadrant4Fill ?? Adjust(primary, 15),
            Quadrant1TextFill: colors.Quadrant1TextFill ?? primaryText,
            Quadrant2TextFill: colors.Quadrant2TextFill ?? Adjust(primaryText, -5),
            Quadrant3TextFill: colors.Quadrant3TextFill ?? Adjust(primaryText, -10),
            Quadrant4TextFill: colors.Quadrant4TextFill ?? Adjust(primaryText, -15),
            PointFill: colors.PointFill ?? DefaultPointFill(theme),
            PointTextFill: colors.PointTextFill ?? primaryText,
            XAxisTextFill: colors.XAxisTextFill ?? primaryText,
            YAxisTextFill: colors.YAxisTextFill ?? primaryText,
            InternalBorderStrokeFill: colors.InternalBorderStrokeFill ?? theme.PrimaryBorderColor,
            ExternalBorderStrokeFill: colors.ExternalBorderStrokeFill ?? theme.PrimaryBorderColor);
    }

    /// <summary>Adds an amount to the red, green and blue channels of a color, as the <c>adjust</c> function of Mermaid.</summary>
    public static SceneColor Adjust(SceneColor color, int amount)
    {
        return new SceneColor(Clamp(color.R + amount), Clamp(color.G + amount), Clamp(color.B + amount), color.A);

        static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }

    /// <summary>
    /// Returns the text of a label without its markdown emphasis, which is not drawn: <c>**</c>, <c>__</c>, <c>*</c> and
    /// <c>_</c> are removed, except an underscore inside a word, and a backslash escapes the next character.
    /// </summary>
    public static string ToPlainText(DiagramText? text)
    {
        if (text is null)
            return "";

        if (text.Format is not DiagramTextFormat.Markdown)
            return text.Text;

        var value = text.Text;
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '\\' && i + 1 < value.Length && value[i + 1] is '*' or '_' or '\\')
            {
                builder.Append(value[i + 1]);
                i++;
                continue;
            }

            if (c is '*' or '_')
            {
                // An underscore inside a word is a character, as in snake_case
                if (c is '_' && i > 0 && i + 1 < value.Length && char.IsLetterOrDigit(value[i - 1]) && char.IsLetterOrDigit(value[i + 1]))
                {
                    builder.Append(c);
                }

                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The default fill of the points. Mermaid computes an invalid color for it (<c>darken</c> without an amount), which
    /// the browsers draw black; on a dark theme, the text color is used instead, as black points would not show.
    /// </summary>
    private static SceneColor DefaultPointFill(DiagramTheme theme) => theme.IsDark ? theme.PrimaryTextColor : SceneColor.Black;

    private static void AddText(Plot plot, ITextMeasurer measurer, string text, double x, double y, TextStyle style)
    {
        if (text.Length > 0)
        {
            AddText(plot, measurer, [text], row => row, _ => x, _ => y, style);
        }
    }

    /// <summary>
    /// Adds the texts of rows, one text mark per line: the text marks draw a line centered on its position, so the
    /// offsets place the first baseline at the top of the text (<see cref="TextStyle.Hanging"/>) or center the lines.
    /// The texts of the y axis are rotated by -90 degrees around their position, and read from the bottom.
    /// </summary>
    private static void AddText<T>(Plot plot, ITextMeasurer measurer, IReadOnlyList<T> rows, Func<T, string> text, Func<T, object?> x, Func<T, object?> y, TextStyle style)
    {
        var lines = new string[rows.Count][];
        var lineCount = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            lines[i] = text(rows[i]).Split('\n');
            lineCount = Math.Max(lineCount, lines[i].Length);
        }

        var metrics = measurer.Measure("", new SceneFont(style.FontSize));
        var lineHeight = metrics.Height * LineHeightFactor;
        var blockTop = style.Hanging ? 0 : -(((lineCount - 1) * lineHeight) + metrics.Height) / 2;
        var centering = (metrics.Ascent - metrics.Descent) / 2;
        for (var line = 0; line < lineCount; line++)
        {
            var index = line;
            var lineRows = Enumerable.Range(0, rows.Count).Where(i => index < lines[i].Length && lines[i][index].Length > 0).ToArray();
            if (lineRows.Length is 0)
                continue;

            var baseline = blockTop + (line * lineHeight) + metrics.Ascent;
            plot.Add(new TextMark<int>(lineRows, i => lines[i][index], i => x(rows[i]), i => y(rows[i]))
            {
                FontSize = style.FontSize,
                FillColor = style.Color,
                Anchor = style.Anchor,
                Rotation = style.Rotated ? -90 : 0,
                OffsetX = style.OffsetX + (style.Rotated ? baseline : 0),
                OffsetY = style.OffsetY + (style.Rotated ? 0 : baseline) - centering,
                CssClass = style.CssClass,
            });
        }
    }

    /// <summary>How a text is drawn. The offsets are in pixels from the position of the text, before the lines are placed.</summary>
    private sealed record TextStyle(double FontSize, SceneColor Color, SceneTextAnchor Anchor, bool Hanging, bool Rotated, double OffsetX, double OffsetY, string CssClass);
}
