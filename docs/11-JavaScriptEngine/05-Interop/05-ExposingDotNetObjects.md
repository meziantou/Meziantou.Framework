---
title: Exposing .NET objects
description: Expose a .NET class to the scripts with a constructor and shared methods, or implement an object whose properties come from .NET.
---

The engine does not wrap .NET objects automatically: it uses no reflection. You decide what the scripts see, with
one of these approaches:

| Approach | Use it for |
| --- | --- |
| An object whose properties are .NET functions | A few functions grouped in a namespace object, such as `app.log(...)`. See [Objects and arrays](03-ObjectsAndArrays.md) and [Functions](04-Functions.md). |
| A constructor and a prototype, with host objects as instances | A .NET class that the scripts create with `new` and whose instances share their methods. |
| A class derived from `JsHostObject` | An object whose properties are computed by .NET on each access, such as a view of a dictionary. |

## Expose a class

A JavaScript class is a constructor whose `prototype` property holds the methods of its instances. Each instance is a
`JsHostObject` whose `Target` is the .NET object:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_ExposeClass;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    // The methods are defined once, on the prototype that the instances share
    var prototype = realm.CreateObject();
    prototype.SetProperty("append", realm.CreateFunction("append", 1, (thisValue, arguments) =>
    {
        GetBuilder(thisValue).Append(engine.ToString(arguments.Length > 0 ? arguments[0] : JsValue.Undefined));
        return thisValue;
    }));
    prototype.SetProperty("toString", realm.CreateFunction("toString", 0, (thisValue, _) => GetBuilder(thisValue).ToString()));

    var constructor = realm.CreateConstructor("TextBuilder", 0, (_, _, newTarget) =>
    {
        if (newTarget is null)
        {
            throw realm.CreateException(JsErrorType.TypeError, "Constructor TextBuilder requires 'new'");
        }

        // The prototype of newTarget lets the scripts extend the class
        var instancePrototype = newTarget.GetProperty("prototype").TryGetObject() ?? prototype;
        return new JsHostObject(instancePrototype, new StringBuilder());
    });
    constructor.DefineProperty("prototype", JsPropertyDescriptor.Data(prototype, writable: false, enumerable: false, configurable: false));
    prototype.DefineProperty("constructor", JsPropertyDescriptor.Data(constructor, writable: true, enumerable: false, configurable: true));
    realm.GlobalObject.SetProperty("TextBuilder", constructor);

    // The .NET object of an instance is the target of its host object
    StringBuilder GetBuilder(JsValue thisValue) => thisValue.TryGetObject() is JsHostObject { Target: StringBuilder builder }
        ? builder
        : throw realm.CreateException(JsErrorType.TypeError, "Illegal invocation");
}

Assert.Equal("Hello, 42", realm.Execute("const builder = new TextBuilder(); builder.append('Hello').append(', ').append(42); `${builder}`").AsString());
Assert.Equal("true,[x]", realm.Execute("class Brackets extends TextBuilder { wrap(text) { return this.append(`[${text}]`); } } const b = new Brackets(); [b instanceof TextBuilder, `${b.wrap('x')}`].join()").AsString());
Assert.Equal("TypeError", realm.Execute("try { TextBuilder.prototype.append.call({}, 'x'); } catch (e) { e.name }").AsString());
```
<!-- endref -->

- `realm.CreateConstructor` creates a function that the scripts can call with `new`. Its delegate receives
  `newTarget`: null for a call without `new`, otherwise the constructor that `new` was applied to.
- The constructor has no `prototype` property: define it, and the `constructor` property of the prototype, with the
  attributes of a JavaScript class.
- Creating the instance with the prototype of `newTarget` lets the scripts extend the class with `extends`.
- The methods read the .NET object from `this`. A script can call them on any object
  (`TextBuilder.prototype.append.call({})`), so they check `this` and throw a `TypeError` otherwise.

## Implement an object in .NET

A class derived from `JsHostObject` can override the internal methods of the object: how its properties are read,
written, enumerated and deleted. This object exposes a dictionary, read on each access, as read-only properties:

<!-- ref:../../../samples/Meziantou.Framework.JavaScriptEngine.Samples/ReadOnlyDictionaryObject.cs;region=JavaScriptEngine_ReadOnlyDictionaryObject;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// Exposes a dictionary to the scripts: its entries are read-only properties, read from the dictionary on each access.
/// </summary>
public sealed class ReadOnlyDictionaryObject(JsRealm realm, IReadOnlyDictionary<string, string> values)
    : JsHostObject(realm.ObjectPrototype, values)
{
    protected override bool GetOwnPropertyCore(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        if (key.IsString && values.TryGetValue(key.AsString(), out var value))
        {
            descriptor = JsPropertyDescriptor.Data(value, writable: false, enumerable: true, configurable: false);
            return true;
        }

        descriptor = default;
        return false;
    }

    // Object.keys, for...in, JSON.stringify...
    protected override IReadOnlyList<JsPropertyKey> OwnPropertyKeysCore() => [.. values.Keys.Select(key => new JsPropertyKey(key))];

    // The scripts cannot add or delete properties
    protected override bool DefineOwnPropertyCore(JsPropertyKey key, in JsPropertyDescriptor descriptor) => false;

    protected override bool DeleteCore(JsPropertyKey key) => !key.IsString || !values.ContainsKey(key.AsString());
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_HostObject;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["REGION"] = "eu-west", ["STAGE"] = "test" };
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("env", new ReadOnlyDictionaryObject(realm, environment));
}

Assert.Equal("eu-west", realm.Execute("env.REGION").AsString());

// The object reads the dictionary on each access
environment["STAGE"] = "production";
Assert.Equal("""{"REGION":"eu-west","STAGE":"production"}""", realm.Execute("JSON.stringify(env)").AsString());
Assert.Equal("TypeError", realm.Execute("'use strict'; try { env.REGION = 'us'; } catch (e) { e.name }").AsString());
```
<!-- endref -->

- The methods to override are the internal methods of the specification: `GetOwnPropertyCore`,
  `DefineOwnPropertyCore`, `HasPropertyCore`, `GetCore`, `SetCore`, `DeleteCore`, `OwnPropertyKeysCore`,
  `GetPrototypeOfCore`, `SetPrototypeOfCore`, `IsExtensibleCore` and `PreventExtensionsCore`. Their default
  implementations are the ones of an ordinary object.
- The ordinary versions stay available to an override: `OrdinaryGetOwnProperty`, `OrdinaryDefineOwnProperty`,
  `OrdinaryDelete` and `OrdinaryOwnPropertyKeys`.
- The ordinary `[[Get]]`, `[[Set]]` and `[[HasProperty]]` use `GetOwnPropertyCore`: overriding it is often enough to
  make a property readable.
- Set `HasCallInternalMethod` and override `CallCore` to make the object callable (it is then not a constructor).
- A derived class that overrides none of the methods that read or write properties can pass
  `hasOrdinaryInternalMethods: true` to the protected constructor: the engine then uses the fast paths of the ordinary
  objects for its properties.

A `JsHostObject` with a `Target` keeps the .NET object alive as long as the scripts reference the JavaScript object.
