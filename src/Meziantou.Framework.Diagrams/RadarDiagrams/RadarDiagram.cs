using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A radar chart, also known as a spider chart: axes around a center, and curves that join a value on each axis, to
/// compare several series over several dimensions (Mermaid <c>radar-beta</c>).
/// </summary>
public sealed class RadarDiagram : Diagram
{
    private int _ticks = 5;
    private double? _maximum;
    private double _minimum;
    private double _width = 600;
    private double _height = 600;
    private double _marginTop = 50;
    private double _marginRight = 50;
    private double _marginBottom = 50;
    private double _marginLeft = 50;
    private double _axisScaleFactor = 1;
    private double _axisLabelFactor = 1.05;
    private double _curveTension = 0.17;
    private double? _axisStrokeWidth;
    private double? _axisLabelFontSize;
    private double? _curveOpacity;
    private double? _curveStrokeWidth;
    private double? _graticuleStrokeWidth;
    private double? _graticuleOpacity;
    private double? _legendFontSize;

    /// <summary>Gets the axes, clockwise from the top.</summary>
    public IList<RadarAxis> Axes { get; } = new List<RadarAxis>();

    /// <summary>Gets the curves. The colors of the theme go to the curves in their order.</summary>
    public IList<RadarCurve> Curves { get; } = new List<RadarCurve>();

