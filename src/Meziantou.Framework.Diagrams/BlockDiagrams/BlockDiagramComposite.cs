namespace Meziantou.Framework.Diagrams;

/// <summary>A composite block of a block diagram: a frame around items placed on its own grid of columns (Mermaid <c>block ... end</c>).</summary>
public sealed class BlockDiagramComposite : BlockDiagramBlock
{
    private int? _columns;

    /// <summary>Initializes a new instance of the <see cref="BlockDiagramComposite"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram.</param>
    /// <param name="label">The title, or <see langword="null"/> for none.</param>
    public BlockDiagramComposite(string id, DiagramText? label = null)
        : base(id, label)
    {
    }

    /// <summary>Gets or sets the number of columns, or <see langword="null"/> to place every item on one row.</summary>
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

    /// <summary>Gets the items, placed in order, left to right and top to bottom.</summary>
    public IList<BlockDiagramItem> Items { get; } = new List<BlockDiagramItem>();
}
