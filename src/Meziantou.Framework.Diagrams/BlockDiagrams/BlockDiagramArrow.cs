namespace Meziantou.Framework.Diagrams;

/// <summary>A block arrow of a block diagram: a wide arrow with a text, pointing in one or more directions (Mermaid <c>id&lt;["text"]&gt;(right)</c>).</summary>
public sealed class BlockDiagramArrow : BlockDiagramBlock
{
    /// <summary>Initializes a new instance of the <see cref="BlockDiagramArrow"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram.</param>
    /// <param name="label">The text, or <see langword="null"/> to show the identifier.</param>
    /// <param name="directions">The directions the arrow points to.</param>
    public BlockDiagramArrow(string id, DiagramText? label = null, BlockArrowDirections directions = BlockArrowDirections.Right)
        : base(id, label)
    {
        Directions = directions;
    }

    /// <summary>Gets or sets the directions the arrow points to. An arrow with no direction is a rectangle.</summary>
    public BlockArrowDirections Directions { get; set; }
}
