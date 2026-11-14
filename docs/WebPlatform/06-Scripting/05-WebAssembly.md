---
title: WebAssembly
description: Run the WebAssembly modules of the pages with the Wasmtime runtime.
---

The `WebAssembly` API of the scripts is available when the browsing context has a WebAssembly runtime. The
`Meziantou.Framework.WebPlatform.Wasmtime` package provides one, based on [Wasmtime](https://wasmtime.dev/), which
compiles the modules to native code:

<InstallNuGetPackage Id="Meziantou.Framework.WebPlatform.Wasmtime" />

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebAssemblyTests.cs;region=WebPlatformWasmtime_RunModules;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmtimeRuntime(); // can be shared by the browsing contexts
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    WebAssemblyRuntime = runtime,
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <output></output>
    <script>
      // (module (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add))
      const bytes = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 7, 1, 96, 2, 127, 127, 1, 127, 3, 2, 1, 0,
        7, 7, 1, 3, 97, 100, 100, 0, 0, 10, 9, 1, 7, 0, 32, 0, 32, 1, 106, 11]);
      WebAssembly.instantiate(bytes).then(({ instance }) => {
        document.querySelector('output').textContent = instance.exports.add(2, 3);
      });
    </script>
    """);
await context.EventLoop.RunUntilIdleAsync();

Assert.Equal("5", document.QuerySelector("output")!.TextContent);
```
<!-- endref -->

- The runtime can be shared by the browsing contexts and the script engines; dispose it when you are done.
- `validate()`, `compile()`, `instantiate()`, `compileStreaming()` and `instantiateStreaming()`, the `Module`,
  `Instance`, `Memory`, `Table`, `Global`, `Tag` and `Exception` objects, and the imported JavaScript functions are
  supported.
- The features of WebAssembly 3.0 are enabled (reference types, garbage collection types, exceptions, tail calls, memory64,
  multiple memories, SIMD), and the threads: see [Threads](#threads).
- The JS String Builtins are supported: the `builtins: ["js-string"]` compile option provides the functions of the
  `wasm:js-string` module (`length`, `concat`, `charCodeAt`, `fromCharCodeArray`...), and `importedStringConstants`
  turns the globals imported from a module into string constants (the name of the import is the string).
- `memory.toResizableBuffer()` gives a resizable `ArrayBuffer` of the bytes of a memory: `resize()` grows the memory by
  whole pages, and the buffer follows the size of the memory.
- The running code is interrupted by the limits of the script engine, such as [Timeout](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.Timeout).

[WasmtimeRuntimeOptions](cref:Meziantou.Framework.WebPlatform.Wasmtime.WasmtimeRuntimeOptions) sets the size of the stack of the WebAssembly code ([MaxStackSize](cref:Meziantou.Framework.WebPlatform.Wasmtime.WasmtimeRuntimeOptions.MaxStackSize), 512 KiB by default: a
deeper recursion throws a `RangeError`), the largest size of a memory ([MaxMemorySize](cref:Meziantou.Framework.WebPlatform.Wasmtime.WasmtimeRuntimeOptions.MaxMemorySize): the memories are .NET arrays,
shared with the `ArrayBuffer` objects of the scripts), and how often the running code checks whether it must stop
([InterruptionCheckInterval](cref:Meziantou.Framework.WebPlatform.Wasmtime.WasmtimeRuntimeOptions.InterruptionCheckInterval), 10 ms by default).
A table has at most 10,000,000 elements, the limit of the JavaScript API of browsers: `table.grow` returns -1 beyond it.

## Threads

The modules compiled with the threads, such as the Emscripten builds with `-pthread`, run in the windows and the
workers of a page:

- A shared memory, created by `new WebAssembly.Memory({ initial, maximum, shared: true })` or declared by a module, has
  `SharedArrayBuffer` buffers. The modules import it, and the atomic instructions (`i32.atomic.rmw.add`,
  `memory.atomic.wait32`, `memory.atomic.notify`...) run on it.
- `postMessage()` sends a `WebAssembly.Module` and a shared `WebAssembly.Memory` to the workers of the agent cluster: the
  module is not compiled again, and the copies of the memory share its bytes. When a copy grows the memory, the `buffer`
  of each copy gets the new size. A shared memory can only be sent by a cross-origin isolated page
  (`Cross-Origin-Opener-Policy` and `Cross-Origin-Embedder-Policy` headers).
- `Atomics.wait()`, `Atomics.waitAsync()` and `Atomics.notify()` on the buffers of a shared memory and the
  `memory.atomic.wait32`, `memory.atomic.wait64` and `memory.atomic.notify` instructions use the same waiters: a script
  wakes the WebAssembly code that waits, and the reverse. An `Atomics.waitAsync()` waits on a thread of its own.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebAssemblyTests.cs;region=WebPlatformWasmtime_SharedMemory;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmtimeRuntime();
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    WebAssemblyRuntime = runtime,
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <output></output>
    <script>
      // (module (import "env" "memory" (memory 1 1 shared))
      //   (func (export "add") (param i32 i32) (result i32) (i32.atomic.rmw.add (local.get 0) (local.get 1))))
      const bytes = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 7, 1, 96, 2, 127, 127, 1, 127, 2, 16, 1, 3, 101,
        110, 118, 6, 109, 101, 109, 111, 114, 121, 2, 3, 1, 1, 3, 2, 1, 0, 7, 7, 1, 3, 97, 100, 100, 0, 0, 10, 12, 1,
        10, 0, 32, 0, 32, 1, 254, 30, 2, 0, 11]);
      const memory = new WebAssembly.Memory({ initial: 1, maximum: 1, shared: true });
      WebAssembly.instantiate(bytes, { env: { memory } }).then(({ instance }) => {
        const counter = new Int32Array(memory.buffer); // a SharedArrayBuffer: postMessage() shares it with the workers
        Atomics.store(counter, 0, 40);
        instance.exports.add(0, 2);
        document.querySelector('output').textContent = Atomics.load(counter, 0);
      });
    </script>
    """);
await context.EventLoop.RunUntilIdleAsync();

Assert.Equal("42", document.QuerySelector("output")!.TextContent);
```
<!-- endref -->

The bytes of a shared memory never move: they are allocated for the maximum size of the memory when it is created, up to
[MaxMemorySize](cref:Meziantou.Framework.WebPlatform.Wasmtime.WasmtimeRuntimeOptions.MaxMemorySize) and 2 GiB minus one page (32,767 pages). The memory cannot grow beyond: give the shared
memories a maximum no larger than the memory they need.

A `memory.atomic.wait32` or `memory.atomic.wait64` instruction blocks the thread until it is notified or its timeout
elapses: the limits of the script engine do not interrupt it. An `Atomics.waitAsync()` on the buffer of a shared memory
uses a thread until it completes: a process runs at most 1,024 of them at once (beyond, `Atomics.waitAsync()` throws a
`RangeError`), and they stop when their window or worker is closed. The threads of WebAssembly are a
[tier 2 feature](https://docs.wasmtime.dev/stability-tiers.html) of Wasmtime.

Wasmtime is a native library: it comes with the `Wasmtime` package, a dependency of
`Meziantou.Framework.WebPlatform.Wasmtime`.
