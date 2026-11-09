using System.Globalization;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the XY charts as Mermaid does: the plot takes a part of the chart, the title, the axes and the legend share the
/// rest, and the plot grows over what they leave. Bars start at the bottom of the plot and lines join their points.
/// </summary>
internal static class XyChartDiagramRenderer
{
    private const double LegendMarkerToFontRatio = 0.75;
    private const double LegendItemSpacingToFontRatio = 0.5;
    private const double LegendMarkerSpacingToFontRatio = 0.35;
    private const double BarPaddingPercent = 0.05;
    private const double LineWidth = 2;
    private const double PointLabelFontSize = 12;
    private const double PointLabelOffset = 10;

    // Mermaid sizes the values of the bars with the width of a character, 0.7 times the size of the font
    private const double DataLabelCharacterWidth = 0.7;
    private const double DataLabelOffset = 10;

    private static readonly string[] DefaultPalette = ["#ECECFF", "#8493A6", "#FFC3A0", "#DCDDE1", "#B8E994", "#D1A36F", "#C3CDE6", "#FFB6C1", "#496078", "#F8F3E3"];
    private static readonly string[] DarkPalette = ["#3498db", "#2ecc71", "#e74c3c", "#f1c40f", "#bdc3c7", "#ffffff", "#34495e", "#9b59b6", "#1abc9c", "#e67e22"];
    private static readonly string[] ForestPalette = ["#CDE498", "#FF6B6B", "#A0D2DB", "#D7BDE2", "#F0F0F0", "#FFC3A0", "#7FD8BE", "#FF9A8B", "#FAF3E0", "#FFF176"];
    private static readonly string[] NeutralPalette = ["#EEE", "#6BB8E4", "#8ACB88", "#C7ACD6", "#E8DCC2", "#FFB2A8", "#FFF380", "#7E8D91", "#FFD8B1", "#FAF3E0"];
    private static readonly string[] BasePalette = ["#FFF4DD", "#FFD8B1", "#FFA07A", "#ECEFF1", "#D6DBDF", "#C3E0A8", "#FFB6A4", "#FFD74D", "#738FA7", "#FFFFF0"];

