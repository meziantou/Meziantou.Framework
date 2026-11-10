namespace Meziantou.Framework.Mermaid;

/// <summary>
/// An axis of an <see cref="XyChart"/>: its title, its values, and how it is drawn (Mermaid <c>x-axis</c> and
/// <c>y-axis</c>, and the <c>xAxis</c> and <c>yAxis</c> configuration).
/// </summary>
/// <remarks>
/// The x axis shows its <see cref="Categories"/> when it has some, the values of its <see cref="Range"/> otherwise, and
/// numbers the values of the first series from 1 when it has neither. The y axis shows the values of its
/// <see cref="Range"/>, or fits the values of the series, from zero when the chart has bars and with round ends; it has
/// no categories.
/// </remarks>
public sealed class XyChartAxis
{
    private double _labelFontSize = 14;
    private double _labelPadding = 5;
    private double _titleFontSize = 16;
    private double _titlePadding = 5;
    private double _tickLength = 5;
    private double _tickWidth = 2;
    private double _lineWidth = 2;
    private double _labelRotation;

    /// <summary>Gets or sets the title of the axis, or <see langword="null"/> for none.</summary>
    public string? Title { get; set; }

    /// <summary>Gets the categories of the x axis, in order. A category gets the values at its index in the series.</summary>
    public IList<string> Categories { get; } = new List<string>();

    /// <summary>Gets or sets the values the axis goes through, or <see langword="null"/> to compute them from the series.</summary>
    public XyChartAxisRange? Range { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the labels of the ticks are drawn. The default is
    /// <see langword="true"/>. The labels of the categories are always drawn: <see langword="false"/> is written back,
    /// but hides only the labels of numbers.
    /// </summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>Gets or sets the size of the font of the labels of the ticks. The default is 14. Charts has one size for the labels of both axes: the largest of the two.</summary>
    public double LabelFontSize
    {
        get => _labelFontSize;
        set => _labelFontSize = XyChart.Validate(value, 1, 1000);
    }

    /// <summary>Gets or sets the space on each side of the labels of the ticks. The default is 5. It is written back but not drawn.</summary>
    public double LabelPadding
    {
        get => _labelPadding;
        set => _labelPadding = XyChart.Validate(value, 0, 1000);
    }

    /// <summary>
    /// Gets or sets the rotation of the labels of the ticks of the x axis, in degrees, clockwise, between -90 and 90. The
    /// default is 0. Only the labels below a vertical chart rotate.
    /// </summary>
    public double LabelRotation
    {
        get => _labelRotation;
        set => _labelRotation = XyChart.Validate(value, -90, 90);
    }

    /// <summary>Gets or sets a value that indicates whether the title is drawn. The default is <see langword="true"/>.</summary>
    public bool ShowTitle { get; set; } = true;

    /// <summary>Gets or sets the size of the font of the title. The default is 16. See <see cref="XyChart.LegendFontSize"/>.</summary>
    public double TitleFontSize
    {
        get => _titleFontSize;
        set => _titleFontSize = XyChart.Validate(value, 1, 1000);
    }

    /// <summary>Gets or sets the space on each side of the title. The default is 5. It is written back but not drawn.</summary>
    public double TitlePadding
    {
        get => _titlePadding;
        set => _titlePadding = XyChart.Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets a value that indicates whether the ticks are drawn. The default is <see langword="true"/>.</summary>
    public bool ShowTicks { get; set; } = true;

    /// <summary>Gets or sets the length of the ticks. The default is 5.</summary>
    public double TickLength
    {
        get => _tickLength;
        set => _tickLength = XyChart.Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets the width of the ticks. The default is 2. It is written back but not drawn: Charts draws thin ticks.</summary>
    public double TickWidth
    {
        get => _tickWidth;
        set => _tickWidth = XyChart.Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets a value that indicates whether the line of the axis is drawn. The default is <see langword="true"/>.</summary>
    public bool ShowLine { get; set; } = true;

    /// <summary>Gets or sets the width of the line of the axis. The default is 2. It is written back but not drawn: Charts draws a thin line.</summary>
    public double LineWidth
    {
        get => _lineWidth;
        set => _lineWidth = XyChart.Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets the color of the title, or <see langword="null"/> for the color of the texts of the theme. See <see cref="XyChart.TitleColor"/>.</summary>
    public SceneColor? TitleColor { get; set; }

    /// <summary>
    /// Gets or sets the color of the labels of the ticks, or <see langword="null"/> for the color of the labels of the
    /// theme. Charts has one color for the labels of both axes: the color of the x axis, else of the y axis.
    /// </summary>
    public SceneColor? LabelColor { get; set; }

    /// <summary>
    /// Gets or sets the color of the ticks, or <see langword="null"/> for the color of the axes of the theme. Charts has one
    /// color for the lines and the ticks of both axes: the first color set among the <see cref="LineColor"/> of the x
    /// axis, of the y axis, then the <see cref="TickColor"/> of the x axis, of the y axis.
    /// </summary>
    public SceneColor? TickColor { get; set; }

    /// <summary>Gets or sets the color of the line of the axis, or <see langword="null"/> for the color of the axes of the theme. See <see cref="TickColor"/>.</summary>
    public SceneColor? LineColor { get; set; }
}
