---
title: Converting .NET values
description: Convert .NET values, collections and JSON to JavaScript values and back, and expose typed .NET delegates as functions.
---

The engine does not convert .NET objects by itself. The helpers of this page convert the common types: primitives,
dates, collections, dictionaries and JSON. They test the type of each value instead of using reflection, so they work
in trimmed and Native AOT applications. Use them inside the scope of the engine.

## From .NET to JavaScript

`realm.FromDotNet` converts a .NET value, and the values it contains, creating the objects in the realm:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_FromDotNet;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    var order = new Dictionary<string, object?>
    {
        ["id"] = "A-12",
        ["date"] = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero),
        ["lines"] = new[]
        {
            new Dictionary<string, object?> { ["sku"] = "pen", ["quantity"] = 2, ["price"] = 1.5m },
            new Dictionary<string, object?> { ["sku"] = "ink", ["quantity"] = 1, ["price"] = 4m },
        },
        ["notes"] = null,
    };

    realm.GlobalObject.SetProperty("order", realm.FromDotNet(order));
}

Assert.Equal("A-12,2024,7,true", realm.Execute("""
    [order.id, order.date.getUTCFullYear(), order.lines.reduce((sum, line) => sum + line.quantity * line.price, 0), order.notes === null].join()
    """).AsString());
```
<!-- endref -->

| .NET | JavaScript |
| --- | --- |
| `null` | `null` |
| `bool`, `string`, `char` | a boolean, a string |
| The numeric types | a number: a `long` or a `decimal` can lose precision |
| `BigInteger` | a BigInt |
| `DateTimeOffset`, `DateTime` | a `Date` (a `DateTime` whose kind is unspecified is UTC) |
| `JsonElement`, `JsonNode` | the value that `JSON.parse` would create |
| A dictionary with string keys | an object with a property for each entry |
| Another `IEnumerable` | an array |
| [JsValue](cref:Meziantou.Framework.JavaScriptEngine.JsValue), [JsObject](cref:Meziantou.Framework.JavaScriptEngine.JsObject) | unchanged |

- Any other type throws a `NotSupportedException`: a class is not converted property by property. Expose it with
  [JsHostObject](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject) or with functions. See [Exposing .NET objects](05-ExposingDotNetObjects.md).
- A value that contains itself throws an `InvalidOperationException`. A value that appears twice is converted twice:
  the scripts get two objects.

## From JavaScript to .NET

`engine.ToDotNet` converts a value to the closest .NET type, and `engine.ToDotNet<T>` to a given type:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_ToDotNet;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var value = realm.Execute("({ name: 'pen', tags: ['blue', 'cheap'], price: 1.5, stock: null, created: new Date(0) })");

// Objects are dictionaries, arrays are lists, numbers are doubles, dates are DateTimeOffset
var product = (Dictionary<string, object?>)engine.ToDotNet(value)!;
Assert.Equal("pen", product["name"]);
Assert.Equal(["blue", "cheap"], (List<object?>)product["tags"]!);
Assert.Equal(1.5, product["price"]);
Assert.Null(product["stock"]);
Assert.Equal(DateTimeOffset.UnixEpoch, product["created"]);

// A typed conversion follows the rules of JavaScript, and checks the range of the integers
Assert.Equal(12, engine.ToDotNet<int>(realm.Execute("'12'")));
Assert.Equal("12", engine.ToDotNet<string>(realm.Execute("12")));
Assert.Null(engine.ToDotNet<int?>(JsValue.Undefined));
```
<!-- endref -->

- Without a type, an object becomes a `Dictionary<string, object?>` of its own enumerable properties whose key is a
  string, and an array becomes a `List<object?>`. The getters run. A function, a symbol, or a value that contains
  itself cannot be converted.
- With a type, the conversion follows the rules of JavaScript (`ToNumber`, `ToString`, `ToBoolean`), then checks the
  result: an integer type rejects a fraction (a `TypeError`) and a value out of its range (a `RangeError`). `undefined`
  and `null` convert to `null` for a reference type or a nullable type, to `false` for `bool`, and throw for the other
  value types.

## Typed functions

`realm.CreateFunction` also takes a `Func<...>` or an `Action<...>` of up to three arguments. The arguments are
converted with `ToDotNet<T>`, and the result with `FromDotNet`:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_TypedFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var log = new List<string>();
using (engine.Enter())
{
    // The arguments are converted to the types of the delegate, and the result to a JavaScript value
    realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", (int a, int b) => a + b));
    realm.GlobalObject.SetProperty("repeat", realm.CreateFunction("repeat", (string text, int? count) => string.Concat(Enumerable.Repeat(text, count ?? 2))));
    realm.GlobalObject.SetProperty("log", realm.CreateFunction("log", (string message) => log.Add(message)));
}

Assert.Equal(7, realm.Execute("add(3, '4')").AsNumber());
Assert.Equal("abab", realm.Execute("repeat('ab')").AsString());
realm.Execute("log('hello')");
Assert.Equal(["hello"], log);

// An argument that cannot be converted is a TypeError (or a RangeError) of the script
Assert.Equal("TypeError: 1.5 is not an integer", realm.Execute("try { add(1.5, 1); } catch (e) { `${e}` }").AsString());
```
<!-- endref -->

- An argument that the script does not pass is `undefined`: make it nullable (`int?`) to make it optional.
- A conversion that fails throws a `TypeError` or a `RangeError` to the script, which can catch it.
- An exception of the delegate is not a JavaScript error, as for the other .NET functions: throw a
  [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) to throw an error to the script. See [Handling errors](../04-RunningCode/04-HandlingErrors.md).
- Creating a function whose delegate uses an unsupported type throws a `NotSupportedException` at once.
- For a function that returns a `Task`, return `realm.CreatePromise(task, ...)`. See
  [Async code and promises](../06-AsyncAndPromises.md#await-a-net-task-from-a-script).
