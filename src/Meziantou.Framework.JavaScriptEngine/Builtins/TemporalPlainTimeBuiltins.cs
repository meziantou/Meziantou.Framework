using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.PlainTime (https://tc39.es/proposal-temporal/#sec-temporal-plaintime-objects).</summary>
internal static class TemporalPlainTimeBuiltins
{
    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("from", 1, (_, _, arguments, _) => ToTemporalTime(Argument(arguments, 0), Argument(arguments, 1))),
        new("compare", 2, (_, _, arguments, _) =>
        {
            var one = ToTemporalTime(Argument(arguments, 0));
            var two = ToTemporalTime(Argument(arguments, 1));
            return CompareTimeRecord(one.Time, two.Time);
        }),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("add", 1, (_, thisValue, arguments, _) => AddDurationToTime(subtract: false, ThisPlainTime(thisValue), Argument(arguments, 0))),
        new("subtract", 1, (_, thisValue, arguments, _) => AddDurationToTime(subtract: true, ThisPlainTime(thisValue), Argument(arguments, 0))),
        new("with", 1, With),
        new("until", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainTime(DifferenceOperation.Until, ThisPlainTime(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("since", 1, (_, thisValue, arguments, _) => DifferenceTemporalPlainTime(DifferenceOperation.Since, ThisPlainTime(thisValue), Argument(arguments, 0), Argument(arguments, 1))),
        new("round", 1, Round),
        new("equals", 1, (_, thisValue, arguments, _) =>
        {
            var plainTime = ThisPlainTime(thisValue);
            var other = ToTemporalTime(Argument(arguments, 0));
            return CompareTimeRecord(plainTime.Time, other.Time) == 0;
        }),
        new("toString", 0, ToStringMethod),
        new("toLocaleString", 0, (_, thisValue, _, _) => TimeRecordToString(ThisPlainTime(thisValue).Time, PrecisionAuto)),
        new("toJSON", 0, (_, thisValue, _, _) => TimeRecordToString(ThisPlainTime(thisValue).Time, PrecisionAuto)),
    ];

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalPlainTimePrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "PlainTime", 0, Construct, prototype);
        realm.DefineMethods(constructor, ConstructorMethods);

        TemporalBuiltinHelpers.DefineTimeGetters(realm, prototype, value => ThisPlainTime(value).Time);
        realm.DefineMethods(prototype, PrototypeMethods);
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "PlainTime");
        return constructor;
    }

    private static JsPlainTime ThisPlainTime(JsValue value)
    {
        if (value.Cell is JsPlainTime time)
            return time;

        return ThrowHelper.ThrowTypeError<JsPlainTime>("this is not a Temporal.PlainTime");
    }

    /// <summary>Temporal.PlainTime([hour [, minute [, second [, millisecond [, microsecond [, nanosecond]]]]]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.PlainTime requires 'new'");

        Span<double> values = stackalloc double[6];
        for (var i = 0; i < values.Length; i++)
        {
            var argument = Argument(arguments, i);
            values[i] = argument.IsUndefined ? 0 : ToIntegerWithTruncation(argument);
        }

        if (!IsValidTime(values[0], values[1], values[2], values[3], values[4], values[5]))
            return ThrowHelper.ThrowRangeError<JsValue>("The time is not valid");

        return CreateTemporalTime(new TimeRecord(0, (int)values[0], (int)values[1], (int)values[2], (int)values[3], (int)values[4], (int)values[5]), newTarget);
    }

    private static JsValue With(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainTime = ThisPlainTime(thisValue);
        var temporalTimeLike = Argument(arguments, 0);
        if (!IsPartialTemporalObject(temporalTimeLike))
            return ThrowHelper.ThrowTypeError<JsValue>("The argument must be a partial time object");

        var partialTime = ToTemporalTimeRecord((JsObject)temporalTimeLike.Cell!, partial: true);
        var time = plainTime.Time;
        var hour = double.IsNaN(partialTime.Hour) ? time.Hour : partialTime.Hour;
        var minute = double.IsNaN(partialTime.Minute) ? time.Minute : partialTime.Minute;
        var second = double.IsNaN(partialTime.Second) ? time.Second : partialTime.Second;
        var millisecond = double.IsNaN(partialTime.Millisecond) ? time.Millisecond : partialTime.Millisecond;
        var microsecond = double.IsNaN(partialTime.Microsecond) ? time.Microsecond : partialTime.Microsecond;
        var nanosecond = double.IsNaN(partialTime.Nanosecond) ? time.Nanosecond : partialTime.Nanosecond;
        var overflow = GetTemporalOverflowOption(GetOptionsObject(Argument(arguments, 1)));
        var result = RegulateTime(hour, minute, second, millisecond, microsecond, nanosecond, overflow);
        return CreateTemporalTime(result);
    }

    private static JsValue Round(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainTime = ThisPlainTime(thisValue);
        var roundTo = GetRoundToOptions(Argument(arguments, 0), "smallestUnit");
        var roundingIncrement = GetRoundingIncrementOption(roundTo);
        var roundingMode = GetRoundingModeOption(roundTo, TemporalRoundingMode.HalfExpand);
        var smallestUnit = GetTemporalUnitValuedOption(roundTo, "smallestUnit", required: true);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time);
        var maximum = MaximumTemporalDurationRoundingIncrement(smallestUnit);
        ValidateTemporalRoundingIncrement(roundingIncrement, maximum, inclusive: false);
        var result = RoundTime(plainTime.Time, roundingIncrement, smallestUnit, roundingMode);
        return CreateTemporalTime(result);
    }

    private static JsValue ToStringMethod(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var plainTime = ThisPlainTime(thisValue);
        var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
        var digits = GetTemporalFractionalSecondDigitsOption(resolvedOptions);
        var roundingMode = GetRoundingModeOption(resolvedOptions, TemporalRoundingMode.Trunc);
        var smallestUnit = GetTemporalUnitValuedOption(resolvedOptions, "smallestUnit", required: false);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time);
        if (smallestUnit == TemporalUnit.Hour)
            return ThrowHelper.ThrowRangeError<JsValue>("The smallest unit cannot be 'hour'");

        var precision = ToSecondsStringPrecisionRecord(smallestUnit, digits);
        var roundResult = RoundTime(plainTime.Time, precision.Increment, precision.Unit, roundingMode);
        return TimeRecordToString(roundResult, precision.Precision);
    }

    /// <summary>DifferenceTemporalPlainTime(operation, temporalTime, other, options)</summary>
    private static JsValue DifferenceTemporalPlainTime(DifferenceOperation operation, JsPlainTime temporalTime, JsValue otherValue, JsValue options)
    {
        var other = ToTemporalTime(otherValue);
        var resolvedOptions = GetOptionsObject(options);
        var settings = GetDifferenceSettings(operation, resolvedOptions, TemporalUnitGroup.Time, disallowWeekAndDay: false, TemporalUnit.Nanosecond, TemporalUnit.Hour);
        var timeDuration = DifferenceTime(temporalTime.Time, other.Time);
        timeDuration = RoundTimeDuration(timeDuration, settings.RoundingIncrement, settings.SmallestUnit, settings.RoundingMode);
        var result = TemporalDurationFromInternal(new InternalDuration(default, timeDuration), settings.LargestUnit);
        if (operation == DifferenceOperation.Since)
        {
            result = CreateNegatedTemporalDuration(result);
        }

        return result;
    }

    /// <summary>AddDurationToTime(operation, temporalTime, temporalDurationLike)</summary>
    private static JsValue AddDurationToTime(bool subtract, JsPlainTime temporalTime, JsValue temporalDurationLike)
    {
        var duration = ToTemporalDuration(temporalDurationLike);
        if (subtract)
        {
            duration = CreateNegatedTemporalDuration(duration);
        }

        var internalDuration = ToInternalDurationRecord(duration);
        var result = AddTime(temporalTime.Time, internalDuration.Time);
        return CreateTemporalTime(result);
    }
}
