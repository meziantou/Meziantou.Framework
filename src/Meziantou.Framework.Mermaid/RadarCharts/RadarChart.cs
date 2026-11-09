
using Meziantou.Framework.Charts;

namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A radar chart, also known as a spider chart: axes around a center, and curves that join a value on each axis, to
/// compare several series over several dimensions (Mermaid <c>radar-beta</c>).
/// </summary>
public sealed class RadarChart : MermaidChart
{
    private const double LabelPadding = 4;

    private static readonly SceneColor DefaultGraticuleColor = SceneColor.Parse("#dedede");

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

    /// <summary>Gets or sets a value that indicates whether the legend lists the curves, in the top right corner (Mermaid <c>showLegend</c>). The default is <see langword="true"/>.</summary>
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

    /// <summary>Gets or sets the margin above the chart, where the title and the legend are drawn (Mermaid <c>marginTop</c>). The default is 50.</summary>
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

    /// <summary>
    /// Gets or sets the tension of the rounded curves, from 0 for straight lines to 1 (Mermaid <c>curveTension</c>). The
    /// default is 0.17. The rounded curves are the Catmull-Rom splines of Meziantou.Framework.Charts, which have no
    /// tension: the value is written back, but only 0, straight lines, changes the drawing.
    /// </summary>
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

    /// <summary>
    /// Draws the chart in polar coordinates: the x scale goes from 0 to 1 around the circle, with the axis <c>i</c> at
    /// <c>i / Axes.Count</c>, and the y scale goes from <see cref="Minimum"/> at the center to the maximum at the end of
    /// the axes. The scale, the axes and their labels are marks, so they take the widths and the colors of the options.
    /// </summary>
    private protected override void Configure(Plot plot)
    {
        var axisCount = Axes.Count;
        var minimum = Minimum;
        var maximum = Maximum ?? GetLargestValue();

        // As Mermaid, the curves are not drawn when the scale is empty. The scale is then the fractions of the radius,
        // as when the values at the end of the axis lines or at their labels are too large for a double.
        var drawCurves = maximum > minimum && double.IsFinite(maximum);
        var extent = Math.Max(1, Math.Max(AxisScaleFactor, AxisLabelFactor));
        var normalized = !drawCurves || !double.IsFinite(minimum + (extent * (maximum - minimum)));
        var (start, range) = normalized ? (0.0, 1.0) : (minimum, maximum - minimum);

        plot.Width = Width + MarginLeft + MarginRight;
        plot.Height = Height + MarginTop + MarginBottom;
        plot.Margin = new ChartMargin(MarginTop, MarginRight, MarginBottom, MarginLeft);
        plot.Coordinates = new PolarCoordinates();
        plot.XScale = new LinearScale { Minimum = 0, Maximum = 1, Nice = false, Zero = false };
        plot.YScale = new LinearScale { Minimum = start, Maximum = start + range, Nice = false, Zero = false };
        plot.XAxis.Visible = false;
        plot.YAxis.Visible = false;

        var graticuleColor = GraticuleColor ?? DefaultGraticuleColor;
        var axisColor = AxisColor ?? plot.Theme.AxisColor;
        plot.Theme = plot.Theme with { FontSize = LegendFontSize ?? 12, GridColor = graticuleColor, AxisColor = axisColor };

        AddGraticule(plot, Value, graticuleColor);
        if (axisCount > 0)
        {
            plot.Add(new RuleXMark<int>(Enumerable.Range(0, axisCount).ToArray(), x: index => (double)index / axisCount)
            {
                Y1 = _ => start,
                Y2 = _ => Value(AxisScaleFactor),
                StrokeColor = axisColor,
                StrokeWidth = AxisStrokeWidth ?? 2,
                CssClass = "radarAxes",
            });
        }

        AddCurves(plot, drawCurves, normalized, minimum, maximum);
        AddAxisLabels(plot, Value(AxisLabelFactor));
        return;

        // The value of the y scale at a fraction of the radius
        double Value(double fraction) => start + (fraction * range);
    }

    private void AddGraticule(Plot plot, Func<double, double> value, SceneColor color)
    {
        if (Ticks is 0)
            return;

        var fill = color.WithOpacity((GraticuleOpacity ?? 0.3) * color.Opacity);
        var strokeWidth = GraticuleStrokeWidth ?? 1;
        // The rings are drawn from the largest, so an opaque fill does not hide the outlines of the smaller ones
        if (Graticule is RadarGraticule.Circle)
        {
            // A pie of one slice of the radius of the circle
            for (var i = Ticks - 1; i >= 0; i--)
            {
                plot.Add(new ArcMark<double>([value((i + 1.0) / Ticks)], value: _ => 1)
                {
                    Radius = radius => radius,
                    FillColor = fill,
                    StrokeColor = color,
                    StrokeWidth = strokeWidth,
                    CssClass = "radarGraticule",
                });
            }

            return;
        }

        var axisCount = Axes.Count;
        if (axisCount is 0)
            return;

        var corners = new List<(int Ring, int Axis)>(Ticks * axisCount);
        for (var ring = Ticks - 1; ring >= 0; ring--)
        {
            for (var axis = 0; axis < axisCount; axis++)
            {
                corners.Add((ring, axis));
            }
        }

        plot.Add(new AreaMark<(int Ring, int Axis)>(corners, x: corner => (double)corner.Axis / axisCount, y: corner => value((corner.Ring + 1.0) / Ticks))
        {
            Series = corner => corner.Ring,
            Closed = true,
            FillColor = fill,
            StrokeColor = color,
            StrokeWidth = strokeWidth,
            CssClass = "radarGraticule",
        });
    }

