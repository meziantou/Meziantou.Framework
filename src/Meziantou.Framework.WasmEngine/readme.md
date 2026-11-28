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

## Typed exports and imports

The source generator of the package writes a class per `.wasm` or `.wat` file of the `AdditionalFiles` items, with a
strongly typed method per exported function, and the host functions of the methods marked with `[WasmImport]`:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmSourceGeneratorTests.cs;region=WasmEngine_TypedExports;format=md-fenced-code-block;dedent=true -->
```csharp
// fib.wat is an AdditionalFiles item of the project: the generator writes the FibModule class
using var runtime = new WasmRuntime();
using var store = runtime.CreateStore();
var fib = FibModule.Instantiate(store, FibModule.Compile(runtime));

Assert.Equal(6765, fib.Fib(20));
```
<!-- endref -->

## Components

The engine runs the components of the component model: the canonical ABI converts their strings, lists, records,
variants, resources and streams to and from .NET values.

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmComponentTests.cs;region=Component_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A component that exports greet(name: string) -> string: the canonical ABI copies the strings between the
// memory of the component and .NET, the component allocates its memory with its realloc function
var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
    (component
      (core module $M
        (memory (export "memory") 1)
        (global $heap (mut i32) (i32.const 1024))
        (func $realloc (export "realloc") (param i32 i32 i32 i32) (result i32)
          (local $pointer i32)
          (local.set $pointer (global.get $heap))
          (global.set $heap (i32.add (global.get $heap) (local.get 3)))
          (local.get $pointer))
        (data (i32.const 16) "Hello, ")
        (func (export "greet") (param $name i32) (param $length i32) (result i32)
          (local $result i32)
          (local.set $result (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.add (local.get $length) (i32.const 7))))
          (memory.copy (local.get $result) (i32.const 16) (i32.const 7))
          (memory.copy (i32.add (local.get $result) (i32.const 7)) (local.get $name) (local.get $length))
          ;; The result is the address and the length of the string, at the address 0
          (i32.store (i32.const 0) (local.get $result))
          (i32.store (i32.const 4) (i32.add (local.get $length) (i32.const 7)))
          (i32.const 0)))
      (core instance $m (instantiate $M))
      (func (export "greet") (param "name" string) (result string)
        (canon lift (core func $m "greet") (memory (core memory $m "memory")) (realloc (core func $m "realloc")))))
    """));

using var store = runtime.CreateStore();
var instance = store.InstantiateComponent(component);
var greet = instance.GetFunction("greet")!;
var result = greet.Call(ComponentValue.FromString("World"));

Assert.Equal("Hello, World", result?.AsString());
```
<!-- endref -->

## Documentation

See [the documentation](../../docs/WasmEngine/01-Introduction.md): memories, tables and globals, exceptions and traps,
limits and interruption, shared memories, telemetry, conformance, the typed exports and imports, and the component
model. To run the programs compiled for WASI (`wasm32-wasip1`, and the components of WASI 0.2 and 0.3), use
[Meziantou.Framework.WasmEngine.Wasi](../Meziantou.Framework.WasmEngine.Wasi/readme.md). To run the WebAssembly
modules of the scripts of Meziantou.Framework.WebPlatform, use
[Meziantou.Framework.WebPlatform.Wasm](../Meziantou.Framework.WebPlatform.Wasm/readme.md).
