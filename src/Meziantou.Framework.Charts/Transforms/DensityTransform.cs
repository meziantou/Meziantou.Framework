namespace Meziantou.Framework.Charts;

/// <summary>
/// Estimates the probability density of the x values with a Gaussian kernel, for each series (the <c>Series</c>
/// channel, else the colors). Each series becomes rows of evenly spaced x values, with their density as y. Draw it with
/// an <see cref="AreaMark{T}"/> or a <see cref="LineMark{T}"/>: a density plot. The y channel of the mark is ignored.
/// </summary>
public sealed class DensityTransform : Transform
{
    private int _samples = 100;

    /// <summary>Gets or sets the bandwidth of the kernel, in the unit of x, or <see langword="null"/> for Silverman's rule of thumb. A larger bandwidth gives a smoother curve.</summary>
    public double? Bandwidth { get; set; }

    /// <summary>Gets or sets the number of points of each curve. The default is 100.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 2.</exception>
    public int Samples
    {
        get => _samples;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
            _samples = value;
        }
    }

    /// <summary>Gets or sets how far the curves go beyond the smallest and the largest values, in bandwidths. The default is 3, where the density is almost zero.</summary>
    public double Cut { get; set; } = 3;

    internal override MarkData Apply(MarkData data)
    {
        var x = data.X;
        if (x is not { IsContinuous: true })
            return data;

        var key = data.SeriesKey;
        var xs = new List<double>();
        var ys = new List<double>();
        var rows = new List<int>();
        foreach (var group in MarkData.GroupBy(data.Count, key))
        {
            var values = group.Select(row => x.Numbers[row]).Where(value => !double.IsNaN(value)).Order().ToArray();
            if (values.Length is 0)
                continue;

            var bandwidth = Bandwidth is > 0 ? Bandwidth.Value : KernelDensity.SilvermanBandwidth(values);
            var (points, density) = KernelDensity.Evaluate(values, bandwidth, values[0] - (Cut * bandwidth), values[^1] + (Cut * bandwidth), Samples);
            xs.AddRange(points);
            ys.AddRange(density);
            rows.AddRange(Enumerable.Repeat(group[0], points.Length));
        }

        // The other channels come from the first row of the series
        var result = data.Select(rows);
        result.X = ChannelValues.FromNumbers([.. xs], x.Kind);
        result.Y = ChannelValues.FromNumbers([.. ys]);
        result.Y1 = null;
        result.Y2 = null;
        result.Titles = null;
        return result;
    }
}
