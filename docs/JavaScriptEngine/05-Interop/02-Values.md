---
title: Values
description: Read the kind of a JsValue, convert it with the rules of JavaScript, create values from .NET, and exchange dates.
---

## Read a value

A `JsValue` holds any JavaScript value. Its `Kind` tells which one, and the `As*` methods read it:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Values;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();

static string Describe(JsValue value) => value.Kind switch
{
    JsValueKind.Undefined => "undefined",
    JsValueKind.Null => "null",
    JsValueKind.Boolean => value.AsBoolean() ? "true" : "false",
    JsValueKind.Number => value.AsNumber().ToString(CultureInfo.InvariantCulture),
    JsValueKind.String => '"' + value.AsString() + '"',
    JsValueKind.BigInt => value.AsBigInt().ToString(CultureInfo.InvariantCulture) + "n",
    JsValueKind.Symbol => value.AsSymbol().ToString(),
    JsValueKind.Object when value.IsCallable => "function",
    JsValueKind.Object => "object",
    _ => throw new UnreachableException(),
};

var values = (JsArray)realm.Execute("[42, 'text', true, null, undefined, 2n ** 70n, Symbol('id'), {}, Math.max]").AsObject();
using (engine.Enter())
{
    var descriptions = new List<string>();
    for (var i = 0u; i < values.Length; i++)
    {
        descriptions.Add(Describe(values.GetProperty(i.ToString(CultureInfo.InvariantCulture))));
    }

    Assert.Equal(["42", "\"text\"", "true", "null", "undefined", "1180591620717411303424n", "Symbol(id)", "object", "function"], descriptions);
}
```
<!-- endref -->

| `Kind` | Test | Read with |
| --- | --- | --- |
| `Undefined` | `IsUndefined` | |
| `Null` | `IsNull` | |
| `Boolean` | `IsBoolean` | `AsBoolean()` |
| `Number` | `IsNumber` | `AsNumber()`: a `double` |
| `String` | `IsString` | `AsString()` |
| `Symbol` | `IsSymbol` | `AsSymbol()`: a `JsSymbol` |
| `BigInt` | `IsBigInt` | `AsBigInt()`: a `BigInteger` |
| `Object` | `IsObject` | `AsObject()`, or `TryGetObject()`, which returns null for the other kinds |

- `IsNullOrUndefined` tests both values that mean "no value", and `IsCallable` tests whether the value is a function.
- The `As*` methods do not convert: call them only on a value of their kind.
- `default(JsValue)` is `undefined`.
- `ToString()` of a `JsValue` is a description for debugging: it never runs code of the script.

## Convert a value

To convert a value of any kind with the rules of JavaScript, as `Number(x)`, `String(x)` and `Boolean(x)` do, use the
conversions of the engine:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Conversions;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();

// AsNumber, AsString... read a value of their kind: check the kind first
var length = realm.Execute("[1, 2, 3].length");
Assert.True(length.IsNumber);
Assert.Equal(3, length.AsNumber());

// The conversions of JavaScript accept any value, and may run code of the script (valueOf, toString)
var price = realm.Execute("({ amount: 5, valueOf() { return this.amount * 2; } })");
Assert.Equal(10, engine.ToNumber(price));
Assert.Equal("[object Object]", engine.ToString(price));
Assert.True(JsEngine.ToBoolean(realm.Execute("'0'")));
Assert.Equal(12, engine.ToNumber(realm.Execute("'12'")));
```
<!-- endref -->

- `engine.ToNumber`, `ToString`, `ToBigInt` and `ToPropertyKey` can call the `valueOf`, `toString` or
  `Symbol.toPrimitive` methods of an object, so they can run code of the script and throw a `JsException`.
  `ToString` throws a `TypeError` for a symbol.
- `engine.ToObject` wraps a primitive value in its object (`new Number(1)`...) and throws a `TypeError` for
  `undefined` and `null`.
- `JsEngine.ToBoolean` never runs code.
- `JsEngine.SameValue` compares two values as `Object.is` does. `JsValue` equality (`==`, `Equals`) is the same
  comparison.
- `engine.IsArray` tells whether a value is an array, as `Array.isArray` does.

## Create a value

The numbers (`int` and `double`), the strings, the booleans, the objects and the symbols convert implicitly to
`JsValue`. The other values have their own members:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CreateValues;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    // The numbers, the strings and the booleans convert implicitly to JsValue
    realm.GlobalObject.SetProperty("count", 3);
    realm.GlobalObject.SetProperty("ratio", 0.5);
    realm.GlobalObject.SetProperty("title", "Report");
    realm.GlobalObject.SetProperty("enabled", true);
    realm.GlobalObject.SetProperty("nothing", JsValue.Null);
    realm.GlobalObject.SetProperty("big", JsValue.FromBigInt(BigInteger.Pow(2, 70)));
}

Assert.Equal("number,number,string,boolean,object,bigint", realm.Execute("[count, ratio, title, enabled, nothing, big].map(x => typeof x).join()").AsString());
```
<!-- endref -->

- `JsValue.Undefined`, `JsValue.Null`, `JsValue.True` and `JsValue.False` are the constants.
- `JsValue.FromBigInt` creates a BigInt; a `long` converts to a number, not to a BigInt.
- A null `JsObject` converts to `null`.
- The well-known symbols are static properties of `JsSymbol`: `JsSymbol.Iterator`, `JsSymbol.ToStringTag`...

For objects and arrays, see [Objects and arrays](03-ObjectsAndArrays.md).

## Exchange dates

A JavaScript `Date` is a number of milliseconds since 1970-01-01T00:00:00Z. Create one with `realm.CreateDate`, and
read one with `TryGetDateValue`:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Dates;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    // A Date is a number of milliseconds since 1970-01-01T00:00:00Z
    var date = realm.CreateDate(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
    realm.GlobalObject.SetProperty("date", date);
}

Assert.Equal("2026-10-29T12:00:00.000Z", realm.Execute("date.toISOString()").AsString());

var returned = realm.Execute("new Date(Date.UTC(2000, 0, 1))").AsObject();
Assert.True(returned.TryGetDateValue(out var timeValue));
Assert.Equal(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.FromUnixTimeMilliseconds((long)timeValue));
```
<!-- endref -->

An invalid date has the time value `NaN`: check it before you convert it to a `DateTimeOffset`. For time zones and
`Temporal`, see [Intl, dates and time zones](../08-IntlDatesAndTimeZones.md).
