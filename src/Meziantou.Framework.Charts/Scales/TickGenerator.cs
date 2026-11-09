namespace Meziantou.Framework.Charts;

/// <summary>The tick and nice-domain algorithms of d3-array and d3-scale (ISC license), for linear, log and time scales.</summary>
internal static class TickGenerator
{
    private static readonly double E10 = Math.Sqrt(50);
    private static readonly double E5 = Math.Sqrt(10);
    private static readonly double E2 = Math.Sqrt(2);

    /// <summary>Returns about <paramref name="count"/> round values (1, 2 or 5 times a power of 10) between two values.</summary>
    public static double[] Ticks(double start, double stop, double count)
    {
        if (!(count > 0) || !double.IsFinite(start) || !double.IsFinite(stop))
            return [];

        if (start == stop)
            return [start];

        var reverse = stop < start;
        var (i1, i2, increment) = reverse ? TickSpec(stop, start, count) : TickSpec(start, stop, count);
        if (!(i2 >= i1))
            return [];

        var n = (int)(i2 - i1 + 1);
        var ticks = new double[n];
        for (var i = 0; i < n; i++)
        {
            var index = reverse ? i2 - i : i1 + i;
            ticks[i] = increment < 0 ? index / -increment : index * increment;
        }

        return ticks;
    }

    /// <summary>The step of <see cref="Ticks"/>: positive for a step of 1 or more, or the negated inverse of the step for a step below 1.</summary>
    public static double TickIncrement(double start, double stop, double count) => TickSpec(start, stop, count).Increment;

    /// <summary>The step between the values of <see cref="Ticks"/>.</summary>
    public static double TickStep(double start, double stop, double count)
    {
        var reverse = stop < start;
        var increment = reverse ? TickIncrement(stop, start, count) : TickIncrement(start, stop, count);
        var step = increment < 0 ? 1 / -increment : increment;
        return reverse ? -step : step;
    }

    /// <summary>Extends a domain to round values, as d3's <c>linear.nice</c>.</summary>
    public static (double Start, double Stop) Nice(double start, double stop, double count)
    {
        if (start == stop || !double.IsFinite(start) || !double.IsFinite(stop) || !(count > 0))
            return (start, stop);

        var reverse = stop < start;
        if (reverse)
        {
            (start, stop) = (stop, start);
        }

        var previousStep = double.NaN;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var step = TickIncrement(start, stop, count);
            if (step == previousStep)
                break;

            if (step > 0)
            {
                start = Math.Floor(start / step) * step;
                stop = Math.Ceiling(stop / step) * step;
            }
            else if (step < 0)
            {
                start = Math.Ceiling(start * step) / step;
                stop = Math.Floor(stop * step) / step;
            }
            else
            {
                break;
            }

            previousStep = step;
        }

        return reverse ? (stop, start) : (start, stop);
    }

    /// <summary>The ticks of a log scale of positive values: the powers of the base, with the multiples 2 and 5 when the domain spans few powers.</summary>
    public static double[] LogTicks(double start, double stop, double logBase, int count)
    {
        // An infinite bound would have an infinite number of powers
        if (!(start > 0) || !(stop > 0) || !double.IsFinite(start) || !double.IsFinite(stop))
            return [];

        var reverse = stop < start;
        if (reverse)
        {
            (start, stop) = (stop, start);
        }

        var first = (int)Math.Floor(Math.Log(start, logBase) + 1e-9);
        var last = (int)Math.Ceiling(Math.Log(stop, logBase) - 1e-9);
        var powers = last - first;
        var result = new List<double>();
        if (powers * 3 <= count && logBase == 10)
        {
            for (var exponent = first; exponent <= last; exponent++)
            {
                foreach (var factor in (ReadOnlySpan<double>)[1, 2, 5])
                {
                    AddIfInside(factor * Math.Pow(logBase, exponent));
                }
            }
        }
        else
        {
            var stride = Math.Max(1, (int)Math.Ceiling(powers / (double)Math.Max(1, count)));
            for (var exponent = first; exponent <= last; exponent++)
            {
                if ((exponent - first) % stride is 0)
                {
                    AddIfInside(Math.Pow(logBase, exponent));
                }
            }
        }

        if (reverse)
        {
            result.Reverse();
        }

        return [.. result];

        void AddIfInside(double value)
        {
            // Snap to the shortest decimal, so 3 * 10^-1 is 0.3, not 0.30000000000000004
            value = NumberFormats.RoundSignificant(value, 12);
            if (value >= start * (1 - 1e-12) && value <= stop * (1 + 1e-12))
            {
                result.Add(value);
            }
        }
    }

    /// <summary>The ticks of a time scale, in milliseconds since the Unix epoch, UTC.</summary>
    public static double[] TimeTicks(double start, double stop, int count)
    {
        var reverse = stop < start;
        if (reverse)
        {
            (start, stop) = (stop, start);
        }

        var interval = TimeInterval.Choose(start, stop, count);
        var ticks = interval.Range(start, stop);
        if (reverse)
        {
            Array.Reverse(ticks);
        }

        return ticks;
    }

    private static (double I1, double I2, double Increment) TickSpec(double start, double stop, double count)
    {
        var step = (stop - start) / Math.Max(0, count);
        var power = Math.Floor(Math.Log10(step));
        var error = step / Math.Pow(10, power);
        var factor = error >= E10 ? 10 : error >= E5 ? 5 : error >= E2 ? 2 : 1;
        double i1;
        double i2;
        double increment;
        if (power < 0)
        {
            increment = Math.Pow(10, -power) / factor;
            i1 = JsRound(start * increment);
            i2 = JsRound(stop * increment);
            if (i1 / increment < start)
            {
                i1++;
            }

            if (i2 / increment > stop)
            {
                i2--;
            }

            increment = -increment;
        }
        else
        {
            increment = Math.Pow(10, power) * factor;
            i1 = JsRound(start / increment);
            i2 = JsRound(stop / increment);
            if (i1 * increment < start)
            {
                i1++;
            }

            if (i2 * increment > stop)
            {
                i2--;
            }
        }

        if (i2 < i1 && count is >= 0.5 and < 2)
            return TickSpec(start, stop, count * 2);

        return (i1, i2, increment);
    }

    private static double JsRound(double value) => Math.Floor(value + 0.5);
}
