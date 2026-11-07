using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The calendars of 12 months of 30 days and an epagomenal 13th month of 5 or 6 days: "coptic", "ethiopic" and
/// "ethioaa" (calendrical_calculations::coptic and ethiopian of ICU4X).
/// </summary>
internal sealed class CopticBasedTemporalCalendar(string id, TemporalEra[] eras, long epoch, long yearOffset) : NonIsoTemporalCalendar(id, eras)
{
    // The rata die of the day before the first day of the year 1 (for the year offset 0)
    private readonly long _epoch = epoch;

    // The arithmetic year minus the year of the calendar whose epoch is _epoch
    private readonly long _yearOffset = yearOffset;

    /// <summary>The Coptic calendar: the epoch is 0284-08-29 (Julian).</summary>
    public static CopticBasedTemporalCalendar CreateCoptic() => new("coptic", [new TemporalEra("am", alias: null, TemporalEraKind.Epoch)], 103_605, 0);

    /// <summary>The Ethiopian calendar, Amete Mihret era: the epoch is 0008-08-29 (Julian); the years before 1 are in the Amete Alem era.</summary>
    public static CopticBasedTemporalCalendar CreateEthiopic() => new("ethiopic", [new TemporalEra("am", alias: null, TemporalEraKind.Epoch), new TemporalEra("aa", alias: null, TemporalEraKind.Offset, -5499)], 2_796, 0);

    /// <summary>The Ethiopian calendar, Amete Alem era: the year 5501 is the year 1 of the Amete Mihret era.</summary>
    public static CopticBasedTemporalCalendar CreateEthioaa() => new("ethioaa", [new TemporalEra("aa", alias: null, TemporalEraKind.Epoch)], 2_796, 5_500);

    protected override int MaxMonthNumber => 13;

    public override CalendarYmd FromRataDie(long rataDie)
    {
        var year = TemporalMath.FloorDivide((4 * (rataDie - _epoch)) + 1463, 1461);
        var month = (int)((rataDie - ToRataDieCore(year, 1, 1)) / 30) + 1;
        var day = (int)(rataDie + 1 - ToRataDieCore(year, month, 1));
        return new CalendarYmd(year + _yearOffset, month, day);
    }

    public override long ToRataDie(long year, int month, int day) => ToRataDieCore(year - _yearOffset, month, day);

    private long ToRataDieCore(long year, int month, int day) => _epoch - 1 + (365 * (year - 1)) + TemporalMath.FloorDivide(year, 4) + (30 * (month - 1)) + day;

    public override int MonthsInYear(long year) => 13;

    public override int DaysInMonth(long year, int month)
    {
        if (month <= 12)
            return 30;

        return InLeapYear(year) ? 6 : 5;
    }

    public override int DaysInYear(long year) => InLeapYear(year) ? 366 : 365;

    public override bool InLeapYear(long year) => TemporalMath.FloorModulo(year - _yearOffset, 4) == 3;

    protected override int MaxDaysInMonth(MonthCodeValue monthCode) => monthCode.Number <= 12 ? 30 : 6;

    protected override long MonthsBetweenYears(long fromYear, long toYear) => 13 * (toYear - fromYear);

    protected override (long Year, int Month) AddMonths(long year, int month, long months)
    {
        if (Math.Abs(months) > 2 * 13 * MaxYear)
            return ThrowOutOfRange<(long, int)>();

        var monthIndex = month - 1 + months;
        return (CheckYear(year + TemporalMath.FloorDivide(monthIndex, 13)), (int)TemporalMath.FloorModulo(monthIndex, 13) + 1);
    }

    protected override (TemporalEra Era, long EraYear) GetEra(CalendarYmd date, long rataDie)
    {
        // Ethiopic: the years before the Amete Mihret era are in the Amete Alem era
        var era = date.Year > 0 || Eras.Length == 1 ? Eras[0] : Eras[1];
        return (era, era.ToEraYear(date.Year));
    }
}

/// <summary>
/// The Indian national calendar ("indian"): the year starts on the 81st day of the ISO year 78 years later (March 22,
/// March 21 in a leap year), the first month has 31 days in the ISO leap years (icu_calendar::cal::indian of ICU4X).
/// </summary>
internal sealed class IndianTemporalCalendar() : NonIsoTemporalCalendar("indian", [new TemporalEra("shaka", alias: null, TemporalEraKind.Epoch)])
{
    private const int YearOffset = 78;
    private const int DayOffset = 80;

    private static long NewYear(long year) => IsoToRataDie(year + YearOffset, 1, 1) + DayOffset;

