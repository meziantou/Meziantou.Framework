using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Exact integer arithmetic of Temporal: floor division, the rounding modes, conversions to Number values.</summary>
internal static class TemporalMath
{
    public const long NsPerDayLong = 86_400_000_000_000;

    /// <summary>nsPerDay</summary>
    public static readonly Int128 NsPerDay = NsPerDayLong;

    /// <summary>nsMaxInstant = 10^8 \u00D7 nsPerDay</summary>
    public static readonly Int128 NsMaxInstant = (Int128)NsPerDayLong * 100_000_000;

    /// <summary>nsMinInstant = -nsMaxInstant</summary>
    public static readonly Int128 NsMinInstant = -NsMaxInstant;

    /// <summary>maxTimeDuration = 2^53 \u00D7 10^9 - 1</summary>
    public static readonly Int128 MaxTimeDuration = ((Int128)9_007_199_254_740_992 * 1_000_000_000) - 1;

    /// <summary>Gets the length of a time unit or day, in nanoseconds ("Length in nanoseconds" of Table 21).</summary>
    public static long UnitLengthNs(TemporalUnit unit)
    {
        return unit switch
        {
            TemporalUnit.Day => NsPerDayLong,
            TemporalUnit.Hour => 3_600_000_000_000,
            TemporalUnit.Minute => 60_000_000_000,
            TemporalUnit.Second => 1_000_000_000,
            TemporalUnit.Millisecond => 1_000_000,
            TemporalUnit.Microsecond => 1_000,
            TemporalUnit.Nanosecond => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Not a time unit"),
        };
    }

