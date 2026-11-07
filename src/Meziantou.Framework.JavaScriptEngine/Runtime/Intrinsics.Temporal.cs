using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The Temporal built-ins are created on first use (the global property, or an intrinsic used by another built-in): they
/// are a large part of the built-ins of a realm, and most scripts do not use them. The creation is not observable.
/// </summary>
internal sealed partial class Intrinsics
{
    private JsObject? _temporal;
    private JsObject? _temporalDurationPrototype;
    private JsObject? _temporalInstantPrototype;
    private JsObject? _temporalPlainDatePrototype;
    private JsObject? _temporalPlainDateTimePrototype;
    private JsObject? _temporalPlainMonthDayPrototype;
    private JsObject? _temporalPlainTimePrototype;
    private JsObject? _temporalPlainYearMonthPrototype;
    private JsObject? _temporalZonedDateTimePrototype;

    /// <summary>%Temporal%</summary>
    public JsObject Temporal => _temporal ?? InitializeTemporalBuiltins();

    /// <summary>%Temporal.Duration.prototype%</summary>
    public JsObject TemporalDurationPrototype => _temporalDurationPrototype ?? InitializeTemporalBuiltins(ref _temporalDurationPrototype);

    /// <summary>%Temporal.Instant.prototype%</summary>
    public JsObject TemporalInstantPrototype => _temporalInstantPrototype ?? InitializeTemporalBuiltins(ref _temporalInstantPrototype);

    /// <summary>%Temporal.PlainDate.prototype%</summary>
    public JsObject TemporalPlainDatePrototype => _temporalPlainDatePrototype ?? InitializeTemporalBuiltins(ref _temporalPlainDatePrototype);

    /// <summary>%Temporal.PlainDateTime.prototype%</summary>
    public JsObject TemporalPlainDateTimePrototype => _temporalPlainDateTimePrototype ?? InitializeTemporalBuiltins(ref _temporalPlainDateTimePrototype);

    /// <summary>%Temporal.PlainMonthDay.prototype%</summary>
    public JsObject TemporalPlainMonthDayPrototype => _temporalPlainMonthDayPrototype ?? InitializeTemporalBuiltins(ref _temporalPlainMonthDayPrototype);

    /// <summary>%Temporal.PlainTime.prototype%</summary>
    public JsObject TemporalPlainTimePrototype => _temporalPlainTimePrototype ?? InitializeTemporalBuiltins(ref _temporalPlainTimePrototype);

    /// <summary>%Temporal.PlainYearMonth.prototype%</summary>
    public JsObject TemporalPlainYearMonthPrototype => _temporalPlainYearMonthPrototype ?? InitializeTemporalBuiltins(ref _temporalPlainYearMonthPrototype);

    /// <summary>%Temporal.ZonedDateTime.prototype%</summary>
    public JsObject TemporalZonedDateTimePrototype => _temporalZonedDateTimePrototype ?? InitializeTemporalBuiltins(ref _temporalZonedDateTimePrototype);

    partial void InitializeTemporal() => AddLazyGlobal("Temporal", static intrinsics => intrinsics.Temporal);

    /// <summary>Creates the Temporal built-ins, and returns %Temporal%.</summary>
    private JsObject InitializeTemporalBuiltins()
    {
        if (_temporal is not null)
            return _temporal;

        // The prototypes exist before the constructors are created: the built-ins read them
        _temporalDurationPrototype = new JsObject(ObjectPrototype);
        _temporalInstantPrototype = new JsObject(ObjectPrototype);
        _temporalPlainDatePrototype = new JsObject(ObjectPrototype);
        _temporalPlainDateTimePrototype = new JsObject(ObjectPrototype);
        _temporalPlainMonthDayPrototype = new JsObject(ObjectPrototype);
        _temporalPlainTimePrototype = new JsObject(ObjectPrototype);
        _temporalPlainYearMonthPrototype = new JsObject(ObjectPrototype);
        _temporalZonedDateTimePrototype = new JsObject(ObjectPrototype);

        // The Temporal object (https://tc39.es/proposal-temporal/#sec-temporal-objects)
        var temporal = new JsObject(ObjectPrototype);
        _temporal = temporal;
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

        // The ECMA-402 amendments of Temporal: the toLocaleString methods
        DateTimeFormatLocaleSensitiveBuiltins.InitializeTemporal(this);
        LocaleSensitiveBuiltins.InitializeTemporal(this);
        return temporal;
    }

    private JsObject InitializeTemporalBuiltins(ref JsObject? prototype)
    {
        InitializeTemporalBuiltins();
        return prototype!;
    }
}
