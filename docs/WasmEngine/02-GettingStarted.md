---
title: Getting started
description: Compile a module, instantiate it in a store, and call its exported functions.
---

A [WasmRuntime](cref:Meziantou.Framework.WasmEngine.WasmRuntime) compiles the modules, and a
[WasmStore](cref:Meziantou.Framework.WasmEngine.WasmStore) holds their instances:

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

- [Compile](cref:Meziantou.Framework.WasmEngine.WasmRuntime.Compile) takes the binary of a module and throws a
  [WasmCompileException](cref:Meziantou.Framework.WasmEngine.WasmCompileException) when it is not valid.
  [Validate](cref:Meziantou.Framework.WasmEngine.WasmRuntime.Validate) only tells whether it is valid.
- [WatToWasm](cref:Meziantou.Framework.WasmEngine.WasmRuntime.WatToWasm) converts the text format to the binary format;
  it throws a `FormatException` when the text is not valid.
- A module is immutable: compile it once, and instantiate it in as many stores as you need, from any thread. Its
  [Imports](cref:Meziantou.Framework.WasmEngine.WasmModule.Imports) and
  [Exports](cref:Meziantou.Framework.WasmEngine.WasmModule.Exports) describe their types, and
  [GetCustomSections](cref:Meziantou.Framework.WasmEngine.WasmModule.GetCustomSections) returns the contents of its
  custom sections.
- A store is not thread-safe: use it from one thread at a time. Dispose it when it is no longer used: its objects can
  no longer be used afterwards. A store that is not disposed is released when it is collected.

## Calling functions

[GetFunction](cref:Meziantou.Framework.WasmEngine.WasmInstance.GetFunction) returns an exported function, or null.
[Call](cref:Meziantou.Framework.WasmEngine.WasmFunction.Call) returns an array of results;
[Invoke](cref:Meziantou.Framework.WasmEngine.WasmFunction.Invoke) writes them to a span you provide. A
[WasmValue](cref:Meziantou.Framework.WasmEngine.WasmValue) is a number (`i32`, `i64`, `f32`, `f64`), a 128-bit vector
or a reference; the arguments must match the types of the parameters, or the call throws a
[WasmTypeMismatchException](cref:Meziantou.Framework.WasmEngine.WasmTypeMismatchException).

The store returns the same .NET object for the same function, memory, table, global or tag, whichever export or table
element it comes from.
