using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.Duration (https://tc39.es/proposal-temporal/#sec-temporal-duration-objects).</summary>
internal static class TemporalDurationBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalDurationPrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "Duration", 0, Construct, prototype);
        realm.DefineMethod(constructor, "from", 1, (_, _, arguments, _) => ToTemporalDuration(Argument(arguments, 0)));
        realm.DefineMethod(constructor, "compare", 2, Compare);

        realm.DefineAccessor(prototype, "years", (_, thisValue, _, _) => ThisDuration(thisValue).Years, setter: null);
        realm.DefineAccessor(prototype, "months", (_, thisValue, _, _) => ThisDuration(thisValue).Months, setter: null);
        realm.DefineAccessor(prototype, "weeks", (_, thisValue, _, _) => ThisDuration(thisValue).Weeks, setter: null);
        realm.DefineAccessor(prototype, "days", (_, thisValue, _, _) => ThisDuration(thisValue).Days, setter: null);
        realm.DefineAccessor(prototype, "hours", (_, thisValue, _, _) => ThisDuration(thisValue).Hours, setter: null);
        realm.DefineAccessor(prototype, "minutes", (_, thisValue, _, _) => ThisDuration(thisValue).Minutes, setter: null);
        realm.DefineAccessor(prototype, "seconds", (_, thisValue, _, _) => ThisDuration(thisValue).Seconds, setter: null);
        realm.DefineAccessor(prototype, "milliseconds", (_, thisValue, _, _) => ThisDuration(thisValue).Milliseconds, setter: null);
        realm.DefineAccessor(prototype, "microseconds", (_, thisValue, _, _) => ThisDuration(thisValue).Microseconds, setter: null);
        realm.DefineAccessor(prototype, "nanoseconds", (_, thisValue, _, _) => ThisDuration(thisValue).Nanoseconds, setter: null);
        realm.DefineAccessor(prototype, "sign", (_, thisValue, _, _) => DurationSign(ThisDuration(thisValue)), setter: null);
        realm.DefineAccessor(prototype, "blank", (_, thisValue, _, _) => DurationSign(ThisDuration(thisValue)) == 0, setter: null);
        realm.DefineMethod(prototype, "with", 1, With);
        realm.DefineMethod(prototype, "negated", 0, (_, thisValue, _, _) => CreateNegatedTemporalDuration(ThisDuration(thisValue)));
        realm.DefineMethod(prototype, "abs", 0, (_, thisValue, _, _) =>
        {
            var duration = ThisDuration(thisValue);
            return CreateTemporalDuration(Math.Abs(duration.Years), Math.Abs(duration.Months), Math.Abs(duration.Weeks), Math.Abs(duration.Days), Math.Abs(duration.Hours), Math.Abs(duration.Minutes), Math.Abs(duration.Seconds), Math.Abs(duration.Milliseconds), Math.Abs(duration.Microseconds), Math.Abs(duration.Nanoseconds));
        });
        realm.DefineMethod(prototype, "add", 1, (_, thisValue, arguments, _) => AddDurations(subtract: false, ThisDuration(thisValue), Argument(arguments, 0)));
        realm.DefineMethod(prototype, "subtract", 1, (_, thisValue, arguments, _) => AddDurations(subtract: true, ThisDuration(thisValue), Argument(arguments, 0)));
        realm.DefineMethod(prototype, "round", 1, Round);
        realm.DefineMethod(prototype, "total", 1, Total);
        realm.DefineMethod(prototype, "toString", 0, ToStringMethod);
        realm.DefineMethod(prototype, "toJSON", 0, (_, thisValue, _, _) => TemporalDurationToString(ThisDuration(thisValue), PrecisionAuto));
        realm.DefineMethod(prototype, "toLocaleString", 0, (_, thisValue, _, _) => TemporalDurationToString(ThisDuration(thisValue), PrecisionAuto));
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "Duration");
        return constructor;
    }

    private static JsDuration ThisDuration(JsValue value)
    {
        if (value.Cell is JsDuration duration)
            return duration;

        return ThrowHelper.ThrowTypeError<JsDuration>("this is not a Temporal.Duration");
    }

    /// <summary>Temporal.Duration([years [, months [, weeks [, days [, hours [, minutes [, seconds [, milliseconds [, microseconds [, nanoseconds]]]]]]]]]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.Duration requires 'new'");

        Span<double> values = stackalloc double[10];
        for (var i = 0; i < values.Length; i++)
        {
            var argument = Argument(arguments, i);
            values[i] = argument.IsUndefined ? 0 : ToIntegerIfIntegral(argument);
        }

        return CreateTemporalDuration(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], newTarget);
    }

    /// <summary>Temporal.Duration.compare(one, two [, options])</summary>
    private static JsValue Compare(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var one = ToTemporalDuration(Argument(arguments, 0));
        var two = ToTemporalDuration(Argument(arguments, 1));
        var resolvedOptions = GetOptionsObject(Argument(arguments, 2));
        var relativeToRecord = GetTemporalRelativeToOption(resolvedOptions);
        if (one.Years == two.Years && one.Months == two.Months && one.Weeks == two.Weeks && one.Days == two.Days && one.Hours == two.Hours && one.Minutes == two.Minutes
            && one.Seconds == two.Seconds && one.Milliseconds == two.Milliseconds && one.Microseconds == two.Microseconds && one.Nanoseconds == two.Nanoseconds)
        {
            return 0;
        }

        var zonedRelativeTo = relativeToRecord.ZonedRelativeTo;
        var plainRelativeTo = relativeToRecord.PlainRelativeTo;
        var largestUnit1 = DefaultTemporalLargestUnit(one);
        var largestUnit2 = DefaultTemporalLargestUnit(two);
        var duration1 = ToInternalDurationRecord(one);
        var duration2 = ToInternalDurationRecord(two);
        if (zonedRelativeTo is not null && (IsDateUnit(largestUnit1) || IsDateUnit(largestUnit2)))
        {
            var timeZone = zonedRelativeTo.TimeZone;
            var calendar = zonedRelativeTo.Calendar;
            var after1 = AddZonedDateTime(zonedRelativeTo.EpochNanoseconds, timeZone, calendar, duration1, TemporalOverflow.Constrain);
            var after2 = AddZonedDateTime(zonedRelativeTo.EpochNanoseconds, timeZone, calendar, duration2, TemporalOverflow.Constrain);
            return after1.CompareTo(after2);
        }

        long days1;
        long days2;
        if (IsCalendarUnit(largestUnit1) || IsCalendarUnit(largestUnit2))
        {
            if (plainRelativeTo is null)
                return ThrowHelper.ThrowRangeError<JsValue>("A relativeTo option is required to compare durations with years, months or weeks");

            days1 = DateDurationDays(duration1.Date, plainRelativeTo);
            days2 = DateDurationDays(duration2.Date, plainRelativeTo);
        }
        else
        {
            days1 = (long)one.Days;
            days2 = (long)two.Days;
        }

        var timeDuration1 = Add24HourDaysToTimeDuration(duration1.Time, days1);
        var timeDuration2 = Add24HourDaysToTimeDuration(duration2.Time, days2);
        return timeDuration1.CompareTo(timeDuration2);
    }

    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var duration = ThisDuration(thisValue);
        var temporalDurationLike = ToTemporalPartialDurationRecord(Argument(arguments, 0));
        return CreateTemporalDuration(
            temporalDurationLike.Years ?? duration.Years,
            temporalDurationLike.Months ?? duration.Months,
            temporalDurationLike.Weeks ?? duration.Weeks,
            temporalDurationLike.Days ?? duration.Days,
            temporalDurationLike.Hours ?? duration.Hours,
            temporalDurationLike.Minutes ?? duration.Minutes,
            temporalDurationLike.Seconds ?? duration.Seconds,
            temporalDurationLike.Milliseconds ?? duration.Milliseconds,
            temporalDurationLike.Microseconds ?? duration.Microseconds,
            temporalDurationLike.Nanoseconds ?? duration.Nanoseconds);
    }

    /// <summary>Temporal.Duration.prototype.round(roundTo)</summary>
    private static JsValue Round(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var duration = ThisDuration(thisValue);
        var roundTo = GetRoundToOptions(Argument(arguments, 0), "smallestUnit");
        var smallestUnitPresent = true;
        var largestUnitPresent = true;
        var largestUnit = GetTemporalUnitValuedOption(roundTo, "largestUnit", required: false);
        var relativeToRecord = GetTemporalRelativeToOption(roundTo);
        var zonedRelativeTo = relativeToRecord.ZonedRelativeTo;
        var plainRelativeTo = relativeToRecord.PlainRelativeTo;
        var roundingIncrement = GetRoundingIncrementOption(roundTo);
        var roundingMode = GetRoundingModeOption(roundTo, TemporalRoundingMode.HalfExpand);
        var smallestUnit = GetTemporalUnitValuedOption(roundTo, "smallestUnit", required: false);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.DateTime);
        if (smallestUnit == TemporalUnit.Unset)
        {
            smallestUnitPresent = false;
            smallestUnit = TemporalUnit.Nanosecond;
        }

        var existingLargestUnit = DefaultTemporalLargestUnit(duration);
        var defaultLargestUnit = LargerOfTwoTemporalUnits(existingLargestUnit, smallestUnit);
        if (largestUnit == TemporalUnit.Unset)
        {
            largestUnitPresent = false;
            largestUnit = defaultLargestUnit;
        }
        else if (largestUnit == TemporalUnit.Auto)
        {
            largestUnit = defaultLargestUnit;
        }

        if (!smallestUnitPresent && !largestUnitPresent)
            return ThrowHelper.ThrowRangeError<JsValue>("One of smallestUnit or largestUnit is required");

        if (LargerOfTwoTemporalUnits(largestUnit, smallestUnit) != largestUnit)
            return ThrowHelper.ThrowRangeError<JsValue>("The largest unit is smaller than the smallest unit");

        var maximum = MaximumTemporalDurationRoundingIncrement(smallestUnit);
        if (maximum != 0)
        {
            ValidateTemporalRoundingIncrement(roundingIncrement, maximum, inclusive: false);
        }

        if (roundingIncrement > 1 && largestUnit != smallestUnit && IsDateUnit(smallestUnit))
            return ThrowHelper.ThrowRangeError<JsValue>("A rounding increment greater than 1 for a date unit requires largestUnit to be the same as smallestUnit");

        InternalDuration internalDuration;
        if (zonedRelativeTo is not null)
        {
            internalDuration = ToInternalDurationRecord(duration);
            var timeZone = zonedRelativeTo.TimeZone;
            var calendar = zonedRelativeTo.Calendar;
            var relativeEpochNs = zonedRelativeTo.EpochNanoseconds;
            var targetEpochNs = AddZonedDateTime(relativeEpochNs, timeZone, calendar, internalDuration, TemporalOverflow.Constrain);
            internalDuration = DifferenceZonedDateTimeWithRounding(relativeEpochNs, targetEpochNs, timeZone, calendar, largestUnit, roundingIncrement, smallestUnit, roundingMode);
            if (IsDateUnit(largestUnit))
            {
                largestUnit = TemporalUnit.Hour;
            }

            return TemporalDurationFromInternal(internalDuration, largestUnit);
        }

        if (plainRelativeTo is not null)
        {
            internalDuration = ToInternalDurationRecordWith24HourDays(duration);
            var targetTime = AddTime(TimeRecord.Midnight, internalDuration.Time);
            var calendar = plainRelativeTo.Calendar;
            var dateDuration = AdjustDateDurationRecord(internalDuration.Date, targetTime.Days);
            var targetDate = CalendarDateAdd(calendar, plainRelativeTo.IsoDate, dateDuration, TemporalOverflow.Constrain);
            var isoDateTime = new IsoDateTime(plainRelativeTo.IsoDate, TimeRecord.Midnight);
            var targetDateTime = new IsoDateTime(targetDate, targetTime.WithoutDays());
            internalDuration = DifferencePlainDateTimeWithRounding(isoDateTime, targetDateTime, calendar, largestUnit, roundingIncrement, smallestUnit, roundingMode);
            return TemporalDurationFromInternal(internalDuration, largestUnit);
        }

        if (IsCalendarUnit(existingLargestUnit) || IsCalendarUnit(largestUnit))
            return ThrowHelper.ThrowRangeError<JsValue>("A relativeTo option is required to round durations with years, months or weeks");

        internalDuration = ToInternalDurationRecordWith24HourDays(duration);
        if (smallestUnit == TemporalUnit.Day)
        {
            // RoundNumberToIncrement(TotalTimeDuration(time, day), increment, mode): exact on the nanoseconds
            var roundedNs = TemporalMath.RoundNumberToIncrement(internalDuration.Time, (Int128)roundingIncrement * TemporalMath.NsPerDay, roundingMode);
            var days = (long)(roundedNs / TemporalMath.NsPerDay);
            var dateDuration = CreateDateDurationRecord(0, 0, 0, days);
            internalDuration = new InternalDuration(dateDuration, 0);
        }
        else
        {
            var timeDuration = RoundTimeDuration(internalDuration.Time, roundingIncrement, smallestUnit, roundingMode);
            internalDuration = new InternalDuration(default, timeDuration);
        }

        return TemporalDurationFromInternal(internalDuration, largestUnit);
    }

    /// <summary>Temporal.Duration.prototype.total(totalOf)</summary>
    private static JsValue Total(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var duration = ThisDuration(thisValue);
        var totalOf = GetRoundToOptions(Argument(arguments, 0), "unit");
        var relativeToRecord = GetTemporalRelativeToOption(totalOf);
        var zonedRelativeTo = relativeToRecord.ZonedRelativeTo;
        var plainRelativeTo = relativeToRecord.PlainRelativeTo;
        var unit = GetTemporalUnitValuedOption(totalOf, "unit", required: true);
        ValidateTemporalUnitValue(unit, TemporalUnitGroup.DateTime);
        double total;
        if (zonedRelativeTo is not null)
        {
            var internalDuration = ToInternalDurationRecord(duration);
            var timeZone = zonedRelativeTo.TimeZone;
            var calendar = zonedRelativeTo.Calendar;
            var relativeEpochNs = zonedRelativeTo.EpochNanoseconds;
            var targetEpochNs = AddZonedDateTime(relativeEpochNs, timeZone, calendar, internalDuration, TemporalOverflow.Constrain);
            total = DifferenceZonedDateTimeWithTotal(relativeEpochNs, targetEpochNs, timeZone, calendar, unit);
        }
        else if (plainRelativeTo is not null)
        {
            var internalDuration = ToInternalDurationRecordWith24HourDays(duration);
            var targetTime = AddTime(TimeRecord.Midnight, internalDuration.Time);
            var calendar = plainRelativeTo.Calendar;
            var dateDuration = AdjustDateDurationRecord(internalDuration.Date, targetTime.Days);
            var targetDate = CalendarDateAdd(calendar, plainRelativeTo.IsoDate, dateDuration, TemporalOverflow.Constrain);
            var isoDateTime = new IsoDateTime(plainRelativeTo.IsoDate, TimeRecord.Midnight);
            var targetDateTime = new IsoDateTime(targetDate, targetTime.WithoutDays());
            total = DifferencePlainDateTimeWithTotal(isoDateTime, targetDateTime, calendar, unit);
        }
        else
        {
            var largestUnit = DefaultTemporalLargestUnit(duration);
            if (IsCalendarUnit(largestUnit) || IsCalendarUnit(unit))
                return ThrowHelper.ThrowRangeError<JsValue>("A relativeTo option is required to total durations with years, months or weeks");

            var internalDuration = ToInternalDurationRecordWith24HourDays(duration);
            total = TotalTimeDuration(internalDuration.Time, unit);
        }

        return total;
    }

    /// <summary>Temporal.Duration.prototype.toString([options])</summary>
    private static JsValue ToStringMethod(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var duration = ThisDuration(thisValue);
        var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
        var digits = GetTemporalFractionalSecondDigitsOption(resolvedOptions);
        var roundingMode = GetRoundingModeOption(resolvedOptions, TemporalRoundingMode.Trunc);
        var smallestUnit = GetTemporalUnitValuedOption(resolvedOptions, "smallestUnit", required: false);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time);
        if (smallestUnit is TemporalUnit.Hour or TemporalUnit.Minute)
            return ThrowHelper.ThrowRangeError<JsValue>("The smallest unit cannot be 'hour' or 'minute'");

        var precision = ToSecondsStringPrecisionRecord(smallestUnit, digits);
        if (precision.Unit == TemporalUnit.Nanosecond && precision.Increment == 1)
            return TemporalDurationToString(duration, precision.Precision);

        var largestUnit = DefaultTemporalLargestUnit(duration);
        var internalDuration = ToInternalDurationRecord(duration);
        var timeDuration = RoundTimeDuration(internalDuration.Time, precision.Increment, precision.Unit, roundingMode);
        internalDuration = new InternalDuration(internalDuration.Date, timeDuration);
        var roundedLargestUnit = LargerOfTwoTemporalUnits(largestUnit, TemporalUnit.Second);
        var roundedDuration = TemporalDurationFromInternal(internalDuration, roundedLargestUnit);
        return TemporalDurationToString(roundedDuration, precision.Precision);
    }
}
