using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.WasmEngine.Text;

/// <summary>The numbers of the text format (https://webassembly.github.io/spec/core/text/values.html).</summary>
internal static class WatNumbers
{
    /// <summary>Parses an unsigned integer (<c>u32</c>, <c>u64</c>), or returns false.</summary>
    public static bool TryParseUnsigned(string text, ulong max, out ulong value)
    {
        value = 0;
        if (text.Length == 0 || text[0] is '+' or '-')
            return false;

        return TryParseMagnitude(text, out var magnitude) && magnitude <= max && TrySet(magnitude, out value);
    }

    private static bool TrySet(BigInteger magnitude, out ulong value)
    {
        value = (ulong)magnitude;
        return true;
    }

    /// <summary>Parses an integer of <paramref name="bits"/> bits: a signed value in [-2^(n-1), 2^(n-1)) or an unsigned value in [0, 2^n).</summary>
    public static bool TryParseInteger(string text, int bits, out long value)
    {
        value = 0;
        if (text.Length == 0)
            return false;

        var negative = text[0] == '-';
        var signed = text[0] is '+' or '-';
        if (!TryParseMagnitude(signed ? text[1..] : text, out var magnitude))
            return false;

        var limit = BigInteger.One << bits;
        if (negative)
        {
            if (magnitude > limit >> 1)
                return false;

            value = (long)(ulong)((limit - magnitude) & (limit - 1));
        }
        else
        {
            // An explicit '+' is a signed value
            if (magnitude >= (signed ? limit >> 1 : limit))
                return false;

            value = (long)(ulong)magnitude;
        }

        // Sign-extend the value of n bits
        if (bits < 64)
        {
            value = value << (64 - bits) >> (64 - bits);
        }

        return true;
    }

    private static bool TryParseMagnitude(string text, out BigInteger magnitude)
    {
        magnitude = BigInteger.Zero;
        var hex = text.StartsWith("0x", StringComparison.Ordinal);
        var digits = hex ? text[2..] : text;
        if (!CheckUnderscores(digits))
            return false;

        foreach (var c in digits)
        {
            if (c == '_')
                continue;

            int digit;
            if (c is >= '0' and <= '9')
            {
                digit = c - '0';
            }
            else if (hex && Uri.IsHexDigit(c))
            {
                digit = Uri.FromHex(c);
            }
            else
            {
                return false;
            }

            magnitude = (magnitude * (hex ? 16 : 10)) + digit;
        }

        return true;
    }

    /// <summary>The digits cannot be empty, and an underscore is between two digits.</summary>
    private static bool CheckUnderscores(string digits)
    {
        if (digits.Length == 0 || digits[0] == '_' || digits[^1] == '_')
            return false;

        return !digits.Contains("__", StringComparison.Ordinal);
    }

    public static bool TryParseF32(string text, out uint bits)
    {
        bits = 0;
        if (!TryParseFloat(text, mantissaBits: 23, exponentBits: 8, out var result))
            return false;

        bits = (uint)result;
        return true;
    }

    public static bool TryParseF64(string text, out ulong bits) => TryParseFloat(text, mantissaBits: 52, exponentBits: 11, out bits);

    private static bool TryParseFloat(string text, int mantissaBits, int exponentBits, out ulong bits)
    {
        bits = 0;
        if (text.Length == 0)
            return false;

        var negative = text[0] == '-';
        var body = text[0] is '+' or '-' ? text[1..] : text;
        var sign = negative ? 1UL << (mantissaBits + exponentBits) : 0;
        var exponentMask = ((1UL << exponentBits) - 1) << mantissaBits;
        if (body == "inf")
        {
            bits = sign | exponentMask;
            return true;
        }

        if (body == "nan")
        {
            bits = sign | exponentMask | (1UL << (mantissaBits - 1));
            return true;
        }

        if (body.StartsWith("nan:0x", StringComparison.Ordinal))
        {
            var payloadText = body[6..];
            if (!CheckUnderscores(payloadText) || !TryParseMagnitude("0x" + payloadText, out var payload) || payload == 0 || payload >= BigInteger.One << mantissaBits)
                return false;

            bits = sign | exponentMask | (ulong)payload;
            return true;
        }

        BigInteger mantissa;
        long exponent;
        if (body.StartsWith("0x", StringComparison.Ordinal))
        {
            if (!TryParseHexFloat(body[2..], out mantissa, out exponent))
                return false;

            // The value is mantissa * 2^exponent
            if (!TryRound(mantissa, exponent, mantissaBits, exponentBits, out var magnitude))
                return false;

            bits = sign | magnitude;
            return true;
        }

        if (!TryNormalizeDecimal(body, out var normalized))
            return false;

        if (mantissaBits == 23)
        {
            var value = float.Parse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (float.IsInfinity(value))
                return false;

            bits = sign | (uint)BitConverter.SingleToInt32Bits(value);
        }
        else
        {
            var value = double.Parse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (double.IsInfinity(value))
                return false;

            bits = sign | (ulong)BitConverter.DoubleToInt64Bits(value);
        }

        return true;
    }

