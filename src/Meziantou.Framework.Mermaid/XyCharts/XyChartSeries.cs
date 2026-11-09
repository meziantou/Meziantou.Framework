namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A series of values of an <see cref="XyChart"/>, drawn as bars (<see cref="XyChartBarSeries"/>) or as a line
/// (<see cref="XyChartLineSeries"/>). The value at an index belongs to the category at the same index of the x axis.
/// </summary>
public abstract class XyChartSeries
{
    private protected XyChartSeries()
    {
    }

    /// <summary>Gets or sets the name of the series, shown in the legend, or <see langword="null"/> to leave the series out of the legend.</summary>
    public string? Name { get; set; }

    /// <summary>Gets the values. A value that is not finite is not drawn.</summary>
    public IList<double> Values { get; } = new List<double>();
}
