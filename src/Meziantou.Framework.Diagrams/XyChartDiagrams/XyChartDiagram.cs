using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// An XY chart: bars and lines over an x axis of categories or of numbers, and a y axis of numbers (Mermaid
/// <c>xychart</c>). The chart is laid out as Mermaid does, in a box of <see cref="Width"/> by <see cref="Height"/> pixels
/// that holds the title, the axes, the legend and the plot.
/// </summary>
public sealed class XyChartDiagram : Diagram
{
    private double _width = 700;
    private double _height = 500;
    private double _titleFontSize = 20;
    private double _titlePadding = 10;
    private double _legendFontSize = 14;
    private double _legendPadding = 10;
    private double _plotReservedSpacePercent = 50;

    /// <summary>Gets or sets the orientation of the chart. The default is <see cref="XyChartOrientation.Vertical"/>.</summary>
    public XyChartOrientation Orientation { get; set; }

    /// <summary>Gets the x axis: the categories or the numbers the values belong to.</summary>
    public XyChartAxis XAxis { get; } = new();

    /// <summary>Gets the y axis: the values.</summary>
    public XyChartAxis YAxis { get; } = new();

    /// <summary>Gets the series, drawn in order: a series hides the series before it where they overlap.</summary>
    public IList<XyChartSeries> Series { get; } = new List<XyChartSeries>();

    /// <summary>Gets or sets the width of the chart, in pixels. The default is 700.</summary>
    public double Width
    {
        get => _width;
        set => _width = Validate(value, 1, 100_000);
    }

    /// <summary>Gets or sets the height of the chart, in pixels. The default is 500.</summary>
    public double Height
    {
        get => _height;
        set => _height = Validate(value, 1, 100_000);
    }

    /// <summary>Gets or sets a value that indicates whether the <see cref="Diagram.Title"/> is drawn. The default is <see langword="true"/>.</summary>
    public bool ShowTitle { get; set; } = true;

    /// <summary>Gets or sets the size of the font of the title. The default is 20.</summary>
    public double TitleFontSize
    {
        get => _titleFontSize;
        set => _titleFontSize = Validate(value, 1, 1000);
    }

    /// <summary>Gets or sets the space above and below the title. The default is 10.</summary>
    public double TitlePadding
    {
        get => _titlePadding;
        set => _titlePadding = Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets a value that indicates whether the legend lists the named series. The default is <see langword="true"/>.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Gets or sets the size of the font of the legend. The default is 14.</summary>
    public double LegendFontSize
    {
        get => _legendFontSize;
        set => _legendFontSize = Validate(value, 1, 1000);
    }

    /// <summary>Gets or sets the space around the legend. The default is 10.</summary>
    public double LegendPadding
    {
        get => _legendPadding;
        set => _legendPadding = Validate(value, 0, 1000);
    }

    /// <summary>
    /// Gets or sets the part of the width and of the height of the chart that the plot takes at least, in percent. The
    /// default is 50: the title, the axes and the legend take the rest, and the plot grows over what they leave.
    /// </summary>
    public double PlotReservedSpacePercent
    {
        get => _plotReservedSpacePercent;
        set => _plotReservedSpacePercent = Validate(value, 1, 100);
    }

    /// <summary>Gets or sets a value that indicates whether the value of each bar is written on it. The default is <see langword="false"/>.</summary>
    public bool ShowDataLabels { get; set; }

    /// <summary>Gets or sets a value that indicates whether the values of the bars are written past their end, instead of inside them. The default is <see langword="false"/>.</summary>
    public bool DataLabelsOutsideBars { get; set; }

    /// <summary>Gets or sets the background of the chart, or <see langword="null"/> for the background of the theme.</summary>
    public SceneColor? BackgroundColor { get; set; }

    /// <summary>Gets or sets the color of the title, or <see langword="null"/> for the color of the texts of the theme.</summary>
    public SceneColor? TitleColor { get; set; }

    /// <summary>Gets or sets the color of the values written on the bars, or <see langword="null"/> for the color of the texts of the theme.</summary>
    public SceneColor? DataLabelColor { get; set; }

    /// <summary>Gets or sets the color of the texts of the legend, or <see langword="null"/> for the color of the texts of the theme.</summary>
    public SceneColor? LegendTextColor { get; set; }

    /// <summary>
    /// Gets the colors of the series, in order, or an empty list for the colors of the theme. The series take the
    /// colors in turn, starting over after the last one.
    /// </summary>
    public IList<SceneColor> PlotColors { get; } = new List<SceneColor>();

    /// <summary>Adds a series drawn as bars.</summary>
    /// <param name="values">The values.</param>
    /// <param name="name">The name, shown in the legend, or <see langword="null"/>.</param>
    /// <returns>The series.</returns>
    public XyChartBarSeries AddBars(IEnumerable<double> values, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var series = new XyChartBarSeries { Name = name };
        foreach (var value in values)
        {
            series.Values.Add(value);
        }

        Series.Add(series);
        return series;
    }

    /// <summary>Adds a series drawn as a line.</summary>
    /// <param name="values">The values.</param>
    /// <param name="name">The name, shown in the legend, or <see langword="null"/>.</param>
    /// <returns>The series.</returns>
    public XyChartLineSeries AddLine(IEnumerable<double> values, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var series = new XyChartLineSeries { Name = name };
        foreach (var value in values)
        {
            series.Values.Add(value);
        }

        Series.Add(series);
        return series;
    }

    private protected override bool DrawsTitle => true;

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => XyChartDiagramRenderer.Render(this, context, group);

    internal static double Validate(double value, double minimum, double maximum)
    {
        if (!(value >= minimum && value <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, $"The value must be between {minimum} and {maximum}.");

        return value;
    }
}
