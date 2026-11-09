using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A treemap: a hierarchy drawn as nested rectangles, each leaf with an area proportional to its value, each section
/// around its children with its name and its total (Mermaid <c>treemap</c>). The layout is the squarified treemap of
/// d3-hierarchy, which Mermaid uses, and the nodes of each section are sorted by decreasing value.
/// </summary>
public sealed class TreemapDiagram : Diagram
{
    private double _nodeWidth = 100;
    private double _nodeHeight = 40;
    private double _innerPadding = 10;
    private string _valueFormat = ",";

    /// <summary>Gets the top-level nodes.</summary>
    public IList<TreemapNode> Nodes { get; } = new List<TreemapNode>();

    /// <summary>Gets the styles of the classes of the nodes, by name (Mermaid <c>classDef</c>).</summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Gets or sets a tenth of the width of the treemap, in pixels, as the <c>nodeWidth</c> of Mermaid. The default is 100: the treemap is 1,000 pixels wide.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 1,000.</exception>
    public double NodeWidth
    {
        get => _nodeWidth;
        set => _nodeWidth = Validate(value, 1, 1000);
    }

    /// <summary>Gets or sets a tenth of the height of the treemap, in pixels, as the <c>nodeHeight</c> of Mermaid. The default is 40: the treemap is 400 pixels high.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 1,000.</exception>
    public double NodeHeight
    {
        get => _nodeHeight;
        set => _nodeHeight = Validate(value, 1, 1000);
    }

    /// <summary>Gets or sets the space between the nodes of a section, in pixels (Mermaid <c>padding</c>). The default is 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1,000.</exception>
    public double InnerPadding
    {
        get => _innerPadding;
        set => _innerPadding = Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets a value that indicates whether the values of the nodes are drawn. The default is <see langword="true"/>.</summary>
    public bool ShowValues { get; set; } = true;

    /// <summary>
    /// Gets or sets the format of the values: a format specifier of d3-format, such as <c>,</c> (the default, with thousands
    /// separators), <c>.1f</c> or <c>.0%</c>, optionally after a <c>$</c>, as in Mermaid. An invalid format falls back to <c>,</c>.
    /// </summary>
    public string ValueFormat
    {
        get => _valueFormat;
        set => _valueFormat = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Adds a top-level node.</summary>
    /// <param name="name">The name, drawn in the node.</param>
    /// <param name="value">The value of a leaf, or <see langword="null"/> for a section.</param>
    /// <returns>The node.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative, not finite or larger than 1E+300.</exception>
    public TreemapNode AddNode(string name, double? value = null)
    {
        var node = new TreemapNode(name, value);
        Nodes.Add(node);
        return node;
    }

    /// <summary>Enumerates the nodes and their descendants, depth first.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<TreemapNode> EnumerateNodes()
    {
        var visited = new HashSet<TreemapNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<TreemapNode>(Nodes.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node))
                continue;

            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private static double Validate(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"The value must be from {minimum} to {maximum}.");

        return value;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => TreemapDiagramRenderer.Render(this, context, group);
}
