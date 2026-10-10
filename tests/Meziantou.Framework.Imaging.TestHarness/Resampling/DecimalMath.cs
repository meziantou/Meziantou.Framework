namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>
/// Elementary functions in <see cref="decimal"/> arithmetic (about 26 correct significant digits), written from their
/// series definitions so that the reference resampler shares no floating-point code with the library: sine (Taylor
/// series after reduction to [-pi/2, pi/2]), natural logarithm (atanh series after reduction to [0.5, 1)) and exponential
/// (Taylor series after halving, then repeated squaring).
/// </summary>
public static class DecimalMath
{
    /// <summary>The constant pi.</summary>
    public const decimal Pi = 3.1415926535897932384626433833m;

    /// <summary>The natural logarithm of 2.</summary>
    public const decimal Ln2 = 0.6931471805599453094172321215m;

    private const decimal Epsilon = 1e-27m;

    /// <summary>Computes <c>sin(pi * x)</c>.</summary>
    /// <param name="x">The argument, in half turns.</param>
    /// <returns>The sine.</returns>
    public static decimal SinPi(decimal x)
    {
        // Reduce to [-1, 1) half turns, then use sin(pi - t) = sin(t) to reach [-1/2, 1/2]
        x -= 2 * decimal.Floor((x + 1) / 2);
        if (x > 0.5m)
        {
            x = 1 - x;
        }
        else if (x < -0.5m)
        {
            x = -1 - x;
        }

        if (x == 0)
            return 0;

        var t = Pi * x;
        var term = t;
        var sum = t;
        var squared = t * t;
        for (var k = 1; Math.Abs(term) > Epsilon; k++)
        {
            term = -term * squared / ((2 * k) * ((2 * k) + 1));
            sum += term;
        }

        return sum;
    }

    /// <summary>Computes the natural logarithm of a positive value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The logarithm.</returns>
    public static decimal Ln(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        var exponent = 0;
        while (value >= 1)
        {
            value /= 2;
            exponent++;
        }

        while (value < 0.5m)
        {
            value *= 2;
            exponent--;
        }

        // ln(m) = 2 atanh(z) = 2 (z + z^3/3 + z^5/5 + ...), z = (m - 1) / (m + 1), |z| <= 1/3
        var z = (value - 1) / (value + 1);
        var z2 = z * z;
        var power = z;
        var sum = 0m;
        for (var k = 1; Math.Abs(power) > Epsilon; k += 2)
        {
            sum += power / k;
            power *= z2;
        }

        return (2 * sum) + (exponent * Ln2);
    }

    /// <summary>Computes <c>e^x</c> for <c>x &lt;= 60</c>.</summary>
    /// <param name="x">The exponent.</param>
    /// <returns>The exponential.</returns>
    public static decimal Exp(decimal x)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(x, 60m);
        if (x < -60m)
            return 0;

        var halvings = 0;
        while (Math.Abs(x) > 0.125m)
        {
            x /= 2;
            halvings++;
        }

        var term = 1m;
        var sum = 1m;
        for (var k = 1; Math.Abs(term) > Epsilon; k++)
        {
            term = term * x / k;
            sum += term;
        }

        for (var i = 0; i < halvings; i++)
        {
            sum *= sum;
        }

        return sum;
    }

    /// <summary>Computes <c>value^exponent</c> for a non-negative value.</summary>
    /// <param name="value">The base.</param>
    /// <param name="exponent">The exponent.</param>
    /// <returns>The power (0 for a zero base).</returns>
    public static decimal Pow(decimal value, decimal exponent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value == 0 ? 0 : Exp(exponent * Ln(value));
    }
}
