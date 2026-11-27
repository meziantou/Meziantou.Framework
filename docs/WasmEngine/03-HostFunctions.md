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

## The caller

A host function that reads or writes the memory of the module, such as a function that prints a string, needs the
instance that called it: the [CreateFunction](cref:Meziantou.Framework.WasmEngine.WasmStore.CreateFunction(Meziantou.Framework.WasmEngine.WasmFunctionType,Meziantou.Framework.WasmEngine.WasmHostCallerCallback,System.Object))
overload that takes a [WasmHostCallerCallback](cref:Meziantou.Framework.WasmEngine.WasmHostCallerCallback) gives it to
the callback, as a [WasmCaller](cref:Meziantou.Framework.WasmEngine.WasmCaller):

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_HostFunctionCaller;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "env" "print" (func $print (param i32 i32)))
      (memory (export "memory") 1)
      (data (i32.const 16) "Hello, world")
      (func (export "run")
        (call $print (i32.const 16) (i32.const 12))))
    """));

using var store = runtime.CreateStore();
var printed = new List<string>();

// The host function receives its caller: the instance whose code called it, whose memory holds the text
var print = store.CreateFunction(
    new WasmFunctionType([WasmValueType.I32, WasmValueType.I32], []),
    (caller, arguments, results) =>
    {
        var memory = caller.GetMemory("memory")!;
        printed.Add(Encoding.UTF8.GetString(memory.GetSpan().Slice(arguments[0].I32, arguments[1].I32)));
    });

var instance = store.Instantiate(module, new WasmImports().Define("env", "print", print));
instance.GetFunction("run")!.Call();

Assert.Equal(["Hello, world"], printed);
```
<!-- endref -->

- [Instance](cref:Meziantou.Framework.WasmEngine.WasmCaller.Instance) is the instance whose code called the function,
  directly, through a table, by reference or with a tail call. When a function of another instance (an import) calls the
  host function, the caller is that other instance. For the start function of a module, it is the instance being
  instantiated. It is null when .NET calls the function ([Invoke](cref:Meziantou.Framework.WasmEngine.WasmFunction.Invoke)).
- [GetMemory](cref:Meziantou.Framework.WasmEngine.WasmCaller.GetMemory) and
  [GetExport](cref:Meziantou.Framework.WasmEngine.WasmCaller.GetExport) return the exports of the calling instance.
- The bytes of a memory are replaced when it grows: get the span of the memory at each call, after the calls that can
  grow it.
- A `WasmCaller` is a `ref struct`: it is valid only while the callback runs.

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
