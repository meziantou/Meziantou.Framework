---
title: Overview
description: Compile, instantiate and call WebAssembly components, and convert their values to and from .NET.
---

A component ([component model](https://github.com/WebAssembly/component-model)) wraps core WebAssembly modules behind
typed interfaces: its functions take and return strings, lists, records, variants, resources and streams instead of
numbers and pointers. The canonical ABI converts these values between the memories of the modules and .NET. Toolchains
such as `cargo component`, `wasm-tools component new`, `jco` and `componentize-py` produce components, and the programs
of WASI 0.2 and 0.3 are components (see [WASI components](../12-Wasi/04-Components.md)).

Compile a component with [CompileComponent](cref:Meziantou.Framework.WasmEngine.WasmRuntime.CompileComponent),
instantiate it in a store with [InstantiateComponent](cref:Meziantou.Framework.WasmEngine.WasmStore.InstantiateComponent),
and call its exported functions:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Tests/WasmComponentTests.cs;region=Component_GettingStarted;format=md-fenced-code-block;dedent=true -->
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

- [WatToWasm](cref:Meziantou.Framework.WasmEngine.WasmRuntime.WatToWasm) converts the text format of a component, as
  well as of a module. [IsComponent](cref:Meziantou.Framework.WasmEngine.WasmRuntime.IsComponent) tells a component
  from a module by its preamble.
- [Imports](cref:Meziantou.Framework.WasmEngine.Components.WasmComponent.Imports) and
  [Exports](cref:Meziantou.Framework.WasmEngine.Components.WasmComponent.Exports) list the names and the kinds of the
  imports and exports of a component.
- [GetFunction](cref:Meziantou.Framework.WasmEngine.Components.WasmComponentInstance.GetFunction) finds an exported
  function, or a function of an exported instance (an interface) as `interface#function`, such as
  `wasi:cli/run@0.2.0#run`. [GetInstance](cref:Meziantou.Framework.WasmEngine.Components.WasmComponentInstance.GetInstance)
  returns an exported instance.

## Validation

[CompileComponent](cref:Meziantou.Framework.WasmEngine.WasmRuntime.CompileComponent) validates the component, and
throws a [WasmCompileException](cref:Meziantou.Framework.WasmEngine.WasmCompileException) when it is not valid. It
checks:

- The indices, and the types of the definitions: the instantiations of the nested components and modules get
  arguments of the types of their imports, and the canonical definitions match the types of their core functions.
- The names of the imports and the exports: kebab case, interface names and their versions (`@0.2.0`,
  `version-suffix`), the `[constructor]`, `[method]` and `[static]` functions of the resources, and their uniqueness
  (`foo-bar` and `FOOBAR` are the same name).
- The resource types: each resource type definition, each instantiation and each import of an instance type creates new
  resource types, which are not interchangeable.
- The canonical options: a function that passes strings or lists needs `memory`, and `realloc` when the strings or the
  lists go into the component.
- The names of the types: the types of the functions of the imports and the exports must be imported or exported (the
  users of the component name them). Export the types that the exported functions use:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Tests/WasmComponentTests.cs;region=Component_Validation;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// The exported function returns a record type that the component does not export: its users cannot name it
var invalid = WasmRuntime.WatToWasm("""
    (component
      (core module $M (func (export "f") (result i32) (i32.const 0)))
      (core instance $m (instantiate $M))
      (type $point (record (field "x" u32)))
      (func $f (result $point) (canon lift (core func $m "f")))
      (export "f" (func $f)))
    """);

var exception = Assert.Throws<WasmCompileException>(() => runtime.CompileComponent(invalid));
Assert.Equal("func not valid to be used as export: `f`", exception.Message);

// Exporting the type gives it a name: the function uses the exported type
var valid = WasmRuntime.WatToWasm("""
    (component
      (core module $M (func (export "f") (result i32) (i32.const 0)))
      (core instance $m (instantiate $M))
      (type $point (record (field "x" u32)))
      (export $point-export "point" (type $point))
      (func $f (result $point-export) (canon lift (core func $m "f")))
      (export "f" (func $f)))
    """);

var component = runtime.CompileComponent(valid);
Assert.Equal(["point", "f"], component.Exports.Select(export => export.Name));
```
<!-- endref -->

The host does not need to define the type imports that are equal to a type of the component (`(type (eq $t))`): they
are the types that the component declares. The values and the start definitions of the component model are not
supported: a component that has them does not compile.

## Values

A [ComponentValue](cref:Meziantou.Framework.WasmEngine.Components.ComponentValue) is a value of the component model.
Create the arguments with its `From*` methods, and read the results with its `As*` methods:

| Type of the component model | Create | Read |
| --- | --- | --- |
| `bool`, `s8` ... `u64`, `f32`, `f64` | `FromBool`, `FromS8` ... `FromU64`, `FromF32`, `FromF64` | `AsBool`, `AsS8` ... `AsU64`, `AsF32`, `AsF64` |
| `char` | `FromChar` (a `Rune`) | `AsChar` |
| `string` | `FromString` | `AsString` |
| `list<T>`, `map<K, V>` | `FromList`, `FromBytes` (a `list<u8>`) | `AsList`, `AsBytes` |
| `record`, `tuple` | `FromRecord`, `FromTuple`: the values of the fields, in the order of the type | `AsRecord` |
| `variant`, `enum` | `FromCase`, `FromEnum`: the name of the case and its payload | `CaseName`, `Payload`, `IsCase` |
| `option<T>` | `Some`, `NoneValue`, `FromOption` | the cases `some` and `none` |
| `result<T, E>` | `Ok`, `Error` | the cases `ok` and `error` |
| `flags` | `FromFlags`: the names of the set flags | `AsFlags` |
| `own<T>`, `borrow<T>` | `FromResource` | `AsResource` (see [Resources](03-Resources.md)) |
| `stream<T>`, `future<T>` | the writers of the host (see [Asynchronous functions and streams](04-Concurrency.md)) | `AsStreamReader`, `AsFutureReader` |

A value that does not match the type of the parameter (a string for a `u32`, an unknown case) throws a
[WasmTypeMismatchException](cref:Meziantou.Framework.WasmEngine.WasmTypeMismatchException).

## Traps

A trap of a component, or of the canonical ABI (an invalid string, a pointer out of the bounds of the memory, an
unknown handle), throws a [WasmTrapException](cref:Meziantou.Framework.WasmEngine.WasmTrapException). After a call
fails, the components of the store cannot be called anymore: their state is unknown. Create a new store to run them
again.

## Pages

- [Host functions](02-HostFunctions.md): give .NET functions and interfaces to the imports of a component.
- [Resources](03-Resources.md): give .NET objects to the components as resources, and release them.
- [Asynchronous functions and streams](04-Concurrency.md): the asynchronous functions of the host, the streams, the
  futures, and the threads of the components.
- [Conformance](05-Conformance.md): the pass rate of the tests of the component model.

## Limitations

- The value definitions and the start definitions (gated features of the component model) are not supported.
- The asynchronous functions and the threads of the components run on threads of the operating system: they are not
  supported on the browser (WebAssembly), where they throw a `PlatformNotSupportedException`. The synchronous functions
  are supported everywhere.
