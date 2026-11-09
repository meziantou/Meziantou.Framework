namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A series of an XY chart and its points on the x axis.</summary>
internal sealed class XyChartPlot(int index, XyChartSeries series, List<XyChartPoint> points)
{
    /// <summary>Gets the index of the series, which picks its color.</summary>
    public int Index { get; } = index;

    public XyChartSeries Series { get; } = series;

    public List<XyChartPoint> Points { get; } = points;
}
