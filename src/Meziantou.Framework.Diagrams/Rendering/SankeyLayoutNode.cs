namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A node of the layout of a sankey diagram, with the fields of d3-sankey.</summary>
internal sealed class SankeyLayoutNode(string name, int index)
{
    public string Name { get; } = name;

    public int Index { get; } = index;

    public List<SankeyLayoutLink> SourceLinks { get; } = [];

    public List<SankeyLayoutLink> TargetLinks { get; } = [];

    /// <summary>Gets or sets the value used by the layout: the largest of the sums of the values of its links, scaled as the values of the links.</summary>
    public double Value { get; set; }

    /// <summary>Gets or sets the value of the node, as written in its label: the largest of the sums of its incoming and outgoing links.</summary>
    public double DisplayValue { get; set; }

    public int Depth { get; set; }

    public int Height { get; set; }

    public int Layer { get; set; }

    public double X0 { get; set; }

    public double X1 { get; set; }

    public double Y0 { get; set; }

    public double Y1 { get; set; }
}
