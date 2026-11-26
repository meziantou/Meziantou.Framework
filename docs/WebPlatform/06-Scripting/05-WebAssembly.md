---
title: WebAssembly
description: Run the WebAssembly modules of the pages with the runtime of Meziantou.Framework.WebPlatform.Wasm.
---

The `WebAssembly` API of the scripts is available when the browsing context has a WebAssembly runtime. The
`Meziantou.Framework.WebPlatform.Wasm` package provides one,
[WasmWebAssemblyRuntime](cref:Meziantou.Framework.WebPlatform.Wasm.WasmWebAssemblyRuntime), based on the
[WebAssembly engine](../../WasmEngine/01-Introduction.md) of `Meziantou.Framework.WasmEngine`, written in .NET (no
native library):

<InstallNuGetPackage Id="Meziantou.Framework.WebPlatform.Wasm" />

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebAssemblyTests.cs;region=WebPlatformWasm_RunModules;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmWebAssemblyRuntime(); // can be shared by the browsing contexts
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
- The legacy exception handling instructions (`try`, `catch`, `catch_all`, `rethrow`, `delegate`), which Emscripten
  still emits by default, are supported.
- The JS String Builtins are supported: the `builtins: ["js-string"]` compile option provides the functions of the
  `wasm:js-string` module (`length`, `concat`, `charCodeAt`, `fromCharCodeArray`...), and `importedStringConstants`
  turns the globals imported from a module into string constants (the name of the import is the string).
- The type reflection is supported: `type()` of the `Memory`, `Table`, `Global`, `Tag` and exported functions describes
  their type (`{ minimum, maximum, shared }`, `{ element, minimum, maximum }`, `{ mutable, value }`, `{ parameters }`,
  `{ parameters, results }`), the descriptors accept `minimum` instead of `initial`, and
  `new WebAssembly.Function({ parameters, results }, fn)` creates a WebAssembly function from a JavaScript function: it
  can be stored in a table. The exported functions are `WebAssembly.Function` objects.
- `memory.toResizableBuffer()` gives a resizable `ArrayBuffer` of the bytes of a memory: `resize()` grows the memory by
  whole pages, and the buffer follows the size of the memory.
- The running code is interrupted by the limits of the script engine, such as [Timeout](cref:Meziantou.Framework.WebPlatform.JavaScript.JavaScriptScriptEngineOptions.Timeout).

The constructor of [WasmWebAssemblyRuntime](cref:Meziantou.Framework.WebPlatform.Wasm.WasmWebAssemblyRuntime) takes the
[WasmRuntimeOptions](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions) of the engine, or a
[WasmRuntime](cref:Meziantou.Framework.WasmEngine.WasmRuntime) that you share with other code (the runtime does not
dispose it). The options set the size of the stack of the WebAssembly code
([MaxStackSize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxStackSize), 512 KiB by default: a deeper
recursion throws a `RangeError`), the largest size of a memory
([MaxMemorySize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxMemorySize): the memories are .NET arrays,
shared with the `ArrayBuffer` objects of the scripts), and how often the running code checks whether it must stop
([InterruptionCheckInterval](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.InterruptionCheckInterval), 10 ms by
default). A table has at most 10,000,000 elements, the limit of the JavaScript API of browsers: `table.grow` returns -1
beyond it. The runtime emits the [activities and the metrics](../../WasmEngine/08-Telemetry.md) of the engine.

## Module cache

The runtime keeps the modules it compiled, by the content of their binary: the pages that share the runtime and load
the same module compile it once. The functions that the instances of a page compiled to .NET code (the ones that run
often) also run compiled in the next pages, so a large module, such as the runtime of .NET of a Blazor WebAssembly
application, starts much faster from the second page.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebAssemblyTests.cs;region=WebPlatformWasm_ModuleCache;format=md-fenced-code-block;dedent=true -->
```csharp
// Share the runtime between the pages: it keeps the modules it compiled (16 MiB of binaries by default), so the next
// pages that load a large module, such as the runtime of .NET, do not compile it again
using var runtime = new WasmWebAssemblyRuntime { ModuleCacheCapacity = 64 * 1024 * 1024 };
using var engine = new JavaScriptScriptEngine();

foreach (var (a, b) in new[] { (2, 3), (4, 5) })
{
    using var context = new BrowsingContext(new BrowsingContextOptions
    {
        ScriptEngine = engine,
        WebAssemblyRuntime = runtime,
    });

    var document = await context.NavigateAsync("https://example.com/", _ => $$"""
        <output></output>
        <script>
          // (module (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add))
          const bytes = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 7, 1, 96, 2, 127, 127, 1, 127, 3, 2, 1, 0,
            7, 7, 1, 3, 97, 100, 100, 0, 0, 10, 9, 1, 7, 0, 32, 0, 32, 1, 106, 11]);
          WebAssembly.instantiate(bytes).then(({ instance }) => {
            document.querySelector('output').textContent = instance.exports.add({{a}}, {{b}});
          });
        </script>
        """);
    await context.EventLoop.RunUntilIdleAsync();

    Assert.Equal((a + b).ToString(System.Globalization.CultureInfo.InvariantCulture), document.QuerySelector("output")!.TextContent);
}
```
<!-- endref -->

