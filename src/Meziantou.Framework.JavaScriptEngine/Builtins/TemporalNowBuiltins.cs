using static Meziantou.Framework.JavaScriptEngine.TemporalOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Temporal.Now (https://tc39.es/proposal-temporal/#sec-temporal-now-object).</summary>
internal static class TemporalNowBuiltins
{
    private static readonly BuiltinMethodTable NowMethods =
    [
        new("timeZoneId", 0, (function, _, _, _) => SystemTimeZoneIdentifier(function.Realm.Engine).Identifier),
        new("instant", 0, (function, _, _, _) => CreateTemporalInstant(SystemUtcEpochNanoseconds(function.Realm.Engine))),
        new("plainDateTimeISO", 0, (function, _, arguments, _) => CreateTemporalDateTime(SystemDateTime(function.Realm.Engine, Argument(arguments, 0)), IsoCalendar)),
        new("zonedDateTimeISO", 0, (function, _, arguments, _) =>
        {
            var engine = function.Realm.Engine;
            var temporalTimeZoneLike = Argument(arguments, 0);
            var timeZone = temporalTimeZoneLike.IsUndefined ? SystemTimeZoneIdentifier(engine) : ToTemporalTimeZoneIdentifier(temporalTimeZoneLike);
            var ns = SystemUtcEpochNanoseconds(engine);
            return CreateTemporalZonedDateTime(ns, timeZone, IsoCalendar);
        }),
        new("plainDateISO", 0, (function, _, arguments, _) => CreateTemporalDate(SystemDateTime(function.Realm.Engine, Argument(arguments, 0)).Date, IsoCalendar)),
        new("plainTimeISO", 0, (function, _, arguments, _) => CreateTemporalTime(SystemDateTime(function.Realm.Engine, Argument(arguments, 0)).Time)),
    ];

    public static JsObject Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var now = new JsObject(intrinsics.ObjectPrototype);
        now.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Temporal.Now", PropertyFlags.Configurable);
        realm.DefineMethods(now, NowMethods);
        return now;
    }

    /// <summary>SystemUTCEpochNanoseconds(): the current time of the TimeProvider of the engine, clamped to the range of Temporal.Instant.</summary>
    public static Int128 SystemUtcEpochNanoseconds(JsEngine engine)
    {
        var now = engine.Options.TimeProvider.GetUtcNow();
        var ns = (Int128)(now.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100;
        return Int128.Clamp(ns, TemporalMath.NsMinInstant, TemporalMath.NsMaxInstant);
    }

    /// <summary>SystemDateTime(temporalTimeZoneLike)</summary>
    private static IsoDateTime SystemDateTime(JsEngine engine, JsValue temporalTimeZoneLike)
    {
        var timeZone = temporalTimeZoneLike.IsUndefined ? SystemTimeZoneIdentifier(engine) : ToTemporalTimeZoneIdentifier(temporalTimeZoneLike);
        var epochNs = SystemUtcEpochNanoseconds(engine);
        return GetIsoDateTimeFor(timeZone, epochNs);
    }
}
