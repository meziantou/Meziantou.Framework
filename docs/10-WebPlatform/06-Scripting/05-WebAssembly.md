---
title: WebAssembly
description: Run the WebAssembly modules of the pages with the Wasmtime runtime.
---

The `WebAssembly` API of the scripts is available when the browsing context has a WebAssembly runtime. The
`Meziantou.Framework.WebPlatform.Wasmtime` package provides one, based on [Wasmtime](https://wasmtime.dev/), which
compiles the modules to native code:

```bash
dotnet add package Meziantou.Framework.WebPlatform.Wasmtime
```

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
  multiple memories, SIMD), except the threads.
- The running code is interrupted by the limits of the script engine, such as `Timeout`.

`WasmtimeRuntimeOptions` sets the size of the stack of the WebAssembly code (`MaxStackSize`, 512 KiB by default: a
deeper recursion throws a `RangeError`), the largest size of a memory (`MaxMemorySize`: the memories are .NET arrays,
shared with the `ArrayBuffer` objects of the scripts), and how often the running code checks whether it must stop
(`InterruptionCheckInterval`, 10 ms by default).

Wasmtime is a native library: it comes with the `Wasmtime` package, a dependency of
`Meziantou.Framework.WebPlatform.Wasmtime`.
