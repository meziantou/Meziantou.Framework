using System.Globalization;
using System.Numerics;
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

        // The integers that are exactly representable are formatted as integers (no shortest digits search)
        if (value is > -9007199254740992 and < 9007199254740992 && value == Math.Truncate(value))
            return ((long)value).ToString(CultureInfo.InvariantCulture);

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

    /// <summary>Number::toString(x, radix) (https://tc39.es/ecma262/#sec-numeric-types-number-tostring).</summary>
    public static string ToString(double value, int radix)
    {
        if (radix == 10 || double.IsNaN(value) || double.IsInfinity(value) || value == 0)
            return ToString(value);

        return ToRadixString(value, radix);
    }

    /// <summary>
    /// Formats a finite non-zero number in a radix other than 10: the digits of the integer part, then the shortest
    /// fraction digits that identify the number (a port of DoubleToRadixCString of V8).
    /// </summary>
    private static string ToRadixString(double value, int radix)
    {
        const string Chars = "0123456789abcdefghijklmnopqrstuvwxyz";

        // The integer part has at most 1024 digits (radix 2) and the fraction part at most 1074 + 52 digits
        const int BufferSize = 2200;
        Span<char> buffer = stackalloc char[BufferSize];
        var integerCursor = BufferSize / 2;
        var fractionCursor = integerCursor;

        var negative = value < 0;
        if (negative)
        {
            value = -value;
        }

        var integer = Math.Floor(value);
        var fraction = value - integer;

        // Only compute the fraction digits up to the precision of the number
        var delta = 0.5 * (Math.BitIncrement(value) - value);
        delta = Math.Max(double.Epsilon, delta);
        if (fraction >= delta)
        {
            buffer[fractionCursor++] = '.';
            do
            {
                fraction *= radix;
                delta *= radix;
                var digit = (int)fraction;
                buffer[fractionCursor++] = Chars[digit];
                fraction -= digit;

                // Round to even
                if (fraction > 0.5 || (fraction == 0.5 && (digit & 1) != 0))
                {
                    if (fraction + delta > 1)
                    {
                        // Propagate the carry to the digits already written
                        while (true)
                        {
                            fractionCursor--;
                            if (fractionCursor == BufferSize / 2)
                            {
                                integer += 1;
                                break;
                            }

                            var c = buffer[fractionCursor];
                            var previousDigit = c > '9' ? c - 'a' + 10 : c - '0';
                            if (previousDigit + 1 < radix)
                            {
                                buffer[fractionCursor++] = Chars[previousDigit + 1];
                                break;
                            }
                        }

                        break;
                    }
                }
            }
            while (fraction >= delta);
        }

        // The digits of the integer part; the digits that are not represented by the number are zeros
        while (GetBinaryExponent(integer / radix) > 0)
        {
            integer /= radix;
            buffer[--integerCursor] = '0';
        }

        do
        {
            var remainder = integer % radix;
            buffer[--integerCursor] = Chars[(int)remainder];
            integer = (integer - remainder) / radix;
        }
        while (integer > 0);

        if (negative)
        {
            buffer[--integerCursor] = '-';
        }

        return buffer[integerCursor..fractionCursor].ToString();
    }

    /// <summary>The exponent e of a positive number m \u00D7 2^e whose significand m is a 53-bit integer (-1074 for the subnormal numbers).</summary>
    private static int GetBinaryExponent(double value)
    {
        var biasedExponent = (int)((BitConverter.DoubleToUInt64Bits(value) >> 52) & 0x7FF);
        return biasedExponent == 0 ? -1074 : biasedExponent - 1075;
    }

    /// <summary>Splits a finite number into an integer significand and a binary exponent: |value| = significand \u00D7 2^exponent.</summary>
    internal static void Decompose(double value, out ulong significand, out int exponent)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var biasedExponent = (int)((bits >> 52) & 0x7FF);
        significand = bits & 0xF_FFFF_FFFF_FFFF;
        if (biasedExponent == 0)
        {
            exponent = -1074;
        }
        else
        {
            significand |= 1UL << 52;
            exponent = biasedExponent - 1075;
        }
    }

    /// <summary>
    /// Rounds value \u00D7 10^scale to an integer, exactly: the integer closest to it, the larger one when two are as close
    /// (the rounding of toFixed, toExponential and toPrecision).
    /// </summary>
    private static BigInteger RoundScaled(double value, int scale)
    {
        Decompose(value, out var significand, out var binaryExponent);
        var numerator = new BigInteger(significand);
        var denominator = BigInteger.One;
        if (binaryExponent >= 0)
        {
            numerator <<= binaryExponent;
        }
        else
        {
            denominator <<= -binaryExponent;
        }

        if (scale >= 0)
        {
            numerator *= BigInteger.Pow(10, scale);
        }
        else
        {
            denominator *= BigInteger.Pow(10, -scale);
        }

        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder * 2 >= denominator)
        {
            quotient++;
        }

        return quotient;
    }

    /// <summary>Compares a positive finite number with 10^exponent, exactly.</summary>
    private static int CompareWithPowerOfTen(double value, int exponent)
    {
        Decompose(value, out var significand, out var binaryExponent);
        var left = new BigInteger(significand);
        var right = BigInteger.One;
        if (binaryExponent >= 0)
        {
            left <<= binaryExponent;
        }
        else
        {
            right <<= -binaryExponent;
        }

        if (exponent >= 0)
        {
            right *= BigInteger.Pow(10, exponent);
        }
        else
        {
            left *= BigInteger.Pow(10, -exponent);
        }

        return left.CompareTo(right);
    }

    /// <summary>
    /// Gets the integer n of <paramref name="precision"/> digits and the exponent e for which n \u00D7 10^(e \u2212 precision + 1)
    /// is the closest to a positive finite number (the larger n when two are as close).
    /// </summary>
    internal static string GetFixedPrecisionDigits(double value, int precision, out int exponent)
    {
        // e is the exponent of the value (10^e <= value < 10^(e + 1)): Math.Log10 can be off by one close to a power of ten,
        // and an exponent too large can give a value of n in range that is not the closest (e.g. 9.999999999999999e-22)
        var e = (int)Math.Floor(Math.Log10(value));
        if (CompareWithPowerOfTen(value, e) < 0)
        {
            e--;
        }
        else if (CompareWithPowerOfTen(value, e + 1) >= 0)
        {
            e++;
        }

        var lower = BigInteger.Pow(10, precision - 1);
        var upper = lower * 10;
        while (true)
        {
            var n = RoundScaled(value, precision - 1 - e);
            if (n >= upper)
            {
                e++;
            }
            else if (n < lower)
            {
                e--;
            }
            else
            {
                exponent = e;
                return n.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>The result of Number.prototype.toFixed(fractionDigits) (fractionDigits is between 0 and 100).</summary>
    public static string ToFixed(double value, int fractionDigits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return ToString(value);

        var sign = "";
        if (value < 0)
        {
            sign = "-";
            value = -value;
        }

        if (value >= 1e21)
            return sign + ToString(value);

        string digits;
        if (value == 0)
        {
            digits = "0";
        }
        else if (value < 9007199254740992 && value == Math.Floor(value))
        {
            digits = ((long)value).ToString(CultureInfo.InvariantCulture) + new string('0', fractionDigits);
        }
        else
        {
            var n = RoundScaled(value, fractionDigits);
            digits = n.ToString(CultureInfo.InvariantCulture);
        }

        if (fractionDigits == 0)
            return sign + digits;

        var k = digits.Length;
        if (k <= fractionDigits)
        {
            digits = new string('0', fractionDigits + 1 - k) + digits;
            k = fractionDigits + 1;
        }

        return string.Concat(sign, digits.AsSpan(0, k - fractionDigits), ".", digits.AsSpan(k - fractionDigits));
    }

    /// <summary>The result of Number.prototype.toExponential(fractionDigits); a null fractionDigits gives as many digits as necessary.</summary>
    public static string ToExponential(double value, int? fractionDigits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return ToString(value);

        var builder = new StringBuilder();
        if (value < 0)
        {
            builder.Append('-');
            value = -value;
        }

        string digits;
        int exponent;
        if (value == 0)
        {
            digits = new string('0', (fractionDigits ?? 0) + 1);
            exponent = 0;
        }
        else if (fractionDigits is { } f)
        {
            digits = GetFixedPrecisionDigits(value, f + 1, out exponent);
        }
        else
        {
            GetShortestDigits(value, out digits, out var n);
            exponent = n - 1;
        }

        builder.Append(digits[0]);
        if (digits.Length > 1)
        {
            builder.Append('.').Append(digits, 1, digits.Length - 1);
        }

        AppendExponent(builder, exponent);
        return builder.ToString();
    }

    /// <summary>The result of Number.prototype.toPrecision(precision) (precision is between 1 and 100).</summary>
    public static string ToPrecision(double value, int precision)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return ToString(value);

        var sign = "";
        if (value < 0)
        {
            sign = "-";
            value = -value;
        }

        string digits;
        int e;
        if (value == 0)
        {
            digits = new string('0', precision);
            e = 0;
        }
        else
        {
            digits = GetFixedPrecisionDigits(value, precision, out e);
            if (e < -6 || e >= precision)
            {
                var builder = new StringBuilder(precision + 8);
                builder.Append(sign).Append(digits[0]);
                if (precision != 1)
                {
                    builder.Append('.').Append(digits, 1, digits.Length - 1);
                }

                AppendExponent(builder, e);
                return builder.ToString();
            }
        }

        if (e == precision - 1)
            return sign + digits;

        if (e >= 0)
            return string.Concat(sign, digits.AsSpan(0, e + 1), ".", digits.AsSpan(e + 1));

        return string.Concat(sign, "0.", new string('0', -(e + 1)), digits);
    }

    private static void AppendExponent(StringBuilder builder, int exponent)
    {
        builder.Append('e').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString(CultureInfo.InvariantCulture));
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
