using System.Globalization;

namespace Meziantou.Framework.Scheduling;

internal static class Extensions
{
    public static string? GetValue(this IDictionary<string, string> dict, string key, string? defaultValue)
    {
        ArgumentNullException.ThrowIfNull(dict);

        if (dict.TryGetValue(key, out var value))
            return value;

        return defaultValue;
    }

    public static int GetValue(this IDictionary<string, string> dict, string key, int defaultValue)
    {
        ArgumentNullException.ThrowIfNull(dict);

        if (dict.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;

        return defaultValue;
    }

    public static int? GetValue(this IDictionary<string, string> dict, string key, int? defaultValue)
    {
        ArgumentNullException.ThrowIfNull(dict);

        if (dict.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;

        return defaultValue;
    }

    public static Frequency GetValue(this IDictionary<string, string> dict, string key, Frequency defaultValue)
    {
        ArgumentNullException.ThrowIfNull(dict);

        if (dict.TryGetValue(key, out var value) && Enum.TryParse<Frequency>(value, ignoreCase: true, out var enumValue))
            return enumValue;

        return defaultValue;
    }

    public static DateTime StartOfWeek(DateTime dt)
    {
        return StartOfWeek(dt, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
    }

    public static DateTime StartOfWeek(DateTime dt, DayOfWeek startOfWeek)
    {
        var diff = dt.DayOfWeek - startOfWeek;
        if (diff < 0)
        {
            diff += 7;
        }

        return dt.AddDays(-1 * diff);
    }

    public static DateTime StartOfMonth(DateTime dt)
    {
        return StartOfMonth(dt, keepTime: false);
    }

    public static DateTime StartOfMonth(DateTime dt, bool keepTime)
    {
        if (keepTime)
        {
            return dt.AddDays(-dt.Day + 1);
        }

        return new DateTime(dt.Year, dt.Month, 1);
    }

    public static DateTime StartOfYear(DateTime dt)
    {
        return StartOfYear(dt, keepTime: false);
    }

    public static DateTime StartOfYear(DateTime dt, bool keepTime)
    {
        if (keepTime)
        {
            return dt.AddDays(-dt.DayOfYear + 1);
        }
        else
        {
            return new DateTime(dt.Year, 1, 1);
        }
    }

    public static string ToEnglishOrdinal(int num)
    {
        if (num <= 0)
            return num.ToString(CultureInfo.CurrentCulture);

        return num.ToString(CultureInfo.CurrentCulture) + (num % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (num % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            },
        };
    }

    public static string ToFrenchOrdinal(int num)
    {
        if (num <= 0)
            return num.ToString(CultureInfo.CurrentCulture);

        return num.ToString(CultureInfo.CurrentCulture) + num switch
        {
            1 => "er",
            _ => "e",
        };
    }
}
