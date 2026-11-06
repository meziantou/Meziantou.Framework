namespace Meziantou.Framework.Charts;

/// <summary>A scale of continuous values: numbers or times.</summary>
public abstract class ContinuousScale : Scale
{
    private protected ContinuousScale()
    {
    }

    /// <summary>Gets or sets the start of the domain, or <see langword="null"/> to use the smallest value of the data.</summary>
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the end of the domain, or <see langword="null"/> to use the largest value of the data.</summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the domain is extended to round values, or <see langword="null"/> for
    /// the default: on for numbers, off for times. A bound set with <see cref="Minimum"/> or <see cref="Maximum"/> is kept.
    /// </summary>
    public bool? Nice { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the domain includes zero, or <see langword="null"/> for the default:
    /// on when a mark of the plot has a baseline, such as bars and areas.
    /// </summary>
    public bool? Zero { get; set; }

    /// <summary>Gets or sets a value that indicates whether the values outside the domain are moved to its bounds.</summary>
    public bool Clamp { get; set; }

    /// <summary>Gets or sets the approximate number of ticks, or <see langword="null"/> to choose it from the length of the axis.</summary>
    public int? TickCount { get; set; }

    /// <summary>Gets or sets the values of the ticks, or <see langword="null"/> to generate them.</summary>
    public IReadOnlyList<double>? Ticks { get; set; }

    /// <summary>Gets or sets the format of the tick labels, or <see langword="null"/> for the default format (see <see cref="NumberFormats"/>).</summary>
    public Func<double, string>? TickFormat { get; set; }
}