    private void AddCurves(Plot plot, bool drawCurves, bool normalized, double minimum, double maximum)
    {
        if (Curves.Count is 0)
            return;

        // The legend lists the curves by label: the duplicate labels get zero-width spaces, so each curve keeps its color
        var keys = new string[Curves.Count];
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Length; i++)
        {
            var key = SingleLine(Curves[i].Label ?? Curves[i].Name);
            while (!usedKeys.Add(key))
            {
                key += "\u200B";
            }

            keys[i] = key;
        }

        var theme = plot.Theme;
        plot.ColorScale = new OrdinalColorScale { Domain = keys, Palette = [.. keys.Select((_, index) => theme.GetPaletteColor(index))] };
        plot.Legend.Visible = ShowLegend;
        plot.Legend.Position = LegendPosition.Right;

        // As Mermaid, a curve without a value per axis is not drawn, and the values out of the scale are at its bounds
        var axisCount = Axes.Count;
        var points = new List<(int Curve, int Axis, double Value)>();
        for (var i = 0; drawCurves && axisCount > 0 && i < Curves.Count; i++)
        {
            var values = Curves[i].Values;
            if (values.Count != axisCount)
                continue;

            for (var axis = 0; axis < axisCount; axis++)
            {
                var value = values[axis];
                value = double.IsNaN(value) ? minimum : Math.Clamp(value, minimum, maximum);
                if (normalized)
                {
                    value = Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
                }

                points.Add((i, axis, value));
            }
        }

        if (points.Count is 0)
            return;

        plot.Add(new AreaMark<(int Curve, int Axis, double Value)>(points, x: point => (double)point.Axis / axisCount, y: point => point.Value)
        {
            Fill = point => keys[point.Curve],
            Stroke = point => keys[point.Curve],
            FillOpacity = CurveOpacity ?? 0.5,
            StrokeWidth = CurveStrokeWidth ?? 2,
            Closed = true,
            Curve = Graticule is RadarGraticule.Circle && CurveTension > 0 ? Curve.CatmullRom : Curve.Linear,
            CssClass = "radarCurves",
        });
    }

    private void AddAxisLabels(Plot plot, double radius)
    {
        var axisCount = Axes.Count;
        var fontSize = AxisLabelFontSize ?? 12;
        var measurer = plot.TextMeasurer ?? AfmTextMeasurer.Default;
        for (var i = 0; i < axisCount; i++)
        {
            var label = SingleLine(Axes[i].Label ?? Axes[i].Name);
            var x = (double)i / axisCount;
            var angle = 2 * Math.PI * x;
            var sin = Math.Sin(angle);
            var cos = Math.Cos(angle);
            var metrics = measurer.Measure(label, new SceneFont(fontSize));

            // As Mermaid, the labels are 4 pixels past their position, anchored away from the center: the text marks are
            // centered vertically, so the labels below the center move down, and those above it move up
            var offsetY = -LabelPadding * cos;
            if (cos < -0.01)
            {
                offsetY += metrics.Height / 2;
            }
            else if (cos > 0.01)
            {
                offsetY -= (metrics.Ascent - metrics.Descent) / 2;
            }

            plot.Add(new TextMark<int>([i], text: _ => label, x: _ => x, y: _ => radius)
            {
                Anchor = sin > 0.01 ? SceneTextAnchor.Start : sin < -0.01 ? SceneTextAnchor.End : SceneTextAnchor.Middle,
                OffsetX = LabelPadding * sin,
                OffsetY = offsetY,
                FontSize = fontSize,
                CssClass = "radarAxisLabel",
            });
        }
    }

    private double GetLargestValue()
    {
        var maximum = double.NegativeInfinity;
        foreach (var curve in Curves)
        {
            foreach (var value in curve.Values)
            {
                if (value > maximum)
                {
                    maximum = value;
                }
            }
        }

        return maximum;
    }

    // An SVG text is one line: the line breaks are spaces
    private static string SingleLine(string text) => text.ReplaceLineEndings(" ");

    private static double Validate(double value, double minimum, double maximum, string name)
    {
        if (!(value >= minimum && value <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, name + " is out of range.");

        return value;
    }

    private static double? ValidateOptional(double? value, double minimum, double maximum, string name) => value is { } number ? Validate(number, minimum, maximum, name) : null;
}
