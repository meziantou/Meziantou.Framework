namespace Meziantou.Framework.Diagrams;

/// <summary>A node of a flowchart: a shape with a label.</summary>
public sealed class FlowchartNode : FlowchartElement
{
    /// <summary>Initializes a new instance of the <see cref="FlowchartNode"/> class.</summary>
    /// <param name="id">The identifier of the node, unique in the flowchart.</param>
    /// <param name="label">The text of the node, or <see langword="null"/> to show its identifier.</param>
    /// <param name="shape">The shape of the node.</param>
    public FlowchartNode(string id, DiagramText? label = null, FlowchartNodeShape shape = FlowchartNodeShape.Rectangle)
        : base(id)
    {
        Label = label;
        Shape = shape;
    }

    /// <summary>Gets or sets the shape of the node.</summary>
    public FlowchartNodeShape Shape { get; set; }
}