    public static SceneBounds Render(XyChartDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var text = new XyChartText(context.Measurer);
        var textColor = theme.PrimaryTextColor;
        var palette = GetPalette(diagram, theme);
        var (xAxis, plots) = CreatePlots(diagram, text);
        var yAxis = CreateYAxis(diagram, text, plots);
        var legendItems = diagram.ShowLegend ? plots.Where(plot => !string.IsNullOrEmpty(plot.Series.Name)).ToList() : [];
        var title = diagram.ShowTitle && !string.IsNullOrEmpty(diagram.Title) ? diagram.Title : null;

        // The layout of Mermaid: the plot reserves its part, then the title, the axes and the legend take what they need
        var width = diagram.Width;
        var height = diagram.Height;
        var availableWidth = width;
        var availableHeight = height;
        var chartWidth = Math.Floor(width * diagram.PlotReservedSpacePercent / 100);
        var chartHeight = Math.Floor(height * diagram.PlotReservedSpacePercent / 100);
        availableWidth -= chartWidth;
        availableHeight -= chartHeight;

        var (titleWidth, titleHeight) = title is null ? default : text.MeasureMax([title], diagram.TitleFontSize);
        var titleSpace = titleHeight + (2 * diagram.TitlePadding);
        if (title is null || titleSpace > availableHeight)
        {
            title = null;
            titleSpace = 0;
        }

        availableHeight -= titleSpace;
        double plotX;
        double plotY;
        var legend = default((double Width, double Height));
        var horizontal = diagram.Orientation is XyChartOrientation.Horizontal;
        if (horizontal)
        {
            xAxis.SetPosition(XyChartAxisPosition.Left);
            var (xAxisWidth, _) = xAxis.CalculateSpace(availableWidth, availableHeight);
            availableWidth -= xAxisWidth;
            plotX = xAxisWidth;
            yAxis.SetPosition(XyChartAxisPosition.Top);
            var (_, yAxisHeight) = yAxis.CalculateSpace(availableWidth, availableHeight);
            availableHeight -= yAxisHeight;
            plotY = titleSpace + yAxisHeight;
        }
        else
        {
            plotY = titleSpace;
            xAxis.SetPosition(XyChartAxisPosition.Bottom);
            var (_, xAxisHeight) = xAxis.CalculateSpace(availableWidth, availableHeight);
            availableHeight -= xAxisHeight;
            yAxis.SetPosition(XyChartAxisPosition.Left);
            var (yAxisWidth, _) = yAxis.CalculateSpace(availableWidth, availableHeight);
            plotX = yAxisWidth;
            availableWidth -= yAxisWidth;
        }

        if (legendItems.Count > 0)
        {
            legend = MeasureLegend(diagram, text, legendItems, availableWidth, chartHeight);
            if (legend == default)
            {
                legendItems.Clear();
            }
        }

        availableWidth -= legend.Width;
        if (availableWidth > 0)
        {
            chartWidth += availableWidth;
        }

        if (availableHeight > 0)
        {
            chartHeight += availableHeight;
        }

        var plotBounds = new SceneBounds(plotX, plotY, chartWidth, chartHeight);
        var legendX = plotX + chartWidth;
        var legendY = plotY + Math.Max((chartHeight - legend.Height) / 2, 0);
        if (horizontal)
        {
            yAxis.SetRange(plotX, plotX + chartWidth);
            yAxis.SetLocation(plotX, titleSpace);
            xAxis.SetRange(plotY, plotY + chartHeight);
            xAxis.SetLocation(0, plotY);
        }
        else
        {
            xAxis.SetRange(plotX, plotX + chartWidth);
            xAxis.SetLocation(plotX, plotY + chartHeight);
            yAxis.SetRange(plotY, plotY + chartHeight);
            yAxis.SetLocation(0, plotY);
        }

        if (plots.Exists(plot => plot.Series is XyChartBarSeries))
        {
            xAxis.RecalculateOuterPaddingToDrawBar();
        }

        var background = diagram.BackgroundColor ?? theme.Background;
        if (background is { } backgroundColor)
        {
            group.Add(new SceneRectangle(0, 0, width, height) { Fill = backgroundColor, CssClass = "background" });
        }

        if (title is not null)
        {
            // The title is centered over the plot, but stays inside the chart
            var halfWidth = Math.Min(titleWidth, width) / 2;
            var center = Math.Min(Math.Max(plotX + (chartWidth / 2), halfWidth), width - halfWidth);
            var titleGroup = group.Add(new SceneGroup { CssClass = "chart-title" });
            text.Draw(titleGroup, title, center, titleSpace / 2, diagram.TitleFontSize, diagram.TitleColor ?? textColor, XyChartTextBaseline.Middle, SceneTextAnchor.Middle, rotation: 0);
        }

        var plotGroup = group.Add(new SceneGroup { CssClass = "plot" });
        for (var i = 0; i < plots.Count; i++)
        {
            var plot = plots[i];
            var color = palette[plot.Index % palette.Count];
            if (plot.Series is XyChartLineSeries line)
            {
                DrawLine(plotGroup, plot, line, i, xAxis, yAxis, color, horizontal, text);
            }
            else
            {
                DrawBars(plotGroup, plot, i, xAxis, yAxis, plotBounds, color, horizontal, diagram, diagram.DataLabelColor ?? textColor);
            }
        }

        if (legendItems.Count > 0)
        {
            DrawLegend(group, diagram, text, legendItems, palette, legendX, legendY, diagram.LegendTextColor ?? textColor);
        }

        xAxis.Draw(group, GetColors(diagram.XAxis, textColor));
        yAxis.Draw(group, GetColors(diagram.YAxis, textColor));
        return new SceneBounds(0, 0, width, height);
    }

    private static XyChartAxisColors GetColors(XyChartAxis axis, SceneColor textColor) => new(axis.TitleColor ?? textColor, axis.LabelColor ?? textColor, axis.TickColor ?? textColor, axis.LineColor ?? textColor);

    private static List<SceneColor> GetPalette(XyChartDiagram diagram, DiagramTheme theme)
    {
        if (diagram.PlotColors.Count > 0)
            return [.. diagram.PlotColors];

        var colors = theme switch
        {
            _ when ReferenceEquals(theme, DiagramTheme.Dark) => DarkPalette,
            _ when ReferenceEquals(theme, DiagramTheme.Forest) => ForestPalette,
            _ when ReferenceEquals(theme, DiagramTheme.Neutral) => NeutralPalette,
            _ when ReferenceEquals(theme, DiagramTheme.Base) => BasePalette,
            _ when theme.IsDark => DarkPalette,
            _ => DefaultPalette,
        };

        return [.. colors.Select(SceneColor.Parse)];
    }

