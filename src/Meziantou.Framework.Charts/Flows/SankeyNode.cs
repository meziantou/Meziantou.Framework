namespace Meziantou.Framework.Charts;

/// <summary>A node of a Sankey layout.</summary>
internal sealed class SankeyNode(string name, int index)
{
    public string Name { get; } = name;

    public int Index { get; } = index;

    /// <summary>The links that leave the node.</summary>
    public List<SankeyLink> SourceLinks { get; } = [];

    /// <summary>The links that reach the node.</summary>
    public List<SankeyLink> TargetLinks { get; } = [];

    public double Value { get; set; }

    public int Depth { get; set; }

    public int Height { get; set; }

    public int Layer { get; set; }

    public double X0 { get; set; }

    public double X1 { get; set; }

    public double Y0 { get; set; }

    public double Y1 { get; set; }
}