    public static Int128 FloorDivide(Int128 value, Int128 divisor)
    {
        var quotient = value / divisor;
        if ((value % divisor != 0) && ((value < 0) != (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }

    public static Int128 FloorModulo(Int128 value, Int128 divisor)
    {
        var remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
        {
            remainder += divisor;
        }

        return remainder;
    }

    public static long FloorDivide(long value, long divisor)
    {
        var quotient = value / divisor;
        if ((value % divisor != 0) && ((value < 0) != (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }

    public static long FloorModulo(long value, long divisor)
    {
        var remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
        {
            remainder += divisor;
        }

        return remainder;
    }

    /// <summary>NegateRoundingMode(roundingMode)</summary>
    public static TemporalRoundingMode NegateRoundingMode(TemporalRoundingMode roundingMode)
    {
        return roundingMode switch
        {
            TemporalRoundingMode.Ceil => TemporalRoundingMode.Floor,
            TemporalRoundingMode.Floor => TemporalRoundingMode.Ceil,
            TemporalRoundingMode.HalfCeil => TemporalRoundingMode.HalfFloor,
            TemporalRoundingMode.HalfFloor => TemporalRoundingMode.HalfCeil,
            _ => roundingMode,
        };
    }

    /// <summary>GetUnsignedRoundingMode(roundingMode, sign)</summary>
    public static UnsignedRoundingMode GetUnsignedRoundingMode(TemporalRoundingMode roundingMode, bool negative)
    {
        return roundingMode switch
        {
            TemporalRoundingMode.Ceil => negative ? UnsignedRoundingMode.Zero : UnsignedRoundingMode.Infinity,
            TemporalRoundingMode.Floor => negative ? UnsignedRoundingMode.Infinity : UnsignedRoundingMode.Zero,
            TemporalRoundingMode.Expand => UnsignedRoundingMode.Infinity,
            TemporalRoundingMode.Trunc => UnsignedRoundingMode.Zero,
            TemporalRoundingMode.HalfCeil => negative ? UnsignedRoundingMode.HalfZero : UnsignedRoundingMode.HalfInfinity,
            TemporalRoundingMode.HalfFloor => negative ? UnsignedRoundingMode.HalfInfinity : UnsignedRoundingMode.HalfZero,
            TemporalRoundingMode.HalfExpand => UnsignedRoundingMode.HalfInfinity,
            TemporalRoundingMode.HalfTrunc => UnsignedRoundingMode.HalfZero,
            _ => UnsignedRoundingMode.HalfEven,
        };
    }

    /// <summary>
    /// ApplyUnsignedRoundingMode for x strictly between r1 and r2 = r1 + 1 (in units of the increment): x - r1 is
    /// <paramref name="numerator"/> / <paramref name="denominator"/> (0 &lt; numerator &lt; denominator).
    /// </summary>
    /// <returns>True for r2, false for r1.</returns>
    public static bool RoundsUp(Int128 numerator, Int128 denominator, bool r1IsEven, UnsignedRoundingMode mode)
    {
        switch (mode)
        {
            case UnsignedRoundingMode.Zero:
                return false;
            case UnsignedRoundingMode.Infinity:
                return true;
        }

        // d1 = numerator, d2 = denominator - numerator
        var d2 = denominator - numerator;
        if (numerator < d2)
            return false;

        if (d2 < numerator)
            return true;

        return mode switch
        {
            UnsignedRoundingMode.HalfZero => false,
            UnsignedRoundingMode.HalfInfinity => true,
            _ => !r1IsEven,
        };
    }

    /// <summary>The same as <see cref="RoundsUp(Int128, Int128, bool, UnsignedRoundingMode)"/> for BigInteger values.</summary>
    public static bool RoundsUp(BigInteger numerator, BigInteger denominator, bool r1IsEven, UnsignedRoundingMode mode)
    {
        switch (mode)
        {
            case UnsignedRoundingMode.Zero:
                return false;
            case UnsignedRoundingMode.Infinity:
                return true;
        }

        var d2 = denominator - numerator;
        if (numerator < d2)
            return false;

        if (d2 < numerator)
            return true;

        return mode switch
        {
            UnsignedRoundingMode.HalfZero => false,
            UnsignedRoundingMode.HalfInfinity => true,
            _ => !r1IsEven,
        };
    }

    /// <summary>RoundNumberToIncrement(x, increment, roundingMode) for an integer x.</summary>
    public static Int128 RoundNumberToIncrement(Int128 x, Int128 increment, TemporalRoundingMode roundingMode)
    {
        var negative = x < 0;
        var magnitude = negative ? -x : x;
        var quotient = magnitude / increment;
        var remainder = magnitude % increment;
        if (remainder != 0)
        {
            var unsignedMode = GetUnsignedRoundingMode(roundingMode, negative);
            if (RoundsUp(remainder, increment, Int128.IsEvenInteger(quotient), unsignedMode))
            {
                quotient++;
            }
        }

        var result = quotient * increment;
        return negative ? -result : result;
    }

    /// <summary>RoundNumberToIncrement(x, increment, roundingMode) for a long.</summary>
    public static long RoundNumberToIncrement(long x, long increment, TemporalRoundingMode roundingMode) => (long)RoundNumberToIncrement((Int128)x, increment, roundingMode);

    /// <summary>RoundNumberToIncrementAsIfPositive(x, increment, roundingMode)</summary>
    public static Int128 RoundNumberToIncrementAsIfPositive(Int128 x, Int128 increment, TemporalRoundingMode roundingMode)
    {
        var quotient = FloorDivide(x, increment);
        var remainder = FloorModulo(x, increment);
        if (remainder != 0)
        {
            var unsignedMode = GetUnsignedRoundingMode(roundingMode, negative: false);
            if (RoundsUp(remainder, increment, Int128.IsEvenInteger(quotient), unsignedMode))
            {
                quotient++;
            }
        }

        return quotient * increment;
    }

    /// <summary>Gets the Number value closest to an integer (\U0001D53D(x)).</summary>
    public static double ToDouble(Int128 value)
    {
        const long MaxSafe = 9_007_199_254_740_992;
        if (value >= -MaxSafe && value <= MaxSafe)
            return (long)value;

        return BigIntOperations.ToNumber((BigInteger)value);
    }

    /// <summary>Converts an integral Number to an integer (the value must be finite and integral).</summary>
    public static Int128 ToInt128(double value) => (Int128)(BigInteger)value;

    /// <summary>Gets the Number value closest to the rational numerator / denominator (round half to even).</summary>
    public static double RationalToDouble(BigInteger numerator, BigInteger denominator)
    {
        if (numerator.IsZero)
            return 0;

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var negative = numerator.Sign < 0;
        var magnitude = negative ? -numerator : numerator;

        // Scale the quotient to at least 55 significant bits, then add a sticky bit for the remainder
        var shift = 55 - (int)(magnitude.GetBitLength() - denominator.GetBitLength());
        var scaled = shift >= 0 ? magnitude << shift : magnitude;
        var scaledDenominator = shift >= 0 ? denominator : denominator << -shift;
        var quotient = BigInteger.DivRem(scaled, scaledDenominator, out var remainder);
        quotient <<= 1;
        if (!remainder.IsZero)
        {
            quotient += BigInteger.One;
        }

        var result = BigIntOperations.ToDouble(quotient, -shift - 1);
        return negative ? -result : result;
    }

    /// <summary>Formats an integral Number as a decimal integer (no exponent).</summary>
    public static string FormatInteger(double value) => ((BigInteger)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
