using System.Globalization;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The conversions of strings to numbers (https://tc39.es/ecma262/#sec-stringtonumber).</summary>
internal static class NumberParsing
{
    /// <summary>StringToNumber(str): the StringNumericLiteral grammar, NaN when the string does not match it.</summary>
    public static double StringToNumber(string value)
    {
        var span = TrimWhiteSpace(value.AsSpan());
        if (span.Length == 0)
            return 0;

        if (span.Length > 2 && span[0] == '0')
        {
            var radix = span[1] switch
            {
                'x' or 'X' => 16,
                'o' or 'O' => 8,
                'b' or 'B' => 2,
                _ => 0,
            };

            if (radix != 0)
                return ParseNonDecimal(span[2..], radix);
        }

        return ParseDecimal(span);
    }

    /// <summary>Parses the digits of a non-decimal integer literal (no sign, no separator); NaN when a character is not a digit.</summary>
    public static double ParseNonDecimal(ReadOnlySpan<char> digits, int radix)
    {
        if (digits.Length == 0)
            return double.NaN;

        double result = 0;
        foreach (var c in digits)
        {
            var digit = GetDigitValue(c);
            if (digit < 0 || digit >= radix)
                return double.NaN;

            result = (result * radix) + digit;
        }

        // Large values: compute exactly with BigInteger, then round once
        if (result >= 9007199254740992)
        {
            var big = System.Numerics.BigInteger.Zero;
            foreach (var c in digits)
            {
                big = (big * radix) + GetDigitValue(c);
            }

            return (double)big;
        }

        return result;
    }

    /// <summary>Parses StrDecimalLiteral with an optional sign (Infinity, digits, '.', exponent); NaN otherwise.</summary>
    public static double ParseDecimal(ReadOnlySpan<char> span)
    {
        var sign = 1.0;
        var body = span;
        if (body.Length > 0 && (body[0] == '+' || body[0] == '-'))
        {
            if (body[0] == '-')
            {
                sign = -1;
            }

            body = body[1..];
        }

        if (body.SequenceEqual("Infinity"))
            return sign * double.PositiveInfinity;

        if (!IsStrUnsignedDecimalLiteral(body))
            return double.NaN;

        return sign * double.Parse(body, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
    }

    private static bool IsStrUnsignedDecimalLiteral(ReadOnlySpan<char> body)
    {
        var i = 0;
        var integerDigits = 0;
        while (i < body.Length && char.IsAsciiDigit(body[i]))
        {
            i++;
            integerDigits++;
        }

        var fractionDigits = 0;
        if (i < body.Length && body[i] == '.')
        {
            i++;
            while (i < body.Length && char.IsAsciiDigit(body[i]))
            {
                i++;
                fractionDigits++;
            }
        }

        if (integerDigits + fractionDigits == 0)
            return false;

        if (i < body.Length && (body[i] == 'e' || body[i] == 'E'))
        {
            i++;
            if (i < body.Length && (body[i] == '+' || body[i] == '-'))
            {
                i++;
            }

            var exponentDigits = 0;
            while (i < body.Length && char.IsAsciiDigit(body[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
                return false;
        }

        return i == body.Length;
    }

    public static int GetDigitValue(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'z' => c - 'a' + 10,
            >= 'A' and <= 'Z' => c - 'A' + 10,
            _ => -1,
        };
    }

    /// <summary>Removes the leading and trailing StrWhiteSpaceChar (WhiteSpace and LineTerminator).</summary>
    public static ReadOnlySpan<char> TrimWhiteSpace(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && IsWhiteSpaceOrLineTerminator(value[start]))
        {
            start++;
        }

        var end = value.Length;
        while (end > start && IsWhiteSpaceOrLineTerminator(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    /// <summary>WhiteSpace or LineTerminator (https://tc39.es/ecma262/#sec-white-space): the Zs category, TAB, VT, FF, ZWNBSP, LF, CR, LS, PS.</summary>
    public static bool IsWhiteSpaceOrLineTerminator(char c)
    {
        return c switch
        {
            '\t' or '\v' or '\f' or ' ' or '\u00A0' or '\uFEFF' or '\n' or '\r' or '\u2028' or '\u2029' => true,
            < '\u1680' => false,
            _ => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator,
        };
    }
}
