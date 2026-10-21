using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A built-in calendar of Temporal: the calendar-dependent abstract operations of the specification (12.3). The ISO 8601
/// calendar implements the steps of the specification; the other calendars (ECMA-402) implement the NonISO* operations.
/// </summary>
internal interface ITemporalCalendar
{
    /// <summary>Gets the canonical calendar type (e.g. "iso8601").</summary>
    string Id { get; }

    /// <summary>CalendarISOToDate(calendar, isoDate)</summary>
    CalendarDate IsoToDate(IsoDate date);

    /// <summary>CalendarDateAdd(calendar, isoDate, duration, overflow) without the final range check.</summary>
    IsoDate DateAdd(IsoDate date, DateDuration duration, TemporalOverflow overflow);

    /// <summary>CalendarDateUntil(calendar, one, two, largestUnit) for two different dates.</summary>
    DateDuration DateUntil(IsoDate one, IsoDate two, TemporalUnit largestUnit);

    /// <summary>CalendarExtraFields(calendar, fields)</summary>
    CalendarFieldNames ExtraFields(CalendarFieldNames fieldNames);

    /// <summary>CalendarFieldKeysToIgnore(calendar, keys)</summary>
    CalendarFieldNames FieldKeysToIgnore(CalendarFieldNames keys);

    /// <summary>CalendarResolveFields(calendar, fields, type)</summary>
    void ResolveFields(CalendarFields fields, CalendarFieldsType type);

    /// <summary>CalendarDateToISO(calendar, fields, overflow)</summary>
    IsoDate DateToIso(CalendarFields fields, TemporalOverflow overflow);

    /// <summary>CalendarMonthDayToISOReferenceDate(calendar, fields, overflow)</summary>
    IsoDate MonthDayToIsoReferenceDate(CalendarFields fields, TemporalOverflow overflow);
}

/// <summary>The available calendars (AvailableCalendars): only the ISO 8601 calendar for now.</summary>
internal static class TemporalCalendars
{
    public static ITemporalCalendar Iso { get; } = new IsoTemporalCalendar();

    /// <summary>Finds a calendar by its calendar type, ignoring the ASCII case.</summary>
    public static bool TryGet(string id, [NotNullWhen(true)] out ITemporalCalendar? calendar)
    {
        if (string.Equals(id, TemporalOperations.IsoCalendar, StringComparison.OrdinalIgnoreCase))
        {
            calendar = Iso;
            return true;
        }

        calendar = null;
        return false;
    }

    /// <summary>Gets the calendar of a canonical calendar type (the [[Calendar]] slot of a Temporal object).</summary>
    public static ITemporalCalendar Get(string id)
    {
        if (ReferenceEquals(id, TemporalOperations.IsoCalendar))
            return Iso;

        return TryGet(id, out var calendar) ? calendar : Iso;
    }
}

/// <summary>The ISO 8601 calendar ("iso8601").</summary>
internal sealed class IsoTemporalCalendar : ITemporalCalendar
{
    public string Id => TemporalOperations.IsoCalendar;

    public CalendarDate IsoToDate(IsoDate date)
    {
        var (week, yearOfWeek) = TemporalOperations.IsoWeekOfYear(date);
        return new CalendarDate(
            Era: null,
            EraYear: null,
            date.Year,
            date.Month,
            TemporalOperations.CreateMonthCode(date.Month),
            date.Day,
            TemporalOperations.IsoDayOfWeek(date),
            TemporalOperations.IsoDayOfYear(date),
            week,
            yearOfWeek,
            DaysInWeek: 7,
            TemporalOperations.IsoDaysInMonth(date.Year, date.Month),
            TemporalOperations.IsoDaysInYear(date.Year),
            MonthsInYear: 12,
            TemporalOperations.IsLeapYear(date.Year));
    }

    public IsoDate DateAdd(IsoDate date, DateDuration duration, TemporalOverflow overflow)
    {
        var (year, month) = TemporalOperations.BalanceIsoYearMonth(date.Year + duration.Years, date.Month + duration.Months);
        var intermediate = TemporalOperations.RegulateIsoDate(year, month, date.Day, overflow);
        var days = duration.Days + (7 * duration.Weeks);
        return TemporalOperations.AddDaysToIsoDate(intermediate, days);
    }

    public DateDuration DateUntil(IsoDate one, IsoDate two, TemporalUnit largestUnit) => TemporalOperations.IsoDateUntil(one, two, largestUnit);

    public CalendarFieldNames ExtraFields(CalendarFieldNames fieldNames) => CalendarFieldNames.None;

    public CalendarFieldNames FieldKeysToIgnore(CalendarFieldNames keys)
    {
        var ignoredKeys = keys;
        if ((keys & (CalendarFieldNames.Month | CalendarFieldNames.MonthCode)) != 0)
        {
            ignoredKeys |= CalendarFieldNames.Month | CalendarFieldNames.MonthCode;
        }

        return ignoredKeys;
    }

    public void ResolveFields(CalendarFields fields, CalendarFieldsType type)
    {
        var needsYear = type is CalendarFieldsType.Date or CalendarFieldsType.YearMonth;
        var needsDay = type is CalendarFieldsType.Date or CalendarFieldsType.MonthDay;
        if (needsYear && fields.Year is null)
            ThrowHelper.ThrowTypeError("The property 'year' is required");

        if (needsDay && fields.Day is null)
            ThrowHelper.ThrowTypeError("The property 'day' is required");

        if (fields.Month is null && fields.MonthCode is null)
            ThrowHelper.ThrowTypeError("The property 'month' or 'monthCode' is required");

        if (fields.MonthCode is { } monthCode)
        {
            if (!TemporalParser.TryParseMonthCode(monthCode, out var month, out var isLeapMonth) || isLeapMonth || month > 12)
                ThrowHelper.ThrowRangeError("The month code '" + monthCode + "' is not valid in the ISO 8601 calendar");

            if (fields.Month is { } fieldMonth && fieldMonth != month)
                ThrowHelper.ThrowRangeError("The month " + NumberFormatting.ToString(fieldMonth) + " does not match the month code '" + monthCode + "'");

            fields.Month = month;
        }
    }

    public IsoDate DateToIso(CalendarFields fields, TemporalOverflow overflow)
    {
        var (year, month, day) = TemporalOperations.RegulateIsoDateFields(fields.Year!.Value, fields.Month!.Value, fields.Day!.Value, overflow);
        return TemporalOperations.CreateIsoDateFromRegulated(year, month, day);
    }

    public IsoDate MonthDayToIsoReferenceDate(CalendarFields fields, TemporalOverflow overflow)
    {
        // The first ISO 8601 leap year after the epoch
        const int ReferenceIsoYear = 1972;
        var year = fields.Year ?? ReferenceIsoYear;
        var (_, month, day) = TemporalOperations.RegulateIsoDateFields(year, fields.Month!.Value, fields.Day!.Value, overflow);
        return new IsoDate(ReferenceIsoYear, month, day);
    }
}
