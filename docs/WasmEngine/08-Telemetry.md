---
title: Telemetry
description: Trace the compilations and the instantiations of the modules, and collect the metrics of the engine.
---

The engine creates activities from the `Meziantou.Framework.WasmEngine` activity source, and records metrics on the
`Meziantou.Framework.WasmEngine` meter. Listen to them with OpenTelemetry, an `ActivityListener` or a `MeterListener`.

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_Telemetry;format=md-fenced-code-block;dedent=true -->
```csharp
// Collect the activities of the current test only (ScopedActivityListener is in Meziantou.Framework)
using var listener = new ScopedActivityListener(new ScopedActivityListenerOptions
{
    SourceNames = ["Meziantou.Framework.WasmEngine"],
    SamplingResult = ActivitySamplingResult.AllDataAndRecorded,
});

using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""(module (func (export "f")))"""));
using var store = runtime.CreateStore();
store.Instantiate(module);

Assert.Equal(["wasm.compile", "wasm.instantiate"], listener.Activities.Select(activity => activity.OperationName));
var compile = listener.Activities.First();
Assert.Equal(module.Bytes.Length, compile.GetTagItem("wasm.module.size"));
```
<!-- endref -->

Only the coarse operations are traced: the calls into WebAssembly and the host functions are not, so the telemetry
does not slow them down.

## Activities

| Operation name | Tags |
| --- | --- |
| `wasm.compile` | `wasm.module.size` (the size of the binary in bytes) |
| `wasm.instantiate` | `wasm.module.imports` and `wasm.module.exports` (their number) |

The status of an activity is `Error` when the operation fails, with the message of the exception as its description
and the type of the exception in the `error.type` tag.

## Metrics

| Name | Type | Unit | Tags |
| --- | --- | --- | --- |
| `meziantou.framework.wasm_engine.module.compile.duration` | Histogram | `s` | `error.type` |
| `meziantou.framework.wasm_engine.module.size` | Histogram | `By` | |
| `meziantou.framework.wasm_engine.instance.instantiate.duration` | Histogram | `s` | `error.type` |
| `meziantou.framework.wasm_engine.traps.total` | Counter | `{trap}` | `wasm.trap.kind` (`stack_overflow` or `other`) |
| `meziantou.framework.wasm_engine.interruptions.total` | Counter | `{interruption}` | |
| `meziantou.framework.wasm_engine.memory.usage` | UpDownCounter | `By` | `wasm.memory.shared` |
| `meziantou.framework.wasm_engine.stores.active` | UpDownCounter | `{store}` | |

- The memory usage counts the bytes of the memories, and the capacity of the shared memories. The bytes of a memory
  are removed from it when the memory is collected.
- A store is active until it is disposed or collected.
- Set [MeterFactory](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MeterFactory) to create the meter from an
  `IMeterFactory` (for instance the one of the dependency injection container of ASP.NET Core); by default, the
  runtimes share one meter.
