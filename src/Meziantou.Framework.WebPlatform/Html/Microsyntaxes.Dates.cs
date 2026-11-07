using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>Dates and times (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#dates-and-times).</summary>
internal static partial class Microsyntaxes
{
    private const double MillisecondsPerDay = 86_400_000;

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#valid-floating-point-number</summary>
    public static bool IsValidFloatingPointNumber(ReadOnlySpan<char> value)
    {
        var i = 0;
        if (i < value.Length && value[i] == '-')
        {
            i++;
        }

        var digits = 0;
        while (i < value.Length && char.IsAsciiDigit(value[i]))
        {
            i++;
            digits++;
        }

        if (i < value.Length && value[i] == '.')
        {
            i++;
            var fractionDigits = 0;
            while (i < value.Length && char.IsAsciiDigit(value[i]))
            {
                i++;
                fractionDigits++;
            }

            if (fractionDigits == 0)
                return false;

            digits += fractionDigits;
        }

        if (digits == 0)
            return false;

        if (i < value.Length && value[i] is 'e' or 'E')
        {
            i++;
            if (i < value.Length && value[i] is '-' or '+')
            {
                i++;
            }

            var exponentDigits = 0;
            while (i < value.Length && char.IsAsciiDigit(value[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
                return false;
        }

        return i == value.Length;
    }

    public static bool IsLeapYear(int year) => (year % 400 == 0) || (year % 4 == 0 && year % 100 != 0);

    public static int DaysInMonth(int year, int month) => month switch
    {
        2 => IsLeapYear(year) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    // Collects exactly count ASCII digits (or at least count when atLeast is true)
    private static bool TryParseDigits(ReadOnlySpan<char> value, ref int position, int count, bool atLeast, out int result)
    {
        result = 0;
        var start = position;
        while (position < value.Length && char.IsAsciiDigit(value[position]) && (atLeast || position - start < count))
        {
            if (result > 100_000_000)
                return false;

            result = (result * 10) + (value[position] - '0');
            position++;
        }

        return atLeast ? position - start >= count : position - start == count;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-month-component</summary>
    private static bool TryParseMonthComponent(ReadOnlySpan<char> value, ref int position, out int year, out int month)
    {
        month = 0;
        if (!TryParseDigits(value, ref position, 4, atLeast: true, out year) || year <= 0)
            return false;

        if (position >= value.Length || value[position] != '-')
            return false;

        position++;
        return TryParseDigits(value, ref position, 2, atLeast: false, out month) && month is >= 1 and <= 12;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-date-component</summary>
    private static bool TryParseDateComponent(ReadOnlySpan<char> value, ref int position, out int year, out int month, out int day)
    {
        day = 0;
        if (!TryParseMonthComponent(value, ref position, out year, out month))
            return false;

        if (position >= value.Length || value[position] != '-')
            return false;

        position++;
        return TryParseDigits(value, ref position, 2, atLeast: false, out day) && day >= 1 && day <= DaysInMonth(year, month);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-time-component</summary>
    private static bool TryParseTimeComponent(ReadOnlySpan<char> value, ref int position, out int hour, out int minute, out double second)
    {
        minute = 0;
        second = 0;
        if (!TryParseDigits(value, ref position, 2, atLeast: false, out hour) || hour > 23)
            return false;

        if (position >= value.Length || value[position] != ':')
            return false;

        position++;
        if (!TryParseDigits(value, ref position, 2, atLeast: false, out minute) || minute > 59)
            return false;

        if (position < value.Length && value[position] == ':')
        {
            position++;
            var start = position;
            if (!TryParseDigits(value, ref position, 2, atLeast: false, out var wholeSeconds))
                return false;

            if (position < value.Length && value[position] == '.')
            {
                position++;
                var fractionStart = position;
                while (position < value.Length && char.IsAsciiDigit(value[position]))
                {
                    position++;
                }

                // One to three digits of fraction
                if (position - fractionStart is 0 or > 3)
                    return false;
            }

            second = double.Parse(value[start..position], NumberStyles.Float, CultureInfo.InvariantCulture);
            if (wholeSeconds > 59)
                return false;
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-month-string</summary>
    public static bool TryParseMonthString(ReadOnlySpan<char> value, out int year, out int month)
    {
        var position = 0;
        return TryParseMonthComponent(value, ref position, out year, out month) && position == value.Length;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-date-string</summary>
    public static bool TryParseDateString(ReadOnlySpan<char> value, out int year, out int month, out int day)
    {
        var position = 0;
        return TryParseDateComponent(value, ref position, out year, out month, out day) && position == value.Length;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-time-string</summary>
    public static bool TryParseTimeString(ReadOnlySpan<char> value, out int hour, out int minute, out double second)
    {
        var position = 0;
        return TryParseTimeComponent(value, ref position, out hour, out minute, out second) && position == value.Length;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-local-date-and-time-string</summary>
    public static bool TryParseLocalDateAndTimeString(ReadOnlySpan<char> value, out double milliseconds)
    {
        milliseconds = 0;
        var position = 0;
        if (!TryParseDateComponent(value, ref position, out var year, out var month, out var day))
            return false;

        if (position >= value.Length || value[position] is not ('T' or ' '))
            return false;

        position++;
        if (!TryParseTimeComponent(value, ref position, out var hour, out var minute, out var second) || position != value.Length)
            return false;

        milliseconds = DateToMilliseconds(year, month, day) + TimeToMilliseconds(hour, minute, second);
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#parse-a-week-string</summary>
    public static bool TryParseWeekString(ReadOnlySpan<char> value, out int year, out int week)
    {
        week = 0;
        var position = 0;
        if (!TryParseDigits(value, ref position, 4, atLeast: true, out year) || year <= 0)
            return false;

        if (position + 1 >= value.Length || value[position] != '-' || value[position + 1] != 'W')
            return false;

        position += 2;
        if (!TryParseDigits(value, ref position, 2, atLeast: false, out week) || position != value.Length)
            return false;

        return week >= 1 && week <= GetWeeksInYear(year);
    }

    /// <summary>The number of weeks of a week-year: 53 when the year starts on a Thursday, or on a Wednesday in a leap year.</summary>
    public static int GetWeeksInYear(int year)
    {
        var dayOfWeek = DayOfWeekOfJanuaryFirst(year);
        return dayOfWeek == 4 || (dayOfWeek == 3 && IsLeapYear(year)) ? 53 : 52;
    }

    // 0 = Sunday ... 6 = Saturday
    private static int DayOfWeekOfJanuaryFirst(int year)
    {
        var days = DaysFromEpoch(year, 1, 1);
        var result = (int)((days + 4) % 7);
        return result < 0 ? result + 7 : result;
    }

    /// <summary>The number of days between 1970-01-01 and the date (proleptic Gregorian calendar).</summary>
    public static long DaysFromEpoch(int year, int month, int day)
    {
        // https://howardhinnant.github.io/date_algorithms.html#days_from_civil
        long y = month <= 2 ? year - 1 : year;
        var era = (y >= 0 ? y : y - 399) / 400;
        var yoe = y - (era * 400);
        var mp = (month + 9) % 12;
        var doy = ((153 * mp) + 2) / 5 + day - 1;
        var doe = (yoe * 365) + (yoe / 4) - (yoe / 100) + doy;
        return (era * 146097) + doe - 719468;
    }

    /// <summary>The date of a number of days since 1970-01-01.</summary>
    public static (int Year, int Month, int Day) DateFromDays(long days)
    {
        days += 719468;
        var era = (days >= 0 ? days : days - 146096) / 146097;
        var doe = days - (era * 146097);
        var yoe = (doe - (doe / 1460) + (doe / 36524) - (doe / 146096)) / 365;
        var y = yoe + (era * 400);
        var doy = doe - ((365 * yoe) + (yoe / 4) - (yoe / 100));
        var mp = ((5 * doy) + 2) / 153;
        var d = doy - (((153 * mp) + 2) / 5) + 1;
        var m = mp < 10 ? mp + 3 : mp - 9;
        return ((int)(m <= 2 ? y + 1 : y), (int)m, (int)d);
    }

    public static double DateToMilliseconds(int year, int month, int day) => DaysFromEpoch(year, month, day) * MillisecondsPerDay;

    public static double TimeToMilliseconds(int hour, int minute, double second) => (((hour * 60) + minute) * 60_000) + Math.Round(second * 1000, MidpointRounding.ToEven);

    /// <summary>The milliseconds since the epoch of the Monday 00:00 UTC of the week.</summary>
    public static double WeekToMilliseconds(int year, int week)
    {
        // Week 1 is the week that contains the first Thursday of the year
        var januaryFirst = DaysFromEpoch(year, 1, 1);
        var dayOfWeek = DayOfWeekOfJanuaryFirst(year);
        var mondayOfWeek1 = januaryFirst - ((dayOfWeek + 6) % 7) + (dayOfWeek is >= 5 or 0 ? 7 : 0);
        return (mondayOfWeek1 + ((week - 1) * 7)) * MillisecondsPerDay;
    }

    /// <summary>Returns the week of a number of milliseconds since the epoch.</summary>
    public static (int Year, int Week) WeekFromMilliseconds(double milliseconds)
    {
        var days = (long)Math.Floor(milliseconds / MillisecondsPerDay);

        // The Thursday of the same week determines the week-year
        var dayOfWeek = (int)(((days + 4) % 7 + 7) % 7);
        var thursday = days - ((dayOfWeek + 6) % 7) + 3;
        var (year, _, _) = DateFromDays(thursday);
        var week = (int)((thursday - DaysFromEpoch(year, 1, 1)) / 7) + 1;
        return (year, week);
    }

    public static string FormatYear(int year) => year.ToString("0000", CultureInfo.InvariantCulture);

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#valid-date-string</summary>
    public static string FormatDate(int year, int month, int day) => FormatYear(year) + "-" + month.ToString("00", CultureInfo.InvariantCulture) + "-" + day.ToString("00", CultureInfo.InvariantCulture);

    public static string FormatMonth(int year, int month) => FormatYear(year) + "-" + month.ToString("00", CultureInfo.InvariantCulture);

    public static string FormatWeek(int year, int week) => FormatYear(year) + "-W" + week.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>The shortest valid time string for a number of milliseconds since midnight (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#valid-time-string).</summary>
    public static string FormatTime(double milliseconds) => FormatTime(milliseconds, shortest: false);

    /// <summary>Formats a time; <paramref name="shortest"/> omits the trailing zeros of the fraction of the seconds (the shortest possible string).</summary>
    private static string FormatTime(double milliseconds, bool shortest)
    {
        var total = (long)Math.Round(milliseconds, MidpointRounding.ToEven);
        var ms = (int)(total % 1000);
        var totalSeconds = total / 1000;
        var seconds = (int)(totalSeconds % 60);
        var minutes = (int)(totalSeconds / 60 % 60);
        var hours = (int)(totalSeconds / 3600 % 24);
        var result = hours.ToString("00", CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture);
        if (seconds != 0 || ms != 0)
        {
            result += ":" + seconds.ToString("00", CultureInfo.InvariantCulture);
            if (ms != 0)
            {
                var fraction = ms.ToString("000", CultureInfo.InvariantCulture);
                result += "." + (shortest ? fraction.TrimEnd('0') : fraction);
            }
        }

        return result;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#valid-normalised-local-date-and-time-string: the time is the shortest possible string.</summary>
    public static string FormatLocalDateAndTime(double milliseconds)
    {
        var days = (long)Math.Floor(milliseconds / MillisecondsPerDay);
        var (year, month, day) = DateFromDays(days);
        return FormatDate(year, month, day) + "T" + FormatTime(milliseconds - (days * MillisecondsPerDay), shortest: true);
    }
}
