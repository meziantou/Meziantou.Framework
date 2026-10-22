namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The locale-sensitive methods that format dates with Intl.DateTimeFormat: Date.prototype.toLocaleString,
/// toLocaleDateString and toLocaleTimeString (ECMA-402), and the toLocaleString methods of the Temporal types (the
/// ECMA-402 amendments of Temporal).
/// </summary>
internal static class DateTimeFormatLocaleSensitiveBuiltins
{
    private static readonly BuiltinMethodTable DatePrototypeMethods =
    [
        new("toLocaleString", 0, (function, thisValue, arguments, _) => DateToLocaleString(function, thisValue, arguments, DateTimeRequired.Any, DateTimeDefaults.All, 0)),
        new("toLocaleDateString", 0, (function, thisValue, arguments, _) => DateToLocaleString(function, thisValue, arguments, DateTimeRequired.Date, DateTimeDefaults.Date, 1)),
        new("toLocaleTimeString", 0, (function, thisValue, arguments, _) => DateToLocaleString(function, thisValue, arguments, DateTimeRequired.Time, DateTimeDefaults.Time, 2)),
    ];

    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        realm.DefineMethods(intrinsics.DatePrototype, DatePrototypeMethods);
    }

    /// <summary>The toLocaleString methods of the Temporal types (the Temporal built-ins are created on first use).</summary>
    public static void InitializeTemporal(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        realm.DefineMethod(intrinsics.TemporalPlainDatePrototype, "toLocaleString", 0, (function, thisValue, arguments, _) => TemporalToLocaleString<JsPlainDate>(function, thisValue, arguments, "PlainDate", DateTimeRequired.Date, DateTimeDefaults.Date));
        realm.DefineMethod(intrinsics.TemporalPlainDateTimePrototype, "toLocaleString", 0, (function, thisValue, arguments, _) => TemporalToLocaleString<JsPlainDateTime>(function, thisValue, arguments, "PlainDateTime", DateTimeRequired.Any, DateTimeDefaults.All));
        realm.DefineMethod(intrinsics.TemporalPlainMonthDayPrototype, "toLocaleString", 0, (function, thisValue, arguments, _) => TemporalToLocaleString<JsPlainMonthDay>(function, thisValue, arguments, "PlainMonthDay", DateTimeRequired.Date, DateTimeDefaults.Date));
        realm.DefineMethod(intrinsics.TemporalPlainTimePrototype, "toLocaleString", 0, (function, thisValue, arguments, _) => TemporalToLocaleString<JsPlainTime>(function, thisValue, arguments, "PlainTime", DateTimeRequired.Time, DateTimeDefaults.Time));
        realm.DefineMethod(intrinsics.TemporalPlainYearMonthPrototype, "toLocaleString", 0, (function, thisValue, arguments, _) => TemporalToLocaleString<JsPlainYearMonth>(function, thisValue, arguments, "PlainYearMonth", DateTimeRequired.Date, DateTimeDefaults.Date));
        realm.DefineMethod(intrinsics.TemporalInstantPrototype, "toLocaleString", 0, (function, thisValue, arguments, _) => TemporalToLocaleString<JsInstant>(function, thisValue, arguments, "Instant", DateTimeRequired.Any, DateTimeDefaults.All));
        realm.DefineMethod(intrinsics.TemporalZonedDateTimePrototype, "toLocaleString", 0, ZonedDateTimeToLocaleString);
    }

    private static JsValue Argument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>Date.prototype.toLocaleString, toLocaleDateString and toLocaleTimeString ([locales [, options]])</summary>
    private static JsValue DateToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, DateTimeRequired required, DateTimeDefaults defaults, int cacheIndex)
    {
        if (thisValue.Cell is not JsDateObject date)
            return ThrowHelper.ThrowTypeError<JsValue>("this is not a Date object.");

        var x = date.DateValue;
        if (double.IsNaN(x))
            return "Invalid Date";

        var intrinsics = function.Realm.Intrinsics;
        var locales = Argument(arguments, 0);
        var options = Argument(arguments, 1);
        JsDateTimeFormatObject dateFormat;
        if (locales.IsUndefined && options.IsUndefined)
        {
            dateFormat = intrinsics.DefaultDateTimeFormats[cacheIndex] ??= DateTimeFormatBuiltins.CreateDateTimeFormat(intrinsics.IntlDateTimeFormatPrototype, locales, options, required, defaults);
        }
        else
        {
            dateFormat = DateTimeFormatBuiltins.CreateDateTimeFormat(intrinsics.IntlDateTimeFormatPrototype, locales, options, required, defaults);
        }

        return DateTimeFormatBuiltins.FormatDateTime(dateFormat, x);
    }

    /// <summary>Temporal.*.prototype.toLocaleString([locales [, options]])</summary>
    private static JsValue TemporalToLocaleString<T>(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, string typeName, DateTimeRequired required, DateTimeDefaults defaults)
        where T : JsObject
    {
        if (thisValue.Cell is not T)
            return ThrowHelper.ThrowTypeError<JsValue>("Temporal." + typeName + ".prototype.toLocaleString called on an incompatible receiver");

        var dateFormat = DateTimeFormatBuiltins.CreateDateTimeFormat(function.Realm.Intrinsics.IntlDateTimeFormatPrototype, Argument(arguments, 0), Argument(arguments, 1), required, defaults);
        return DateTimeFormatBuiltins.FormatDateTime(dateFormat, thisValue);
    }

    /// <summary>Temporal.ZonedDateTime.prototype.toLocaleString([locales [, options]])</summary>
    private static JsValue ZonedDateTimeToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsZonedDateTime zonedDateTime)
            return ThrowHelper.ThrowTypeError<JsValue>("Temporal.ZonedDateTime.prototype.toLocaleString called on an incompatible receiver");

        var intrinsics = function.Realm.Intrinsics;
        var dateTimeFormat = DateTimeFormatBuiltins.CreateDateTimeFormat(intrinsics.IntlDateTimeFormatPrototype, Argument(arguments, 0), Argument(arguments, 1), DateTimeRequired.Any, DateTimeDefaults.All, zonedDateTime.TimeZone);
        if (zonedDateTime.Calendar != TemporalOperations.IsoCalendar && zonedDateTime.Calendar != dateTimeFormat.Calendar)
            ThrowHelper.ThrowRangeError("The calendar of the Temporal.ZonedDateTime (" + zonedDateTime.Calendar + ") is not the calendar of the DateTimeFormat (" + dateTimeFormat.Calendar + ")");

        var instant = new JsInstant(intrinsics.TemporalInstantPrototype, zonedDateTime.EpochNanoseconds);
        return DateTimeFormatBuiltins.FormatDateTime(dateTimeFormat, instant);
    }
}
