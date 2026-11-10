namespace Meziantou.Framework.Diagrams;

/// <summary>A node of a mindmap: a text in a shape, with its children.</summary>
public sealed class MindmapNode
{
    /// <summary>Initializes a new instance of the <see cref="MindmapNode"/> class.</summary>
    /// <param name="label">The text.</param>
    /// <param name="shape">The shape.</param>
    public MindmapNode(DiagramText label, MindmapNodeShape shape = MindmapNodeShape.Default)
    {
        ArgumentNullException.ThrowIfNull(label);
        Label = label;
        Shape = shape;
    }

    /// <summary>Gets or sets the identifier written before the shape in Mermaid (<c>id[text]</c>), or <see langword="null"/> for none. The mindmaps do not use it.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the text.</summary>
    public DiagramText Label { get; set; }

    /// <summary>Gets or sets the shape.</summary>
    public MindmapNodeShape Shape { get; set; }

    /// <summary>Gets or sets the name of the icon (Mermaid <c>::icon(fa fa-book)</c>), which is written to Mermaid but not drawn, or <see langword="null"/> for none.</summary>
    public string? Icon { get; set; }

    /// <summary>Gets the CSS classes of the node, written to the SVG (Mermaid <c>:::urgent large</c>).</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Gets the children, in order.</summary>
    public IList<MindmapNode> Children { get; } = new List<MindmapNode>();

    /// <summary>Adds a child.</summary>
    /// <param name="label">The text.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The child.</returns>
    public MindmapNode AddChild(DiagramText label, MindmapNodeShape shape = MindmapNodeShape.Default)
    {
        var child = new MindmapNode(label, shape);
        Children.Add(child);
        return child;
    }

    /// <summary>Returns the text of the node.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Label.Text;
}
