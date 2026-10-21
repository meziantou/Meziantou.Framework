namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The traditional Chinese ("chinese") and Korean ("dangi") lunisolar calendars (icu_calendar::cal::east_asian_traditional
/// of ICU4X): the data computed for 1900 to 2102, and a mean-motion approximation of the solar terms and the new moons
/// outside. The arithmetic year is the ISO year in which the year starts; a leap month has the month code of the
/// previous month with the "L" suffix.
/// </summary>
internal sealed class EastAsianTemporalCalendar : NonIsoTemporalCalendar
{
    private const long MillisecondsPerDay = 86_400_000;

    // The mean year and solar term of the Gregorian calendar, and the mean synodic month on 2000-01-01, in milliseconds
    private const long MeanYearLength = MillisecondsPerDay * 146_097 / 400;
    private const long MeanSolarTermLength = MeanYearLength / 12;
    private static readonly long MeanSynodicMonthLength = (long)((Int128)MillisecondsPerDay * 295_305_888_531 / 10_000_000_000);

    // The offsets of the approximations: UTC+8, UTC+9, and the local time of Beijing (UTC+1397/180 hours)
    private const long UtcPlus8 = MillisecondsPerDay * 8 / 24;
    private const long UtcPlus9 = MillisecondsPerDay * 9 / 24;
    private const long BeijingOffset = MillisecondsPerDay * 1397 / 180 / 24;

    // The winter solstice of 1999-12-22T07:44Z and the new moon of 2000-01-06T18:14Z, in milliseconds since the rata die 0
    private static readonly long UtcSolstice = (IsoToRataDie(1999, 12, 22) * MillisecondsPerDay) + (((7 * 60) + 44) * 60 * 1000);
    private static readonly long UtcNewMoon = (IsoToRataDie(2000, 1, 6) * MillisecondsPerDay) + (((18 * 60) + 14) * 60 * 1000);

    private readonly bool _korean;
    private LunisolarYear? _lastYear;

    private EastAsianTemporalCalendar(string id, bool korean)
        : base(id, [])
    {
        _korean = korean;
    }

    public static EastAsianTemporalCalendar CreateChinese() => new("chinese", korean: false);

    public static EastAsianTemporalCalendar CreateDangi() => new("dangi", korean: true);

    /// <summary>A year of the calendar: its new year, the lengths of its months and its leap month.</summary>
    private sealed class LunisolarYear(long relatedIsoYear, long newYear, int monthLengths, int leapMonth)
    {
        public long RelatedIsoYear { get; } = relatedIsoYear;

        public long NewYear { get; } = newYear;

        public int LeapMonth { get; } = leapMonth;

        public int MonthsInYear => LeapMonth == 0 ? 12 : 13;

        public int DaysInYear => DaysBeforeMonth(MonthsInYear + 1);

        public int DaysInMonth(int month) => 29 + ((monthLengths >> (month - 1)) & 1);

        public int DaysBeforeMonth(int month) => (29 * (month - 1)) + int.PopCount(monthLengths & ((1 << (month - 1)) - 1));

        public static LunisolarYear FromData(long relatedIsoYear, uint data)
        {
            var newYear = IsoToRataDie(relatedIsoYear, 1, 19) + (data >> 17);
            return new LunisolarYear(relatedIsoYear, newYear, (int)(data & 0x1FFF), (int)((data >> 13) & 0xF));
        }
    }

    private LunisolarYear GetYear(long relatedIsoYear)
    {
        var lastYear = Volatile.Read(ref _lastYear);
        if (lastYear is not null && lastYear.RelatedIsoYear == relatedIsoYear)
            return lastYear;

        var year = ComputeYear(relatedIsoYear);
        Volatile.Write(ref _lastYear, year);
        return year;
    }

    private LunisolarYear ComputeYear(long relatedIsoYear)
    {
        var data = _korean ? TemporalCalendarData.KoreaYears : TemporalCalendarData.ChinaYears;
        var index = relatedIsoYear - (_korean ? TemporalCalendarData.KoreaStartYear : TemporalCalendarData.ChinaStartYear);
        if (index >= 0 && index < data.Length)
            return LunisolarYear.FromData(relatedIsoYear, data[(int)index]);

        if (index >= data.Length)
            return ComputeSimpleYear(_korean ? UtcPlus9 : UtcPlus8, relatedIsoYear);

        index = relatedIsoYear - TemporalCalendarData.QingStartYear;
        if (index >= 0 && index < TemporalCalendarData.QingYears.Length)
            return LunisolarYear.FromData(relatedIsoYear, TemporalCalendarData.QingYears[(int)index]);

        return ComputeSimpleYear(BeijingOffset, relatedIsoYear);
    }

    /// <summary>
    /// The approximation of a year outside of the data, inspired by the píngqì rule of the Ming dynasty: the solar terms
    /// and the new moons are computed from their mean motions, anchored in the Gregorian calendar.
    /// </summary>
    private static LunisolarYear ComputeSimpleYear(long utcOffset, long relatedIsoYear)
    {
        var majorSolarTerm = PeriodicMomentOnOrBefore(IsoToRataDie(relatedIsoYear, 1, 1) - 1, UtcSolstice + utcOffset, MeanYearLength);
        var newMoon = PeriodicMomentOnOrBefore(Day(majorSolarTerm), UtcNewMoon + utcOffset, MeanSynodicMonthLength);
        var nextNewMoon = newMoon + MeanSynodicMonthLength;

        // The solstice is in the month of the 11th solar term of the previous year
        var solarTerm = -2;
        var hadLeapInSui = false;

        // Skip the months before the year (M11, maybe M11L, M12, maybe M12L)
        while (solarTerm < 0 || (Day(nextNewMoon) <= Day(majorSolarTerm) && !hadLeapInSui))
        {
            if (Day(nextNewMoon) <= Day(majorSolarTerm) && !hadLeapInSui)
            {
                hadLeapInSui = true;
            }
            else
            {
                solarTerm++;
                majorSolarTerm += MeanSolarTermLength;
            }

            newMoon = nextNewMoon;
            nextNewMoon += MeanSynodicMonthLength;
        }

        var newYear = Day(newMoon);
        var monthLengths = 0;
        var leapMonth = 0;

        // Iterate over the 12 solar terms, producing potentially 13 months
        while (solarTerm < 12 || (Day(nextNewMoon) <= Day(majorSolarTerm) && !hadLeapInSui))
        {
            var index = solarTerm + (leapMonth != 0 ? 1 : 0);
            if (index < 13 && Day(nextNewMoon) - Day(newMoon) == 30)
            {
                monthLengths |= 1 << index;
            }

            if (Day(nextNewMoon) <= Day(majorSolarTerm) && !hadLeapInSui)
            {
                hadLeapInSui = true;
                leapMonth = solarTerm + 1;
            }
            else
            {
                solarTerm++;
                majorSolarTerm += MeanSolarTermLength;
            }

            newMoon = nextNewMoon;
            nextNewMoon += MeanSynodicMonthLength;
        }

        return new LunisolarYear(relatedIsoYear, newYear, monthLengths, leapMonth);

        static long Day(long moment) => TemporalMath.FloorDivide(moment, MillisecondsPerDay);
    }

    /// <summary>The largest moment base + n × duration that is on the day <paramref name="rataDie"/> or before.</summary>
    private static long PeriodicMomentOnOrBefore(long rataDie, long baseMoment, long duration)
    {
        var difference = (rataDie * MillisecondsPerDay) - baseMoment;
        var periods = TemporalMath.FloorDivide(difference + MillisecondsPerDay - 1, duration);
        return baseMoment + (periods * duration);
    }

    private LunisolarYear GetYearContaining(long rataDie)
    {
        var year = GetYear(IsoYear(rataDie));
        if (rataDie < year.NewYear)
        {
            year = GetYear(year.RelatedIsoYear - 1);
        }

        return year;
    }

    public override CalendarYmd FromRataDie(long rataDie)
    {
        var year = GetYearContaining(rataDie);
        var dayOfYear = (int)Math.Min(rataDie - year.NewYear, year.DaysInYear - 1);
        var month = 1;
        while (dayOfYear >= year.DaysInMonth(month))
        {
            dayOfYear -= year.DaysInMonth(month);
            month++;
        }

        return new CalendarYmd(year.RelatedIsoYear, month, dayOfYear + 1);
    }

    public override long ToRataDie(long year, int month, int day)
    {
        var yearData = GetYear(year);
        return yearData.NewYear + yearData.DaysBeforeMonth(month) + day - 1;
    }

    public override int MonthsInYear(long year) => GetYear(year).MonthsInYear;

    public override int DaysInMonth(long year, int month) => GetYear(year).DaysInMonth(month);

    public override int DaysInYear(long year) => GetYear(year).DaysInYear;

    public override bool InLeapYear(long year) => GetYear(year).LeapMonth != 0;

    protected override int LeapMonth(long year) => GetYear(year).LeapMonth;

    protected override bool IsValidLeapMonthNumber(int number) => number is >= 1 and <= 12;

    protected override int MaxDaysInMonth(MonthCodeValue monthCode) => 30;

    /// <summary>The reference dates of the month codes and days (the reference years are related ISO years, as in ICU4X).</summary>
    protected override long GetReferenceDate(MonthCodeValue monthCode, int day, TemporalOverflow overflow)
    {
        var referenceYear = GetReferenceYear(monthCode, day);
        if (referenceYear == 0)
        {
            // The leap month (or its 30th day) is not known to have occurred
            if (overflow == TemporalOverflow.Reject)
                ThrowHelper.ThrowRangeError("The month code '" + monthCode.ToCode() + "' and the day " + day.ToString(System.Globalization.CultureInfo.InvariantCulture) + " do not exist in the calendar " + Id);

            monthCode = new MonthCodeValue(monthCode.Number, IsLeap: false);
            referenceYear = GetReferenceYear(monthCode, day);
        }

        var leapMonth = LeapMonth(referenceYear);
        var month = monthCode.IsLeap ? leapMonth : (leapMonth != 0 && monthCode.Number >= leapMonth ? monthCode.Number + 1 : monthCode.Number);
        return ToRataDie(referenceYear, month, day);
    }

    private int GetReferenceYear(MonthCodeValue monthCode, int day)
    {
        var day30 = day > 29;
        return (monthCode.Number, monthCode.IsLeap, day30) switch
        {
            (1, false, false) => 1972,
            (1, false, true) => 1970,
            (2, false, _) => 1972,
            (2, true, false) => 1947,
            (3, false, false) => 1972,
            (3, false, true) => _korean ? 1968 : 1966,
            (3, true, false) => 1966,
            (3, true, true) => 1955,
            (4, false, false) => 1972,
            (4, false, true) => 1970,
            (4, true, false) => 1963,
            (4, true, true) => 1944,
            (5, false, _) => 1972,
            (5, true, false) => 1971,
            (5, true, true) => 1952,
            (6, false, false) => 1972,
            (6, false, true) => 1971,
            (6, true, false) => 1960,
            (6, true, true) => 1941,
            (7, false, _) => 1972,
            (7, true, false) => 1968,
            (7, true, true) => 1938,
            (8, false, false) => 1972,
            (8, false, true) => 1971,
            (8, true, false) => 1957,
            (9, false, _) => 1972,
            (9, true, false) => 2014,
            (10, false, _) => 1972,
            (10, true, false) => 1984,
            (11, false, false) when day > 26 => 1971,
            (11, false, false) => 1972,
            (11, false, true) => 1969,
            (11, true, false) => 2033,
            (12, false, _) => 1971,
            _ => 0,
        };
    }
}
