
using System.Globalization;
using System.Text;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.TreemapDiagrams;

namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A treemap: a hierarchy drawn as nested rectangles, each leaf with an area proportional to its value, each section
/// around its children with its name and its total (Mermaid <c>treemap</c>). It is drawn with the
/// <see cref="TreemapMark{T}"/> of Meziantou.Framework.Charts, whose squarified layout is the one of d3-hierarchy that
/// Mermaid uses, and the nodes of each section are sorted by decreasing value.
/// </summary>
public sealed class TreemapChart : MermaidChart
{
    // The paddings of the sections of Mermaid: a header above the children, and 10 pixels on the other sides
    private const double SectionHeaderHeight = 35;
    private const double SectionPadding = 10;

    // Where Charts draws the title, and the space it leaves under it
    private const double ChartsTitleTop = 10;
    private const double ChartsTitleGap = 8;

    // The deepest level drawn: Hierarchy.FromTree rejects the trees deeper than 1,000 levels
    private const int MaxDrawnDepth = 900;

    private double _diagramPadding = 8;
    private double _nodeWidth = 100;
    private double _nodeHeight = 40;
    private double _innerPadding = 10;
    private string _valueFormat = ",";

    /// <summary>Gets the top-level nodes.</summary>
    public IList<TreemapNode> Nodes { get; } = new List<TreemapNode>();

    /// <summary>
    /// Gets the styles of the classes of the nodes, by name (Mermaid <c>classDef</c>). The fill and the opacity of a
    /// style color the nodes of its class; the border, the text and the font of a style are written back but not drawn.
    /// </summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Gets or sets a tenth of the width of the treemap, in pixels, as the <c>nodeWidth</c> of Mermaid. The default is 100: the treemap is laid out in 1,000 pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 1,000.</exception>
    public double NodeWidth
    {
        get => _nodeWidth;
        set => _nodeWidth = Validate(value, 1, 1000);
    }

    /// <summary>Gets or sets a tenth of the height of the treemap, in pixels, as the <c>nodeHeight</c> of Mermaid. The default is 40: the treemap is laid out in 400 pixels.</summary>
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

