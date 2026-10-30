---
title: Handling errors
description: Catch the errors of the scripts, find where they were thrown, and throw JavaScript errors from .NET functions.
---

## Catch the errors of a script

An error that a script does not catch is thrown to .NET as a `JsException`:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CatchErrors;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();

var exception = Assert.Throws<JsException>(() => realm.Execute("function check(order) {\n  if (!order.id) throw new RangeError('The order has no id');\n}\ncheck({});", "orders.js"));

// The thrown value, and where it was thrown
Assert.Equal("RangeError: The order has no id", exception.Message);
Assert.Equal(new JsSourceLocation("orders.js", 2, 18), exception.Location);
using (engine.Enter())
{
    var error = exception.Value.AsObject();
    Assert.Equal("RangeError", error.GetProperty("name").AsString());
    Assert.Equal("The order has no id", error.GetProperty("message").AsString());
    Assert.Equal("RangeError: The order has no id\n    at check (orders.js:2:28)\n    at orders.js:4:1", error.GetErrorStack());
}

// A script can throw any value
exception = Assert.Throws<JsException>(() => realm.Execute("throw 42;"));
Assert.Equal(42, exception.Value.AsNumber());
```
<!-- endref -->

- `Value` is the thrown value. A script can throw any value, not only errors: check its kind before you read it.
- `Message` describes the thrown value without running code of the script: the `name` and the `message` of an error
  (`RangeError: The order has no id`), or `Uncaught` and the value for anything else (`Uncaught 42`).
- `Location` is the position of the `throw` (or of the operation that failed), with the URL given to `Execute`. Lines
  and columns are 1-based.
- `GetErrorStack` returns the `stack` of an error object. Its first frame is where the error object was created
  (`new RangeError(...)`), which can differ from the position of the `throw`.

The same exception comes from `JsEngine.Call`, `JsEngine.Construct`, and from the conversions that run code of the
script, such as `engine.ToString` calling a `toString` method that throws.

## The exceptions of the engine

| Exception | When |
| --- | --- |
| `JsException` | A script threw a value that it did not catch, or the code is not valid (a `SyntaxError`). |
| `JsSyntaxErrorException` | `JsPreparedScript.Parse` got a script that is not valid. See [Scripts](02-Scripts.md#check-the-syntax-of-a-script). |
| `JsTerminationException` | A limit stopped the script, or it was canceled. See [Limits and sandboxing](../07-LimitsAndSandboxing.md). |

## Throw a JavaScript error from .NET

A .NET function called by a script throws a JavaScript error with `realm.CreateException`. The script can catch it
like any error:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_ThrowFromDotNet;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("parseAge", realm.CreateFunction("parseAge", 1, (_, arguments) =>
    {
        var text = arguments.Length > 0 ? engine.ToString(arguments[0]) : "";
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var age))
        {
            // A JavaScript error, which the scripts can catch
            throw realm.CreateException(JsErrorType.TypeError, "Invalid age: " + text);
        }

        return age;
    }));
}

Assert.Equal(42, realm.Execute("parseAge('42')").AsNumber());
Assert.Equal("TypeError: Invalid age: abc", realm.Execute("try { parseAge('abc'); } catch (e) { `${e}` }").AsString());
```
<!-- endref -->

- `JsErrorType` gives the native error types: `Error`, `TypeError`, `RangeError`, `SyntaxError`, `ReferenceError`,
  `EvalError` and `UriError`. Prefer the type that a built-in would throw: `TypeError` for an argument of the wrong
  type, `RangeError` for a value out of range.
- `realm.CreateError` creates the error object without throwing it, for instance to reject a promise.
- To throw any other value, throw `new JsException(value)`.

## Exceptions that are not JavaScript errors

Any other .NET exception thrown by a .NET function is not a JavaScript error. The script cannot catch it: it goes
through the script, without running its `catch` blocks, to the code that called `Execute`:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_DotNetExceptions;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("fail", realm.CreateFunction("fail", 0, (_, _) => throw new InvalidOperationException("Unexpected state")));
}

// A .NET exception is not a JavaScript error: the script cannot catch it, and it goes to the caller
Assert.Throws<InvalidOperationException>(() => realm.Execute("try { fail(); } catch { }"));
```
<!-- endref -->

Convert the failures that a script should handle, such as invalid input, to JavaScript errors. Let the other ones
propagate: they are bugs or failures of your application.
