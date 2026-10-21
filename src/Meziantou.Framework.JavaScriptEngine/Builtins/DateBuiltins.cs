using Meziantou.Framework.JavaScriptEngine.TimeZones;
using static Meziantou.Framework.JavaScriptEngine.DateOperations;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Date constructor and Date.prototype (https://tc39.es/ecma262/#sec-date-objects), with the Annex B methods.</summary>
internal static class DateBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.DatePrototype;
        var constructor = realm.CreateBuiltinFunction("Date", 7, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "now", 0, Now);
        realm.DefineMethod(constructor, "parse", 1, Parse);
        realm.DefineMethod(constructor, "UTC", 7, Utc);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "getDate", 0, LocalGetter(t => DateFromTime(t)));
        realm.DefineMethod(prototype, "getDay", 0, LocalGetter(t => WeekDay(t)));
        realm.DefineMethod(prototype, "getFullYear", 0, LocalGetter(YearFromTime));
        realm.DefineMethod(prototype, "getHours", 0, LocalGetter(t => HourFromTime(t)));
        realm.DefineMethod(prototype, "getMilliseconds", 0, LocalGetter(t => MsFromTime(t)));
        realm.DefineMethod(prototype, "getMinutes", 0, LocalGetter(t => MinFromTime(t)));
        realm.DefineMethod(prototype, "getMonth", 0, LocalGetter(t => MonthFromTime(t)));
        realm.DefineMethod(prototype, "getSeconds", 0, LocalGetter(t => SecFromTime(t)));
        realm.DefineMethod(prototype, "getTime", 0, GetTime);
        realm.DefineMethod(prototype, "getTimezoneOffset", 0, GetTimezoneOffset);
        realm.DefineMethod(prototype, "getUTCDate", 0, UtcGetter(t => DateFromTime(t)));
        realm.DefineMethod(prototype, "getUTCDay", 0, UtcGetter(t => WeekDay(t)));
        realm.DefineMethod(prototype, "getUTCFullYear", 0, UtcGetter(YearFromTime));
        realm.DefineMethod(prototype, "getUTCHours", 0, UtcGetter(t => HourFromTime(t)));
        realm.DefineMethod(prototype, "getUTCMilliseconds", 0, UtcGetter(t => MsFromTime(t)));
        realm.DefineMethod(prototype, "getUTCMinutes", 0, UtcGetter(t => MinFromTime(t)));
        realm.DefineMethod(prototype, "getUTCMonth", 0, UtcGetter(t => MonthFromTime(t)));
        realm.DefineMethod(prototype, "getUTCSeconds", 0, UtcGetter(t => SecFromTime(t)));
        realm.DefineMethod(prototype, "setDate", 1, (function, thisValue, arguments, _) => SetDate(function, thisValue, arguments, utc: false));
        realm.DefineMethod(prototype, "setFullYear", 3, (function, thisValue, arguments, _) => SetFullYear(function, thisValue, arguments, utc: false));
        realm.DefineMethod(prototype, "setHours", 4, (function, thisValue, arguments, _) => SetHours(function, thisValue, arguments, utc: false));
        realm.DefineMethod(prototype, "setMilliseconds", 1, (function, thisValue, arguments, _) => SetMilliseconds(function, thisValue, arguments, utc: false));
        realm.DefineMethod(prototype, "setMinutes", 3, (function, thisValue, arguments, _) => SetMinutes(function, thisValue, arguments, utc: false));
        realm.DefineMethod(prototype, "setMonth", 2, (function, thisValue, arguments, _) => SetMonth(function, thisValue, arguments, utc: false));
        realm.DefineMethod(prototype, "setSeconds", 2, (function, thisValue, arguments, _) => SetSeconds(function, thisValue, arguments, utc: false));
        realm.DefineMethod(prototype, "setTime", 1, SetTime);
        realm.DefineMethod(prototype, "setUTCDate", 1, (function, thisValue, arguments, _) => SetDate(function, thisValue, arguments, utc: true));
        realm.DefineMethod(prototype, "setUTCFullYear", 3, (function, thisValue, arguments, _) => SetFullYear(function, thisValue, arguments, utc: true));
        realm.DefineMethod(prototype, "setUTCHours", 4, (function, thisValue, arguments, _) => SetHours(function, thisValue, arguments, utc: true));
        realm.DefineMethod(prototype, "setUTCMilliseconds", 1, (function, thisValue, arguments, _) => SetMilliseconds(function, thisValue, arguments, utc: true));
        realm.DefineMethod(prototype, "setUTCMinutes", 3, (function, thisValue, arguments, _) => SetMinutes(function, thisValue, arguments, utc: true));
        realm.DefineMethod(prototype, "setUTCMonth", 2, (function, thisValue, arguments, _) => SetMonth(function, thisValue, arguments, utc: true));
        realm.DefineMethod(prototype, "setUTCSeconds", 2, (function, thisValue, arguments, _) => SetSeconds(function, thisValue, arguments, utc: true));
        realm.DefineMethod(prototype, "toDateString", 0, ToDateString);
        realm.DefineMethod(prototype, "toISOString", 0, ToIsoString);
        realm.DefineMethod(prototype, "toJSON", 1, ToJson);
        realm.DefineMethod(prototype, "toTemporalInstant", 0, ToTemporalInstant);

        // Without Intl, the locale-sensitive methods use the formats of toString (ECMA-402 replaces them)
        realm.DefineMethod(prototype, "toLocaleDateString", 0, ToDateString);
        realm.DefineMethod(prototype, "toLocaleString", 0, ToStringImpl);
        realm.DefineMethod(prototype, "toLocaleTimeString", 0, ToTimeString);
        realm.DefineMethod(prototype, "toString", 0, ToStringImpl);
        realm.DefineMethod(prototype, "toTimeString", 0, ToTimeString);
        var toUtcString = realm.DefineEagerMethod(prototype, "toUTCString", 0, ToUtcStringImpl);
        realm.DefineMethod(prototype, "valueOf", 0, GetTime);
        realm.DefineMethod(prototype, WellKnownSymbols.ToPrimitive, 1, ToPrimitive, PropertyFlags.Configurable);

        // Annex B
        realm.DefineMethod(prototype, "getYear", 0, GetYear);
        realm.DefineMethod(prototype, "setYear", 1, SetYear);
        prototype.DefineBuiltIn("toGMTString", toUtcString);

        intrinsics.AddGlobal("Date", constructor);
        return constructor;
    }

    private static NamedTimeZone GetTimeZone(JsBuiltinFunction function) => function.Realm.Engine.SystemTimeZone.Zone;

    private static JsValue Argument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>thisTimeValue(value): the [[DateValue]] of a Date object.</summary>
    private static JsDateObject ThisDateObject(JsValue value)
    {
        if (value.Cell is JsDateObject date)
            return date;

        return ThrowHelper.ThrowTypeError<JsDateObject>("this is not a Date object.");
    }

    private static double ThisTimeValue(JsValue value) => ThisDateObject(value).DateValue;

    /// <summary>Date.prototype.toTemporalInstant()</summary>
    private static JsValue ToTemporalInstant(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var t = ThisTimeValue(thisValue);
        var ns = BigIntOperations.NumberToBigInt(t) * 1_000_000;
        return TemporalOperations.CreateTemporalInstant((Int128)ns);
    }

    /// <summary>Date(...values)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var engine = function.Realm.Engine;
        if (newTarget is null)
            return DateOperations.ToDateString(engine.GetCurrentTimeValue(), engine.SystemTimeZone);

        double dv;
        if (arguments.Length == 0)
        {
            dv = engine.GetCurrentTimeValue();
        }
        else if (arguments.Length == 1)
        {
            var value = arguments[0];
            double tv;
            if (value.Cell is JsDateObject date)
            {
                tv = date.DateValue;
            }
            else
            {
                var v = JsOperations.ToPrimitive(value);
                tv = v.Cell is JsString s ? DateParser.Parse(s.ToString(), engine.SystemTimeZone.Zone) : JsOperations.ToNumber(v);
            }

            dv = TimeClip(tv);
        }
        else
        {
            var finalDate = ToDate(arguments);
            dv = TimeClip(DateOperations.Utc(finalDate, engine.SystemTimeZone.Zone));
        }

        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, static i => i.DatePrototype);
        return new JsDateObject(prototype, dv);
    }

    /// <summary>The date of the arguments (year, month[, date[, hours[, minutes[, seconds[, ms]]]]]) of the constructor and Date.UTC.</summary>
    private static double ToDate(ReadOnlySpan<JsValue> arguments)
    {
        var y = JsOperations.ToNumber(Argument(arguments, 0));
        var m = arguments.Length > 1 ? JsOperations.ToNumber(arguments[1]) : 0;
        var dt = arguments.Length > 2 ? JsOperations.ToNumber(arguments[2]) : 1;
        var h = arguments.Length > 3 ? JsOperations.ToNumber(arguments[3]) : 0;
        var min = arguments.Length > 4 ? JsOperations.ToNumber(arguments[4]) : 0;
        var s = arguments.Length > 5 ? JsOperations.ToNumber(arguments[5]) : 0;
        var milli = arguments.Length > 6 ? JsOperations.ToNumber(arguments[6]) : 0;
        var yr = MakeFullYear(y);
        return MakeDate(MakeDay(yr, m, dt), MakeTime(h, min, s, milli));
    }

    /// <summary>Date.now()</summary>
    private static JsValue Now(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return function.Realm.Engine.GetCurrentTimeValue();
    }

    /// <summary>Date.parse(string)</summary>
    private static JsValue Parse(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var value = JsOperations.ToString(Argument(arguments, 0));
        return DateParser.Parse(value, GetTimeZone(function));
    }

    /// <summary>Date.UTC(year[, month[, date[, hours[, minutes[, seconds[, ms]]]]]])</summary>
    private static JsValue Utc(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return TimeClip(ToDate(arguments));
    }

    private static BuiltinFunctionCallback LocalGetter(Func<double, double> component)
    {
        return (function, thisValue, _, _) =>
        {
            var t = ThisTimeValue(thisValue);
            if (double.IsNaN(t))
                return double.NaN;

            return component(LocalTime(t, GetTimeZone(function)));
        };
    }

    private static BuiltinFunctionCallback UtcGetter(Func<double, double> component)
    {
        return (_, thisValue, _, _) =>
        {
            var t = ThisTimeValue(thisValue);
            if (double.IsNaN(t))
                return double.NaN;

            return component(t);
        };
    }

    /// <summary>Date.prototype.getTime() and Date.prototype.valueOf()</summary>
    private static JsValue GetTime(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisTimeValue(thisValue);
    }

    /// <summary>Date.prototype.getTimezoneOffset()</summary>
    private static JsValue GetTimezoneOffset(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var t = ThisTimeValue(thisValue);
        if (double.IsNaN(t))
            return double.NaN;

        return (t - LocalTime(t, GetTimeZone(function))) / MsPerMinute;
    }

    /// <summary>Date.prototype.getYear() (Annex B)</summary>
    private static JsValue GetYear(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var t = ThisTimeValue(thisValue);
        if (double.IsNaN(t))
            return double.NaN;

        return YearFromTime(LocalTime(t, GetTimeZone(function))) - 1900;
    }

    /// <summary>Stores the new date value: TimeClip(UTC(date)) for the local setters, TimeClip(date) for the UTC ones.</summary>
    private static double SetDateValue(JsBuiltinFunction function, JsDateObject dateObject, double newDate, bool utc)
    {
        var u = TimeClip(utc ? newDate : DateOperations.Utc(newDate, GetTimeZone(function)));
        dateObject.DateValue = u;
        return u;
    }

    private static double ToLocalIfNeeded(JsBuiltinFunction function, double t, bool utc) => utc ? t : LocalTime(t, GetTimeZone(function));

    /// <summary>Date.prototype.setDate(date) and setUTCDate(date)</summary>
    private static JsValue SetDate(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool utc)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var dt = JsOperations.ToNumber(Argument(arguments, 0));
        if (double.IsNaN(t))
            return double.NaN;

        t = ToLocalIfNeeded(function, t, utc);
        var newDate = MakeDate(MakeDay(YearFromTime(t), MonthFromTime(t), dt), TimeWithinDay(t));
        return SetDateValue(function, dateObject, newDate, utc);
    }

    /// <summary>Date.prototype.setFullYear(year[, month[, date]]) and setUTCFullYear</summary>
    private static JsValue SetFullYear(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool utc)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var y = JsOperations.ToNumber(Argument(arguments, 0));
        t = double.IsNaN(t) ? 0 : ToLocalIfNeeded(function, t, utc);
        var m = arguments.Length > 1 ? JsOperations.ToNumber(arguments[1]) : MonthFromTime(t);
        var dt = arguments.Length > 2 ? JsOperations.ToNumber(arguments[2]) : DateFromTime(t);
        var newDate = MakeDate(MakeDay(y, m, dt), TimeWithinDay(t));
        return SetDateValue(function, dateObject, newDate, utc);
    }

    /// <summary>Date.prototype.setHours(hour[, min[, sec[, ms]]]) and setUTCHours</summary>
    private static JsValue SetHours(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool utc)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var h = JsOperations.ToNumber(Argument(arguments, 0));
        var m = arguments.Length > 1 ? JsOperations.ToNumber(arguments[1]) : double.NaN;
        var s = arguments.Length > 2 ? JsOperations.ToNumber(arguments[2]) : double.NaN;
        var milli = arguments.Length > 3 ? JsOperations.ToNumber(arguments[3]) : double.NaN;
        if (double.IsNaN(t))
            return double.NaN;

        t = ToLocalIfNeeded(function, t, utc);
        if (arguments.Length <= 1)
        {
            m = MinFromTime(t);
        }

        if (arguments.Length <= 2)
        {
            s = SecFromTime(t);
        }

        if (arguments.Length <= 3)
        {
            milli = MsFromTime(t);
        }

        var date = MakeDate(Day(t), MakeTime(h, m, s, milli));
        return SetDateValue(function, dateObject, date, utc);
    }

    /// <summary>Date.prototype.setMilliseconds(ms) and setUTCMilliseconds</summary>
    private static JsValue SetMilliseconds(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool utc)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var ms = JsOperations.ToNumber(Argument(arguments, 0));
        if (double.IsNaN(t))
            return double.NaN;

        t = ToLocalIfNeeded(function, t, utc);
        var time = MakeTime(HourFromTime(t), MinFromTime(t), SecFromTime(t), ms);
        return SetDateValue(function, dateObject, MakeDate(Day(t), time), utc);
    }

    /// <summary>Date.prototype.setMinutes(min[, sec[, ms]]) and setUTCMinutes</summary>
    private static JsValue SetMinutes(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool utc)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var m = JsOperations.ToNumber(Argument(arguments, 0));
        var s = arguments.Length > 1 ? JsOperations.ToNumber(arguments[1]) : double.NaN;
        var milli = arguments.Length > 2 ? JsOperations.ToNumber(arguments[2]) : double.NaN;
        if (double.IsNaN(t))
            return double.NaN;

        t = ToLocalIfNeeded(function, t, utc);
        if (arguments.Length <= 1)
        {
            s = SecFromTime(t);
        }

        if (arguments.Length <= 2)
        {
            milli = MsFromTime(t);
        }

        var date = MakeDate(Day(t), MakeTime(HourFromTime(t), m, s, milli));
        return SetDateValue(function, dateObject, date, utc);
    }

    /// <summary>Date.prototype.setMonth(month[, date]) and setUTCMonth</summary>
    private static JsValue SetMonth(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool utc)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var m = JsOperations.ToNumber(Argument(arguments, 0));
        var dt = arguments.Length > 1 ? JsOperations.ToNumber(arguments[1]) : double.NaN;
        if (double.IsNaN(t))
            return double.NaN;

        t = ToLocalIfNeeded(function, t, utc);
        if (arguments.Length <= 1)
        {
            dt = DateFromTime(t);
        }

        var newDate = MakeDate(MakeDay(YearFromTime(t), m, dt), TimeWithinDay(t));
        return SetDateValue(function, dateObject, newDate, utc);
    }

    /// <summary>Date.prototype.setSeconds(sec[, ms]) and setUTCSeconds</summary>
    private static JsValue SetSeconds(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool utc)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var s = JsOperations.ToNumber(Argument(arguments, 0));
        var milli = arguments.Length > 1 ? JsOperations.ToNumber(arguments[1]) : double.NaN;
        if (double.IsNaN(t))
            return double.NaN;

        t = ToLocalIfNeeded(function, t, utc);
        if (arguments.Length <= 1)
        {
            milli = MsFromTime(t);
        }

        var date = MakeDate(Day(t), MakeTime(HourFromTime(t), MinFromTime(t), s, milli));
        return SetDateValue(function, dateObject, date, utc);
    }

    /// <summary>Date.prototype.setTime(time)</summary>
    private static JsValue SetTime(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = JsOperations.ToNumber(Argument(arguments, 0));
        var v = TimeClip(t);
        dateObject.DateValue = v;
        return v;
    }

    /// <summary>Date.prototype.setYear(year) (Annex B)</summary>
    private static JsValue SetYear(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var dateObject = ThisDateObject(thisValue);
        var t = dateObject.DateValue;
        var y = JsOperations.ToNumber(Argument(arguments, 0));
        t = double.IsNaN(t) ? 0 : LocalTime(t, GetTimeZone(function));
        var yyyy = MakeFullYear(y);
        var d = MakeDay(yyyy, MonthFromTime(t), DateFromTime(t));
        var date = MakeDate(d, TimeWithinDay(t));
        return SetDateValue(function, dateObject, date, utc: false);
    }

    /// <summary>Date.prototype.toDateString()</summary>
    private static JsValue ToDateString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var tv = ThisTimeValue(thisValue);
        return DateOperations.ToDateOnlyString(tv, function.Realm.Engine.SystemTimeZone);
    }

    /// <summary>Date.prototype.toISOString()</summary>
    private static JsValue ToIsoString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var tv = ThisTimeValue(thisValue);
        if (!double.IsFinite(tv))
            return ThrowHelper.ThrowRangeError<JsValue>("Invalid time value");

        return DateOperations.ToIsoString(tv);
    }

    /// <summary>Date.prototype.toJSON(key)</summary>
    private static JsValue ToJson(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var o = JsOperations.ToObject(thisValue);
        var tv = JsOperations.ToPrimitive(o, JsOperations.PreferredType.Number);
        if (tv.IsNumber && !double.IsFinite(tv.NumberValue))
            return JsValue.Null;

        return JsOperations.Invoke(o, "toISOString", []);
    }

    /// <summary>Date.prototype.toString()</summary>
    private static JsValue ToStringImpl(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var tv = ThisTimeValue(thisValue);
        return DateOperations.ToDateString(tv, function.Realm.Engine.SystemTimeZone);
    }

    /// <summary>Date.prototype.toTimeString()</summary>
    private static JsValue ToTimeString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var tv = ThisTimeValue(thisValue);
        return DateOperations.ToTimeOnlyString(tv, function.Realm.Engine.SystemTimeZone);
    }

    /// <summary>Date.prototype.toUTCString() (and Date.prototype.toGMTString, the same function)</summary>
    private static JsValue ToUtcStringImpl(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var tv = ThisTimeValue(thisValue);
        return DateOperations.ToUtcString(tv);
    }

    /// <summary>Date.prototype[@@toPrimitive](hint)</summary>
    private static JsValue ToPrimitive(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject o)
            return ThrowHelper.ThrowTypeError<JsValue>("Date.prototype[Symbol.toPrimitive] requires that 'this' be an Object");

        var hint = Argument(arguments, 0);
        var tryFirst = hint.Cell is JsString s ? s.ToString() switch
        {
            "string" or "default" => JsOperations.PreferredType.String,
            "number" => JsOperations.PreferredType.Number,
            _ => JsOperations.PreferredType.None,
        } : JsOperations.PreferredType.None;

        if (tryFirst == JsOperations.PreferredType.None)
            return ThrowHelper.ThrowTypeError<JsValue>("Invalid hint: " + JsOperations.DescribeForError(hint));

        return JsOperations.OrdinaryToPrimitive(o, tryFirst);
    }
}
