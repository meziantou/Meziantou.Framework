using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>Parses the floating-point numbers written with the invariant culture faster than the BCL, with the same result.</summary>
/// <remarks>
/// This is the fast path of Clinger (1990), also the first step of fast_float: when the digits form an integer that the type represents exactly and
/// the power of ten is also exact, one IEEE multiplication or division gives the correctly rounded value, which is what the BCL returns. The other
/// values, such as values with more digits, large exponents, white spaces, group separators or <c>NaN</c>, are left to the BCL.
/// </remarks>
internal static class InvariantFloatingPoint
{
    // 10^22 is the largest power of ten that a double represents exactly (5^22 < 2^53)
    private static readonly double[] DoublePowersOf10 =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    // 10^10 is the largest power of ten that a float represents exactly (5^10 < 2^24)
    private static readonly float[] SinglePowersOf10 = [1e0f, 1e1f, 1e2f, 1e3f, 1e4f, 1e5f, 1e6f, 1e7f, 1e8f, 1e9f, 1e10f];

    // More digits may not fit in a ulong
    private const int MaxDigits = 19;

    // Larger exponents are left to the BCL, which also prevents the overflow of the exponent
    private const int MaxExponentDigits = 4;

    /// <summary>Gets a value indicating whether <typeparamref name="T"/> has a fast path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSupported<T>() => typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half);

    /// <summary>Tries to parse a <see cref="double"/>, a <see cref="float"/> or a <see cref="Half"/> written with the invariant culture.</summary>
    /// <returns>
    /// <see langword="true"/> when the value is parsed, with the value that <c>T.Parse(value, CultureInfo.InvariantCulture)</c> returns;
    /// <see langword="false"/> when the value must be parsed by the BCL, valid or not.
    /// </returns>
    /// <remarks>
    /// The accepted values are an optional sign, digits with an optional dot, at least one digit, and an optional exponent: <c>-123.45</c>,
    /// <c>.5</c>, <c>1e-3</c>.
    /// </remarks>
    public static bool TryParse<TChar, T>(ReadOnlySpan<TChar> value, out T result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default!;
        if (!TryParseParts(value, out var negative, out var mantissa, out var exponent))
            return false;

        if (typeof(T) == typeof(double))
        {
            if (mantissa > (1UL << 53) || exponent is < -22 or > 22)
                return false;

            var number = (double)mantissa;
            number = exponent < 0 ? number / DoublePowersOf10[-exponent] : number * DoublePowersOf10[exponent];
            result = (T)(object)(negative ? -number : number);
            return true;
        }

        if (typeof(T) == typeof(float))
        {
            if (mantissa > (1UL << 24) || exponent is < -10 or > 10)
                return false;

            var number = (float)mantissa;
            number = exponent < 0 ? number / SinglePowersOf10[-exponent] : number * SinglePowersOf10[exponent];
            result = (T)(object)(negative ? -number : number);
            return true;
        }

        if (typeof(T) == typeof(Half))
        {
            // The mantissa and 10^4 are exact Half values. The operation is rounded to a double, then to a Half: rounding twice gives the correctly
            // rounded value when the first precision is at least twice the second one plus two bits (Figueroa, 1995), and 53 >= 2 * 11 + 2.
            if (mantissa > (1UL << 11) || exponent is < -4 or > 4)
                return false;

            var number = (double)mantissa;
            number = exponent < 0 ? number / DoublePowersOf10[-exponent] : number * DoublePowersOf10[exponent];
            var half = (Half)(negative ? -number : number);

            // The BCL decides how an overflow is reported
            if (Half.IsInfinity(half))
                return false;

            result = (T)(object)half;
            return true;
        }

        return false;
    }

    /// <summary>Splits <c>[+-]digits[.digits][(e|E)[+-]digits]</c> into a sign, the digits as an integer, and the power of ten to apply to it.</summary>
    private static bool TryParseParts<TChar>(ReadOnlySpan<TChar> value, out bool negative, out ulong mantissa, out int exponent)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        negative = false;
        mantissa = 0;
        exponent = 0;

        var index = 0;
        if (index < value.Length && (value[index] == TChar.CreateTruncating('-') || value[index] == TChar.CreateTruncating('+')))
        {
            negative = value[index] == TChar.CreateTruncating('-');
            index++;
        }

        // A value with more digits is left to the BCL as soon as it is found, so that a long value is not read twice. With more digits, the
        // mantissa could overflow.
        var digits = 0;
        while (index < value.Length && uint.CreateTruncating(value[index]) - '0' <= 9)
        {
            if (++digits > MaxDigits)
                return false;

            mantissa = (mantissa * 10) + (uint.CreateTruncating(value[index]) - '0');
            index++;
        }

        var fractionDigits = 0;
        if (index < value.Length && value[index] == TChar.CreateTruncating('.'))
        {
            index++;
            while (index < value.Length && uint.CreateTruncating(value[index]) - '0' <= 9)
            {
                if (++digits > MaxDigits)
                    return false;

                mantissa = (mantissa * 10) + (uint.CreateTruncating(value[index]) - '0');
                index++;
                fractionDigits++;
            }
        }

        if (digits == 0)
            return false;

        if (index < value.Length && (value[index] == TChar.CreateTruncating('e') || value[index] == TChar.CreateTruncating('E')))
        {
            index++;
            var negativeExponent = false;
            if (index < value.Length && (value[index] == TChar.CreateTruncating('-') || value[index] == TChar.CreateTruncating('+')))
            {
                negativeExponent = value[index] == TChar.CreateTruncating('-');
                index++;
            }

            var exponentStart = index;
            while (index < value.Length && uint.CreateTruncating(value[index]) - '0' <= 9)
            {
                if (index - exponentStart == MaxExponentDigits)
                    return false;

                exponent = (exponent * 10) + (int)(uint.CreateTruncating(value[index]) - '0');
                index++;
            }

            if (index == exponentStart)
                return false;

            if (negativeExponent)
            {
                exponent = -exponent;
            }
        }

        exponent -= fractionDigits;
        return index == value.Length;
    }
}
