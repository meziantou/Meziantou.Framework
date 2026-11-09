namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A link of the layout of a sankey diagram, with the fields of d3-sankey.</summary>
internal sealed class SankeyLayoutLink(SankeyLayoutNode source, SankeyLayoutNode target, double value, int index)
{
    public SankeyLayoutNode Source { get; } = source;

    public SankeyLayoutNode Target { get; } = target;

    /// <summary>Gets the value used by the layout: the value of the link, divided by the largest node value when the values are tiny or huge.</summary>
    public double Value { get; } = value;

    public int Index { get; } = index;

    public double Width { get; set; }

    /// <summary>Gets or sets the vertical center of the link at its source.</summary>
    public double Y0 { get; set; }

    /// <summary>Gets or sets the vertical center of the link at its target.</summary>
    public double Y1 { get; set; }
}
