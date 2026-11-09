namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Measures the nodes of a mindmap and gives them their section, as Mermaid.</summary>
internal sealed class MindmapLayoutState(MindmapDiagram diagram, DiagramRenderContext context)
{
    private const int SectionCount = 11;
    private readonly HashSet<MindmapNode> _visited = new(ReferenceEqualityComparer.Instance);

    public DiagramRenderContext Context { get; } = context;

    /// <summary>Returns the color of a section: the first color of the palette for the root, the next ones for the sections.</summary>
    public SceneColor GetColor(int section) => Context.Theme.Palette.Count is 0
        ? Context.Theme.NodeBackground
        : Context.Theme.GetPaletteColor(section + 1);

    public MindmapLayoutNode Build(MindmapNode node, int depth, int section)
    {
        _visited.Add(node);
        var text = Context.Text(node.Label, maxWidth: diagram.MaxNodeWidth);

        // As Mermaid, the shapes with a border have more space around their text
        var padding = diagram.NodePadding * node.Shape switch
        {
            MindmapNodeShape.RoundedSquare => 1.5,
            MindmapNodeShape.Hexagon => 2,
            _ => 1,
        };
        var width = text.Width + (2 * padding);
        var height = text.Height + (Context.FontSize * 1.1 * 0.5) + padding;
        switch (node.Shape)
        {
            case MindmapNodeShape.Circle:
                width = height = Math.Max(width, height);
                break;
            case MindmapNodeShape.Hexagon:
                width += height / 2;
                break;
        }

        var result = new MindmapLayoutNode(node, text)
        {
            Depth = depth,
            Section = depth is 0 ? -1 : diagram.ColorBy is MindmapColorMode.Depth ? (depth - 1) % SectionCount : section,
            Padding = padding,
            Width = width,
            Height = height,
        };

        // A node listed twice, or nested in itself, is drawn once; very deep trees are cut
        if (depth < 256)
        {
            var index = 0;
            foreach (var child in node.Children)
            {
                if (_visited.Contains(child))
                    continue;

                result.Children.Add(Build(child, depth + 1, depth is 0 ? index % SectionCount : section));
                index++;
            }
        }

        var childrenHeight = result.Children.Count is 0 ? 0 : result.Children.Sum(child => child.SubtreeHeight) + (15 * (result.Children.Count - 1));
        result.SubtreeHeight = Math.Max(height, childrenHeight);
        return result;
    }
}
