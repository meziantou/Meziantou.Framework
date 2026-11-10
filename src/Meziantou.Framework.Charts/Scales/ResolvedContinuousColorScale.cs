namespace Meziantou.Framework.Charts;

/// <summary>A sequential or diverging color scale with its domain.</summary>
internal sealed class ResolvedContinuousColorScale(ColorScheme scheme, double minimum, double maximum, double? midpoint, Func<double, string>? format, SceneColor fallback) : ResolvedColorScale
{
    public ColorScheme Scheme { get; } = scheme;

    public double Minimum { get; private set; } = minimum;

    public double Maximum { get; private set; } = maximum;

    /// <summary>Gets a value that indicates whether the scale sets its minimum, which <see cref="Include"/> keeps.</summary>
    public bool HasFixedMinimum { get; init; }

    /// <summary>Gets a value that indicates whether the scale sets its maximum, which <see cref="Include"/> keeps.</summary>
    public bool HasFixedMaximum { get; init; }

    public double? Midpoint { get; } = midpoint;

    public override SceneColor Map(ChannelValues values, int index)
    {
        var value = values.GetNumber(index);
        return double.IsNaN(value) ? fallback : MapNumber(value);
    }

    /// <summary>Extends the domain to a value known once the plot is laid out, such as the count of a hexagon.</summary>
    public void Include(double value)
    {
        if (double.IsNaN(value))
            return;

        if (!HasFixedMinimum && !(Minimum <= value))
        {
            Minimum = value;
        }

        if (!HasFixedMaximum && !(Maximum >= value))
        {
            Maximum = value;
        }
    }

    public SceneColor MapNumber(double value) => Scheme.Interpolate(GetPosition(value));

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
