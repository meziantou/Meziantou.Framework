namespace Meziantou.Framework.Diagrams.Layout;

internal sealed class LayoutEdge(LayoutNode v, LayoutNode w)
{
    public LayoutNode V { get; internal set; } = v;

    public LayoutNode W { get; internal set; } = w;

    public object? Tag { get; set; }

    public double Weight { get; set; } = 1;

    public int MinLength { get; set; } = 1;

    public double LabelWidth { get; set; }

    public double LabelHeight { get; set; }

    /// <summary>Gets the points of the edge, from its source to its target.</summary>
    public List<LayoutPoint> Points { get; internal set; } = [];

    public double LabelX { get; internal set; }

    public double LabelY { get; internal set; }

    public bool HasLabel => LabelWidth > 0 && LabelHeight > 0;

    internal bool Reversed { get; set; }

    internal bool NestingEdge { get; set; }

    internal int LabelRank { get; set; } = int.MinValue;

    internal double CutValue { get; set; }
}
