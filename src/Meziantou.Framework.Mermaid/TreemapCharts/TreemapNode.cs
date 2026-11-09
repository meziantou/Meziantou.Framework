namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A node of a treemap: a section, which groups other nodes, or a leaf, whose value gives its area. As in Mermaid, a node
/// with a <see cref="Value"/> is a leaf, and a node without one is a section.
/// </summary>
public sealed class TreemapNode
{
    private string _name;
    private double? _value;

    /// <summary>Initializes a new instance of the <see cref="TreemapNode"/> class.</summary>
    /// <param name="name">The name, drawn in the node.</param>
    /// <param name="value">The value of a leaf, or <see langword="null"/> for a section.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative, not finite or larger than 1E+300.</exception>
    public TreemapNode(string name, double? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        _value = ValidateValue(value);
    }

    /// <summary>Gets or sets the name, drawn in the node.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the value of a leaf, or <see langword="null"/> for a section, whose value is the sum of the values of its children.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, not finite or larger than 1E+300.</exception>
    public double? Value
    {
        get => _value;
        set => _value = ValidateValue(value);
    }

    /// <summary>Gets the children of a section. In Mermaid, a leaf has no children.</summary>
    public IList<TreemapNode> Children { get; } = new List<TreemapNode>();

    /// <summary>Gets or sets the name of the class of the node (Mermaid <c>:::class</c>), whose style is in <see cref="TreemapChart.ClassDefinitions"/>, or <see langword="null"/> for none.</summary>
    public string? CssClass { get; set; }

    /// <summary>Adds a child.</summary>
    /// <param name="name">The name, drawn in the node.</param>
    /// <param name="value">The value of a leaf, or <see langword="null"/> for a section.</param>
    /// <returns>The child.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative, not finite or larger than 1E+300.</exception>
    public TreemapNode AddChild(string name, double? value = null)
    {
        var node = new TreemapNode(name, value);
        Children.Add(node);
        return node;
    }

    private static double? ValidateValue(double? value)
    {
        if (value is { } number && (!(number >= 0) || number > 1e300))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a positive number, at most 1E+300.");

        return value;
    }
}
