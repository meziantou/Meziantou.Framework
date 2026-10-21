namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Temporal%</summary>
    public JsObject Temporal { get; private set; } = null!;

    /// <summary>%Temporal.Duration.prototype%</summary>
    public JsObject TemporalDurationPrototype { get; private set; } = null!;

    /// <summary>%Temporal.Instant.prototype%</summary>
    public JsObject TemporalInstantPrototype { get; private set; } = null!;

    /// <summary>%Temporal.PlainDate.prototype%</summary>
    public JsObject TemporalPlainDatePrototype { get; private set; } = null!;

    /// <summary>%Temporal.PlainDateTime.prototype%</summary>
    public JsObject TemporalPlainDateTimePrototype { get; private set; } = null!;

    /// <summary>%Temporal.PlainMonthDay.prototype%</summary>
    public JsObject TemporalPlainMonthDayPrototype { get; private set; } = null!;

    /// <summary>%Temporal.PlainTime.prototype%</summary>
    public JsObject TemporalPlainTimePrototype { get; private set; } = null!;

    /// <summary>%Temporal.PlainYearMonth.prototype%</summary>
    public JsObject TemporalPlainYearMonthPrototype { get; private set; } = null!;

    /// <summary>%Temporal.ZonedDateTime.prototype%</summary>
    public JsObject TemporalZonedDateTimePrototype { get; private set; } = null!;

    partial void InitializeTemporal()
    {
        TemporalDurationPrototype = new JsObject(ObjectPrototype);
        TemporalInstantPrototype = new JsObject(ObjectPrototype);
        TemporalPlainDatePrototype = new JsObject(ObjectPrototype);
        TemporalPlainDateTimePrototype = new JsObject(ObjectPrototype);
        TemporalPlainMonthDayPrototype = new JsObject(ObjectPrototype);
        TemporalPlainTimePrototype = new JsObject(ObjectPrototype);
        TemporalPlainYearMonthPrototype = new JsObject(ObjectPrototype);
        TemporalZonedDateTimePrototype = new JsObject(ObjectPrototype);

        // The Temporal object (https://tc39.es/proposal-temporal/#sec-temporal-objects)
        var temporal = new JsObject(ObjectPrototype);
        Temporal = temporal;
        temporal.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Temporal", PropertyFlags.Configurable);
        temporal.DefineBuiltIn("Instant", TemporalInstantBuiltins.Initialize(this));
        temporal.DefineBuiltIn("PlainDateTime", TemporalPlainDateTimeBuiltins.Initialize(this));
        temporal.DefineBuiltIn("PlainDate", TemporalPlainDateBuiltins.Initialize(this));
        temporal.DefineBuiltIn("PlainTime", TemporalPlainTimeBuiltins.Initialize(this));
        temporal.DefineBuiltIn("PlainYearMonth", TemporalPlainYearMonthBuiltins.Initialize(this));
        temporal.DefineBuiltIn("PlainMonthDay", TemporalPlainMonthDayBuiltins.Initialize(this));
        temporal.DefineBuiltIn("Duration", TemporalDurationBuiltins.Initialize(this));
        temporal.DefineBuiltIn("ZonedDateTime", TemporalZonedDateTimeBuiltins.Initialize(this));
        temporal.DefineBuiltIn("Now", TemporalNowBuiltins.Initialize(this));
        AddGlobal("Temporal", temporal);
    }
}
