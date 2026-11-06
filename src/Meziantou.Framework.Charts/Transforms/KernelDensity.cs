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
}