    /// <summary>Gets or sets a value that indicates whether the legend lists the curves (Mermaid <c>showLegend</c>). The default is <see langword="true"/>.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Gets or sets the number of circles or polygons of the scale, from 0 to 32 (Mermaid <c>ticks</c>). The default is 5.</summary>
    public int Ticks
    {
        get => _ticks;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 32);
            _ticks = value;
        }
    }

    /// <summary>
    /// Gets or sets the value at the end of the axes, or <see langword="null"/> for the largest value of the curves
    /// (Mermaid <c>max</c>). The values above it are drawn at the end of the axes.
    /// </summary>
    public double? Maximum
    {
        get => _maximum;
        set => _maximum = value is { } number ? Validate(number, 0, double.MaxValue, "The maximum") : null;
    }

    /// <summary>Gets or sets the value at the center (Mermaid <c>min</c>). The default is 0. The values below it are drawn at the center.</summary>
    public double Minimum
    {
        get => _minimum;
        set => _minimum = Validate(value, 0, double.MaxValue, "The minimum");
    }

    /// <summary>Gets or sets the shape of the scale and of the curves (Mermaid <c>graticule</c>). The default is <see cref="RadarGraticule.Circle"/>.</summary>
    public RadarGraticule Graticule { get; set; }

    /// <summary>Gets or sets the width of the chart, without its margins (Mermaid <c>width</c>). The default is 600.</summary>
    public double Width
    {
        get => _width;
        set => _width = Validate(value, 1, 10_000, "The width");
    }

    /// <summary>Gets or sets the height of the chart, without its margins (Mermaid <c>height</c>). The default is 600.</summary>
    public double Height
    {
        get => _height;
        set => _height = Validate(value, 1, 10_000, "The height");
    }

    /// <summary>Gets or sets the margin above the chart (Mermaid <c>marginTop</c>). The default is 50.</summary>
    public double MarginTop
    {
        get => _marginTop;
        set => _marginTop = Validate(value, 0, 10_000, "The margin");
    }

    /// <summary>Gets or sets the margin on the right of the chart (Mermaid <c>marginRight</c>). The default is 50.</summary>
    public double MarginRight
    {
        get => _marginRight;
        set => _marginRight = Validate(value, 0, 10_000, "The margin");
    }

    /// <summary>Gets or sets the margin below the chart (Mermaid <c>marginBottom</c>). The default is 50.</summary>
    public double MarginBottom
    {
        get => _marginBottom;
        set => _marginBottom = Validate(value, 0, 10_000, "The margin");
    }

    /// <summary>Gets or sets the margin on the left of the chart (Mermaid <c>marginLeft</c>). The default is 50.</summary>
    public double MarginLeft
    {
        get => _marginLeft;
        set => _marginLeft = Validate(value, 0, 10_000, "The margin");
    }

    /// <summary>Gets or sets the length of the axis lines, as a ratio of the radius of the chart (Mermaid <c>axisScaleFactor</c>). The default is 1.</summary>
    public double AxisScaleFactor
    {
        get => _axisScaleFactor;
        set => _axisScaleFactor = Validate(value, 0, 100, "The scale factor");
    }

    /// <summary>Gets or sets the distance of the labels of the axes from the center, as a ratio of the radius (Mermaid <c>axisLabelFactor</c>). The default is 1.05.</summary>
    public double AxisLabelFactor
    {
        get => _axisLabelFactor;
        set => _axisLabelFactor = Validate(value, 0, 100, "The label factor");
    }

    /// <summary>Gets or sets the tension of the rounded curves, from 0 for straight lines to 1 (Mermaid <c>curveTension</c>). The default is 0.17.</summary>
    public double CurveTension
    {
        get => _curveTension;
        set => _curveTension = Validate(value, 0, 1, "The tension");
    }

    /// <summary>Gets or sets the color of the axes (Mermaid <c>axisColor</c>). The default is the line color of the theme.</summary>
    public SceneColor? AxisColor { get; set; }

    /// <summary>Gets or sets the width of the axes (Mermaid <c>axisStrokeWidth</c>). The default is 2.</summary>
    public double? AxisStrokeWidth
    {
        get => _axisStrokeWidth;
        set => _axisStrokeWidth = ValidateOptional(value, 0, 1000, "The width");
    }

    /// <summary>Gets or sets the font size of the labels of the axes (Mermaid <c>axisLabelFontSize</c>). The default is 12.</summary>
    public double? AxisLabelFontSize
    {
        get => _axisLabelFontSize;
        set => _axisLabelFontSize = ValidateOptional(value, 1, 200, "The font size");
    }

    /// <summary>Gets or sets the opacity of the fill of the curves, from 0 to 1 (Mermaid <c>curveOpacity</c>). The default is 0.5.</summary>
    public double? CurveOpacity
    {
        get => _curveOpacity;
        set => _curveOpacity = ValidateOptional(value, 0, 1, "The opacity");
    }

    /// <summary>Gets or sets the width of the curves (Mermaid <c>curveStrokeWidth</c>). The default is 2.</summary>
    public double? CurveStrokeWidth
    {
        get => _curveStrokeWidth;
        set => _curveStrokeWidth = ValidateOptional(value, 0, 1000, "The width");
    }

    /// <summary>Gets or sets the color of the scale (Mermaid <c>graticuleColor</c>). The default is <c>#dedede</c>.</summary>
    public SceneColor? GraticuleColor { get; set; }

    /// <summary>Gets or sets the width of the lines of the scale (Mermaid <c>graticuleStrokeWidth</c>). The default is 1.</summary>
    public double? GraticuleStrokeWidth
    {
        get => _graticuleStrokeWidth;
        set => _graticuleStrokeWidth = ValidateOptional(value, 0, 1000, "The width");
    }

    /// <summary>Gets or sets the opacity of the fill of the scale, from 0 to 1 (Mermaid <c>graticuleOpacity</c>). The default is 0.3.</summary>
    public double? GraticuleOpacity
    {
        get => _graticuleOpacity;
        set => _graticuleOpacity = ValidateOptional(value, 0, 1, "The opacity");
    }

    /// <summary>Gets or sets the font size of the legend (Mermaid <c>legendFontSize</c>). The default is 12.</summary>
    public double? LegendFontSize
    {
        get => _legendFontSize;
        set => _legendFontSize = ValidateOptional(value, 1, 200, "The font size");
    }

    /// <summary>Adds an axis after the last one.</summary>
    /// <param name="name">The identifier of the axis.</param>
    /// <param name="label">The text drawn at the end of the axis, or <see langword="null"/> to draw the identifier.</param>
    /// <returns>The axis.</returns>
    public RadarAxis AddAxis(string name, string? label = null)
    {
        var axis = new RadarAxis(name, label);
        Axes.Add(axis);
        return axis;
    }

    /// <summary>Adds a curve after the last one.</summary>
    /// <param name="name">The identifier of the curve.</param>
    /// <param name="label">The text of the curve in the legend, or <see langword="null"/> to write the identifier.</param>
    /// <param name="values">The values, in the order of the axes.</param>
    /// <returns>The curve.</returns>
    public RadarCurve AddCurve(string name, string? label, params IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var curve = new RadarCurve(name, label);
        foreach (var value in values)
        {
            curve.Values.Add(value);
        }

        Curves.Add(curve);
        return curve;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => RadarDiagramRenderer.Render(this, context, group);

    private static double Validate(double value, double minimum, double maximum, string name)
    {
        if (!(value >= minimum && value <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, name + " is out of range.");

        return value;
    }

    private static double? ValidateOptional(double? value, double minimum, double maximum, string name) => value is { } number ? Validate(number, minimum, maximum, name) : null;
}
