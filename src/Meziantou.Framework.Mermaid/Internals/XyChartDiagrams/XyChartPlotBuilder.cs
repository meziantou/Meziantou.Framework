using System.Globalization;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.Charts;

namespace Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;

/// <summary>
/// Builds the plot that draws an <see cref="XyChart"/>: a bar mark or a line mark per series, over a band scale of the
/// categories or a linear scale of numbers, in the colors of the palette of the XY charts of Mermaid.
/// </summary>
internal static class XyChartPlotBuilder
{
    // The width of the lines and the size and the distance of the labels of the points, as in Mermaid
    private const double LineWidth = 2;
    private const double PointLabelFontSize = 12;
    private const double PointLabelOffset = 10;
    private const double DataLabelGap = 4;

    // The plotColorPalette of the xyChart theme variables of each theme of Mermaid
    private static readonly SceneColor[] DefaultPalette = Parse("#ECECFF", "#8493A6", "#FFC3A0", "#DCDDE1", "#B8E994", "#D1A36F", "#C3CDE6", "#FFB6C1", "#496078", "#F8F3E3");
    private static readonly SceneColor[] DarkPalette = Parse("#3498db", "#2ecc71", "#e74c3c", "#f1c40f", "#bdc3c7", "#ffffff", "#34495e", "#9b59b6", "#1abc9c", "#e67e22");
    private static readonly SceneColor[] ForestPalette = Parse("#CDE498", "#FF6B6B", "#A0D2DB", "#D7BDE2", "#F0F0F0", "#FFC3A0", "#7FD8BE", "#FF9A8B", "#FAF3E0", "#FFF176");
    private static readonly SceneColor[] NeutralPalette = Parse("#EEE", "#6BB8E4", "#8ACB88", "#C7ACD6", "#E8DCC2", "#FFB2A8", "#FFF380", "#7E8D91", "#FFD8B1", "#FAF3E0");
    private static readonly SceneColor[] BasePalette = Parse("#FFF4DD", "#FFD8B1", "#FFA07A", "#ECEFF1", "#D6DBDF", "#C3E0A8", "#FFB6A4", "#FFD74D", "#738FA7", "#FFFFF0");

    public static void Configure(XyChart chart, Plot plot)
    {
        var palette = GetPalette(chart);
        var horizontal = chart.Orientation is XyChartOrientation.Horizontal;
        plot.Width = chart.Width;
        plot.Height = chart.Height;
        if (!chart.ShowTitle)
        {
            plot.Title = null;
        }

        plot.Theme = CreateTheme(chart, plot.Theme, palette);
        if (horizontal)
        {
            plot.Coordinates = new CartesianCoordinates { Transposed = true };
        }

        var getX = ConfigureXScale(chart, plot);
        var yRange = ConfigureYScale(chart, plot);
        ConfigureAxis(plot.XAxis, chart.XAxis, plot.XScale, rotateLabels: !horizontal);
        ConfigureAxis(plot.YAxis, chart.YAxis, plot.YScale, rotateLabels: false);

        // The baseline of the bars: zero, or the end of the range of the y axis closest to it
        var baseline = yRange is var (low, high) ? Math.Clamp(0, low, high) : 0;
        var reversed = chart.YAxis.Range is { } range && range.Start > range.End;
        var legendNames = new List<string>();
        var legendColors = new List<SceneColor>();
        var dataLabels = new List<XyChartDatum>[2] { [], [] };
        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var color = palette[index % palette.Length];
            var rows = CreateRows(series, getX);
            if (yRange is var (minimum, maximum) && rows.Exists(row => row.Y < minimum || row.Y > maximum))
            {
                // A value out of the range of the y axis stays in the plot area
                plot.Clip = true;
            }

            // A named series is listed in the legend, in its color; a name already listed is not listed again
            string? key = null;
            if (chart.ShowLegend && series.Name is { Length: > 0 } name && !legendNames.Contains(name, StringComparer.Ordinal))
            {
                key = name;
                legendNames.Add(name);
                legendColors.Add(color);
            }

            var cssClass = (series is XyChartLineSeries ? "line-plot-" : "bar-plot-") + index.ToString(CultureInfo.InvariantCulture);
            if (series is XyChartLineSeries)
            {
                var line = new LineMark<XyChartDatum>(rows, row => row.X, row => row.Y) { StrokeWidth = LineWidth, CssClass = cssClass };
                if (key is null)
                {
                    line.StrokeColor = color;
                }
                else
                {
                    line.Stroke = _ => key;
                }

                plot.Add(line);
                if (rows.Exists(row => !string.IsNullOrEmpty(row.Label)))
                {
                    plot.Add(new TextMark<XyChartDatum>(rows, row => row.Label, row => row.X, row => row.Y)
                    {
                        FillColor = color,
                        FontSize = PointLabelFontSize,
                        Anchor = horizontal ? SceneTextAnchor.Start : SceneTextAnchor.Middle,
                        OffsetX = horizontal ? PointLabelOffset : 0,
                        OffsetY = horizontal ? 0 : -PointLabelOffset,
                        CssClass = cssClass + "-labels",
                    });
                }
            }
            else
            {
                var bars = new BarMark<XyChartDatum>(rows, row => row.X, row => row.Y) { CssClass = cssClass };
                if (key is null)
                {
                    bars.FillColor = color;
                }
                else
                {
                    bars.Fill = _ => key;
                }

                plot.Add(bars);
                if (chart.ShowDataLabels)
                {
                    foreach (var row in rows)
                    {
                        if (!double.IsNaN(row.Y))
                        {
                            // The labels of the bars that go up (or right), then of those that go down (or left)
                            dataLabels[(row.Y >= baseline) != reversed ? 0 : 1].Add(row with { Label = JavaScriptNumber.ToString(row.Y) });
                        }
                    }
                }
            }
        }

