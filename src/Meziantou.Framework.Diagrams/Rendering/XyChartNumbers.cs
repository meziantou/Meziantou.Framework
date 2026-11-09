using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The numbers of the XY charts as Mermaid computes and writes them: the ticks of d3 and the strings of JavaScript.</summary>
internal static class XyChartNumbers
{
    private static readonly double E10 = Math.Sqrt(50);
    private static readonly double E5 = Math.Sqrt(10);
    private static readonly double E2 = Math.Sqrt(2);

    /// <summary>Returns about <paramref name="count"/> round values between two values, as <c>d3.ticks</c> does.</summary>
    public static List<double> Ticks(double start, double stop, int count)
    {
        if (!double.IsFinite(start) || !double.IsFinite(stop))
            return [];

        if (start == stop)
            return [start];

        var reverse = stop < start;
        var (i1, i2, increment) = reverse ? TickSpec(stop, start, count) : TickSpec(start, stop, count);
        if (!(i2 >= i1) || !double.IsFinite(increment) || increment == 0)
            return [];

        // The step never makes more than about 'count' ticks, but the arithmetic of huge numbers can
        var n = i2 - i1 + 1;
        if (n > 1000)
            return [];

        var ticks = new List<double>((int)n);
        for (var i = 0; i < n; i++)
        {
            var index = reverse ? i2 - i : i1 + i;
            ticks.Add(increment < 0 ? index / -increment : index * increment);
        }

        return ticks;
    }

    private static (double I1, double I2, double Increment) TickSpec(double start, double stop, double count)
    {
        while (true)
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
                i1 = Round(start * increment);
                i2 = Round(stop * increment);
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
                i1 = Round(start / increment);
                i2 = Round(stop / increment);
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
            {
                count *= 2;
                continue;
            }

            return (i1, i2, increment);
        }
    }

    // Math.round of JavaScript rounds the halves up
    private static double Round(double value) => Math.Round(value, MidpointRounding.ToPositiveInfinity);

    /// <summary>Converts a number to a string as JavaScript does: the shortest digits that read back to the number, and an exponent below 1e-6 and from 1e21.</summary>
    public static string ToJavaScriptString(double value)
    {
        if (double.IsNaN(value))
            return "NaN";

        if (double.IsPositiveInfinity(value))
            return "Infinity";

        if (double.IsNegativeInfinity(value))
            return "-Infinity";

        if (value == 0)
            return "0";

        var (digits, exponent) = GetDigits(Math.Abs(value));
        var builder = new StringBuilder();
        if (value < 0)
        {
            builder.Append('-');
        }

        // The value is 0.digits x 10^exponent
        var k = digits.Length;
        if (k <= exponent && exponent <= 21)
        {
            builder.Append(digits).Append('0', exponent - k);
        }
        else if (exponent is > 0 and <= 21)
        {
            builder.Append(digits, 0, exponent).Append('.').Append(digits, exponent, k - exponent);
        }
        else if (exponent is > -6 and <= 0)
        {
            builder.Append("0.").Append('0', -exponent).Append(digits);
        }
        else
        {
            builder.Append(digits[0]);
            if (k > 1)
            {
                builder.Append('.').Append(digits, 1, k - 1);
            }

            var e = exponent - 1;
            builder.Append('e').Append(e < 0 ? '-' : '+').Append(Math.Abs(e).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Returns the shortest significant digits of a positive finite number, and the position of its decimal point.</summary>
    internal static (string Digits, int Exponent) GetDigits(double value)
    {
        // "E16" is not the shortest; "R" is, as a decimal or as an exponent
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var e = text.IndexOf('E', StringComparison.Ordinal);
        if (e >= 0)
        {
            exponent = int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..e];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integerDigits = dot < 0 ? text.Length : dot;
        var digits = dot < 0 ? text : string.Concat(text.AsSpan(0, dot), text.AsSpan(dot + 1));
        var position = integerDigits + exponent;

        // Leading zeros of a number below 1, and trailing zeros of an integer, are not significant digits
        var leading = 0;
        while (leading < digits.Length - 1 && digits[leading] == '0')
        {
            leading++;
        }

        digits = digits[leading..];
        position -= leading;
        digits = digits.TrimEnd('0');
        return (digits.Length is 0 ? "0" : digits, position);
    }
}
