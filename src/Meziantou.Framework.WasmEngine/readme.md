# Meziantou.Framework.WasmEngine

A WebAssembly engine written in .NET (no native library): compile, validate and instantiate
WebAssembly 3.0 modules (and the threads), give them .NET functions, read and write their memories, tables and globals,
throw and catch their exceptions, interrupt them, and trace them with an `ActivitySource` and a `Meter`.

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
// A runtime compiles the modules: create one and share it (it can be used from any thread)
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (func (export "add") (param i32 i32) (result i32)
        (i32.add (local.get 0) (local.get 1))))
    """));

// A store holds the instances: use it from one thread at a time
using var store = runtime.CreateStore();
var instance = store.Instantiate(module);
var add = instance.GetFunction("add")!;

var results = add.Call(WasmValue.FromI32(40), WasmValue.FromI32(2));
Assert.Equal(42, results[0].I32);
```
<!-- endref -->

## Host functions

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_HostFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "env" "log" (func $log (param i32)))
      (func (export "run")
        (call $log (i32.const 1))
        (call $log (i32.const 2))))
    """));

using var store = runtime.CreateStore();
var logged = new List<int>();
var log = store.CreateFunction(
    new WasmFunctionType([WasmValueType.I32], []),
    (arguments, results) => logged.Add(arguments[0].I32));

// The imports are resolved by their module and name
var instance = store.Instantiate(module, new WasmImports().Define("env", "log", log));
instance.GetFunction("run")!.Call();

Assert.Equal([1, 2], logged);
```
<!-- endref -->

## Documentation

See [the documentation](../../docs/WasmEngine/01-Introduction.md): memories, tables and globals, exceptions and traps,
limits and interruption, shared memories, telemetry, and conformance. To run the WebAssembly
modules of the scripts of Meziantou.Framework.WebPlatform, use
[Meziantou.Framework.WebPlatform.Wasm](../Meziantou.Framework.WebPlatform.Wasm/readme.md).
