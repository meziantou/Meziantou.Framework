using System.Numerics;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization.Converters;

/// <summary>Parses the numbers in their most common form faster than the general parsers of the BCL, with the same results.</summary>
/// <remarks>
/// The general parsers handle the styles, the culture, the white spaces and the exponents: about 10 ns for an <see cref="int"/> and 50 ns for a
/// <see cref="double"/>. A value in another form is not parsed: the converters then use the general parsers, which also report the errors.
/// </remarks>
internal static class PlainNumberParser
{
    // 10^15 < 2^53: the integer of a double of 15 digits or less is exact
    private const int MaxDoubleDigits = 15;

    private static ReadOnlySpan<double> PowersOf10 => [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];

    /// <summary>Parses an <see cref="int"/>, a <see cref="long"/> or a <see cref="double"/> written with the invariant culture, as <c>T.Parse(value, provider)</c> does.</summary>
    /// <returns><see langword="false"/> for the other types, the other cultures, and the values that are not in the plain form.</returns>
    public static bool TryParse<TChar, T>(ReadOnlySpan<TChar> value, IFormatProvider provider, out T result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        // The checks of T are removed by the JIT for the value types
        if (typeof(T) == typeof(int))
        {
            // 9 digits always fit in an int: a longer value goes to the general parser, which checks the overflow
            if (InvariantNumbers.IsInvariant(provider) && TryParseInteger(value, maxDigits: 9, out var magnitude, out var negative))
            {
                var number = (int)magnitude;
                result = (T)(object)(negative ? -number : number);
                return true;
            }
        }
        else if (typeof(T) == typeof(long))
        {
            if (InvariantNumbers.IsInvariant(provider) && TryParseInteger(value, maxDigits: 18, out var magnitude, out var negative))
            {
                var number = (long)magnitude;
                result = (T)(object)(negative ? -number : number);
                return true;
            }
        }
        else if (typeof(T) == typeof(double))
        {
            if (InvariantNumbers.IsInvariant(provider) && TryParseDouble(value, out var number))
            {
                result = (T)(object)number;
                return true;
            }
        }

        result = default!;
        return false;
    }

    /// <summary>Parses an optional minus sign followed by 1 to <paramref name="maxDigits"/> digits.</summary>
    internal static bool TryParseInteger<TChar>(ReadOnlySpan<TChar> value, int maxDigits, out ulong magnitude, out bool negative)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        magnitude = 0;
        negative = !value.IsEmpty && value[0] == TChar.CreateTruncating('-');
        if (negative)
        {
            value = value[1..];
        }

        if (value.IsEmpty || value.Length > maxDigits)
            return false;

        foreach (var c in value)
        {
            var digit = ulong.CreateTruncating(c) - '0';
            if (digit > 9)
                return false;

            magnitude = (magnitude * 10) + digit;
        }

        return true;
    }

    /// <summary>Parses an optional minus sign, then digits, then optionally a dot followed by digits, up to 15 digits in all.</summary>
    /// <remarks>
    /// The integer of the digits and the power of 10 of the decimals are exact doubles, so their quotient is the correctly rounded value of the text, as
    /// <see cref="double.Parse(ReadOnlySpan{char}, IFormatProvider?)"/> returns (Clinger's fast path). <c>-0</c> is negative zero, as with the BCL.
    /// </remarks>
    internal static bool TryParseDouble<TChar>(ReadOnlySpan<TChar> value, out double result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = 0;
        var negative = !value.IsEmpty && value[0] == TChar.CreateTruncating('-');
        if (negative)
        {
            value = value[1..];
        }

        if (value.IsEmpty || value.Length > MaxDoubleDigits + 1)
            return false;

        ulong number = 0;
        var digits = 0;
        var scale = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var digit = ulong.CreateTruncating(value[i]) - '0';
            if (digit <= 9)
            {
                number = (number * 10) + digit;
                digits++;
                continue;
            }

            // A single dot, between digits
            if (value[i] != TChar.CreateTruncating('.') || scale > 0 || i == 0 || i == value.Length - 1)
                return false;

            scale = value.Length - i - 1;
        }

        if (digits > MaxDoubleDigits)
            return false;

        var magnitude = (double)number / PowersOf10[scale];
        result = negative ? -magnitude : magnitude;
        return true;
    }
}
