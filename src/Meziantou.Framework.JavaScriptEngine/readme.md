# Meziantou.Framework.JavaScriptEngine

An ECMAScript engine written in .NET: a hand-written parser, a bytecode compiler and an interpreter. It is validated by
[test262](https://github.com/tc39/test262), the conformance suite of ECMAScript: see
[the test262 results](../../tests/Meziantou.Framework.JavaScriptEngine.Test262/summary.md). It implements ECMAScript
2025, ECMA-402 (`Intl`) and `Temporal`, and the stage 3 proposals `import defer` and source phase imports (without
module sources). It uses no reflection: it is trimming- and Native AOT-compatible, and its results do not depend on the
globalization settings of the process (the Unicode, time zone and CLDR data are part of the library).

It is the script engine of [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md), through
[Meziantou.Framework.WebPlatform.JavaScript](../Meziantou.Framework.WebPlatform.JavaScript/readme.md).

## Run scripts

A `JsEngine` is an agent: it runs the code of its realms on one thread at a time, and owns the job queue of the
promises. A `JsRealm` has its own global object and intrinsics.

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

`JsEngineOptions` limits each entry into JavaScript (a script, a call from .NET): `Timeout`, `MemoryLimit` (the bytes
allocated by the thread), `MaxStatements`, `RecursionLimit` (10,000 calls by default: the frames of the interpreter are
not on the .NET stack, and an unbounded recursion throws a `RangeError`), `RegExpTimeout` and a `CancellationToken`. A
script that exceeds a limit is stopped with a `JsTerminationException`, which JavaScript code cannot catch. The options
also give the clock (`TimeProvider`), the time zone, the default locale, the host hooks (`JsHost`: the compilation of
strings by `eval`, the promise rejection tracking, the job callbacks) and the module loader.

The members of the objects (`JsObject.GetProperty`, `SetProperty`, `JsRealm.CreateObject`...) are used inside a scope
of the engine (`using var scope = engine.Enter()`); `JsRealm.Execute`, `JsEngine.Call` and `JsEngine.Construct` enter
it themselves.

## Modules

The modules are loaded by a `JsModuleLoader`: derive from it and override `LoadImportedModule` to resolve a specifier
and complete the request with a module (`JsRealm.ParseModule` or `JsRealm.CreateJsonModule`), or fail it. A module is
loaded, linked, then evaluated, as in the specification:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Modules;format=md-fenced-code-block;dedent=true -->
```csharp
var files = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["main.js"] = "import { square } from './math.js'; globalThis.result = square(7);",
    ["math.js"] = "export function square(x) { return x * x; }",
};

using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new DictionaryModuleLoader(files) });
var realm = engine.CreateRealm();
var module = realm.ParseModule(files["main.js"], "main.js");
module.LoadRequestedModules();
engine.RunJobs();
module.Link();
module.Evaluate();
engine.RunJobs();

Assert.Equal(49, realm.Execute("result").AsNumber());
```
<!-- endref -->

## Documentation

[docs/architecture.md](docs/architecture.md) describes the values, the objects, the realms, the compiler and the
interpreter.
