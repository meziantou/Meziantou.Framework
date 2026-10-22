using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

internal enum IntlDecimalKind : byte
{
    Finite,
    NaN,
    PositiveInfinity,
    NegativeInfinity,
}

/// <summary>The unsigned rounding modes of ECMA-402 (GetUnsignedRoundingMode).</summary>
internal enum UnsignedRoundingMode : byte
{
    Zero,
    Infinity,
    HalfZero,
    HalfInfinity,
    HalfEven,
}

/// <summary>
/// An Intl mathematical value (https://tc39.es/ecma402/#intl-mathematical-value): an exact decimal number
/// (coefficient \u00D7 10^exponent with its sign, which keeps negative zero), not-a-number, or an infinity. A Number is
/// converted with its shortest round-tripping decimal where ECMA-402 uses ToIntlMathematicalValue
/// (<see cref="FromNumber"/>), and with its exact binary value where it uses \u211D(x) (<see cref="FromDouble"/>).
/// </summary>
internal readonly struct IntlDecimal
{
    private static readonly BigInteger[] SmallPowersOfTen = CreatePowersOfTen();

    public IntlDecimal(bool isNegative, BigInteger coefficient, int exponent)
    {
        Kind = IntlDecimalKind.Finite;
        IsNegative = isNegative;
        Coefficient = coefficient;
        Exponent = exponent;
    }

    private IntlDecimal(IntlDecimalKind kind)
    {
        Kind = kind;
        IsNegative = kind is IntlDecimalKind.NegativeInfinity;
        Coefficient = BigInteger.Zero;
        Exponent = 0;
    }

    public static IntlDecimal NaN => new(IntlDecimalKind.NaN);

    public static IntlDecimal PositiveInfinity => new(IntlDecimalKind.PositiveInfinity);

    public static IntlDecimal NegativeInfinity => new(IntlDecimalKind.NegativeInfinity);

    public static IntlDecimal Zero => new(isNegative: false, BigInteger.Zero, 0);

    public IntlDecimalKind Kind { get; }

    /// <summary>Gets whether the value is negative (including negative zero and negative infinity).</summary>
    public bool IsNegative { get; }

    /// <summary>Gets the absolute value of the coefficient.</summary>
    public BigInteger Coefficient { get; }

    public int Exponent { get; }

    public bool IsFinite => Kind is IntlDecimalKind.Finite;

    public bool IsNaN => Kind is IntlDecimalKind.NaN;

    public bool IsInfinity => Kind is IntlDecimalKind.PositiveInfinity or IntlDecimalKind.NegativeInfinity;

    public bool IsZero => Kind is IntlDecimalKind.Finite && Coefficient.IsZero;

    public bool IsNegativeZero => IsZero && IsNegative;

    /// <summary>Gets the exponent of the most significant digit (floor(log10(|x|))); the value must be finite and not zero.</summary>
    public int Magnitude => DigitCount(Coefficient) - 1 + Exponent;

    /// <summary>Gets whether the value is an integer.</summary>
    public bool IsInteger
    {
        get
        {
            if (!IsFinite)
                return false;

            if (Exponent >= 0 || Coefficient.IsZero)
                return true;

            return BigInteger.Remainder(Coefficient, PowerOfTen(-Exponent)).IsZero;
        }
    }

    /// <summary>
    /// The value of a Number as ToIntlMathematicalValue computes it: the decimal value of Number::toString(x, 10), the
    /// shortest decimal that round-trips (0.1 is 0.1, not 0.1000000000000000055511151231257827...).
    /// </summary>
    public static IntlDecimal FromNumber(double value)
    {
        // NaN, the infinities, the zeros and the integers that are exactly representable have the same value either way
        if (!double.IsFinite(value) || (Math.Abs(value) < 9007199254740992 && value == Math.Truncate(value)))
            return FromDouble(value);

        // "R" gives the shortest round-tripping digits (at most 17, so they fit in a ulong), as Number::toString does
        Span<char> buffer = stackalloc char[32];
        Math.Abs(value).TryFormat(buffer, out var written, "R", CultureInfo.InvariantCulture);
        ulong coefficient = 0;
        var exponent = 0;
        var fraction = false;
        for (var i = 0; i < written; i++)
        {
            var c = buffer[i];
            if (char.IsAsciiDigit(c))
            {
                coefficient = (coefficient * 10) + (ulong)(c - '0');
                if (fraction)
                {
                    exponent--;
                }
            }
            else if (c == '.')
            {
                fraction = true;
            }
            else
            {
                exponent += int.Parse(buffer[(i + 1)..written], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
            }
        }

        return new IntlDecimal(value < 0, coefficient, exponent);
    }

    /// <summary>The exact binary value of a Number (\u211D(x)).</summary>
    public static IntlDecimal FromDouble(double value)
    {
        if (double.IsNaN(value))
            return NaN;

        if (double.IsPositiveInfinity(value))
            return PositiveInfinity;

        if (double.IsNegativeInfinity(value))
            return NegativeInfinity;

        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var biasedExponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & ((1L << 52) - 1);
        int exponent;
        if (biasedExponent == 0)
        {
            exponent = -1074;
        }
        else
        {
            mantissa |= 1L << 52;
            exponent = biasedExponent - 1075;
        }

        if (mantissa == 0)
            return new IntlDecimal(negative, BigInteger.Zero, 0);

        var trailingZeros = BitOperations.TrailingZeroCount(mantissa);
        mantissa >>= trailingZeros;
        exponent += trailingZeros;
        if (exponent >= 0)
            return new IntlDecimal(negative, new BigInteger(mantissa) << exponent, 0);

        // m \u00D7 2^e = m \u00D7 5^-e \u00D7 10^e
        return new IntlDecimal(negative, new BigInteger(mantissa) * BigInteger.Pow(5, -exponent), exponent);
    }

    public static IntlDecimal FromBigInteger(BigInteger value) => new(value.Sign < 0, BigInteger.Abs(value), 0);

    /// <summary>
    /// The mathematical value of a string (ToIntlMathematicalValue): the StringNumericLiteral grammar with its exact
    /// value; NaN when the string does not match it. A value that rounds to an infinity or to zero as a Number becomes
    /// that infinity or (negative) zero.
    /// </summary>
    public static IntlDecimal Parse(string text)
    {
        var number = NumberParsing.StringToNumber(text);
        if (double.IsNaN(number))
            return NaN;

        if (double.IsInfinity(number))
            return number > 0 ? PositiveInfinity : NegativeInfinity;

        if (number == 0)
            return new IntlDecimal(double.IsNegative(number), BigInteger.Zero, 0);

        var span = NumberParsing.TrimWhiteSpace(text.AsSpan());
        if (span.Length > 2 && span[0] == '0' && span[1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
        {
            var radix = span[1] switch
            {
                'x' or 'X' => 16,
                'o' or 'O' => 8,
                _ => 2,
            };

            var value = BigInteger.Zero;
            foreach (var c in span[2..])
            {
                value = (value * radix) + NumberParsing.GetDigitValue(c);
            }

            return new IntlDecimal(isNegative: false, value, 0);
        }

        var negative = false;
        if (span[0] is '+' or '-')
        {
            negative = span[0] == '-';
            span = span[1..];
        }

        var digits = new StringBuilder(span.Length);
        var exponent = 0;
        var index = 0;
        while (index < span.Length && char.IsAsciiDigit(span[index]))
        {
            digits.Append(span[index++]);
        }

        if (index < span.Length && span[index] == '.')
        {
            index++;
            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                digits.Append(span[index++]);
                exponent--;
            }
        }

        if (index < span.Length && span[index] is 'e' or 'E')
        {
            index++;
            var exponentNegative = false;
            if (index < span.Length && span[index] is '+' or '-')
            {
                exponentNegative = span[index] == '-';
                index++;
            }

            long value = 0;
            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                value = Math.Min((value * 10) + (span[index++] - '0'), 100_000_000);
            }

            exponent += (int)(exponentNegative ? -value : value);
        }

        var coefficient = BigInteger.Parse(digits.Length == 0 ? "0" : digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture);
        return new IntlDecimal(negative, coefficient, exponent);
    }

    public IntlDecimal Negate() => Kind switch
    {
        IntlDecimalKind.Finite => new IntlDecimal(!IsNegative, Coefficient, Exponent),
        IntlDecimalKind.PositiveInfinity => NegativeInfinity,
        IntlDecimalKind.NegativeInfinity => PositiveInfinity,
        _ => this,
    };

    public IntlDecimal Abs() => IsNegative ? Negate() : this;

    /// <summary>Gets x \u00D7 10^n.</summary>
    public IntlDecimal ScaleByPowerOfTen(int n) => IsFinite ? new IntlDecimal(IsNegative, Coefficient, Exponent + n) : this;

    /// <summary>Compares two finite values (negative zero equals zero).</summary>
    public int CompareValue(IntlDecimal other)
    {
        var left = IsZero ? 0 : IsNegative ? -1 : 1;
        var right = other.IsZero ? 0 : other.IsNegative ? -1 : 1;
        if (left != right || left == 0)
            return left.CompareTo(right);

        var exponent = Math.Min(Exponent, other.Exponent);
        var a = Coefficient * PowerOfTen(Exponent - exponent);
        var b = other.Coefficient * PowerOfTen(other.Exponent - exponent);
        var result = a.CompareTo(b);
        return left < 0 ? -result : result;
    }

    /// <summary>Converts the value to the closest Number.</summary>
    public double ToDouble()
    {
        switch (Kind)
        {
            case IntlDecimalKind.NaN:
                return double.NaN;
            case IntlDecimalKind.PositiveInfinity:
                return double.PositiveInfinity;
            case IntlDecimalKind.NegativeInfinity:
                return double.NegativeInfinity;
        }

        if (Coefficient.IsZero)
            return IsNegative ? -0.0 : 0.0;

        var text = Coefficient.ToString(CultureInfo.InvariantCulture) + "e" + Exponent.ToString(CultureInfo.InvariantCulture);
        var result = NumberParsing.StringToNumber(text);
        return IsNegative ? -result : result;
    }

    /// <summary>
    /// Rounds a non-negative finite value to a multiple of increment \u00D7 10^magnitude with an unsigned rounding mode
    /// (the r1 / r2 choice of ToRawPrecision and ToRawFixed, with ApplyUnsignedRoundingMode); returns n such that the
    /// result is n \u00D7 10^magnitude.
    /// </summary>
    public static BigInteger RoundToMagnitude(BigInteger coefficient, int exponent, int magnitude, int increment, UnsignedRoundingMode mode)
    {
        BigInteger numerator;
        BigInteger denominator;
        if (exponent >= magnitude)
        {
            numerator = coefficient * PowerOfTen(exponent - magnitude);
            denominator = BigInteger.One;
        }
        else
        {
            numerator = coefficient;
            denominator = PowerOfTen(magnitude - exponent);
        }

        var unit = denominator * increment;
        var quotient = BigInteger.DivRem(numerator, unit, out var remainder);
        var n1 = quotient * increment;
        if (remainder.IsZero)
            return n1;

        var n2 = n1 + increment;
        switch (mode)
        {
            case UnsignedRoundingMode.Zero:
                return n1;
            case UnsignedRoundingMode.Infinity:
                return n2;
        }

        var comparison = (remainder << 1).CompareTo(unit);
        if (comparison < 0)
            return n1;

        if (comparison > 0)
            return n2;

        return mode switch
        {
            UnsignedRoundingMode.HalfZero => n1,
            UnsignedRoundingMode.HalfInfinity => n2,
            _ => quotient.IsEven ? n1 : n2,
        };
    }

    public static BigInteger PowerOfTen(int n) => n < SmallPowersOfTen.Length ? SmallPowersOfTen[n] : BigInteger.Pow(10, n);

    /// <summary>Gets the number of decimal digits of a non-negative integer (1 for zero).</summary>
    public static int DigitCount(BigInteger value)
    {
        if (value < ulong.MaxValue)
        {
            var v = (ulong)value;
            var count = 1;
            while (v >= 10)
            {
                v /= 10;
                count++;
            }

            return count;
        }

        // An estimate from the bit length, then corrected
        var estimate = (int)((value.GetBitLength() - 1) * 0.30102999566398120) + 1;
        if (value >= PowerOfTen(estimate))
            return estimate + 1;

        if (value < PowerOfTen(estimate - 1))
            return estimate - 1;

        return estimate;
    }

    public override string ToString()
    {
        return Kind switch
        {
            IntlDecimalKind.NaN => "NaN",
            IntlDecimalKind.PositiveInfinity => "Infinity",
            IntlDecimalKind.NegativeInfinity => "-Infinity",
            _ => (IsNegative ? "-" : "") + Coefficient.ToString(CultureInfo.InvariantCulture) + "e" + Exponent.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static BigInteger[] CreatePowersOfTen()
    {
        var result = new BigInteger[64];
        result[0] = BigInteger.One;
        for (var i = 1; i < result.Length; i++)
        {
            result[i] = result[i - 1] * 10;
        }

        return result;
    }
}
