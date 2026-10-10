using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>
/// Exact equivalents of the few arithmetic and formatting rules the committed corpus depends on: floor division and
/// modulo, round-half-even, shortest round-trip float representations and correctly rounded fixed-point formatting. The
/// manifest records numbers and descriptions produced by these rules, so they must never drift.
/// </summary>
internal static class Py
{
    public static void Assert(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException("Assertion failed" + (message is null ? "" : ": " + message));
    }

    /// <summary>Floor division (rounds toward negative infinity).</summary>
    public static long FloorDiv(long a, long b)
    {
        var q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0)))
            q--;
        return q;
    }

    public static int FloorDiv(int a, int b) => (int)FloorDiv((long)a, b);

    /// <summary>Modulo with the sign of the divisor.</summary>
    public static long Mod(long a, long b)
    {
        var r = a % b;
        if (r != 0 && ((r < 0) != (b < 0)))
            r += b;
        return r;
    }

    public static int Mod(int a, int b) => (int)Mod((long)a, b);

    /// <summary>Ceiling division of non-negative values.</summary>
    public static long CeilDiv(long a, long b) => -FloorDiv(-a, b);

    public static int CeilDiv(int a, int b) => (int)CeilDiv((long)a, b);

    public static int BitLength(long value) => value == 0 ? 0 : 64 - BitOperations.LeadingZeroCount((ulong)Math.Abs(value));

    public static int BitLength(BigInteger value) => value.IsZero ? 0 : (int)BigInteger.Abs(value).GetBitLength();

    /// <summary>Round half to even of the exact binary value, as an integer.</summary>
    public static long Round(double value) => (long)Math.Round(value, MidpointRounding.ToEven);

    /// <summary>Rounds to <paramref name="digits"/> decimal places: the exact binary value is rounded half to even, then the
    /// nearest double of that decimal is returned.</summary>
    public static double Round(double value, int digits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value == 0)
            return value;

        var scaled = RoundHalfEven(ExactRational(value) * Rational.Pow10(digits));
        if (scaled.IsZero)
            return value < 0 ? -0.0 : 0.0;

        return double.Parse(scaled.ToString(CultureInfo.InvariantCulture) + "E-" + digits.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>"%.nf": the exact binary value rounded half to even to <paramref name="digits"/> decimal places.</summary>
    public static string FormatFixed(double value, int digits)
    {
        var negative = double.IsNegative(value);
        var scaled = RoundHalfEven(ExactRational(Math.Abs(value)) * Rational.Pow10(digits));
        var text = scaled.ToString(CultureInfo.InvariantCulture);
        if (digits > 0)
        {
            text = text.PadLeft(digits + 1, '0');
            text = text[..^digits] + "." + text[^digits..];
        }

        return (negative ? "-" : "") + text;
    }

    /// <summary>Python repr of an integer (an explicit overload: without it, integers would bind to <see cref="Repr(double)"/>).</summary>
    public static string Repr(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The shortest representation that round-trips, laid out like Python's float repr.</summary>
    public static string Repr(double value)
    {
        if (double.IsNaN(value))
            return "nan";
        if (double.IsPositiveInfinity(value))
            return "inf";
        if (double.IsNegativeInfinity(value))
            return "-inf";

        var negative = double.IsNegative(value);
        var r = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var e = r.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(r[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..e];
        }

        var dot = r.IndexOf('.', StringComparison.Ordinal);
        var integerPart = dot >= 0 ? r[..dot] : r;
        var fractionPart = dot >= 0 ? r[(dot + 1)..] : "";
        var digits = (integerPart + fractionPart).TrimStart('0');
        var leadingZeros = (integerPart + fractionPart).Length - (integerPart + fractionPart).TrimStart('0').Length;
        var point = integerPart.Length - leadingZeros + exponent; // value = 0.digits * 10^point
        digits = digits.TrimEnd('0');
        string result;
        if (digits.Length == 0)
        {
            result = "0.0";
        }
        else if (point <= -4 || point > 16)
        {
            var mantissa = digits.Length == 1 ? digits : digits[..1] + "." + digits[1..];
            var exp = point - 1;
            result = mantissa + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
        }
        else if (point <= 0)
        {
            result = "0." + new string('0', -point) + digits;
        }
        else if (point >= digits.Length)
        {
            result = digits + new string('0', point - digits.Length) + ".0";
        }
        else
        {
            result = digits[..point] + "." + digits[point..];
        }

        return (negative ? "-" : "") + result;
    }

    /// <summary>Python repr of common values: str (quoted), int, bool, None, float, lists and tuples (<see cref="Px"/>,
    /// <see cref="ITuple"/>).</summary>
    public static string Repr(object? value)
    {
        return value switch
        {
            null => "None",
            bool b => b ? "True" : "False",
            string s => ReprString(s),
            double d => Repr(d),
            float f => Repr((double)f),
            byte or sbyte or short or ushort or int or uint or long or ulong or BigInteger => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            Px px => px.ToString(),
            System.Runtime.CompilerServices.ITuple tuple => ReprTuple(Enumerable.Range(0, tuple.Length).Select(i => tuple[i])),
            IEnumerable enumerable => "[" + string.Join(", ", enumerable.Cast<object?>().Select(Repr)) + "]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        };
    }

    public static string ReprTuple(IEnumerable<object?> items)
    {
        var list = items.Select(Repr).ToList();
        return list.Count == 1 ? "(" + list[0] + ",)" : "(" + string.Join(", ", list) + ")";
    }

    /// <summary>str(): strings unchanged, everything else as <see cref="Repr(object?)"/>.</summary>
    public static string Str(object? value) => value is string s ? s : Repr(value);

    private static string ReprString(string value)
    {
        var quote = value.Contains('\'', StringComparison.Ordinal) && !value.Contains('"', StringComparison.Ordinal) ? '"' : '\'';
        var sb = new StringBuilder();
        sb.Append(quote);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c == quote)
                        sb.Append('\\').Append(c);
                    else if (c < 0x20 || c == 0x7F)
                        sb.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append(quote);
        return sb.ToString();
    }

    private static Rational ExactRational(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0)
            exponent++;
        else
            mantissa |= 1L << 52;
        exponent -= 1075;
        BigInteger numerator = mantissa;
        BigInteger denominator = BigInteger.One;
        if (exponent > 0)
            numerator <<= exponent;
        else
            denominator <<= -exponent;
        return new Rational(negative ? -numerator : numerator, denominator);
    }

    private static BigInteger RoundHalfEven(Rational value)
    {
        var floor = value.Floor();
        var remainder = value - floor;
        var comparison = remainder.CompareTo(new Rational(1, 2));
        if (comparison > 0 || (comparison == 0 && !floor.IsEven))
            return floor + 1;
        return floor;
    }
}
