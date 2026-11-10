using System.Globalization;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.PieDiagrams;
using Meziantou.Framework.Mermaid.Internals.Charts;


namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A pie chart: slices whose angles are proportional to their values, labeled with their percentages, and a legend
/// (Mermaid <c>pie</c>). The slices are drawn clockwise from the top, in their order.
/// </summary>
/// <remarks>
/// <para>As Mermaid, the slices smaller than 1% of the total are not drawn, but the legend lists them.</para>
/// <para>
/// The chart is drawn with <see cref="Meziantou.Framework.Charts.Plot"/>: a pie of the radius of the pies of Mermaid, in
/// the colors of the <c>pie1</c> to <c>pie12</c> theme variables of Mermaid, with the legend of
/// Meziantou.Framework.Charts. <see cref="MermaidChart.ToPlot"/> returns the plot to customize it.
/// </para>
/// </remarks>
public sealed class PieChart : MermaidChart
{
    // The radius of the pies of Mermaid, and the size of their texts
    private const double PieRadius = 185;
    private const double DefaultTextSize = 17;
    private const double HighlightScale = 1.05;

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

    /// <summary>
    /// Gets or sets the position of the legend. The default is <see cref="PieLegendPosition.Right"/>.
    /// <see cref="PieLegendPosition.Left"/> and <see cref="PieLegendPosition.Center"/> are written back, but the legend
    /// is drawn on the right: Meziantou.Framework.Charts draws no legend there.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the color of the texts of the legend, which the title uses too (Mermaid <c>pieLegendTextColor</c>).
    /// The default depends on the theme.
    /// </summary>
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

    private protected override void Configure(Plot plot)
    {
        var colors = new PieChartColors(this);
        var legendFont = new SceneFont(LegendTextSize ?? DefaultTextSize);

        // As the map of Mermaid, a label keeps the value of its first slice
        var sections = new List<PieSlice>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slice in Slices)
        {
            if (labels.Add(slice.Label))
            {
                sections.Add(slice);
            }
        }

        // The shares are relative to the largest value, so the total of huge values stays finite. The legend texts are the
        // keys of the colors: the texts that become equal once on one line get zero-width spaces, so each slice keeps its color
        var legendTexts = new string[sections.Count];
        var usedTexts = new HashSet<string>(StringComparer.Ordinal);
        var palette = new SceneColor[sections.Count];
        var largest = 0.0;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var legendText = (ShowData ? section.Label + " [" + JavaScriptNumber.ToString(section.Value) + "]" : section.Label).ReplaceLineEndings(" ");
            while (!usedTexts.Add(legendText))
            {
                legendText += "\u200B";
            }