    /// <summary>Gets or sets the space around the top-level nodes, in pixels (Mermaid <c>diagramPadding</c>). The default is 8.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1,000.</exception>
    public double DiagramPadding
    {
        get => _diagramPadding;
        set => _diagramPadding = Validate(value, 0, 1000);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the values of the nodes are drawn: under the names of the leaves, and
    /// after the names of the sections. The default is <see langword="true"/>.
    /// </summary>
    public bool ShowValues { get; set; } = true;

    /// <summary>
    /// Gets or sets the format of the values: a format specifier of d3-format, such as <c>,</c> (the default, with thousands
    /// separators), <c>.1f</c> or <c>.0%</c>, optionally after a <c>$</c>, as in Mermaid. An invalid format falls back to <c>,</c>.
    /// A leaf shows its value under its name, and a section after its name.
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

    private protected override void Configure(Plot plot)
    {
        var formatter = TreemapValueFormatter.Create(ValueFormat);
        var root = CreateHierarchy();
        var nodes = root.Descendants().ToList();
        var keys = new Dictionary<HierarchyNode<TreemapNode?>, string>(nodes.Count);
        foreach (var node in nodes)
        {
            keys.Add(node, keys.Count.ToString(CultureInfo.InvariantCulture));
        }

        // The values of the leaves are drawn under their names, those of the sections after their names
        var mark = new TreemapMark<TreemapNode?>(root)
        {
            PaddingInner = InnerPadding,
            PaddingOuter = SectionPadding,
            PaddingTop = SectionHeaderHeight,
            ShowValues = ShowValues,
            ValueFormat = formatter.Format,
            Fill = node => keys[node],
            Label = node => GetLabel(node, formatter),
            Title = node => GetTooltip(node, formatter),
        };

        plot.Add(mark);
        plot.ColorScale = new OrdinalColorScale
        {
            Domain = [.. nodes.Select(node => keys[node])],
            Palette = GetColors(nodes),
        };
        plot.Legend.Visible = false;

        // The plot area is the size Mermaid gives the treemap, whose root is laid out as a section but not drawn. As
        // Mermaid, which fits the image to what it draws, the empty header and sides of the root are cropped: they are
        // in the margins, which are negative when they are larger than DiagramPadding. DiagramPadding is around the
        // sections, under the title.
        var top = DiagramPadding;
        if (plot.Title is { } title)
        {
            var measurer = plot.TextMeasurer ?? AfmTextMeasurer.Default;
            top = Math.Max(DiagramPadding, ChartsTitleTop) + measurer.Measure(title, new SceneFont(plot.Theme.TitleFontSize, Weight: SceneFont.BoldWeight)).Height + ChartsTitleGap;
        }

        var contentWidth = Math.Max(0, (NodeWidth * 10) - (2 * SectionPadding));
        var contentHeight = Math.Max(0, (NodeHeight * 10) - SectionHeaderHeight - SectionPadding);
        plot.Width = Math.Max(1, contentWidth + (2 * DiagramPadding));
        plot.Height = Math.Max(1, top + contentHeight + DiagramPadding);
        plot.Margin = new ChartMargin(top - SectionHeaderHeight, DiagramPadding - SectionPadding, DiagramPadding - SectionPadding, DiagramPadding - SectionPadding);
    }

    /// <summary>Builds the tree to draw: a node that is in the chart twice is drawn once, and the levels deeper than <see cref="MaxDrawnDepth"/> are not drawn.</summary>
    private HierarchyNode<TreemapNode?> CreateHierarchy()
    {
        var visited = new HashSet<TreemapNode>(ReferenceEqualityComparer.Instance);
        var children = new Dictionary<TreemapNode, List<TreemapNode>>(ReferenceEqualityComparer.Instance);
        var topLevel = Claim(Nodes);
        var stack = new Stack<(List<TreemapNode> Nodes, int Depth)>();
        stack.Push((topLevel, 1));
        while (stack.TryPop(out var item))
        {
            foreach (var node in item.Nodes)
            {
                var claimed = item.Depth < MaxDrawnDepth ? Claim(node.Children) : [];
                children.Add(node, claimed);
                if (claimed.Count > 0)
                {
                    stack.Push((claimed, item.Depth + 1));
                }
            }
        }

        return Hierarchy.FromTree<TreemapNode?>(null, node => node is null ? topLevel : children[node], name: node => node is null ? "" : CollapseWhiteSpace(node.Name), value: node => node?.Value ?? 0).SortByValue();

        List<TreemapNode> Claim(IEnumerable<TreemapNode> nodes) => [.. nodes.Where(visited.Add)];
    }

    /// <summary>
    /// Returns the fill of each node, as Mermaid colors them: each section name takes the next color of the palette, in
    /// breadth-first order, and a leaf the color of its section. The top-level leaves, which Mermaid leaves transparent,
    /// take the color of their own name. The fill of the class of a node replaces its color.
    /// </summary>
    private List<SceneColor> GetColors(List<HierarchyNode<TreemapNode?>> nodes)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<HierarchyNode<TreemapNode?>>();
        queue.Enqueue(nodes[0]);
        while (queue.TryDequeue(out var node))
        {
            if (node.Data is { } data && IsSection(node))
            {
                GetIndex(data.Name);
            }

            foreach (var child in node.Children)
            {
                queue.Enqueue(child);
            }
        }

        var colors = new List<SceneColor>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Data is not { } data)
            {
                colors.Add(SceneColor.Transparent);
                continue;
            }

            var name = IsSection(node) || node.Parent!.Data is not { } parent ? data.Name : parent.Name;
            var color = Theme.GetPaletteColor(GetIndex(name));
            if (data.CssClass is { } cssClass && ClassDefinitions.TryGetValue(cssClass, out var style))
            {
                color = style.Fill ?? color;
                if (style.Opacity is { } opacity && double.IsFinite(opacity))
                {
                    color = color.WithOpacity(color.Opacity * opacity);
                }
            }

            colors.Add(color);
        }

        return colors;

        int GetIndex(string name)
        {
            if (!indexes.TryGetValue(name, out var index))
            {
                index = indexes.Count;
                indexes.Add(name, index);
            }

            return index;
        }
    }

    private string? GetLabel(HierarchyNode<TreemapNode?> node, TreemapValueFormatter formatter)
    {
        if (node.Data is null)
            return null;

        if (!ShowValues || node.Value is 0 || node.IsLeaf)
            return node.Name;

        var value = formatter.Format(node.Value);
        return node.Name.Length is 0 ? value : node.Name + " (" + value + ")";
    }

    private string GetTooltip(HierarchyNode<TreemapNode?> node, TreemapValueFormatter formatter)
    {
        var path = string.Join(" / ", node.Ancestors().Reverse().Where(ancestor => ancestor.Name.Length > 0).Select(ancestor => ancestor.Name));
        if (!ShowValues)
            return path;

        var value = formatter.Format(node.Value);
        return path.Length is 0 ? value : path + ": " + value;
    }

    private static bool IsSection(HierarchyNode<TreemapNode?> node) => !node.IsLeaf || node.Data?.Value is null;

    /// <summary>Collapses the white space of a name as SVG does: the line feeds and the runs of spaces become a single space.</summary>
    private static string CollapseWhiteSpace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
