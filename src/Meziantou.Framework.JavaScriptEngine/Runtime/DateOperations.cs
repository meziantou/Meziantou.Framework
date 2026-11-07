using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The time model of the Date objects (https://tc39.es/ecma262/#sec-time-values-and-time-range), on time values in milliseconds.</summary>
internal static class DateOperations
{
    public const double MsPerDay = 86_400_000;
    public const double MsPerHour = 3_600_000;
    public const double MsPerMinute = 60_000;
    public const double MsPerSecond = 1000;

    /// <summary>The largest absolute time value (100,000,000 days).</summary>
    public const double MaxTimeValue = 8.64e15;

    private static readonly string[] WeekdayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly int[] CumulativeDays = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];

    public static double Day(double t) => Math.Floor(t / MsPerDay);

    public static double TimeWithinDay(double t) => Modulo(t, MsPerDay);

    /// <summary>The largest integral year y such that TimeFromYear(y) &lt;= t (t is a time value or a local time).</summary>
    public static double YearFromTime(double t) => CalendarMath.YearFromDays((long)Day(t));

    /// <summary>The month, from 0 (January) to 11.</summary>
    public static int MonthFromTime(double t)
    {
        CalendarMath.CivilFromDays((long)Day(t), out _, out var month, out _);
        return month - 1;
    }

    /// <summary>The day of the month, from 1 to 31.</summary>
    public static int DateFromTime(double t)
    {
        CalendarMath.CivilFromDays((long)Day(t), out _, out _, out var day);
        return day;
    }

    /// <summary>The day of the week, from 0 (Sunday) to 6.</summary>
    public static int WeekDay(double t) => (int)Modulo(Day(t) + 4, 7);

    public static int HourFromTime(double t) => (int)Modulo(Math.Floor(t / MsPerHour), 24);

    public static int MinFromTime(double t) => (int)Modulo(Math.Floor(t / MsPerMinute), 60);

    public static int SecFromTime(double t) => (int)Modulo(Math.Floor(t / MsPerSecond), 60);

    public static int MsFromTime(double t) => (int)Modulo(t, MsPerSecond);

    /// <summary>MakeTime(hour, min, sec, ms)</summary>
    public static double MakeTime(double hour, double min, double sec, double ms)
    {
        if (!double.IsFinite(hour) || !double.IsFinite(min) || !double.IsFinite(sec) || !double.IsFinite(ms))
            return double.NaN;

        var h = Truncate(hour);
        var m = Truncate(min);
        var s = Truncate(sec);
        var milli = Truncate(ms);
        return ((h * MsPerHour) + (m * MsPerMinute)) + (s * MsPerSecond) + milli;
    }

    /// <summary>MakeDay(year, month, date)</summary>
    public static double MakeDay(double year, double month, double date)
    {
        if (!double.IsFinite(year) || !double.IsFinite(month) || !double.IsFinite(date))
            return double.NaN;

        var y = Truncate(year);
        var m = Truncate(month);
        var dt = Truncate(date);
        var ym = y + Math.Floor(m / 12);
        if (!double.IsFinite(ym))
            return double.NaN;

        var mn = (int)Modulo(m, 12);

        // The day of the first day of the month mn of the year ym
        var day = DayFromYear(ym) + CumulativeDays[mn] + (mn >= 2 && InLeapYear(ym) ? 1 : 0);
        if (!double.IsFinite(day))
            return double.NaN;

        return day + dt - 1;
    }

    /// <summary>MakeDate(day, time)</summary>
    public static double MakeDate(double day, double time)
    {
        if (!double.IsFinite(day) || !double.IsFinite(time))
            return double.NaN;

        var tv = (day * MsPerDay) + time;
        return double.IsFinite(tv) ? tv : double.NaN;
    }

    /// <summary>MakeFullYear(year)</summary>
    public static double MakeFullYear(double year)
    {
        if (double.IsNaN(year))
            return double.NaN;

        var truncated = Truncate(year);
        return truncated is >= 0 and <= 99 ? 1900 + truncated : year;
    }

    /// <summary>TimeClip(time)</summary>
    public static double TimeClip(double time)
    {
        if (!double.IsFinite(time) || Math.Abs(time) > MaxTimeValue)
            return double.NaN;

        return Truncate(time);
    }

    /// <summary>LocalTime(t): t is a finite time value.</summary>
    public static double LocalTime(double t, NamedTimeZone timeZone) => t + GetOffsetMilliseconds(t, timeZone);

    /// <summary>The offset of the time zone at the time value t, in milliseconds.</summary>
    public static double GetOffsetMilliseconds(double t, NamedTimeZone timeZone) => timeZone.GetOffsetSeconds((long)Math.Floor(t / MsPerSecond)) * MsPerSecond;

    /// <summary>UTC(t): converts a local time to a time value.</summary>
    public static double Utc(double t, NamedTimeZone timeZone)
    {
        if (!double.IsFinite(t))
            return double.NaN;

        // The offsets are less than a day: the result of a larger value is clipped to NaN anyway
        if (Math.Abs(t) > MaxTimeValue + (2 * MsPerDay))
            return double.NaN;

        var offset = timeZone.GetOffsetSecondsForLocalTime((long)Math.Floor(t / MsPerSecond));
        return t - (offset * MsPerSecond);
    }

    /// <summary>ToDateString(tv): the format of Date.prototype.toString.</summary>
    public static string ToDateString(double tv, TimeZoneIdentifier timeZone)
    {
        if (double.IsNaN(tv))
            return "Invalid Date";

        var t = LocalTime(tv, timeZone.Zone);
        var sb = new StringBuilder(64);
        AppendDateString(sb, t);
        sb.Append(' ');
        AppendTimeString(sb, t);
        AppendTimeZoneString(sb, tv, timeZone);
        return sb.ToString();
    }

    /// <summary>The format of Date.prototype.toDateString.</summary>
    public static string ToDateOnlyString(double tv, TimeZoneIdentifier timeZone)
    {
        if (double.IsNaN(tv))
            return "Invalid Date";

        var sb = new StringBuilder(16);
        AppendDateString(sb, LocalTime(tv, timeZone.Zone));
        return sb.ToString();
    }

    /// <summary>The format of Date.prototype.toTimeString.</summary>
    public static string ToTimeOnlyString(double tv, TimeZoneIdentifier timeZone)
    {
        if (double.IsNaN(tv))
            return "Invalid Date";

        var sb = new StringBuilder(48);
        AppendTimeString(sb, LocalTime(tv, timeZone.Zone));
        AppendTimeZoneString(sb, tv, timeZone);
        return sb.ToString();
    }

    /// <summary>The format of Date.prototype.toUTCString: "Tue, 01 Feb 2022 00:00:00 GMT".</summary>
    public static string ToUtcString(double tv)
    {
        if (double.IsNaN(tv))
            return "Invalid Date";

        var sb = new StringBuilder(32);
        sb.Append(WeekdayNames[WeekDay(tv)]).Append(", ");
        AppendPadded(sb, DateFromTime(tv), 2);
        sb.Append(' ').Append(MonthNames[MonthFromTime(tv)]).Append(' ');
        AppendYear(sb, YearFromTime(tv));
        sb.Append(' ');
        AppendTimeString(sb, tv);
        return sb.ToString();
    }

    /// <summary>The format of Date.prototype.toISOString (tv is finite): "YYYY-MM-DDTHH:mm:ss.sssZ", the years outside 0-9999 as +YYYYYY or -YYYYYY.</summary>
    public static string ToIsoString(double tv)
    {
        var sb = new StringBuilder(27);
        var year = YearFromTime(tv);
        if (year is >= 0 and <= 9999)
        {
            AppendPadded(sb, (long)year, 4);
        }
        else
        {
            sb.Append(year < 0 ? '-' : '+');
            AppendPadded(sb, (long)Math.Abs(year), 6);
        }

        sb.Append('-');
        AppendPadded(sb, MonthFromTime(tv) + 1, 2);
        sb.Append('-');
        AppendPadded(sb, DateFromTime(tv), 2);
        sb.Append('T');
        AppendPadded(sb, HourFromTime(tv), 2);
        sb.Append(':');
        AppendPadded(sb, MinFromTime(tv), 2);
        sb.Append(':');
        AppendPadded(sb, SecFromTime(tv), 2);
        sb.Append('.');
        AppendPadded(sb, MsFromTime(tv), 3);
        sb.Append('Z');
        return sb.ToString();
    }

    /// <summary>DateString(tv): "Tue Feb 01 2022".</summary>
    private static void AppendDateString(StringBuilder sb, double t)
    {
        sb.Append(WeekdayNames[WeekDay(t)]).Append(' ').Append(MonthNames[MonthFromTime(t)]).Append(' ');
        AppendPadded(sb, DateFromTime(t), 2);
        sb.Append(' ');
        AppendYear(sb, YearFromTime(t));
    }

    /// <summary>TimeString(tv): "00:00:00 GMT".</summary>
    private static void AppendTimeString(StringBuilder sb, double t)
    {
        AppendPadded(sb, HourFromTime(t), 2);
        sb.Append(':');
        AppendPadded(sb, MinFromTime(t), 2);
        sb.Append(':');
        AppendPadded(sb, SecFromTime(t), 2);
        sb.Append(" GMT");
    }

    /// <summary>TimeZoneString(tv): "+0100 (CET)". The name is the abbreviation of the time zone when it is made of letters, its identifier otherwise.</summary>
    private static void AppendTimeZoneString(StringBuilder sb, double tv, TimeZoneIdentifier timeZone)
    {
        var type = timeZone.Zone.GetLocalTimeType((long)Math.Floor(tv / MsPerSecond));
        var offset = type.OffsetSeconds * MsPerSecond;
        sb.Append(offset >= 0 ? '+' : '-');
        var absOffset = Math.Abs(offset);
        AppendPadded(sb, HourFromTime(absOffset), 2);
        AppendPadded(sb, MinFromTime(absOffset), 2);
        sb.Append(" (").Append(IsAlphabetic(type.Abbreviation) ? type.Abbreviation : timeZone.PrimaryIdentifier).Append(')');

        static bool IsAlphabetic(string value)
        {
            if (value.Length == 0)
                return false;

            foreach (var c in value)
            {
                if (!char.IsAsciiLetter(c))
                    return false;
            }

            return true;
        }
    }

    private static void AppendYear(StringBuilder sb, double year)
    {
        if (year < 0)
        {
            sb.Append('-');
        }

        AppendPadded(sb, (long)Math.Abs(year), 4);
    }

    private static void AppendPadded(StringBuilder sb, long value, int width)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        sb.Append('0', Math.Max(0, width - digits.Length)).Append(digits);
    }

    /// <summary>DayFromYear(y) = 365 * (y - 1970) + floor((y - 1969) / 4) - floor((y - 1901) / 100) + floor((y - 1601) / 400).</summary>
    private static double DayFromYear(double y)
    {
        return (365 * (y - 1970)) + Math.Floor((y - 1969) / 4) - Math.Floor((y - 1901) / 100) + Math.Floor((y - 1601) / 400);
    }

    private static bool InLeapYear(double y) => (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;

    /// <summary>ToIntegerOrInfinity of a finite number (truncation, -0 becomes +0).</summary>
    private static double Truncate(double value) => Math.Truncate(value) + 0.0;

    /// <summary>The modulo of the spec (the sign of the divisor), +0 instead of -0.</summary>
    private static double Modulo(double value, double divisor)
    {
        var remainder = value % divisor;
        if (remainder < 0)
        {
            remainder += divisor;
        }

        return remainder + 0.0;
    }
}
