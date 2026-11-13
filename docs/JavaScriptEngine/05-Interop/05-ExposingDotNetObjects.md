---
title: Exposing .NET objects
description: Generate the JavaScript class of a .NET class with [JsExport], or expose a class by hand with a constructor and a prototype, or implement an object whose properties come from .NET.
---

The engine does not wrap .NET objects by reflection: you decide what the scripts see, with one of these approaches:

| Approach | Use it for |
| --- | --- |
| A class marked with [JsExportAttribute](cref:Meziantou.Framework.JavaScriptEngine.JsExportAttribute) | A .NET class whose public members the scripts use as a JavaScript class: a source generator writes the binding. |
| An object whose properties are .NET functions | A few functions grouped in a namespace object, such as `app.log(...)`. See [Objects and arrays](03-ObjectsAndArrays.md) and [Functions](04-Functions.md). |
| A constructor and a prototype, with host objects as instances | A class whose JavaScript API differs from its .NET API, or a class you cannot make `partial`. |
| A class derived from [JsHostObject](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject) | An object whose properties are computed by .NET on each access, such as a view of a dictionary. |

## Generate the class with [JsExport]

Mark a `partial` class with [JsExportAttribute](cref:Meziantou.Framework.JavaScriptEngine.JsExportAttribute). The package contains a source generator that writes its
JavaScript class at build time: the code uses no reflection, so it works in trimmed and Native AOT applications.

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_ExportClassDeclaration;format=md-fenced-code-block;dedent=true -->
```csharp
// The source generator creates the JavaScript class: the class must be partial
[JsExport]
public sealed partial class Counter
{
    // The constructor of the JavaScript class: "new Counter()" or "new Counter(5)"
    public Counter(int start = 0) => Value = start;

    // An accessor of the prototype, named in camelCase: "counter.value"
    public int Value { get; set; }

    // A method of the prototype: "counter.increment(2)"
    public int Increment(int step = 1) => Value += step;

    // An instance of an exported class is converted to its JavaScript object
    public Counter Clone() => new(Value);

    [JsName("toString")]
    public override string ToString() => "Counter(" + Value.ToString(CultureInfo.InvariantCulture) + ")";

    [JsIgnore]
    public void Reset() => Value = 0;

    // A static member is a property of the constructor: "Counter.parse('5')"
    public static Counter Parse(string text) => new(int.Parse(text, CultureInfo.InvariantCulture));

    // A task is a promise
    public async Task<int> IncrementLaterAsync()
    {
        await Task.Yield();
        return Increment();
    }
}
```
<!-- endref -->

The generator adds a static `JsClass` property to the class. [GetClass](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.GetClass) creates the constructor of the class in a
realm, the first time it is called, and returns the same constructor afterward:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_ExportClass;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    // The constructor of the JavaScript class of Counter, created once per realm
    realm.GlobalObject.SetProperty("Counter", realm.GetClass(Counter.JsClass));
}

Assert.Equal(3, realm.Execute("const counter = new Counter(1); counter.increment(); counter.increment(); counter.value").AsNumber());
Assert.Equal("Counter(13)", realm.Execute("counter.value += 10; `${counter}`").AsString());
Assert.Equal(5, realm.Execute("Counter.parse('5').value").AsNumber());
Assert.True(realm.Execute("counter.reset === undefined").AsBoolean());

// The scripts can extend the class
Assert.Equal(15, realm.Execute("class Double extends Counter { increment() { return super.increment(2); } } new Double(13).increment()").AsNumber());

// An argument that cannot be converted is a TypeError of the script
Assert.Equal("TypeError: 1.5 is not an integer", realm.Execute("try { counter.increment(1.5); } catch (e) { `${e}` }").AsString());

// The .NET object of a JavaScript object, and the JavaScript object of a .NET object
var counter = engine.ToDotNet<Counter>(realm.Execute("counter"));
Assert.Equal(13, counter.Value);
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("sameCounter", realm.FromDotNet(counter));
}

