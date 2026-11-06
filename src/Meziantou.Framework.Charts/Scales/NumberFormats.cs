using System.Globalization;

namespace Meziantou.Framework.Charts;

/// <summary>Number formats for the tick labels of the scales (<see cref="ContinuousScale.TickFormat"/>). They use the invariant culture.</summary>
public static class NumberFormats
{
    private static readonly string[] SiPrefixes = ["", "k", "M", "G", "T", "P", "E"];

    /// <summary>Formats a number with at most 6 significant digits and thousands separators, such as <c>1,234.5</c>.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Default(double value)
    {
        if (!double.IsFinite(value))
            return value.ToString(CultureInfo.InvariantCulture);

        var rounded = RoundSignificant(value, 6);
        if (rounded == 0)
            return "0";

        var magnitude = Math.Abs(rounded);
        if (magnitude is >= 1e15 or < 1e-6)
            return rounded.ToString("0.#####E+0", CultureInfo.InvariantCulture);

        return rounded.ToString("#,##0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a number with an SI prefix (<c>k</c>, <c>M</c>, <c>G</c>, <c>T</c>...) for the thousands, such as <c>1.5k</c> or <c>20M</c>.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Si(double value)
    {
        if (!double.IsFinite(value))
            return value.ToString(CultureInfo.InvariantCulture);

        var magnitude = Math.Abs(value);
        var group = 0;
        while (magnitude >= 1000 && group < SiPrefixes.Length - 1)
        {
            magnitude /= 1000;
            group++;
        }

        // 999,950 is 1M, not 1,000k
        var rounded = RoundSignificant(magnitude, 4);
        if (rounded >= 1000 && group < SiPrefixes.Length - 1)
        {
            rounded /= 1000;
            group++;
        }

        var number = rounded.ToString("0.###", CultureInfo.InvariantCulture);
        var text = number + SiPrefixes[group];
        return value < 0 && number is not "0" ? "-" + text : text;
    }

    /// <summary>Creates a format with a fixed number of decimals and thousands separators.</summary>
    /// <param name="decimals">The number of decimals.</param>
    /// <returns>The format.</returns>
    public static Func<double, string> Fixed(int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 15);
        var format = decimals is 0 ? "#,##0" : "#,##0." + new string('0', decimals);
        return value => Normalize(DecimalRounding.Round(value, decimals)).ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Creates a format that shows a ratio as a percentage: 0.25 is <c>25%</c>.</summary>
    /// <param name="decimals">The number of decimals of the percentage.</param>
    /// <returns>The format.</returns>
    public static Func<double, string> Percent(int decimals = 0)
    {
        var format = Fixed(decimals);
        return value => format(value * 100) + "%";
    }

    /// <summary>Formats a tick with as many decimals as the step between the ticks needs, so the labels line up.</summary>
    internal static string ForStep(double value, double step)
    {
        if (!(step > 0) || !double.IsFinite(step))
            return Default(value);

        var decimals = (int)Math.Max(0, Math.Ceiling(-Math.Log10(step) - 1e-9));
        if (decimals > 15 || Math.Abs(value) >= 1e15)
            return Default(value);

        var format = decimals is 0 ? "#,##0" : "#,##0." + new string('0', decimals);
        return Normalize(DecimalRounding.Round(value, decimals)).ToString(format, CultureInfo.InvariantCulture);
    }

    internal static double RoundSignificant(double value, int digits)
    {
        if (value == 0 || !double.IsFinite(value))
            return value;

        var scale = (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1;
        var decimals = digits - scale;
        if (decimals is >= 0 and <= 15)
            return DecimalRounding.Round(value, decimals);

        var factor = Math.Pow(10, scale - digits);
        return Math.Round(value / factor, MidpointRounding.AwayFromZero) * factor;
    }

    private static double Normalize(double value) => value == 0 ? 0 : value;
}
