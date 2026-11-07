---
title: Calling JavaScript from .NET
description: Evaluate code in the realm of a window, call the functions of a page, convert values between .NET and JavaScript, and add .NET functions to the pages.
---

The engine runs code in the realm of a window, as the console of a browser does. The DOM objects are the same objects in
.NET and in JavaScript: a node returned by a script is the [Node](cref:Meziantou.Framework.WebPlatform.Dom.Node) you query from .NET.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_ExchangeValues;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
var document = await context.NavigateAsync("https://example.com/", _ => """
    <p id="price">12</p>
    <script>
      function addTax(amount, rate) { return amount * (1 + rate); }
    </script>
    """);

// Evaluate an expression in the realm of the window
var count = engine.Evaluate(context.Window, "document.querySelectorAll('p').length");
Assert.Equal(1, count.AsNumber());

// The nodes are the same objects in .NET and in JavaScript
var price = engine.FromJsValue(context.Window, engine.Evaluate(context.Window, "document.getElementById('price')"));
Assert.Same(document.GetElementById("price"), price);

// Call a function of the page with .NET values
var addTax = engine.Evaluate(context.Window, "addTax");
var total = engine.Call(context.Window, addTax, JsValue.Undefined, engine.ToJsValue(context.Window, 100), engine.ToJsValue(context.Window, 0.2));
Assert.Equal(120, total.AsNumber(), 6);
```
<!-- endref -->

| Method | Effect |
| --- | --- |
| `Evaluate(window, code)` | Runs code as an indirect `eval` in the realm of the window, and returns its result as a [JsValue](cref:Meziantou.Framework.JavaScriptEngine.JsValue) |
| `Call(window, function, thisValue, arguments)` | Calls a function |
| `ToJsValue(window, value)` | Converts a .NET value: a DOM object is the object the scripts see, a string, a number and a boolean are primitive values, null is `null` |
| `FromJsValue(window, value)` | Converts a JavaScript value: a DOM object is the .NET object, a primitive value is the .NET value, `undefined` and `null` are null, other values are returned as they are |

[JsValue](cref:Meziantou.Framework.JavaScriptEngine.JsValue) is the value type of Meziantou.Framework.JavaScriptEngine: [AsNumber()](cref:Meziantou.Framework.JavaScriptEngine.JsValue.AsNumber), [AsString()](cref:Meziantou.Framework.JavaScriptEngine.JsValue.AsString) and the other members read
it. [Evaluate](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngine.Evaluate) does not run the microtasks: run the event loop to settle the promises.

## Add .NET functions to the pages

Derive from [JavaScriptScriptEngine](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngine) and override [OnRealmCreated](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngine.OnRealmCreated): it runs when a window gets its realm, and can add
properties to its global object:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_HostFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>A script engine whose windows have a <c>hostAdd</c> function.</summary>
private sealed class HostFunctionsScriptEngine : JavaScriptScriptEngine
{
    protected override void OnRealmCreated(GlobalScope global, JsObject globalObject)
    {
        var realm = Engine.CurrentRealm!;
        globalObject.SetProperty("hostAdd", realm.CreateFunction("hostAdd", 2, (_, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
    }
}
```
<!-- endref -->

The functions you add are the only way for the scripts to reach .NET: validate their arguments as you validate the input
of a public API.
