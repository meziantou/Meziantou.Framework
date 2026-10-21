using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A date of a non-ISO calendar: the arithmetic year, the ordinal month (1-based) and the day.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CalendarYmd(long Year, int Month, int Day);

/// <summary>A month code: the month number and whether the month is a leap month ("M05L" is (5, true)).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MonthCodeValue(int Number, bool IsLeap)
{
    private static readonly string[] CommonCodes = ["M00", "M01", "M02", "M03", "M04", "M05", "M06", "M07", "M08", "M09", "M10", "M11", "M12", "M13"];
    private static readonly string[] LeapCodes = ["M00L", "M01L", "M02L", "M03L", "M04L", "M05L", "M06L", "M07L", "M08L", "M09L", "M10L", "M11L", "M12L"];

    public string ToCode() => IsLeap ? LeapCodes[Number] : CommonCodes[Number];

    /// <summary>Parses a month code that PrepareCalendarFields already validated (ParseMonthCode).</summary>
    public static MonthCodeValue Parse(string monthCode)
    {
        return TemporalParser.TryParseMonthCode(monthCode, out var number, out var isLeap) ? new MonthCodeValue(number, isLeap) : default;
    }

    /// <summary>Compares two month codes in the lexicographic code unit order of their strings ("M05" &lt; "M05L" &lt; "M06").</summary>
    public static int Compare(MonthCodeValue one, MonthCodeValue two)
    {
        if (one.Number != two.Number)
            return one.Number < two.Number ? -1 : 1;

        if (one.IsLeap == two.IsLeap)
            return 0;

        return one.IsLeap ? 1 : -1;
    }
}

/// <summary>How the arithmetic year of an era is computed from the era year (the Era Kind of ECMA-402).</summary>
internal enum TemporalEraKind : byte
{
    /// <summary>The epoch era: the arithmetic year is the era year.</summary>
    Epoch,

    /// <summary>An era counting backwards from the epoch: 1 is the arithmetic year 0.</summary>
    Negative,

    /// <summary>An era starting at the arithmetic year <see cref="TemporalEra.Offset"/>.</summary>
    Offset,
}

/// <summary>An era of a calendar (the Eras table of ECMA-402).</summary>
internal sealed class TemporalEra(string code, string? alias, TemporalEraKind kind, long offset = 0)
{
    public string Code { get; } = code;

    public string? Alias { get; } = alias;

    public TemporalEraKind Kind { get; } = kind;

    public long Offset { get; } = offset;

    /// <summary>CalendarDateArithmeticYearForEraYear for this era.</summary>
    public double ToArithmeticYear(double eraYear) => Kind switch
    {
        TemporalEraKind.Epoch => eraYear,
        TemporalEraKind.Negative => 1 - eraYear,
        _ => Offset + eraYear - 1,
    };

    /// <summary>The era year of an arithmetic year in this era.</summary>
    public long ToEraYear(long year) => Kind switch
    {
        TemporalEraKind.Epoch => year,
        TemporalEraKind.Negative => 1 - year,
        _ => year - Offset + 1,
    };
}

/// <summary>
/// A calendar of ECMA-402 other than ISO 8601. A calendar provides the conversions between its dates and the rata die
/// (the day count where 1 is 0001-01-01 of the proleptic Gregorian calendar, as in Calendrical Calculations and ICU4X),
/// the lengths of its years and months, its leap months and its eras; this class implements the NonISO* operations of
/// the Intl era and monthCode proposal from them.
/// </summary>
internal abstract class NonIsoTemporalCalendar(string id, TemporalEra[] eras) : ITemporalCalendar
{
    /// <summary>The rata die of 1970-01-01.</summary>
    protected const long UnixEpochRataDie = 719_163;

    /// <summary>The largest magnitude of an arithmetic year that the calendars compute: the dates of the range of Temporal have smaller years in every calendar.</summary>
    protected const long MaxYear = 300_000;

    /// <summary>The first and last dates of ISODateWithinLimits.</summary>
    private static readonly long MinRataDie = IsoToRataDie(-271_821, 4, 19);
    private static readonly long MaxRataDie = IsoToRataDie(275_760, 9, 13);

    /// <summary>The bounds of the reference dates of NonISOMonthDayToISOReferenceDate.</summary>
    private static readonly long ReferenceStart = IsoToRataDie(1900, 1, 1);
    private static readonly long ReferenceEnd = IsoToRataDie(1972, 12, 31);
    private static readonly long FallbackReferenceEnd = IsoToRataDie(2035, 12, 31);

