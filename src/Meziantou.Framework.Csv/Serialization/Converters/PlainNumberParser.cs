using System.Numerics;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization.Converters;

/// <summary>Parses the integers in their most common form faster than the general parsers of the BCL, with the same results.</summary>
/// <remarks>
/// The general parsers handle the styles, the culture and the white spaces: about 10 ns for an <see cref="int"/>. A value in another form is
/// not parsed: the converters then use the general parsers, which also report the errors.
/// </remarks>
internal static class PlainNumberParser
{
    /// <summary>Parses an <see cref="int"/> or a <see cref="long"/> written with the invariant culture, as <c>T.Parse(value, provider)</c> does.</summary>
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
}
