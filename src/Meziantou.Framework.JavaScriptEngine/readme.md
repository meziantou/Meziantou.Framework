# Meziantou.Framework.JavaScriptEngine

An ECMAScript engine written in .NET: a hand-written parser, a bytecode compiler, an interpreter, and a compiler of the
hot functions to .NET code. It is validated by
[test262](https://github.com/tc39/test262), the conformance suite of ECMAScript: see
[the test262 results](../../docs/JavaScriptEngine/10-Conformance.md), and by the
[Web Tooling Benchmark](../../tests/Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark/readme.md) (babel,
TypeScript, prettier, terser... give the same results as in Node.js) and
[about 60 npm packages](../../tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/readme.md) (lightningcss, sass,
tailwindcss, prettier, Vue, date-fns, d3... loaded as ES modules in a page of Meziantou.Framework.WebPlatform, and
compared with Chromium). It implements ECMAScript
2025 (including the proper tail calls of strict code), ECMA-402 (`Intl`) and `Temporal`, the stage 3 proposals
[decorators](https://github.com/tc39/proposal-decorators) (with the `accessor` fields and the
[metadata](https://github.com/tc39/proposal-decorator-metadata) of `Symbol.metadata`), `import defer`, source phase
imports (the module sources are given by the host),
[immutable `ArrayBuffer`](https://github.com/tc39/proposal-immutable-arraybuffer) (`transferToImmutable`,
`sliceToImmutable`), Await Dictionary (`Promise.allKeyed` and `Promise.allSettledKeyed`) and Non-extensible Applies to
Private (a private field or method cannot be added to a non-extensible object), and the `ShadowRealm`,
[Import Text](https://github.com/tc39/proposal-import-text), [Import Bytes](https://github.com/tc39/proposal-import-bytes)
and [deferred re-exports](https://github.com/tc39/proposal-deferred-reexports) (`export defer`) proposals. It binds .NET code without reflection: it is trimming- and Native AOT-compatible (with Native AOT, the code is only
interpreted), and its results do not depend
on the globalization settings of the process (the Unicode, time zone and CLDR data are part of the library, and the word
lists of ICU that `Intl.Segmenter` uses for Chinese, Japanese, Thai, Lao, Khmer and Burmese come from
[Meziantou.Framework.Unicode.Dictionaries](../Meziantou.Framework.Unicode.Dictionaries/readme.md)).

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

// Expose a .NET function to the scripts. They can pass any value, and fewer arguments than expected: convert the
// arguments explicitly (AsNumber throws when a value is not a number)
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("add", realm.CreateFunction("add", 2, (thisValue, arguments) =>
    {
        var left = arguments.Length > 0 ? engine.ToNumber(arguments[0]) : double.NaN;
        var right = arguments.Length > 1 ? engine.ToNumber(arguments[1]) : double.NaN;
        return left + right;
    }));
}

var result = realm.Execute("const values = [1, 2, 3].map(x => add(x, 10)); values.join('-')");
Assert.Equal("11-12-13", result.AsString());
Assert.Equal("6,NaN", realm.Execute("[add('5', 1), add(1)].join()").AsString());

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

## Expose .NET classes

Mark a `partial` class with `[JsExport]`: a source generator, included in the package, writes its JavaScript class at
build time, so the binding uses no reflection. The public constructor, methods, properties and fields are exposed with
camelCase names (`[JsName]` renames a member, `[JsIgnore]` hides it), the arguments and the results are converted, and a
task is a promise.

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_ExportClassDeclaration;format=md-fenced-code-block;dedent=true -->
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

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineEmbeddingTests.cs;region=JavaScriptEngine_ExportClass;format=md-fenced-code-block;dedent=true -->
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

## Modules

The modules are loaded by a `JsModuleLoader`: derive from it and override `LoadImportedModule` to resolve a specifier
and complete the request with a module (`JsRealm.ParseModule`, or for the `json`, `text` and `bytes` values of the `type`
import attribute `JsRealm.CreateJsonModule`, `CreateTextModule` and `CreateBytesModule`), or fail it. A module is
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

The value of a source phase import (`import source x from` and `import.source()`) is the module source of the module
(`JsModule.ModuleSource`), set by the module loader: a `JsHostObject` that extends `JsRealm.AbstractModuleSource` and
whose `ModuleSourceClassName` is set (e.g. `WebAssembly.Module`). The source phase import of a module that has no module
source, such as a JavaScript module, throws a SyntaxError.

## Documentation

[docs/architecture.md](docs/architecture.md) describes the values, the objects, the realms, the compiler and the
interpreter.
