using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The value properties and the function properties of the global object (https://tc39.es/ecma262/#sec-global-object),
/// and escape/unescape (https://tc39.es/ecma262/#sec-additional-properties-of-the-global-object).
/// </summary>
internal static class GlobalFunctionsBuiltins
{
    private const string UriReservedPlusHash = ";/?:@&=+$,#";

    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;

        // The value properties
        intrinsics.AddGlobal("globalThis", realm.GlobalThis, PropertyFlags.BuiltIn);
        intrinsics.AddGlobal("Infinity", double.PositiveInfinity, PropertyFlags.None);
        intrinsics.AddGlobal("NaN", double.NaN, PropertyFlags.None);
        intrinsics.AddGlobal("undefined", JsValue.Undefined, PropertyFlags.None);

        // The function properties (eval is added by the compiler)
        intrinsics.AddGlobal("isFinite", realm.CreateBuiltinFunction("isFinite", 1, IsFinite));
        intrinsics.AddGlobal("isNaN", realm.CreateBuiltinFunction("isNaN", 1, IsNaN));
        intrinsics.AddGlobal("parseFloat", intrinsics.ParseFloat);
        intrinsics.AddGlobal("parseInt", intrinsics.ParseInt);
        intrinsics.AddGlobal("decodeURI", realm.CreateBuiltinFunction("decodeURI", 1, DecodeUri));
        intrinsics.AddGlobal("decodeURIComponent", realm.CreateBuiltinFunction("decodeURIComponent", 1, DecodeUriComponent));
        intrinsics.AddGlobal("encodeURI", realm.CreateBuiltinFunction("encodeURI", 1, EncodeUri));
        intrinsics.AddGlobal("encodeURIComponent", realm.CreateBuiltinFunction("encodeURIComponent", 1, EncodeUriComponent));

        // Annex B
        intrinsics.AddGlobal("escape", realm.CreateBuiltinFunction("escape", 1, Escape));
        intrinsics.AddGlobal("unescape", realm.CreateBuiltinFunction("unescape", 1, Unescape));
    }

    private static JsValue IsFinite(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return double.IsFinite(JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default));
    }

    private static JsValue IsNaN(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return double.IsNaN(JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default));
    }

    /// <summary>parseInt(string, radix)</summary>
    internal static JsValue ParseInt(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var s = TrimLeadingWhiteSpace(input.AsSpan());
        var negative = false;
        if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
        {
            negative = s[0] == '-';
            s = s[1..];
        }

        var radix = JsOperations.ToInt32(arguments.Length > 1 ? arguments[1] : default);
        var stripPrefix = true;
        if (radix != 0)
        {
            if (radix is < 2 or > 36)
                return double.NaN;

            if (radix != 16)
            {
                stripPrefix = false;
            }
        }
        else
        {
            radix = 10;
        }

        if (stripPrefix && s.Length >= 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
        {
            s = s[2..];
            radix = 16;
        }

        var end = 0;
        while (end < s.Length && IsDigit(s[end], radix))
        {
            end++;
        }

        if (end == 0)
            return double.NaN;

        var value = ParseIntegerDigits(s[..end], radix);
        return negative ? -value : value;
    }

    private static bool IsDigit(char c, int radix)
    {
        var digit = NumberParsing.GetDigitValue(c);
        return digit >= 0 && digit < radix;
    }

    /// <summary>The number closest to the integer written with the digits (round half to even).</summary>
    internal static double ParseIntegerDigits(ReadOnlySpan<char> digits, int radix)
    {
        if (radix == 10)
            return double.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

        // Exact while the value is less than 2^53
        ulong small = 0;
        var i = 0;
        for (; i < digits.Length; i++)
        {
            if (small >= (1UL << 53) / (ulong)radix)
                break;

            small = (small * (ulong)radix) + (ulong)NumberParsing.GetDigitValue(digits[i]);
        }

        if (i == digits.Length)
            return small;

        var big = new BigInteger(small);
        for (; i < digits.Length; i++)
        {
            big = (big * radix) + NumberParsing.GetDigitValue(digits[i]);
        }

        return BigIntOperations.ToNumber(big);
    }

    /// <summary>parseFloat(string)</summary>
    internal static JsValue ParseFloat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        return ParseFloat(TrimLeadingWhiteSpace(input.AsSpan()));
    }

    /// <summary>The value of the longest prefix of the string that satisfies StrDecimalLiteral; NaN when there is none.</summary>
    internal static double ParseFloat(ReadOnlySpan<char> s)
    {
        var i = 0;
        var negative = false;
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
        {
            negative = s[0] == '-';
            i++;
        }

        if (s[i..].StartsWith("Infinity", StringComparison.Ordinal))
            return negative ? double.NegativeInfinity : double.PositiveInfinity;

        var integerStart = i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
        }

        var integerDigits = i - integerStart;
        if (i < s.Length && s[i] == '.')
        {
            var j = i + 1;
            while (j < s.Length && char.IsAsciiDigit(s[j]))
            {
                j++;
            }

            if (integerDigits > 0 || j > i + 1)
            {
                i = j;
            }
        }

        if (i == integerStart)
            return double.NaN;

        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            var j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-'))
            {
                j++;
            }

            var exponentStart = j;
            while (j < s.Length && char.IsAsciiDigit(s[j]))
            {
                j++;
            }

            if (j > exponentStart)
            {
                i = j;
            }
        }

        return double.Parse(s[..i], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
    }

    private static ReadOnlySpan<char> TrimLeadingWhiteSpace(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && NumberParsing.IsWhiteSpaceOrLineTerminator(value[start]))
        {
            start++;
        }

        return value[start..];
    }

    // ------------------------------------------------------------------------------------------------------------------
    // URI handling functions (https://tc39.es/ecma262/#sec-uri-handling-functions)
    // ------------------------------------------------------------------------------------------------------------------

    private static JsValue DecodeUri(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var uriString = JsOperations.ToJsString(arguments.Length > 0 ? arguments[0] : default);
        return JsValue.FromJsString(Decode(uriString, UriReservedPlusHash));
    }

    private static JsValue DecodeUriComponent(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var componentString = JsOperations.ToJsString(arguments.Length > 0 ? arguments[0] : default);
        return JsValue.FromJsString(Decode(componentString, ""));
    }

    private static JsValue EncodeUri(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var uriString = JsOperations.ToJsString(arguments.Length > 0 ? arguments[0] : default);
        return JsValue.FromJsString(Encode(uriString, UriReservedPlusHash));
    }

    private static JsValue EncodeUriComponent(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var componentString = JsOperations.ToJsString(arguments.Length > 0 ? arguments[0] : default);
        return JsValue.FromJsString(Encode(componentString, ""));
    }

    /// <summary>uriUnreserved: uriAlpha, DecimalDigit and uriMark.</summary>
    private static bool IsUriUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '!' or '~' or '*' or '\'' or '(' or ')';

    /// <summary>Encode(string, extraUnescaped)</summary>
    private static JsString Encode(JsString value, string extraUnescaped)
    {
        var s = value.ToString();
        var k = 0;
        while (k < s.Length && (IsUriUnreserved(s[k]) || extraUnescaped.Contains(s[k], StringComparison.Ordinal)))
        {
            k++;
        }

        if (k == s.Length)
            return value;

        var builder = new StringBuilder(s.Length + 16);
        builder.Append(s, 0, k);
        Span<byte> octets = stackalloc byte[4];
        for (; k < s.Length; k++)
        {
            var c = s[k];
            if (IsUriUnreserved(c) || extraUnescaped.Contains(c, StringComparison.Ordinal))
            {
                builder.Append(c);
                continue;
            }

            int codePoint;
            if (char.IsHighSurrogate(c))
            {
                if (k + 1 >= s.Length || !char.IsLowSurrogate(s[k + 1]))
                {
                    ThrowUriError();
                }

                codePoint = char.ConvertToUtf32(c, s[k + 1]);
                k++;
            }
            else if (char.IsLowSurrogate(c))
            {
                codePoint = ThrowUriError<int>();
            }
            else
            {
                codePoint = c;
            }

            var length = new Rune(codePoint).EncodeToUtf8(octets);
            for (var i = 0; i < length; i++)
            {
                builder.Append('%').Append(HexUpper(octets[i] >> 4)).Append(HexUpper(octets[i] & 0xF));
            }
        }

        return CreateString(builder);
    }

    /// <summary>Decode(string, preserveEscapeSet)</summary>
    private static JsString Decode(JsString value, string preserveEscapeSet)
    {
        var s = value.ToString();
        var k = s.IndexOf('%', StringComparison.Ordinal);
        if (k < 0)
            return value;

        var builder = new StringBuilder(s.Length);
        builder.Append(s, 0, k);
        for (; k < s.Length; k++)
        {
            var c = s[k];
            if (c != '%')
            {
                builder.Append(c);
                continue;
            }

            var start = k;
            if (k + 3 > s.Length)
            {
                ThrowUriError();
            }

            var b = ParseHexOctet(s, k + 1);
            if (b < 0)
            {
                ThrowUriError();
            }

            k += 2;
            if ((b & 0x80) == 0)
            {
                var asciiChar = (char)b;
                if (preserveEscapeSet.Contains(asciiChar, StringComparison.Ordinal))
                {
                    builder.Append(s, start, 3);
                }
                else
                {
                    builder.Append(asciiChar);
                }

                continue;
            }

            var n = BitOperations.LeadingZeroCount((uint)~(b << 24));
            if (n is 1 or > 4)
            {
                ThrowUriError();
            }

            var codePoint = b & (0xFF >> (n + 1));
            for (var j = 1; j < n; j++)
            {
                k++;
                if (k + 3 > s.Length || s[k] != '%')
                {
                    ThrowUriError();
                }

                var continuation = ParseHexOctet(s, k + 1);
                if (continuation < 0 || (continuation & 0xC0) != 0x80)
                {
                    ThrowUriError();
                }

                codePoint = (codePoint << 6) | (continuation & 0x3F);
                k += 2;
            }

            var minimum = n switch
            {
                2 => 0x80,
                3 => 0x800,
                _ => 0x10000,
            };

            if (codePoint < minimum || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            {
                ThrowUriError();
            }

            if (codePoint >= 0x10000)
            {
                builder.Append(char.ConvertFromUtf32(codePoint));
            }
            else
            {
                builder.Append((char)codePoint);
            }
        }

        return CreateString(builder);
    }

    /// <summary>ParseHexOctet(string, position): the value of the two hexadecimal digits, or -1.</summary>
    private static int ParseHexOctet(string s, int position)
    {
        var high = GetHexValue(s[position]);
        var low = GetHexValue(s[position + 1]);
        if (high < 0 || low < 0)
            return -1;

        return (high << 4) | low;
    }

    private static int GetHexValue(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
    }

    private static char HexUpper(int value) => "0123456789ABCDEF"[value];

    [DoesNotReturn]
    private static void ThrowUriError() => ThrowHelper.ThrowError(ErrorType.URIError, "URI malformed");

    [DoesNotReturn]
    private static T ThrowUriError<T>() => throw ThrowHelper.CreateException(ErrorType.URIError, "URI malformed");

    private static JsString CreateString(StringBuilder builder)
    {
        if (builder.Length > JsString.MaxLength)
        {
            ThrowHelper.ThrowRangeError("Invalid string length");
        }

        return JsString.Create(builder.ToString());
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Annex B
    // ------------------------------------------------------------------------------------------------------------------

    private static bool IsEscapeUnescaped(char c) => char.IsAsciiLetterOrDigit(c) || c is '@' or '*' or '_' or '+' or '-' or '.' or '/';

    /// <summary>escape(string)</summary>
    private static JsValue Escape(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var value = JsOperations.ToJsString(arguments.Length > 0 ? arguments[0] : default);
        var s = value.ToString();
        var k = 0;
        while (k < s.Length && IsEscapeUnescaped(s[k]))
        {
            k++;
        }

        if (k == s.Length)
            return JsValue.FromJsString(value);

        var builder = new StringBuilder(s.Length + 16);
        builder.Append(s, 0, k);
        for (; k < s.Length; k++)
        {
            var c = s[k];
            if (IsEscapeUnescaped(c))
            {
                builder.Append(c);
            }
            else if (c < 256)
            {
                builder.Append('%').Append(HexUpper(c >> 4)).Append(HexUpper(c & 0xF));
            }
            else
            {
                builder.Append("%u").Append(HexUpper(c >> 12)).Append(HexUpper((c >> 8) & 0xF)).Append(HexUpper((c >> 4) & 0xF)).Append(HexUpper(c & 0xF));
            }
        }

        return JsValue.FromJsString(CreateString(builder));
    }

    /// <summary>unescape(string)</summary>
    private static JsValue Unescape(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var value = JsOperations.ToJsString(arguments.Length > 0 ? arguments[0] : default);
        var s = value.ToString();
        var k = s.IndexOf('%', StringComparison.Ordinal);
        if (k < 0)
            return JsValue.FromJsString(value);

        var builder = new StringBuilder(s.Length);
        builder.Append(s, 0, k);
        for (; k < s.Length; k++)
        {
            var c = s[k];
            if (c == '%')
            {
                if (k + 6 <= s.Length && s[k + 1] == 'u')
                {
                    var high = ParseHexOctet(s, k + 2);
                    var low = ParseHexOctet(s, k + 4);
                    if (high >= 0 && low >= 0)
                    {
                        c = (char)((high << 8) | low);
                        k += 5;
                    }
                }
                else if (k + 3 <= s.Length)
                {
                    var octet = ParseHexOctet(s, k + 1);
                    if (octet >= 0)
                    {
                        c = (char)octet;
                        k += 2;
                    }
                }
            }

            builder.Append(c);
        }

        return JsValue.FromJsString(CreateString(builder));
    }
}
