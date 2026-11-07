using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.PlainDateTime (https://tc39.es/proposal-temporal/#sec-temporal-plaindatetime-objects).</summary>
internal static class TemporalPlainDateTimeBuiltins
{
    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("from", 1, (_, _, arguments, _) => ToTemporalDateTime(Argument(arguments, 0), Argument(arguments, 1))),
        new("compare", 2, (_, _, arguments, _) =>
        {
            var one = ToTemporalDateTime(Argument(arguments, 0));
            var two = ToTemporalDateTime(Argument(arguments, 1));
            return CompareIsoDateTime(one.IsoDateTime, two.IsoDateTime);
        }),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("with", 1, With),
        new("withPlainTime", 0, (_, thisValue, arguments, _) =>
        {
            var plainDateTime = ThisPlainDateTime(thisValue);
            var time = ToTimeRecordOrMidnight(Argument(arguments, 0));
            return CreateTemporalDateTime(new IsoDateTime(plainDateTime.IsoDateTime.Date, time), plainDateTime.Calendar);
        }),
        new("withCalendar", 1, (_, thisValue, arguments, _) =>
        {
            var plainDateTime = ThisPlainDateTime(thisValue);
            var calendar = ToTemporalCalendarIdentifier(Argument(arguments, 0));
            return CreateTemporalDateTime(plainDateTime.IsoDateTime, calendar);
        }),
        new("add", 1, (_, thisValue, arguments, _) => AddDurationToDateTime(subtract: false, ThisPlainDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("subtract", 1, (_, thisValue, arguments, _) => AddDurationToDateTime(subtract: true, ThisPlainDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("until", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainDateTime(DifferenceOperation.Until, ThisPlainDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("since", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainDateTime(DifferenceOperation.Since, ThisPlainDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("round", 1, Round),
        new("equals", 1, (_, thisValue, arguments, _) =>
        {
            var plainDateTime = ThisPlainDateTime(thisValue);
            var other = ToTemporalDateTime(Argument(arguments, 0));
            if (CompareIsoDateTime(plainDateTime.IsoDateTime, other.IsoDateTime) != 0)
                return false;

            return CalendarEquals(plainDateTime.Calendar, other.Calendar);
        }),
        new("toString", 0, ToStringMethod),
        new("toLocaleString", 0, (_, thisValue, _, _) =>
        {
            var plainDateTime = ThisPlainDateTime(thisValue);
            return IsoDateTimeToString(plainDateTime.IsoDateTime, plainDateTime.Calendar, PrecisionAuto, ShowCalendarOption.Auto);
        }),
        new("toJSON", 0, (_, thisValue, _, _) =>
        {
            var plainDateTime = ThisPlainDateTime(thisValue);
            return IsoDateTimeToString(plainDateTime.IsoDateTime, plainDateTime.Calendar, PrecisionAuto, ShowCalendarOption.Auto);
        }),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods2 =
    [
        new("toZonedDateTime", 1, (_, thisValue, arguments, _) =>
        {
            var plainDateTime = ThisPlainDateTime(thisValue);
            var timeZone = ToTemporalTimeZoneIdentifier(Argument(arguments, 0));
            var resolvedOptions = GetOptionsObject(Argument(arguments, 1));
            var disambiguation = GetTemporalDisambiguationOption(resolvedOptions);
            var epochNs = GetEpochNanosecondsFor(timeZone, plainDateTime.IsoDateTime, disambiguation);
            return CreateTemporalZonedDateTime(epochNs, timeZone, plainDateTime.Calendar);
        }),
        new("toPlainDate", 0, (_, thisValue, _, _) =>
        {
            var plainDateTime = ThisPlainDateTime(thisValue);
            return CreateTemporalDate(plainDateTime.IsoDateTime.Date, plainDateTime.Calendar);
        }),
        new("toPlainTime", 0, (_, thisValue, _, _) => CreateTemporalTime(ThisPlainDateTime(thisValue).IsoDateTime.Time)),
    ];

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalPlainDateTimePrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "PlainDateTime", 3, Construct, prototype);
        realm.DefineMethods(constructor, ConstructorMethods);

        TemporalBuiltinHelpers.DefineDateGetters(realm, prototype, value =>
        {
            var dateTime = ThisPlainDateTime(value);
            return (dateTime.Calendar, dateTime.IsoDateTime.Date);
        });
        TemporalBuiltinHelpers.DefineTimeGetters(realm, prototype, value => ThisPlainDateTime(value).IsoDateTime.Time);
        realm.DefineMethods(prototype, PrototypeMethods);
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "PlainDateTime");
        realm.DefineMethods(prototype, PrototypeMethods2);
        return constructor;
    }

    private static JsPlainDateTime ThisPlainDateTime(JsValue value)
    {
        if (value.Cell is JsPlainDateTime dateTime)
            return dateTime;

        return ThrowHelper.ThrowTypeError<JsPlainDateTime>("this is not a Temporal.PlainDateTime");
    }

    /// <summary>Temporal.PlainDateTime(isoYear, isoMonth, isoDay [, hour [, minute [, second [, millisecond [, microsecond [, nanosecond [, calendar]]]]]]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.PlainDateTime requires 'new'");

        var isoYear = ToIntegerWithTruncation(Argument(arguments, 0));
        var isoMonth = ToIntegerWithTruncation(Argument(arguments, 1));
        var isoDay = ToIntegerWithTruncation(Argument(arguments, 2));
        Span<double> time = stackalloc double[6];
        for (var i = 0; i < time.Length; i++)
        {
            var argument = Argument(arguments, i + 3);
            time[i] = argument.IsUndefined ? 0 : ToIntegerWithTruncation(argument);
        }

        var calendarValue = Argument(arguments, 9);
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

        if (!IsValidIsoDate(isoYear, isoMonth, isoDay) || Math.Abs(isoYear) > 1_000_000)
            return ThrowHelper.ThrowRangeError<JsValue>("The date is not valid");

        if (!IsValidTime(time[0], time[1], time[2], time[3], time[4], time[5]))
            return ThrowHelper.ThrowRangeError<JsValue>("The time is not valid");

        var isoDateTime = new IsoDateTime(new IsoDate((long)isoYear, (int)isoMonth, (int)isoDay), new TimeRecord(0, (int)time[0], (int)time[1], (int)time[2], (int)time[3], (int)time[4], (int)time[5]));
        return CreateTemporalDateTime(isoDateTime, calendar, newTarget);
    }

    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDateTime = ThisPlainDateTime(thisValue);
        var temporalDateTimeLike = Argument(arguments, 0);
        if (!IsPartialTemporalObject(temporalDateTimeLike))
            return ThrowHelper.ThrowTypeError<JsValue>("The argument must be a partial date-time object");

        var calendar = plainDateTime.Calendar;
        var fields = IsoDateToFields(calendar, plainDateTime.IsoDateTime.Date, CalendarFieldsType.Date);
        fields.SetTime(plainDateTime.IsoDateTime.Time);
        var partialDateTime = PrepareCalendarFields(calendar, (JsObject)temporalDateTimeLike.Cell!, CalendarFieldNames.YearMonthDay | CalendarFieldNames.Time, CalendarFieldNames.None, partial: true);
        fields = CalendarMergeFields(calendar, fields, partialDateTime);
        var overflow = GetTemporalOverflowOption(GetOptionsObject(Argument(arguments, 1)));
        var result = InterpretTemporalDateTimeFields(calendar, fields, overflow);
        return CreateTemporalDateTime(result, calendar);
    }

    private static JsValue Round(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDateTime = ThisPlainDateTime(thisValue);
        var roundTo = GetRoundToOptions(Argument(arguments, 0), "smallestUnit");
        var roundingIncrement = GetRoundingIncrementOption(roundTo);
        var roundingMode = GetRoundingModeOption(roundTo, TemporalRoundingMode.HalfExpand);
        var smallestUnit = GetTemporalUnitValuedOption(roundTo, "smallestUnit", required: true);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time, TemporalUnit.Day);
        if (smallestUnit == TemporalUnit.Day)
        {
            ValidateTemporalRoundingIncrement(roundingIncrement, 1, inclusive: true);
        }
        else
        {
            ValidateTemporalRoundingIncrement(roundingIncrement, MaximumTemporalDurationRoundingIncrement(smallestUnit), inclusive: false);
        }

        if (smallestUnit == TemporalUnit.Nanosecond && roundingIncrement == 1)
            return CreateTemporalDateTime(plainDateTime.IsoDateTime, plainDateTime.Calendar);

        var result = RoundIsoDateTime(plainDateTime.IsoDateTime, roundingIncrement, smallestUnit, roundingMode);
        return CreateTemporalDateTime(result, plainDateTime.Calendar);
    }

    private static JsValue ToStringMethod(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainDateTime = ThisPlainDateTime(thisValue);
        var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
        var showCalendar = GetTemporalShowCalendarNameOption(resolvedOptions);
        var digits = GetTemporalFractionalSecondDigitsOption(resolvedOptions);
        var roundingMode = GetRoundingModeOption(resolvedOptions, TemporalRoundingMode.Trunc);
        var smallestUnit = GetTemporalUnitValuedOption(resolvedOptions, "smallestUnit", required: false);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time);
        if (smallestUnit == TemporalUnit.Hour)
            return ThrowHelper.ThrowRangeError<JsValue>("The smallest unit cannot be 'hour'");

        var precision = ToSecondsStringPrecisionRecord(smallestUnit, digits);
        var result = RoundIsoDateTime(plainDateTime.IsoDateTime, precision.Increment, precision.Unit, roundingMode);
        if (!IsoDateTimeWithinLimits(result))
            return ThrowHelper.ThrowRangeError<JsValue>("The date and time is outside the range of Temporal.PlainDateTime");

        return IsoDateTimeToString(result, plainDateTime.Calendar, precision.Precision, showCalendar);
    }

    /// <summary>DifferenceTemporalPlainDateTime(operation, dateTime, other, options)</summary>
    private static JsValue DifferenceTemporalPlainDateTime(DifferenceOperation operation, JsPlainDateTime dateTime, JsValue otherValue, JsValue options)
    {
        var other = ToTemporalDateTime(otherValue);
        if (!CalendarEquals(dateTime.Calendar, other.Calendar))
            return ThrowHelper.ThrowRangeError<JsValue>("The calendars are different");

        var resolvedOptions = GetOptionsObject(options);
        var settings = GetDifferenceSettings(operation, resolvedOptions, TemporalUnitGroup.DateTime, disallowWeekAndDay: false, TemporalUnit.Nanosecond, TemporalUnit.Day);
        if (CompareIsoDateTime(dateTime.IsoDateTime, other.IsoDateTime) == 0)
            return CreateZeroDuration();

        var internalDuration = DifferencePlainDateTimeWithRounding(dateTime.IsoDateTime, other.IsoDateTime, dateTime.Calendar, settings.LargestUnit, settings.RoundingIncrement, settings.SmallestUnit, settings.RoundingMode);
        var result = TemporalDurationFromInternal(internalDuration, settings.LargestUnit);
        if (operation == DifferenceOperation.Since)
        {
            result = CreateNegatedTemporalDuration(result);
        }

        return result;
    }

    /// <summary>AddDurationToDateTime(operation, dateTime, temporalDurationLike, options)</summary>
    private static JsValue AddDurationToDateTime(bool subtract, JsPlainDateTime dateTime, JsValue temporalDurationLike, JsValue options)
    {
        var duration = ToTemporalDuration(temporalDurationLike);
        if (subtract)
        {
            duration = CreateNegatedTemporalDuration(duration);
        }

        var resolvedOptions = GetOptionsObject(options);
        var overflow = GetTemporalOverflowOption(resolvedOptions);
        var internalDuration = ToInternalDurationRecordWith24HourDays(duration);
        var timeResult = AddTime(dateTime.IsoDateTime.Time, internalDuration.Time);
        var dateDuration = AdjustDateDurationRecord(internalDuration.Date, timeResult.Days);
        var addedDate = CalendarDateAdd(dateTime.Calendar, dateTime.IsoDateTime.Date, dateDuration, overflow);
        return CreateTemporalDateTime(new IsoDateTime(addedDate, timeResult.WithoutDays()), dateTime.Calendar);
    }
}