            legendTexts[i] = legendText;
            palette[i] = colors.Slice(i);
            largest = Math.Max(largest, section.Value);
        }

        var shares = new double[sections.Count];
        var total = 0.0;
        for (var i = 0; i < sections.Count; i++)
        {
            shares[i] = largest > 0 ? sections[i].Value / largest : 0;
            total += shares[i];
        }

        // The slices smaller than 1% are not drawn; the others share the whole circle, as the pie layout of d3
        var drawn = new List<int>();
        var drawnTotal = 0.0;
        for (var i = 0; i < sections.Count; i++)
        {
            if (total > 0 && shares[i] / total * 100 >= 1)
            {
                drawn.Add(i);
                drawnTotal += shares[i];
            }
        }

        var arcs = new List<PieArc>(drawn.Count);
        var cumulative = 0.0;
        foreach (var index in drawn)
        {
            var percent = Math.Round(shares[index] / total * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
            var highlighted = HighlightedSlice is { Length: > 0 } highlight && string.Equals(highlight, sections[index].Label, StringComparison.Ordinal);
            arcs.Add(new PieArc(legendTexts[index], cumulative / drawnTotal, (cumulative + shares[index]) / drawnTotal, percent, highlighted));
            cumulative += shares[index];
        }

        // The radial scale goes from the hole (0) to the edge (1); Radial converts a fraction of the radius of the pie
        var hole = DonutHole;
        double Radial(double fraction) => (fraction - hole) / (1 - hole);

        plot.Theme = plot.Theme with { Foreground = colors.LegendText, FontSize = legendFont.Size };
        plot.Coordinates = new PolarCoordinates { InnerRadius = hole };
        plot.XScale = new LinearScale { Minimum = 0, Maximum = 1, Nice = false, Zero = false };
        plot.YScale = new LinearScale { Minimum = 0, Maximum = 1, Nice = false, Zero = false };
        plot.XAxis.Visible = false;
        plot.YAxis.Visible = false;
        plot.ColorScale = new OrdinalColorScale { Domain = legendTexts, Palette = palette };
        plot.Legend.Visible = sections.Count > 0;
        plot.Legend.Position = LegendPosition switch
        {
            PieLegendPosition.Top => Charts.LegendPosition.Top,
            PieLegendPosition.Bottom => Charts.LegendPosition.Bottom,
            _ => Charts.LegendPosition.Right,
        };

        plot.Add(new RuleYMark(1)
        {
            StrokeColor = OuterStrokeColor ?? SceneColor.Black,
            StrokeWidth = OuterStrokeWidth ?? 2,
            CssClass = "pieOuterCircle",
        });

        var strokeColor = StrokeColor ?? SceneColor.Black;
        var strokeWidth = StrokeWidth ?? 2;
        plot.Add(new RectMark<PieArc>(arcs.Where(arc => !arc.Highlighted))
        {
            X1 = arc => arc.Start,
            X2 = arc => arc.End,
            Y1 = _ => 0d,
            Y2 = _ => 1d,
            Fill = arc => arc.Category,
            StrokeColor = strokeColor,
            StrokeWidth = strokeWidth,
            Opacity = Opacity ?? 0.7,
            CssClass = "pieCircle",
        });

        // A highlighted slice is scaled by 1.05 from the center of the pie, and opaque
        if (arcs.Exists(arc => arc.Highlighted))
        {
            plot.Add(new RectMark<PieArc>(arcs.Where(arc => arc.Highlighted))
            {
                X1 = arc => arc.Start,
                X2 = arc => arc.End,
                Y1 = _ => Radial(HighlightScale * hole),
                Y2 = _ => Radial(HighlightScale),
                Fill = arc => arc.Category,
                StrokeColor = strokeColor,
                StrokeWidth = strokeWidth * HighlightScale,
                CssClass = "pieCircle highlighted",
            });
        }

        var textPosition = Radial(TextPosition);
        plot.Add(new TextMark<PieArc>(arcs, text: arc => arc.Percent, x: arc => (arc.Start + arc.End) / 2, y: _ => textPosition)
        {
            FontSize = SectionTextSize ?? DefaultTextSize,
            FillColor = colors.SectionText,
            CssClass = "slice",
        });

        SetSize(plot, legendTexts, legendFont);
    }

    /// <summary>Sizes the plot so the pie has the radius of the pies of Mermaid, whatever the size of the title and of the legend.</summary>
    private static void SetSize(Plot plot, string[] legendTexts, SceneFont legendFont)
    {
        // The space around the pie: the padding of the plot and the room of the hidden angular axis
        const double PieSize = (2 * PieRadius) + 28;

        // The legend draws a swatch and a gap before each text, which it truncates to 160 pixels
        const double SwatchWidth = 15;
        const double MaxLegendTextWidth = 160;
        const double ItemGap = 14;

        var measurer = plot.TextMeasurer ?? AfmTextMeasurer.Default;
        var titleHeight = string.IsNullOrEmpty(plot.Title) ? 0 : measurer.Measure(plot.Title, new SceneFont(plot.Theme.TitleFontSize, Bold: true)).Height + 8;
        var lineHeight = Math.Max(10, measurer.Measure("Xg", legendFont).Height) + 4;
        if (legendTexts.Length is 0)
        {
            plot.Width = PieSize;
            plot.Height = PieSize + titleHeight;
            return;
        }

        if (plot.Legend.Position is Charts.LegendPosition.Right)
        {
            var legendWidth = legendTexts.Max(text => SwatchWidth + Math.Min(MaxLegendTextWidth, measurer.Measure(text, legendFont).Width));
            plot.Width = PieSize + legendWidth + 12;
            plot.Height = Math.Max(PieSize, 20 + (legendTexts.Length * lineHeight)) + titleHeight;
            return;
        }

        // The legend wraps in rows as wide as the pie
        var maxWidth = PieSize - 20;
        var x = 0.0;
        var rows = 1;
        foreach (var text in legendTexts)
        {
            var itemWidth = SwatchWidth + Math.Min(MaxLegendTextWidth, measurer.Measure(text, legendFont).Width);
            if (x > 0 && x + itemWidth > maxWidth)
            {
                x = 0;
                rows++;
            }

            x += itemWidth + ItemGap;
        }

        plot.Width = PieSize;
        plot.Height = PieSize + titleHeight + (rows * lineHeight) + 8;
    }

    private static double? Validate(double? value, double minimum, double maximum, string name)
    {
        if (value is { } number && !(number >= minimum && number <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, name + " is out of range.");

        return value;
    }

    /// <summary>A drawn slice: its category in the legend, its start and end as fractions of the circle, and its percentage.</summary>
    private sealed record PieArc(string Category, double Start, double End, string Percent, bool Highlighted);
}
