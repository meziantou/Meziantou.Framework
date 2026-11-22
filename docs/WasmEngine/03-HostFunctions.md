---
title: Host functions and imports
description: Give .NET functions, memories, tables, globals and tags to the imports of a module.
---

[CreateFunction](cref:Meziantou.Framework.WasmEngine.WasmStore.CreateFunction) creates a function whose steps are a
.NET callback. Give it to the module as an import:

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

- [WasmImports](cref:Meziantou.Framework.WasmEngine.WasmImports) resolves the imports by their module and name; the
  [Define](cref:Meziantou.Framework.WasmEngine.WasmImports.Define) overload that takes an instance defines all its
  exports, to link several modules. [Instantiate](cref:Meziantou.Framework.WasmEngine.WasmStore.Instantiate) also takes
  the values in the order of [Imports](cref:Meziantou.Framework.WasmEngine.WasmModule.Imports).
- A missing import, or a value whose type does not match the import, throws a
  [WasmLinkException](cref:Meziantou.Framework.WasmEngine.WasmLinkException).
- The callback receives one argument per parameter, and writes one result per result of the type. An exception it
  throws stops the execution and is thrown by the call into WebAssembly, unless it is a
  [WasmThrowException](cref:Meziantou.Framework.WasmEngine.WasmThrowException), which throws a WebAssembly exception
  that the module can catch (see [Exceptions and traps](05-ExceptionsAndTraps.md)).
- The `state` argument is kept by the function, in
  [HostState](cref:Meziantou.Framework.WasmEngine.WasmFunction.HostState).
- The source generator of the package writes the type and the conversions of a host function from a .NET method marked
  with [WasmImportAttribute](cref:Meziantou.Framework.WasmEngine.WasmImportAttribute): see
  [Typed exports and imports](10-SourceGenerator.md).

The other imports are created by [CreateMemory](cref:Meziantou.Framework.WasmEngine.WasmStore.CreateMemory),
[CreateTable](cref:Meziantou.Framework.WasmEngine.WasmStore.CreateTable),
[CreateGlobal](cref:Meziantou.Framework.WasmEngine.WasmStore.CreateGlobal) and
[CreateTag](cref:Meziantou.Framework.WasmEngine.WasmStore.CreateTag), or are the exports of another instance of the
same store.

## Host values

An `externref` holds any .NET object: [FromHostValue](cref:Meziantou.Framework.WasmEngine.WasmValue.FromHostValue)
wraps it in a [WasmHostReference](cref:Meziantou.Framework.WasmEngine.WasmHostReference), and the module gives it back
unchanged. The other references are the functions, the structs and the arrays of the GC types
([WasmGcObject](cref:Meziantou.Framework.WasmEngine.WasmGcObject), whose arrays are read and written with
[GetArrayElement](cref:Meziantou.Framework.WasmEngine.WasmStore.GetArrayElement) and
[SetArrayElement](cref:Meziantou.Framework.WasmEngine.WasmStore.SetArrayElement)), the `i31ref` scalars
([WasmI31Reference](cref:Meziantou.Framework.WasmEngine.WasmI31Reference)) and the exceptions.
