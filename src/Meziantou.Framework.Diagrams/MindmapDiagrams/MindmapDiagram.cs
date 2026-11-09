using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A mindmap: a tree of ideas around a central one, each branch in its own color (Mermaid <c>mindmap</c>). The root
/// is in the middle, its first children on its right and the others on its left.
/// </summary>
public sealed class MindmapDiagram : Diagram
{
    private double _maxNodeWidth = 200;
    private double _nodePadding = 10;

    /// <summary>Gets or sets the central node, or <see langword="null"/> for an empty mindmap.</summary>
    public MindmapNode? Root { get; set; }

    /// <summary>Gets or sets how the nodes are colored: by branch of the root (the default) or by depth (Mermaid <c>colorBy</c>).</summary>
    public MindmapColorMode ColorBy { get; set; }

    /// <summary>Gets or sets the width at which the text of a node wraps, in pixels. The default is 200 (Mermaid <c>maxNodeWidth</c>).</summary>
    public double MaxNodeWidth
    {
        get => _maxNodeWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 10_000);
            _maxNodeWidth = value;
        }
    }

    /// <summary>Gets or sets the space between the text of a node and its border, in pixels. The default is 10 (Mermaid <c>padding</c>).</summary>
    public double NodePadding
    {
        get => _nodePadding;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _nodePadding = value;
        }
    }

    /// <summary>Sets the central node.</summary>
    /// <param name="label">The text.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The node.</returns>
    public MindmapNode SetRoot(DiagramText label, MindmapNodeShape shape = MindmapNodeShape.Default)
    {
        Root = new MindmapNode(label, shape);
        return Root;
    }

    /// <summary>Enumerates the nodes, depth first, from the root.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<MindmapNode> EnumerateNodes()
    {
        if (Root is null)
            yield break;

        var visited = new HashSet<MindmapNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<MindmapNode>();
        stack.Push(Root);
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

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => MindmapDiagramRenderer.Render(this, context, group);
}
