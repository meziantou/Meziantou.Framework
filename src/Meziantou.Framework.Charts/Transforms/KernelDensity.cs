namespace Meziantou.Framework.Charts;

/// <summary>Gaussian kernel density estimation.</summary>
internal static class KernelDensity
{
    private static readonly double InverseSqrtTwoPi = 1 / Math.Sqrt(2 * Math.PI);

    /// <summary>Silverman's rule of thumb: 0.9 min(σ, IQR / 1.34) n^(-1/5).</summary>
    public static double SilvermanBandwidth(double[] sorted)
    {
        if (sorted.Length is 0)
            return 1;

        var mean = sorted.Average();
        var deviation = sorted.Length > 1 ? Math.Sqrt(sorted.Sum(value => (value - mean) * (value - mean)) / (sorted.Length - 1)) : 0;
        var spread = (BoxStatistics.Quantile(sorted, 0.75) - BoxStatistics.Quantile(sorted, 0.25)) / 1.34;
        var scale = Math.Min(deviation, spread) is var minimum && minimum > 0 ? minimum : Math.Max(deviation, spread);
        if (!(scale > 0))
        {
            // All the values are equal: a bandwidth of a tenth of their magnitude, or 1 around zero
            scale = mean == 0 ? 1 : Math.Abs(mean) / 10;
        }

        return 0.9 * scale * Math.Pow(sorted.Length, -0.2);
    }

    public static double Estimate(double[] values, double bandwidth, double x)
    {
        var sum = 0.0;
        foreach (var value in values)
        {
            var u = (x - value) / bandwidth;
            sum += Math.Exp(-0.5 * u * u);
        }

        return sum * InverseSqrtTwoPi / (values.Length * bandwidth);
    }

    /// <summary>Evaluates the density at evenly spaced points between two values, both included.</summary>
    public static (double[] X, double[] Density) Evaluate(double[] values, double bandwidth, double start, double end, int samples)
    {
        samples = Math.Max(2, samples);
        var x = new double[samples];
        var density = new double[samples];
        for (var i = 0; i < samples; i++)
        {
            x[i] = start + ((end - start) * i / (samples - 1));
            density[i] = Estimate(values, bandwidth, x[i]);
        }

        return (x, density);
    }

    /// <summary>Returns the largest density of curves that alternate a value and its density, or 0 when there is none.</summary>
    public static double MaxDensity(IEnumerable<IReadOnlyList<double>> curves)
    {
        var max = 0d;
        foreach (var curve in curves)
        {
            for (var i = 1; i < curve.Count; i += 2)
            {
                if (curve[i] > max)
                {
                    max = curve[i];
                }
            }
        }

        return max;
    }

    /// <summary>
    /// Evaluates the density of points with a product of Gaussian kernels on a square grid of <paramref name="size"/> points
    /// per axis, from <paramref name="cut"/> bandwidths before the smallest value to as many after the largest. The value of
    /// the point (i, j) is at <c>grid[j * size + i]</c>, at x = x0 + i stepX and y = y0 + j stepY.
    /// </summary>
    public static (double[] Grid, double X0, double StepX, double Y0, double StepY) Evaluate2D(double[] xs, double[] ys, double bandwidthX, double bandwidthY, int size, double cut = 3)
    {
        var x0 = xs.Min() - (cut * bandwidthX);
        var y0 = ys.Min() - (cut * bandwidthY);
        var stepX = (xs.Max() + (cut * bandwidthX) - x0) / (size - 1);
        var stepY = (ys.Max() + (cut * bandwidthY) - y0) / (size - 1);
        var kx = Kernels(xs, x0, stepX, bandwidthX, size);
        var ky = Kernels(ys, y0, stepY, bandwidthY, size);
        var scale = 1 / (xs.Length * 2 * Math.PI * bandwidthX * bandwidthY);
        var grid = new double[size * size];
        for (var j = 0; j < size; j++)
        {
            for (var i = 0; i < size; i++)
            {
                var sum = 0.0;
                for (var k = 0; k < xs.Length; k++)
                {
                    sum += kx[(i * xs.Length) + k] * ky[(j * xs.Length) + k];
                }

                grid[(j * size) + i] = sum * scale;
            }
        }

        return (grid, x0, stepX, y0, stepY);

        static double[] Kernels(double[] values, double start, double step, double bandwidth, int size)
        {
            var result = new double[size * values.Length];
            for (var i = 0; i < size; i++)
            {
                var position = start + (i * step);
                for (var k = 0; k < values.Length; k++)
                {
                    var u = (position - values[k]) / bandwidth;
                    result[(i * values.Length) + k] = Math.Exp(-0.5 * u * u);
                }
            }

            return result;
        }
    }
}
