namespace Meziantou.Framework.Diagrams;

/// <summary>A block of a block diagram: a shape with a text, which edges can link.</summary>
public class BlockDiagramBlock : BlockDiagramItem
{
    /// <summary>Initializes a new instance of the <see cref="BlockDiagramBlock"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram, which is shown when there is no label.</param>
    /// <param name="label">The text, or <see langword="null"/> to show the identifier.</param>
    public BlockDiagramBlock(string id, DiagramText? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Label = label;
    }

    /// <summary>Gets the identifier of the block, unique in the diagram.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the text, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the shape. A composite block is a frame, and an arrow is an arrow, whatever their shape.</summary>
    public FlowchartNodeShape Shape { get; set; }

    /// <summary>Gets or sets the style, which takes precedence over the style of the classes.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the CSS classes: the names of styles of <see cref="BlockDiagram.ClassDefinitions"/>.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Returns the identifier of the block.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
