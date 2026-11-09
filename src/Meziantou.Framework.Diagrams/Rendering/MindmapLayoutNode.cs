namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A node of a mindmap being laid out: its size, the height of its subtree and its position.</summary>
internal sealed class MindmapLayoutNode(MindmapNode source, TextBlock text)
{
    public MindmapNode Source { get; } = source;

    public TextBlock Text { get; } = text;

    public List<MindmapLayoutNode> Children { get; } = [];

    public int Depth { get; set; }

    /// <summary>Gets or sets the section, which gives the color: -1 for the root.</summary>
    public int Section { get; set; }

    public double Padding { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double SubtreeHeight { get; set; }

    public double X { get; set; }

    public double Y { get; set; }
}
