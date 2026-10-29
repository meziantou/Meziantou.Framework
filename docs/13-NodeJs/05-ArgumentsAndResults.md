---
title: Arguments and results
description: Pass arguments, get typed results, compatible with trimming and Native AOT, and exchange the values that JSON cannot represent.
---

Arguments and results cross the process boundary as JSON. This page describes how .NET values become JavaScript values,
and back.

## Get a typed result

Each call has overloads that return a `JsonElement`, and generic overloads that deserialize the result. `MyJsonContext`
is a `JsonSerializerContext` declared with `[JsonSerializable(typeof(Person))]`:

<!-- ref:../../samples/Meziantou.Framework.NodeJs.Samples/ReadmeExamples.cs;region=NodeJs_TypedResults;format=md-fenced-code-block;dedent=true -->
```csharp
// Trimming and Native AOT compatible
Person? person = await node.InvokeAsync("./people.mjs", "get", [42], MyJsonContext.Default.Person);

// Reflection-based serialization of arguments and results
Person? samePerson = await node.InvokeAsync<Person>("./people.mjs", "get", [42]);
```
<!-- endref -->

- The overloads that take a `JsonTypeInfo<T>` use the source-generated serializer: they work with trimming and Native
  AOT.
- The other generic overloads serialize the arguments and deserialize the result using reflection. Without
  `JsonSerializerOptions`, they use the web defaults (`JsonSerializerDefaults.Web`), as JavaScript code uses camelCase
  names: properties are written in camelCase and read case-insensitively, numbers can be read from strings, and `NaN`
  and infinities can be read as numbers. Options that you provide are used as is.
- A result that cannot be deserialized to the requested type throws the exception of `System.Text.Json`, usually a
  `JsonException`.

## Pass arguments

There are two kinds of arguments:

- **A list of `JsonNode` values**, used by the overloads that do not serialize using reflection, including the ones
  that take a `JsonTypeInfo<T>`. Strings, numbers and booleans convert implicitly to `JsonNode`, so a collection expression such as
  `["hello", 42, true]` works. Use a `JsonObject` or a `JsonArray` for objects and arrays. This kind works with trimming
  and Native AOT.
