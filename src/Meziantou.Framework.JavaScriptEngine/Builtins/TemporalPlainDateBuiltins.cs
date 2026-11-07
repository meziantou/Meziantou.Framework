using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.PlainDate (https://tc39.es/proposal-temporal/#sec-temporal-plaindate-objects).</summary>
internal static class TemporalPlainDateBuiltins
{
    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("from", 1, (_, _, arguments, _) => ToTemporalDate(Argument(arguments, 0), Argument(arguments, 1))),
        new("compare", 2, Compare),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("toPlainYearMonth", 0, ToPlainYearMonth),
        new("toPlainMonthDay", 0, ToPlainMonthDay),
        new("add", 1, (_, thisValue, arguments, _) => AddDurationToDate(subtract: false, ThisPlainDate(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("subtract", 1, (_, thisValue, arguments, _) => AddDurationToDate(subtract: true, ThisPlainDate(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("with", 1, With),
        new("withCalendar", 1, WithCalendar),
        new("until", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainDate(DifferenceOperation.Until, ThisPlainDate(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("since", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainDate(DifferenceOperation.Since, ThisPlainDate(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("equals", 1, EqualsMethod),
        new("toPlainDateTime", 0, ToPlainDateTime),
        new("toZonedDateTime", 1, ToZonedDateTime),
        new("toString", 0, ToStringMethod),
        new("toLocaleString", 0, (_, thisValue, _, _) => TemporalDateToString(ThisPlainDate(thisValue), ShowCalendarOption.Auto)),
        new("toJSON", 0, (_, thisValue, _, _) => TemporalDateToString(ThisPlainDate(thisValue), ShowCalendarOption.Auto)),
    ];

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalPlainDatePrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "PlainDate", 3, Construct, prototype);
        realm.DefineMethods(constructor, ConstructorMethods);

        TemporalBuiltinHelpers.DefineDateGetters(realm, prototype, value =>
        {
            var date = ThisPlainDate(value);
            return (date.Calendar, date.IsoDate);
        });
        realm.DefineMethods(prototype, PrototypeMethods);
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "PlainDate");
        return constructor;
    }

    private static JsPlainDate ThisPlainDate(JsValue value)
    {
        if (value.Cell is JsPlainDate date)
            return date;

        return ThrowHelper.ThrowTypeError<JsPlainDate>("this is not a Temporal.PlainDate");
    }

    /// <summary>Temporal.PlainDate(isoYear, isoMonth, isoDay [, calendar])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.PlainDate requires 'new'");

        var y = ToIntegerWithTruncation(Argument(arguments, 0));
        var m = ToIntegerWithTruncation(Argument(arguments, 1));
        var d = ToIntegerWithTruncation(Argument(arguments, 2));
        var calendarValue = Argument(arguments, 3);
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

        if (!IsValidIsoDate(y, m, d) || Math.Abs(y) > 1_000_000)
            return ThrowHelper.ThrowRangeError<JsValue>("The date is not valid");

        return CreateTemporalDate(new IsoDate((long)y, (int)m, (int)d), calendar, newTarget);
    }

    private static JsValue Compare(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var one = ToTemporalDate(Argument(arguments, 0));
        var two = ToTemporalDate(Argument(arguments, 1));
        return CompareIsoDate(one.IsoDate, two.IsoDate);
    }

    private static JsValue ToPlainYearMonth(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var calendar = plainDate.Calendar;
        var fields = IsoDateToFields(calendar, plainDate.IsoDate, CalendarFieldsType.Date);
        var isoDate = CalendarYearMonthFromFields(calendar, fields, TemporalOverflow.Constrain);
        return CreateTemporalYearMonth(isoDate, calendar);
    }

    private static JsValue ToPlainMonthDay(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var calendar = plainDate.Calendar;
        var fields = IsoDateToFields(calendar, plainDate.IsoDate, CalendarFieldsType.Date);
        var isoDate = CalendarMonthDayFromFields(calendar, fields, TemporalOverflow.Constrain);
        return CreateTemporalMonthDay(isoDate, calendar);
    }

    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var temporalDateLike = Argument(arguments, 0);
        if (!IsPartialTemporalObject(temporalDateLike))
            return ThrowHelper.ThrowTypeError<JsValue>("The argument must be a partial date object");

        var calendar = plainDate.Calendar;
        var fields = IsoDateToFields(calendar, plainDate.IsoDate, CalendarFieldsType.Date);
        var partialDate = PrepareCalendarFields(calendar, (JsObject)temporalDateLike.Cell!, CalendarFieldNames.YearMonthDay, CalendarFieldNames.None, partial: true);
        fields = CalendarMergeFields(calendar, fields, partialDate);
        var overflow = GetTemporalOverflowOption(GetOptionsObject(Argument(arguments, 1)));
        var isoDate = CalendarDateFromFields(calendar, fields, overflow);
        return CreateTemporalDate(isoDate, calendar);
    }

    private static JsValue WithCalendar(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var calendar = ToTemporalCalendarIdentifier(Argument(arguments, 0));
        return CreateTemporalDate(plainDate.IsoDate, calendar);
    }

    private static JsValue EqualsMethod(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var other = ToTemporalDate(Argument(arguments, 0));
        if (CompareIsoDate(plainDate.IsoDate, other.IsoDate) != 0)
            return false;

        return CalendarEquals(plainDate.Calendar, other.Calendar);
    }

    private static JsValue ToPlainDateTime(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var time = ToTimeRecordOrMidnight(Argument(arguments, 0));
        return CreateTemporalDateTime(new IsoDateTime(plainDate.IsoDate, time), plainDate.Calendar);
    }

    private static JsValue ToZonedDateTime(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var item = Argument(arguments, 0);
        TemporalTimeZone timeZone;
        JsValue temporalTime;
        if (item.Cell is JsObject obj)
        {
            var timeZoneLike = obj.Get("timeZone");
            if (timeZoneLike.IsUndefined)
            {
                timeZone = ToTemporalTimeZoneIdentifier(item);
                temporalTime = JsValue.Undefined;
            }
            else
            {
                timeZone = ToTemporalTimeZoneIdentifier(timeZoneLike);
                temporalTime = obj.Get("plainTime");
            }
        }
        else
        {
            timeZone = ToTemporalTimeZoneIdentifier(item);
            temporalTime = JsValue.Undefined;
        }

        Int128 epochNs;
        if (temporalTime.IsUndefined)
        {
            epochNs = GetStartOfDay(timeZone, plainDate.IsoDate);
        }
        else
        {
            var plainTime = ToTemporalTime(temporalTime);
            var isoDateTime = new IsoDateTime(plainDate.IsoDate, plainTime.Time);
            if (!IsoDateTimeWithinLimits(isoDateTime))
                return ThrowHelper.ThrowRangeError<JsValue>("The date and time is outside the range of Temporal");

            epochNs = GetEpochNanosecondsFor(timeZone, isoDateTime, TemporalDisambiguation.Compatible);
        }

        return CreateTemporalZonedDateTime(epochNs, timeZone, plainDate.Calendar);
    }

    private static JsValue ToStringMethod(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDate = ThisPlainDate(thisValue);
        var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
        var showCalendar = GetTemporalShowCalendarNameOption(resolvedOptions);
        return TemporalDateToString(plainDate, showCalendar);
    }

    /// <summary>DifferenceTemporalPlainDate(operation, temporalDate, other, options)</summary>
    private static JsValue DifferenceTemporalPlainDate(DifferenceOperation operation, JsPlainDate temporalDate, JsValue otherValue, JsValue options)
    {
        var other = ToTemporalDate(otherValue);
        if (!CalendarEquals(temporalDate.Calendar, other.Calendar))
            return ThrowHelper.ThrowRangeError<JsValue>("The calendars are different");

        var resolvedOptions = GetOptionsObject(options);
        var settings = GetDifferenceSettings(operation, resolvedOptions, TemporalUnitGroup.Date, disallowWeekAndDay: false, TemporalUnit.Day, TemporalUnit.Day);
        if (CompareIsoDate(temporalDate.IsoDate, other.IsoDate) == 0)
            return CreateZeroDuration();

        var dateDifference = CalendarDateUntil(temporalDate.Calendar, temporalDate.IsoDate, other.IsoDate, settings.LargestUnit);
        var duration = new InternalDuration(dateDifference, 0);
        if (settings.SmallestUnit != TemporalUnit.Day || settings.RoundingIncrement != 1)
        {
            var isoDateTime = new IsoDateTime(temporalDate.IsoDate, TimeRecord.Midnight);
            var originEpochNs = GetUtcEpochNanoseconds(isoDateTime);
            var isoDateTimeOther = new IsoDateTime(other.IsoDate, TimeRecord.Midnight);
            var destEpochNs = GetUtcEpochNanoseconds(isoDateTimeOther);
            duration = RoundRelativeDuration(duration, originEpochNs, destEpochNs, isoDateTime, timeZone: null, temporalDate.Calendar, settings.LargestUnit, settings.RoundingIncrement, settings.SmallestUnit, settings.RoundingMode);
        }

        var result = TemporalDurationFromInternal(duration, TemporalUnit.Day);
        if (operation == DifferenceOperation.Since)
        {
            result = CreateNegatedTemporalDuration(result);
        }

        return result;
    }

    /// <summary>AddDurationToDate(operation, temporalDate, temporalDurationLike, options)</summary>
    private static JsValue AddDurationToDate(bool subtract, JsPlainDate temporalDate, JsValue temporalDurationLike, JsValue options)
    {
        var calendar = temporalDate.Calendar;
        var duration = ToTemporalDuration(temporalDurationLike);
        if (subtract)
        {
            duration = CreateNegatedTemporalDuration(duration);
        }

        var dateDuration = ToDateDurationRecordWithoutTime(duration);
        var resolvedOptions = GetOptionsObject(options);
        var overflow = GetTemporalOverflowOption(resolvedOptions);
        var result = CalendarDateAdd(calendar, temporalDate.IsoDate, dateDuration, overflow);
        return CreateTemporalDate(result, calendar);
    }
}