- [ModuleCacheCapacity](cref:Meziantou.Framework.WebPlatform.Wasm.WasmWebAssemblyRuntime.ModuleCacheCapacity) is the
  maximum total size of the binaries of the kept modules: 16 MiB by default. The least recently used modules are removed
  first, a module larger than the capacity is not kept, and 0 disables the cache.
- The modules smaller than 1 KiB are not kept: they compile in a fraction of a millisecond, and a compiled module uses
  about 100 times the size of a small binary.
- A compiled module uses much more memory than its binary: the runtime of .NET (a 3 MB binary) uses about 170 MB once a
  page ran it.
- Each compilation still gives a new `WebAssembly.Module` object to the scripts, and a binary that is not valid is not
  kept: it fails again at the next compilation.
- Disposing the runtime releases the modules.

## Module scripts

A module script can import a WebAssembly module, as a JavaScript module ([ESM integration](https://github.com/WebAssembly/esm-integration)):
a response whose MIME type is `application/wasm` is a WebAssembly module.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebAssemblyTests.cs;region=WebPlatformWasm_ModuleScripts;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmWebAssemblyRuntime();
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    WebAssemblyRuntime = runtime,
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <output></output>
    <script type="module">
      // A response whose MIME type is application/wasm (a data: URL here) is a WebAssembly module:
      // (module (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add))
      import { add } from "data:application/wasm;base64,AGFzbQEAAAABBwFgAn9/AX8DAgEABwcBA2FkZAAACgkBBwAgACABags=";

      // A source phase import gives the compiled WebAssembly.Module, without instantiating it
      import source addModule from "data:application/wasm;base64,AGFzbQEAAAABBwFgAn9/AX8DAgEABwcBA2FkZAAACgkBBwAgACABags=";

      const { exports } = new WebAssembly.Instance(addModule);
      document.querySelector('output').textContent = add(2, 3) + ' ' + exports.add(4, 5);
    </script>
    """);
await context.EventLoop.RunUntilIdleAsync();

Assert.Equal("5 9", document.QuerySelector("output")!.TextContent);
```
<!-- endref -->

- `<script type="module" src="app.wasm">`, `import`, `import()`, `<link rel="modulepreload">` and the script of a
  module worker (`new Worker("app.wasm", { type: "module" })`) load WebAssembly modules.
- The imports of the module are the exports of other modules: `(import "./lib.js" "log" (func))` imports the `log`
  export of `./lib.js`, and an import of a WebAssembly module gets the export of its instance. The names are checked when
  the module graph is linked: an import that the module does not export is a `SyntaxError`, and no module of the graph
  runs. The modules it imports run before it; a module that is not evaluated yet (a cycle) gives a `LinkError`, as does
  a value whose type does not match the import.
- Its exports are the bindings of the module: the functions, memories and tables are the objects of the exports of the
  instance, the globals are their values. The binding of a mutable global follows its value; the one of a `v128` global
  throws a `ReferenceError`. `WebAssembly.namespaceInstance(ns)` returns the `WebAssembly.Instance`.
- `import source mod from "./app.wasm"` and `import.source()` give the compiled `WebAssembly.Module` without instantiating
  it.
- The modules are compiled with the JS String Builtins (`wasm:js-string`) and the imported string constants of
  `wasm:js/string-constants`. A module that does not compile is a `CompileError`, a module whose imports or exports use
  the reserved names (`wasm:`, `wasm-js:`) a `LinkError`, reported as the error of the script.
- A WebAssembly module is a script: the `script-src` directive of the Content Security Policy applies, not
  `'wasm-unsafe-eval'`.

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

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebAssemblyTests.cs;region=WebPlatformWasm_SharedMemory;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmWebAssemblyRuntime();
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
[MaxMemorySize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxMemorySize) and 2 GiB minus one page (32,767 pages). The memory cannot grow beyond: give the shared
memories a maximum no larger than the memory they need.

A `memory.atomic.wait32` or `memory.atomic.wait64` instruction blocks the thread until it is notified or its timeout
elapses: the limits of the script engine do not interrupt it. An `Atomics.waitAsync()` on the buffer of a shared memory
uses a thread until it completes: a process runs at most 1,024 of them at once (beyond, `Atomics.waitAsync()` throws a
`RangeError`), and they stop when their window or worker is closed.