Assert.True(realm.Execute("sameCounter === counter").AsBoolean());
```
<!-- endref -->

The members of the JavaScript class:

- The public constructor is the constructor of the class: the scripts call it with `new`, and a call without `new`
  throws a `TypeError`. With several public constructors, mark one with [JsConstructorAttribute](cref:Meziantou.Framework.JavaScriptEngine.JsConstructorAttribute). An abstract class,
  or a class without public constructor, cannot be created by the scripts (`new` throws a `TypeError`), but its
  objects can be returned to them.
- The public instance methods are methods of the prototype, and the public instance properties and fields are
  accessors of the prototype: a read-only property, an `init` property or a `readonly` field has no setter. As for the
  members of a JavaScript class, they are not enumerable.
- The public static members are properties of the constructor.
- The names are converted to camelCase (`IncrementLaterAsync` is `incrementLaterAsync`).
  [JsNameAttribute](cref:Meziantou.Framework.JavaScriptEngine.JsNameAttribute) sets another name, and [JsIgnoreAttribute](cref:Meziantou.Framework.JavaScriptEngine.JsIgnoreAttribute) hides a member. `ToString`,
  `Equals` and `GetHashCode` are exposed only with `[JsName]`, as `toString` in the example.
- The indexers, the operators, the events and the write-only properties are not exposed.
- The scripts can extend the class with `extends`, and a method called with another `this`
  (`Counter.prototype.increment.call({})`) throws a `TypeError`.

### Arguments and results

The arguments are converted as [ToDotNet\<T\>](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.ToDotNet) converts them, and the results as
[FromDotNet](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.FromDotNet) does. See [Converting .NET values](08-DotNetValues.md).

| .NET type | Argument | Result |
| --- | --- | --- |
| `bool`, `string`, the numeric types (including `Half`, and on .NET 11 `BFloat16`, `Decimal32`, `Decimal64` and `Decimal128`), `BigInteger`, `DateTimeOffset`, `DateTime`, and their nullable forms | yes | yes |
| `object`, [JsValue](cref:Meziantou.Framework.JavaScriptEngine.JsValue), [JsObject](cref:Meziantou.Framework.JavaScriptEngine.JsObject) | yes | yes |
| A class marked with `[JsExport]` | yes | yes |
| `char`, `JsonElement`, `JsonNode`, a class derived from [JsObject](cref:Meziantou.Framework.JavaScriptEngine.JsObject) | | yes |
| Arrays, collections and dictionaries with string keys of these types | | yes |
| `void`, `Task`, `ValueTask` | | `undefined`, a promise |
| `Task<T>`, `ValueTask<T>` | | a promise of the result |
| [JsRealm](cref:Meziantou.Framework.JavaScriptEngine.JsRealm) | the realm of the function | |

- A missing argument is `undefined`. A parameter that has a default value gets it when the argument is missing or
  `undefined`. The `length` of a function counts the parameters before the first one that has a default value.
- A parameter whose type is a reference type that is not nullable (`string`, not `string?`) requires a value:
  `undefined` and `null` throw a `TypeError`.
- An argument that cannot be converted throws a `TypeError` or a `RangeError` to the script, which can catch it. An
  exception thrown by the .NET code is not a JavaScript error: throw a [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) to throw an error to the script.
- A `JsRealm` parameter is not an argument of the script: it receives the realm of the function, to create objects.
- A promise settles when the host runs the jobs of the engine. See [Async code and promises](../06-AsyncAndPromises.md).

### Objects of the class

The JavaScript object of a .NET object is a [JsHostObject](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject) whose [Target](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.Target) is the .NET object.

- [FromDotNet](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.FromDotNet) converts an object of an exported class to its JavaScript object, and the results of the
  members do the same: a realm always gives the same JavaScript object for the same .NET object, including the object
  that a script created with `new`.
- [ToDotNet](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.ToDotNet) converts the JavaScript object back to the .NET object. The typed functions of
  [CreateFunction](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateFunction) accept the exported classes too.
- The prototype of the object is the prototype of the class of its runtime type.

### Static classes

A static class is a plain object whose properties are its static members, as `Math`:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_ExportStaticClassDeclaration;format=md-fenced-code-block;dedent=true -->
```csharp
// A static class is a plain object, as Math
[JsExport(Name = "App")]
public static partial class AppApi
{
    public const string Version = "1.0";

    public static string Greet(string name, string greeting = "Hello") => greeting + ", " + name + "!";

