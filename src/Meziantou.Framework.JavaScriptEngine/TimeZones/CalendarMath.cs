namespace Meziantou.Framework.JavaScriptEngine.TimeZones;

/// <summary>Proleptic Gregorian calendar arithmetic on day numbers (days since 1970-01-01), valid for any year that fits the arithmetic.</summary>
internal static class CalendarMath
{
    public const long SecondsPerDay = 86_400;

    /// <summary>Gets the number of days from 1970-01-01 to the date (the day may exceed the length of the month: the arithmetic is linear).</summary>
    public static long DaysFromCivil(long year, int month, long day)
    {
        // https://howardhinnant.github.io/date_algorithms.html#days_from_civil
        var y = month <= 2 ? year - 1 : year;
        var era = FloorDivide(y, 400);
        var yoe = y - (era * 400);
        var mp = (month + 9) % 12;
        var doy = (((153 * mp) + 2) / 5) + day - 1;
        var doe = (yoe * 365) + (yoe / 4) - (yoe / 100) + doy;
        return (era * 146_097) + doe - 719_468;
    }

    /// <summary>Gets the date of a day number.</summary>
    public static void CivilFromDays(long days, out long year, out int month, out int day)
    {
        // https://howardhinnant.github.io/date_algorithms.html#civil_from_days
        var z = days + 719_468;
        var era = FloorDivide(z, 146_097);
        var doe = z - (era * 146_097);
        var yoe = (doe - (doe / 1460) + (doe / 36_524) - (doe / 146_096)) / 365;
        var doy = doe - ((365 * yoe) + (yoe / 4) - (yoe / 100));
        var mp = ((5 * doy) + 2) / 153;
        day = (int)(doy - (((153 * mp) + 2) / 5) + 1);
        month = (int)(mp < 10 ? mp + 3 : mp - 9);
        year = (yoe + (era * 400)) + (month <= 2 ? 1 : 0);
    }

    /// <summary>Gets the year of a day number.</summary>
    public static long YearFromDays(long days)
    {
        CivilFromDays(days, out var year, out _, out _);
        return year;
    }

    /// <summary>Gets the day of the week of a day number (0 = Sunday).</summary>
    public static int DayOfWeek(long days) => (int)FloorModulo(days + 4, 7);

    public static bool IsLeapYear(long year) => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

    public static int DaysInMonth(long year, int month)
    {
        return month switch
        {
            2 => IsLeapYear(year) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };
    }

    public static long FloorDivide(long value, long divisor)
    {
        var quotient = value / divisor;
        if ((value % divisor != 0) && ((value < 0) != (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }

    public static long FloorModulo(long value, long divisor)
    {
        var remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
        {
            remainder += divisor;
        }

        return remainder;
    }
}
