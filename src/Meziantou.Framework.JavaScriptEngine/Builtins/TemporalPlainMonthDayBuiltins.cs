using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.PlainMonthDay (https://tc39.es/proposal-temporal/#sec-temporal-plainmonthday-objects).</summary>
internal static class TemporalPlainMonthDayBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalPlainMonthDayPrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "PlainMonthDay", 2, Construct, prototype);
        realm.DefineMethod(constructor, "from", 1, (_, _, arguments, _) => ToTemporalMonthDay(Argument(arguments, 0), Argument(arguments, 1)));

        Func<JsValue, (string Calendar, IsoDate Date)> getDate = value =>
        {
            var monthDay = ThisPlainMonthDay(value);
            return (monthDay.Calendar, monthDay.IsoDate);
        };
        realm.DefineAccessor(prototype, "calendarId", (_, thisValue, _, _) => ThisPlainMonthDay(thisValue).Calendar, setter: null);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "monthCode", getDate, d => d.MonthCode);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "day", getDate, d => d.Day);
        realm.DefineMethod(prototype, "with", 1, With);
        realm.DefineMethod(prototype, "equals", 1, (_, thisValue, arguments, _) =>
        {
            var monthDay = ThisPlainMonthDay(thisValue);
            var other = ToTemporalMonthDay(Argument(arguments, 0));
            if (CompareIsoDate(monthDay.IsoDate, other.IsoDate) != 0)
                return false;

            return CalendarEquals(monthDay.Calendar, other.Calendar);
        });
        realm.DefineMethod(prototype, "toString", 0, (_, thisValue, arguments, _) =>
        {
            var monthDay = ThisPlainMonthDay(thisValue);
            var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
            var showCalendar = GetTemporalShowCalendarNameOption(resolvedOptions);
            return TemporalMonthDayToString(monthDay, showCalendar);
        });
        realm.DefineMethod(prototype, "toLocaleString", 0, (_, thisValue, _, _) => TemporalMonthDayToString(ThisPlainMonthDay(thisValue), ShowCalendarOption.Auto));
        realm.DefineMethod(prototype, "toJSON", 0, (_, thisValue, _, _) => TemporalMonthDayToString(ThisPlainMonthDay(thisValue), ShowCalendarOption.Auto));
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "PlainMonthDay");
        realm.DefineMethod(prototype, "toPlainDate", 1, (_, thisValue, arguments, _) =>
        {
            var monthDay = ThisPlainMonthDay(thisValue);
            if (Argument(arguments, 0).Cell is not JsObject item)
                return ThrowHelper.ThrowTypeError<JsValue>("The argument must be an object");

            var calendar = monthDay.Calendar;
            var fields = IsoDateToFields(calendar, monthDay.IsoDate, CalendarFieldsType.MonthDay);
            var inputFields = PrepareCalendarFields(calendar, item, CalendarFieldNames.Year, CalendarFieldNames.None, partial: false);
            var mergedFields = CalendarMergeFields(calendar, fields, inputFields);
            var isoDate = CalendarDateFromFields(calendar, mergedFields, TemporalOverflow.Constrain);
            return CreateTemporalDate(isoDate, calendar);
        });
        return constructor;
    }

    private static JsPlainMonthDay ThisPlainMonthDay(JsValue value)
    {
        if (value.Cell is JsPlainMonthDay monthDay)
            return monthDay;

        return ThrowHelper.ThrowTypeError<JsPlainMonthDay>("this is not a Temporal.PlainMonthDay");
    }

    /// <summary>Temporal.PlainMonthDay(isoMonth, isoDay [, calendar [, referenceISOYear]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.PlainMonthDay requires 'new'");

        var referenceIsoYear = Argument(arguments, 3);
        if (referenceIsoYear.IsUndefined)
        {
            referenceIsoYear = 1972;
        }

        var m = ToIntegerWithTruncation(Argument(arguments, 0));
        var d = ToIntegerWithTruncation(Argument(arguments, 1));
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

        var y = ToIntegerWithTruncation(referenceIsoYear);
        if (!IsValidIsoDate(y, m, d) || Math.Abs(y) > 1_000_000)
            return ThrowHelper.ThrowRangeError<JsValue>("The date is not valid");

        return CreateTemporalMonthDay(new IsoDate((long)y, (int)m, (int)d), calendar, newTarget);
    }

    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var monthDay = ThisPlainMonthDay(thisValue);
        var temporalMonthDayLike = Argument(arguments, 0);
        if (!IsPartialTemporalObject(temporalMonthDayLike))
            return ThrowHelper.ThrowTypeError<JsValue>("The argument must be a partial month-day object");

        var calendar = monthDay.Calendar;
        var fields = IsoDateToFields(calendar, monthDay.IsoDate, CalendarFieldsType.MonthDay);
        var partialMonthDay = PrepareCalendarFields(calendar, (JsObject)temporalMonthDayLike.Cell!, CalendarFieldNames.YearMonthDay, CalendarFieldNames.None, partial: true);
        fields = CalendarMergeFields(calendar, fields, partialMonthDay);
        var overflow = GetTemporalOverflowOption(GetOptionsObject(Argument(arguments, 1)));
        var isoDate = CalendarMonthDayFromFields(calendar, fields, overflow);
        return CreateTemporalMonthDay(isoDate, calendar);
    }
}