    /// <summary>Checks a decimal float (digits, an optional fraction and an optional exponent, with underscores between digits) and removes its underscores.</summary>
    private static bool TryNormalizeDecimal(string text, out string normalized)
    {
        normalized = "";
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
        var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? mantissa : mantissa[..dot];
        var fraction = dot < 0 ? null : mantissa[(dot + 1)..];
        if (!IsDigits(integer, hex: false) || (fraction is { Length: > 0 } && !IsDigits(fraction, hex: false)))
            return false;

        if (exponentIndex >= 0)
        {
            var exponent = text[(exponentIndex + 1)..];
            if (exponent.Length > 0 && exponent[0] is '+' or '-')
            {
                exponent = exponent[1..];
            }

            if (!IsDigits(exponent, hex: false))
                return false;
        }

        normalized = text.Replace("_", "", StringComparison.Ordinal);
        if (normalized.EndsWith('.', StringComparison.Ordinal))
        {
            normalized += "0";
        }

        normalized = normalized.Replace(".e", ".0e", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool IsDigits(string text, bool hex)
    {
        if (!CheckUnderscores(text))
            return false;

        foreach (var c in text)
        {
            if (c != '_' && !(hex ? Uri.IsHexDigit(c) : c is >= '0' and <= '9'))
                return false;
        }

        return true;
    }

    private static bool TryParseHexFloat(string text, out BigInteger mantissa, out long exponent)
    {
        mantissa = BigInteger.Zero;
        exponent = 0;
        var exponentIndex = text.IndexOfAny(['p', 'P']);
        var digits = exponentIndex < 0 ? text : text[..exponentIndex];
        var dot = digits.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? digits : digits[..dot];
        var fraction = dot < 0 ? "" : digits[(dot + 1)..];
        if (!IsDigits(integer, hex: true) || (fraction.Length > 0 && !IsDigits(fraction, hex: true)))
            return false;

        foreach (var c in integer + fraction)
        {
            if (c != '_')
            {
                mantissa = (mantissa * 16) + Uri.FromHex(c);
            }
        }

        exponent = -4L * fraction.Count(c => c != '_');
        if (exponentIndex >= 0)
        {
            var exponentText = text[(exponentIndex + 1)..];
            var negative = exponentText.StartsWith('-', StringComparison.Ordinal);
            if (exponentText.Length > 0 && exponentText[0] is '+' or '-')
            {
                exponentText = exponentText[1..];
            }

            if (!IsDigits(exponentText, hex: false) || !TryParseMagnitude(exponentText, out var value))
                return false;

            var clamped = (long)BigInteger.Min(value, 100_000);
            exponent += negative ? -clamped : clamped;
        }

        return true;
    }

    /// <summary>Rounds mantissa * 2^exponent to the nearest float (ties to even): returns false when it is too large.</summary>
    private static bool TryRound(BigInteger mantissa, long exponent, int mantissaBits, int exponentBits, out ulong bits)
    {
        bits = 0;
        if (mantissa.IsZero)
            return true;

        var bias = (1 << (exponentBits - 1)) - 1;
        var length = (long)mantissa.GetBitLength();

        // The exponent of the leading bit, and the exponent of the last bit that the float keeps
        var leading = exponent + length - 1;
        var lastKept = Math.Max(leading - mantissaBits, 1 - bias - mantissaBits);
        var shift = lastKept - exponent;
        BigInteger kept;
        if (shift > 0)
        {
            var dropped = mantissa & ((BigInteger.One << (int)Math.Min(shift, int.MaxValue)) - 1);
            kept = mantissa >> (int)Math.Min(shift, int.MaxValue);
            var half = BigInteger.One << (int)(shift - 1);
            if (dropped > half || (dropped == half && !kept.IsEven))
            {
                kept++;
            }
        }
        else
        {
            kept = mantissa << (int)-shift;
        }

        // The rounding can carry into a new leading bit
        if (kept.GetBitLength() > mantissaBits + 1)
        {
            kept >>= 1;
            lastKept++;
        }

        var biasedExponent = lastKept + mantissaBits + bias;
        if (kept.GetBitLength() <= mantissaBits)
        {
            // A subnormal number (or zero)
            bits = (ulong)kept;
            return true;
        }

        if (biasedExponent >= (1 << exponentBits) - 1)
            return false;

        bits = ((ulong)biasedExponent << mantissaBits) | ((ulong)kept & ((1UL << mantissaBits) - 1));
        return true;
    }
}