- **An array of objects**, used by the reflection-based overloads and by the overloads that take
  `JsonSerializerOptions`. Each object is serialized using reflection, so you can pass your own types, such as the items
  of the cart in [Calling modules](04-CallingModules.md#call-an-export), which become
  `{ price: 3, quantity: 2 }`.

A call receives the arguments in order: as the parameters of the function for `InvokeAsync`, and in the `args` array for
`EvaluateAsync`.

## Pass values that JSON cannot represent

Use `JSValue` for a value that JSON cannot represent. It converts implicitly to `JsonNode`, so it can be an argument, or
be nested in a `JsonObject` or a `JsonArray` argument:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=JSValue_Example;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

var types = await node.EvaluateAsync(
    "return [typeof args[0], typeof args[1], args[2].date instanceof Date];",
    [JSValue.Undefined, JSValue.BigInt(long.MaxValue), new JsonObject { ["date"] = JSValue.Date(DateTimeOffset.UtcNow) }]);

Assert.Equal("undefined", types[0].GetString());
Assert.Equal("bigint", types[1].GetString());
Assert.True(types[2].GetBoolean());
```
<!-- endref -->

| .NET | JavaScript |
| --- | --- |
| `JSValue.Undefined` | `undefined` |
| `JSValue.Null` | `null` |
| `JSValue.String(string)` | string |
| `JSValue.Boolean(bool)` | boolean |
| `JSValue.BigInt(BigInteger)` | `BigInt`. `long`, `ulong`, `Int128` and `UInt128` convert implicitly to `BigInteger`. |
| `JSValue.Number(...)` | `Number`, from `double`, `float`, `Half`, `decimal` and all the integer types. `NaN`, infinities and `-0` are preserved. A value that is not exactly representable is rounded to the nearest double, as `Number` would. |
| `JSValue.Date(DateTimeOffset)`, `JSValue.Date(DateTime)` | `Date`, truncated to the millisecond. A local or unspecified `DateTime` is a local time. |
| `JSValue.Uint8Array(ReadOnlyMemory<byte>)` | `Uint8Array` |

A `JSValue` can only be used in the arguments of a call. It cannot be a member of an object serialized using
reflection, and a `JsonNode` created from it cannot be serialized or cloned.

## Read the values that JSON cannot represent

A result is serialized with `JSON.stringify`, after converting the values that JSON does not support, or supports with
a loss of information:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_ResultConversions;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

var counts = await node.EvaluateAsync<Dictionary<string, int>>("return new Map([['a', 1], ['b', 2]]);");
Assert.Equal(2, counts!["b"]);

var tags = await node.EvaluateAsync<HashSet<string>>("return new Set(['a', 'b']);");
Assert.True(tags!.SetEquals(["a", "b"]));

var bytes = await node.EvaluateAsync<byte[]>("return new Uint8Array([1, 2, 3]);");
Assert.Equal(new byte[] { 1, 2, 3 }, bytes);

var date = await node.EvaluateAsync<DateTimeOffset>("return new Date(Date.UTC(2026, 0, 15));");
Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), date);

var ratio = await node.EvaluateAsync<double>("return 0 / 0;");
Assert.True(double.IsNaN(ratio));
```
<!-- endref -->

| JavaScript | JSON | .NET |
| --- | --- | --- |
| `BigInt` | exact number (Node.js 21 or later) | `GetInt64()`, `Int128`, `decimal`, or `BigInteger.Parse(element.GetRawText())` |
| `NaN`, `Infinity`, `-Infinity` | `"NaN"`, `"Infinity"`, `"-Infinity"` | `double` or `float` with `JsonNumberHandling.AllowNamedFloatingPointLiterals` (the default without `JsonSerializerOptions`) |
| `-0` | `-0` (Node.js 21 or later) | `double` |
| `Uint8Array`, `Buffer`, `Uint8ClampedArray`, `DataView`, `ArrayBuffer` | base64 string | `byte[]`, or `GetBytesFromBase64()` |
| Other typed arrays (`Float64Array`, `Int32Array`…) | array of numbers | `double[]`, `int[]`… |
| `Map` | object, keys converted to strings | `Dictionary<string, T>`, `Dictionary<int, T>`… The keys must be strings, numbers, `BigInt`s or booleans. |
| `Set` | array | `HashSet<T>`, `List<T>`… |
| `Date` | ISO 8601 string | `DateTimeOffset`, `DateTime` |
| `undefined` | `null` | |
| A string or a key that contains a lone surrogate (e.g. a string cut in the middle of an emoji) | string, the lone surrogate replaced by `U+FFFD` | `string` |

- When you deserialize with a `JsonTypeInfo<T>` or with your own `JsonSerializerOptions`, set
  `NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals` to read `NaN` and infinities as numbers.
- Before Node.js 21, a result that contains a `BigInt` cannot be serialized, so the call fails, and `-0` is returned as
  `0`.
- A function or a `Symbol` returned by the call, or a value whose `toJSON` method returns `undefined`, cannot be
  serialized, so the call fails. Keep it in the process with a reference (see
  [Keeping values in Node.js](06-References.md)), or ignore it with a void call such as `InvokeVoidAsync`. Functions and
  `Symbol`s nested in the result are omitted, as `JSON.stringify` does.
- The depth of a result is only limited by the stack of the Node.js process, as `JSON.stringify` is recursive.

## Ignore the result

`InvokeVoidAsync` and `EvaluateVoidAsync` wait for the call to complete, but do not serialize its result, so the result
does not need to be serializable. Use them when you do not need the result: they also skip the cost of serializing it.

## Size of the messages

A message, the arguments or the result of a call, cannot exceed the maximum length of a JavaScript string (about 512
million UTF-16 characters). A larger message fails its call. To process large data, pass a file path and let the
JavaScript code read the file, or split the work into several calls.
