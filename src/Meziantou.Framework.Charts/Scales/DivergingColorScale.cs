namespace Meziantou.Framework.Charts;

/// <summary>Maps numbers to the colors of a diverging ramp: the middle of the ramp is <see cref="Midpoint"/>, and each half covers one side of it.</summary>
public sealed class DivergingColorScale : ColorScale
{
    /// <summary>Gets or sets the ramp. The default is <see cref="ColorScheme.RedBlue"/>.</summary>
    public ColorScheme Scheme { get; set; } = ColorScheme.RedBlue;

    /// <summary>Gets or sets the value of the middle of the ramp. The default is 0.</summary>
    public double Midpoint { get; set; }

    /// <summary>Gets or sets the value of the start of the ramp, or <see langword="null"/> for the smallest value of the data.</summary>
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the value of the end of the ramp, or <see langword="null"/> for the largest value of the data.</summary>
    public double? Maximum { get; set; }

    /// <summary>Gets or sets the format of the labels of the legend, or <see langword="null"/> for the default format.</summary>
    public Func<double, string>? TickFormat { get; set; }
}