    // A JsRealm parameter receives the realm of the function: it is not an argument of the script
    public static JsObject CreatePoint(JsRealm realm, double x, double y)
    {
        var point = realm.CreateObject();
        point.SetProperty("x", x);
        point.SetProperty("y", y);
        return point;
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_ExportStaticClass;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    // The object of a static class
    realm.GlobalObject.SetProperty("app", realm.GetClass(AppApi.JsClass));
}

Assert.Equal("Hello, Ada!", realm.Execute("app.greet('Ada')").AsString());
Assert.Equal("Hi, Ada!", realm.Execute("app.greet('Ada', 'Hi')").AsString());
Assert.Equal("1.0", realm.Execute("app.version").AsString());
Assert.Equal(5, realm.Execute("const point = app.createPoint(3, 4); Math.hypot(point.x, point.y)").AsNumber());

// A parameter whose type is a non-nullable reference type requires a value
Assert.Equal("TypeError: A value is required: undefined cannot be converted", realm.Execute("try { app.greet(); } catch (e) { `${e}` }").AsString());
```
<!-- endref -->

The `Name` of the attribute is the name of the class, or the `Symbol.toStringTag` of the object of a static class
(`[object App]`).

### Inheritance

A class derived from an exported class can be exported too. Its constructor and its prototype inherit from the ones of
the base class, so `instanceof` works, and the methods of the base class are defined once, on the base prototype: an
override runs when a script calls the method of the base class. The public members of the base classes that are not
exported are members of the derived class: an override with `[JsName]` gives such a member its JavaScript name.

### Build errors

The generator reports what it cannot expose as a build error:

| Rule | Error |
| --- | --- |
| `MFJS001` | The class, or a class that contains it, is not `partial`. |
| `MFJS002` | The class is generic, nested in a generic type, or file-local. |
| `MFJS003` | A member has a type that cannot be converted, is generic, or has a `ref`, `out`, `in` or `params` parameter; or the constructor does not set the `required` members. Hide the member with `[JsIgnore]`. |
| `MFJS004` | Two members have the same JavaScript name, such as the overloads of a method. Rename one with `[JsName]`, or hide it with `[JsIgnore]`. |
| `MFJS005` | The class has several public constructors and none, or more than one, is marked with `[JsConstructor]`. |
| `MFJS006` | A `[JsName]` or the `Name` of `[JsExport]` is empty. |

## Expose a class by hand

Writing the binding yourself lets the JavaScript API differ from the .NET API. A JavaScript class is a constructor
whose `prototype` property holds the methods of its instances. Each instance is a
[JsHostObject](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject) whose [Target](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.Target) is the .NET object:

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

A class derived from [JsHostObject](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject) can override the internal methods of the object: how its properties are read,
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

- The methods to override are the internal methods of the specification: [GetOwnPropertyCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.GetOwnPropertyCore),
  [DefineOwnPropertyCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.DefineOwnPropertyCore), [HasPropertyCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.HasPropertyCore), [GetCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.GetCore), [SetCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.SetCore), [DeleteCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.DeleteCore), [OwnPropertyKeysCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.OwnPropertyKeysCore),
  [GetPrototypeOfCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.GetPrototypeOfCore), [SetPrototypeOfCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.SetPrototypeOfCore), [IsExtensibleCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.IsExtensibleCore) and [PreventExtensionsCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.PreventExtensionsCore). Their default
  implementations are the ones of an ordinary object.
- The ordinary versions stay available to an override: [OrdinaryGetOwnProperty](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.OrdinaryGetOwnProperty), [OrdinaryDefineOwnProperty](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.OrdinaryDefineOwnProperty),
  [OrdinaryDelete](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.OrdinaryDelete) and [OrdinaryOwnPropertyKeys](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.OrdinaryOwnPropertyKeys).
- The ordinary `[[Get]]`, `[[Set]]` and `[[HasProperty]]` use [GetOwnPropertyCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.GetOwnPropertyCore): overriding it is often enough to
  make a property readable.
- Set [HasCallInternalMethod](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.HasCallInternalMethod) and override [CallCore](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.CallCore) to make the object callable (it is then not a constructor).
- Set [RejectsPrivateElements](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.RejectsPrivateElements) to forbid the private fields, methods and accessors of the classes on the object,
  as a browser does for its `WindowProxy` and `Location` objects: a class whose constructor returns the object (from a
  base class) then throws a `TypeError` instead of adding its `#private` elements to it.
- Call [InitializeErrorData](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.InitializeErrorData) to make the object an error, as the `DOMException` of a browser: `Error.isError` returns
  true for it, and `Error.prototype.stack` returns the stack trace of the code that ran when you called the method.
  The argument is the first line of the stack trace, such as `SyntaxError: The value is invalid`.
- A derived class that overrides none of the methods that read or write properties can pass
  `hasOrdinaryInternalMethods: true` to the protected constructor: the engine then uses the fast paths of the ordinary
  objects for its properties.

A [JsHostObject](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject) with a [Target](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.Target) keeps the .NET object alive as long as the scripts reference the JavaScript object.
