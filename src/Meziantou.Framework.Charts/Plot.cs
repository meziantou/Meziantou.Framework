using System.Text;

namespace Meziantou.Framework.Charts;

/// <summary>
/// A chart, described with the grammar of graphics: layers of marks (bars, lines, dots, arcs...) whose channels are
/// mapped by scales (x, y, color, size) and placed by coordinates (Cartesian or polar).
/// </summary>
/// <remarks>
/// Every chart type is a combination of these pieces. The scales, the axes and the legend are inferred from the marks
/// unless they are set. <see cref="CreateScene"/> lays the chart out into a <see cref="ChartScene"/>; <see cref="ToSvg"/>
/// writes it as SVG. The output is deterministic: the same plot always gives the same SVG.
/// </remarks>
public sealed class Plot
{
    private double _width = 640;
    private double _height = 400;

    /// <summary>Gets or sets the width of the chart, in pixels. The default is 640.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Width
    {
        get => _width;
        set => _width = ValidateSize(value);
    }

    /// <summary>Gets or sets the height of the chart, in pixels. The default is 400.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Height
    {
        get => _height;
        set => _height = ValidateSize(value);
    }

    /// <summary>Gets or sets the title, drawn at the top of the chart and used as its accessible name.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the subtitle, drawn under the title.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Gets or sets the accessible description of the chart (an SVG <c>desc</c>). It is not drawn.</summary>
    public string? Description { get; set; }

    /// <summary>Gets the marks, drawn in order: the first one is at the bottom.</summary>
    public IList<Mark> Marks { get; } = new List<Mark>();

    /// <summary>Gets or sets the x scale, or <see langword="null"/> to infer it from the marks.</summary>
    public Scale? XScale { get; set; }

    /// <summary>Gets or sets the y scale, or <see langword="null"/> to infer it from the marks.</summary>
    public Scale? YScale { get; set; }

    /// <summary>
    /// Gets or sets the secondary y scale, used by the marks that set <see cref="Mark.UseSecondaryYScale"/>, or
    /// <see langword="null"/> to infer it from them. In Cartesian coordinates, its axis is on the right of the chart; in
    /// transposed and polar coordinates, it has no axis.
    /// </summary>
    public Scale? SecondaryYScale { get; set; }

    /// <summary>Gets or sets the color scale, or <see langword="null"/> to infer it from the marks.</summary>
    public ColorScale? ColorScale { get; set; }

    /// <summary>Gets or sets the size scale, or <see langword="null"/> for the default.</summary>
    public SizeScale? SizeScale { get; set; }

    /// <summary>Gets or sets the coordinates, or <see langword="null"/> for the default: polar for arcs (pies, donuts, gauges), Cartesian otherwise.</summary>
    public Coordinates? Coordinates { get; set; }

    /// <summary>Gets the options of the axis of the x scale.</summary>
    public Axis XAxis { get; } = new();

    /// <summary>Gets the options of the axis of the y scale.</summary>
    public Axis YAxis { get; } = new();

    /// <summary>Gets the options of the axis of the secondary y scale. It has no grid lines by default.</summary>
    public Axis SecondaryYAxis { get; } = new();

    /// <summary>Gets the options of the legend of the color scale.</summary>
    public Legend Legend { get; } = new();

    /// <summary>Gets the options of the grid of small charts, when marks have facets (<see cref="Mark{T}.FacetX"/>, <see cref="Mark{T}.FacetY"/>).</summary>
    public FacetOptions Facets { get; } = new();

    /// <summary>Gets or sets the theme. The default is <see cref="ChartTheme.Light"/>.</summary>
    public ChartTheme Theme { get; set; } = ChartTheme.Light;

    /// <summary>Gets or sets the space around the plot area, or <see langword="null"/> to compute it from the axes, the title and the legend.</summary>
    public ChartMargin? Margin { get; set; }

    /// <summary>Gets or sets a value that indicates whether the marks are clipped to the plot area. Use it with a domain narrower than the data.</summary>
    public bool Clip { get; set; }

    /// <summary>Gets or sets the measurer of the texts, or <see langword="null"/> for <see cref="AfmTextMeasurer.Default"/>.</summary>
    public ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Adds a mark.</summary>
    /// <param name="mark">The mark.</param>
    /// <returns>The plot, to chain calls.</returns>
    public Plot Add(Mark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        Marks.Add(mark);
        return this;
    }

    /// <summary>Lays the chart out into drawing primitives.</summary>
    /// <param name="textMeasurer">The measurer of the texts, or <see langword="null"/> for <see cref="TextMeasurer"/>.</param>
    /// <returns>The scene.</returns>
    public ChartScene CreateScene(ITextMeasurer? textMeasurer = null) => PlotRenderer.Render(this, textMeasurer ?? TextMeasurer ?? AfmTextMeasurer.Default);

    /// <summary>Returns the chart as SVG.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The SVG document, without an XML declaration.</returns>
    public string ToSvg(SvgRenderOptions? options = null) => SvgChartRenderer.Render(CreateScene(), options);

    /// <summary>Writes the chart as SVG.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public void WriteSvg(TextWriter writer, SvgRenderOptions? options = null) => SvgChartRenderer.Render(CreateScene(), writer, options);

    /// <summary>Writes the chart as SVG, encoded in UTF-8 without a byte order mark.</summary>
    /// <param name="stream">The destination. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public void WriteSvg(Stream stream, SvgRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
        WriteSvg(writer, options);
    }

    private static double ValidateSize(double value)
    {
        if (!(value > 0) || !double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The size must be a positive finite number.");

        return value;
    }
}
