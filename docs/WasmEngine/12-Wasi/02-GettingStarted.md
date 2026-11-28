---
title: Getting started
description: Run a WASI command, give it arguments, environment variables and standard streams, and read its exit code.
---

A [WasiContext](cref:Meziantou.Framework.WasmEngine.Wasi.WasiContext) is the environment of a program. Define its imports
in the store, instantiate the module with them, and run the program:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiContextTests.cs;region=Wasi_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A command that writes "Hello, WASI!" to its standard output (file descriptor 1) with fd_write
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
      (memory (export "memory") 1)
      (data (i32.const 8) "Hello, WASI!\n")
      (func (export "_start")
        ;; An iovec at 0: the address and the length of the text
        (i32.store (i32.const 0) (i32.const 8))
        (i32.store (i32.const 4) (i32.const 13))
        (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 32)))))
    """));

using var output = new MemoryStream();
using var wasi = new WasiContext(new WasiOptions
{
    Arguments = ["hello.wasm"],
    StandardOutput = output,
});

using var store = runtime.CreateStore();
var instance = store.Instantiate(module, wasi.DefineImports(store));
var exitCode = wasi.Run(instance);

Assert.Equal(0, exitCode);
Assert.Equal("Hello, WASI!\n", Encoding.UTF8.GetString(output.ToArray()));
```
<!-- endref -->

- [DefineImports](cref:Meziantou.Framework.WasmEngine.Wasi.WasiContext.DefineImports) defines the functions of the
  module `wasi_snapshot_preview1` in a [WasmImports](cref:Meziantou.Framework.WasmEngine.WasmImports). Give it the imports
  of your own modules to link them together.
- [Run](cref:Meziantou.Framework.WasmEngine.Wasi.WasiContext.Run) calls the export `_start` of a command, and returns its
  exit code. A library (a reactor) exports `_initialize` instead: call
  [Initialize](cref:Meziantou.Framework.WasmEngine.Wasi.WasiContext.Initialize), then its exports.
- The functions read and write the memory that the module exports as `memory`.
- Use one context per instance, from the thread of its store. Dispose it to close the files the program left open.

## The options

[WasiOptions](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions) gives the program its environment:

- [Arguments](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.Arguments): the command line. By convention, the first
  argument is the name of the program.
- [EnvironmentVariables](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.EnvironmentVariables): the environment
  variables. The program sees none of the variables of your process unless you copy them.
- [StandardInput](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.StandardInput),
  [StandardOutput](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.StandardOutput) and
  [StandardError](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.StandardError): the streams of the file
  descriptors 0, 1 and 2. Without a stream, the input is empty and the output is discarded. Pass
  `Console.OpenStandardOutput()` to show the output of the program in the console. The context does not close the
  streams.
- [PreopenedDirectories](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.PreopenedDirectories): the directories the
  program can access (see [File system](03-FileSystem.md)).
- [TimeProvider](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.TimeProvider): the clocks. The monotonic clock
  counts from the creation of the context.
- [RandomSource](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions.RandomSource): the random bytes, by default from a
  cryptographically secure generator. Give a deterministic source to replay a program.

## The exit code

`proc_exit` ends the program: it throws a [WasiExitException](cref:Meziantou.Framework.WasmEngine.Wasi.WasiExitException),
which the module cannot catch, and which [Run](cref:Meziantou.Framework.WasmEngine.Wasi.WasiContext.Run) converts to the
exit code:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiContextTests.cs;region=Wasi_ExitCode;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "wasi_snapshot_preview1" "proc_exit" (func $proc_exit (param i32)))
      (memory (export "memory") 1)
      (func (export "_start")
        (call $proc_exit (i32.const 3))
        (unreachable)))
    """));

using var wasi = new WasiContext();
using var store = runtime.CreateStore();
var instance = store.Instantiate(module, wasi.DefineImports(store));

// proc_exit ends the program: Run returns its code
Assert.Equal(3, wasi.Run(instance));
```
<!-- endref -->

When you call the exports of the instance yourself, catch the `WasiExitException`. A program that traps throws a
[WasmTrapException](cref:Meziantou.Framework.WasmEngine.WasmTrapException), as any WebAssembly code (see
[Exceptions and traps](../05-ExceptionsAndTraps.md)).

## Limits

The engine limits the stack, the memories and the time of the program (see
[Limits, interruption and compilation](../06-LimitsAndInterruption.md)). A program that sleeps in `poll_oneoff` is not
interrupted while it sleeps.