    /// <summary>Creates the x axis and places the values of the series on it, as the database of Mermaid does.</summary>
    private static (XyChartAxisLayout Axis, List<XyChartPlot> Plots) CreatePlots(XyChartDiagram diagram, XyChartText text)
    {
        var axis = diagram.XAxis;
        var plots = new List<XyChartPlot>(diagram.Series.Count);
        if (axis.Categories.Count > 0)
        {
            var categories = axis.Categories.ToList();
            foreach (var series in diagram.Series)
            {
                // The values past the last category are not drawn, and a category without value has no point
                var points = new List<XyChartPoint>();
                for (var i = 0; i < categories.Count && i < series.Values.Count; i++)
                {
                    points.Add(new XyChartPoint(i, categories[i], double.NaN, series.Values[i]));
                }

                plots.Add(new XyChartPlot(plots.Count, series, points));
            }

            return (XyChartAxisLayout.CreateBand(axis, axis.Title, text, categories), plots);
        }

        double start;
        double end;
        if (axis.Range is { } range)
        {
            (start, end) = (range.Start, range.End);
        }
        else if (diagram.Series.FirstOrDefault(series => series.Values.Count > 0) is { } first)
        {
            // Without values on the x axis, the points of the first series are numbered from 1
            (start, end) = (1, first.Values.Count);
        }
        else
        {
            return (XyChartAxisLayout.CreateBand(axis, axis.Title, text, []), diagram.Series.Select((series, index) => new XyChartPlot(index, series, [])).ToList());
        }

        foreach (var series in diagram.Series)
        {
            var count = series.Values.Count;
            var step = count > 1 ? (end - start) / (count - 1) : 0;
            var points = new List<XyChartPoint>(count);
            for (var i = 0; i < count; i++)
            {
                points.Add(new XyChartPoint(i, Category: null, start + (i * step), series.Values[i]));
            }

            plots.Add(new XyChartPlot(plots.Count, series, points));
        }

        return (XyChartAxisLayout.CreateLinear(axis, axis.Title, text, start, end), plots);
    }

    private static XyChartAxisLayout CreateYAxis(XyChartDiagram diagram, XyChartText text, List<XyChartPlot> plots)
    {
        var axis = diagram.YAxis;
        if (axis.Range is { } range)
            return XyChartAxisLayout.CreateLinear(axis, axis.Title, text, range.Start, range.End);

        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var plot in plots)
        {
            foreach (var point in plot.Points)
            {
                if (double.IsFinite(point.Value))
                {
                    minimum = Math.Min(minimum, point.Value);
                    maximum = Math.Max(maximum, point.Value);
                }
            }
        }

