using System.Globalization;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The conversions of numbers to strings (https://tc39.es/ecma262/#sec-numeric-types-number-tostring).</summary>
internal static class NumberFormatting
{
    private static readonly string[] SmallIntegers = CreateSmallIntegers();

    /// <summary>Number::toString(x, 10)</summary>
    public static string ToString(double value)
    {
        if (double.IsNaN(value))
            return "NaN";

        if (value == 0)
            return "0";

        if (value >= 0 && value < SmallIntegers.Length && value == Math.Floor(value))
            return SmallIntegers[(int)value];

        if (double.IsInfinity(value))
            return value > 0 ? "Infinity" : "-Infinity";

        if (value < 0)
            return "-" + ToString(-value);

        GetShortestDigits(value, out var digits, out var n);
        var k = digits.Length;
        var builder = new StringBuilder(k + 8);
        if (k <= n && n <= 21)
        {
            builder.Append(digits).Append('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            builder.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
        }
        else if (-6 < n && n <= 0)
        {
            builder.Append("0.").Append('0', -n).Append(digits);
        }
        else
        {
            var exponent = n - 1;
            builder.Append(digits[0]);
            if (k > 1)
            {
                builder.Append('.').Append(digits, 1, k - 1);
            }

            builder.Append('e').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Gets the shortest decimal digits that round-trip a positive finite number: value = digits x 10^(n \u2212 digits.Length),
    /// with no leading nor trailing zero in the digits.
    /// </summary>
    public static void GetShortestDigits(double value, out string digits, out int n)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var exponentIndex = text.IndexOfAny(['E', 'e']);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        var pointIndex = text.IndexOf('.', StringComparison.Ordinal);
        var integerDigits = pointIndex >= 0 ? pointIndex : text.Length;
        var allDigits = pointIndex >= 0 ? string.Concat(text.AsSpan(0, pointIndex), text.AsSpan(pointIndex + 1)) : text;

        // value = 0.allDigits x 10^(integerDigits + exponent)
        var leadingZeros = 0;
        while (leadingZeros < allDigits.Length - 1 && allDigits[leadingZeros] == '0')
        {
            leadingZeros++;
        }

        allDigits = allDigits[leadingZeros..].TrimEnd('0');
        if (allDigits.Length == 0)
        {
            allDigits = "0";
        }

        digits = allDigits;
        n = integerDigits + exponent - leadingZeros;
    }

    private static string[] CreateSmallIntegers()
    {
        var result = new string[1024];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }
}
