using Meziantou.Framework.Mermaid.Internals.SankeyDiagrams;

namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A sankey diagram: flows between nodes, drawn as links whose width is proportional to their quantity, between nodes
/// placed in columns (Mermaid <c>sankey</c>). The nodes are the sources and the targets of the links, named by them; the
/// chart is drawn with the <see cref="Meziantou.Framework.Charts.SankeyMark{T}"/> of Meziantou.Framework.Charts, whose
/// layout is the one of d3-sankey, which Mermaid uses.
/// </summary>
/// <remarks>
/// The links must not form a cycle: a link that closes a cycle is not drawn, nor a node that only such links name.
/// </remarks>
public sealed class SankeyChart : MermaidChart
{
    private double _width = 600;
    private double _height = 400;
    private double _nodeWidth = 10;
    private double _nodePadding = 12;
    private string _valuePrefix = "";
    private string _valueSuffix = "";

    /// <summary>Gets the links, in their order: the nodes are ordered by their first appearance in the links.</summary>
    public IList<SankeyLink> Links { get; } = new List<SankeyLink>();

    /// <summary>Gets or sets the width of the area of the nodes and the links, in pixels, without the margins of the chart. The default is 600.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 10,000.</exception>
    public double Width
    {
        get => _width;
        set => _width = Validate(value, 1, 10_000);
    }

    /// <summary>Gets or sets the height of the area of the nodes and the links, in pixels, without the title and the margins of the chart. The default is 400.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 10,000.</exception>
    public double Height
    {
        get => _height;
        set => _height = Validate(value, 1, 10_000);
    }

    /// <summary>Gets or sets the width of the rectangles of the nodes, in pixels. The default is 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1,000.</exception>
    public double NodeWidth
    {
        get => _nodeWidth;
        set => _nodeWidth = Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets the vertical space between the nodes of a column, in pixels, increased by 15 when <see cref="ShowValues"/> is set. The default is 12.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1,000.</exception>
    public double NodePadding
    {
        get => _nodePadding;
        set => _nodePadding = Validate(value, 0, 1000);
    }

    /// <summary>
    /// Gets or sets how the links are colored when <see cref="LinkColor"/> is <see langword="null"/>. The default is
    /// <see cref="SankeyLinkColoring.Gradient"/>, which is written back but drawn as <see cref="SankeyLinkColoring.Source"/>.
    /// </summary>
    public SankeyLinkColoring LinkColoring { get; set; }

    /// <summary>Gets or sets the color of every link, or <see langword="null"/> to color them as <see cref="LinkColoring"/> says.</summary>
    public SceneColor? LinkColor { get; set; }

    /// <summary>Gets or sets how the nodes are placed in columns. The default is <see cref="SankeyNodeAlignment.Justify"/>.</summary>
    public SankeyNodeAlignment NodeAlignment { get; set; }

    /// <summary>Gets or sets a value that indicates whether the labels of the nodes show their value. The default is <see langword="true"/>.</summary>
    public bool ShowValues { get; set; } = true;

    /// <summary>Gets or sets the text written before the values, such as a currency symbol. The default is an empty text.</summary>
    public string ValuePrefix
    {
        get => _valuePrefix;
        set => _valuePrefix = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the text written after the values, such as a unit. The default is an empty text.</summary>
    public string ValueSuffix
    {
        get => _valueSuffix;
        set => _valueSuffix = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets how the labels of the nodes are drawn. The default is <see cref="SankeyLabelStyle.Legacy"/>.
    /// <see cref="SankeyLabelStyle.Outlined"/> is written back but not drawn: the labels are always drawn as
    /// <see cref="SankeyLabelStyle.Legacy"/>.
    /// </summary>
    public SankeyLabelStyle LabelStyle { get; set; }

    /// <summary>Gets the colors of the nodes, by name. The other nodes take the colors of the Tableau 10 scheme, in their order.</summary>
    public IDictionary<string, SceneColor> NodeColors { get; } = new Dictionary<string, SceneColor>(StringComparer.Ordinal);

    /// <summary>Adds a link.</summary>
    /// <param name="source">The name of the node the flow comes from.</param>
    /// <param name="target">The name of the node the flow goes to.</param>
    /// <param name="value">The quantity, which gives the width of the link.</param>
    /// <returns>The link.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative, not finite or larger than 1E+300.</exception>
    public SankeyLink AddLink(string source, string target, double value)
    {
        var link = new SankeyLink(source, target, value);
        Links.Add(link);
        return link;
    }

    /// <summary>Returns the names of the nodes, by order of their first appearance in the links, the source of a link before its target.</summary>
    /// <returns>The names of the nodes.</returns>
    public IReadOnlyList<string> GetNodes()
    {
        var names = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in Links)
        {
            if (known.Add(link.Source))
            {
                names.Add(link.Source);
            }

            if (known.Add(link.Target))
            {
                names.Add(link.Target);
            }
        }

        return names;
    }

    private static double Validate(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"The value must be from {minimum} to {maximum}.");

        return value;
    }

    private protected override void Configure(Meziantou.Framework.Charts.Plot plot) => SankeyChartPlot.Configure(this, plot);
}