        return XyChartAxisLayout.CreateLinear(axis, axis.Title, text, minimum, maximum);
    }

    private static double GetPosition(XyChartAxisLayout axis, XyChartPoint point) => point.Category is null ? axis.GetPosition(point.X) : axis.GetPosition(point.Category);

    private static void DrawBars(SceneGroup group, XyChartPlot plot, int index, XyChartAxisLayout xAxis, XyChartAxisLayout yAxis, SceneBounds plotBounds, SceneColor color, bool horizontal, XyChartDiagram diagram, SceneColor dataLabelColor)
    {
        var barGroup = group.Add(new SceneGroup { CssClass = "bar-plot-" + index.ToString(CultureInfo.InvariantCulture) });
        var barWidth = Math.Min(xAxis.OuterPadding * 2, xAxis.GetTickDistance()) * (1 - BarPaddingPercent);
        var bars = new List<(SceneBounds Bounds, string Label)>();
        foreach (var point in plot.Points)
        {
            var x = GetPosition(xAxis, point);
            var y = yAxis.GetPosition(point.Value);
            var bounds = horizontal
                ? new SceneBounds(plotBounds.X, x - (barWidth / 2), y - plotBounds.X, barWidth)
                : new SceneBounds(x - (barWidth / 2), y, barWidth, plotBounds.Bottom - y);

            // A bar below the start of the axis has a negative size, which SVG does not draw
            if (!(bounds.Width > 0 && bounds.Height > 0) || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
                continue;

            barGroup.Add(new SceneRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height) { Fill = color, Stroke = color, StrokeWidth = 0 });
            bars.Add((bounds, XyChartNumbers.ToJavaScriptString(point.Value)));
        }

        if (diagram.ShowDataLabels && bars.Count > 0)
        {
            DrawDataLabels(barGroup, bars, horizontal, diagram.DataLabelsOutsideBars, dataLabelColor);
        }
    }

    /// <summary>Writes the values of the bars, in the largest font that fits all of them, as Mermaid does.</summary>
    private static void DrawDataLabels(SceneGroup group, List<(SceneBounds Bounds, string Label)> bars, bool horizontal, bool outside, SceneColor color)
    {
        var fontSize = double.PositiveInfinity;
        foreach (var (bounds, label) in bars)
        {
            fontSize = Math.Min(fontSize, horizontal ? GetHorizontalFontSize(bounds, label) : GetVerticalFontSize(bounds, label));
        }

        fontSize = Math.Floor(fontSize);
        if (!(fontSize >= 1) || !double.IsFinite(fontSize))
            return;

        var font = new SceneFont(fontSize);
        foreach (var (bounds, label) in bars)
        {
            if (horizontal)
            {
                var x = outside ? bounds.Right + DataLabelOffset : bounds.Right - DataLabelOffset;

                // dominant-baseline: middle
                group.Add(new SceneText(x, bounds.Y + (bounds.Height / 2) + (fontSize * 0.35), label, font, color) { Anchor = outside ? SceneTextAnchor.Start : SceneTextAnchor.End, CssClass = "data-label" });
            }
            else
            {
                // dominant-baseline: auto above the bar, hanging inside it
                var y = outside ? bounds.Y - DataLabelOffset : bounds.Y + DataLabelOffset + (fontSize * 0.8);
                group.Add(new SceneText(bounds.X + (bounds.Width / 2), y, label, font, color) { Anchor = SceneTextAnchor.Middle, CssClass = "data-label" });
            }
        }
    }

    private static double GetVerticalFontSize(SceneBounds bounds, string label)
    {
        // The font decreases from the width of the bar until the text fits in it
        var fontSize = bounds.Width / (label.Length * DataLabelCharacterWidth);
        var limit = Math.Min(bounds.Width / (label.Length * DataLabelCharacterWidth), bounds.Height - DataLabelOffset);
        fontSize = Skip(fontSize, limit);
        var steps = 0;
        while (!FitsVertically(bounds, label, fontSize) && fontSize > 0 && steps++ < 4)
        {
            fontSize--;
        }

        return fontSize;
    }

    private static bool FitsVertically(SceneBounds bounds, string label, double fontSize)
    {
        var textWidth = fontSize * label.Length * DataLabelCharacterWidth;
        var centerX = bounds.X + (bounds.Width / 2);
        var horizontalFits = centerX - (textWidth / 2) >= bounds.X && centerX + (textWidth / 2) <= bounds.Right;
        var verticalFits = bounds.Y + DataLabelOffset + fontSize <= bounds.Bottom;
        return horizontalFits && verticalFits;
    }

    private static double GetHorizontalFontSize(SceneBounds bounds, string label)
    {
        var fontSize = bounds.Height * 0.7;
        var limit = (bounds.Width - DataLabelOffset) / (label.Length * DataLabelCharacterWidth);
        fontSize = Skip(fontSize, limit);
        var steps = 0;
        while (fontSize * label.Length * DataLabelCharacterWidth > bounds.Width - DataLabelOffset && fontSize > 0 && steps++ < 4)
        {
            fontSize--;
        }

        return fontSize;
    }

    /// <summary>Skips the steps of one pixel that Mermaid takes down to a limit, or to zero.</summary>
    private static double Skip(double fontSize, double limit)
    {
        if (!(fontSize > limit) || !double.IsFinite(fontSize))
            return fontSize;

        var steps = Math.Ceiling(fontSize - limit) - 1;
        var toZero = Math.Ceiling(fontSize) - 1;
        steps = Math.Max(0, Math.Min(steps, toZero));
        return fontSize - steps;
    }

    private static void DrawLine(SceneGroup group, XyChartPlot plot, XyChartLineSeries line, int index, XyChartAxisLayout xAxis, XyChartAxisLayout yAxis, SceneColor color, bool horizontal, XyChartText text)
    {
        var lineGroup = group.Add(new SceneGroup { CssClass = "line-plot-" + index.ToString(CultureInfo.InvariantCulture) });
        var builder = new PathBuilder();
        var points = new List<(double X, double Y, int Index)>();
        var drawing = false;
        foreach (var point in plot.Points)
        {
            var x = GetPosition(xAxis, point);
            var y = yAxis.GetPosition(point.Value);
            var (pageX, pageY) = horizontal ? (y, x) : (x, y);
            if (!double.IsFinite(pageX) || !double.IsFinite(pageY))
            {
                // A value that is not a number cuts the line
                drawing = false;
                continue;
            }

            if (drawing)
            {
                builder.LineTo(pageX, pageY);
            }
            else
            {
                builder.MoveTo(pageX, pageY);
                drawing = true;
            }

            points.Add((pageX, pageY, point.Index));
        }

        if (builder.IsEmpty)
            return;

        lineGroup.Add(new ScenePath(builder.Build()) { Stroke = color, StrokeWidth = LineWidth, LineJoin = SceneLineJoin.Miter });
        var labels = default(SceneGroup);
        foreach (var (x, y, pointIndex) in points)
        {
            if (pointIndex >= line.PointLabels.Count || line.PointLabels[pointIndex] is not { Length: > 0 } label)
                continue;

            labels ??= lineGroup.Add(new SceneGroup { CssClass = "labels" });
            if (horizontal)
            {
                text.Draw(labels, label, x + PointLabelOffset, y, PointLabelFontSize, color, XyChartTextBaseline.Middle, SceneTextAnchor.Start, rotation: 0);
            }
            else
            {
                text.Draw(labels, label, x, y - PointLabelOffset, PointLabelFontSize, color, XyChartTextBaseline.Middle, SceneTextAnchor.Middle, rotation: 0);
            }
        }
    }

    private static (double Width, double Height) MeasureLegend(XyChartDiagram diagram, XyChartText text, List<XyChartPlot> items, double availableWidth, double availableHeight)
    {
        var fontSize = diagram.LegendFontSize;
        var markerSize = fontSize * LegendMarkerToFontRatio;
        var markerSpacing = fontSize * LegendMarkerSpacingToFontRatio;
        var itemSpacing = fontSize * LegendItemSpacingToFontRatio;
        var (textWidth, _) = text.MeasureMax(items.Select(item => item.Series.Name!), fontSize);
        var width = (diagram.LegendPadding * 2) + markerSize + markerSpacing + textWidth;
        var height = (diagram.LegendPadding * 2) + (items.Count * fontSize) + ((items.Count - 1) * itemSpacing);
        return width <= availableWidth && height <= availableHeight ? (width, height) : default;
    }

    private static void DrawLegend(SceneGroup group, XyChartDiagram diagram, XyChartText text, List<XyChartPlot> items, List<SceneColor> palette, double x, double y, SceneColor textColor)
    {
        var legendGroup = group.Add(new SceneGroup { CssClass = "legend" });
        var fontSize = diagram.LegendFontSize;
        var markerSize = fontSize * LegendMarkerToFontRatio;
        var markerSpacing = fontSize * LegendMarkerSpacingToFontRatio;
        var rowHeight = fontSize + (fontSize * LegendItemSpacingToFontRatio);
        var startX = x + diagram.LegendPadding;
        var startY = y + diagram.LegendPadding;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var color = palette[item.Index % palette.Count];
            var top = startY + (i * rowHeight);
            if (item.Series is XyChartBarSeries)
            {
                legendGroup.Add(new SceneRectangle(startX, top, markerSize, markerSize) { Fill = color, Stroke = color, StrokeWidth = 0, CssClass = "marker" });
            }
            else
            {
                legendGroup.Add(new SceneLine(startX, top + (markerSize / 2), startX + markerSize, top + (markerSize / 2)) { Stroke = color, StrokeWidth = LineWidth, CssClass = "marker" });
            }

            text.Draw(legendGroup, item.Series.Name!, startX + markerSize + markerSpacing, top + (markerSize / 2), fontSize, textColor, XyChartTextBaseline.Middle, SceneTextAnchor.Start, rotation: 0);
        }
    }
}
