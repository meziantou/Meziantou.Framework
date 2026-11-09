using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A block diagram: blocks placed on a grid of columns in the order they are declared, spanning one or more columns,
/// grouped in composite blocks, with block arrows, empty spaces and edges (Mermaid <c>block</c>). Unlike a flowchart,
/// the author decides where each block goes.
/// </summary>
public sealed class BlockDiagram : Diagram
{
    private int? _columns;

    /// <summary>Gets or sets the number of columns of the top level, or <see langword="null"/> to place every item on one row (Mermaid <c>columns auto</c>).</summary>
    public int? Columns
    {
        get => _columns;
        set
        {
            if (value is < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "A grid has one column at least.");

            _columns = value;
        }
    }

    /// <summary>Gets the items of the top level, placed in order, left to right and top to bottom.</summary>
    public IList<BlockDiagramItem> Items { get; } = new List<BlockDiagramItem>();

    /// <summary>Gets the edges, drawn in order.</summary>
    public IList<BlockDiagramEdge> Edges { get; } = new List<BlockDiagramEdge>();

    /// <summary>Gets the styles of the CSS classes of the blocks, by name (Mermaid <c>classDef</c>). The class <c>default</c> applies to every block.</summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Adds a block to the top level.</summary>
    /// <param name="id">The identifier, which is shown when there is no label.</param>
    /// <param name="label">The text, or <see langword="null"/> to show the identifier.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The block.</returns>
    public BlockDiagramBlock AddBlock(string id, DiagramText? label = null, FlowchartNodeShape shape = FlowchartNodeShape.Rectangle)
    {
        var block = new BlockDiagramBlock(id, label) { Shape = shape };
        Items.Add(block);
        return block;
    }

    /// <summary>Adds an edge.</summary>
    /// <param name="from">The source.</param>
    /// <param name="to">The target.</param>
    /// <param name="label">The text, or <see langword="null"/> for none.</param>
    /// <returns>The edge.</returns>
    public BlockDiagramEdge AddEdge(BlockDiagramBlock from, BlockDiagramBlock to, DiagramText? label = null)
    {
        var edge = new BlockDiagramEdge(from, to) { Label = label };
        Edges.Add(edge);
        return edge;
    }

    /// <summary>Finds a block by identifier, in the top level and the composite blocks.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The block, or <see langword="null"/> when there is none.</returns>
    public BlockDiagramBlock? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var block in EnumerateBlocks())
        {
            if (string.Equals(block.Id, id, StringComparison.Ordinal))
                return block;
        }

        return null;
    }

    /// <summary>Enumerates the blocks, the arrows and the composite blocks with their content, depth first.</summary>
    /// <returns>The blocks.</returns>
    public IEnumerable<BlockDiagramBlock> EnumerateBlocks()
    {
        var visited = new HashSet<BlockDiagramBlock>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<BlockDiagramItem>(Items.Reverse());
        while (stack.Count > 0)
        {
            if (stack.Pop() is not BlockDiagramBlock block || !visited.Add(block))
                continue;

            yield return block;
            if (block is BlockDiagramComposite composite)
            {
                for (var i = composite.Items.Count - 1; i >= 0; i--)
                {
                    stack.Push(composite.Items[i]);
                }
            }
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => BlockDiagramRenderer.Render(this, context, group);
}
