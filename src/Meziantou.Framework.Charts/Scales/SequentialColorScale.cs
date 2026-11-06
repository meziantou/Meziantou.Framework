namespace Meziantou.Framework.Charts;

/// <summary>Maps numbers to the colors of a ramp, from its start for the smallest value to its end for the largest.</summary>
public sealed class SequentialColorScale : ColorScale
{
    /// <summary>Gets or sets the ramp. The default is <see cref="ColorScheme.Viridis"/>.</summary>
    public ColorScheme Scheme { get; set; } = ColorScheme.Viridis;

    /// <summary>Gets or sets the value of the start of the ramp, or <see langword="null"/> for the smallest value of the data.</summary>
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the value of the end of the ramp, or <see langword="null"/> for the largest value of the data.</summary>
    public double? Maximum { get; set; }

    /// <summary>Gets or sets the format of the labels of the legend, or <see langword="null"/> for the default format.</summary>
    public Func<double, string>? TickFormat { get; set; }
}
