using System.Diagnostics.Contracts;
using System.Globalization;

namespace Meziantou.Framework;

public static class DateTimeExtensions
{
    [Pure]
#if NETCOREAPP3_1_OR_GREATER
    [Obsolete("Use System.Globalization.ISOWeek", DiagnosticId = "MEZ_NETCORE3_1")]
#elif NET461 || NET462 || NETSTANDARD2_0
#else
#error Platform not supported
#endif
    public static DateTime FirstDateOfWeekIso8601(int year, int weekOfYear, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var jan1 = new DateTime(year, 1, 1);
        var fourthDay = (DayOfWeek)(((int)weekStart + 3) % 7);
        var daysOffset = fourthDay - jan1.DayOfWeek;

        var firstThursday = jan1.AddDays(daysOffset);
        var cal = CultureInfo.CurrentCulture.Calendar;
        var firstWeek = cal.GetWeekOfYear(firstThursday, CalendarWeekRule.FirstFourDayWeek, weekStart);

        var weekNum = weekOfYear;
        if (firstWeek <= 1)
        {
            weekNum--;
        }

        var result = firstThursday.AddDays(weekNum * 7);
        return result.AddDays(-3);
    }

    [Pure]
    public static DateTime StartOfWeek(this DateTime dt)
    {
        return StartOfWeek(dt, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
    }

    [Pure]
    public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
    {
        var diff = dt.DayOfWeek - startOfWeek;
        if (diff < 0)
        {
            diff += 7;
        }

        return dt.AddDays(-1 * diff);
    }

    [Pure]
    public static DateTime StartOfMonth(this DateTime dt)
    {
        return StartOfMonth(dt, keepTime: false);
    }

    [Pure]
    public static DateTime StartOfMonth(this DateTime dt, bool keepTime)
    {
        if (keepTime)
        {
            return dt.AddDays(-dt.Day + 1);
        }

        return new DateTime(dt.Year, dt.Month, 1);
    }

    [Pure]
    public static DateTime EndOfMonth(this DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month));
    }

    [Pure]
    public static DateTime StartOfYear(this DateTime dt)
    {
        return StartOfYear(dt, keepTime: false);
    }

    [Pure]
    public static DateTime StartOfYear(this DateTime dt, bool keepTime)
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

    [Pure]
    public static DateTime TruncateMilliseconds(this DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
    }

    public static string ToStringInvariant(this DateTime dt)
    {
        return dt.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this DateTime dt, string format)
    {
        return dt.ToString(format, CultureInfo.InvariantCulture);
    }

#if NET6_0_OR_GREATER
    public static string ToStringInvariant(this DateOnly date)
    {
        return date.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this DateOnly date, string format)
    {
        return date.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this TimeOnly time)
    {
        return time.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToStringInvariant(this TimeOnly time, string format)
    {
        return time.ToString(format, CultureInfo.InvariantCulture);
    }
#elif NET5_0 || NETSTANDARD2_0
#else
#error Platform not supported
#endif
}
