using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.ZonedDateTime (https://tc39.es/proposal-temporal/#sec-temporal-zoneddatetime-objects).</summary>
internal static class TemporalZonedDateTimeBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalZonedDateTimePrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "ZonedDateTime", 2, Construct, prototype);
        realm.DefineMethod(constructor, "from", 1, (_, _, arguments, _) => ToTemporalZonedDateTime(Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(constructor, "compare", 2, (_, _, arguments, _) =>
        {
            var one = ToTemporalZonedDateTime(Argument(arguments, 0));
            var two = ToTemporalZonedDateTime(Argument(arguments, 1));
            return one.EpochNanoseconds.CompareTo(two.EpochNanoseconds);
        });

        realm.DefineAccessor(prototype, "calendarId", (_, thisValue, _, _) => ThisZonedDateTime(thisValue).Calendar, setter: null);
        realm.DefineAccessor(prototype, "timeZoneId", (_, thisValue, _, _) => ThisZonedDateTime(thisValue).TimeZone.Identifier, setter: null);
        Func<JsValue, (string Calendar, IsoDate Date)> getDate = value =>
        {
            var zonedDateTime = ThisZonedDateTime(value);
            return (zonedDateTime.Calendar, GetIsoDateTime(zonedDateTime).Date);
        };
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "era", getDate, _ => JsValue.Undefined);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "eraYear", getDate, _ => JsValue.Undefined);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "year", getDate, d => d.Year);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "month", getDate, d => d.Month);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "monthCode", getDate, d => d.MonthCode);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "day", getDate, d => d.Day);
        TemporalBuiltinHelpers.DefineTimeGetters(realm, prototype, value => GetIsoDateTime(ThisZonedDateTime(value)).Time);
        realm.DefineAccessor(prototype, "epochMilliseconds", (_, thisValue, _, _) => TemporalBuiltinHelpers.EpochMilliseconds(ThisZonedDateTime(thisValue).EpochNanoseconds), setter: null);
        realm.DefineAccessor(prototype, "epochNanoseconds", (_, thisValue, _, _) => TemporalBuiltinHelpers.BigInt(ThisZonedDateTime(thisValue).EpochNanoseconds), setter: null);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "dayOfWeek", getDate, d => d.DayOfWeek);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "dayOfYear", getDate, d => d.DayOfYear);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "weekOfYear", getDate, d => d.WeekOfYear);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "yearOfWeek", getDate, d => d.YearOfWeek);
        realm.DefineAccessor(prototype, "hoursInDay", HoursInDay, setter: null);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "daysInWeek", getDate, d => d.DaysInWeek);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "daysInMonth", getDate, d => d.DaysInMonth);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "daysInYear", getDate, d => d.DaysInYear);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "monthsInYear", getDate, d => d.MonthsInYear);
        TemporalBuiltinHelpers.DefineCalendarGetter(realm, prototype, "inLeapYear", getDate, d => d.InLeapYear);
        realm.DefineAccessor(prototype, "offsetNanoseconds", (_, thisValue, _, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            return GetOffsetNanosecondsFor(zonedDateTime.TimeZone, zonedDateTime.EpochNanoseconds);
        }, setter: null);
        realm.DefineAccessor(prototype, "offset", (_, thisValue, _, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            return FormatUtcOffsetNanoseconds(GetOffsetNanosecondsFor(zonedDateTime.TimeZone, zonedDateTime.EpochNanoseconds));
        }, setter: null);

        realm.DefineMethod(prototype, "with", 1, With);
        realm.DefineMethod(prototype, "withPlainTime", 0, WithPlainTime);
        realm.DefineMethod(prototype, "withTimeZone", 1, (_, thisValue, arguments, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            var timeZone = ToTemporalTimeZoneIdentifier(Argument(arguments, 0));
            return CreateTemporalZonedDateTime(zonedDateTime.EpochNanoseconds, timeZone, zonedDateTime.Calendar);
        });
        realm.DefineMethod(prototype, "withCalendar", 1, (_, thisValue, arguments, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            var calendar = ToTemporalCalendarIdentifier(Argument(arguments, 0));
            return CreateTemporalZonedDateTime(zonedDateTime.EpochNanoseconds, zonedDateTime.TimeZone, calendar);
        });
        realm.DefineMethod(prototype, "add", 1, (_, thisValue, arguments, _) => AddDurationToZonedDateTime(subtract: false, ThisZonedDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "subtract", 1, (_, thisValue, arguments, _) => AddDurationToZonedDateTime(subtract: true, ThisZonedDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "until", 1, (_, thisValue, arguments, _) => DifferenceTemporalZonedDateTime(DifferenceOperation.Until, ThisZonedDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "since", 1, (_, thisValue, arguments, _) => DifferenceTemporalZonedDateTime(DifferenceOperation.Since, ThisZonedDateTime(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "round", 1, Round);
        realm.DefineMethod(prototype, "equals", 1, (_, thisValue, arguments, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            var other = ToTemporalZonedDateTime(Argument(arguments, 0));
            if (zonedDateTime.EpochNanoseconds != other.EpochNanoseconds)
                return false;

            if (!TimeZoneEquals(zonedDateTime.TimeZone, other.TimeZone))
                return false;

            return CalendarEquals(zonedDateTime.Calendar, other.Calendar);
        });
        realm.DefineMethod(prototype, "toString", 0, ToStringMethod);
        realm.DefineMethod(prototype, "toLocaleString", 0, (_, thisValue, _, _) => TemporalZonedDateTimeToString(ThisZonedDateTime(thisValue), PrecisionAuto, ShowCalendarOption.Auto, ShowTimeZoneOption.Auto, showOffset: true));
        realm.DefineMethod(prototype, "toJSON", 0, (_, thisValue, _, _) => TemporalZonedDateTimeToString(ThisZonedDateTime(thisValue), PrecisionAuto, ShowCalendarOption.Auto, ShowTimeZoneOption.Auto, showOffset: true));
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "ZonedDateTime");
        realm.DefineMethod(prototype, "startOfDay", 0, (_, thisValue, _, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            var isoDateTime = GetIsoDateTime(zonedDateTime);
            var epochNanoseconds = GetStartOfDay(zonedDateTime.TimeZone, isoDateTime.Date);
            return CreateTemporalZonedDateTime(epochNanoseconds, zonedDateTime.TimeZone, zonedDateTime.Calendar);
        });
        realm.DefineMethod(prototype, "getTimeZoneTransition", 1, GetTimeZoneTransition);
        realm.DefineMethod(prototype, "toInstant", 0, (_, thisValue, _, _) => CreateTemporalInstant(ThisZonedDateTime(thisValue).EpochNanoseconds));
        realm.DefineMethod(prototype, "toPlainDate", 0, (_, thisValue, _, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            return CreateTemporalDate(GetIsoDateTime(zonedDateTime).Date, zonedDateTime.Calendar);
        });
        realm.DefineMethod(prototype, "toPlainTime", 0, (_, thisValue, _, _) => CreateTemporalTime(GetIsoDateTime(ThisZonedDateTime(thisValue)).Time));
        realm.DefineMethod(prototype, "toPlainDateTime", 0, (_, thisValue, _, _) =>
        {
            var zonedDateTime = ThisZonedDateTime(thisValue);
            return CreateTemporalDateTime(GetIsoDateTime(zonedDateTime), zonedDateTime.Calendar);
        });
        return constructor;
    }

    private static JsZonedDateTime ThisZonedDateTime(JsValue value)
    {
        if (value.Cell is JsZonedDateTime zonedDateTime)
            return zonedDateTime;

        return ThrowHelper.ThrowTypeError<JsZonedDateTime>("this is not a Temporal.ZonedDateTime");
    }

    private static IsoDateTime GetIsoDateTime(JsZonedDateTime zonedDateTime) => GetIsoDateTimeFor(zonedDateTime.TimeZone, zonedDateTime.EpochNanoseconds);

    /// <summary>Temporal.ZonedDateTime(epochNanoseconds, timeZone [, calendar])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.ZonedDateTime requires 'new'");

        var epochNanoseconds = TemporalBuiltinHelpers.ToEpochNanoseconds(Argument(arguments, 0));
        if (Argument(arguments, 1).Cell is not JsString timeZoneString)
            return ThrowHelper.ThrowTypeError<JsValue>("The time zone must be a string");

        var timeZoneParse = ParseTimeZoneIdentifier(timeZoneString.ToString());
        var timeZone = timeZoneParse.IsOffset ? TemporalTimeZone.FromOffset(timeZoneParse.OffsetMinutes) : GetAvailableNamedTimeZone(timeZoneParse.Name!);
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

        return CreateTemporalZonedDateTime(epochNanoseconds, timeZone, calendar, newTarget);
    }

    private static JsValue HoursInDay(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var zonedDateTime = ThisZonedDateTime(thisValue);
        var timeZone = zonedDateTime.TimeZone;
        var isoDateTime = GetIsoDateTime(zonedDateTime);
        var today = isoDateTime.Date;
        var tomorrow = AddDaysToIsoDate(today, 1);
        var todayNs = GetStartOfDay(timeZone, today);
        var tomorrowNs = GetStartOfDay(timeZone, tomorrow);
        return TotalTimeDuration(tomorrowNs - todayNs, TemporalUnit.Hour);
    }

    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var zonedDateTime = ThisZonedDateTime(thisValue);
        var temporalZonedDateTimeLike = Argument(arguments, 0);
        if (!IsPartialTemporalObject(temporalZonedDateTimeLike))
            return ThrowHelper.ThrowTypeError<JsValue>("The argument must be a partial date-time object");

        var epochNs = zonedDateTime.EpochNanoseconds;
        var timeZone = zonedDateTime.TimeZone;
        var calendar = zonedDateTime.Calendar;
        var offsetNanoseconds = GetOffsetNanosecondsFor(timeZone, epochNs);
        var isoDateTime = GetIsoDateTimeFor(timeZone, epochNs);
        var fields = IsoDateToFields(calendar, isoDateTime.Date, CalendarFieldsType.Date);
        fields.SetTime(isoDateTime.Time);
        fields.OffsetString = FormatUtcOffsetNanoseconds(offsetNanoseconds);
        var partialZonedDateTime = PrepareCalendarFields(calendar, (JsObject)temporalZonedDateTimeLike.Cell!, CalendarFieldNames.YearMonthDay | CalendarFieldNames.Time | CalendarFieldNames.Offset, CalendarFieldNames.None, partial: true);
        fields = CalendarMergeFields(calendar, fields, partialZonedDateTime);
        var resolvedOptions = GetOptionsObject(Argument(arguments, 1));
        var disambiguation = GetTemporalDisambiguationOption(resolvedOptions);
        var offset = GetTemporalOffsetOption(resolvedOptions, TemporalOffsetOption.Prefer);
        var overflow = GetTemporalOverflowOption(resolvedOptions);
        var dateTimeResult = InterpretTemporalDateTimeFields(calendar, fields, overflow);
        var newOffsetNanoseconds = ParseDateTimeUtcOffset(fields.OffsetString!);
        var epochNanoseconds = InterpretIsoDateTimeOffset(dateTimeResult.Date, dateTimeResult.Time, OffsetBehaviour.Option, newOffsetNanoseconds, timeZone, disambiguation, offset, MatchBehaviour.MatchExactly);
        return CreateTemporalZonedDateTime(epochNanoseconds, timeZone, calendar);
    }

    private static JsValue WithPlainTime(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var zonedDateTime = ThisZonedDateTime(thisValue);
        var timeZone = zonedDateTime.TimeZone;
        var calendar = zonedDateTime.Calendar;
        var isoDateTime = GetIsoDateTime(zonedDateTime);
        var plainTimeLike = Argument(arguments, 0);
        Int128 epochNs;
        if (plainTimeLike.IsUndefined)
        {
            epochNs = GetStartOfDay(timeZone, isoDateTime.Date);
        }
        else
        {
            var plainTime = ToTemporalTime(plainTimeLike);
            var resultIsoDateTime = new IsoDateTime(isoDateTime.Date, plainTime.Time);
            epochNs = GetEpochNanosecondsFor(timeZone, resultIsoDateTime, TemporalDisambiguation.Compatible);
        }

        return CreateTemporalZonedDateTime(epochNs, timeZone, calendar);
    }

    private static JsValue Round(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var zonedDateTime = ThisZonedDateTime(thisValue);
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
            return CreateTemporalZonedDateTime(zonedDateTime.EpochNanoseconds, zonedDateTime.TimeZone, zonedDateTime.Calendar);

        var thisNs = zonedDateTime.EpochNanoseconds;
        var timeZone = zonedDateTime.TimeZone;
        var calendar = zonedDateTime.Calendar;
        var isoDateTime = GetIsoDateTimeFor(timeZone, thisNs);
        Int128 epochNanoseconds;
        if (smallestUnit == TemporalUnit.Day)
        {
            var dateStart = isoDateTime.Date;
            var dateEnd = AddDaysToIsoDate(dateStart, 1);
            var startNs = GetStartOfDay(timeZone, dateStart);
            var endNs = GetStartOfDay(timeZone, dateEnd);
            var dayLengthNs = endNs - startNs;
            var dayProgressNs = thisNs - startNs;
            var roundedDayNs = TemporalMath.RoundNumberToIncrement(dayProgressNs, dayLengthNs, roundingMode);
            epochNanoseconds = roundedDayNs + startNs;
        }
        else
        {
            var roundResult = RoundIsoDateTime(isoDateTime, roundingIncrement, smallestUnit, roundingMode);
            var offsetNanoseconds = GetOffsetNanosecondsFor(timeZone, thisNs);
            epochNanoseconds = InterpretIsoDateTimeOffset(roundResult.Date, roundResult.Time, OffsetBehaviour.Option, offsetNanoseconds, timeZone, TemporalDisambiguation.Compatible, TemporalOffsetOption.Prefer, MatchBehaviour.MatchExactly);
        }

        return CreateTemporalZonedDateTime(epochNanoseconds, timeZone, calendar);
    }

    private static JsValue ToStringMethod(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var zonedDateTime = ThisZonedDateTime(thisValue);
        var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
        var showCalendar = GetTemporalShowCalendarNameOption(resolvedOptions);
        var digits = GetTemporalFractionalSecondDigitsOption(resolvedOptions);
        var showOffset = GetTemporalShowOffsetOption(resolvedOptions);
        var roundingMode = GetRoundingModeOption(resolvedOptions, TemporalRoundingMode.Trunc);
        var smallestUnit = GetTemporalUnitValuedOption(resolvedOptions, "smallestUnit", required: false);
        var showTimeZone = GetTemporalShowTimeZoneNameOption(resolvedOptions);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time);
        if (smallestUnit == TemporalUnit.Hour)
            return ThrowHelper.ThrowRangeError<JsValue>("The smallest unit cannot be 'hour'");

        var precision = ToSecondsStringPrecisionRecord(smallestUnit, digits);
        return TemporalZonedDateTimeToString(zonedDateTime, precision.Precision, showCalendar, showTimeZone, showOffset, precision.Increment, precision.Unit, roundingMode);
    }

    private static JsValue GetTimeZoneTransition(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var zonedDateTime = ThisZonedDateTime(thisValue);
        var timeZone = zonedDateTime.TimeZone;
        var directionParam = GetRoundToOptions(Argument(arguments, 0), "direction");
        var next = GetDirectionOption(directionParam);
        if (timeZone.Zone is not { } zone)
            return JsValue.Null;

        var transition = next ? GetNamedTimeZoneNextTransition(zone, zonedDateTime.EpochNanoseconds) : GetNamedTimeZonePreviousTransition(zone, zonedDateTime.EpochNanoseconds);
        if (transition is not { } value)
            return JsValue.Null;

        return CreateTemporalZonedDateTime(value, timeZone, zonedDateTime.Calendar);
    }

    /// <summary>DifferenceTemporalZonedDateTime(operation, zonedDateTime, other, options)</summary>
    private static JsValue DifferenceTemporalZonedDateTime(DifferenceOperation operation, JsZonedDateTime zonedDateTime, JsValue otherValue, JsValue options)
    {
        var other = ToTemporalZonedDateTime(otherValue);
        if (!CalendarEquals(zonedDateTime.Calendar, other.Calendar))
            return ThrowHelper.ThrowRangeError<JsValue>("The calendars are different");

        var resolvedOptions = GetOptionsObject(options);
        var settings = GetDifferenceSettings(operation, resolvedOptions, TemporalUnitGroup.DateTime, disallowWeekAndDay: false, TemporalUnit.Nanosecond, TemporalUnit.Hour);
        JsDuration result;
        if (!IsDateUnit(settings.LargestUnit))
        {
            var internalDuration = DifferenceInstant(zonedDateTime.EpochNanoseconds, other.EpochNanoseconds, settings.RoundingIncrement, settings.SmallestUnit, settings.RoundingMode);
            result = TemporalDurationFromInternal(internalDuration, settings.LargestUnit);
        }
        else
        {
            if (!TimeZoneEquals(zonedDateTime.TimeZone, other.TimeZone))
                return ThrowHelper.ThrowRangeError<JsValue>("The time zones are different: use a time unit as largestUnit");

            if (zonedDateTime.EpochNanoseconds == other.EpochNanoseconds)
                return CreateZeroDuration();

            var internalDuration = DifferenceZonedDateTimeWithRounding(zonedDateTime.EpochNanoseconds, other.EpochNanoseconds, zonedDateTime.TimeZone, zonedDateTime.Calendar, settings.LargestUnit, settings.RoundingIncrement, settings.SmallestUnit, settings.RoundingMode);
            result = TemporalDurationFromInternal(internalDuration, TemporalUnit.Hour);
        }

        if (operation == DifferenceOperation.Since)
        {
            result = CreateNegatedTemporalDuration(result);
        }

        return result;
    }

    /// <summary>AddDurationToZonedDateTime(operation, zonedDateTime, temporalDurationLike, options)</summary>
    private static JsValue AddDurationToZonedDateTime(bool subtract, JsZonedDateTime zonedDateTime, JsValue temporalDurationLike, JsValue options)
    {
        var duration = ToTemporalDuration(temporalDurationLike);
        if (subtract)
        {
            duration = CreateNegatedTemporalDuration(duration);
        }

        var resolvedOptions = GetOptionsObject(options);
        var overflow = GetTemporalOverflowOption(resolvedOptions);
        var calendar = zonedDateTime.Calendar;
        var timeZone = zonedDateTime.TimeZone;
        var internalDuration = ToInternalDurationRecord(duration);
        var epochNanoseconds = AddZonedDateTime(zonedDateTime.EpochNanoseconds, timeZone, calendar, internalDuration, overflow);
        return CreateTemporalZonedDateTime(epochNanoseconds, timeZone, calendar);
    }
}
