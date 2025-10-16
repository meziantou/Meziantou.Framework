namespace Meziantou.Framework;

public static class NumberExtensions
{
    public static decimal MakeSameSignAs(this decimal number, decimal sign)
    {
        return Math.Abs(number) * Math.Sign(sign);
    }

    public static int MakeSameSignAs(this int number, int sign)
    {
        return Math.Abs(number) * Math.Sign(sign);
    }

    public static long MakeSameSignAs(this long number, long sign)
    {
        return Math.Abs(number) * Math.Sign(sign);
    }

    public static float MakeSameSignAs(this float number, float sign)
    {
        return MathF.CopySign(number, sign);
    }

    public static double MakeSameSignAs(this double number, double sign)
    {
        return Math.CopySign(number, sign);
    }

    public static string ToEnglishOrdinal(int num)
    {
        return ToEnglishOrdinal(num, CultureInfo.CurrentCulture);
    }

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

    public static string ToFrenchOrdinal(int num)
    {
        return ToFrenchOrdinal(num, CultureInfo.CurrentCulture);
    }

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
