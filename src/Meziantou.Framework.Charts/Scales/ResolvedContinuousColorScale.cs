namespace Meziantou.Framework.Charts;

/// <summary>A sequential or diverging color scale with its domain.</summary>
internal sealed class ResolvedContinuousColorScale(ColorScheme scheme, double minimum, double maximum, double? midpoint, Func<double, string>? format, ChartColor fallback) : ResolvedColorScale
{
    public ColorScheme Scheme { get; } = scheme;

    public double Minimum { get; } = minimum;

    public double Maximum { get; } = maximum;

    public double? Midpoint { get; } = midpoint;

    public override ChartColor Map(ChannelValues values, int index)
    {
        var value = values.GetNumber(index);
        return double.IsNaN(value) ? fallback : MapNumber(value);
    }

    public ChartColor MapNumber(double value) => Scheme.Interpolate(GetPosition(value));

    /// <summary>The position of a value on the ramp, from 0 to 1.</summary>
    public double GetPosition(double value)
    {
        if (Midpoint is { } middle)
        {
            if (value < middle)
                return middle == Minimum ? 0.5 : 0.5 * (value - Minimum) / (middle - Minimum);

            return Maximum == middle ? 0.5 : 0.5 + (0.5 * (value - middle) / (Maximum - middle));
        }

        return Maximum == Minimum ? 0.5 : (value - Minimum) / (Maximum - Minimum);
    }

    public string Format(double value, double step) => format?.Invoke(value) ?? NumberFormats.ForStep(value, step);
}