    private readonly TemporalEra[] _eras = eras;

    public string Id { get; } = id;

    /// <summary>Gets the eras of the calendar (empty when the calendar has no eras).</summary>
    protected TemporalEra[] Eras => _eras;

    private bool SupportsEra => _eras.Length != 0;

    // ------------------------------------------------------------------------------------------------------------------
    // The primitives of a calendar
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the date of a rata die.</summary>
    public abstract CalendarYmd FromRataDie(long rataDie);

    /// <summary>Gets the rata die of a valid date.</summary>
    public abstract long ToRataDie(long year, int month, int day);

    public abstract int MonthsInYear(long year);

    public abstract int DaysInMonth(long year, int month);

    public virtual int DaysInYear(long year) => (int)(ToRataDie(year + 1, 1, 1) - ToRataDie(year, 1, 1));

    public abstract bool InLeapYear(long year);

    /// <summary>Gets the ordinal of the leap month of a year, or 0. Its month code is the one of the previous month with the "L" suffix.</summary>
    protected virtual int LeapMonth(long year) => 0;

    /// <summary>Gets the largest month number of the month codes (13 for the calendars with an epagomenal month).</summary>
    protected virtual int MaxMonthNumber => 12;

    /// <summary>Gets whether "M{number}L" is a month code of the calendar.</summary>
    protected virtual bool IsValidLeapMonthNumber(int number) => false;

    /// <summary>Gets whether a leap month missing in a year is constrained to the next month (skip-forward) rather than to the month of the same number (skip-backward).</summary>
    protected virtual bool LeapMonthSkipsForward => false;

    /// <summary>Gets whether the calendar has eras that start in the middle of a year (CalendarHasMidYearEras).</summary>
    protected virtual bool HasMidYearEras => false;

    /// <summary>Gets the maximum number of days of a month in any year.</summary>
    protected abstract int MaxDaysInMonth(MonthCodeValue monthCode);

    /// <summary>Gets the era and the era year of a date (only called for a calendar with eras).</summary>
    protected virtual (TemporalEra Era, long EraYear) GetEra(CalendarYmd date, long rataDie)
    {
        var era = _eras[0];
        return (era, era.ToEraYear(date.Year));
    }

    /// <summary>Gets the number of months of the years from <paramref name="fromYear"/> (inclusive) to <paramref name="toYear"/> (exclusive), negated when <paramref name="toYear"/> is smaller.</summary>
    protected virtual long MonthsBetweenYears(long fromYear, long toYear)
    {
        long result = 0;
        for (var year = fromYear; year < toYear; year++)
        {
            result += MonthsInYear(year);
        }

        for (var year = toYear; year < fromYear; year++)
        {
            result -= MonthsInYear(year);
        }

        return result;
    }

