using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A calendar whose months and days are the ones of the ISO 8601 calendar, with another numbering of the years and
/// other eras: "gregory", "buddhist", "roc" and "japanese".
/// </summary>
internal class GregorianBasedTemporalCalendar(string id, TemporalEra[] eras, long yearOffset) : NonIsoTemporalCalendar(id, eras)
{
    /// <summary>The arithmetic year minus the ISO year.</summary>
    private readonly long _yearOffset = yearOffset;

    public static GregorianBasedTemporalCalendar CreateGregorian() => new("gregory", [new TemporalEra("ce", "ad", TemporalEraKind.Epoch), new TemporalEra("bce", "bc", TemporalEraKind.Negative)], 0);

    public static GregorianBasedTemporalCalendar CreateBuddhist() => new("buddhist", [new TemporalEra("be", alias: null, TemporalEraKind.Epoch)], 543);

    public static GregorianBasedTemporalCalendar CreateRoc() => new("roc", [new TemporalEra("roc", alias: null, TemporalEraKind.Epoch), new TemporalEra("broc", alias: null, TemporalEraKind.Negative)], -1911);

    public override CalendarYmd FromRataDie(long rataDie)
    {
        CalendarMath.CivilFromDays(rataDie - UnixEpochRataDie, out var year, out var month, out var day);
        return new CalendarYmd(year + _yearOffset, month, day);
    }

    public override long ToRataDie(long year, int month, int day) => IsoToRataDie(year - _yearOffset, month, day);

    public override int MonthsInYear(long year) => 12;

    public override int DaysInMonth(long year, int month) => CalendarMath.DaysInMonth(year - _yearOffset, month);

    public override int DaysInYear(long year) => InLeapYear(year) ? 366 : 365;

    public override bool InLeapYear(long year) => CalendarMath.IsLeapYear(year - _yearOffset);

    protected override int MaxDaysInMonth(MonthCodeValue monthCode) => monthCode.Number == 2 ? 29 : CalendarMath.DaysInMonth(2001, monthCode.Number);

    protected override long MonthsBetweenYears(long fromYear, long toYear) => 12 * (toYear - fromYear);

    protected override (long Year, int Month) AddMonths(long year, int month, long months)
    {
        if (Math.Abs(months) > 2 * 13 * MaxYear)
            return ThrowOutOfRange<(long, int)>();

        var monthIndex = month - 1 + months;
        return (CheckYear(year + TemporalMath.FloorDivide(monthIndex, 12)), (int)TemporalMath.FloorModulo(monthIndex, 12) + 1);
    }

    protected override (TemporalEra Era, long EraYear) GetEra(CalendarYmd date, long rataDie) => GetEraFromYear(date.Year);

    /// <summary>Gets the era of an arithmetic year: the first era (epoch kind) for the positive years, else the second one (negative kind).</summary>
    protected (TemporalEra Era, long EraYear) GetEraFromYear(long year)
    {
        var era = year > 0 || Eras.Length == 1 ? Eras[0] : Eras[1];
        return (era, era.ToEraYear(year));
    }
}

/// <summary>
/// The Japanese calendar ("japanese"): the eras of the Japanese government from Meiji 6 (1873-01-01), the eras of the
/// Gregorian calendar before.
/// </summary>
internal sealed class JapaneseTemporalCalendar : GregorianBasedTemporalCalendar
{
    // The eras since Meiji, with the rata die of their first day
    private static readonly (TemporalEra Era, long Start)[] ModernEras =
    [
        (new TemporalEra("meiji", alias: null, TemporalEraKind.Offset, 1868), IsoToRataDie(1868, 10, 23)),
        (new TemporalEra("taisho", alias: null, TemporalEraKind.Offset, 1912), IsoToRataDie(1912, 7, 30)),
        (new TemporalEra("showa", alias: null, TemporalEraKind.Offset, 1926), IsoToRataDie(1926, 12, 25)),
        (new TemporalEra("heisei", alias: null, TemporalEraKind.Offset, 1989), IsoToRataDie(1989, 1, 8)),
        (new TemporalEra("reiwa", alias: null, TemporalEraKind.Offset, 2019), IsoToRataDie(2019, 5, 1)),
    ];

    // The first day of the year 6 of Meiji: the Gregorian eras are used before
    private static readonly long FirstModernEraDate = IsoToRataDie(1873, 1, 1);

    public JapaneseTemporalCalendar()
        : base("japanese", CreateEras(), 0)
    {
    }

    protected override bool HasMidYearEras => true;

    private static TemporalEra[] CreateEras()
    {
        var eras = new TemporalEra[ModernEras.Length + 2];
        eras[0] = new TemporalEra("ce", "ad", TemporalEraKind.Epoch);
        eras[1] = new TemporalEra("bce", "bc", TemporalEraKind.Negative);
        for (var i = 0; i < ModernEras.Length; i++)
        {
            eras[i + 2] = ModernEras[i].Era;
        }

        return eras;
    }

    protected override (TemporalEra Era, long EraYear) GetEra(CalendarYmd date, long rataDie)
    {
        if (rataDie >= FirstModernEraDate)
        {
            for (var i = ModernEras.Length - 1; i >= 0; i--)
            {
                var (era, start) = ModernEras[i];
                if (rataDie >= start)
                    return (era, era.ToEraYear(date.Year));
            }
        }

        return GetEraFromYear(date.Year);
    }
}
