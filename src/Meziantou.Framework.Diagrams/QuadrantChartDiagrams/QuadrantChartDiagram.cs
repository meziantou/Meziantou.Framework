using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A quadrant chart: points placed on two axes that go from 0 to 1, over a square split into four quadrants, as for a
/// priority or a risk matrix (Mermaid <c>quadrantChart</c>). The quadrants are numbered as in mathematics: 1 is the top
/// right one, 2 the top left one, 3 the bottom left one and 4 the bottom right one.
/// </summary>
public sealed class QuadrantChartDiagram : Diagram
{
    private double _width = 500;
    private double _height = 500;
    private double _quadrantPadding = 5;
    private double _quadrantTextTopPadding = 5;
    private double _quadrantLabelFontSize = 16;
    private double _internalBorderWidth = 1;
    private double _externalBorderWidth = 2;
    private double _xAxisLabelPadding = 5;
    private double _xAxisLabelFontSize = 16;
    private double _yAxisLabelPadding = 5;
    private double _yAxisLabelFontSize = 16;
    private double _pointTextPadding = 5;
    private double _pointLabelFontSize = 12;
    private double _pointRadius = 5;

    /// <summary>Gets or sets the text on the left of the x axis, or <see langword="null"/> for none.</summary>
    public DiagramText? XAxisLeftLabel { get; set; }

    /// <summary>Gets or sets the text on the right of the x axis, or <see langword="null"/> for none. Mermaid needs <see cref="XAxisLeftLabel"/> with it.</summary>
    public DiagramText? XAxisRightLabel { get; set; }

    /// <summary>Gets or sets the text at the bottom of the y axis, or <see langword="null"/> for none.</summary>
    public DiagramText? YAxisBottomLabel { get; set; }

    /// <summary>Gets or sets the text at the top of the y axis, or <see langword="null"/> for none. Mermaid needs <see cref="YAxisBottomLabel"/> with it.</summary>
    public DiagramText? YAxisTopLabel { get; set; }

    /// <summary>Gets or sets the text of the top right quadrant (Mermaid <c>quadrant-1</c>), or <see langword="null"/> for none.</summary>
    public DiagramText? Quadrant1Label { get; set; }

    /// <summary>Gets or sets the text of the top left quadrant (Mermaid <c>quadrant-2</c>), or <see langword="null"/> for none.</summary>
    public DiagramText? Quadrant2Label { get; set; }

    /// <summary>Gets or sets the text of the bottom left quadrant (Mermaid <c>quadrant-3</c>), or <see langword="null"/> for none.</summary>
    public DiagramText? Quadrant3Label { get; set; }

    /// <summary>Gets or sets the text of the bottom right quadrant (Mermaid <c>quadrant-4</c>), or <see langword="null"/> for none.</summary>
    public DiagramText? Quadrant4Label { get; set; }

    /// <summary>Gets the points. The first ones are drawn above the next ones, as in Mermaid.</summary>
    public IList<QuadrantChartPoint> Points { get; } = new List<QuadrantChartPoint>();

    /// <summary>Gets the styles that the points share by their <see cref="QuadrantChartPoint.ClassName"/> (Mermaid <c>classDef</c>).</summary>
    public IDictionary<string, QuadrantChartPointStyle> ClassDefinitions { get; } = new Dictionary<string, QuadrantChartPointStyle>(StringComparer.Ordinal);

    /// <summary>Gets the colors that replace the ones of the theme (the <c>quadrant*</c> theme variables of Mermaid).</summary>
    public QuadrantChartColors Colors { get; } = new();

    /// <summary>Gets or sets the width of the chart, the axis labels included, in pixels. The default is 500.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double Width { get => _width; set => _width = Check(value, 10_000); }

    /// <summary>Gets or sets the height of the chart, the axis labels included, in pixels. The title is drawn above it. The default is 500.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double Height { get => _height; set => _height = Check(value, 10_000); }

