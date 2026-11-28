---
title: WASI 0.2 and 0.3
description: Run the components of WASI 0.2 and WASI 0.3, the programs compiled for wasm32-wasip2 and wasm32-wasip3.
---

WASI 0.2 (Preview 2) and WASI 0.3 (Preview 3) are interfaces of the [component model](../13-Components/01-Overview.md):
`wasi:cli`, `wasi:io`, `wasi:clocks`, `wasi:random`, `wasi:filesystem`... The toolchains that target `wasm32-wasip2`
and `wasm32-wasip3` (Rust, the WASI SDK, `cargo component`, `componentize-py`, `jco`) produce components that import
them. [WasiComponentContext](cref:Meziantou.Framework.WasmEngine.Wasi.WasiComponentContext) defines them, with the same
[WasiOptions](cref:Meziantou.Framework.WasmEngine.Wasi.WasiOptions) as WASI Preview 1:

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiComponentContextTests.cs;region=WasiComponent_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A component that exports wasi:cli/run, compiled from its binary (File.ReadAllBytes("app.wasm")) or its text
var component = runtime.CompileComponent(WasmRuntime.WatToWasm(text));

using var output = new MemoryStream();
var wasi = new WasiComponentContext(new WasiOptions
{
    Arguments = ["app.wasm"],
    StandardOutput = output,
});

using var store = runtime.CreateStore();
var instance = store.InstantiateComponent(component, wasi.DefineImports());
var exitCode = WasiComponentContext.Run(instance);

Assert.Equal(0, exitCode);
Assert.Equal("Hello, WASI!\n", Encoding.UTF8.GetString(output.ToArray()));
```
<!-- endref -->

- [DefineImports](cref:Meziantou.Framework.WasmEngine.Wasi.WasiComponentContext.DefineImports) defines the interfaces
  of WASI 0.2.12 and WASI 0.3.2. A component that imports an older version of the same minor version, such as
  `wasi:cli/stdout@0.2.0`, uses them too.
- [Run](cref:Meziantou.Framework.WasmEngine.Wasi.WasiComponentContext.Run) calls the function `run` of the export
  `wasi:cli/run` (WASI 0.3 when the component exports both versions), and returns the exit code: 0 when `run` returns
  `ok`, 1 when it returns `error`, or the code given to `wasi:cli/exit`.
- The preopened directories, the standard streams, the clocks and the random numbers are the ones of the options:
  see [Getting started](02-GettingStarted.md) and [File system](03-FileSystem.md). The file systems and their sandbox
  are the same for the three versions of WASI.

## What is supported

| Interface | WASI 0.2 | WASI 0.3 |
| --- | --- | --- |
| `wasi:cli` | `environment`, `exit`, `stdin`, `stdout`, `stderr`, `terminal-*` (no terminal) | the same, the standard streams being `stream<u8>` |
| `wasi:io` | `error`, `poll`, `streams` | (replaced by the streams and the futures of the component model) |
| `wasi:clocks` | `monotonic-clock`, `wall-clock` | `monotonic-clock` (with the asynchronous `wait-until` and `wait-for`), `system-clock`, `types` |
| `wasi:random` | `random`, `insecure`, `insecure-seed` | the same |
| `wasi:filesystem` | `types`, `preopens` | the same, the functions being asynchronous and the contents streams |
| `wasi:sockets` | (not supported) | `types`, `ip-name-lookup`: see [Network](05-Network.md) |
| `wasi:http` | (not supported) | `types`, `client`, and the calls of `handler`: see [HTTP](06-Http.md) |

In WASI 0.3, the host copies the streams of the standard input, the standard output and the files from and to the .NET
streams in the background, while the component runs.

A component can ask for 64 MiB of random bytes in one call (`get-random-bytes`): a larger request traps.

## What is not supported

- `wasi:sockets` and `wasi:http` of WASI 0.2: a component that imports them cannot be instantiated (a
  [WasmLinkException](cref:Meziantou.Framework.WasmEngine.WasmLinkException) for the missing imports).
- WASI 0.3 on the browser (WebAssembly): its asynchronous functions need threads of the operating system. WASI 0.2 is
  synchronous and works there.

The [conformance](07-Conformance.md) page shows the pass rate of the tests of `wasm32-wasip3`.
