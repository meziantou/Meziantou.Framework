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
- [Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute) returns when the script ends. The callbacks of the promises it creates run later, from [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs): see
  [Async code and promises](../06-AsyncAndPromises.md).

To read the result, see [Values](../05-Interop/02-Values.md).

`realm.Evaluate(code)` runs code as an indirect `eval` would, and also returns its completion value.

## Reuse a compiled script

[JsPreparedScript.Parse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.Parse) parses and compiles a script once. The result is immutable: it runs in any realm of any
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

[Parse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.Parse) reports the syntax errors of the whole script. The body of a nested function is compiled on its first call,
once for all the engines.

`realm.Execute(string)` parses and compiles its code on every call, and keeps no cache. When the same code runs often,
parse it once and keep the [JsPreparedScript](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript) yourself, for instance in a field or in a dictionary of the scripts of your
application. Bound the dictionary when the scripts come from your users: the engine does not evict anything.

A prepared script holds code, not state. Each run evaluates it again, in the realm that runs it:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_PreparedScriptRuns;format=md-fenced-code-block;dedent=true -->
```csharp
var counter = JsPreparedScript.Parse("var count = (count ?? 0) + 1; count", "counter.js");

using var engine = new JsEngine();
var first = engine.CreateRealm();
var second = engine.CreateRealm();

// Each run evaluates the script again, in the realm that runs it: its global declarations go to that realm
Assert.Equal(1, first.Execute(counter).AsNumber());
Assert.Equal(2, first.Execute(counter).AsNumber());
Assert.Equal(1, second.Execute(counter).AsNumber());

// A realm cannot declare the same let, const or class twice: run such a script once per realm
var settings = JsPreparedScript.Parse("const limit = 10; limit", "settings.js");
Assert.Equal(10, first.Execute(settings).AsNumber());
var exception = Assert.Throws<JsException>(() => first.Execute(settings));
Assert.StartsWith("SyntaxError", exception.Message, StringComparison.Ordinal);

// The URL given to Parse is the location of the errors of every run
var failing = JsPreparedScript.Parse("let total = 0;\nthrow new Error('Invalid total');", "https://example.com/total.js");
exception = Assert.Throws<JsException>(() => second.Execute(failing));
Assert.Equal(new JsSourceLocation("https://example.com/total.js", 2, 1), exception.Location);
```
<!-- endref -->

- The global declarations of a run go to the realm that runs it, as for `realm.Execute(string)`. Running the script
  again in the same realm sees the variables of the previous runs.
- A realm cannot declare the same `let`, `const` or `class` twice: a second run of a script that declares one throws a
  `SyntaxError`. Wrap the code in a function, or run it in a new realm, to run it several times.
- The URL given to [Parse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.Parse) is the location of the errors and of the stack traces of every run; it cannot be changed
  per run.
- The script can be shared between threads, but each engine stays on one thread at a time: see
  [Threads](../03-EnginesAndRealms.md#threads).

A prepared script keeps only the compiled code: each engine keeps what it learns while it runs the script (the inline
caches of the property accesses). Keeping a prepared script alive does not keep the engines that ran it alive, and the
engines that run the same script do not slow each other down.

When the script is a part of a larger document, such as an inline script of an HTML page, pass its position in the
document (`lineOffset` and `columnOffset`): the locations of the errors are then positions in the document.

## Check the syntax of a script

[JsPreparedScript.TryParse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.TryParse) reports a syntax error without throwing, with its position. Use it to validate a script
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

- [Line](cref:Meziantou.Framework.JavaScriptEngine.JsSyntaxErrorException.Line) and [Column](cref:Meziantou.Framework.JavaScriptEngine.JsSyntaxErrorException.Column) are 1-based; the column counts UTF-16 code units.
- [JsPreparedScript.Parse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.Parse) throws the same error as a [JsSyntaxErrorException](cref:Meziantou.Framework.JavaScriptEngine.JsSyntaxErrorException).
- `realm.Execute(string)` does not throw a [JsSyntaxErrorException](cref:Meziantou.Framework.JavaScriptEngine.JsSyntaxErrorException): a script that is not valid throws a [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException)
  whose value is a `SyntaxError`, as in JavaScript.

## How the code runs

The engine compiles the source to bytecode, which an interpreter runs. A function that runs often, 1,000 calls or loop
iterations by default, is then compiled to .NET code, which the .NET JIT compiles to machine code. The compiled
code runs the same instructions without the dispatch of the interpreter. The code that gains the most spends its time
in JavaScript itself: loops, arithmetic, local variables and property accesses. The code that spends its time in the
built-ins (JSON, regular expressions, Intl...) runs at their speed either way, and a call from a JavaScript function to
another one costs the same.

- The compilation changes nothing a script can observe: the results, the errors and their locations, the stack traces
  and the [limits](../07-LimitsAndSandboxing.md) are the same as with the interpreter.
- The compiled code belongs to the compiled script, like the bytecode: a function of a
  [JsPreparedScript](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript) is compiled once for all the engines
  and the threads that run it.
- The compilation is skipped with Native AOT, where no code can be generated at run time, for the code compiled for a
  [debugger](05-Debugging.md), and for the very large functions: their code is interpreted.

[CompilationThreshold](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CompilationThreshold) sets how many
calls and loop iterations make a function hot. Lower it when your scripts run the same functions many times from the
start, such as a computation run once per request: they reach their compiled speed sooner. Raise it when the scripts
run many functions a few times each: fewer functions pay for a compilation they do not recover. The engines that run the
same [JsPreparedScript](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript) count together.

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CompilationThreshold;format=md-fenced-code-block;dedent=true -->
```csharp
// The functions of this engine are compiled after 100 calls or loop iterations, instead of 1000
using var engine = new JsEngine(new JsEngineOptions { CompilationThreshold = 100 });
var realm = engine.CreateRealm();
var result = realm.Execute("function square(x) { return x * x; } let sum = 0; for (let i = 0; i < 1_000; i++) { sum += square(i); } sum");
Assert.Equal(332_833_500, result.AsNumber());
```
<!-- endref -->

Set [EnableCompilation](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.EnableCompilation) to false to only
interpret the code, for instance when the scripts are short and run once, so that the compilation is not worth its
time:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_DisableCompilation;format=md-fenced-code-block;dedent=true -->
```csharp
// The code of this engine is only interpreted: the hot functions are not compiled to .NET code
using var engine = new JsEngine(new JsEngineOptions { EnableCompilation = false });
var realm = engine.CreateRealm();
var result = realm.Execute("let sum = 0; for (let i = 0; i < 100_000; i++) { sum += i; } sum");
Assert.Equal(4_999_950_000, result.AsNumber());
```
<!-- endref -->
