---
title: Typed exports and imports
description: Generate a strongly typed class from the exports of a module, and the host functions of the imports from .NET methods.
---

The package includes a source generator. It writes at build time the code that converts the arguments and the results to
and from [WasmValue](cref:Meziantou.Framework.WasmEngine.WasmValue): without reflection, so it works with trimming and
Native AOT.

## Typed exports

Add a module of the binary format (`.wasm`) or of the text format (`.wat`) to the `AdditionalFiles` items of the
project, for instance `<AdditionalFiles Include="fib.wat" />`. The generator writes a class named after the file
(`FibModule`), with a method per exported function. Here `fib.wat` exports a function `fib` whose type is
`(func (param i32) (result i32))`:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmSourceGeneratorTests.cs;region=WasmEngine_TypedExports;format=md-fenced-code-block;dedent=true -->
```csharp
// fib.wat is an AdditionalFiles item of the project: the generator writes the FibModule class
using var runtime = new WasmRuntime();
using var store = runtime.CreateStore();
var fib = FibModule.Instantiate(store, FibModule.Compile(runtime));

Assert.Equal(6765, fib.Fib(20));
```
<!-- endref -->

- `Compile` compiles the module embedded in the class. `Instantiate` instantiates a module and wraps the instance, and
  the constructor wraps an instance you already have. The constructor throws a
  [WasmLinkException](cref:Meziantou.Framework.WasmEngine.WasmLinkException) when an export is missing or has another
  type: the class can wrap a module loaded at run time, as long as its exports match the file.
- The methods are named after the exports in PascalCase (`add_two` is `AddTwo`); a number is appended to a name that is
  already used. The parameters keep the names of the text format (`(param $count i32)` is `count`), or are named
  `arg0`, `arg1`... A function with several results returns a tuple.
- The exported memories, tables, globals and tags are properties of type
  [WasmMemory](cref:Meziantou.Framework.WasmEngine.WasmMemory), [WasmTable](cref:Meziantou.Framework.WasmEngine.WasmTable),
  [WasmGlobal](cref:Meziantou.Framework.WasmEngine.WasmGlobal) and [WasmTag](cref:Meziantou.Framework.WasmEngine.WasmTag).
  `Instance` is the wrapped instance.
- A call writes its arguments and reads its results on the stack: the calls whose values are numbers do not allocate.

The values have these .NET types:

| WebAssembly | .NET |
| --- | --- |
| `i32`, `i64`, `f32`, `f64` | `int`, `long`, `float`, `double` |
| `externref`, `(ref extern)` | `object?`, `object`: the value of a [WasmHostReference](cref:Meziantou.Framework.WasmEngine.WasmHostReference) |
| `funcref`, `(ref func)`, the references to a function type | [WasmFunction](cref:Meziantou.Framework.WasmEngine.WasmFunction) (nullable or not) |
| `v128` and the other references | [WasmValue](cref:Meziantou.Framework.WasmEngine.WasmValue) |

Set metadata on the item to change the class:

| Metadata | Default | Description |
| --- | --- | --- |
| `WasmClassName` | The file name in PascalCase, followed by `Module` | The name of the class |
| `WasmNamespace` | The `RootNamespace` of the project | The namespace of the class |
| `WasmVisibility` | `internal` | `public` to make the class public |
| `WasmEmbed` | `true` | `false` to not embed the module in the assembly: the class has no `Compile` method, and wraps the instances of a module you load |

An invalid module is reported at build time (MFWASM005), at its position for the text format.

## Typed imports

Mark the methods of a partial class with
[WasmImportAttribute](cref:Meziantou.Framework.WasmEngine.WasmImportAttribute) and the module and the name of the import.
The generator writes a `DefineWasmImports` method, which creates a host function per method:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmSourceGeneratorTests.cs;region=WasmEngine_TypedImportsHost;format=md-fenced-code-block;dedent=true -->
```csharp
// A partial class: the generator adds the DefineWasmImports method
public sealed partial class Logger
{
    private readonly List<int> _values = [];

    public IReadOnlyList<int> Values => _values;

    // A host function of the import "env" "log" whose type is (func (param i32))
    [WasmImport("env", "log")]
    public void Log(int value) => _values.Add(value);

    // A static method: (func (param f64) (result f64))
    [WasmImport("env", "scale")]
    public static double Scale(double value) => value * 2;
}
```
<!-- endref -->

