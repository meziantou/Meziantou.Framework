namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The Hijri calendars: "islamic-civil" and "islamic-tbla" (the tabular calendar with the leap years 2, 5, 7, 10, 13,
/// 16, 18, 21, 24, 26 and 29 of the 30-year cycle and the civil or astronomical epoch), and "islamic-umalqura" (the
/// months published by KACST from 1300 AH to 1600 AH, the civil calendar outside) (icu_calendar::cal::hijri of ICU4X).
/// </summary>
internal sealed class IslamicTemporalCalendar(string id, long epoch, bool ummAlQura)
    : NonIsoTemporalCalendar(id, [new TemporalEra("ah", alias: null, TemporalEraKind.Epoch), new TemporalEra("bh", alias: null, TemporalEraKind.Negative)])
{
    // The rata die of 0622-07-16 (Julian), the civil epoch, and of 0622-07-15 (Julian), the astronomical epoch
    private const long FridayEpoch = 227_015;
    private const long ThursdayEpoch = 227_014;

    private readonly long _epoch = epoch;
    private readonly bool _ummAlQura = ummAlQura;

    public static IslamicTemporalCalendar CreateCivil() => new("islamic-civil", FridayEpoch, ummAlQura: false);

    public static IslamicTemporalCalendar CreateTbla() => new("islamic-tbla", ThursdayEpoch, ummAlQura: false);

    public static IslamicTemporalCalendar CreateUmmAlQura() => new("islamic-umalqura", FridayEpoch, ummAlQura: true);

    /// <summary>Gets the packed data of a year of the Umm al-Qura calendar, or -1 outside of the data.</summary>
    private int GetUmmAlQuraData(long year)
    {
        if (!_ummAlQura)
            return -1;

        var index = year - TemporalCalendarData.UmmAlQuraStartYear;
        var data = TemporalCalendarData.UmmAlQuraYears;
        return index >= 0 && index < data.Length ? data[(int)index] : -1;
    }

    private long NewYear(long year)
    {
        var data = GetUmmAlQuraData(year);
        if (data >= 0)
        {
            // The new year of the data is an offset from the mean new year of the tabular calendar
            var meanNewYear = FridayEpoch + ((year - 1) * ((354 * 30) + 11) / 30);
            var offset = data >> 13;
            return (data & (1 << 12)) != 0 ? meanNewYear - offset : meanNewYear + offset;
        }

        return _epoch + ((year - 1) * 354) + TemporalMath.FloorDivide(3 + (11 * year), 30);
    }

    public override CalendarYmd FromRataDie(long rataDie)
    {
        var year = TemporalMath.FloorDivide((30 * (rataDie - _epoch)) + 10646, 10631);
        while (rataDie < NewYear(year))
        {
            year--;
        }

        while (rataDie >= NewYear(year) + DaysInYear(year))
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

    public override int MonthsInYear(long year) => 12;

    public override int DaysInMonth(long year, int month)
    {
        var data = GetUmmAlQuraData(year);
        if (data >= 0)
            return 29 + ((data >> (month - 1)) & 1);

        if (month % 2 == 1)
            return 30;

        return month == 12 && IsTabularLeapYear(year) ? 30 : 29;
    }

    public override int DaysInYear(long year)
    {
        var data = GetUmmAlQuraData(year);
        if (data >= 0)
            return (29 * 12) + int.PopCount(data & 0xFFF);

        return IsTabularLeapYear(year) ? 355 : 354;
    }

    public override bool InLeapYear(long year) => DaysInYear(year) == 355;

    private static bool IsTabularLeapYear(long year) => TemporalMath.FloorModulo(14 + (11 * year), 30) < 11;

    protected override int MaxDaysInMonth(MonthCodeValue monthCode)
    {
        if (_ummAlQura || monthCode.Number % 2 == 1 || monthCode.Number == 12)
            return 30;

        return 29;
    }

    protected override long MonthsBetweenYears(long fromYear, long toYear) => 12 * (toYear - fromYear);

    protected override (long Year, int Month) AddMonths(long year, int month, long months)
    {
        if (Math.Abs(months) > 2 * 13 * MaxYear)
            return ThrowOutOfRange<(long, int)>();

        var monthIndex = month - 1 + months;
        return (CheckYear(year + TemporalMath.FloorDivide(monthIndex, 12)), (int)TemporalMath.FloorModulo(monthIndex, 12) + 1);
    }

    protected override (TemporalEra Era, long EraYear) GetEra(CalendarYmd date, long rataDie)
    {
        var era = date.Year > 0 ? Eras[0] : Eras[1];
        return (era, era.ToEraYear(date.Year));
    }
}
