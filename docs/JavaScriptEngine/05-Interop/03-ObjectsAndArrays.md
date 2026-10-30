---
title: Objects and arrays
description: Create objects and arrays for the scripts, read the objects they return, define read-only and lazy properties, and exchange data as JSON.
---

## Create objects and arrays

`realm.CreateObject` creates an empty object, and `realm.CreateArray` an array of values. Set their properties with
[SetProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.SetProperty), or define them with [DefineProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.DefineProperty) to choose their attributes:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CreateObjects;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    var settings = realm.CreateObject();
    settings.SetProperty("theme", "dark");
    settings.SetProperty("fontSize", 14);
    settings.SetProperty("languages", realm.CreateArray(["en", "fr"]));

    // A read-only property, which the scripts do not enumerate
    settings.DefineProperty("version", JsPropertyDescriptor.Data("1.0", writable: false, enumerable: false, configurable: false));

    realm.GlobalObject.SetProperty("settings", settings);
}

Assert.Equal("theme,fontSize,languages", realm.Execute("Object.keys(settings).join()").AsString());
Assert.Equal("TypeError", realm.Execute("'use strict'; try { settings.version = '2.0'; } catch (e) { e.name }").AsString());
Assert.Equal("1.0", realm.Execute("settings.version").AsString());
```
<!-- endref -->

- [SetProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.SetProperty) assigns a property, as `obj.name = value` does. It returns false when the assignment fails, for
  instance on a read-only property.
- [DefineProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.DefineProperty) takes a [JsPropertyDescriptor](cref:Meziantou.Framework.JavaScriptEngine.JsPropertyDescriptor): `JsPropertyDescriptor.Data(value, writable, enumerable,
  configurable)` for a value, `JsPropertyDescriptor.Accessor(getter, setter, enumerable, configurable)` for a getter and
  a setter, which are functions (see [Functions](04-Functions.md)). It returns false when the property cannot be
  defined.
- `CreateObject(prototype)` creates an object with another prototype, or with none (`null`).
- `realm.GlobalObject` is an object like the others: its properties are the global variables of the scripts.

## Read the objects of a script

The objects returned by a script are read the same way:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_ReadObjects;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var user = realm.Execute("({ name: 'Ada', roles: ['admin', 'dev'], [Symbol.for('id')]: 1 })").AsObject();

using (engine.Enter())
{
    Assert.Equal("Ada", user.GetProperty("name").AsString());

    // An array is a JsArray: its elements are the properties "0", "1"...
    var roles = (JsArray)user.GetProperty("roles").AsObject();
    Assert.Equal(2u, roles.Length);
    Assert.Equal("dev", roles.GetProperty("1").AsString());

    // The keys are strings or symbols
    Assert.Equal(["name", "roles"], user.GetOwnPropertyKeys().Where(key => key.IsString).Select(key => key.AsString()));

    // A missing property is undefined
    Assert.True(user.GetProperty("email").IsUndefined);
    Assert.False(user.ContainsProperty("email"));
}
```
<!-- endref -->

- [GetProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.GetProperty) reads a property through the prototype chain, as `obj.name` does: it calls getters, and the traps of
  a `Proxy`. A missing property is `undefined`.
- [TryGetOwnProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.TryGetOwnProperty) reads the descriptor of an own property without calling its getter.
- [GetOwnPropertyKeys](cref:Meziantou.Framework.JavaScriptEngine.JsObject.GetOwnPropertyKeys) returns the keys of the own properties in the order of JavaScript: the array indexes, then the
  strings in creation order, then the symbols. It includes the properties that are not enumerable.
- [ContainsProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.ContainsProperty) tests a property of the object or of its prototypes, as `key in obj` does.
- An array is a [JsArray](cref:Meziantou.Framework.JavaScriptEngine.JsArray): [Length](cref:Meziantou.Framework.JavaScriptEngine.JsArray.Length) is its length, and its elements are the properties `"0"`, `"1"`...
- [DeleteProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.DeleteProperty), [GetPrototype](cref:Meziantou.Framework.JavaScriptEngine.JsObject.GetPrototype), [SetPrototype](cref:Meziantou.Framework.JavaScriptEngine.JsObject.SetPrototype) and [PreventObjectExtensions](cref:Meziantou.Framework.JavaScriptEngine.JsObject.PreventObjectExtensions) do what `delete`,
  `Object.getPrototypeOf`, `Object.setPrototypeOf` and `Object.preventExtensions` do.

## Create properties on first use

A host that exposes many objects, few of which a script uses, can create them on first access:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_LazyProperties;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var created = 0;
using (engine.Enter())
{
    // The value is created the first time a script reads the property
    realm.GlobalObject.DefineLazyDataProperty("catalog", () =>
    {
        created++;
        return realm.CreateArray(["apple", "pear"]);
    }, writable: false, enumerable: false, configurable: true);
}

realm.Execute("1 + 1");
Assert.Equal(0, created);
Assert.Equal("apple,pear", realm.Execute("catalog.join()").AsString());
Assert.Equal(1, created);
```
<!-- endref -->

[DefineLazyAccessorProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.DefineLazyAccessorProperty) does the same for a getter and a setter. Each method has an overload that passes a state
to a static factory, so the properties do not allocate a closure each. When you define hundreds of properties on an
object, call [ReserveProperties](cref:Meziantou.Framework.JavaScriptEngine.JsObject.ReserveProperties) first.

## Exchange data as JSON

To pass a whole tree of data, such as a .NET object serialized with `System.Text.Json`, call `JSON.parse` and
`JSON.stringify` of the realm:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Json;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
realm.Execute("""
    function summarize(order) {
      return { id: order.id, count: order.items.length, total: order.items.reduce((sum, item) => sum + item.price, 0) };
    }
    """);

using (engine.Enter())
{
    var json = realm.GlobalObject.GetProperty("JSON").AsObject();
    var parse = json.GetProperty("parse");
    var stringify = json.GetProperty("stringify");

    // .NET to JavaScript: parse a JSON text, e.g. the output of System.Text.Json
    var order = engine.Call(parse, json, """{ "id": "A-12", "items": [{ "price": 5 }, { "price": 7.5 }] }""");

    var summary = engine.Call(realm.GlobalObject.GetProperty("summarize"), JsValue.Undefined, order);

    // JavaScript to .NET: a JSON text
    Assert.Equal("""{"id":"A-12","count":2,"total":12.5}""", engine.Call(stringify, json, summary).AsString());
}
```
<!-- endref -->

A script can replace `JSON` or its methods: when the scripts are not trusted, read `JSON.parse` and `JSON.stringify`
once, right after you create the realm and before any script runs.