Give the imports to the instantiation of a module:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmSourceGeneratorTests.cs;region=WasmEngine_TypedImports;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "env" "log" (func $log (param i32)))
      (import "env" "scale" (func $scale (param f64) (result f64)))
      (func (export "run") (result f64)
        (call $log (i32.const 1))
        (call $scale (f64.const 21))))
    """));

using var store = runtime.CreateStore();
var logger = new Logger();

// DefineWasmImports is generated: it creates a host function per method marked with [WasmImport]
var instance = store.Instantiate(module, logger.DefineWasmImports(store));
var result = instance.GetFunction("run")!.Call();

Assert.Equal(42.0, result[0].F64);
Assert.Equal([1], logger.Values);
```
<!-- endref -->

- `DefineWasmImports` defines the imports in the [WasmImports](cref:Meziantou.Framework.WasmEngine.WasmImports) you
  give it, or in a new one, and returns them: chain the classes to define the imports of several of them. It is static
  when all the methods are static, and an instance method otherwise.
- The parameters and the results are `int` or `uint` (`i32`), `long` or `ulong` (`i64`), `float` (`f32`), `double`
  (`f64`), `object` (`externref`) and [WasmFunction](cref:Meziantou.Framework.WasmEngine.WasmFunction) (`funcref`). In a
  nullable context, `object` and `WasmFunction` are the non-nullable references (`(ref extern)`, `(ref func)`), and
  `object?` and `WasmFunction?` the nullable ones. A method with several results returns a tuple.
- The type of the host function must match the type of the import, or the instantiation throws a
  [WasmLinkException](cref:Meziantou.Framework.WasmEngine.WasmLinkException).
- A method can have several attributes: it is then the host function of several imports.
- The generator reports the methods it cannot convert at build time: a parameter of another type or passed by reference,
  a generic method, an instance method of a struct (MFWASM003), a class that is not partial (MFWASM001), or two methods
  of the same import (MFWASM004).

### The caller

A first parameter of type [WasmCaller](cref:Meziantou.Framework.WasmEngine.WasmCaller) is not a parameter of the
WebAssembly function: it receives the caller of the host function, such as the instance whose memory holds the data the
function reads (see [The caller](03-HostFunctions.md#the-caller)):

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmSourceGeneratorTests.cs;region=WasmEngine_TypedImportsCallerHost;format=md-fenced-code-block;dedent=true -->
```csharp
public static partial class Printer
{
    // A first parameter of type WasmCaller receives the caller: the type of the import is (func (param i32 i32) (result i32))
    [WasmImport("env", "print")]
    public static int Print(WasmCaller caller, int address, int length)
    {
        var memory = caller.GetMemory("memory")!;
        Printed.Add(System.Text.Encoding.UTF8.GetString(memory.GetSpan().Slice(address, length)));
        return length;
    }

    public static IList<string> Printed { get; } = new List<string>();
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmSourceGeneratorTests.cs;region=WasmEngine_TypedImportsCaller;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "env" "print" (func $print (param i32 i32) (result i32)))
      (memory (export "memory") 1)
      (data (i32.const 0) "Hello")
      (func (export "run") (result i32)
        (call $print (i32.const 0) (i32.const 5))))
    """));

using var store = runtime.CreateStore();
var instance = store.Instantiate(module, Printer.DefineWasmImports(store));

Assert.Equal(5, instance.GetFunction("run")!.Call()[0].I32);
Assert.Equal(["Hello"], Printer.Printed);
```
<!-- endref -->

A `WasmCaller` at another position is reported (MFWASM003).

The strings, the structs and the data in memory are not converted: their layout depends on the toolchain that compiled
the module. Read and write them in the memory of the instance (see [Memories, tables and globals](04-MemoriesTablesAndGlobals.md)).
