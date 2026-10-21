using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.Instant (https://tc39.es/proposal-temporal/#sec-temporal-instant-objects).</summary>
internal static class TemporalInstantBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.TemporalInstantPrototype;
        var constructor = TemporalBuiltinHelpers.CreateConstructor(intrinsics, "Instant", 1, Construct, prototype);
        realm.DefineMethod(constructor, "from", 1, (_, _, arguments, _) => ToTemporalInstant(Argument(arguments, 0)));
        realm.DefineMethod(constructor, "fromEpochMilliseconds", 1, (_, _, arguments, _) =>
        {
            var epochMilliseconds = JsOperations.ToNumber(Argument(arguments, 0));
            var milliseconds = BigIntOperations.NumberToBigInt(epochMilliseconds);
            var epochNanoseconds = milliseconds * 1_000_000;
            if (epochNanoseconds > (System.Numerics.BigInteger)TemporalMath.NsMaxInstant || epochNanoseconds < (System.Numerics.BigInteger)TemporalMath.NsMinInstant)
                return ThrowHelper.ThrowRangeError<JsValue>("The epoch milliseconds are outside the range of Temporal.Instant");

            return CreateTemporalInstant((Int128)epochNanoseconds);
        });
        realm.DefineMethod(constructor, "fromEpochNanoseconds", 1, (_, _, arguments, _) => CreateTemporalInstant(TemporalBuiltinHelpers.ToEpochNanoseconds(Argument(arguments, 0))));
        realm.DefineMethod(constructor, "compare", 2, (_, _, arguments, _) =>
        {
            var one = ToTemporalInstant(Argument(arguments, 0));
            var two = ToTemporalInstant(Argument(arguments, 1));
            return one.EpochNanoseconds.CompareTo(two.EpochNanoseconds);
        });

        realm.DefineAccessor(prototype, "epochMilliseconds", (_, thisValue, _, _) => TemporalBuiltinHelpers.EpochMilliseconds(ThisInstant(thisValue).EpochNanoseconds), setter: null);
        realm.DefineAccessor(prototype, "epochNanoseconds", (_, thisValue, _, _) => TemporalBuiltinHelpers.BigInt(ThisInstant(thisValue).EpochNanoseconds), setter: null);
        realm.DefineMethod(prototype, "add", 1, (_, thisValue, arguments, _) => AddDurationToInstant(subtract: false, ThisInstant(thisValue), Argument(arguments, 0)));
        realm.DefineMethod(prototype, "subtract", 1, (_, thisValue, arguments, _) => AddDurationToInstant(subtract: true, ThisInstant(thisValue), Argument(arguments, 0)));
        realm.DefineMethod(prototype, "until", 1, (_, thisValue, arguments, _) => DifferenceTemporalInstant(DifferenceOperation.Until, ThisInstant(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "since", 1, (_, thisValue, arguments, _) => DifferenceTemporalInstant(DifferenceOperation.Since, ThisInstant(thisValue), Argument(arguments, 0), Argument(arguments, 1)));
        realm.DefineMethod(prototype, "round", 1, Round);
        realm.DefineMethod(prototype, "equals", 1, (_, thisValue, arguments, _) =>
        {
            var instant = ThisInstant(thisValue);
            var other = ToTemporalInstant(Argument(arguments, 0));
            return instant.EpochNanoseconds == other.EpochNanoseconds;
        });
        realm.DefineMethod(prototype, "toString", 0, ToStringMethod);
        realm.DefineMethod(prototype, "toLocaleString", 0, (_, thisValue, _, _) => TemporalInstantToString(ThisInstant(thisValue).EpochNanoseconds, timeZone: null, PrecisionAuto));
        realm.DefineMethod(prototype, "toJSON", 0, (_, thisValue, _, _) => TemporalInstantToString(ThisInstant(thisValue).EpochNanoseconds, timeZone: null, PrecisionAuto));
        TemporalBuiltinHelpers.DefineValueOf(realm, prototype, "Instant");
        realm.DefineMethod(prototype, "toZonedDateTimeISO", 1, (_, thisValue, arguments, _) =>
        {
            var instant = ThisInstant(thisValue);
            var timeZone = ToTemporalTimeZoneIdentifier(Argument(arguments, 0));
            return CreateTemporalZonedDateTime(instant.EpochNanoseconds, timeZone, IsoCalendar);
        });
        return constructor;
    }

    private static JsInstant ThisInstant(JsValue value)
    {
        if (value.Cell is JsInstant instant)
            return instant;

        return ThrowHelper.ThrowTypeError<JsInstant>("this is not a Temporal.Instant");
    }

    /// <summary>Temporal.Instant(epochNanoseconds)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Temporal.Instant requires 'new'");

        var epochNanoseconds = TemporalBuiltinHelpers.ToEpochNanoseconds(Argument(arguments, 0));
        return CreateTemporalInstant(epochNanoseconds, newTarget);
    }

    private static JsValue Round(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var instant = ThisInstant(thisValue);
        var roundTo = GetRoundToOptions(Argument(arguments, 0), "smallestUnit");
        var roundingIncrement = GetRoundingIncrementOption(roundTo);
        var roundingMode = GetRoundingModeOption(roundTo, TemporalRoundingMode.HalfExpand);
        var smallestUnit = GetTemporalUnitValuedOption(roundTo, "smallestUnit", required: true);
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time);
        var maximum = smallestUnit switch
        {
            TemporalUnit.Hour => 24L,
            TemporalUnit.Minute => 1440L,
            TemporalUnit.Second => 86_400L,
            TemporalUnit.Millisecond => 86_400_000L,
            TemporalUnit.Microsecond => 86_400_000_000L,
            _ => 86_400_000_000_000L,
        };
        ValidateTemporalRoundingIncrement(roundingIncrement, maximum, inclusive: true);
        var roundedNs = RoundTemporalInstant(instant.EpochNanoseconds, roundingIncrement, smallestUnit, roundingMode);
        return CreateTemporalInstant(roundedNs);
    }

    private static JsValue ToStringMethod(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var instant = ThisInstant(thisValue);
        var resolvedOptions = GetOptionsObject(Argument(arguments, 0));
        var digits = GetTemporalFractionalSecondDigitsOption(resolvedOptions);
        var roundingMode = GetRoundingModeOption(resolvedOptions, TemporalRoundingMode.Trunc);
        var smallestUnit = GetTemporalUnitValuedOption(resolvedOptions, "smallestUnit", required: false);
        var timeZoneValue = resolvedOptions.Get("timeZone");
        ValidateTemporalUnitValue(smallestUnit, TemporalUnitGroup.Time);
        if (smallestUnit == TemporalUnit.Hour)
            return ThrowHelper.ThrowRangeError<JsValue>("The smallest unit cannot be 'hour'");

        TemporalTimeZone? timeZone = null;
        if (!timeZoneValue.IsUndefined)
        {
            timeZone = ToTemporalTimeZoneIdentifier(timeZoneValue);
        }

        var precision = ToSecondsStringPrecisionRecord(smallestUnit, digits);
        var roundedNs = RoundTemporalInstant(instant.EpochNanoseconds, precision.Increment, precision.Unit, roundingMode);
        return TemporalInstantToString(roundedNs, timeZone, precision.Precision);
    }

    /// <summary>DifferenceTemporalInstant(operation, instant, other, options)</summary>
    private static JsValue DifferenceTemporalInstant(DifferenceOperation operation, JsInstant instant, JsValue otherValue, JsValue options)
    {
        var other = ToTemporalInstant(otherValue);
        var resolvedOptions = GetOptionsObject(options);
        var settings = GetDifferenceSettings(operation, resolvedOptions, TemporalUnitGroup.Time, disallowWeekAndDay: false, TemporalUnit.Nanosecond, TemporalUnit.Second);
        var internalDuration = DifferenceInstant(instant.EpochNanoseconds, other.EpochNanoseconds, settings.RoundingIncrement, settings.SmallestUnit, settings.RoundingMode);
        var result = TemporalDurationFromInternal(internalDuration, settings.LargestUnit);
        if (operation == DifferenceOperation.Since)
        {
            result = CreateNegatedTemporalDuration(result);
        }

        return result;
    }

    /// <summary>AddDurationToInstant(operation, instant, temporalDurationLike)</summary>
    private static JsValue AddDurationToInstant(bool subtract, JsInstant instant, JsValue temporalDurationLike)
    {
        var duration = ToTemporalDuration(temporalDurationLike);
        if (subtract)
        {
            duration = CreateNegatedTemporalDuration(duration);
        }

        var largestUnit = DefaultTemporalLargestUnit(duration);
        if (IsDateUnit(largestUnit))
            return ThrowHelper.ThrowRangeError<JsValue>("Durations with days or larger units cannot be added to a Temporal.Instant");

        var internalDuration = ToInternalDurationRecordWith24HourDays(duration);
        var ns = AddInstant(instant.EpochNanoseconds, internalDuration.Time);
        return CreateTemporalInstant(ns);
    }
}
