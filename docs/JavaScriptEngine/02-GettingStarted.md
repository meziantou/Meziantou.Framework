---
title: Getting started
description: Install the package, run a script, expose a .NET function and handle errors.
---

## Install the package

Add the `Meziantou.Framework.JavaScriptEngine` package to a project:

```bash
dotnet add package Meziantou.Framework.JavaScriptEngine
```

The examples of this documentation omit the `using` directives: add
`using Meziantou.Framework.JavaScriptEngine;`.

## Run a script

Create an engine, create a realm, and run the script in the realm:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_HelloWorld;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();

var result = realm.Execute("const name = 'world'; `Hello, ${name}!`");
Assert.Equal("Hello, world!", result.AsString());
```
<!-- endref -->

- `JsEngine` runs the code and owns its limits and its job queue. It is disposable.
- `JsRealm` is a global environment: a global object and its built-ins (`Array`, `Math`, `JSON`...).
- `Execute` returns the value of the last statement of the script, as a `JsValue`. `AsString` reads a string value.

## Expose a function, run promises, catch errors

This example limits the execution of the scripts, gives them a .NET function, runs promise callbacks and catches a
JavaScript error:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Execute;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine(new JsEngineOptions
{
    Timeout = TimeSpan.FromSeconds(5), // per entry into JavaScript
    RecursionLimit = 1_000,
});

var realm = engine.CreateRealm();

// Expose a .NET function to the scripts
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", 2, (thisValue, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
}

var result = realm.Execute("const values = [1, 2, 3].map(x => add(x, 10)); values.join('-')");
Assert.Equal("11-12-13", result.AsString());

// The promise jobs run when the host asks for them
realm.Execute("globalThis.log = []; Promise.resolve().then(() => log.push('job')); log.push('script');");
engine.RunJobs();
Assert.Equal("script,job", realm.Execute("log.join()").AsString());

// An uncaught JavaScript error is a JsException
var exception = Assert.Throws<JsException>(() => realm.Execute("null.x"));
Assert.StartsWith("TypeError", exception.Message, StringComparison.Ordinal);
```
<!-- endref -->

- `JsEngineOptions` sets the limits of each entry into JavaScript. See
  [Limits and sandboxing](07-LimitsAndSandboxing.md).
- `CreateFunction` creates a JavaScript function implemented by a .NET delegate. Reading and writing properties, such
  as `GlobalObject.SetProperty`, happens inside `engine.Enter()`. See [Engines and realms](03-EnginesAndRealms.md) and
  [Functions](05-Interop/04-Functions.md).
- The callbacks of promises run when you call `RunJobs`. See [Async code and promises](06-AsyncAndPromises.md).
- An uncaught JavaScript error is thrown as a `JsException`. See
  [Handling errors](04-RunningCode/04-HandlingErrors.md).

## Next steps

- [Engines and realms](03-EnginesAndRealms.md): engines, realms, scopes and threads.
- [Running code](04-RunningCode/01-Overview.md): scripts, modules and errors.
- [Exchanging values](05-Interop/01-Overview.md): values, objects, functions and binary data.
