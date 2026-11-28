---
title: Introduction
description: A WebAssembly engine written in .NET: compile and run modules with host functions, memories, exceptions, limits and telemetry.
---

`Meziantou.Framework.WasmEngine` runs WebAssembly modules inside a .NET application. It is written in .NET, without
native library: it decodes and validates the modules, interprets their code, and compiles the functions that run often
to .NET code (IL) when the platform supports dynamic code. The interpreter runs everywhere .NET runs, including Native
AOT applications.

<InstallNuGetPackage Id="Meziantou.Framework.WasmEngine" />

## What you can do with it

- **Compile, validate and instantiate modules**, from their binary or text format. See
  [Getting started](02-GettingStarted.md).
- **Give .NET functions to the modules** as imports, by position or by module and name. See
  [Host functions and imports](03-HostFunctions.md).
- **Read and write the memories, tables and globals** of the instances. See
  [Memories, tables and globals](04-MemoriesTablesAndGlobals.md).
- **Throw and catch WebAssembly exceptions** from .NET, and handle the traps. See
  [Exceptions and traps](05-ExceptionsAndTraps.md).
- **Interrupt long-running code**, limit the stack, the memories and the tables, and choose when the functions are
  compiled to .NET code. See [Limits, interruption and compilation](06-LimitsAndInterruption.md).
- **Share memories between threads**, with the atomic wait and notify instructions. See
  [Shared memories](07-SharedMemories.md).
- **Trace the compilations and the instantiations**, and collect metrics. See [Telemetry](08-Telemetry.md).
- **Call the exports with strongly typed methods**, and write the host functions as .NET methods, with the source
  generator of the package. See [Typed exports and imports](10-SourceGenerator.md).
- **Run WASI programs**, the programs compiled for `wasm32-wasip1` and the components of WASI 0.2 and 0.3, with their
  arguments, standard streams and a sandboxed file system, with the `Meziantou.Framework.WasmEngine.Wasi` package. See
  [WASI](12-Wasi/01-Overview.md).
- **Run components** of the component model, exchange strings, lists, records, variants, resources and streams with
  them, and give them synchronous and asynchronous host functions. See [Component model](13-Components/01-Overview.md).

The engine passes the official WebAssembly test suite: see [Conformance](09-Conformance.md).

## Language support

The features of WebAssembly 3.0 are enabled: the reference types, the typed function references, the GC types (structs,
arrays, `i31ref`), the exceptions, the tail calls, memory64, the multiple memories, the SIMD and relaxed SIMD
instructions, and the wide arithmetic. The threads (the shared memories and the atomic instructions) are enabled too.
The legacy exception handling instructions (`try`, `catch`, `catch_all`, `rethrow`, `delegate`), which toolchains such
as Emscripten still emit, are supported too.

## Limitations

- WASI is provided by the `Meziantou.Framework.WasmEngine.Wasi` package (see [WASI](12-Wasi/01-Overview.md)): the
  sockets and HTTP are those of WASI 0.3 only.
- The asynchronous functions and threads of the components are not supported on the browser, and their value and start
  definitions are not supported: see [Component model](13-Components/01-Overview.md).
- The memories are .NET arrays: a memory cannot be larger than the maximum length of a .NET array (about 2 GiB).
- The interpreted code is much slower than native code. The compiled functions are faster, but they are compiled only
  where dynamic code is supported (not in Native AOT applications).

To run the WebAssembly modules of the scripts of `Meziantou.Framework.WebPlatform` (the `WebAssembly` namespace of
JavaScript), use `Meziantou.Framework.WebPlatform.Wasm`, which is based on this engine: see
[WebAssembly](../WebPlatform/06-Scripting/05-WebAssembly.md).
