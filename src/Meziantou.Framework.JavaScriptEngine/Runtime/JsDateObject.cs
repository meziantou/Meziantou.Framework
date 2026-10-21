namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A Date object: its [[DateValue]] internal slot is a time value (milliseconds since the epoch, or NaN).</summary>
internal sealed class JsDateObject(JsObject? prototype, double dateValue) : JsObject(prototype, ObjectClass.Date)
{
    public double DateValue { get; set; } = dateValue;
}
