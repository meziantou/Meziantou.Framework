namespace Meziantou.Framework.Charts;

/// <summary>A link of a Sankey layout.</summary>
internal sealed class SankeyLink(SankeyNode source, SankeyNode target, double value, int index)
{
    public SankeyNode Source { get; } = source;

    public SankeyNode Target { get; } = target;

    public double Value { get; } = value;

    /// <summary>The index of the link, which is also the index of its row.</summary>
    public int Index { get; } = index;

    public double Width { get; set; }

    /// <summary>The y coordinate of the middle of the link where it leaves its source.</summary>
    public double Y0 { get; set; }

    /// <summary>The y coordinate of the middle of the link where it reaches its target.</summary>
    public double Y1 { get; set; }
}