    /// <summary>Gets or sets the space around the quadrants, in pixels. The default is 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double QuadrantPadding { get => _quadrantPadding; set => _quadrantPadding = Check(value, 10_000); }

    /// <summary>Gets or sets the space between the top of a quadrant and its text when the chart has points, in pixels. The default is 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double QuadrantTextTopPadding { get => _quadrantTextTopPadding; set => _quadrantTextTopPadding = Check(value, 10_000); }

    /// <summary>Gets or sets the size of the texts of the quadrants. The default is 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1000.</exception>
    public double QuadrantLabelFontSize { get => _quadrantLabelFontSize; set => _quadrantLabelFontSize = Check(value, 1_000); }

    /// <summary>Gets or sets the width of the lines between the quadrants. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1000.</exception>
    public double InternalBorderWidth { get => _internalBorderWidth; set => _internalBorderWidth = Check(value, 1_000); }

    /// <summary>Gets or sets the width of the border around the quadrants. The default is 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1000.</exception>
    public double ExternalBorderWidth { get => _externalBorderWidth; set => _externalBorderWidth = Check(value, 1_000); }

    /// <summary>Gets or sets the space above and below the texts of the x axis, in pixels. The default is 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double XAxisLabelPadding { get => _xAxisLabelPadding; set => _xAxisLabelPadding = Check(value, 10_000); }

    /// <summary>Gets or sets the size of the texts of the x axis. The default is 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1000.</exception>
    public double XAxisLabelFontSize { get => _xAxisLabelFontSize; set => _xAxisLabelFontSize = Check(value, 1_000); }

    /// <summary>Gets or sets the space on both sides of the texts of the y axis, in pixels. The default is 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double YAxisLabelPadding { get => _yAxisLabelPadding; set => _yAxisLabelPadding = Check(value, 10_000); }

    /// <summary>Gets or sets the size of the texts of the y axis. The default is 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1000.</exception>
    public double YAxisLabelFontSize { get => _yAxisLabelFontSize; set => _yAxisLabelFontSize = Check(value, 1_000); }

    /// <summary>Gets or sets the space between a point and its text, in pixels. The default is 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double PointTextPadding { get => _pointTextPadding; set => _pointTextPadding = Check(value, 10_000); }

    /// <summary>Gets or sets the size of the texts of the points. The default is 12.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1000.</exception>
    public double PointLabelFontSize { get => _pointLabelFontSize; set => _pointLabelFontSize = Check(value, 1_000); }

    /// <summary>Gets or sets the radius of the points that have none in their style. The default is 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10000.</exception>
    public double PointRadius { get => _pointRadius; set => _pointRadius = Check(value, 10_000); }

    /// <summary>
    /// Gets or sets where the texts of the x axis are drawn. The default is <see cref="QuadrantChartXAxisPosition.Top"/>.
    /// As in Mermaid, they are always drawn at the bottom when the chart has points.
    /// </summary>
    public QuadrantChartXAxisPosition XAxisPosition { get; set; }

    /// <summary>Gets or sets where the texts of the y axis are drawn. The default is <see cref="QuadrantChartYAxisPosition.Left"/>.</summary>
    public QuadrantChartYAxisPosition YAxisPosition { get; set; }

    /// <summary>Adds a point.</summary>
    /// <param name="label">The text drawn below the point.</param>
    /// <param name="x">The position on the x axis, from 0 (left) to 1 (right).</param>
    /// <param name="y">The position on the y axis, from 0 (bottom) to 1 (top).</param>
    /// <returns>The point.</returns>
    public QuadrantChartPoint AddPoint(DiagramText label, double x, double y)
    {
        var point = new QuadrantChartPoint(label, x, y);
        Points.Add(point);
        return point;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => QuadrantChartDiagramRenderer.Render(this, context, group);

    private static double Check(double value, double maximum)
    {
        if (!(value >= 0 && value <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value is negative, too large or not a number.");

        return value;
    }
}