    /// <summary>BalanceNonISODate(calendar, year, month + months, 1): the year and month <paramref name="months"/> months after a month.</summary>
    protected virtual (long Year, int Month) AddMonths(long year, int month, long months)
    {
        if (Math.Abs(months) > 2 * 13 * MaxYear)
            return ThrowOutOfRange<(long, int)>();

        var resolvedMonth = month + months;
        var monthsInYear = MonthsInYear(year);
        while (resolvedMonth <= 0)
        {
            year = CheckYear(year - 1);
            monthsInYear = MonthsInYear(year);
            resolvedMonth += monthsInYear;
        }

        while (resolvedMonth > monthsInYear)
        {
            resolvedMonth -= monthsInYear;
            year = CheckYear(year + 1);
            monthsInYear = MonthsInYear(year);
        }

        return (year, (int)resolvedMonth);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------------------------

    protected static long IsoToRataDie(long year, int month, int day) => CalendarMath.DaysFromCivil(year, month, day) + UnixEpochRataDie;

    protected static long IsoToRataDie(IsoDate date) => IsoToRataDie(date.Year, date.Month, date.Day);

    protected static IsoDate RataDieToIso(long rataDie)
    {
        CalendarMath.CivilFromDays(rataDie - UnixEpochRataDie, out var year, out var month, out var day);
        return new IsoDate(year, month, day);
    }

    /// <summary>Gets the ISO year of a rata die.</summary>
    protected static long IsoYear(long rataDie)
    {
        CalendarMath.CivilFromDays(rataDie - UnixEpochRataDie, out var year, out _, out _);
        return year;
    }

    protected static T ThrowOutOfRange<T>() => ThrowHelper.ThrowRangeError<T>("The date is outside the range of Temporal");

    /// <summary>Checks that a year can be computed: a larger year is outside the range of Temporal (a RangeError).</summary>
    protected static long CheckYear(double year)
    {
        if (!(Math.Abs(year) <= MaxYear))
            return ThrowOutOfRange<long>();

        return (long)year;
    }

    /// <summary>Gets the month code of an ordinal month.</summary>
    public MonthCodeValue GetMonthCode(long year, int month)
    {
        var leapMonth = LeapMonth(year);
        if (leapMonth == 0 || month < leapMonth)
            return new MonthCodeValue(month, IsLeap: false);

        if (month == leapMonth)
            return new MonthCodeValue(month - 1, IsLeap: true);

        return new MonthCodeValue(month - 1, IsLeap: false);
    }

    /// <summary>IsValidMonthCodeForCalendar(calendar, monthCode)</summary>
    private bool IsValidMonthCode(MonthCodeValue monthCode)
    {
        if (monthCode.IsLeap)
            return IsValidLeapMonthNumber(monthCode.Number);

        return monthCode.Number >= 1 && monthCode.Number <= MaxMonthNumber;
    }

    /// <summary>YearContainsMonthCode(calendar, year, monthCode) for a valid month code.</summary>
    private bool YearContainsMonthCode(long year, MonthCodeValue monthCode)
    {
        if (!monthCode.IsLeap)
            return true;

        var leapMonth = LeapMonth(year);
        return leapMonth != 0 && leapMonth - 1 == monthCode.Number;
    }

    /// <summary>ConstrainMonthCode(calendar, year, monthCode, overflow) for a valid month code.</summary>
    private MonthCodeValue ConstrainMonthCode(long year, MonthCodeValue monthCode, TemporalOverflow overflow)
    {
        if (YearContainsMonthCode(year, monthCode))
            return monthCode;

        if (overflow == TemporalOverflow.Reject)
            return ThrowHelper.ThrowRangeError<MonthCodeValue>("The month code '" + monthCode.ToCode() + "' does not exist in the year " + year.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return LeapMonthSkipsForward ? new MonthCodeValue(monthCode.Number + 1, IsLeap: false) : new MonthCodeValue(monthCode.Number, IsLeap: false);
    }

    /// <summary>MonthCodeToOrdinal(calendar, year, monthCode) for a month code of the year.</summary>
    private int MonthCodeToOrdinal(long year, MonthCodeValue monthCode)
    {
        var leapMonth = LeapMonth(year);
        if (monthCode.IsLeap)
            return leapMonth;

        return leapMonth != 0 && monthCode.Number >= leapMonth ? monthCode.Number + 1 : monthCode.Number;
    }

    private TemporalEra? CanonicalizeEra(string era)
    {
        foreach (var candidate in _eras)
        {
            if (candidate.Code == era || candidate.Alias == era)
                return candidate;
        }

        return null;
    }

    /// <summary>The year and month one month after (or before when <paramref name="sign"/> is -1) a month.</summary>
    private (long Year, int Month) StepMonth(long year, int month, int sign)
    {
        month += sign;
        if (month < 1)
        {
            year = CheckYear(year - 1);
            return (year, MonthsInYear(year));
        }

        if (month > MonthsInYear(year))
            return (CheckYear(year + 1), 1);

        return (year, month);
    }

    /// <summary>CompareSurpasses(sign, year, monthCode, day, target) with a month code.</summary>
    private static bool CompareSurpasses(int sign, long year, MonthCodeValue monthCode, int day, long targetYear, MonthCodeValue targetMonthCode, int targetDay)
    {
        if (year != targetYear)
            return sign * (year - targetYear) > 0;

        var comparison = MonthCodeValue.Compare(monthCode, targetMonthCode);
        if (comparison != 0)
            return sign * comparison > 0;

        if (day != targetDay)
            return sign * (day - targetDay) > 0;

        return false;
    }

    /// <summary>CompareSurpasses(sign, year, month, day, target) with an ordinal month.</summary>
    private static bool CompareSurpasses(int sign, long year, int month, int day, CalendarYmd target)
    {
        if (year != target.Year)
            return sign * (year - target.Year) > 0;

        if (month != target.Month)
            return sign * (month - target.Month) > 0;

        if (day != target.Day)
            return sign * (day - target.Day) > 0;

        return false;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // ITemporalCalendar
    // ------------------------------------------------------------------------------------------------------------------

    public CalendarDate IsoToDate(IsoDate date)
    {
        var rataDie = IsoToRataDie(date);
        var ymd = FromRataDie(rataDie);
        string? era = null;
        long? eraYear = null;
        if (SupportsEra)
        {
            var (temporalEra, year) = GetEra(ymd, rataDie);
            era = temporalEra.Code;
            eraYear = year;
        }

        return new CalendarDate(
            era,
            eraYear,
            ymd.Year,
            ymd.Month,
            GetMonthCode(ymd.Year, ymd.Month).ToCode(),
            ymd.Day,
            TemporalOperations.IsoDayOfWeek(date),
            (int)(rataDie - ToRataDie(ymd.Year, 1, 1)) + 1,
            WeekOfYear: null,
            YearOfWeek: null,
            DaysInWeek: 7,
            DaysInMonth(ymd.Year, ymd.Month),
            DaysInYear(ymd.Year),
            MonthsInYear(ymd.Year),
            InLeapYear(ymd.Year));
    }

    /// <summary>NonISODateAdd(calendar, isoDate, duration, overflow)</summary>
    public IsoDate DateAdd(IsoDate date, DateDuration duration, TemporalOverflow overflow)
    {
        var parts = FromRataDie(IsoToRataDie(date));
        var y0 = CheckYear(parts.Year + (double)duration.Years);
        var monthCode = ConstrainMonthCode(y0, GetMonthCode(parts.Year, parts.Month), overflow);
        var m0 = MonthCodeToOrdinal(y0, monthCode);
        var (year, month) = AddMonths(y0, m0, duration.Months);
        var day = parts.Day;
        var daysInMonth = DaysInMonth(year, month);
        if (day > daysInMonth)
        {
            if (overflow == TemporalOverflow.Reject)
                ThrowHelper.ThrowRangeError("The day " + day.ToString(System.Globalization.CultureInfo.InvariantCulture) + " does not exist in the month");

            day = daysInMonth;
        }

        var rataDie = (Int128)ToRataDie(year, month, day) + ((Int128)duration.Weeks * 7) + duration.Days;
        if (rataDie < MinRataDie || rataDie > MaxRataDie)
            return ThrowOutOfRange<IsoDate>();

        return RataDieToIso((long)rataDie);
    }

    /// <summary>NonISODateUntil(calendar, one, two, largestUnit) for two different dates.</summary>
    public DateDuration DateUntil(IsoDate one, IsoDate two, TemporalUnit largestUnit)
    {
        var rataDie1 = IsoToRataDie(one);
        var rataDie2 = IsoToRataDie(two);
        var difference = rataDie2 - rataDie1;
        if (largestUnit == TemporalUnit.Week)
            return new DateDuration(0, 0, difference / 7, difference % 7);

        if (largestUnit == TemporalUnit.Day)
            return new DateDuration(0, 0, 0, difference);

        var sign = difference > 0 ? 1 : -1;
        var parts = FromRataDie(rataDie1);
        var target = FromRataDie(rataDie2);
        var partsMonthCode = GetMonthCode(parts.Year, parts.Month);
        var targetMonthCode = GetMonthCode(target.Year, target.Month);

        // The loops of the specification start from the smallest count that cannot surpass the second date (the
        // difference minus one), then add one unit while the date does not surpass the second date
        long years = 0;
        if (largestUnit == TemporalUnit.Year)
        {
            var yearDifference = target.Year - parts.Year;
            var candidateYears = sign * (yearDifference - sign) > 0 ? yearDifference - sign : sign;
            while (!SurpassesYears(candidateYears))
            {
                years = candidateYears;
                candidateYears += sign;
            }
        }

        var y0 = parts.Year + years;
        var m0 = MonthCodeToOrdinal(y0, ConstrainMonthCode(y0, partsMonthCode, TemporalOverflow.Constrain));
        long months = 0;
        var candidateMonths = (long)sign;
        if (largestUnit == TemporalUnit.Month)
        {
            var estimate = MonthsBetweenYears(y0, target.Year) + target.Month - m0;
            if (sign * (estimate - sign) > 0)
            {
                candidateMonths = estimate - sign;
            }
        }

        var (candidateYear, candidateMonth) = AddMonths(y0, m0, candidateMonths);
        while (!CompareSurpasses(sign, candidateYear, candidateMonth, parts.Day, target))
        {
            months = candidateMonths;
            candidateMonths += sign;
            (candidateYear, candidateMonth) = StepMonth(candidateYear, candidateMonth, sign);
        }

        var (year, month) = AddMonths(y0, m0, months);
        var regulatedDay = Math.Min(parts.Day, DaysInMonth(year, month));
        var days = rataDie2 - ToRataDie(year, month, regulatedDay);
        return new DateDuration(years, months, 0, days);

        bool SurpassesYears(long candidate)
        {
            var surpassYear = parts.Year + candidate;
            if (CompareSurpasses(sign, surpassYear, partsMonthCode, parts.Day, target.Year, targetMonthCode, target.Day))
                return true;

            var surpassMonth = MonthCodeToOrdinal(surpassYear, ConstrainMonthCode(surpassYear, partsMonthCode, TemporalOverflow.Constrain));
            return CompareSurpasses(sign, surpassYear, surpassMonth, parts.Day, target);
        }
    }

    /// <summary>CalendarExtraFields(calendar, fields)</summary>
    public CalendarFieldNames ExtraFields(CalendarFieldNames fieldNames)
    {
        if ((fieldNames & CalendarFieldNames.Year) != 0 && SupportsEra)
            return CalendarFieldNames.Era | CalendarFieldNames.EraYear;

        return CalendarFieldNames.None;
    }

    /// <summary>NonISOFieldKeysToIgnore(calendar, keys)</summary>
    public CalendarFieldNames FieldKeysToIgnore(CalendarFieldNames keys)
    {
        var ignoredKeys = keys;
        if ((keys & CalendarFieldNames.Month) != 0)
        {
            ignoredKeys |= CalendarFieldNames.MonthCode;
        }

        if ((keys & CalendarFieldNames.MonthCode) != 0)
        {
            ignoredKeys |= CalendarFieldNames.Month;
        }

        if (SupportsEra && (keys & (CalendarFieldNames.Era | CalendarFieldNames.EraYear | CalendarFieldNames.Year)) != 0)
        {
            ignoredKeys |= CalendarFieldNames.Era | CalendarFieldNames.EraYear | CalendarFieldNames.Year;
        }

        if (HasMidYearEras && (keys & (CalendarFieldNames.Day | CalendarFieldNames.Month | CalendarFieldNames.MonthCode)) != 0)
        {
            ignoredKeys |= CalendarFieldNames.Era | CalendarFieldNames.EraYear;
        }

        return ignoredKeys;
    }

    /// <summary>NonISOResolveFields(calendar, fields, type)</summary>
    public void ResolveFields(CalendarFields fields, CalendarFieldsType type)
    {
        var needsYear = type is CalendarFieldsType.Date or CalendarFieldsType.YearMonth || fields.MonthCode is null || fields.Month is not null;
        var needsDay = type is CalendarFieldsType.Date or CalendarFieldsType.MonthDay;
        if (needsYear && fields.Year is null)
        {
            if (!SupportsEra)
                ThrowHelper.ThrowTypeError("The property 'year' is required");

            if (fields.Era is null || fields.EraYear is null)
                ThrowHelper.ThrowTypeError("The properties 'year', or 'era' and 'eraYear', are required");
        }

        if (SupportsEra)
        {
            if (fields.Era is not null && fields.EraYear is null)
                ThrowHelper.ThrowTypeError("The property 'eraYear' is required with 'era'");

            if (fields.EraYear is not null && fields.Era is null)
                ThrowHelper.ThrowTypeError("The property 'era' is required with 'eraYear'");
        }

        if (needsDay && fields.Day is null)
            ThrowHelper.ThrowTypeError("The property 'day' is required");

        if (fields.Month is null && fields.MonthCode is null)
            ThrowHelper.ThrowTypeError("The property 'month' or 'monthCode' is required");

        if (SupportsEra && fields.EraYear is { } eraYear)
        {
            var era = CanonicalizeEra(fields.Era!);
            if (era is null)
                ThrowHelper.ThrowRangeError("The era '" + fields.Era + "' is not valid in the calendar " + Id);

            var arithmeticYear = era.ToArithmeticYear(eraYear);
            if (fields.Year is { } year && year != arithmeticYear)
                ThrowHelper.ThrowRangeError("The year " + NumberFormatting.ToString(year) + " does not match the era year");

            fields.Year = arithmeticYear;
        }

        fields.Era = null;
        fields.EraYear = null;
        if (fields.MonthCode is { } monthCodeText)
        {
            var monthCode = MonthCodeValue.Parse(monthCodeText);
            if (!IsValidMonthCode(monthCode))
                ThrowHelper.ThrowRangeError("The month code '" + monthCodeText + "' is not valid in the calendar " + Id);

            if (fields.Year is { } yearValue)
            {
                var year = CheckYear(yearValue);
                var month = MonthCodeToOrdinal(year, ConstrainMonthCode(year, monthCode, TemporalOverflow.Constrain));
                if (fields.Month is { } fieldMonth && fieldMonth != month)
                    ThrowHelper.ThrowRangeError("The month " + NumberFormatting.ToString(fieldMonth) + " does not match the month code '" + monthCodeText + "'");

                fields.Month = month;
            }
        }
    }

    /// <summary>NonISOCalendarDateToISO(calendar, fields, overflow)</summary>
    public IsoDate DateToIso(CalendarFields fields, TemporalOverflow overflow)
    {
        var year = CheckYear(fields.Year!.Value);
        if (fields.MonthCode is { } monthCode)
        {
            ConstrainMonthCode(year, MonthCodeValue.Parse(monthCode), overflow);
        }

        var month = Regulate(fields.Month!.Value, MonthsInYear(year), overflow);
        var day = Regulate(fields.Day!.Value, DaysInMonth(year, month), overflow);
        return RataDieToIso(ToRataDie(year, month, day));
    }

    /// <summary>NonISOMonthDayToISOReferenceDate(calendar, fields, overflow)</summary>
    public IsoDate MonthDayToIsoReferenceDate(CalendarFields fields, TemporalOverflow overflow)
    {
        MonthCodeValue monthCode;
        int daysInMonth;
        if (fields.Year is { } yearValue)
        {
            var year = CheckYear(yearValue);
            if (ToRataDie(year + 1, 1, 1) <= MinRataDie || ToRataDie(year, 1, 1) > MaxRataDie)
                ThrowOutOfRange<IsoDate>();

            var month = Regulate(fields.Month!.Value, MonthsInYear(year), overflow);
            monthCode = fields.MonthCode is null ? GetMonthCode(year, month) : ConstrainMonthCode(year, MonthCodeValue.Parse(fields.MonthCode), overflow);
            daysInMonth = DaysInMonth(year, month);
        }
        else
        {
            monthCode = MonthCodeValue.Parse(fields.MonthCode!);
            daysInMonth = MaxDaysInMonth(monthCode);
        }

        var day = Regulate(fields.Day!.Value, daysInMonth, overflow);
        return RataDieToIso(GetReferenceDate(monthCode, day, overflow));
    }

    /// <summary>Clamps a month or a day to its maximum, or throws a RangeError for the reject overflow.</summary>
    private static int Regulate(double value, int max, TemporalOverflow overflow)
    {
        if (value <= max)
            return (int)value;

        if (overflow == TemporalOverflow.Reject)
            ThrowHelper.ThrowRangeError("The value " + NumberFormatting.ToString(value) + " is out of range (maximum: " + max.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");

        return max;
    }

    /// <summary>
    /// Gets the reference date of a month code and a day (the latest date between 1900-01-01 and 1972-12-31, else the
    /// earliest date between 1973-01-01 and 2035-12-31).
    /// </summary>
    protected virtual long GetReferenceDate(MonthCodeValue monthCode, int day, TemporalOverflow overflow)
    {
        var firstYear = FromRataDie(ReferenceStart).Year;
        for (var year = FromRataDie(ReferenceEnd).Year; year >= firstYear; year--)
        {
            if (TryGetDate(year, out var rataDie) && rataDie >= ReferenceStart && rataDie <= ReferenceEnd)
                return rataDie;
        }

        var lastYear = FromRataDie(FallbackReferenceEnd).Year;
        for (var year = FromRataDie(ReferenceEnd + 1).Year; year <= lastYear; year++)
        {
            if (TryGetDate(year, out var rataDie) && rataDie > ReferenceEnd && rataDie <= FallbackReferenceEnd)
                return rataDie;
        }

        return ThrowHelper.ThrowRangeError<long>("The month code '" + monthCode.ToCode() + "' and the day " + day.ToString(System.Globalization.CultureInfo.InvariantCulture) + " do not exist in the calendar " + Id);

        bool TryGetDate(long year, out long rataDie)
        {
            if (YearContainsMonthCode(year, monthCode))
            {
                var month = MonthCodeToOrdinal(year, monthCode);
                if (day <= DaysInMonth(year, month))
                {
                    rataDie = ToRataDie(year, month, day);
                    return true;
                }
            }

            rataDie = 0;
            return false;
        }
    }
}
