namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Helpers shared by the built-ins of Temporal.</summary>
internal static class TemporalBuiltinHelpers
{
    /// <summary>Creates the constructor of a Temporal type, with its prototype property, constructor and @@toStringTag.</summary>
    public static JsBuiltinFunction CreateConstructor(Intrinsics intrinsics, string name, int length, BuiltinFunctionCallback construct, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction(name, length, construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Temporal." + name, PropertyFlags.Configurable);
        return constructor;
    }

    /// <summary>valueOf(): always a TypeError.</summary>
    public static void DefineValueOf(JsRealm realm, JsObject prototype, string typeName)
    {
        realm.DefineMethod(prototype, "valueOf", 0, (_, _, _, _) => ThrowHelper.ThrowTypeError<JsValue>("Do not use valueOf on a Temporal." + typeName + ": use compare() or equals()"));
    }

    /// <summary>Defines the getters that read a field of CalendarISOToDate(calendar, isoDate).</summary>
    public static void DefineCalendarGetter(JsRealm realm, JsObject prototype, string name, Func<JsValue, (string Calendar, IsoDate Date)> getDate, Func<CalendarDate, JsValue> getter)
    {
        realm.DefineAccessor(prototype, name, (_, thisValue, _, _) =>
        {
            var (calendar, date) = getDate(thisValue);
            return getter(TemporalOperations.CalendarIsoToDate(calendar, date));
        }, setter: null);
    }

    /// <summary>Defines the getters calendarId, era, eraYear, year, month, monthCode, day... of PlainDate, PlainDateTime and ZonedDateTime.</summary>
    public static void DefineDateGetters(JsRealm realm, JsObject prototype, Func<JsValue, (string Calendar, IsoDate Date)> getDate)
    {
        realm.DefineAccessor(prototype, "calendarId", (_, thisValue, _, _) => getDate(thisValue).Calendar, setter: null);
        DefineCalendarGetter(realm, prototype, "era", getDate, _ => JsValue.Undefined);
        DefineCalendarGetter(realm, prototype, "eraYear", getDate, _ => JsValue.Undefined);
        DefineCalendarGetter(realm, prototype, "year", getDate, d => d.Year);
        DefineCalendarGetter(realm, prototype, "month", getDate, d => d.Month);
        DefineCalendarGetter(realm, prototype, "monthCode", getDate, d => d.MonthCode);
        DefineCalendarGetter(realm, prototype, "day", getDate, d => d.Day);
        DefineCalendarGetter(realm, prototype, "dayOfWeek", getDate, d => d.DayOfWeek);
        DefineCalendarGetter(realm, prototype, "dayOfYear", getDate, d => d.DayOfYear);
        DefineCalendarGetter(realm, prototype, "weekOfYear", getDate, d => d.WeekOfYear);
        DefineCalendarGetter(realm, prototype, "yearOfWeek", getDate, d => d.YearOfWeek);
        DefineCalendarGetter(realm, prototype, "daysInWeek", getDate, d => d.DaysInWeek);
        DefineCalendarGetter(realm, prototype, "daysInMonth", getDate, d => d.DaysInMonth);
        DefineCalendarGetter(realm, prototype, "daysInYear", getDate, d => d.DaysInYear);
        DefineCalendarGetter(realm, prototype, "monthsInYear", getDate, d => d.MonthsInYear);
        DefineCalendarGetter(realm, prototype, "inLeapYear", getDate, d => d.InLeapYear);
    }

    /// <summary>Defines the getters hour, minute, second, millisecond, microsecond and nanosecond.</summary>
    public static void DefineTimeGetters(JsRealm realm, JsObject prototype, Func<JsValue, TimeRecord> getTime)
    {
        realm.DefineAccessor(prototype, "hour", (_, thisValue, _, _) => getTime(thisValue).Hour, setter: null);
        realm.DefineAccessor(prototype, "minute", (_, thisValue, _, _) => getTime(thisValue).Minute, setter: null);
        realm.DefineAccessor(prototype, "second", (_, thisValue, _, _) => getTime(thisValue).Second, setter: null);
        realm.DefineAccessor(prototype, "millisecond", (_, thisValue, _, _) => getTime(thisValue).Millisecond, setter: null);
        realm.DefineAccessor(prototype, "microsecond", (_, thisValue, _, _) => getTime(thisValue).Microsecond, setter: null);
        realm.DefineAccessor(prototype, "nanosecond", (_, thisValue, _, _) => getTime(thisValue).Nanosecond, setter: null);
    }

    /// <summary>Gets the result of a string conversion (a JsString value).</summary>
    public static JsValue String(string value) => JsValue.FromString(value);

    /// <summary>Gets a BigInt value from epoch nanoseconds.</summary>
    public static JsValue BigInt(Int128 value) => JsValue.FromBigInt((System.Numerics.BigInteger)value);

    /// <summary>Gets epochMilliseconds: floor(ns / 10^6).</summary>
    public static JsValue EpochMilliseconds(Int128 epochNanoseconds) => (double)(long)TemporalMath.FloorDivide(epochNanoseconds, 1_000_000);

    /// <summary>Gets the epoch nanoseconds of a BigInt argument (ToBigInt then IsValidEpochNanoseconds).</summary>
    public static Int128 ToEpochNanoseconds(JsValue value)
    {
        var bigint = BigIntOperations.ToBigInt(value);
        if (bigint > (System.Numerics.BigInteger)TemporalMath.NsMaxInstant || bigint < (System.Numerics.BigInteger)TemporalMath.NsMinInstant)
            return ThrowHelper.ThrowRangeError<Int128>("The epoch nanoseconds are outside the range of Temporal.Instant");

        return (Int128)bigint;
    }
}