    public override CalendarYmd FromRataDie(long rataDie)
    {
        var year = IsoYear(rataDie) - YearOffset;
        if (rataDie < NewYear(year))
        {
            year--;
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

    public override int MonthsInYear(long year) => 12;

    public override int DaysInMonth(long year, int month) => month switch
    {
        1 => InLeapYear(year) ? 31 : 30,
        <= 6 => 31,
        _ => 30,
    };

    public override int DaysInYear(long year) => InLeapYear(year) ? 366 : 365;

    public override bool InLeapYear(long year) => CalendarMath.IsLeapYear(year + YearOffset);

    protected override int MaxDaysInMonth(MonthCodeValue monthCode) => monthCode.Number <= 6 ? 31 : 30;

    protected override long MonthsBetweenYears(long fromYear, long toYear) => 12 * (toYear - fromYear);

    protected override (long Year, int Month) AddMonths(long year, int month, long months)
    {
        if (Math.Abs(months) > 2 * 13 * MaxYear)
            return ThrowOutOfRange<(long, int)>();

        var monthIndex = month - 1 + months;
        return (CheckYear(year + TemporalMath.FloorDivide(monthIndex, 12)), (int)TemporalMath.FloorModulo(monthIndex, 12) + 1);
    }
}

/// <summary>
/// The Persian (Solar Hijri) calendar ("persian"): the 33-year rule with the corrections of the astronomical calendar
/// (calendrical_calculations::persian "fast" functions of ICU4X).
/// </summary>
internal sealed class PersianTemporalCalendar() : NonIsoTemporalCalendar("persian", [new TemporalEra("ap", alias: null, TemporalEraKind.Epoch)])
{
    // The rata die of 0622-03-19 (Julian)
    private const long Epoch = 226_896;

    // The years that are not leap years while they are by the 33-year rule; the following year is a leap year while it
    // is not by the 33-year rule
    private static ReadOnlySpan<short> NonLeapCorrection =>
    [
        1502, 1601, 1634, 1667, 1700, 1733, 1766, 1799, 1832, 1865, 1898, 1931, 1964, 1997, 2030, 2059,
        2063, 2096, 2129, 2158, 2162, 2191, 2195, 2224, 2228, 2257, 2261, 2290, 2294, 2323, 2327, 2356,
        2360, 2389, 2393, 2422, 2426, 2455, 2459, 2488, 2492, 2521, 2525, 2554, 2558, 2587, 2591, 2620,
        2624, 2653, 2657, 2686, 2690, 2719, 2723, 2748, 2752, 2756, 2781, 2785, 2789, 2818, 2822, 2847,
        2851, 2855, 2880, 2884, 2888, 2913, 2917, 2921, 2946, 2950, 2954, 2979, 2983, 2987,
    ];

    private static bool IsNonLeapCorrection(long year) => year >= NonLeapCorrection[0] && year <= NonLeapCorrection[^1] && NonLeapCorrection.BinarySearch((short)year) >= 0;

    private static long NewYear(long year)
    {
        var newYear = Epoch - 1 + (365 * (year - 1)) + TemporalMath.FloorDivide((8 * year) + 21, 33);
        if (IsNonLeapCorrection(year - 1))
        {
            newYear--;
        }

        return newYear;
    }

    public override CalendarYmd FromRataDie(long rataDie)
    {
        var year = 1 + TemporalMath.FloorDivide((33 * (rataDie - Epoch + 1)) + 3, 12053);
        while (rataDie < NewYear(year))
        {
            year--;
        }

        while (rataDie >= NewYear(year + 1))
        {
            year++;
        }

        var dayOfYear = (int)(rataDie - NewYear(year)) + 1;
        var month = dayOfYear <= 186 ? ((dayOfYear - 1) / 31) + 1 : ((dayOfYear - 7) / 30) + 1;
        var day = (int)(rataDie - ToRataDie(year, month, 1)) + 1;
        return new CalendarYmd(year, month, day);
    }

    public override long ToRataDie(long year, int month, int day) => NewYear(year) - 1 + (month <= 7 ? 31 * (month - 1) : (30 * (month - 1)) + 6) + day;

    public override int MonthsInYear(long year) => 12;

    public override int DaysInMonth(long year, int month) => month switch
    {
        <= 6 => 31,
        <= 11 => 30,
        _ => InLeapYear(year) ? 30 : 29,
    };

    public override int DaysInYear(long year) => InLeapYear(year) ? 366 : 365;

    public override bool InLeapYear(long year)
    {
        if (IsNonLeapCorrection(year))
            return false;

        if (IsNonLeapCorrection(year - 1))
            return true;

        return TemporalMath.FloorModulo((25 * year) + 11, 33) < 8;
    }

    protected override int MaxDaysInMonth(MonthCodeValue monthCode) => monthCode.Number <= 6 ? 31 : 30;

    protected override long MonthsBetweenYears(long fromYear, long toYear) => 12 * (toYear - fromYear);

    protected override (long Year, int Month) AddMonths(long year, int month, long months)
    {
        if (Math.Abs(months) > 2 * 13 * MaxYear)
            return ThrowOutOfRange<(long, int)>();

        var monthIndex = month - 1 + months;
        return (CheckYear(year + TemporalMath.FloorDivide(monthIndex, 12)), (int)TemporalMath.FloorModulo(monthIndex, 12) + 1);
    }
}
