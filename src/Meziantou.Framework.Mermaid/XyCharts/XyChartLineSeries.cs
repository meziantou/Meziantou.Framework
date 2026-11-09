namespace Meziantou.Framework.Mermaid;

/// <summary>A series of an <see cref="XyChart"/> drawn as a line through its values (Mermaid <c>line</c>).</summary>
public sealed class XyChartLineSeries : XyChartSeries
{
    /// <summary>
    /// Gets the labels of the points, drawn next to them in the color of the line: the label at an index belongs to the
    /// value at the same index. A <see langword="null"/> or empty label is not drawn.
    /// </summary>
    public IList<string?> PointLabels { get; } = new List<string?>();
}
