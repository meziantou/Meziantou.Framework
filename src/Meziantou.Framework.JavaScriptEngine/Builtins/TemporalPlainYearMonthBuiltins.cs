using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.PlainYearMonth (https://tc39.es/proposal-temporal/#sec-temporal-plainyearmonth-objects).</summary>
internal static class TemporalPlainYearMonthBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalPlainYearMonthPrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "PlainYearMonth", 2, Construct, prototype);
        realm.DefineMethod(constructor, "from", 1, (_, _, arguments, _) => ToTemporalYearMonth(Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(constructor, "compare", 2, (_, _, arguments, _) =>
        {
            var one = ToTemporalYearMonth(Argument(arguments, 0));
            var two = ToTemporalYearMonth(Argument(arguments, 1));
            return CompareIsoDate(one.IsoDate, two.IsoDate);
        });

        Func<JsValue, (string Calendar, IsoDate Date)> getDate = value =>
        {
            var yearMonth = ThisPlainYearMonth(value);
            return (yearMonth.Calendar, yearMonth.IsoDate);
        };
        realm.DefineAccessor(prototype, "calendarId", (_, thisValue, _, _) => ThisPlainYearMonth(thisValue).Calendar, setter: null);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "era", getDate, _ => JsValue.Undefined);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "eraYear", getDate, _ => JsValue.Undefined);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "year", getDate, d => d.Year);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "month", getDate, d => d.Month);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "monthCode", getDate, d => d.MonthCode);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "daysInYear", getDate, d => d.DaysInYear);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "daysInMonth", getDate, d => d.DaysInMonth);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "monthsInYear", getDate, d => d.MonthsInYear);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "inLeapYear", getDate, d => d.InLeapYear);
        realm.DefineMethod(prototype, "with", 1, With);
        realm.DefineMethod(prototype, "add", 1, (_, thisValue, arguments, _) => AddDurationToYearMonth(subtract: false, ThisPlainYearMonth(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "subtract", 1, (_, thisValue, arguments, _) => AddDurationToYearMonth(subtract: true, ThisPlainYearMonth(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "until", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainYearMonth(DifferenceOperation.Until, ThisPlainYearMonth(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "since", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainYearMonth(DifferenceOperation.Since, ThisPlainYearMonth(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "equals", 1, (_, thisValue, arguments, _) =>
        {
            var yearMonth = ThisPlainYearMonth(thisValue);
            var other = ToTemporalYearMonth(Argument(arguments, 0));
            if (CompareIsoDate(yearMonth.IsoDate, other.IsoDate) != 0)
                return false;

            return CalendarEquals(yearMonth.Calendar, other.Calendar);
        });
        realm.DefineMethod(prototype, "toString", 0, (_, thisValue, arguments, _) =>
        {
            var yearMonth = ThisPlainYearMonth(thisValue);
            var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
            var showCalendar = GetTemporalShowCalendarNameOption(resolvedOptions);
            return TemporalYearMonthToString(yearMonth, showCalendar);
        });
        realm.DefineMethod(prototype, "toLocaleString", 0, (_, thisValue, _, _) => TemporalYearMonthToString(ThisPlainYearMonth(thisValue), ShowCalendarOption.Auto));
        realm.DefineMethod(prototype, "toJSON", 0, (_, thisValue, _, _) => TemporalYearMonthToString(ThisPlainYearMonth(thisValue), ShowCalendarOption.Auto));
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "PlainYearMonth");
        realm.DefineMethod(prototype, "toPlainDate", 1, (_, thisValue, arguments, _) =>
        {
            var yearMonth = ThisPlainYearMonth(thisValue);
            if (Argument(arguments, 0).Cell is not JsObject item)
                return ThrowHelper.ThrowTypeError<JsValue>("The argument must be an object");

            var calendar = yearMonth.Calendar;
            var fields = IsoDateToFields(calendar, yearMonth.IsoDate, CalendarFieldsType.YearMonth);
            var inputFields = PrepareCalendarFields(calendar, item, CalendarFieldNames.Day, CalendarFieldNames.None, partial: false);
            var mergedFields = CalendarMergeFields(calendar, fields, inputFields);
            var isoDate = CalendarDateFromFields(calendar, mergedFields, TemporalOverflow.Constrain);
            return CreateTemporalDate(isoDate, calendar);
        });
        return constructor;
    }

    private static JsPlainYearMonth ThisPlainYearMonth(JsValue value)
    {
        if (value.Cell is JsPlainYearMonth yearMonth)
            return yearMonth;

        return ThrowHelper.ThrowTypeError<JsPlainYearMonth>("this is not a Temporal.PlainYearMonth");
    }

    /// <summary>Temporal.PlainYearMonth(isoYear, isoMonth [, calendar [, referenceISODay]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.PlainYearMonth requires 'new'");

        var referenceIsoDay = Argument(arguments, 3);
        if (referenceIsoDay.IsUndefined)
        {
            referenceIsoDay = 1;
        }

        var y = ToIntegerWithTruncation(Argument(arguments, 0));
        var m = ToIntegerWithTruncation(Argument(arguments, 1));
        var calendarValue = Argument(arguments, 2);
        string calendar;
        if (calendarValue.IsUndefined)
        {
            calendar = IsoCalendar;
        }
        else if (calendarValue.Cell is JsString calendarString)
        {
            calendar = CanonicalizeCalendar(calendarString.ToString());
        }
        else
        {
            return ThrowHelper.ThrowTypeError<JsValue>("The calendar must be a string");
        }

        var reference = ToIntegerWithTruncation(referenceIsoDay);
        if (!IsValidIsoDate(y, m, reference) || Math.Abs(y) > 1_000_000)
            return ThrowHelper.ThrowRangeError<JsValue>("The date is not valid");

        return CreateTemporalYearMonth(new IsoDate((long)y, (int)m, (int)reference), calendar, newTarget);
    }

    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var yearMonth = ThisPlainYearMonth(thisValue);
        var temporalYearMonthLike = Argument(arguments, 0);
        if (!IsPartialTemporalObject(temporalYearMonthLike))
            return ThrowHelper.ThrowTypeError<JsValue>("The argument must be a partial year-month object");

        var calendar = yearMonth.Calendar;
        var fields = IsoDateToFields(calendar, yearMonth.IsoDate, CalendarFieldsType.YearMonth);
        var partialYearMonth = PrepareCalendarFields(calendar, (JsObject)temporalYearMonthLike.Cell!, CalendarFieldNames.Year | CalendarFieldNames.Month | CalendarFieldNames.MonthCode, CalendarFieldNames.None, partial: true);
        fields = CalendarMergeFields(calendar, fields, partialYearMonth);
        var overflow = GetTemporalOverflowOption(GetOptionsObject(Argument(arguments, 1)));
        var isoDate = CalendarYearMonthFromFields(calendar, fields, overflow);
        return CreateTemporalYearMonth(isoDate, calendar);
    }

    /// <summary>DifferenceTemporalPlainYearMonth(operation, yearMonth, other, options)</summary>
    private static JsValue DifferenceTemporalPlainYearMonth(DifferenceOperation operation, JsPlainYearMonth yearMonth, JsValue otherValue, JsValue options)
    {
        var other = ToTemporalYearMonth(otherValue);
        var calendar = yearMonth.Calendar;
        if (!CalendarEquals(calendar, other.Calendar))
            return ThrowHelper.ThrowRangeError<JsValue>("The calendars are different");

        var resolvedOptions = GetOptionsObject(options);
        var settings = GetDifferenceSettings(operation, resolvedOptions, TemporalUnitGroup.Date, disallowWeekAndDay: true, TemporalUnit.Month, TemporalUnit.Year);
        if (CompareIsoDate(yearMonth.IsoDate, other.IsoDate) == 0)
            return CreateZeroDuration();

        var thisFields = IsoDateToFields(calendar, yearMonth.IsoDate, CalendarFieldsType.YearMonth);
        thisFields.Day = 1;
        var thisDate = CalendarDateFromFields(calendar, thisFields, TemporalOverflow.Constrain);
        var otherFields = IsoDateToFields(calendar, other.IsoDate, CalendarFieldsType.YearMonth);
        otherFields.Day = 1;
        var otherDate = CalendarDateFromFields(calendar, otherFields, TemporalOverflow.Constrain);
        var dateDifference = CalendarDateUntil(calendar, thisDate, otherDate, settings.LargestUnit);
        var yearsMonthsDifference = AdjustDateDurationRecord(dateDifference, 0, 0);
        var duration = new InternalDuration(yearsMonthsDifference, 0);
        if (settings.SmallestUnit != TemporalUnit.Month || settings.RoundingIncrement != 1)
        {
            var isoDateTime = new IsoDateTime(thisDate, TimeRecord.Midnight);
            var originEpochNs = GetUtcEpochNanoseconds(isoDateTime);
            var isoDateTimeOther = new IsoDateTime(otherDate, TimeRecord.Midnight);
            var destEpochNs = GetUtcEpochNanoseconds(isoDateTimeOther);
            duration = RoundRelativeDuration(duration, originEpochNs, destEpochNs, isoDateTime, timeZone: null, calendar, settings.LargestUnit, settings.RoundingIncrement, settings.SmallestUnit, settings.RoundingMode);
        }

        var result = TemporalDurationFromInternal(duration, TemporalUnit.Day);
        if (operation == DifferenceOperation.Since)
        {
            result = CreateNegatedTemporalDuration(result);
        }

        return result;
    }

    /// <summary>AddDurationToYearMonth(operation, yearMonth, temporalDurationLike, options)</summary>
    private static JsValue AddDurationToYearMonth(bool subtract, JsPlainYearMonth yearMonth, JsValue temporalDurationLike, JsValue options)
    {
        var duration = ToTemporalDuration(temporalDurationLike);
        if (subtract)
        {
            duration = CreateNegatedTemporalDuration(duration);
        }

        var internalDuration = ToInternalDurationRecord(duration);
        var resolvedOptions = GetOptionsObject(options);
        var overflow = GetTemporalOverflowOption(resolvedOptions);
        var durationToAdd = internalDuration.Date;
        if (durationToAdd.Weeks != 0 || durationToAdd.Days != 0 || internalDuration.Time != 0)
            return ThrowHelper.ThrowRangeError<JsValue>("Only years and months can be added to a Temporal.PlainYearMonth");

        var calendar = yearMonth.Calendar;
        var fields = IsoDateToFields(calendar, yearMonth.IsoDate, CalendarFieldsType.YearMonth);
        fields.Day = 1;
        var date = CalendarDateFromFields(calendar, fields, TemporalOverflow.Constrain);
        var addedDate = CalendarDateAdd(calendar, date, durationToAdd, overflow);
        var addedDateFields = IsoDateToFields(calendar, addedDate, CalendarFieldsType.YearMonth);
        var isoDate = CalendarYearMonthFromFields(calendar, addedDateFields, overflow);
        return CreateTemporalYearMonth(isoDate, calendar);
    }
}
