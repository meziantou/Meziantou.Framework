---
title: Functions
description: Expose .NET functions to the scripts, read their arguments safely, and call JavaScript functions and constructors from .NET.
---

## Expose a .NET function

`realm.CreateFunction` creates a JavaScript function implemented by a [JsNativeFunction](cref:Meziantou.Framework.JavaScriptEngine.JsNativeFunction) delegate. Store it in a
property, such as a property of the global object, to make it visible to the scripts:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Functions;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    // The name and the length are the name and length properties of the function
    realm.GlobalObject.SetProperty("formatPrice", realm.CreateFunction("formatPrice", 2, (thisValue, arguments) =>
    {
        // The arguments are the ones of the call: a script can pass fewer than the length of the function
        if (arguments.Length < 1 || !arguments[0].IsNumber)
        {
            throw realm.CreateException(JsErrorType.TypeError, "The amount must be a number");
        }

        var currency = arguments.Length > 1 ? engine.ToString(arguments[1]) : "EUR";
        return arguments[0].AsNumber().ToString("F2", CultureInfo.InvariantCulture) + " " + currency;
    }));
}

Assert.Equal("12.50 EUR", realm.Execute("formatPrice(12.5)").AsString());
Assert.Equal("3.00 USD", realm.Execute("formatPrice(3, 'USD')").AsString());
Assert.Equal("formatPrice,2", realm.Execute("[formatPrice.name, formatPrice.length].join()").AsString());
Assert.Equal("TypeError: The amount must be a number", realm.Execute("try { formatPrice('x'); } catch (e) { `${e}` }").AsString());
```
<!-- endref -->

- The delegate receives the `this` value of the call and the arguments, and returns the result. Return
  [JsValue.Undefined](cref:Meziantou.Framework.JavaScriptEngine.JsValue.Undefined) for a function that returns nothing.
- `arguments` holds the arguments that the script passed: there can be fewer or more than the length of the function.
  Check `arguments.Length` before you read an argument.
- Check the kind of an argument before you read it with [AsNumber](cref:Meziantou.Framework.JavaScriptEngine.JsValue.AsNumber), [AsString](cref:Meziantou.Framework.JavaScriptEngine.JsValue.AsString)..., or convert it with
  `engine.ToNumber`, `engine.ToString`... as the built-ins do. See [Values](02-Values.md).
- Throw a JavaScript error with `realm.CreateException` for an invalid argument. See
  [Handling errors](../04-RunningCode/04-HandlingErrors.md#throw-a-javascript-error-from-net).
- The function runs inside the scope of the engine, in its realm: it can read and write properties and create objects
  directly.

A function created by [CreateFunction](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateFunction) cannot be called with `new`. To create a constructor, see
[Exposing .NET objects](05-ExposingDotNetObjects.md).

## Call a JavaScript function from .NET

`engine.Call` calls a function with a `this` value and arguments; `engine.Construct` calls a constructor as `new`
does:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CallJavaScript;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
realm.Execute("""
    function applyDiscount(order, percent) {
      return { ...order, total: order.total * (1 - percent / 100) };
    }
    """);

using (engine.Enter())
{
    var order = realm.CreateObject();
    order.SetProperty("id", "A-12");
    order.SetProperty("total", 80);

    // Call(function, this, arguments...)
    var applyDiscount = realm.GlobalObject.GetProperty("applyDiscount");
    var discounted = engine.Call(applyDiscount, JsValue.Undefined, order, 25).AsObject();
    Assert.Equal("A-12", discounted.GetProperty("id").AsString());
    Assert.Equal(60, discounted.GetProperty("total").AsNumber());

    // Construct(constructor, arguments): new Map([["a", 1]])
    var map = engine.Construct(realm.GlobalObject.GetProperty("Map"), [realm.CreateArray([realm.CreateArray(["a", 1])])]).AsObject();
    Assert.Equal(1, map.GetProperty("size").AsNumber());
}
```
<!-- endref -->

- Pass [JsValue.Undefined](cref:Meziantou.Framework.JavaScriptEngine.JsValue.Undefined) as `this` for a plain function call, or the object for a method call
  (`engine.Call(obj.GetProperty("method"), obj, ...)`).
- [Call](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Call) throws a [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) with a `TypeError` when the value is not a function, and the exceptions that the
  function throws. Test [IsCallable](cref:Meziantou.Framework.JavaScriptEngine.JsValue.IsCallable) first when the value comes from a script.
- A [Call](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Call) or a [Construct](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Construct) made while no script runs is an entry into JavaScript: the limits of the engine apply to
  it, as to a script. See [Limits and sandboxing](../07-LimitsAndSandboxing.md).
- [Construct](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Construct) accepts a `newTarget`, as `Reflect.construct` does.

## Functions and realms

A function created by `realm.CreateFunction` runs in its realm, even when a script of another realm calls it: the
objects that it creates belong to its realm. Pass `runInCallerRealm: true` to run it in the realm of its caller
instead, for a function shared by several realms.
