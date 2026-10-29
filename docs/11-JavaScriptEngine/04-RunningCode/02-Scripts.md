---
title: Scripts
description: Run a script and read its result, compile a script once to run it many times, and check its syntax without running it.
---

## Run a script

`realm.Execute` runs a script and returns its *completion value*, the value of its last statement:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CompletionValue;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();

// The result of a script is the value of its last statement
var total = realm.Execute("let total = 0; for (const price of [10, 20, 12.5]) { total += price; } total", "prices.js");
Assert.Equal(42.5, total.AsNumber());

// The scripts of a realm share their global variables and functions
realm.Execute("var counter = 1; function increment() { return ++counter; }");
realm.Execute("increment();");
Assert.Equal(2, realm.Execute("counter").AsNumber());
```
<!-- endref -->

- The second argument is the URL or the file name of the script. It appears in the stack traces and in the locations
  of the errors: give one to every script.
- Declarations have no value: the result of `var x = 1;` alone is `undefined`. End the script with an expression to
  return a value.
- The `var` and `function` declarations of a script are properties of the global object; its `let`, `const` and
  `class` declarations are global bindings, also visible to the next scripts of the realm.
- `Execute` returns when the script ends. The callbacks of the promises it creates run later, from `RunJobs`: see
  [Async code and promises](../06-AsyncAndPromises.md).

To read the result, see [Values](../05-Interop/02-Values.md).

`realm.Evaluate(code)` runs code as an indirect `eval` would, and also returns its completion value.

## Reuse a compiled script

`JsPreparedScript.Parse` parses and compiles a script once. The result is immutable: it runs in any realm of any
engine, on any thread. Use it for the scripts that run many times, such as the same user script for each request:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_PreparedScript;format=md-fenced-code-block;dedent=true -->
```csharp
// Parse and compile once, run many times, in any engine and on any thread
var script = JsPreparedScript.Parse("function fib(n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); } fib(input)", "fib.js");

var results = new double[4];
Parallel.For(0, results.Length, i =>
{
    // An engine runs on one thread at a time: each thread uses its own engine
    using var engine = new JsEngine();
    var realm = engine.CreateRealm();
    using (engine.Enter())
    {
        realm.GlobalObject.SetProperty("input", 10 + i);
    }

    results[i] = realm.Execute(script).AsNumber();
});

Assert.Equal([55, 89, 144, 233], results);
```
<!-- endref -->

`Parse` reports the syntax errors of the whole script. The body of a nested function is compiled on its first call,
once for all the engines.

When the script is a part of a larger document, such as an inline script of an HTML page, pass its position in the
document (`lineOffset` and `columnOffset`): the locations of the errors are then positions in the document.

## Check the syntax of a script

`JsPreparedScript.TryParse` reports a syntax error without throwing, with its position. Use it to validate a script
before you save it:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_ValidateScript;format=md-fenced-code-block;dedent=true -->
```csharp
// TryParse reports a syntax error without throwing
if (JsPreparedScript.TryParse("let total = 1;\nlet = ;", "user.js", lineOffset: 0, columnOffset: 0, out var script, out var error))
{
    Assert.Fail("The script is not valid: " + script.Url);
}

Assert.Equal("user.js", error.Url);
Assert.Equal(2, error.Line);
Assert.Equal(7, error.Column);
```
<!-- endref -->

- `Line` and `Column` are 1-based; the column counts UTF-16 code units.
- `JsPreparedScript.Parse` throws the same error as a `JsSyntaxErrorException`.
- `realm.Execute(string)` does not throw a `JsSyntaxErrorException`: a script that is not valid throws a `JsException`
  whose value is a `SyntaxError`, as in JavaScript.
