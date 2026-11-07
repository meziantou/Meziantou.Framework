namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>Common microsyntaxes (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html).</summary>
internal static partial class Microsyntaxes
{
    private static bool IsAsciiWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-integers</summary>
    public static bool TryParseInteger(ReadOnlySpan<char> input, out long value)
    {
        value = 0;
        var position = 0;
        var sign = 1;
        while (position < input.Length && IsAsciiWhitespace(input[position]))
        {
            position++;
        }

        if (position >= input.Length)
            return false;

        if (input[position] == '-')
        {
            sign = -1;
            position++;
            if (position >= input.Length)
                return false;
        }
        else if (input[position] == '+')
        {
            position++;
            if (position >= input.Length)
                return false;
        }

        if (!char.IsAsciiDigit(input[position]))
            return false;

        long result = 0;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            // Values beyond the range of a 64-bit integer saturate; callers range-check the result
            if (result < long.MaxValue / 10)
            {
                result = (result * 10) + (input[position] - '0');
            }
            else
            {
                result = long.MaxValue;
            }

            position++;
        }

        value = sign * result;
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-non-negative-integers</summary>
    public static bool TryParseNonNegativeInteger(ReadOnlySpan<char> input, out long value)
    {
        if (!TryParseInteger(input, out value) || value < 0)
        {
            value = 0;
            return false;
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-floating-point-number-values</summary>
    public static bool TryParseFloat(ReadOnlySpan<char> input, out double value)
    {
        value = 0;
        var position = 0;
        while (position < input.Length && IsAsciiWhitespace(input[position]))
        {
            position++;
        }

        if (position >= input.Length)
            return false;

        var text = new System.Text.StringBuilder();
        if (input[position] == '-')
        {
            text.Append('-');
            position++;
            if (position >= input.Length)
                return false;
        }
        else if (input[position] == '+')
        {
            position++;
            if (position >= input.Length)
                return false;
        }

        if (input[position] == '.' && position + 1 < input.Length && char.IsAsciiDigit(input[position + 1]))
        {
            text.Append('0');
        }
        else
        {
            if (!char.IsAsciiDigit(input[position]))
                return false;

            while (position < input.Length && char.IsAsciiDigit(input[position]))
            {
                text.Append(input[position++]);
            }
        }

        // Fraction
        if (position < input.Length && input[position] == '.')
        {
            position++;
            if (position < input.Length && char.IsAsciiDigit(input[position]))
            {
                text.Append('.');
                while (position < input.Length && char.IsAsciiDigit(input[position]))
                {
                    text.Append(input[position++]);
                }
            }
        }

        // Exponent
        if (position < input.Length && input[position] is 'e' or 'E')
        {
            position++;
            var exponentSign = "";
            if (position < input.Length && input[position] is '-' or '+')
            {
                exponentSign = input[position] == '-' ? "-" : "+";
                position++;
            }

            if (position < input.Length && char.IsAsciiDigit(input[position]))
            {
                text.Append('e').Append(exponentSign);
                while (position < input.Length && char.IsAsciiDigit(input[position]))
                {
                    text.Append(input[position++]);
                }
            }
        }

        // Conversion: the closest double; values rounding to the special values 2^1024 and -2^1024 are errors
        var result = double.Parse(text.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        if (double.IsInfinity(result))
            return false;

        value = result == 0 ? 0 : result;
        return true;
    }

    /// <summary>
    /// The best representation of the number as a floating-point number
    /// (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#best-representation-of-the-number-as-a-floating-point-number),
    /// i.e. Number::toString (https://tc39.es/ecma262/#sec-numeric-types-number-tostring).
    /// </summary>
    public static string ToBestFloatingPointRepresentation(double value)
    {
        if (double.IsNaN(value))
            return "NaN";

        if (value == 0)
            return "0";

        if (double.IsInfinity(value))
            return value > 0 ? "Infinity" : "-Infinity";

        var sign = value < 0 ? "-" : "";
        var roundTrip = Math.Abs(value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        // Extract the significant digits (k digits) and the exponent n such that value = 0.digits * 10^n
        var exponentIndex = roundTrip.IndexOfAny(['E', 'e']);
        var mantissa = exponentIndex >= 0 ? roundTrip[..exponentIndex] : roundTrip;
        var exponent = exponentIndex >= 0 ? int.Parse(roundTrip.AsSpan(exponentIndex + 1), System.Globalization.CultureInfo.InvariantCulture) : 0;
        var pointIndex = mantissa.IndexOf('.', StringComparison.Ordinal);
        var integerPart = pointIndex >= 0 ? mantissa[..pointIndex] : mantissa;
        var fractionPart = pointIndex >= 0 ? mantissa[(pointIndex + 1)..] : "";
        var digits = (integerPart + fractionPart).TrimStart('0');
        var n = integerPart.TrimStart('0').Length + exponent;
        if (integerPart.TrimStart('0').Length == 0)
        {
            // 0.000ddd: the leading zeros of the fraction reduce the exponent
            var leadingZeros = fractionPart.Length - fractionPart.TrimStart('0').Length;
            n = -leadingZeros + exponent;
        }

        digits = digits.TrimEnd('0');
        var k = digits.Length;
        string result;
        if (k <= n && n <= 21)
        {
            result = digits + new string('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            result = digits[..n] + "." + digits[n..];
        }
        else if (-6 < n && n <= 0)
        {
            result = "0." + new string('0', -n) + digits;
        }
        else
        {
            var e = n - 1;
            var exponentText = (e >= 0 ? "+" : "-") + Math.Abs(e).ToString(System.Globalization.CultureInfo.InvariantCulture);
            result = k == 1 ? digits + "e" + exponentText : digits[..1] + "." + digits[1..] + "e" + exponentText;
        }

        return sign + result;
    }
}
