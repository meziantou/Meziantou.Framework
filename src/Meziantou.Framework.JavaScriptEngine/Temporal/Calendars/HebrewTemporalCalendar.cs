namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The Hebrew calendar ("hebrew"), with Tishrei as the first month: the leap month Adar I ("M05L") is the 6th month of the
/// 7 leap years of the 19-year cycle (the arithmetic of Calendrical Calculations, as calendrical_calculations::hebrew).
/// </summary>
internal sealed class HebrewTemporalCalendar() : NonIsoTemporalCalendar("hebrew", [new TemporalEra("am", alias: null, TemporalEraKind.Epoch)])
{
    // The rata die of -3761-10-07 (Julian)
    private const long Epoch = -1_373_427;

    private static bool IsLeapYear(long year) => TemporalMath.FloorModulo((7 * year) + 1, 19) < 7;

    /// <summary>Gets the months before the year (since the epoch).</summary>
    private static long MonthsElapsed(long year) => TemporalMath.FloorDivide((235 * year) - 234, 19);

    private static long CalendarElapsedDays(long year)
    {
        var monthsElapsed = MonthsElapsed(year);
        var partsElapsed = 12084 + (13753 * monthsElapsed);
        var days = (29 * monthsElapsed) + TemporalMath.FloorDivide(partsElapsed, 25920);
        return TemporalMath.FloorModulo(3 * (days + 1), 7) < 3 ? days + 1 : days;
    }

    private static long NewYear(long year)
    {
        var elapsed = CalendarElapsedDays(year);
        var correction = 0;
        if (CalendarElapsedDays(year + 1) - elapsed == 356)
        {
            correction = 2;
        }
        else if (elapsed - CalendarElapsedDays(year - 1) == 382)
        {
            correction = 1;
        }

        return Epoch + elapsed + correction;
    }

    public override CalendarYmd FromRataDie(long rataDie)
    {
        var year = TemporalMath.FloorDivide((rataDie - Epoch) * 98_496, 35_975_351) + 1;
        while (rataDie < NewYear(year))
        {
            year--;
        }

        while (rataDie >= NewYear(year + 1))
        {
            year++;
        }

        var dayOfYear = (int)(rataDie - NewYear(year));
        var month = 1;
        while (dayOfYear >= DaysInMonth(year, month))
        {
            dayOfYear -= DaysInMonth(year, month);
            month++;
        }

        return new CalendarYmd(year, month, dayOfYear + 1);
    }

    public override long ToRataDie(long year, int month, int day)
    {
        var dayOfYear = day - 1;
        for (var i = 1; i < month; i++)
        {
            dayOfYear += DaysInMonth(year, i);
        }

        return NewYear(year) + dayOfYear;
    }

    public override int MonthsInYear(long year) => IsLeapYear(year) ? 13 : 12;

    public override int DaysInMonth(long year, int month)
    {
        switch (month)
        {
            case 1:
            case 5:
                return 30;
            case 2:
                // Heshvan has 30 days in the complete years (355 or 385 days)
                return DaysInYear(year) % 10 == 5 ? 30 : 29;
            case 3:
                // Kislev has 29 days in the deficient years (353 or 383 days)
                return DaysInYear(year) % 10 == 3 ? 29 : 30;
            case 4:
                return 29;
        }

        if (IsLeapYear(year))
        {
            // Adar I (30 days), Adar II (29 days), then Nisan (30 days)...
            if (month == 6)
                return 30;

            return month % 2 == 0 ? 30 : 29;
        }

        // Adar (29 days), then Nisan (30 days)...
        return month % 2 == 0 ? 29 : 30;
    }

    public override int DaysInYear(long year) => (int)(NewYear(year + 1) - NewYear(year));

    public override bool InLeapYear(long year) => IsLeapYear(year);

    protected override int LeapMonth(long year) => IsLeapYear(year) ? 6 : 0;

    protected override bool IsValidLeapMonthNumber(int number) => number == 5;

    protected override bool LeapMonthSkipsForward => true;

    protected override int MaxDaysInMonth(MonthCodeValue monthCode)
    {
        if (monthCode.IsLeap)
            return 30;

        return monthCode.Number switch
        {
            1 or 2 or 3 or 5 => 30,
            4 or 6 => 29,
            var number => number % 2 == 1 ? 30 : 29,
        };
    }

    protected override long MonthsBetweenYears(long fromYear, long toYear) => MonthsElapsed(toYear) - MonthsElapsed(fromYear);

    protected override (long Year, int Month) AddMonths(long year, int month, long months)
    {
        if (Math.Abs(months) > 2 * 13 * MaxYear)
            return ThrowOutOfRange<(long, int)>();

        var monthIndex = MonthsElapsed(year) + month - 1 + months;
        var resultYear = CheckYear(TemporalMath.FloorDivide(19 * monthIndex, 235) + 1);
        while (MonthsElapsed(resultYear) > monthIndex)
        {
            resultYear--;
        }

        while (MonthsElapsed(resultYear + 1) <= monthIndex)
        {
            resultYear++;
        }

        return (CheckYear(resultYear), (int)(monthIndex - MonthsElapsed(resultYear)) + 1);
    }
}
