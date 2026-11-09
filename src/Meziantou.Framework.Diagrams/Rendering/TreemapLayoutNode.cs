namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A node of the layout of a treemap, with the fields of a node of d3-hierarchy.</summary>
internal sealed class TreemapLayoutNode(TreemapNode? data, string name, int depth, TreemapLayoutNode? parent)
{
    /// <summary>Gets the node of the diagram, or <see langword="null"/> for the root, which holds the top-level nodes.</summary>
    public TreemapNode? Data { get; } = data;

    public string Name { get; } = name;

    public int Depth { get; } = depth;

    public TreemapLayoutNode? Parent { get; } = parent;

    public List<TreemapLayoutNode> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public double Value { get; set; }

    public double X0 { get; set; }

    public double Y0 { get; set; }

    public double X1 { get; set; }

    public double Y1 { get; set; }

    public double Width => X1 - X0;

    public double Height => Y1 - Y0;
}