        for (var direction = 0; direction < dataLabels.Length; direction++)
        {
            if (dataLabels[direction].Count > 0)
            {
                plot.Add(CreateDataLabels(chart, plot.Theme, dataLabels[direction], horizontal, forward: direction is 0));
            }
        }

        if (legendNames.Count > 0)
        {
            plot.ColorScale = new OrdinalColorScale { Domain = legendNames, Palette = legendColors };
            plot.Legend.Visible = true;
            plot.Legend.Position = LegendPosition.Right;
        }
        else
        {
            plot.Legend.Visible = false;
        }
    }

    private static ChartTheme CreateTheme(XyChart chart, ChartTheme theme, SceneColor[] palette)
    {
        // Charts has a color for each kind of element: the first color the chart sets for the elements of a kind applies to all of them
        return theme with
        {
            Background = chart.BackgroundColor ?? theme.Background,
            Foreground = chart.TitleColor ?? chart.LegendTextColor ?? chart.XAxis.TitleColor ?? chart.YAxis.TitleColor ?? theme.Foreground,
            MutedForeground = chart.XAxis.LabelColor ?? chart.YAxis.LabelColor ?? theme.MutedForeground,
            AxisColor = chart.XAxis.LineColor ?? chart.YAxis.LineColor ?? chart.XAxis.TickColor ?? chart.YAxis.TickColor ?? theme.AxisColor,
            Palette = palette,
            TitleFontSize = chart.TitleFontSize,
            TickFontSize = Math.Max(chart.XAxis.LabelFontSize, chart.YAxis.LabelFontSize),
            FontSize = Math.Max(Math.Max(chart.XAxis.TitleFontSize, chart.YAxis.TitleFontSize), chart.LegendFontSize),
        };
    }

    private static SceneColor[] GetPalette(XyChart chart)
    {
        if (chart.PlotColors.Count > 0)
            return [.. chart.PlotColors];

        var theme = chart.Theme;
        return theme switch
        {
            _ when ReferenceEquals(theme, DiagramTheme.Dark) => DarkPalette,
            _ when ReferenceEquals(theme, DiagramTheme.Forest) => ForestPalette,
            _ when ReferenceEquals(theme, DiagramTheme.Neutral) => NeutralPalette,
            _ when ReferenceEquals(theme, DiagramTheme.Base) => BasePalette,
            _ when theme.IsDark => DarkPalette,
            _ => DefaultPalette,
        };
    }

    /// <summary>Sets the x scale, and returns the position on the x axis of a value of a series, from its index and the number of values of the series.</summary>
    private static Func<int, int, object?> ConfigureXScale(XyChart chart, Plot plot)
    {
        var axis = chart.XAxis;
        if (axis.Categories.Count > 0)
        {
            // The value at an index belongs to the category at the same index, and the values past the last category are not drawn
            var categories = axis.Categories.ToArray();
            plot.XScale = new BandScale { Domain = categories.Distinct(StringComparer.Ordinal).ToArray() };
            return (_, index) => index < categories.Length ? categories[index] : null;
        }

        double start;
        double end;
        if (axis.Range is { } range && double.IsFinite(range.Start) && double.IsFinite(range.End))
        {
            (start, end) = (range.Start, range.End);
        }
        else if (chart.Series.FirstOrDefault(series => series.Values.Count > 0) is { } first)
        {
            // Without values on the x axis, the points of the first series are numbered from 1
            (start, end) = (1, first.Values.Count);
        }
        else
        {
            plot.XScale = new BandScale { Domain = [] };
            return (_, _) => null;
        }

        var low = Math.Min(start, end);
        var high = Math.Max(start, end);
        if (low == high)
        {
            // A single value is in the middle of the axis
            var delta = low == 0 ? 1 : Math.Abs(low) * 0.1;
            (low, high) = (low - delta, high + delta);
        }
        else if (chart.Series.Where(series => series is XyChartBarSeries).Select(series => series.Values.Count).DefaultIfEmpty(0).Max() is var count and > 0)
        {
            // The bars at the ends of the axis stay in the plot area: half the distance between two bars on each side
            var padding = (count > 1 ? (high / (count - 1)) - (low / (count - 1)) : (high / 10) - (low / 10)) / 2;
            if (double.IsFinite(low - padding) && double.IsFinite(high + padding))
            {
                (low, high) = (low - padding, high + padding);
            }
        }

        plot.XScale = new LinearScale { Minimum = low, Maximum = high, Reverse = start > end, Nice = false, Zero = false };

        // The values of each series spread from the start to the end of the axis
        return (count, index) => count > 1 ? Interpolate(start, end, index / (double)(count - 1)) : start;
    }

    /// <summary>Sets the y scale, and returns the bounds of the range of the y axis, if it has one.</summary>
    private static (double Low, double High)? ConfigureYScale(XyChart chart, Plot plot)
    {
        if (chart.YAxis.Range is { } range && double.IsFinite(range.Start) && double.IsFinite(range.End))
        {
            var low = Math.Min(range.Start, range.End);
            var high = Math.Max(range.Start, range.End);
            plot.YScale = new LinearScale { Minimum = low, Maximum = high, Reverse = range.Start > range.End, Nice = false, Zero = false };
            return (low, high);
        }

        // Charts fits the values, from zero when there are bars
        plot.YScale = new LinearScale();
        return null;
    }

    private static void ConfigureAxis(Axis axis, XyChartAxis options, Scale? scale, bool rotateLabels)
    {
        var hasTitle = options.ShowTitle && !string.IsNullOrEmpty(options.Title);
        axis.Label = hasTitle ? options.Title : null;
        axis.TickSize = options.ShowTicks ? options.TickLength : 0;
        axis.Line = options.ShowLine;
        if (rotateLabels && options.LabelRotation != 0)
        {
            axis.TickRotation = options.LabelRotation;
        }

        if (!options.ShowLabels && scale is ContinuousScale continuous)
        {
            continuous.TickFormat = _ => string.Empty;
        }

        if (!options.ShowLabels && !options.ShowTicks && !options.ShowLine && !hasTitle)
        {
            axis.Visible = false;
        }
    }

    private static List<XyChartDatum> CreateRows(XyChartSeries series, Func<int, int, object?> getX)
    {
        var count = series.Values.Count;
        var labels = (series as XyChartLineSeries)?.PointLabels;
        var rows = new List<XyChartDatum>(count);
        for (var index = 0; index < count; index++)
        {
            if (getX(count, index) is not { } x)
                continue;

            // A value that is not finite is missing: it is not drawn, and breaks the line
            var value = series.Values[index];
            var label = labels is not null && index < labels.Count ? labels[index] : null;
            rows.Add(new XyChartDatum(x, double.IsFinite(value) ? value : double.NaN, label));
        }

        return rows;
    }

    private static TextMark<XyChartDatum> CreateDataLabels(XyChart chart, ChartTheme theme, List<XyChartDatum> rows, bool horizontal, bool forward)
    {
        // The label is past the end of the bar, or inside the bar against its end
        var outside = chart.DataLabelsOutsideBars;
        var sign = forward == outside ? 1 : -1;
        var mark = new TextMark<XyChartDatum>(rows, row => row.Label, row => row.X, row => row.Y)
        {
            FillColor = chart.DataLabelColor,
            FontSize = theme.TickFontSize,
            CssClass = "data-labels",
        };

        if (horizontal)
        {
            mark.Anchor = sign > 0 ? SceneTextAnchor.Start : SceneTextAnchor.End;
            mark.OffsetX = sign * DataLabelGap;
        }
        else
        {
            mark.OffsetY = -sign * ((theme.TickFontSize / 2) + DataLabelGap);
        }

        return mark;
    }

    private static double Interpolate(double start, double end, double t) => (start * (1 - t)) + (end * t);

    private static SceneColor[] Parse(params string[] colors) => [.. colors.Select(SceneColor.Parse)];
}
