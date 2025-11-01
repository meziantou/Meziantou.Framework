namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for numeric types.
/// </summary>
/// <example>
/// <code>
/// int value = -5;
/// int result = value.MakeSameSignAs(10); // result = 5
/// string ordinal = NumberExtensions.ToEnglishOrdinal(21); // "21st"
/// string invariant = 1234.ToStringInvariant(); // "1234"
/// </code>
/// </example>
public static class NumberExtensions
{
    /// <summary>Returns the number with the same sign as the specified value.</summary>
    public static decimal MakeSameSignAs(this decimal number, decimal sign)
    {
        return Math.Abs(number) * Math.Sign(sign);
    }

    /// <summary>Returns the number with the same sign as the specified value.</summary>
    public static int MakeSameSignAs(this int number, int sign)
    {
        return Math.Abs(number) * Math.Sign(sign);
    }

    /// <summary>Returns the number with the same sign as the specified value.</summary>
    public static long MakeSameSignAs(this long number, long sign)
    {
        return Math.Abs(number) * Math.Sign(sign);
    }

    /// <summary>Returns the number with the same sign as the specified value.</summary>
    public static float MakeSameSignAs(this float number, float sign)
    {
        return MathF.CopySign(number, sign);
    }

    /// <summary>Returns the number with the same sign as the specified value.</summary>
    public static double MakeSameSignAs(this double number, double sign)
    {
        return Math.CopySign(number, sign);
    }

    /// <summary>Converts an integer to its English ordinal representation (e.g., 1st, 2nd, 3rd).</summary>
    public static string ToEnglishOrdinal(int num)
    {
        return ToEnglishOrdinal(num, CultureInfo.CurrentCulture);
    }

    /// <summary>Converts an integer to its English ordinal representation using the specified format provider.</summary>
    public static string ToEnglishOrdinal(int num, IFormatProvider formatProvider)
    {
        if (num <= 0)
            return num.ToString(formatProvider);

        return (num % 100) switch
        {
            11 or 12 or 13 => string.Format(formatProvider, "{0}th", num),
            _ => (num % 10) switch
            {
                1 => string.Format(formatProvider, "{0}st", num),
                2 => string.Format(formatProvider, "{0}nd", num),
                3 => string.Format(formatProvider, "{0}rd", num),
                _ => string.Format(formatProvider, "{0}th", num),
            },
        };
    }

    /// <summary>Converts an integer to its French ordinal representation (e.g., 1er, 2e, 3e).</summary>
    public static string ToFrenchOrdinal(int num)
    {
        return ToFrenchOrdinal(num, CultureInfo.CurrentCulture);
    }

    /// <summary>Converts an integer to its French ordinal representation using the specified format provider.</summary>
    public static string ToFrenchOrdinal(int num, IFormatProvider formatProvider)
    {
        if (num <= 0)
            return num.ToString(formatProvider);

        return num switch
        {
            1 => string.Format(formatProvider, "{0}er", num),
            _ => string.Format(formatProvider, "{0}e", num),
        };
    }

    /// <summary>Converts the number to its string representation using the invariant culture.</summary>
    public static string ToStringInvariant(this byte number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this byte number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this sbyte number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this sbyte number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this short number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this short number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this ushort number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this ushort number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this int number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this int number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this uint number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this uint number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this long number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this long number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this ulong number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this ulong number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this Half number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this Half number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this float number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this float number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this double number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this double number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this decimal number)
    {
        return ToStringInvariant(number, format: null);
    }

    public static string ToStringInvariant(this decimal number, string? format)
    {
        if (format is not null)
            return number.ToString(format, CultureInfo.InvariantCulture);

        return number.ToString(CultureInfo.InvariantCulture);
    }
}
