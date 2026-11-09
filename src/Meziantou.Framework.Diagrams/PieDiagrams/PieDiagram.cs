using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A pie chart: slices whose angles are proportional to their values, labeled with their percentages, and a legend
/// (Mermaid <c>pie</c>). The slices are drawn clockwise from the top, in their order.
/// </summary>
/// <remarks>
/// As Mermaid, the slices smaller than 1% of the total are not drawn, but the legend lists them.
/// </remarks>
public sealed class PieDiagram : Diagram
{
    private double _textPosition = 0.75;
    private double _donutHole;
    private double? _strokeWidth;
    private double? _outerStrokeWidth;
    private double? _opacity;
    private double? _sectionTextSize;
    private double? _legendTextSize;

    /// <summary>Gets the slices, in the order they are drawn, clockwise from the top.</summary>
    public IList<PieSlice> Slices { get; } = new List<PieSlice>();

    /// <summary>Gets or sets a value that indicates whether the legend writes the value of each slice after its label (Mermaid <c>showData</c>).</summary>
    public bool ShowData { get; set; }

    /// <summary>
    /// Gets or sets the distance of the percentages from the center, from 0 at the center to 1 on the edge of the pie
    /// (Mermaid <c>textPosition</c>). The default is 0.75.
    /// </summary>
    public double TextPosition
    {
        get => _textPosition;
        set
        {
            if (!(value is >= 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The position of the texts must be between 0 and 1.");

            _textPosition = value;
        }
    }

    /// <summary>
    /// Gets or sets the radius of the hole in the middle of the pie, as a ratio of its radius, from 0 for a full pie to 0.9
    /// (Mermaid <c>donutHole</c>). The default is 0.
    /// </summary>
    public double DonutHole
    {
        get => _donutHole;
        set
        {
            if (!(value is >= 0 and <= 0.9))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The donut hole must be between 0 and 0.9.");

            _donutHole = value;
        }
    }

    /// <summary>Gets or sets the position of the legend. The default is <see cref="PieLegendPosition.Right"/>.</summary>
    public PieLegendPosition LegendPosition { get; set; }

    /// <summary>Gets or sets the label of the slice drawn highlighted: bigger and opaque (Mermaid <c>highlightSlice</c>).</summary>
    public string? HighlightedSlice { get; set; }

    /// <summary>
    /// Gets the colors of the slices, by position of the slice, which replace the colors of the theme (Mermaid
    /// <c>pie1</c>...<c>pie12</c>, for the positions 0 to 11). The colors repeat every 12 slices.
    /// </summary>
    public IDictionary<int, SceneColor> SliceColors { get; } = new Dictionary<int, SceneColor>();

    /// <summary>Gets or sets the color of the borders of the slices (Mermaid <c>pieStrokeColor</c>). The default is black.</summary>
    public SceneColor? StrokeColor { get; set; }

    /// <summary>Gets or sets the width of the borders of the slices (Mermaid <c>pieStrokeWidth</c>). The default is 2.</summary>
    public double? StrokeWidth
    {
        get => _strokeWidth;
        set => _strokeWidth = Validate(value, 0, 1000, "The width of the borders");
    }

    /// <summary>Gets or sets the color of the circle around the pie (Mermaid <c>pieOuterStrokeColor</c>). The default is black.</summary>
    public SceneColor? OuterStrokeColor { get; set; }

    /// <summary>Gets or sets the width of the circle around the pie (Mermaid <c>pieOuterStrokeWidth</c>). The default is 2.</summary>
    public double? OuterStrokeWidth
    {
        get => _outerStrokeWidth;
        set => _outerStrokeWidth = Validate(value, 0, 1000, "The width of the circle");
    }

    /// <summary>Gets or sets the opacity of the slices, from 0 to 1 (Mermaid <c>pieOpacity</c>). The default is 0.7.</summary>
    public double? Opacity
    {
        get => _opacity;
        set => _opacity = Validate(value, 0, 1, "The opacity");
    }

    /// <summary>Gets or sets the color of the percentages (Mermaid <c>pieSectionTextColor</c>). The default is the text color of the theme.</summary>
    public SceneColor? SectionTextColor { get; set; }

    /// <summary>Gets or sets the font size of the percentages (Mermaid <c>pieSectionTextSize</c>). The default is 17.</summary>
    public double? SectionTextSize
    {
        get => _sectionTextSize;
        set => _sectionTextSize = Validate(value, 1, 200, "The font size");
    }

    /// <summary>Gets or sets the color of the texts of the legend (Mermaid <c>pieLegendTextColor</c>). The default depends on the theme.</summary>
    public SceneColor? LegendTextColor { get; set; }

    /// <summary>Gets or sets the font size of the texts of the legend (Mermaid <c>pieLegendTextSize</c>). The default is 17.</summary>
    public double? LegendTextSize
    {
        get => _legendTextSize;
        set => _legendTextSize = Validate(value, 1, 200, "The font size");
    }

    /// <summary>Adds a slice after the last one.</summary>
    /// <param name="label">The label of the slice.</param>
    /// <param name="value">The value of the slice, positive or zero.</param>
    /// <returns>The slice.</returns>
    public PieSlice AddSlice(string label, double value)
    {
        var slice = new PieSlice(label, value);
        Slices.Add(slice);
        return slice;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => PieDiagramRenderer.Render(this, context, group);

    private static double? Validate(double? value, double minimum, double maximum, string name)
    {
        if (value is { } number && !(number >= minimum && number <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, name + " is out of range.");

        return value;
    }
}
