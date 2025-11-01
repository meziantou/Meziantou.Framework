namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="DateTime"/> to perform common date manipulations.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>Gets the first date of a specific ISO 8601 week.</summary>
    [Obsolete("Use System.Globalization.ISOWeek", DiagnosticId = "MEZ_NETCORE3_1")]
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

    /// <summary>Returns the start of the week for the specified date using the current culture's first day of week.</summary>
    public static DateTime StartOfWeek(this DateTime dt)
    {
        return StartOfWeek(dt, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
    }

    /// <summary>Returns the start of the week for the specified date using the specified first day of week.</summary>
    public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
    {
        var diff = dt.DayOfWeek - startOfWeek;
        if (diff < 0)
        {
            diff += 7;
        }

        return dt.AddDays(-1 * diff);
    }

    /// <summary>Returns the first day of the month for the specified date at midnight.</summary>
    public static DateTime StartOfMonth(this DateTime dt)
    {
        return StartOfMonth(dt, keepTime: false);
    }

    /// <summary>Returns the first day of the month for the specified date, optionally keeping the time component.</summary>
    public static DateTime StartOfMonth(this DateTime dt, bool keepTime)
    {
        if (keepTime)
        {
            return dt.AddDays(-dt.Day + 1);
        }

        return new DateTime(dt.Year, dt.Month, 1);
    }

    /// <summary>Returns the last day of the month for the specified date at midnight.</summary>
    public static DateTime EndOfMonth(this DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month));
    }

    /// <summary>Returns the first day of the year for the specified date at midnight.</summary>
    public static DateTime StartOfYear(this DateTime dt)
    {
        return StartOfYear(dt, keepTime: false);
    }

    /// <summary>Returns the first day of the year for the specified date, optionally keeping the time component.</summary>
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

    /// <summary>Returns a new DateTime with the milliseconds component set to zero.</summary>
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
}
