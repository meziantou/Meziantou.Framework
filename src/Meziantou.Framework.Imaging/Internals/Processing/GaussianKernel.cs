namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The weights of a Gaussian blur: the same on every platform.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The blur of a standard deviation <c>s</c> has the radius <c>r = ceil(3 s)</c> and the
/// <c>2 r + 1</c> weights <c>exp(-i^2 / (2 s^2))</c> for <c>i</c> from <c>-r</c> to <c>r</c>, divided by their sum
/// (added from the first one to the last one): what is beyond three standard deviations, 0.3% of the bell, is left
/// out, and the weights add up to 1. Without deviation, the only weight is 1.
/// </para>
/// <para>
/// The exponential is computed with additions, multiplications and divisions only (its argument is halved until its
/// series converges in a few terms, and the result squared as many times): the function of the platform could differ
/// by a unit in the last place from a platform to another.
/// </para>
/// </remarks>
internal static class GaussianKernel
{
    /// <summary>The largest standard deviation a blur accepts: its weights are 60,001.</summary>
    public const double MaxSigma = 10000;

    /// <summary>Validates a standard deviation.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds <see cref="MaxSigma"/>.</exception>
    public static void Validate(double sigma, string paramName)
    {
        if (!double.IsFinite(sigma))
            throw new ArgumentException("The standard deviation must be a finite number.", paramName);

        if (sigma < 0 || sigma > MaxSigma)
            throw new ArgumentOutOfRangeException(paramName, sigma, "The standard deviation must be between 0 and 10000.");
    }

    /// <summary>Gets the radius of the blur of a standard deviation: the number of weights on each side of the middle one.</summary>
    public static int GetRadius(double sigma) => (int)Math.Ceiling(3 * sigma);

    /// <summary>Computes the weights of a blur.</summary>
    /// <param name="sigma">The standard deviation, validated by the caller.</param>
    /// <param name="weights">The weights: twice the radius plus one.</param>
    public static void Fill(double sigma, Span<double> weights)
    {
        var radius = weights.Length / 2;
        weights[radius] = 1;
        var scale = 2 * sigma * sigma;
        for (var i = 1; i <= radius; i++)
        {
            weights[radius - i] = weights[radius + i] = ExpNegative(i * (double)i / scale);
        }

        var sum = 0d;
        foreach (var weight in weights)
        {
            sum += weight;
        }

        foreach (ref var weight in weights)
        {
            weight /= sum;
        }
    }

    /// <summary>Creates the weights of a blur.</summary>
    public static double[] Create(double sigma)
    {
        var weights = new double[(2 * GetRadius(sigma)) + 1];
        Fill(sigma, weights);
        return weights;
    }

    /// <summary>Gets the exponential of the opposite of a number that is not negative.</summary>
    private static double ExpNegative(double value)
    {
        // exp(-x) = exp(-x / 2^k)^(2^k): the series of an argument under 1/8 is exact to the last place in 12 terms
        var squarings = 0;
        while (value > 0.125)
        {
            value /= 2;
            squarings++;
        }

        var term = 1d;
        var sum = 1d;
        for (var n = 1; n <= 12; n++)
        {
            term *= -value / n;
            sum += term;
        }

        for (; squarings > 0; squarings--)
        {
            sum *= sum;
        }

        return sum;
    }
}
