namespace Meziantou.Framework.Charts;

/// <summary>
/// Rounds numbers to a number of decimals, away from zero at the midpoints, with operations IEEE 754 defines exactly
/// (a product, a rounding to an integer and a quotient), so the result is the same on every runtime.
/// <see cref="Math.Round(double, int, MidpointRounding)"/> rounds a value such as 369.825 differently on .NET 10 and .NET 11.
/// </summary>
internal static class DecimalRounding
{
    private static readonly double[] PowersOfTen =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    public static double Round(double value, int decimals)
    {
        if (!double.IsFinite(value))
            return value;

        var scale = PowersOfTen[Math.Clamp(decimals, 0, PowersOfTen.Length - 1)];
        var scaled = value * scale;

        // From 2^52, a double has no fractional part
        if (Math.Abs(scaled) >= 4503599627370496)
            return value;

        return Math.Round(scaled, MidpointRounding.AwayFromZero) / scale;
    }
}
