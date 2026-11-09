namespace Meziantou.Framework.Diagrams;

/// <summary>An edge of a block diagram: a straight line between two blocks.</summary>
public sealed class BlockDiagramEdge
{
    /// <summary>Initializes a new instance of the <see cref="BlockDiagramEdge"/> class.</summary>
    /// <param name="from">The source.</param>
    /// <param name="to">The target.</param>
    public BlockDiagramEdge(BlockDiagramBlock from, BlockDiagramBlock to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
    }

    /// <summary>Gets the source.</summary>
    public BlockDiagramBlock From { get; }

    /// <summary>Gets the target.</summary>
    public BlockDiagramBlock To { get; }

    /// <summary>Gets or sets the text, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the style of the line.</summary>
    public FlowchartLineStyle LineStyle { get; set; }

    /// <summary>Gets or sets the decoration at <see cref="From"/>.</summary>
    public FlowchartArrowHead StartArrowHead { get; set; }

    /// <summary>Gets or sets the decoration at <see cref="To"/>. The default is an arrowhead.</summary>
    public FlowchartArrowHead EndArrowHead { get; set; } = FlowchartArrowHead.Arrow;
}
